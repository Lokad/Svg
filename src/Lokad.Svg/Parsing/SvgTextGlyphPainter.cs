using Lokad.Svg.Fonts;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Parsing;

/// <summary>Borrows one resolved path placement; invisible entries preserve source/glyph indexing without painting out-of-range characters.</summary>
internal readonly record struct SvgPlacedTextGlyph(SvgPositionedGlyph Position, SvgAffineTransform Map, bool Visible)
{
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
        SvgDocumentState state, int maximumCommands, RenderBudget budget, ParserWorkCounter work, CancellationToken token)
    {
        SvgTextDecorationRenderer.Emit(run, run.PaintFragments, placement, [], clip, state, fallback, false, maximumCommands, budget, work, token);
        Emit(run, placement, [], clip, fallback, state, maximumCommands, budget, work, token);
        SvgTextDecorationRenderer.Emit(run, run.PaintFragments, placement, [], clip, state, fallback, true, maximumCommands, budget, work, token);
    }

    internal static void Emit(SvgTextRun run, SvgAffineTransform placement, ReadOnlySpan<SvgPlacedTextGlyph> pathPlacements,
        SvgLeafMaskBinding? pathClip, SvgColor fallback, SvgDocumentState state, int maximumCommands, RenderBudget budget, ParserWorkCounter work, CancellationToken token)
    {
        if (!pathPlacements.IsEmpty && pathPlacements.Length != run.Glyphs.Length) throw new ArgumentException("Path placements must cover every source glyph.", nameof(pathPlacements));
        if (run.PaintFragments.IsEmpty)
        {
            Fill(run, placement, pathPlacements, pathClip, fallback, 0, run.Glyphs.Length, state, budget, work, token);
            return;
        }
        foreach (SvgTextPaintFragment fragment in run.PaintFragments)
        {
            work.Consume(token);
            SvgTextShader? shader = null;
            SvgTextGlyphPaint fragmentPaint = run.GetGlyphPaint(fragment.Start, fallback);
            if (fragmentPaint.HasStroke && fragmentPaint.Stroke is {} fragmentStroke && fragmentStroke.Style.Stroke is {} strokeValue && SvgLocalUrl.TryParseId(strokeValue, out _))
                shader = Bind(strokeValue, run, placement, pathPlacements, state, work, token);
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
                    SvgTextDecorationRenderer.Emit(run, decoration, placement, pathPlacements, pathClip, state, fallback, false, maximumCommands, budget, work, token);
                if (strokeFirst && paint.HasStroke) Stroke(run, placement, pathPlacements, pathClip, shader, fallback, start, end, state, budget, work, token);
                if (paint.HasFill) Fill(run, placement, pathPlacements, pathClip, fallback, start, end, state, budget, work, token);
                if (!strokeFirst && paint.HasStroke) Stroke(run, placement, pathPlacements, pathClip, shader, fallback, start, end, state, budget, work, token);
                if (!pathPlacements.IsEmpty)
                    SvgTextDecorationRenderer.Emit(run, decoration, placement, pathPlacements, pathClip, state, fallback, true, maximumCommands, budget, work, token);
                start = end;
            }
        }

        static void Fill(SvgTextRun run, SvgAffineTransform placement, ReadOnlySpan<SvgPlacedTextGlyph> paths, SvgLeafMaskBinding? pathClip,
            SvgColor fallback, int start, int end, SvgDocumentState state, RenderBudget budget, ParserWorkCounter work, CancellationToken token)
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
                        shader = fillServer is null ? null : Bind(fillServer, run, placement, paths, state, work, token);
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
                    SvgGlyphGeometry glyph = SvgGlyphGeometry.Create(contours, paint.Color, font.Font, position, size, map, state.OutputTextScale, budget);
                    glyph.Shader = shader;
                    try
                    {
                        SvgGeometry geometry = paths.IsEmpty ? SvgPaintRecording.RecordGlyph(glyph, state, work, token) : SvgGeometry.FromGlyphContours(glyph);
                        if (pathClip is not null) geometry = geometry with { LeafMask = pathClip };
                        state.Scene.AddGeometry(geometry);
                    }
                    catch { glyph.Dispose(); throw; }
                }
            }
            finally { decoded?.Dispose(); }
        }

        static void Stroke(SvgTextRun run, SvgAffineTransform placement, ReadOnlySpan<SvgPlacedTextGlyph> paths, SvgLeafMaskBinding? pathClip,
            SvgTextShader? shader, SvgColor fallback, int start, int end, SvgDocumentState state, RenderBudget budget, ParserWorkCounter work, CancellationToken token)
        {
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
                    SvgGeometry geometry = paths.IsEmpty ? SvgPaintRecording.RecordGlyph(glyph, state, work, token) : SvgGeometry.FromGlyphContours(glyph);
                    if (pathClip is not null) geometry = geometry with { LeafMask = pathClip };
                    state.Scene.AddGeometry(geometry);
                }
                catch { glyph.Dispose(); throw; }
            }
        }

        static SvgTextShader Bind(string value, SvgTextRun run, SvgAffineTransform placement, ReadOnlySpan<SvgPlacedTextGlyph> paths,
            SvgDocumentState state, ParserWorkCounter work, CancellationToken token)
        {
            if (!SvgLocalUrl.TryParseId(value, out string id)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only local text paints are implemented.");
            if (state.Gradients.TryGetValue(id, out SvgLinearGradient linear))
            {
                SvgRectangle box = linear.IsUserSpace || linear.IsEmpty ? default : SvgTextLayoutBounds.Measure(run, paths, placement, work, token);
                return state.Scene.RetainTextShader(SvgGradientSyntax.BindLinearSourceMap(linear, box, placement, state, work, token));
            }
            if (state.RadialGradients.TryGetValue(id, out SvgRadialGradient radial))
            {
                SvgRectangle box = radial.IsUserSpace || radial.IsEmpty ? default : SvgTextLayoutBounds.Measure(run, paths, placement, work, token);
                return state.Scene.RetainTextShader(SvgGradientSyntax.BindRadialSourceMap(radial, box, placement, work, token));
            }
            if (state.Patterns.TryGetValue(id, out SvgPatternTile tile))
            {
                tile = SvgPatternReferenceResolver.ForPlacement(id, tile, state.OutputMap.ComposeChecked(placement), state, work, token);
                SvgRectangle box = tile.Coordinates is null ? default : SvgTextLayoutBounds.Measure(run, paths, placement, work, token);
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
    }
}
