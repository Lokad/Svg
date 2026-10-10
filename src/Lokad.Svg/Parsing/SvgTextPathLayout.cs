using Lokad.Svg.Fonts;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Parsing;

/// <summary>Retains a bounded glyph run at its original paint position until local definitions are available.</summary>
/// <remarks>The document owns the run and identifier; the run borrows the prepared font. Parser-thread confined.</remarks>
internal readonly record struct SvgPendingTextPath(SvgTextRun Run, SvgTextPathReference Reference, SvgColor Color, int CommandIndex, RenderBudget.BudgetReservation Owner);

/// <summary>Describes the positive horizontal baseline admitted before general tangent glyph maps.</summary>
internal readonly record struct SvgTextPathBaseline(double X, double Y, double Length)
{
    /// <summary>Scales authored numeric start offsets; percentages and glyph advances retain their separate coordinates.</summary>
    internal double OffsetScale { get; init; } = 1;
}

internal static class SvgTextPathLayout
{
    /// <summary>Reuses authored path commands and composed coordinates without flattening or fill closure.</summary>
    internal static SvgTextPathBaseline ReadBaseline(SvgDefinitionLeafSource source, int maximumCommands, RenderBudget budget,
        ParserWorkCounter work, CancellationToken token)
    {
        work.Consume(token);
        if (source.LocalName != "path") throw new SvgException(SvgErrorCode.UnresolvedResource, "The text-path reference must identify a path.");
        double? pathLength = SvgStrokeStyleSyntax.ParsePathLength(source.GetAttribute("pathLength"));
        if (pathLength == 0) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Zero calibrated text-path lengths are not implemented.");
        SvgAffineTransform map = source.InheritedTransform.ComposeChecked(SvgGeometrySyntax.ParseTransform(source.GetAttribute("transform"), work, token));
        string data = source.GetAttribute("d") ?? string.Empty;
        using BudgetedParserList<SvgAuthoredPathCommand> commands = new(budget, SvgMemoryCategory.Parser, work, token);
        (int prefixLength, _) = SvgPathDataParser.ReadRenderablePrefix(data, maximumCommands, work, token);
        using RenderBudget.BudgetReservation? prefixOwner = prefixLength == data.Length ? null : budget.Reserve(32L + 2L * prefixLength, SvgMemoryCategory.Parser);
        SvgPathDataParser.ParseCommands(prefixLength == data.Length ? data : data[..prefixLength], maximumCommands, work, token, commands);
        SvgDevicePoint start = default;
        SvgDevicePoint previous = default;
        bool hasStart = false;
        foreach (SvgAuthoredPathCommand command in commands.AsSpan())
        {
            work.Consume(token);
            if (command.Verb == SvgAuthoredPathVerb.End) continue;
            if (command.Verb == SvgAuthoredPathVerb.Move && !hasStart)
            {
                start = previous = map.Apply(command.First.X, command.First.Y);
                hasStart = true;
                continue;
            }
            if (command.Verb != SvgAuthoredPathVerb.Line) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only one open positive horizontal text-path contour is implemented.");
            SvgDevicePoint next = map.Apply(command.First.X, command.First.Y);
            if (next.Y != previous.Y || next.X < previous.X) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Rotated, curved and reversing text-path tangents are not implemented.");
            previous = next;
        }
        double length = hasStart ? previous.X - start.X : 0;
        if (!double.IsFinite(length)) throw new SvgException(SvgErrorCode.LimitExceeded, "The text-path length exceeds the supported range.");
        double offsetScale = pathLength is double calibrated ? length / calibrated : 1;
        if (!double.IsFinite(offsetScale)) throw new SvgException(SvgErrorCode.LimitExceeded, "The text-path calibration exceeds the supported range.");
        return new(start.X, start.Y, length) { OffsetScale = offsetScale };
    }

    /// <summary>Resolves local paths, places glyphs and moves their ordinary scene commands into reserved paint slots.</summary>
    /// <remarks>Existing scene/source owners provide raster replay. Every path/glyph/command step charges work;
    /// document teardown releases queued runs and partially adopted glyphs on denial or cancellation.</remarks>
    internal static void Resolve(SvgDocumentState state, SvgRenderLimits limits, ParserWorkCounter work, CancellationToken token)
    {
        for (int index = state.PendingTextPaths.Count - 1; index >= 0; index--)
        {
            work.Consume(token);
            SvgPendingTextPath pending = state.PendingTextPaths[index];
            if (!state.Definitions.TryGetValue(pending.Reference.PathId, out SvgDefinition definition) || definition.LeafSource is not {} source)
                throw new SvgException(SvgErrorCode.UnresolvedResource, "The local text-path reference was not found.");
            SvgTextPathBaseline baseline = ReadBaseline(source, limits.MaximumPathCommands, state.Budget, work, token);
            double offset = pending.Reference.IsPercentage ? baseline.Length * (pending.Reference.StartOffset / 100) : pending.Reference.StartOffset * baseline.OffsetScale;
            if (!double.IsFinite(offset)) throw new SvgException(SvgErrorCode.LimitExceeded, "The resolved text-path offset is not finite.");
            int tail = state.Scene.CommandCount;
            foreach (SvgPositionedGlyph glyph in pending.Run.Glyphs)
            {
                work.Consume(token);
                double distance = offset + glyph.X;
                double midpoint = distance + glyph.Advance / 2;
                if (!double.IsFinite(distance) || !double.IsFinite(midpoint)) throw new SvgException(SvgErrorCode.LimitExceeded, "Text-path placement exceeds the supported range.");
                if (baseline.Length == 0 || midpoint < 0 || midpoint > baseline.Length) continue;
                SvgPositionedGlyph placed = glyph with { X = baseline.X + distance, Y = baseline.Y + glyph.Y };
                using SvgGlyphContourStorage contours = SvgGlyphOutline.GetContourStorage(pending.Run.Font, placed, pending.Run.Style.FontSize, state.Budget, work, token);
                SvgGlyphGeometry geometry = SvgGlyphGeometry.Create(contours, pending.Color, pending.Run.Font, placed, pending.Run.Style.FontSize, state.Budget);
                try { state.Scene.AddGeometry(SvgGeometry.FromGlyphContours(geometry)); }
                catch { geometry.Dispose(); throw; }
            }
            int inserted = state.Scene.ResolvePaintPlaceholder(pending.CommandIndex, tail);
            SvgReferenceResolver.ShiftPendingCommandRanges(state, pending.CommandIndex + 1, inserted, true, work, token);
            for (int useIndex = 0; inserted != 0 && useIndex < state.PendingUses.Count; useIndex++)
            {
                work.Consume(token);
                PendingUse use = state.PendingUses[useIndex];
                if (use.Slot.CommandIndex > pending.CommandIndex) state.PendingUses[useIndex] = use with { Slot = use.Slot with { CommandIndex = use.Slot.CommandIndex + inserted } };
            }
            pending.Run.Dispose();
            pending.Owner.Dispose();
        }
    }
}
