// Adapts pinned SkDWriteGeometrySink.cpp and SkFloatUtils.h.
// Copyright 2012 Google Inc. See THIRD_PARTY_NOTICES.txt.
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
        using RenderBudget.BudgetReservation? dashOwner = paint.Style.Dasharray is null ? null : budget.Reserve(192, SvgMemoryCategory.Geometry);
        double[]? dashes = SvgStrokeStyleSyntax.ParseDasharray(paint.Style.Dasharray);
        if (dashes is not null)
        {
            bool hasLength = false;
            foreach (double interval in dashes) { work.Consume(token); hasLength |= interval != 0; }
            // The font paint's invalid empty dash effect falls back to its solid outline.
            if (!hasLength) dashes = null;
        }
        double strokeScale = paint.NonScaling ? 1 / paint.FontScale : 1;
        if (!double.IsFinite(strokeScale) || strokeScale <= 0) throw new SvgException(SvgErrorCode.LimitExceeded, "Text stroke scaling exceeds the device range.");
        SvgAffineTransform linear = SvgAffineTransform.FromMatrix(_glyphMap.A * scaleX, _glyphMap.B * scaleY, _glyphMap.C * scaleX, _glyphMap.D * scaleY, 0, 0);
        double fontScale = paint.FontScale;
        linear = SvgAffineTransform.FromMatrix(linear.A / fontScale, linear.B / fontScale, linear.C / fontScale, linear.D / fontScale, 0, 0);
        float effectiveSize = SvgGlyphDeviceMetrics.GetEffectiveFontSize(_fontSize * fontScale);
        if (effectiveSize == 0) return SvgAuthoredPath.Create(new([], null), SvgAffineTransform.Identity, budget);
        SvgGlyphStrikeTransform strike = SvgGlyphStrikeTransform.Create(effectiveSize, linear);
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
                // Measure dashes on retained TrueType quadratics. The Windows
                // sink recovers quadratic controls from its rounded cubics.
                else if (windows && dashes is null)
                {
                    const float oneThird = 1f / 3;
                    const float twoThirds = 1 - oneThird;
                    // DirectWrite constructs cubics before Skia maps the outline.
                    // Preserve that float order in solid outline construction.
                    SvgDevicePoint a = Scaled(segment.Start), b = Scaled(segment.Control), c = Scaled(segment.End);
                    SvgDevicePoint firstControl = new((float)a.X * oneThird + (float)b.X * twoThirds, (float)a.Y * oneThird + (float)b.Y * twoThirds);
                    SvgDevicePoint secondControl = new((float)c.X * oneThird + (float)b.X * twoThirds, (float)c.Y * oneThird + (float)b.Y * twoThirds);
                    if (TryRecoverQuadratic(a, firstControl, secondControl, c, out SvgDevicePoint quadratic))
                        source.Add(new(SvgAuthoredPathVerb.Quadratic, SvgAuthoredArcFlags.None, Unmap(SvgNativePathStroke.MapPoint(strike.RemainingMap, quadratic)), target, default));
                    else
                        source.Add(new(SvgAuthoredPathVerb.Cubic, SvgAuthoredArcFlags.None, Unmap(SvgNativePathStroke.MapPoint(strike.RemainingMap, firstControl)), Unmap(SvgNativePathStroke.MapPoint(strike.RemainingMap, secondControl)), target));
                }
                else source.Add(new(SvgAuthoredPathVerb.Quadratic, SvgAuthoredArcFlags.None, Local(segment.Control), target, default));
            }
            source.Add(new(SvgAuthoredPathVerb.Close, SvgAuthoredArcFlags.None, default, default, default));
            first = end;
        }
        using BudgetedParserList<SvgPoint> points = new(budget, SvgMemoryCategory.Geometry, work, token);
        using BudgetedParserList<int> ends = new(budget, SvgMemoryCategory.Geometry, work, token);
        using SvgOrthogonalGeometryLowering lowering = new(points, ends, "text", true, false, int.MaxValue, budget, work, token);
        if (dashes is not null)
            for (int index = 0; index < dashes.Length; index++) { work.Consume(token); dashes[index] *= fontScale * strokeScale; }
        double width = strokePaint.Width * fontScale * strokeScale;
        double dashOffset = SvgStrokeStyleSyntax.ParseDashoffset(paint.Style.Dashoffset) * fontScale * strokeScale;
        SvgParsedGeometry stroke = lowering.LowerNativeStrokeCommands(source, linear, width, width,
            SvgStrokeStyleSyntax.ParseMiterLimit(paint.Style.Miterlimit), SvgStrokeStyleSyntax.ParseLinecap(paint.Style.Linecap),
            SvgStrokeStyleSyntax.ParseLinejoin(paint.Style.Linejoin, true), dashes, dashOffset, null, false, strokePaint.Color, true,
            1, SvgAffineTransform.Identity, null, budget, SvgAuthoredStrokeShape.Path, SvgNativeStrokeResolution.GlyphStrike);
        try
        {
            if (stroke.Geometries.Length == 0) return SvgAuthoredPath.Create(new([], null), SvgAffineTransform.Identity, budget);
            SvgAuthoredPath outline = stroke.Geometries[0].AuthoredPath?.StrokeOutline ?? throw new InvalidOperationException("A glyph stroke requires a retained outline.");
            using BudgetedParserList<SvgAuthoredPathCommand> retained = new(budget, SvgMemoryCategory.Scene, work, token);
            foreach (SvgAuthoredPathCommand command in outline.Commands) retained.Add(command);
            OwnedParserArray<SvgAuthoredPathCommand> owner = retained.Detach();
            try { return SvgAuthoredPath.Create(owner, SvgAffineTransform.FromScaleOffset(1, 1, integerX, integerY).ComposeChecked(outline.SceneMap), budget); }
            catch { owner.Dispose(); throw; }
        }
        finally { stroke.Discard(); stroke.Dispose(); }

        SvgDevicePoint Scaled(OpenTypeOutlinePoint point) => new((float)point.X * pointScale, -(float)point.Y * pointScale);
        SvgDevicePoint Device(OpenTypeOutlinePoint point) => SvgNativePathStroke.MapPoint(strike.RemainingMap, Scaled(point));
        SvgDevicePoint Unmap(SvgDevicePoint point) => SvgNativePathStroke.MapPoint(inverse, new((float)point.X + phaseX, (float)point.Y + phaseY));
        SvgDevicePoint Local(OpenTypeOutlinePoint point) => Unmap(Device(point));

        static bool TryRecoverQuadratic(SvgDevicePoint a, SvgDevicePoint b, SvgDevicePoint c, SvgDevicePoint d, out SvgDevicePoint control)
        {
            float x = (float)a.X + ((float)b.X - (float)a.X) * 3 / 2;
            float otherX = ((float)c.X - (float)d.X) * 3 / 2 + (float)d.X;
            float y = (float)a.Y + ((float)b.Y - (float)a.Y) * 3 / 2;
            float otherY = ((float)c.Y - (float)d.Y) * 3 / 2 + (float)d.Y;
            control = new(x, y);
            return Near(x, otherX) && Near(y, otherY);

            static bool Near(float first, float second)
                => !float.IsNaN(first) && !float.IsNaN(second) && Math.Abs(Biased(first) - Biased(second)) <= 10;

            static long Biased(float value)
            {
                uint bits = BitConverter.SingleToUInt32Bits(value);
                long magnitude = bits & 0x7FFFFFFFu;
                return 0x80000000L + ((bits & 0x80000000u) == 0 ? magnitude : -magnitude);
            }
        }
    }
}
