// Managed adaptation of Skia linear/radial unit matrices and shader seeds.
// Copyright 2008 The Android Open Source Project; 2012 Google Inc.; 2018 Google LLC.
// See THIRD_PARTY_NOTICES.txt.
using Lokad.Svg.Parsing;

namespace Lokad.Svg.Rendering;

/// <summary>Retains the shader-local matrix and the drawing-reference matrix without concatenating their translations.</summary>
internal readonly record struct SvgGradientPaintMatrices(SvgShaderMatrix Local, SvgAffineTransform Referencing);

/// <summary>Prepares native float gradient or image coordinates for one raster draw.</summary>
/// <remarks>Draw-local mutable state caches the owning bitmap's matrix without
/// allocating storage. Callers retain raster work accounting and cancellation.
/// Native centered radial and strictly inside zero-radius focal cones use float unit matrices.
/// Other conical maps and non-finite float maps keep the existing evaluator.
/// Instances must not be shared between concurrent raster draws.</remarks>
internal struct SvgShaderCoordinates
{
    private readonly SvgShaderMatrix _unit;
    private readonly SvgShaderMatrix _paintMap;
    private readonly SvgShaderMatrix? _localMatrix;
    private readonly bool _enabled;
    private readonly float _focalRadiusRatioInverse;
    private SvgCpuBitmapOrigin _origin;
    private SvgShaderMatrix _prepared;
    private bool _hasPrepared;
    private bool _canSample;

    private SvgShaderCoordinates(SvgShaderMatrix unit, SvgShaderMatrix paintMap, SvgShaderMatrix? localMatrix)
        : this(unit, paintMap, localMatrix, 0) { }

    private SvgShaderCoordinates(SvgShaderMatrix unit, SvgShaderMatrix paintMap, SvgShaderMatrix? localMatrix, float focalRadiusRatioInverse)
    {
        _unit = unit;
        _paintMap = paintMap;
        _localMatrix = localMatrix;
        _focalRadiusRatioInverse = focalRadiusRatioInverse;
        _enabled = unit.IsFinite && paintMap.IsFinite && paintMap.TryInvert(out _) && (localMatrix is null || localMatrix.Value.IsFinite);
    }

    internal static SvgShaderCoordinates CreateImage(SvgShaderMatrix paintMap, SvgShaderMatrix imageLocal)
        => new(new(1, 0, 0, 0, 1, 0), paintMap, imageLocal);

    internal static SvgShaderCoordinates CreateLinear(SvgLinearGradient gradient, SvgRectangle box, double horizontalScale, double verticalScale, double horizontalOffset, double verticalOffset)
        => CreateLinear(gradient, box, horizontalScale, verticalScale, horizontalOffset, verticalOffset, null);

    internal static SvgShaderCoordinates CreateLinear(SvgLinearGradient gradient, SvgRectangle box, double horizontalScale, double verticalScale, double horizontalOffset, double verticalOffset, SvgGradientPaintMatrices? textMatrices)
        => CreateLinear(gradient, box, horizontalScale, verticalScale, horizontalOffset, verticalOffset, textMatrices, SvgAffineTransform.Identity);

    internal static SvgShaderCoordinates CreateLinear(SvgLinearGradient gradient, SvgRectangle box, double horizontalScale, double verticalScale, double horizontalOffset, double verticalOffset, SvgGradientPaintMatrices? textMatrices, SvgAffineTransform sourcePlacement)
    {
        if (gradient.SourcePaint?.Data is { IsMapped: true } source)
        {
            SvgAffineTransform output = SvgAffineTransform.FromScaleOffset(horizontalScale, verticalScale, horizontalOffset, verticalOffset);
            if (sourcePlacement != SvgAffineTransform.Identity) output = output.ComposeChecked(sourcePlacement);
            SvgAffineTransform reference = output.ComposeChecked(textMatrices?.Referencing ?? source.Referencing);
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
    {
        if (gradient.FocalRadius != 0) return default;
        if (gradient.HasGeneralTransform && gradient.NativePaintMatrices is {} matrices)
        {
            float sx = (float)horizontalScale * (gradient.IsUserSpace ? 1 : (float)box.Width);
            float sy = (float)verticalScale * (gradient.IsUserSpace ? 1 : (float)box.Height);
            float tx = gradient.IsUserSpace ? (float)horizontalOffset : (float)box.X * (float)horizontalScale + (float)horizontalOffset;
            float ty = gradient.IsUserSpace ? (float)verticalOffset : (float)box.Y * (float)verticalScale + (float)verticalOffset;
            SvgAffineTransform output = SvgAffineTransform.FromScaleOffset(sx, sy, tx, ty);
            if (sourcePlacement != SvgAffineTransform.Identity) output = output.ComposeChecked(sourcePlacement);
            SvgAffineTransform reference = output.ComposeChecked(matrices.Referencing);
            SvgShaderMatrix ctm = new((float)reference.A, (float)reference.C, (float)reference.E, (float)reference.B, (float)reference.D, (float)reference.F);
            return Circle((float)gradient.CenterX, (float)gradient.CenterY, (float)gradient.Radius,
                (float)gradient.FocalX, (float)gradient.FocalY, ctm, matrices.Local);
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
            return Circle((float)gradient.CenterX, (float)gradient.CenterY, (float)gradient.Radius, (float)gradient.FocalX, (float)gradient.FocalY, paint, null);
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
        return Circle(cx, cy, radius, focalX, focalY, new(scaleX, 0, translateX, 0, scaleY, translateY), null);

        static SvgShaderCoordinates Circle(float x, float y, float radius, float fx, float fy, SvgShaderMatrix paint, SvgShaderMatrix? local)
        {
            if (radius <= 0 || !float.IsFinite(radius)) return default;
            if (fx != x || fy != y)
            {
                float dx = x - fx, dy = y - fy;
                float distance = MathF.Sqrt(dx * dx + dy * dy);
                float ratio = radius / distance;
                if (!float.IsFinite(ratio) || ratio <= 1 + 1f / 4096) return default;
                SvgShaderMatrix centers = new(dy, dx, fx, -dx, dy, fy);
                if (!centers.TryInvert(out SvgShaderMatrix inverseCenters)) return default;
                SvgShaderMatrix unitX = SvgShaderMatrix.Concat(new(0, 1, 0, -1, 0, 0), inverseCenters);
                float squareMinusOne = ratio * ratio - 1;
                float xScale = ratio / squareMinusOne, yScale = 1f / MathF.Sqrt(squareMinusOne);
                SvgShaderMatrix unit = new(unitX.ScaleX * xScale, unitX.SkewX * xScale, unitX.TranslateX * xScale,
                    unitX.SkewY * yScale, unitX.ScaleY * yScale, unitX.TranslateY * yScale);
                return new(unit, paint, local, 1f / ratio);
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

    internal bool TryRadial(int x, int y, SvgCpuBitmapOrigin origin, out double position)
    {
        position = 0;
        if (!TryMap(x, y, origin, out var point)) return false;
        // Keep separate float squares before the native radius stage.
        position = MathF.Sqrt(point.X * point.X + point.Y * point.Y) - point.X * _focalRadiusRatioInverse;
        return double.IsFinite(position);
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
