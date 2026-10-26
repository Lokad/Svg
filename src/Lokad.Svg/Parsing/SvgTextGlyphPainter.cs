// Adapts Blink's source-coordinate drawing rectangle enclosure before mapping.
// Copyright 2017, 2018 The Chromium Authors. See THIRD_PARTY_NOTICES.txt.
using Lokad.Svg.Fonts;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Parsing;

/// <summary>Borrows one resolved path placement; in-range entries retain layout bounds even without paint, while invisible entries preserve indexing for out-of-range characters.</summary>
internal readonly record struct SvgPlacedTextGlyph(SvgPositionedGlyph Position, SvgAffineTransform Map, bool Visible)
{
    /// <summary>Retains the local source map before paint truncation, glyph stretch and output placement.</summary>
    internal SvgAffineTransform SourceMap { get; init; }
    internal SvgTextDecorationPlacement Decoration { get; init; }
}

/// <summary>Retains the native fragment origin separately from its tangent map for rectangle paint precision.</summary>
internal readonly record struct SvgTextDecorationPlacement(SvgDevicePoint Origin, SvgAffineTransform Map);

/// <summary>Borrows one immutable scene-owned text paint; variants contain exactly one bound paint source.</summary>
/// <remarks>Implementations retain no parser state or font ownership. Scene construction reserves metadata
/// and dominant stop/tile storage before publication. Raster replay borrows the value, charges sampling
/// and observes cancellation; immutable variants support concurrent independent conversions.</remarks>
internal abstract record SvgTextShader;
internal sealed record SvgTextLinearShader(SvgLinearGradient Gradient) : SvgTextShader;
internal sealed record SvgTextRadialShader(SvgRadialGradient Gradient) : SvgTextShader;
internal sealed record SvgTextPatternShader(SvgPatternGeometry Pattern) : SvgTextShader;

/// <summary>Replays fill and stroke in normalized fragment order using the existing platform glyph sources.</summary>
/// <remarks>All inputs remain borrowed. Each emitted geometry transfers to the scene; temporary decoded contours
/// observe the shared budget/work/token and release on every exit. Direct and path font-placement distinctions remain explicit.</remarks>
internal static class SvgTextGlyphPainter
{
    /// <summary>Emits one direct run with the same ordered decorations, paints and borrowed clip as deferred replay.</summary>
    internal static void EmitDirect(SvgTextRun run, SvgAffineTransform placement, SvgLeafMaskBinding? clip, SvgColor fallback,
        SvgDocumentState state, int maximumCommands, RenderBudget budget, ParserWorkCounter work, SvgRecordedTransform? recording, CancellationToken token)
        => EmitDirect(run, placement, clip, fallback, state, maximumCommands, budget, work, recording, null, token);

    internal static void EmitDirect(SvgTextRun run, SvgAffineTransform placement, SvgLeafMaskBinding? clip, SvgColor fallback,
        SvgDocumentState state, int maximumCommands, RenderBudget budget, ParserWorkCounter work, SvgRecordedTransform? recording, SvgRectangle? objectBox, CancellationToken token)
    {
        SvgTextPaintPreparation prepared = SvgTextPaintPreparation.Create(run, work, token);
        prepared.ObjectBox = objectBox;
        SvgTextDecorationRenderer.Emit(run, run.PaintFragments, placement, [], clip, state, fallback, false, maximumCommands, budget, work, recording, ref prepared, token);
        Emit(run, placement, [], clip, fallback, state, maximumCommands, budget, work, recording, ref prepared, token);
        SvgTextDecorationRenderer.Emit(run, run.PaintFragments, placement, [], clip, state, fallback, true, maximumCommands, budget, work, recording, ref prepared, token);
    }

    internal static void Emit(SvgTextRun run, SvgAffineTransform placement, ReadOnlySpan<SvgPlacedTextGlyph> pathPlacements,
        SvgLeafMaskBinding? pathClip, SvgColor fallback, SvgDocumentState state, int maximumCommands, RenderBudget budget, ParserWorkCounter work, SvgRecordedTransform? recording, CancellationToken token)
        => Emit(run, placement, pathPlacements, pathClip, fallback, state, maximumCommands, budget, work, recording, null, token);

    internal static void Emit(SvgTextRun run, SvgAffineTransform placement, ReadOnlySpan<SvgPlacedTextGlyph> pathPlacements,
        SvgLeafMaskBinding? pathClip, SvgColor fallback, SvgDocumentState state, int maximumCommands, RenderBudget budget, ParserWorkCounter work, SvgRecordedTransform? recording, SvgRectangle? objectBox, CancellationToken token)
    {
        SvgTextPaintPreparation prepared = SvgTextPaintPreparation.Create(run, work, token);
        prepared.ObjectBox = objectBox;
        Emit(run, placement, pathPlacements, pathClip, fallback, state, maximumCommands, budget, work, recording, ref prepared, token);
    }

    private static void Emit(SvgTextRun run, SvgAffineTransform placement, ReadOnlySpan<SvgPlacedTextGlyph> pathPlacements,
        SvgLeafMaskBinding? pathClip, SvgColor fallback, SvgDocumentState state, int maximumCommands, RenderBudget budget, ParserWorkCounter work,
        SvgRecordedTransform? recording, ref SvgTextPaintPreparation prepared, CancellationToken token)
    {
        if (!pathPlacements.IsEmpty && pathPlacements.Length != run.Glyphs.Length) throw new ArgumentException("Path placements must cover every source glyph.", nameof(pathPlacements));
        if (run.PaintFragments.IsEmpty)
        {
            Fill(run, placement, pathPlacements, pathClip, fallback, 0, run.Glyphs.Length, state, budget, work, recording, ref prepared, token);
            return;
        }
        foreach (SvgTextPaintFragment fragment in run.PaintFragments)
        {
            work.Consume(token);
            if (fragment.Start == fragment.End) continue;
            SvgTextShader? shader = null;
            SvgTextGlyphPaint fragmentPaint = run.GetGlyphPaint(fragment.Start, fallback);
            if (fragmentPaint.HasStroke && fragmentPaint.Stroke is {} fragmentStroke && fragmentStroke.Style.Stroke is {} strokeValue && SvgLocalUrl.TryParseId(strokeValue, out _))
                shader = Bind(strokeValue, run, placement, pathPlacements, state, work, ref prepared, token);
            // A direct run shares each paint pass. Path text paints independently
            // placed glyphs, so an adjacent glyph's stroke follows the prior fill.
            int start = fragment.Start;
            while (start < fragment.End)
            {
                int end = pathPlacements.IsEmpty ? fragment.End : start + 1;
                SvgTextGlyphPaint paint = run.GetGlyphPaint(start, fallback);
                bool strokeFirst = paint.Stroke?.PaintOrder.StrokeFirst() == true;
                double decorationWidth = run.GlyphStretch == 0 ? 0 : (float)run.Glyphs[start].Advance / (float)run.GlyphStretch;
                ReadOnlySpan<SvgTextPaintFragment> decoration = [new(start, end, decorationWidth)];
                if (!pathPlacements.IsEmpty)
                    SvgTextDecorationRenderer.Emit(run, decoration, placement, pathPlacements, pathClip, state, fallback, false, maximumCommands, budget, work, recording, ref prepared, token);
                if (strokeFirst && paint.HasStroke) Stroke(run, placement, pathPlacements, pathClip, shader, fallback, start, end, state, budget, work, recording, ref prepared, token);
                if (paint.HasFill) Fill(run, placement, pathPlacements, pathClip, fallback, start, end, state, budget, work, recording, ref prepared, token);
                if (!strokeFirst && paint.HasStroke) Stroke(run, placement, pathPlacements, pathClip, shader, fallback, start, end, state, budget, work, recording, ref prepared, token);
                if (!pathPlacements.IsEmpty)
                    SvgTextDecorationRenderer.Emit(run, decoration, placement, pathPlacements, pathClip, state, fallback, true, maximumCommands, budget, work, recording, ref prepared, token);
                start = end;
            }
        }

        static void Fill(SvgTextRun run, SvgAffineTransform placement, ReadOnlySpan<SvgPlacedTextGlyph> paths, SvgLeafMaskBinding? pathClip,
            SvgColor fallback, int start, int end, SvgDocumentState state, RenderBudget budget, ParserWorkCounter work, SvgRecordedTransform? recording, ref SvgTextPaintPreparation prepared, CancellationToken token)
        {
            OpenTypeGlyphContourStorage? decoded = null;
            string? fillServer = null;
            SvgTextShader? shader = null;
            ushort decodedIndex = 0;
            SvgPreparedFont? decodedFont = null;
            try
            {
                for (int index = start; index < end; index++)
                {
                    work.Consume(token);
                    SvgPlacedTextGlyph placed = Place(run, index, placement, paths);
                    if (!placed.Visible) continue;
                    SvgGlyphFontStyle font = run.GetGlyphFont(index);
                    if (decoded is null || decodedIndex != placed.Position.GlyphIndex || !ReferenceEquals(decodedFont, font.Font))
                    {
                        decoded?.Dispose();
                        decoded = OpenTypeFont.GetGlyphContours(font.Font.Bytes, placed.Position.GlyphIndex, font.Font.Metrics, budget, work, token);
                        decodedIndex = placed.Position.GlyphIndex; decodedFont = font.Font;
                    }
                    using SvgGlyphContourStorage contours = SvgGlyphOutline.GetContourStorage(font.Font, placed.Position, font.FontSize, placed.Map, decoded, budget, work, token);
                    SvgTextGlyphPaint paint = run.GetGlyphPaint(index, fallback);
                    if (!paint.HasFill) continue;
                    if (paint.FillServer != fillServer)
                    {
                        fillServer = paint.FillServer;
                        shader = fillServer is null ? null : Bind(fillServer, run, placement, paths, state, work, ref prepared, token);
                    }
                    SvgPositionedGlyph position = placed.Position;
                    SvgAffineTransform map = placed.Map;
                    double screenScale = SvgGlyphDeviceMetrics.GetScreenScale(placement);
                    double size = SvgGlyphDeviceMetrics.GetEffectiveFontSize(font.FontSize * screenScale * state.OutputTextScale) / (screenScale * state.OutputTextScale);
                    if (paths.IsEmpty && map != SvgAffineTransform.Identity)
                    {
                        double scale = screenScale;
                        position = position with { X = position.X * scale, Y = position.Y * scale, Advance = position.Advance * scale };
                        size *= scale;
                        map = SvgAffineTransform.FromMatrix(map.A / scale, map.B / scale, map.C / scale, map.D / scale, map.E, map.F);
                        if (map.IsTranslation) { position = position.Translate(map); map = SvgAffineTransform.Identity; }
                    }
                    if (size == 0) continue;
                    SvgGlyphGeometry glyph = SvgGlyphGeometry.Create(contours, paint.Color, font.Font, position, size, map, paths.IsEmpty ? state.OutputTextScale : screenScale, budget);
                    glyph.Shader = shader;
                    try
                    {
                        SvgRecordedTransform? shaderRecording = shader is SvgTextPatternShader ? recording : null;
                        SvgGeometry geometry = Record(glyph, run, placement, paths, shaderRecording, state, fallback, paint.FillPaint, ref prepared, work, token);
                        if (pathClip is not null) geometry = geometry with { LeafMask = pathClip };
                        state.Scene.AddGeometry(geometry);
                    }
                    catch { glyph.Dispose(); throw; }
                }
            }
            finally { decoded?.Dispose(); }
        }

        static void Stroke(SvgTextRun run, SvgAffineTransform placement, ReadOnlySpan<SvgPlacedTextGlyph> paths, SvgLeafMaskBinding? pathClip,
            SvgTextShader? shader, SvgColor fallback, int start, int end, SvgDocumentState state, RenderBudget budget, ParserWorkCounter work, SvgRecordedTransform? recording, ref SvgTextPaintPreparation prepared, CancellationToken token)
        {
            // Empty servers have no stroke outline. Retaining one would enlarge
            // native layer bounds and shift the surviving paint's bitmap origin.
            if (shader is SvgTextLinearShader { Gradient.IsEmpty: true } or SvgTextRadialShader { Gradient.IsEmpty: true }) return;
            for (int index = start; index < end; index++)
            {
                work.Consume(token);
                SvgPlacedTextGlyph placed = Place(run, index, placement, paths);
                SvgTextGlyphPaint paint = run.GetGlyphPaint(index, fallback);
                if (!placed.Visible || paint.Stroke is not {} stroke || !paint.HasStroke) continue;
                SvgGlyphFontStyle font = run.GetGlyphFont(index);
                using SvgGlyphContourStorage contours = SvgGlyphOutline.GetContourStorage(font.Font, placed.Position, font.FontSize, placed.Map, budget, work, token);
                if (contours.Contours.Length == 0) continue;
                stroke = stroke with { FontScale = state.OutputTextScale * SvgGlyphDeviceMetrics.GetScreenScale(placement) };
                SvgGlyphGeometry glyph = SvgGlyphGeometry.CreateStroke(contours, stroke, font.Font, placed.Position, font.FontSize, placed.Map, budget);
                glyph.Shader = shader;
                try
                {
                    SvgRecordedTransform? shaderRecording = shader is SvgTextPatternShader ? recording : null;
                    SvgGeometry geometry = Record(glyph, run, placement, paths, shaderRecording, state, fallback, stroke.ResolvePaint(), ref prepared, work, token);
                    if (pathClip is not null) geometry = geometry with { LeafMask = pathClip };
                    state.Scene.AddGeometry(geometry);
                }
                catch { glyph.Dispose(); throw; }
            }
        }

        static SvgTextShader Bind(string value, SvgTextRun run, SvgAffineTransform placement, ReadOnlySpan<SvgPlacedTextGlyph> paths,
            SvgDocumentState state, ParserWorkCounter work, ref SvgTextPaintPreparation prepared, CancellationToken token)
        {
            if (!SvgLocalUrl.TryParseId(value, out string id)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only local text paints are implemented.");
            if (state.Gradients.TryGetValue(id, out SvgLinearGradient linear))
            {
                SvgRectangle box = linear.IsUserSpace || linear.IsEmpty ? default : prepared.ObjectBox ?? (prepared.SourceBox ??= SvgTextLayoutBounds.Measure(run, paths, placement, state.TextBoundsMetrics, work, token));
                return state.Scene.RetainTextShader(SvgGradientSyntax.BindLinearSourceMap(linear, box, placement, state, work, token));
            }
            if (state.RadialGradients.TryGetValue(id, out SvgRadialGradient radial))
            {
                SvgRectangle box = radial.IsUserSpace || radial.IsEmpty ? default : prepared.ObjectBox ?? (prepared.SourceBox ??= SvgTextLayoutBounds.Measure(run, paths, placement, state.TextBoundsMetrics, work, token));
                return state.Scene.RetainTextShader(SvgGradientSyntax.BindRadialSourceMap(radial, box, placement, work, token));
            }
            if (state.Patterns.TryGetValue(id, out SvgPatternTile tile))
            {
                state.OutputMap.ComposeChecked(placement);
                tile = SvgPatternReferenceResolver.ForReplay(id, tile, state, work, token);
                SvgRectangle box = tile.Coordinates is null ? default : prepared.ObjectBox ?? (prepared.SourceBox ??= SvgTextLayoutBounds.Measure(run, paths, placement, state.TextBoundsMetrics, work, token));
                SvgGeometry content = SvgPatternSyntax.LowerPatternFillGeometry(tile, box, true, 255, null, placement, null);
                if (content.Kind != SvgGeometryKind.PatternFill)
                    return state.Scene.RetainTextShader(new SvgLinearGradient(default, default, 0, 0, 1, 0, 0, 1, true, true, SvgGradientSpread.Pad, [], 1, 1, false));
                SvgPatternGeometry pattern = content.Pattern;
                pattern.BindObjectBox(new((float)box.X, (float)box.Y, (float)(box.X + box.Width), (float)(box.Y + box.Height)));
                return state.Scene.RetainTextShader(pattern);
            }
            throw new SvgException(SvgErrorCode.UnsupportedFeature, "A text paint requires a defined local paint server.");
        }

        static SvgPlacedTextGlyph Place(SvgTextRun run, int index, SvgAffineTransform placement, ReadOnlySpan<SvgPlacedTextGlyph> paths)
        {
            if (!paths.IsEmpty) return paths[index];
            (SvgPositionedGlyph position, SvgAffineTransform map) = run.GetGlyphPlacement(index, placement);
            return new(position, map, true);
        }

        static SvgGeometry Record(SvgGlyphGeometry glyph, SvgTextRun run, SvgAffineTransform placement, ReadOnlySpan<SvgPlacedTextGlyph> paths,
            SvgRecordedTransform? recording, SvgDocumentState state, SvgColor fallback, SvgSolidPaint sourcePaint, ref SvgTextPaintPreparation prepared, ParserWorkCounter work, CancellationToken token)
        {
            float alpha = glyph.Shader is null ? sourcePaint.Alpha : glyph.Color.Alpha / 255f;
            SvgPaintBounds? objects = null;
            if (state.FilterGroupDepth > 0 && prepared.NativeDrawing)
            {
                SvgRectangle box = prepared.ObjectBox ?? (prepared.SourceBox ??= SvgTextLayoutBounds.Measure(run, paths, placement, state.TextBoundsMetrics, work, token));
                objects = new SvgPaintBounds((float)box.X, (float)box.Y, (float)(box.X + box.Width), (float)(box.Y + box.Height)).Map(placement, work, token);
            }
            // Generic/oversized outlines and filter captures retain their contour
            // drawing and bitmap-origin contracts, independent of native text drawings.
            if (!prepared.NativeDrawing || state.FilterGroupDepth > 0)
                return paths.IsEmpty || recording is not null || state.FilterGroupDepth > 0 || alpha != glyph.Color.Alpha / 255f ? RecordFallback(objects) : SvgGeometry.FromGlyphContours(glyph);
            SvgRecordedPaint paint = prepared.RecordDrawing(run, placement, paths, state, fallback, alpha, recording, work, token);
            SvgGeometry nativeGeometry = SvgGeometry.FromGlyphContours(glyph) with { RecordedPaint = paint };
            if ((paths.IsEmpty || recording is not null) && state.Viewports.Count != 0)
                nativeGeometry = nativeGeometry with { LeafMask = SvgViewportFrame.ResolveGlyphClip(state, work, token) };
            return nativeGeometry;

            SvgGeometry RecordFallback(SvgPaintBounds? sourceObjects)
            {
                SvgGeometry geometry = SvgGeometry.FromGlyphContours(glyph);
                if (state.Viewports.Count != 0) geometry = geometry with { LeafMask = SvgViewportFrame.ResolveGlyphClip(state, work, token) };
                if (state.FilterGroupDepth <= 0)
                {
                    if (recording is null && alpha == glyph.Color.Alpha / 255f) return geometry;
                    SvgRecordedPaintValue recorded = new(state.NewPaintOperationIdentity(), alpha, false,
                        new SvgRecordingScope(1), SvgPaintOpacity.Opaque, new SvgRecordingScope(1)) { RecordingTransform = recording };
                    return geometry with { RecordedPaint = state.Scene.RegisterRecordedPaint(recorded) };
                }
                float left = float.PositiveInfinity, top = float.PositiveInfinity;
                float right = float.NegativeInfinity, bottom = float.NegativeInfinity;
                foreach (SvgGlyphQuadraticContour contour in glyph.Contours)
                foreach (SvgGlyphQuadraticSegment segment in contour.Segments)
                {
                    work.Consume(token);
                    Include(segment.Start); Include(segment.Control); Include(segment.End);
                }
                SvgPaintBounds bounds = float.IsPositiveInfinity(left) ? default : new(left, top, right, bottom);
                float inflation = (float)Math.Max(2, glyph.StrokeBoundsInflation);
                SvgRecordedPaintValue paint = new(state.NewPaintOperationIdentity(), alpha, false, new SvgRecordingScope(1), SvgPaintOpacity.Opaque, new SvgRecordingScope(1))
                {
                    RecordingTransform = recording,
                    ObjectBounds = sourceObjects ?? bounds,
                    VisualBounds = new(bounds.Left - inflation, bounds.Top - inflation, bounds.Right + inflation, bounds.Bottom + inflation)
                };
                return geometry with { RecordedPaint = state.Scene.RegisterRecordedPaint(paint) };

                void Include(SvgGlyphOutlinePoint point)
                {
                    left = MathF.Min(left, (float)point.X); top = MathF.Min(top, (float)point.Y);
                    right = MathF.Max(right, (float)point.X); bottom = MathF.Max(bottom, (float)point.Y);
                }
            }
        }
    }
}
