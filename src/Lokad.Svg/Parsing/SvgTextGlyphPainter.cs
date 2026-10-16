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

/// <summary>Replays fill and stroke in normalized fragment order using the existing platform glyph sources.</summary>
/// <remarks>All inputs remain borrowed. Each emitted geometry transfers to the scene; temporary decoded contours
/// observe the shared budget/work/token and release on every exit. Direct and path font-placement distinctions remain explicit.</remarks>
internal static class SvgTextGlyphPainter
{
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
                if (strokeFirst && paint.HasStroke) Stroke(run, placement, pathPlacements, pathClip, fallback, start, end, state, budget, work, token);
                if (paint.HasFill) Fill(run, placement, pathPlacements, pathClip, fallback, start, end, state, budget, work, token);
                if (!strokeFirst && paint.HasStroke) Stroke(run, placement, pathPlacements, pathClip, fallback, start, end, state, budget, work, token);
                if (!pathPlacements.IsEmpty)
                    SvgTextDecorationRenderer.Emit(run, decoration, placement, pathPlacements, pathClip, state, fallback, true, maximumCommands, budget, work, token);
                start = end;
            }
        }

        static void Fill(SvgTextRun run, SvgAffineTransform placement, ReadOnlySpan<SvgPlacedTextGlyph> paths, SvgLeafMaskBinding? pathClip,
            SvgColor fallback, int start, int end, SvgDocumentState state, RenderBudget budget, ParserWorkCounter work, CancellationToken token)
        {
            OpenTypeGlyphContourStorage? decoded = null;
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
                    SvgPositionedGlyph position = placed.Position;
                    SvgAffineTransform map = placed.Map;
                    double screenScale = SvgGlyphDeviceMetrics.GetScreenScale(placement);
                    double size = SvgGlyphDeviceMetrics.GetEffectiveFontSize(font.FontSize * screenScale * state.OutputTextScale) / (screenScale * state.OutputTextScale);
                    if (paths.IsEmpty && map != SvgAffineTransform.Identity)
                    {
                        double scale = screenScale;
                        position = position with { X = position.X * scale, Y = position.Y * scale };
                        size *= scale;
                        map = SvgAffineTransform.FromMatrix(map.A / scale, map.B / scale, map.C / scale, map.D / scale, map.E, map.F);
                        if (map.IsTranslation) { position = position.Translate(map); map = SvgAffineTransform.Identity; }
                    }
                    if (size == 0) continue;
                    SvgGlyphGeometry glyph = SvgGlyphGeometry.Create(contours, paint.Color, font.Font, position, size, map, state.DefinitionReplayScene is null ? null : state.OutputTextScale, budget);
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
            SvgColor fallback, int start, int end, SvgDocumentState state, RenderBudget budget, ParserWorkCounter work, CancellationToken token)
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
                try
                {
                    SvgGeometry geometry = paths.IsEmpty ? SvgPaintRecording.RecordGlyph(glyph, state, work, token) : SvgGeometry.FromGlyphContours(glyph);
                    if (pathClip is not null) geometry = geometry with { LeafMask = pathClip };
                    state.Scene.AddGeometry(geometry);
                }
                catch { glyph.Dispose(); throw; }
            }
        }

        static SvgPlacedTextGlyph Place(SvgTextRun run, int index, SvgAffineTransform placement, ReadOnlySpan<SvgPlacedTextGlyph> paths)
        {
            if (!paths.IsEmpty) return paths[index];
            (SvgPositionedGlyph position, SvgAffineTransform map) = run.GetGlyphPlacement(index, placement);
            return new(position, map, true);
        }
    }
}
