namespace Lokad.Svg.Fonts;

using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

internal sealed partial class SvgGlyphRasterSource
{
    /// <summary>Builds a glyph-local native stroke outline and retains its integer device placement.</summary>
    /// <remarks>Prepared font bytes remain borrowed. Decoding, source verbs and stroke construction share
    /// bounded work/cancellation and aggregate storage; the returned path is an exclusive owner.</remarks>
    internal SvgAuthoredPath CreateStrokePath(SvgTextStrokePaint paint, double scaleX, double offsetX, double scaleY, double offsetY,
        RenderBudget budget, ParserWorkCounter work, CancellationToken token)
    {
        SvgPreparedFont font = _font ?? throw new InvalidOperationException("The glyph source is disposed.");
        SvgStroke strokePaint = paint.Resolve() ?? throw new InvalidOperationException("A glyph stroke requires nonzero width.");
        SvgAffineTransform linear = SvgAffineTransform.FromMatrix(_glyphMap.A * scaleX, _glyphMap.B * scaleY, _glyphMap.C * scaleX, _glyphMap.D * scaleY, 0, 0);
        double fontScale = linear.TryGetUniformScale(out double uniform) ? uniform : 1;
        linear = SvgAffineTransform.FromMatrix(linear.A / fontScale, linear.B / fontScale, linear.C / fontScale, linear.D / fontScale, 0, 0);
        SvgGlyphStrikeTransform strike = SvgGlyphStrikeTransform.Create(_fontSize * fontScale, linear);
        linear = strike.RelaxedMap;
        if (!linear.TryInvert(out SvgAffineTransform inverse)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Text stroke placement must be invertible.");
        inverse = SvgAffineTransform.FromMatrix((float)inverse.A, (float)inverse.B, (float)inverse.C, (float)inverse.D, 0, 0);
        SvgDevicePoint origin = SvgNativePathStroke.MapPoint(_glyphMap, new(_glyph.X, _glyph.Y));
        float deviceX = (float)(origin.X * scaleX + offsetX), deviceY = (float)(origin.Y * scaleY + offsetY);
        float roundedX = MathF.Floor(deviceX * 4 + .5f) / 4;
        float roundedY = linear.B == 0 ? MathF.Floor(deviceY + .5f) : MathF.Floor(deviceY * 4 + .5f) / 4;
        float integerX = MathF.Floor(roundedX), integerY = MathF.Floor(roundedY);
        float phaseX = roundedX - integerX, phaseY = roundedY - integerY;
        bool windows = OperatingSystem.IsWindows();
        using OpenTypeGlyphContourStorage decoded = windows
            ? OpenTypeFont.GetGlyphContours(font.Bytes, _glyph.GlyphIndex, font.Metrics, budget, work, token)
            : linear.B == 0 && linear.C == 0
                ? OpenTypeFont.GetLatinLightContours(font.Bytes, _glyph.GlyphIndex, font.Metrics, strike.DeviceSize, budget, work, token)
                : OpenTypeFont.GetUnhintedDeviceContours(font.Bytes, _glyph.GlyphIndex, font.Metrics, strike.DeviceSize, budget, work, token);
        float pointScale = windows ? strike.DeviceSize / font.UnitsPerEm : 1f / 64;
        using BudgetedParserList<SvgAuthoredPathCommand> source = new(budget, SvgMemoryCategory.Scene, work, token);
        int first = 0;
        foreach (int end in decoded.ContourEnds)
        {
            work.Consume(token);
            source.Add(new(SvgAuthoredPathVerb.Move, SvgAuthoredArcFlags.None, Local(decoded.Segments[first].Start), default, default));
            for (int index = first; index < end; index++)
            {
                work.Consume(token);
                OpenTypeQuadraticSegment segment = decoded.Segments[index];
                SvgDevicePoint target = Local(segment.End);
                if (segment.Control == segment.End) source.Add(new(SvgAuthoredPathVerb.Line, SvgAuthoredArcFlags.None, target, default, default));
                else if (windows)
                {
                    const float twoThirds = 2f / 3;
                    const float oneThird = 1 - twoThirds;
                    SvgDevicePoint a = Device(segment.Start), b = Device(segment.Control), c = Device(segment.End);
                    SvgDevicePoint firstControl = Unmap(new((float)a.X * oneThird + (float)b.X * twoThirds, (float)a.Y * oneThird + (float)b.Y * twoThirds));
                    SvgDevicePoint secondControl = Unmap(new((float)c.X * oneThird + (float)b.X * twoThirds, (float)c.Y * oneThird + (float)b.Y * twoThirds));
                    source.Add(new(SvgAuthoredPathVerb.Cubic, SvgAuthoredArcFlags.None, firstControl, secondControl, target));
                }
                else source.Add(new(SvgAuthoredPathVerb.Quadratic, SvgAuthoredArcFlags.None, Local(segment.Control), target, default));
            }
            source.Add(new(SvgAuthoredPathVerb.Close, SvgAuthoredArcFlags.None, default, default, default));
            first = end;
        }
        using BudgetedParserList<SvgPoint> points = new(budget, SvgMemoryCategory.Geometry, work, token);
        using BudgetedParserList<int> ends = new(budget, SvgMemoryCategory.Geometry, work, token);
        using SvgOrthogonalGeometryLowering lowering = new(points, ends, "text", true, false, int.MaxValue, budget, work, token);
        SvgParsedGeometry stroke = lowering.LowerNativeStrokeCommands(source, linear, strokePaint.Width * fontScale, strokePaint.Width * fontScale,
            SvgStrokeStyleSyntax.ParseMiterLimit(paint.Style.Miterlimit), SvgStrokeCap.Butt,
            SvgStrokeStyleSyntax.ParseLinejoin(paint.Style.Linejoin, true), null, 0, null, false, strokePaint.Color, true,
            1, SvgAffineTransform.Identity, null, budget, SvgAuthoredStrokeShape.Path);
        try
        {
            SvgAuthoredPath outline = stroke.Geometries[0].AuthoredPath?.StrokeOutline ?? throw new InvalidOperationException("A glyph stroke requires a retained outline.");
            using BudgetedParserList<SvgAuthoredPathCommand> retained = new(budget, SvgMemoryCategory.Scene, work, token);
            foreach (SvgAuthoredPathCommand command in outline.Commands) retained.Add(command);
            OwnedParserArray<SvgAuthoredPathCommand> owner = retained.Detach();
            try { return SvgAuthoredPath.Create(owner, SvgAffineTransform.FromScaleOffset(1, 1, integerX, integerY).ComposeChecked(outline.SceneMap), budget); }
            catch { owner.Dispose(); throw; }
        }
        finally { stroke.Discard(); stroke.Dispose(); }

        SvgDevicePoint Device(OpenTypeOutlinePoint point) => SvgNativePathStroke.MapPoint(strike.RemainingMap, new((float)point.X * pointScale, -(float)point.Y * pointScale));
        SvgDevicePoint Unmap(SvgDevicePoint point) => SvgNativePathStroke.MapPoint(inverse, new((float)point.X + phaseX, (float)point.Y + phaseY));
        SvgDevicePoint Local(OpenTypeOutlinePoint point) => Unmap(Device(point));
    }
}
