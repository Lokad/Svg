// Managed adaptation of Skia linear/radial unit matrices and shader seeds.
// Copyright 2008 The Android Open Source Project; 2012 Google Inc.; 2018 Google LLC.
// See THIRD_PARTY_NOTICES.txt.
using Lokad.Svg.Parsing;

namespace Lokad.Svg.Rendering;

/// <summary>Retains the shader-local matrix and the drawing-reference matrix without concatenating their translations.</summary>
internal readonly record struct SvgGradientPaintMatrices(SvgShaderMatrix Local, SvgShaderMatrix Referencing);

/// <summary>Prepares native float gradient coordinates for one raster draw.</summary>
/// <remarks>Draw-local mutable state caches the owning bitmap's matrix without
/// allocating storage. Callers retain raster work accounting and cancellation.
/// Native centered radial and strictly inside zero-radius focal cones use float unit matrices.
/// Other conical maps and non-finite float maps keep the existing evaluator.
/// Instances must not be shared between concurrent raster draws.</remarks>
internal struct SvgGradientCoordinates
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

    private SvgGradientCoordinates(SvgShaderMatrix unit, SvgShaderMatrix paintMap) : this(unit, paintMap, null) { }

    private SvgGradientCoordinates(SvgShaderMatrix unit, SvgShaderMatrix paintMap, SvgShaderMatrix? localMatrix)
        : this(unit, paintMap, localMatrix, 0) { }

    private SvgGradientCoordinates(SvgShaderMatrix unit, SvgShaderMatrix paintMap, SvgShaderMatrix? localMatrix, float focalRadiusRatioInverse)
    {
        _unit = unit;
        _paintMap = paintMap;
        _localMatrix = localMatrix;
        _focalRadiusRatioInverse = focalRadiusRatioInverse;
        _enabled = unit.IsFinite && paintMap.IsFinite && paintMap.TryInvert(out _) && (localMatrix is null || localMatrix.Value.IsFinite);
    }

    internal static SvgGradientCoordinates CreateLinear(SvgLinearGradient gradient, SvgRectangle box, double horizontalScale, double verticalScale, double horizontalOffset, double verticalOffset)
    {
        float sx = (float)(gradient.IsUserSpace ? gradient.ReferenceScaleX : box.Width) * (float)horizontalScale;
        float sy = (float)(gradient.IsUserSpace ? gradient.ReferenceScaleY : box.Height) * (float)verticalScale;
        float tx = gradient.IsUserSpace ? (float)horizontalOffset : (float)box.X * (float)horizontalScale + (float)horizontalOffset;
        float ty = gradient.IsUserSpace ? (float)verticalOffset : (float)box.Y * (float)verticalScale + (float)verticalOffset;
        float x1 = (float)(gradient.IsUserSpace ? gradient.X1 / gradient.ReferenceScaleX : gradient.X1);
        float y1 = (float)(gradient.IsUserSpace ? gradient.Y1 / gradient.ReferenceScaleY : gradient.Y1);
        float x2 = (float)(gradient.IsUserSpace ? gradient.X2 / gradient.ReferenceScaleX : gradient.X2);
        float y2 = (float)(gradient.IsUserSpace ? gradient.Y2 / gradient.ReferenceScaleY : gradient.Y2);
        float dx = x2 - x1, dy = y2 - y1;
        float square = dx * dx + dy * dy;
        float length = float.IsFinite(square) ? MathF.Sqrt(square) : (float)Math.Sqrt((double)dx * dx + (double)dy * dy);
        if (length == 0) return default;
        float inverse = 1f / length;
        float cosine = dx * inverse, sine = -dy * inverse;
        float oneMinusCosine = 1 - cosine;
        float biasX = (sine * y1 + oneMinusCosine * x1 - x1) * inverse;
        float biasY = (-sine * x1 + oneMinusCosine * y1 - y1) * inverse;
        return new(new(cosine * inverse, -sine * inverse, biasX, sine * inverse, cosine * inverse, biasY), new(sx, 0, tx, 0, sy, ty));
    }

    internal static SvgGradientCoordinates CreateRadial(SvgRadialGradient gradient, SvgRectangle box, double horizontalScale, double verticalScale, double horizontalOffset, double verticalOffset)
    {
        bool concentric = gradient.FocalX == gradient.CenterX && gradient.FocalY == gradient.CenterY;
        if (gradient.FocalRadius != 0 || !concentric && (!gradient.HasGeneralTransform || gradient.NativePaintMatrices is null)) return default;
        if (gradient.HasGeneralTransform && gradient.NativePaintMatrices is {} matrices)
        {
            float sx = (float)horizontalScale * (gradient.IsUserSpace ? 1 : (float)box.Width);
            float sy = (float)verticalScale * (gradient.IsUserSpace ? 1 : (float)box.Height);
            float tx = gradient.IsUserSpace ? (float)horizontalOffset : (float)box.X * (float)horizontalScale + (float)horizontalOffset;
            float ty = gradient.IsUserSpace ? (float)verticalOffset : (float)box.Y * (float)verticalScale + (float)verticalOffset;
            SvgShaderMatrix ctm = SvgShaderMatrix.Concat(new(sx, 0, tx, 0, sy, ty), matrices.Referencing);
            float nativeRadius = (float)gradient.Radius;
            if (nativeRadius <= 0 || !float.IsFinite(nativeRadius)) return default;
            if (!concentric)
            {
                float fx = (float)gradient.FocalX, fy = (float)gradient.FocalY;
                float dx = (float)gradient.CenterX - fx, dy = (float)gradient.CenterY - fy;
                float distance = MathF.Sqrt(dx * dx + dy * dy);
                float ratio = nativeRadius / distance;
                if (!float.IsFinite(ratio) || ratio <= 1 + 1f / 4096) return default;
                SvgShaderMatrix centers = new(dy, dx, fx, -dx, dy, fy);
                if (!centers.TryInvert(out SvgShaderMatrix inverseCenters)) return default;
                SvgShaderMatrix unitX = SvgShaderMatrix.Concat(new(0, 1, 0, -1, 0, 0), inverseCenters);
                float squareMinusOne = ratio * ratio - 1;
                float xScale = ratio / squareMinusOne, yScale = 1f / MathF.Sqrt(squareMinusOne);
                SvgShaderMatrix unit = new(unitX.ScaleX * xScale, unitX.SkewX * xScale, unitX.TranslateX * xScale,
                    unitX.SkewY * yScale, unitX.ScaleY * yScale, unitX.TranslateY * yScale);
                return new(unit, ctm, matrices.Local, 1f / ratio);
            }
            float inverse = 1f / nativeRadius;
            return new(new(inverse, 0, -(float)gradient.CenterX * inverse, 0, inverse, -(float)gradient.CenterY * inverse), ctm, matrices.Local);
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
            return Circle((float)gradient.CenterX, (float)gradient.CenterY, (float)gradient.Radius, paint);
        }
        float scaleX = (float)(gradient.IsUserSpace ? gradient.ReferenceScaleX : box.Width) * (float)horizontalScale;
        float scaleY = (float)(gradient.IsUserSpace ? gradient.ReferenceScaleY : box.Height) * (float)verticalScale;
        float translateX = gradient.IsUserSpace ? (float)horizontalOffset : (float)box.X * (float)horizontalScale + (float)horizontalOffset;
        float translateY = gradient.IsUserSpace ? (float)verticalOffset : (float)box.Y * (float)verticalScale + (float)verticalOffset;
        float cx = (float)(gradient.IsUserSpace ? gradient.CenterX / gradient.ReferenceScaleX : gradient.CenterX);
        float cy = (float)(gradient.IsUserSpace ? gradient.CenterY / gradient.ReferenceScaleY : gradient.CenterY);
        float radius = (float)(gradient.IsUserSpace ? gradient.Radius / Math.Abs(gradient.ReferenceScaleX) : gradient.Radius);
        return Circle(cx, cy, radius, new(scaleX, 0, translateX, 0, scaleY, translateY));

        static SvgGradientCoordinates Circle(float x, float y, float radius, SvgShaderMatrix paint)
        {
            if (radius <= 0 || !float.IsFinite(radius)) return default;
            float inverse = 1f / radius;
            return new(new(inverse, 0, -x * inverse, 0, inverse, -y * inverse), paint);
        }
    }

    internal bool TryLinear(int x, int y, SvgCpuRasterGrid grid, out double position)
    {
        position = 0;
        if (!TryMap(x, y, grid, out var point)) return false;
        position = point.X;
        return double.IsFinite(position);
    }

    internal bool TryRadial(int x, int y, SvgCpuRasterGrid grid, out double position)
    {
        position = 0;
        if (!TryMap(x, y, grid, out var point)) return false;
        // Keep separate float squares before the native radius stage.
        position = MathF.Sqrt(point.X * point.X + point.Y * point.Y) - point.X * _focalRadiusRatioInverse;
        return double.IsFinite(position);
    }

    private bool TryMap(int x, int y, SvgCpuRasterGrid grid, out (float X, float Y) point)
    {
        point = default;
        if (!_enabled) return false;
        SvgCpuBitmapOrigin origin = grid.GetBitmapOrigin(x, y);
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
        point = _prepared.Map(x - origin.X + .5f, y - origin.Y + .5f);
        return float.IsFinite(point.X) && float.IsFinite(point.Y);
    }
}
