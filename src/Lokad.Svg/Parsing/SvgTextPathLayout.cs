using Lokad.Svg.Fonts;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Parsing;

/// <summary>Retains a shaped direct/path run or owned continuation layout at its original paint slot until local paint/path definitions are available.</summary>
/// <remarks>Exactly one input is present. The document owns both alternatives and their aggregate allowance;
/// fonts, viewport clips and the originating builder remain borrowed. Resolution and cleanup are parser-thread confined.</remarks>
internal readonly record struct SvgPendingTextRun(SvgTextRun? Run, SvgTextPathReference? Reference, SvgAffineTransform Placement, SvgColor Color, int CommandIndex, RenderBudget.BudgetReservation Owner, SvgSceneBuilder Scene)
{
    internal SvgRecordedTransform? RecordingTransform { get; init; }
    internal SvgPendingTextLayout? Continuation { get; init; }
    /// <summary>Retains the enclosing filter-source recording context until deferred emission.</summary>
    internal int FilterGroupDepth { get; init; }
    /// <summary>Borrows the originating viewport clip chain until deferred glyph command emission.</summary>
    internal SvgLeafMaskBinding? ViewportClip { get; init; }
}

/// <summary>Describes a measured baseline and its authored offset calibration.</summary>
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
        // A referenced path's own transform supplements the text coordinate system;
        // definition ancestors do not move that local baseline (SVG 2 textPath).
        SvgAffineTransform map = SvgGeometrySyntax.ParseTransform(source.GetAttribute("transform"), work, token);
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
        => Resolve(state, default, limits, work, token);

    /// <summary>Resolves only the appended source-local suffix; earlier document runs remain borrowed and untouched.</summary>
    internal static void Resolve(SvgDocumentState state, SvgPendingCommandStart first, SvgRenderLimits limits, ParserWorkCounter work, CancellationToken token)
    {
        if (state.PendingTextRuns.Count == first.Text) return;
        using SvgPaintCommandBatch commands = new(state, state.Scene, first, state.PendingCommandEnd, work, token);
        for (int index = state.PendingTextRuns.Count - 1; index >= first.Text; index--)
        {
            work.Consume(token);
            SvgPendingTextRun pending = state.PendingTextRuns[index];
            if (!ReferenceEquals(pending.Scene, state.Scene)) continue;
            SvgPaintCommandEmission emission = commands.Begin(pending.CommandIndex);
            Emit(state, index, limits, work, token);
            commands.Retain(emission);
        }
        commands.Complete();
    }

    /// <summary>Emits one queued run into its recorded scene; the caller replaces its paint slot in source order.</summary>
    /// <remarks>The document retains ownership on failure. Success releases the run and metadata allowance;
    /// teardown is idempotent. Temporary scene and filter bindings are restored even on work denial or cancellation.</remarks>
    internal static void Emit(SvgDocumentState state, int index, SvgRenderLimits limits, ParserWorkCounter work, CancellationToken token)
    {
        SvgPendingTextRun pending = state.PendingTextRuns[index];
        SvgSceneBuilder? previous = state.DefinitionReplayScene;
        int previousFilterDepth = state.FilterGroupDepth;
        if (!ReferenceEquals(pending.Scene, state.Scene)) state.DefinitionReplayScene = pending.Scene;
        state.FilterGroupDepth = Math.Max(previousFilterDepth, pending.FilterGroupDepth);
        try
        {
            SvgTextRun run;
            if (pending.Reference is not {} reference)
            {
                run = pending.Run ?? throw new InvalidOperationException("A queued direct paint requires a shaped run.");
                SvgTextGlyphPainter.EmitDirect(run, pending.Placement, pending.ViewportClip, pending.Color, state, limits.MaximumPathCommands, state.Budget, work, pending.RecordingTransform, token);
            }
            else
            {
                if (!state.Definitions.TryGetValue(reference.PathId, out SvgDefinition definition) || definition.LeafSource is not {} source)
                    throw new SvgException(SvgErrorCode.UnresolvedResource, "The local text-path reference was not found.");
                using SvgMeasuredTextPath measured = ReadMeasured(source, limits.MaximumPathCommands, state.Budget, work, token);
                SvgTextPathBaseline baseline = measured.Baseline;
                double offset = reference.IsPercentage ? baseline.Length * (reference.StartOffset / 100) : reference.StartOffset * baseline.OffsetScale;
                if (!double.IsFinite(offset)) throw new SvgException(SvgErrorCode.LimitExceeded, "The resolved text-path offset is not finite.");
                if (pending.Continuation is {} continuation)
                {
                    SvgDevicePoint origin = measured.EndPoint;
                    if (measured.IsClosed && baseline.Length > 0)
                    {
                        double wrapped = (offset % baseline.Length + baseline.Length) % baseline.Length;
                        origin = measured.GlyphMap(wrapped, 0, 0, work, token).Apply(0, 0);
                    }
                    SvgExtractedTextRun raw = continuation.Source;
                    SvgTextStyle style = continuation.Style with { X = origin.X, Y = origin.Y };
                    run = SvgTextRunBuilder.Create(style, raw.Layout.Value, raw.Layout.LetterSpacingSpans, raw.Layout.PositionAdjustments,
                        raw.PaintSpans.Items, raw.FontSpans.Items, raw.NodeBoundaries.Items, continuation.Paint, continuation.Metrics, continuation.Snapshot, state.Budget, work, token);
                    state.PendingTextRuns[index] = pending with { Run = run };
                    continuation.Dispose();
                    SvgTextGlyphPainter.EmitDirect(run, pending.Placement, pending.ViewportClip, pending.Color, state, limits.MaximumPathCommands, state.Budget, work, pending.RecordingTransform, token);
                }
                else
                {
                    run = pending.Run ?? throw new InvalidOperationException("A queued path requires a shaped run or continuation layout.");
                    double pathScale = run.DeviceMetrics?.Scale ?? state.OutputTextScale;
                    using BudgetedParserList<SvgPlacedTextGlyph> placements = new(state.Budget, SvgMemoryCategory.Geometry, work, token);
                    int glyphIndex = 0;
                    foreach (SvgPositionedGlyph glyph in run.Glyphs)
                    {
                        work.Consume(token);
                        SvgGlyphFontStyle glyphFont = run.GetGlyphFont(glyphIndex);
                        double rotation = run.GetGlyphRotation(glyphIndex);
                        SvgTextGlyphPaint paint = run.GetGlyphPaint(glyphIndex++, pending.Color);
                        // Unpainted path glyphs still contribute to source paint bounds.
                        double distance = offset + glyph.X;
                        // Native SVG adds the device character offset and inline
                        // half-width before scalar division and the path offset.
                        double midpoint = ((float)(glyph.X * pathScale) + (float)(glyph.Advance * pathScale) / 2) / (float)pathScale + (float)offset;
                        if (!double.IsFinite(distance) || !double.IsFinite(midpoint)) throw new SvgException(SvgErrorCode.LimitExceeded, "Text-path placement exceeds the supported range.");
                        if (baseline.Length == 0 || midpoint < 0 || midpoint > baseline.Length) { placements.Add(default); continue; }
                        bool horizontalPlacement = measured.IsHorizontal && rotation == 0;
                        SvgPositionedGlyph placed = horizontalPlacement
                            ? glyph with { X = baseline.X + distance, Y = baseline.Y + glyph.Y }
                            : glyph with { X = 0, Y = 0 };
                        SvgAffineTransform glyphMap = horizontalPlacement ? SvgAffineTransform.Identity
                            : measured.GlyphMap(midpoint, glyph.Advance / 2, glyph.Y, rotation, work, token);
                        // SVG layout keeps signed inline advances, but the rotated
                        // fragment rectangle clamps its painted width to zero.
                        double paintHalfAdvance = glyph.Advance / 2;
                        if (!horizontalPlacement && !glyphMap.IsTranslation && paintHalfAdvance < 0)
                        {
                            glyphMap = glyphMap.ComposeChecked(SvgAffineTransform.FromTranslation(paintHalfAdvance, 0));
                            paintHalfAdvance = 0;
                        }
                        SvgAffineTransform sourceMap = glyphMap.ComposeChecked(SvgAffineTransform.FromTranslation(placed.X, placed.Y));
                        if (horizontalPlacement)
                        {
                            // Bounds retain the scalar point and physical inline subtraction
                            // before inverse scaling; paint fitting keeps its separate origin.
                            SvgAffineTransform sourcePoint = measured.GlyphMap(midpoint, 0, 0, work, token);
                            float scale = (float)pathScale;
                            float sourceX = (float)sourcePoint.E * scale - (float)(glyph.Advance * pathScale) / 2;
                            float sourceY = (float)sourcePoint.F * scale + (float)(glyph.Y * pathScale);
                            sourceMap = SvgAffineTransform.FromTranslation(sourceX / (double)scale, sourceY / (double)scale);
                        }
                        SvgTextDecorationPlacement decoration = default;
                        if (paint.Decoration is not null)
                        {
                            double stretch = run.GlyphStretch;
                            if (glyphMap.IsTranslation)
                            {
                                SvgDevicePoint origin = glyphMap.Apply(placed.X, placed.Y);
                                decoration = new(origin, SvgAffineTransform.FromScaleOffset(stretch, 1, origin.X - stretch * origin.X, 0));
                            }
                            else
                            {
                                double scale = pathScale;
                                SvgDevicePoint center = glyphMap.Apply(paintHalfAdvance, -glyph.Y);
                                float cx = (float)center.X * (float)scale, cy = (float)center.Y * (float)scale;
                                float size = SvgGlyphDeviceMetrics.GetEffectiveFontSize(glyphFont.FontSize * scale);
                                float ascent = OperatingSystem.IsWindows()
                                    ? size * glyphFont.Font.Metrics.LineMetrics.WindowsAscender / glyphFont.Font.UnitsPerEm
                                    : glyphFont.Font.Metrics.LineMetrics.Ascender * (size / glyphFont.Font.UnitsPerEm);
                                ascent = MathF.Floor(ascent + .5f);
                                // Decoration rectangles use the physical fragment origin
                                // (truncated layout units), separately from glyph fitting.
                                SvgDevicePoint origin = new(MathF.Truncate(cx * 64) / (64 * scale), (MathF.Truncate((cy - ascent) * 64) / 64 + ascent) / scale);
                                SvgAffineTransform map = glyphMap.ComposeChecked(SvgAffineTransform.FromScale(stretch, 1))
                                    .ComposeChecked(SvgAffineTransform.FromTranslation(-cx / scale, -cy / scale));
                                decoration = new(origin, map);
                            }
                            decoration = decoration with { Map = pending.Placement.ComposeChecked(decoration.Map) };
                        }
                        // A zero tangent angle uses the native horizontal strike, even on a curved baseline.
                        if (!horizontalPlacement && glyphMap.A == 1 && glyphMap.B == 0 && glyphMap.C == 0 && glyphMap.D == 1)
                        {
                            placed = placed with { X = glyphMap.E, Y = glyphMap.F };
                            glyphMap = SvgAffineTransform.Identity;
                        }
                        else if (!horizontalPlacement && (OperatingSystem.IsWindows() || OperatingSystem.IsLinux()))
                        {
                            SvgGlyphDeviceMetrics metrics = new(pathScale, OperatingSystem.IsWindows() ? SvgGlyphMetricTarget.Windows : SvgGlyphMetricTarget.Linux, true);
                            glyphMap = metrics.PlaceTextPathPaint(glyphMap, paintHalfAdvance, glyph.Y, glyphFont.FontSize, glyphFont.Font.Metrics);
                        }
                        if (glyphMap == SvgAffineTransform.Identity && (OperatingSystem.IsWindows() || OperatingSystem.IsLinux()))
                        {
                            // Zero-angle fragments truncate their whole scaled paint origin too.
                            SvgGlyphDeviceMetrics metrics = new(pathScale, OperatingSystem.IsWindows() ? SvgGlyphMetricTarget.Windows : SvgGlyphMetricTarget.Linux, true);
                            SvgAffineTransform origin = metrics.PlaceTextPathPaint(SvgAffineTransform.FromTranslation(placed.X, placed.Y), 0, 0, glyphFont.FontSize, glyphFont.Font.Metrics);
                            double paintedX = origin.E;
                            if (run.GlyphStretch != 1)
                            {
                                // Zero-angle paths use the same physical paint-origin
                                // truncation and float stretch composition as ordinary text.
                                float sourceX = (float)placed.X * (float)pathScale;
                                float stretch = (float)run.GlyphStretch;
                                float translation = sourceX - stretch * sourceX;
                                paintedX = (MathF.Truncate(sourceX * 64f) / 64 * stretch + translation) / (float)pathScale;
                            }
                            placed = placed with { X = paintedX, Y = origin.F };
                        }
                        if (run.GlyphStretch != 1)
                        {
                            SvgAffineTransform stretch = SvgAffineTransform.FromScale(run.GlyphStretch, 1);
                            glyphMap = (glyphMap == SvgAffineTransform.Identity ? SvgAffineTransform.FromTranslation(placed.X, placed.Y) : glyphMap).ComposeChecked(stretch);
                            placed = placed with { X = 0, Y = 0 };
                        }
                        if (pending.Placement != SvgAffineTransform.Identity)
                        {
                            if (glyphMap == SvgAffineTransform.Identity && pending.Placement.IsTranslation) placed = placed.Translate(pending.Placement);
                            else glyphMap = pending.Placement.ComposeChecked(glyphMap);
                        }
                        placements.Add(new(placed, glyphMap, true) { SourceMap = sourceMap, Decoration = decoration });
                    }
                    SvgTextGlyphPainter.Emit(run, pending.Placement, placements.AsSpan(), pending.ViewportClip, pending.Color, state, limits.MaximumPathCommands, state.Budget, work, pending.RecordingTransform, token);
                }
            }
            run.Dispose();
            pending.Owner.Dispose();
        }
        finally
        {
            state.DefinitionReplayScene = previous;
            state.FilterGroupDepth = previousFilterDepth;
        }
    }
}
