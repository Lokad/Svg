// Adapts Blink's SVG solid-decoration metrics and rectangle placement.
// Copyright 2014, 2017, 2020 The Chromium Authors. See THIRD_PARTY_NOTICES.txt.
using Lokad.Svg.Fonts;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Parsing;

/// <summary>Emits bounded solid decoration fragments through ordinary retained rectangle geometry and the active scene scopes.</summary>
internal static class SvgTextDecorationRenderer
{
    internal static void Emit(SvgTextRun run, SvgAffineTransform placement, SvgDocumentState state, SvgColor fallbackColor,
        bool lineThrough, int maximumCommands, RenderBudget budget, ParserWorkCounter work, CancellationToken token)
    {
        if (run.PaintFragments.IsEmpty) return;
        using BudgetedParserList<SvgPoint> points = new(budget, SvgMemoryCategory.Geometry, work, token);
        using BudgetedParserList<int> ends = new(budget, SvgMemoryCategory.Geometry, work, token);
        SvgOrthogonalGeometryLowering lowering = new(points, ends, "text decoration", false, false, maximumCommands, budget, work, token);
        try
        {
            foreach (SvgTextPaintFragment fragment in run.PaintFragments)
            {
                work.Consume(token);
                SvgTextGlyphPaint glyphPaint = run.GetGlyphPaint(fragment.Start, fallbackColor);
                if (glyphPaint.Decoration is not {} decoration || !decoration.Paint.IsPainted) continue;
                SvgTextDecorationLine lines = lineThrough ? decoration.Lines & SvgTextDecorationLine.LineThrough
                    : decoration.Lines & (SvgTextDecorationLine.Underline | SvgTextDecorationLine.Overline);
                if (lines == SvgTextDecorationLine.None) continue;
                SvgGlyphFontStyle font = run.GetGlyphFont(fragment.Start);
                SvgGlyphDeviceMetrics metrics = run.DeviceMetrics ?? new(1, OperatingSystem.IsWindows() ? SvgGlyphMetricTarget.Windows : SvgGlyphMetricTarget.Linux, false);
                float size = (float)(font.FontSize * metrics.Scale);
                float ascent = metrics.Target == SvgGlyphMetricTarget.Windows
                    ? size * font.Font.Metrics.LineMetrics.WindowsAscender / font.Font.UnitsPerEm
                    : font.Font.Metrics.LineMetrics.Ascender * (size / font.Font.UnitsPerEm);
                ascent = MathF.Floor(ascent + .5f);
                float thickness = size / 10f;
                (SvgPositionedGlyph first, SvgAffineTransform map) = run.GetDecorationPlacement(fragment.Start, placement);
                double width = metrics.RoundWidth(fragment.Width);
                float top = MathF.Truncate(((float)(first.Y * metrics.Scale) - ascent) * 64) / 64;
                double x = MathF.Truncate((float)(first.X * metrics.Scale) * 64) / (64 * metrics.Scale);
                if (width <= 0 || thickness <= 0) continue;
                SvgTextGlyphPaint paint = decoration.Paint;
                if ((lines & SvgTextDecorationLine.Underline) != 0)
                for (int line = 0; line < decoration.UnderlineCount; line++)
                    Line(ref lowering, run, fragment, first, x, (top + ascent + MathF.Max(1, MathF.Ceiling(thickness / 2))) / metrics.Scale, width, thickness / metrics.Scale, metrics.Scale, map, paint, state, budget, work, token);
                if ((lines & SvgTextDecorationLine.Overline) != 0)
                for (int line = 0; line < decoration.OverlineCount; line++)
                    Line(ref lowering, run, fragment, first, x, (top - MathF.Floor(thickness)) / metrics.Scale, width, thickness / metrics.Scale, metrics.Scale, map, paint, state, budget, work, token);
                if ((lines & SvgTextDecorationLine.LineThrough) != 0)
                for (int line = 0; line < decoration.LineThroughCount; line++)
                    Rectangle(ref lowering, x, (top + 2 * ascent / 3 - thickness / 2) / metrics.Scale, width, thickness / metrics.Scale, map, paint, null, state, budget, work, token);
            }
        }
        finally { lowering.Dispose(); }

        static void Line(ref SvgOrthogonalGeometryLowering lowering, SvgTextRun run, SvgTextPaintFragment fragment, SvgPositionedGlyph first,
            double x, double y, double width, double height, double scale, SvgAffineTransform map, SvgTextGlyphPaint paint,
            SvgDocumentState state, RenderBudget budget, ParserWorkCounter work, CancellationToken token)
        {
            using BudgetedParserList<SvgTextInkGap> gaps = new(budget, SvgMemoryCategory.Geometry, work, token);
            double inset = Math.Min(height / 2, .5 / scale);
            SvgTextInkIntercepts.Collect(run, fragment, first, y + inset, y + height - inset, Math.Min(height, 13 / scale), gaps, budget, work, token);
            if (gaps.Count == 0)
            {
                Rectangle(ref lowering, x, y, width, height, map, paint, null, state, budget, work, token);
                return;
            }
            if (paint.HasStroke)
            {
                RenderBudget.BudgetReservation owner = budget.Reserve(ParserCollectionMemory.GetArrayByteCount<SvgClipBox>(gaps.Count), SvgMemoryCategory.Geometry);
                SvgClipBox[] holes;
                int count = 0;
                try
                {
                    holes = new SvgClipBox[gaps.Count];
                    // Native ink exclusions retain the stripe's upper stroke fringe,
                    // but cover the lower half-pixel and the end glyph's full intercept.
                    double top = Math.Floor(y * scale) / scale, bottom = Math.Ceiling((y + height) * scale + .5) / scale;
                    foreach (SvgTextInkGap gap in gaps.AsSpan())
                    {
                        work.Consume(token);
                        double left = gap.Left, right = gap.Right;
                        if (right <= left) continue;
                        if (count > 0 && left <= holes[count - 1].Right)
                            holes[count - 1] = holes[count - 1] with { Width = Math.Max(holes[count - 1].Right, right) - holes[count - 1].X };
                        else holes[count++] = new(left, top, right - left, bottom - top);
                    }
                    state.DefinitionReservations.Add(owner);
                }
                catch { owner.Dispose(); throw; }
                SvgLeafMaskBinding? clip = SvgViewportFrame.ResolveGlyphClip(state, work, token);
                if (count > 0)
                {
                    SvgAffineTransform device = state.OutputMap.ComposeChecked(map);
                    if (!device.TryInvert(out SvgAffineTransform inverse)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Singular text decoration clipping maps are not implemented.");
                    double stroke = (paint.Stroke?.Resolve()?.Width ?? 0) / 2;
                    double outsetX = stroke + 2 * (Math.Abs(inverse.A) + Math.Abs(inverse.C));
                    double outsetY = stroke + 2 * (Math.Abs(inverse.B) + Math.Abs(inverse.D));
                    SvgLeafMaskOperation operation = SvgLeafMaskComposition.NewOperation(state, null, SvgAffineTransform.Identity);
                    operation.Resolved = true; operation.Value = 255;
                    operation.AffineViewport = new(new(x - outsetX, y - outsetY, width + 2 * outsetX, height + 2 * outsetY), map)
                    { IsBinary = true, ExcludedBoxes = holes.AsMemory(0, count) };
                    operation.RegionKind = SvgLayerRegionKind.AffineViewport;
                    clip = SvgLeafMaskComposition.NewBinding(state, operation, clip);
                }
                Rectangle(ref lowering, x, y, width, height, map, paint, clip, state, budget, work, token);
                return;
            }
            double cursor = x, end = x + width;
            foreach (SvgTextInkGap gap in gaps.AsSpan())
            {
                work.Consume(token);
                double next = Math.Min(gap.Left, end);
                if (next > cursor) Piece(cursor, next, ref lowering, x, y, width, height, map, paint, state, budget, work, token);
                cursor = Math.Max(cursor, gap.Right);
                if (cursor >= end) return;
            }
            if (cursor < end) Piece(cursor, end, ref lowering, x, y, width, height, map, paint, state, budget, work, token);
        }

        static void Piece(double left, double right, ref SvgOrthogonalGeometryLowering lowering, double x, double y, double width, double height,
            SvgAffineTransform map, SvgTextGlyphPaint paint, SvgDocumentState state, RenderBudget budget, ParserWorkCounter work, CancellationToken token)
        {
            SvgAffineTransform device = state.OutputMap.ComposeChecked(map);
            if (!device.TryInvert(out SvgAffineTransform inverse))
                throw new SvgException(SvgErrorCode.UnsupportedFeature, "Singular text decoration clipping maps are not implemented.");
            // Keep the rectangle's own antialiased outer fringe. Only interior
            // ink boundaries use binary clipping; the outer clip is two device
            // pixels beyond the paint, mapped back through the same affine map.
            double outsetX = 2 * (Math.Abs(inverse.A) + Math.Abs(inverse.C));
            double outsetY = 2 * (Math.Abs(inverse.B) + Math.Abs(inverse.D));
            double clipLeft = left == x ? left - outsetX : left;
            double clipRight = right == x + width ? right + outsetX : right;
            SvgLeafMaskOperation operation = SvgLeafMaskComposition.NewOperation(state, null, SvgAffineTransform.Identity);
            operation.Resolved = true; operation.Value = 255;
            operation.AffineViewport = new(new(clipLeft, y - outsetY, clipRight - clipLeft, height + 2 * outsetY), map) { IsBinary = true };
            operation.RegionKind = SvgLayerRegionKind.AffineViewport;
            SvgLeafMaskBinding clip = SvgLeafMaskComposition.NewBinding(state, operation, SvgViewportFrame.ResolveGlyphClip(state, work, token));
            Rectangle(ref lowering, x, y, width, height, map, paint, clip, state, budget, work, token);
        }

        static void Rectangle(ref SvgOrthogonalGeometryLowering lowering, double x, double y, double width, double height,
            SvgAffineTransform map, SvgTextGlyphPaint paint, SvgLeafMaskBinding? clip, SvgDocumentState state, RenderBudget budget, ParserWorkCounter work, CancellationToken token)
        {
            work.Consume(token);
            SvgColor color = paint.Color;
            SvgPaint fill = new(color, paint.HasFill, SvgPaintOpacity.Opaque, color, false);
            SvgStroke? stroke = paint.Stroke?.Resolve();
            SvgResolvedPaintableElement element = new(default, map, new(fill, color, stroke?.Color, 255, true), state.OutputTextScale)
            { StrokeHostMap = state.OutputMap.ComposeChecked(map) };
            bool strokeFirst = paint.Stroke?.PaintOrder.StrokeFirst() == true;
            if (strokeFirst && stroke is {} first && paint.Stroke is {} firstStyle)
                Stroke(ref lowering, x, y, width, height, element, first, firstStyle, clip, state, budget, work, token);
            if (paint.HasFill)
            {
                if (map.IsAxisAligned)
                {
                    float left = (float)x, top = (float)y, right = left + (float)width, bottom = top + (float)height;
                    SvgRectangle rectangle = map.Apply(new SvgRectangle(left, top, right - (double)left, bottom - (double)top, color, true, null));
                    SvgGeometry geometry = SvgGeometry.FromRectangle(rectangle) with { SolidPaint = SvgPaintResolver.ResolveSolidPaint(fill) };
                    state.Scene.AddGeometry(Prepare(geometry, x, y, width, height, map, color, 0, clip, state, work, token));
                }
                else
                {
                    using SvgParsedGeometry parsed = lowering.LowerRectangleSourceFill(x, y, width, height, element, null, budget);
                    try
                    {
                        for (int index = 0; index < parsed.Storage.Items.Length; index++)
                            parsed.Storage.Items[index] = Prepare(parsed.Storage.Items[index], x, y, width, height, map, color, 0, clip, state, work, token);
                        state.Scene.AddGeometries(parsed.Geometries);
                    }
                    catch { parsed.Discard(); throw; }
                }
            }
            if (!strokeFirst && stroke is {} last && paint.Stroke is {} lastStyle)
                Stroke(ref lowering, x, y, width, height, element, last, lastStyle, clip, state, budget, work, token);

            static void Stroke(ref SvgOrthogonalGeometryLowering lowering, double x, double y, double width, double height,
                SvgResolvedPaintableElement element, SvgStroke stroke, SvgTextStrokePaint paint, SvgLeafMaskBinding? clip,
                SvgDocumentState state, RenderBudget budget, ParserWorkCounter work, CancellationToken token)
            {
                SvgInheritedStrokeStyle style = paint.Style;
                SvgStrokeStyleSyntax.SvgAdmittedStroke admitted = new(stroke.Color, stroke.Width,
                    SvgStrokeStyleSyntax.ParseMiterLimit(style.Miterlimit), SvgStrokeStyleSyntax.ParseLinecap(style.Linecap),
                    SvgStrokeStyleSyntax.ParseLinejoin(style.Linejoin, true), SvgStrokeStyleSyntax.ParseDasharray(style.Dasharray),
                    SvgStrokeStyleSyntax.ParseDashoffset(style.Dashoffset), null, paint.NonScaling);
                using SvgParsedGeometry parsed = lowering.LowerRoundedRectStroke(x, y, width, height, 0, 0, element, admitted, null, budget);
                try
                {
                    for (int index = 0; index < parsed.Storage.Items.Length; index++)
                        parsed.Storage.Items[index] = Prepare(parsed.Storage.Items[index], x, y, width, height, element.Transform, stroke.Color, stroke.Width / 2, clip, state, work, token);
                    state.Scene.AddGeometries(parsed.Geometries);
                }
                catch { parsed.Discard(); throw; }
            }
        }

        static SvgGeometry Prepare(SvgGeometry geometry, double x, double y, double width, double height, SvgAffineTransform map,
            SvgColor color, double strokeOutset, SvgLeafMaskBinding? clip, SvgDocumentState state, ParserWorkCounter work, CancellationToken token)
        {
            geometry = geometry with { LeafMask = clip ?? SvgViewportFrame.ResolveGlyphClip(state, work, token) };
            if (state.FilterGroupDepth > 0)
            {
                SvgDevicePoint first = map.Apply(x, y), second = map.Apply(x + width, y), third = map.Apply(x, y + height), fourth = map.Apply(x + width, y + height);
                SvgPaintBounds bounds = new((float)Math.Min(Math.Min(first.X, second.X), Math.Min(third.X, fourth.X)),
                    (float)Math.Min(Math.Min(first.Y, second.Y), Math.Min(third.Y, fourth.Y)),
                    (float)Math.Max(Math.Max(first.X, second.X), Math.Max(third.X, fourth.X)),
                    (float)Math.Max(Math.Max(first.Y, second.Y), Math.Max(third.Y, fourth.Y)));
                SvgRecordedPaintValue recorded = new(state.NewPaintOperationIdentity(), color.Alpha / 255f, false, new(1), SvgPaintOpacity.Opaque, new(1))
                { ObjectBounds = bounds, VisualBounds = new(bounds.Left - (float)(strokeOutset * (Math.Abs(map.A) + Math.Abs(map.C))),
                    bounds.Top - (float)(strokeOutset * (Math.Abs(map.B) + Math.Abs(map.D))), bounds.Right + (float)(strokeOutset * (Math.Abs(map.A) + Math.Abs(map.C))),
                    bounds.Bottom + (float)(strokeOutset * (Math.Abs(map.B) + Math.Abs(map.D)))) };
                geometry = geometry with { RecordedPaint = state.Scene.RegisterRecordedPaint(recorded) };
            }
            return geometry;
        }
    }
}
