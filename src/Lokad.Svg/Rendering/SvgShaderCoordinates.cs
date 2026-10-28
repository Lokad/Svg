// Managed adaptation of Skia linear/radial unit matrices and shader seeds.
// Copyright 2008 The Android Open Source Project; 2012 Google Inc.; 2018 Google LLC.
// See THIRD_PARTY_NOTICES.txt.
using Lokad.Svg.Parsing;

namespace Lokad.Svg.Rendering;

/// <summary>Retains the shader-local matrix and the drawing-reference matrix without concatenating their translations.</summary>
internal readonly record struct SvgGradientPaintMatrices(SvgShaderMatrix Local, SvgAffineTransform Referencing);

/// <summary>Carries a radial coordinate and the native cone's visibility mask before spreading.</summary>
/// <remarks>Visible positions may include positive infinity at a cone boundary. Callers apply
/// the mask before sampling the ramp and retain coverage, work, cancellation and owner lifetimes.</remarks>
internal readonly record struct SvgRadialShaderSample(double Position, bool IsVisible);

/// <summary>Prepares native float gradient or image coordinates for one raster draw.</summary>
/// <remarks>Draw-local mutable state caches the owning bitmap's matrix without
/// allocating storage. Callers retain raster work accounting and cancellation.
/// Native centered radial, contained focal circles and foci on the circle use float unit matrices.
/// Other conical maps and non-finite float maps keep the existing evaluator.
/// Instances must not be shared between concurrent raster draws.</remarks>
internal struct SvgShaderCoordinates
{
    private readonly SvgShaderMatrix _unit;
    private readonly SvgShaderMatrix _paintMap;
    private readonly SvgShaderMatrix? _localMatrix;
    private readonly bool _enabled;
    private readonly float _focalRadiusRatioInverse;
    private readonly float _radialPositionScale;
    private readonly float _radialPositionBias;
    private readonly bool _focalOnCircle;
    private SvgCpuBitmapOrigin _origin;
    private SvgShaderMatrix _prepared;
    private bool _hasPrepared;
    private bool _canSample;

    internal readonly bool MasksRadialSamples => _focalOnCircle;

    private SvgShaderCoordinates(SvgShaderMatrix unit, SvgShaderMatrix paintMap, SvgShaderMatrix? localMatrix)
        : this(unit, paintMap, localMatrix, 0) { }

    private SvgShaderCoordinates(SvgShaderMatrix unit, SvgShaderMatrix paintMap, SvgShaderMatrix? localMatrix, float focalRadiusRatioInverse)
        : this(unit, paintMap, localMatrix, focalRadiusRatioInverse, 1, 0) { }

    private SvgShaderCoordinates(SvgShaderMatrix unit, SvgShaderMatrix paintMap, SvgShaderMatrix? localMatrix, float focalRadiusRatioInverse, float radialPositionScale, float radialPositionBias)
        : this(unit, paintMap, localMatrix, focalRadiusRatioInverse, radialPositionScale, radialPositionBias, false) { }

    private SvgShaderCoordinates(SvgShaderMatrix unit, SvgShaderMatrix paintMap, SvgShaderMatrix? localMatrix, float focalRadiusRatioInverse, float radialPositionScale, float radialPositionBias, bool focalOnCircle)
    {
        _unit = unit;
        _paintMap = paintMap;
        _localMatrix = localMatrix;
        _focalRadiusRatioInverse = focalRadiusRatioInverse;
        _radialPositionScale = radialPositionScale;
        _radialPositionBias = radialPositionBias;
        _focalOnCircle = focalOnCircle;
        _enabled = float.IsFinite(radialPositionScale) && float.IsFinite(radialPositionBias) && unit.IsFinite && paintMap.IsFinite && paintMap.TryInvert(out _) && (localMatrix is null || localMatrix.Value.IsFinite);
    }

    /// <summary>Classifies the native factory from borrowed definition coordinates before local or instance placement.</summary>
    /// <remarks>Allocation-free bounded float arithmetic; callers own work and cancellation at draw/record traversal.</remarks>
    internal static bool IsConstantFactory(SvgLinearGradient gradient)
    {
        double x1 = gradient.X1, y1 = gradient.Y1, x2 = gradient.X2, y2 = gradient.Y2;
        if (gradient.SourcePaint is {} paint)
        {
            SvgLinearPaintMapData source = paint.Data;
            x1 = source.X1; y1 = source.Y1; x2 = source.X2; y2 = source.Y2;
        }
        float dx = (float)x2 - (float)x1, dy = (float)y2 - (float)y1;
        return MathF.Sqrt(dx * dx + dy * dy) <= 1f / 32768;
    }

    /// <summary>Classifies a native constant conical factory independently of spatial mapping and cone sampling.</summary>
    /// <remarks>Borrowed definition metadata is immutable. The caller owns bounded traversal work and cancellation.</remarks>
    internal static bool IsConstantFactory(SvgRadialGradient gradient)
    {
        double x = gradient.CenterX, y = gradient.CenterY, radius = gradient.Radius;
        double fx = gradient.FocalX, fy = gradient.FocalY, focalRadius = gradient.FocalRadius;
        if (gradient.SourceDefinition is {} source)
        {
            x = source.CenterX; y = source.CenterY; radius = source.Radius;
            fx = source.HasFocalX ? source.FocalX : x;
            fy = source.HasFocalY ? source.FocalY : y;
            focalRadius = source.HasFocalRadius ? source.FocalRadius : 0;
        }
        float dx = (float)x - (float)fx, dy = (float)y - (float)fy;
        float outer = (float)radius, inner = (float)focalRadius;
        const float threshold = 1f / 32768;
        return outer >= 0 && inner >= 0 && MathF.Sqrt(dx * dx + dy * dy) <= threshold
            && MathF.Abs(outer - inner) <= threshold && (gradient.Spread != SvgGradientSpread.Pad || outer <= threshold);
    }

    internal static SvgShaderCoordinates CreateImage(SvgShaderMatrix paintMap, SvgShaderMatrix imageLocal)
        => new(new(1, 0, 0, 0, 1, 0), paintMap, imageLocal);

    internal static SvgShaderCoordinates CreateLinear(SvgLinearGradient gradient, SvgRectangle box, double horizontalScale, double verticalScale, double horizontalOffset, double verticalOffset)
        => CreateLinear(gradient, box, horizontalScale, verticalScale, horizontalOffset, verticalOffset, null);

    internal static SvgShaderCoordinates CreateLinear(SvgLinearGradient gradient, SvgRectangle box, double horizontalScale, double verticalScale, double horizontalOffset, double verticalOffset, SvgGradientPaintMatrices? textMatrices)
        => CreateLinear(gradient, box, horizontalScale, verticalScale, horizontalOffset, verticalOffset, textMatrices, SvgAffineTransform.Identity);

    internal static SvgShaderCoordinates CreateLinear(SvgLinearGradient gradient, SvgRectangle box, double horizontalScale, double verticalScale, double horizontalOffset, double verticalOffset, SvgGradientPaintMatrices? textMatrices, SvgAffineTransform sourcePlacement)
        => CreateLinear(gradient, box, horizontalScale, verticalScale, horizontalOffset, verticalOffset, textMatrices, sourcePlacement, null);

    internal static SvgShaderCoordinates CreateLinear(SvgLinearGradient gradient, SvgRectangle box, double horizontalScale, double verticalScale, double horizontalOffset, double verticalOffset, SvgGradientPaintMatrices? textMatrices, SvgAffineTransform sourcePlacement, SvgAffineTransform? nativeReference)
    {
        if (gradient.SourcePaint?.Data is { IsMapped: true } source)
        {
            SvgAffineTransform output = SvgAffineTransform.FromScaleOffset(horizontalScale, verticalScale, horizontalOffset, verticalOffset);
            if (sourcePlacement != SvgAffineTransform.Identity) output = output.ComposeChecked(sourcePlacement);
            SvgAffineTransform reference = nativeReference ?? output.ComposeChecked(textMatrices?.Referencing ?? source.Referencing);
            SvgShaderMatrix paint = new((float)reference.A, (float)reference.C, (float)reference.E, (float)reference.B, (float)reference.D, (float)reference.F);
            SvgShaderMatrix local = textMatrices?.Local ?? new((float)source.Local.A, (float)source.Local.C, (float)source.Local.E, (float)source.Local.B, (float)source.Local.D, (float)source.Local.F);
            return Linear((float)source.X1, (float)source.Y1, (float)source.X2, (float)source.Y2, paint, local);
        }
        float sx = (float)(gradient.IsUserSpace ? gradient.ReferenceScaleX : box.Width) * (float)horizontalScale;
        float sy = (float)(gradient.IsUserSpace ? gradient.ReferenceScaleY : box.Height) * (float)verticalScale;
        float tx = gradient.IsUserSpace ? (float)horizontalOffset : (float)box.X * (float)horizontalScale + (float)horizontalOffset;
        float ty = gradient.IsUserSpace ? (float)verticalOffset : (float)box.Y * (float)verticalScale + (float)verticalOffset;
        float x1 = (float)(gradient.IsUserSpace ? gradient.X1 / gradient.ReferenceScaleX : gradient.X1);
        float y1 = (float)(gradient.IsUserSpace ? gradient.Y1 / gradient.ReferenceScaleY : gradient.Y1);
        float x2 = (float)(gradient.IsUserSpace ? gradient.X2 / gradient.ReferenceScaleX : gradient.X2);
        float y2 = (float)(gradient.IsUserSpace ? gradient.Y2 / gradient.ReferenceScaleY : gradient.Y2);
        return Linear(x1, y1, x2, y2, new(sx, 0, tx, 0, sy, ty), null);

        static SvgShaderCoordinates Linear(float x1, float y1, float x2, float y2, SvgShaderMatrix paint, SvgShaderMatrix? local)
        {
            float dx = x2 - x1, dy = y2 - y1;
            float square = dx * dx + dy * dy;
            float length = float.IsFinite(square) ? MathF.Sqrt(square) : (float)Math.Sqrt((double)dx * dx + (double)dy * dy);
            if (length == 0) return default;
            float inverse = 1f / length;
            float cosine = dx * inverse, sine = -dy * inverse;
            float oneMinusCosine = 1 - cosine;
            float biasX = (sine * y1 + oneMinusCosine * x1 - x1) * inverse;
            float biasY = (-sine * x1 + oneMinusCosine * y1 - y1) * inverse;
            return new(new(cosine * inverse, -sine * inverse, biasX, sine * inverse, cosine * inverse, biasY), paint, local);
        }
    }

    internal static SvgShaderCoordinates CreateRadial(SvgRadialGradient gradient, SvgRectangle box, double horizontalScale, double verticalScale, double horizontalOffset, double verticalOffset)
        => CreateRadial(gradient, box, horizontalScale, verticalScale, horizontalOffset, verticalOffset, SvgAffineTransform.Identity);

    internal static SvgShaderCoordinates CreateRadial(SvgRadialGradient gradient, SvgRectangle box, double horizontalScale, double verticalScale, double horizontalOffset, double verticalOffset, SvgAffineTransform sourcePlacement)
        => CreateRadial(gradient, box, horizontalScale, verticalScale, horizontalOffset, verticalOffset, sourcePlacement, null);

    internal static SvgShaderCoordinates CreateRadial(SvgRadialGradient gradient, SvgRectangle box, double horizontalScale, double verticalScale, double horizontalOffset, double verticalOffset, SvgAffineTransform sourcePlacement, SvgAffineTransform? nativeReference)
    {
        if (gradient.HasGeneralTransform && gradient.NativePaintMatrices is {} matrices)
        {
            float sx = (float)horizontalScale * (gradient.IsUserSpace ? 1 : (float)box.Width);
            float sy = (float)verticalScale * (gradient.IsUserSpace ? 1 : (float)box.Height);
            float tx = gradient.IsUserSpace ? (float)horizontalOffset : (float)box.X * (float)horizontalScale + (float)horizontalOffset;
            float ty = gradient.IsUserSpace ? (float)verticalOffset : (float)box.Y * (float)verticalScale + (float)verticalOffset;
            SvgAffineTransform output = SvgAffineTransform.FromScaleOffset(sx, sy, tx, ty);
            if (sourcePlacement != SvgAffineTransform.Identity) output = output.ComposeChecked(sourcePlacement);
            SvgAffineTransform reference = nativeReference ?? output.ComposeChecked(matrices.Referencing);
            SvgShaderMatrix ctm = new((float)reference.A, (float)reference.C, (float)reference.E, (float)reference.B, (float)reference.D, (float)reference.F);
            return Circle((float)gradient.CenterX, (float)gradient.CenterY, (float)gradient.Radius,
                (float)gradient.FocalX, (float)gradient.FocalY, (float)gradient.FocalRadius, ctm, matrices.Local);
        }
        if (gradient.HasGeneralTransform)
        {
            if (!gradient.Inverse.TryInvert(out SvgGradientTransform forward)) return default;
            float sx = (float)horizontalScale * (gradient.IsUserSpace ? 1 : (float)box.Width);
            float sy = (float)verticalScale * (gradient.IsUserSpace ? 1 : (float)box.Height);
            float tx = gradient.IsUserSpace ? (float)horizontalOffset : (float)box.X * (float)horizontalScale + (float)horizontalOffset;
            float ty = gradient.IsUserSpace ? (float)verticalOffset : (float)box.Y * (float)verticalScale + (float)verticalOffset;
            SvgShaderMatrix paint = new(sx * (float)forward.A, sx * (float)forward.C, sx * (float)forward.E + tx,
                sy * (float)forward.B, sy * (float)forward.D, sy * (float)forward.F + ty);
            return Circle((float)gradient.CenterX, (float)gradient.CenterY, (float)gradient.Radius, (float)gradient.FocalX, (float)gradient.FocalY, (float)gradient.FocalRadius, paint, null);
        }
        float scaleX = (float)(gradient.IsUserSpace ? gradient.ReferenceScaleX : box.Width) * (float)horizontalScale;
        float scaleY = (float)(gradient.IsUserSpace ? gradient.ReferenceScaleY : box.Height) * (float)verticalScale;
        float translateX = gradient.IsUserSpace ? (float)horizontalOffset : (float)box.X * (float)horizontalScale + (float)horizontalOffset;
        float translateY = gradient.IsUserSpace ? (float)verticalOffset : (float)box.Y * (float)verticalScale + (float)verticalOffset;
        float cx = (float)(gradient.IsUserSpace ? gradient.CenterX / gradient.ReferenceScaleX : gradient.CenterX);
        float cy = (float)(gradient.IsUserSpace ? gradient.CenterY / gradient.ReferenceScaleY : gradient.CenterY);
        float radius = (float)(gradient.IsUserSpace ? gradient.Radius / Math.Abs(gradient.ReferenceScaleX) : gradient.Radius);
        float focalX = (float)(gradient.IsUserSpace ? gradient.FocalX / gradient.ReferenceScaleX : gradient.FocalX);
        float focalY = (float)(gradient.IsUserSpace ? gradient.FocalY / gradient.ReferenceScaleY : gradient.FocalY);
        float focalRadius = (float)(gradient.IsUserSpace ? gradient.FocalRadius / Math.Abs(gradient.ReferenceScaleX) : gradient.FocalRadius);
        return Circle(cx, cy, radius, focalX, focalY, focalRadius, new(scaleX, 0, translateX, 0, scaleY, translateY), null);

        static SvgShaderCoordinates Circle(float x, float y, float radius, float fx, float fy, float focalRadius, SvgShaderMatrix paint, SvgShaderMatrix? local)
        {
            const float nearZero = 1f / 4096;
            if (radius <= 0 || !float.IsFinite(radius) || focalRadius < 0 || focalRadius >= radius) return default;
            float dx = x - fx, dy = y - fy;
            float distance = MathF.Sqrt(dx * dx + dy * dy);
            if (focalRadius == 0 && distance <= nearZero)
            {
                // The factory reduction uses the focus; the later concentric reduction uses the outer center.
                if (distance <= 1f / 32768) { x = fx; y = fy; }
                fx = x; fy = y;
            }
            if (focalRadius != 0)
            {
                if (distance <= nearZero)
                {
                    float span = radius - focalRadius;
                    if (span <= nearZero) return default;
                    float centeredInverse = 1f / radius;
                    return new(new(centeredInverse, 0, -x * centeredInverse, 0, centeredInverse, -y * centeredInverse), paint, local,
                        0, radius / span, -focalRadius / span);
                }
            }
            if (fx != x || fy != y)
            {
                float ratio = radius / distance;
                float focal = 0, focalSpan = 1;
                if (focalRadius != 0)
                {
                    float inner = focalRadius / distance;
                    focal = inner / (inner - ratio);
                    focalSpan = MathF.Abs(1 - focal);
                    ratio /= focalSpan;
                }
                bool focalOnCircle = MathF.Abs(ratio - 1) <= nearZero;
                if (!float.IsFinite(ratio) || ratio < 1 && !focalOnCircle) return default;
                SvgShaderMatrix centers = new(dy, dx, fx, -dx, dy, fy);
                if (!centers.TryInvert(out SvgShaderMatrix inverseCenters)) return default;
                SvgShaderMatrix unitX = SvgShaderMatrix.Concat(new(0, 1, 0, -1, 0, 0), inverseCenters);
                if (focalRadius != 0)
                {
                    float span = 1 - focal;
                    SvgShaderMatrix focalCenters = new(0, span, focal, -span, 0, 0);
                    if (!focalCenters.TryInvert(out SvgShaderMatrix inverseFocal)) return default;
                    SvgShaderMatrix focalMap = SvgShaderMatrix.Concat(new(0, 1, 0, -1, 0, 0), inverseFocal);
                    unitX = SvgShaderMatrix.Concat(focalMap, unitX);
                }
                float squareMinusOne = ratio * ratio - 1;
                float xScale = focalOnCircle ? .5f : ratio / squareMinusOne;
                float yScale = focalOnCircle ? .5f : 1f / MathF.Sqrt(squareMinusOne);
                SvgShaderMatrix unit = new(unitX.ScaleX * xScale, unitX.SkewX * xScale, unitX.TranslateX * xScale,
                    unitX.SkewY * yScale, unitX.ScaleY * yScale, unitX.TranslateY * yScale);
                if (focalRadius != 0)
                    unit = new(unit.ScaleX * focalSpan, unit.SkewX * focalSpan, unit.TranslateX * focalSpan,
                        unit.SkewY * focalSpan, unit.ScaleY * focalSpan, unit.TranslateY * focalSpan);
                return new(unit, paint, local, 1f / ratio, 1, MathF.Abs(focal) <= nearZero ? 0 : focal, focalOnCircle);
            }
            float inverse = 1f / radius;
            return new(new(inverse, 0, -x * inverse, 0, inverse, -y * inverse), paint, local);
        }
    }

    /// <summary>Evaluates the admitted two-circle fallback after the caller maps into gradient coordinates.</summary>
    /// <remarks>Allocation-free scalar arithmetic shared by geometry and glyph paints. The caller charges
    /// sampling and observes cancellation. Native float dispatch stays with each paint consumer.</remarks>
    internal static double EvaluateRadial(SvgDevicePoint ray, SvgDevicePoint offset, double radius, double focalRadius)
    {
        if (offset.X == 0 && offset.Y == 0)
            return (Math.Sqrt(ray.X * ray.X + ray.Y * ray.Y) - focalRadius) / (radius - focalRadius);
        if (ray.X == 0 && ray.Y == 0) return 0;
        double dot = ray.X * offset.X + ray.Y * offset.Y;
        double raySquared = ray.X * ray.X + ray.Y * ray.Y;
        if (focalRadius == 0)
        {
            double discriminant = dot * dot + raySquared * (radius * radius - offset.X * offset.X - offset.Y * offset.Y);
            if (discriminant < 0) discriminant = 0;
            return raySquared / (dot + Math.Sqrt(discriminant));
        }
        double span = radius - focalRadius;
        double linear = dot + focalRadius * span;
        double quadratic = offset.X * offset.X + offset.Y * offset.Y - span * span;
        double constant = raySquared - focalRadius * focalRadius;
        double coneDiscriminant = linear * linear - quadratic * constant;
        if (coneDiscriminant < 0) coneDiscriminant = 0;
        return (linear - Math.Sqrt(coneDiscriminant)) / quadratic;
    }

    internal bool TryLinear(int x, int y, SvgCpuBitmapOrigin origin, out double position)
    {
        position = 0;
        if (!TryMap(x, y, origin, out var point)) return false;
        position = point.X;
        return double.IsFinite(position);
    }

    internal bool TryRadial(int x, int y, SvgCpuBitmapOrigin origin, out SvgRadialShaderSample sample)
    {
        sample = default;
        if (!TryMap(x, y, origin, out var point)) return false;
        // Keep separate float squares before the native radius stage.
        float radial = _focalOnCircle ? point.X + point.Y * point.Y / point.X
            : MathF.Sqrt(point.X * point.X + point.Y * point.Y) - point.X * _focalRadiusRatioInverse;
        if (_focalOnCircle && (radial <= 0 || float.IsNaN(radial))) return true;
        double position = _radialPositionScale == 1 && _radialPositionBias == 0 ? radial
            : MathF.FusedMultiplyAdd(radial, _radialPositionScale, _radialPositionBias);
        sample = new(position, true);
        return _focalOnCircle || double.IsFinite(position);
    }

    internal bool TryMap(int x, int y, SvgCpuBitmapOrigin origin, out (float X, float Y) point)
    {
        point = default;
        if (!TryGetMatrix(origin, out SvgShaderMatrix matrix)) return false;
        point = matrix.Map(x - origin.X + .5f, y - origin.Y + .5f);
        return float.IsFinite(point.X) && float.IsFinite(point.Y);
    }

    /// <summary>Returns the existing bitmap-local inverse for samplers with a distinct native coordinate discipline.</summary>
    internal bool TryGetMatrix(SvgCpuBitmapOrigin origin, out SvgShaderMatrix matrix)
    {
        matrix = default;
        if (!_enabled) return false;
        if (!_hasPrepared || origin != _origin)
        {
            _origin = origin;
            _hasPrepared = true;
            SvgShaderMatrix local = _paintMap with { TranslateX = _paintMap.TranslateX - origin.X, TranslateY = _paintMap.TranslateY - origin.Y };
            if (_localMatrix is {} gradientLocal) local = SvgShaderMatrix.Concat(local, gradientLocal);
            _canSample = local.TryInvert(out SvgShaderMatrix inverse);
            if (_canSample)
            {
                _prepared = SvgShaderMatrix.Concat(_unit, inverse);
                _canSample = _prepared.IsFinite;
            }
        }
        if (!_canSample) return false;
        matrix = _prepared;
        return true;
    }
}
