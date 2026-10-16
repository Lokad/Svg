using Lokad.Svg.Fonts;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Parsing;

/// <summary>Measures the unclipped text layout box independently of fitted glyph coverage and stroke fringes.</summary>
/// <remarks>Font programs and runs remain borrowed. Header visits and corner maps consume shared work and
/// observe cancellation; no outlines, pixels or owned storage are created.</remarks>
internal static class SvgTextLayoutBounds
{
    internal static SvgRectangle Measure(SvgTextRun run, ReadOnlySpan<SvgPlacedTextGlyph> paths, SvgAffineTransform placement,
        ParserWorkCounter work, CancellationToken token)
    {
        double left = double.PositiveInfinity, top = double.PositiveInfinity, right = double.NegativeInfinity, bottom = double.NegativeInfinity;
        double inkLeft = double.PositiveInfinity, inkTop = double.PositiveInfinity, inkRight = double.NegativeInfinity, inkBottom = double.NegativeInfinity;
        double scale = run.DeviceMetrics?.Scale ?? 1;
        SvgAffineTransform inverse = SvgAffineTransform.Identity;
        if (!paths.IsEmpty && !placement.TryInvert(out inverse))
            throw new SvgException(SvgErrorCode.UnsupportedFeature, "Singular text paint placement is not implemented.");
        for (int index = 0; index < run.Glyphs.Length; index++)
        {
            work.Consume(token);
            SvgPositionedGlyph glyph = run.Glyphs[index];
            SvgGlyphFontStyle face = run.GetGlyphFont(index);
            float size = SvgGlyphDeviceMetrics.GetEffectiveFontSize(face.FontSize * scale);
            OpenTypeFontMetrics metrics = face.Font.Metrics;
            float ascent = OperatingSystem.IsWindows() ? size * metrics.LineMetrics.WindowsAscender / metrics.UnitsPerEm : metrics.LineMetrics.Ascender * (size / metrics.UnitsPerEm);
            float descent = OperatingSystem.IsWindows() ? size * metrics.LineMetrics.WindowsDescender / metrics.UnitsPerEm : -metrics.LineMetrics.Descender * (size / metrics.UnitsPerEm);
            double heightAbove = MathF.Floor(ascent + .5f) / scale, heightBelow = MathF.Floor(descent + .5f) / scale;
            SvgAffineTransform map;
            if (paths.IsEmpty)
                map = SvgAffineTransform.FromTranslation(glyph.X, glyph.Y).ComposeChecked(SvgAffineTransform.FromRotationDegrees(run.GetGlyphRotation(index)))
                    .ComposeChecked(SvgAffineTransform.FromScale(run.GlyphStretch, 1));
            else
            {
                SvgPlacedTextGlyph path = paths[index];
                if (!path.Visible) continue;
                map = inverse.ComposeChecked(path.Map).ComposeChecked(SvgAffineTransform.FromTranslation(path.Position.X, path.Position.Y));
            }
            double advance = run.GlyphStretch == 0 ? 0 : glyph.Advance / run.GlyphStretch;
            Include(map, 0, -heightAbove, advance, heightBelow, ref left, ref top, ref right, ref bottom);
            if (OpenTypeFont.GetGlyphHeader(face.Font.Bytes, glyph.GlyphIndex, metrics, work, token) is {} header)
            {
                double unit = size / (scale * metrics.UnitsPerEm);
                Include(map, header.XMinimum * unit, -header.YMaximum * unit, header.XMaximum * unit, -header.YMinimum * unit,
                    ref inkLeft, ref inkTop, ref inkRight, ref inkBottom);
            }
        }
        if (double.IsPositiveInfinity(left)) return default;
        if (paths.IsEmpty)
            foreach (SvgTextPaintFragment fragment in run.PaintFragments)
            {
                work.Consume(token);
                if (run.GetGlyphRotation(fragment.Start) != 0) continue;
                // Layout retains the unrounded fragment width before paint positions
                // truncate to 1/64. The last painted origin cannot recover that width.
                double width = run.DeviceMetrics?.RoundWidth(fragment.Width) ?? fragment.Width;
                left = Math.Min(left, run.Glyphs[fragment.Start].X);
                right = Math.Max(right, run.Glyphs[fragment.Start].X + width);
            }
        left = Math.Min(left, inkLeft); top = Math.Min(top, inkTop); right = Math.Max(right, inkRight); bottom = Math.Max(bottom, inkBottom);
        return new(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top), default, false, null);

        void Include(SvgAffineTransform map, double x1, double y1, double x2, double y2, ref double l, ref double t, ref double r, ref double b)
        {
            for (int corner = 0; corner < 4; corner++)
            {
                work.Consume(token);
                SvgDevicePoint point = map.Apply((corner & 1) == 0 ? x1 : x2, (corner & 2) == 0 ? y1 : y2);
                if (!double.IsFinite(point.X) || !double.IsFinite(point.Y)) throw new SvgException(SvgErrorCode.LimitExceeded, "Text layout bounds exceed the numeric range.");
                l = Math.Min(l, point.X); t = Math.Min(t, point.Y); r = Math.Max(r, point.X); b = Math.Max(b, point.Y);
            }
        }
    }
}
