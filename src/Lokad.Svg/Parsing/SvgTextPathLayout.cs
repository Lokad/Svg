using Lokad.Svg.Fonts;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Parsing;

/// <summary>Retains a bounded glyph run at its original paint position until local definitions are available.</summary>
/// <remarks>The document owns the run and identifier; the run borrows the prepared font. Parser-thread confined.</remarks>
internal readonly record struct SvgPendingTextPath(SvgTextRun Run, SvgTextPathReference Reference, SvgColor Color, int CommandIndex, RenderBudget.BudgetReservation Owner);

/// <summary>Describes a measured open baseline and its authored offset calibration.</summary>
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
        using SvgMeasuredTextPath path = ReadMeasured(source, maximumCommands, budget, work, token);
        return path.Baseline;
    }

    internal static SvgMeasuredTextPath ReadMeasured(SvgDefinitionLeafSource source, int maximumCommands, RenderBudget budget,
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
        return SvgMeasuredTextPath.Create(commands.AsSpan(), map, pathLength, budget, work, token);
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
            using SvgMeasuredTextPath measured = ReadMeasured(source, limits.MaximumPathCommands, state.Budget, work, token);
            SvgTextPathBaseline baseline = measured.Baseline;
            double offset = pending.Reference.IsPercentage ? baseline.Length * (pending.Reference.StartOffset / 100) : pending.Reference.StartOffset * baseline.OffsetScale;
            if (!double.IsFinite(offset)) throw new SvgException(SvgErrorCode.LimitExceeded, "The resolved text-path offset is not finite.");
            int tail = state.Scene.CommandCount;
            foreach (SvgPositionedGlyph glyph in pending.Run.Glyphs)
            {
                work.Consume(token);
                double distance = offset + glyph.X;
                double midpoint = distance + glyph.Advance / 2;
                if (!measured.IsHorizontal)
                    // Native SVG adds the device character offset and inline
                    // half-width before scalar division and the path offset.
                    midpoint = ((float)(glyph.X * state.OutputTextScale) + (float)(glyph.Advance * state.OutputTextScale) / 2) / (float)state.OutputTextScale + (float)offset;
                if (!double.IsFinite(distance) || !double.IsFinite(midpoint)) throw new SvgException(SvgErrorCode.LimitExceeded, "Text-path placement exceeds the supported range.");
                if (baseline.Length == 0 || midpoint < 0 || midpoint > baseline.Length) continue;
                SvgPositionedGlyph placed = measured.IsHorizontal
                    ? glyph with { X = baseline.X + distance, Y = baseline.Y + glyph.Y }
                    : glyph with { X = 0, Y = 0 };
                SvgAffineTransform glyphMap = measured.IsHorizontal ? SvgAffineTransform.Identity
                    : measured.GlyphMap(midpoint, glyph.Advance / 2, glyph.Y, work, token);
                // A zero tangent angle uses the native horizontal strike, even on a curved baseline.
                if (!measured.IsHorizontal && glyphMap.A == 1 && glyphMap.B == 0 && glyphMap.C == 0 && glyphMap.D == 1)
                {
                    placed = placed with { X = glyphMap.E, Y = glyphMap.F };
                    glyphMap = SvgAffineTransform.Identity;
                }
                else if (!measured.IsHorizontal && (OperatingSystem.IsWindows() || OperatingSystem.IsLinux()))
                {
                    SvgGlyphDeviceMetrics metrics = new(state.OutputTextScale, OperatingSystem.IsWindows() ? SvgGlyphMetricTarget.Windows : SvgGlyphMetricTarget.Linux, true);
                    glyphMap = metrics.PlaceTextPathPaint(glyphMap, glyph.Advance / 2, glyph.Y, pending.Run.Style.FontSize, pending.Run.Font.Metrics);
                }
                using SvgGlyphContourStorage contours = SvgGlyphOutline.GetContourStorage(pending.Run.Font, placed, pending.Run.Style.FontSize, glyphMap, state.Budget, work, token);
                SvgGlyphGeometry geometry = SvgGlyphGeometry.Create(contours, pending.Color, pending.Run.Font, placed, pending.Run.Style.FontSize, glyphMap, state.Budget);
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
