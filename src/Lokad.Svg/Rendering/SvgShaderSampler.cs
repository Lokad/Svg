using Lokad.Svg.Parsing;

namespace Lokad.Svg.Rendering;

/// <summary>Borrows one draw's gradient ramp or texture for allocation-free per-pixel paint sampling.</summary>
/// <remarks>The caller owns and disposes the ramp/texture after every sampler borrower finishes.
/// Coordinates are mutable draw-local caches; instances must not be shared between threads.
/// Pixel/geometry work and cancellation belong to the caller; optional texture interpolation
/// is charged here before texture reads and interpolation. Copies borrow the same owners.</remarks>
internal struct SvgShaderSampler
{
    private enum Kind : byte { Linear, Radial, Pattern, Constant }
    private Kind _kind;
    private SvgLinearGradient _linear;
    private SvgRadialGradient _radial;
    private SvgPatternGeometry? _pattern;
    private ReadOnlyMemory<byte> _texture;
    private SvgRectangle _box;
    private SvgGradientRamp _ramp;
    private SvgShaderCoordinates _coordinates;
    private SvgShaderSamplingContext _context;
    private double _directionX, _directionY, _denominator;
    private float _alpha;
    private SvgShaderPaint _constant;

    internal static SvgShaderSampler Constant(SvgShaderPaint color, float alpha)
        => new() { _kind = Kind.Constant, _constant = color, _alpha = alpha };

    internal static SvgShaderSampler Linear(SvgLinearGradient definition, SvgRectangle box, SvgGradientRamp ramp,
        SvgShaderCoordinates coordinates, SvgShaderSamplingContext context, float alpha)
    {
        double dx = definition.X2 - definition.X1, dy = definition.Y2 - definition.Y1;
        return new() { _kind = Kind.Linear, _linear = definition, _box = box, _ramp = ramp, _coordinates = coordinates,
            _context = context, _alpha = alpha, _directionX = dx, _directionY = dy, _denominator = dx * dx + dy * dy };
    }

    internal static SvgShaderSampler Radial(SvgRadialGradient definition, SvgRectangle box, SvgGradientRamp ramp,
        SvgShaderCoordinates coordinates, SvgShaderSamplingContext context, float alpha)
        => new() { _kind = Kind.Radial, _radial = definition, _box = box, _ramp = ramp, _coordinates = coordinates, _context = context, _alpha = alpha };

    internal static SvgShaderSampler Pattern(SvgPatternGeometry definition, byte[] texture,
        SvgShaderCoordinates coordinates, SvgShaderSamplingContext context, float alpha)
        => new() { _kind = Kind.Pattern, _pattern = definition, _texture = texture, _coordinates = coordinates, _context = context, _alpha = alpha };

    /// <summary>Preserves native paint-alpha quantization before shading a reduced-width hairline.</summary>
    internal void ApplyHairlineCoverage(float coverage)
    {
        if (coverage == 1) return;
        int alpha = Math.Clamp((int)(_alpha * 255f + .5f), 0, 255);
        _alpha = ((alpha * (int)(coverage * 256)) >> 8) / 255f;
    }

    internal SvgAnalyticPaint Sample(int x, int y, CancellationToken token)
    {
        if (_kind != Kind.Pattern) return SampleGradient(x, y);
        SvgPatternGeometry pattern = _pattern ?? throw new InvalidOperationException("Pattern sampling requires a retained texture definition.");
        SvgPatternSample sample = SvgPatternSample.Prepare(pattern, x, y, pattern.NativeImageMap is null ? default : _context.Origin(x, y), ref _coordinates);
        if (sample.RequiresFiltering) token.ThrowIfCancellationRequested();
        return SvgAnalyticPaint.Shader(sample.Sample(_texture.Span, _alpha));
    }

    private SvgAnalyticPaint SampleGradient(int x, int y)
    {
        if (_kind == Kind.Constant) return SvgAnalyticPaint.Constant(_constant.AsConstant(_alpha));
        if (_kind == Kind.Linear)
        {
            SvgLinearGradient definition = _linear;
            double position;
            if (!_coordinates.TryLinear(x, y, _context.Origin(x, y), out position))
            {
                SvgDevicePoint point = _context.SourcePoint(x, y);
                if (definition.SourcePaint is { Data.IsMapped: true } source) position = source.Position(point.X, point.Y);
                else if (definition.IsUserSpace && Math.Abs(definition.ReferenceScaleX) != Math.Abs(definition.ReferenceScaleY))
                {
                    double rx = (point.X - definition.X1) / definition.ReferenceScaleX, ry = (point.Y - definition.Y1) / definition.ReferenceScaleY;
                    double ax = _directionX / definition.ReferenceScaleX, ay = _directionY / definition.ReferenceScaleY;
                    position = (rx * ax + ry * ay) / (ax * ax + ay * ay);
                }
                else if (definition.IsUserSpace) position = ((point.X - definition.X1) * _directionX + (point.Y - definition.Y1) * _directionY) / _denominator;
                else position = (((point.X - _box.X) / _box.Width - definition.X1) * _directionX
                    + ((point.Y - _box.Y) / _box.Height - definition.Y1) * _directionY) / _denominator;
            }
            return SvgAnalyticPaint.Shader(_ramp.SampleAbsolute(SvgGradientRamp.Spread(position, definition.Spread), _alpha, _context.Dither(x, y)));
        }
        SvgRadialGradient circle = _radial;
        double radialPosition;
        if (circle.Stops.Length == 1 && !_coordinates.MasksRadialSamples) radialPosition = 0;
        else if (!_coordinates.TryRadial(x, y, _context.Origin(x, y), out SvgRadialShaderSample sample))
        {
            SvgDevicePoint point = _context.SourcePoint(x, y);
            double px = circle.IsUserSpace ? point.X : (point.X - _box.X) / _box.Width;
            double py = circle.IsUserSpace ? point.Y : (point.Y - _box.Y) / _box.Height;
            if (circle.HasGeneralTransform) (px, py) = circle.Inverse.Apply(px, py);
            double fx = circle.FocalX, fy = circle.FocalY, cx = circle.CenterX, cy = circle.CenterY;
            double radius = circle.Radius, focalRadius = circle.FocalRadius;
            if (circle.IsUserSpace && Math.Abs(circle.ReferenceScaleX) != Math.Abs(circle.ReferenceScaleY))
            {
                px = (px - cx) / circle.ReferenceScaleX; py = (py - cy) / circle.ReferenceScaleY;
                fx = (fx - cx) / circle.ReferenceScaleX; fy = (fy - cy) / circle.ReferenceScaleY;
                cx = 0; cy = 0;
                radius /= Math.Abs(circle.ReferenceScaleX); focalRadius /= Math.Abs(circle.ReferenceScaleX);
            }
            radialPosition = SvgShaderCoordinates.EvaluateRadial(new(px - fx, py - fy), new(cx - fx, cy - fy), radius, focalRadius);
        }
        else
        {
            if (!sample.IsVisible) return SvgAnalyticPaint.Shader(default);
            radialPosition = sample.Position;
        }
        return SvgAnalyticPaint.Shader(_ramp.SampleAbsolute(SvgGradientRamp.Spread(radialPosition, circle.Spread), _alpha, _context.Dither(x, y)));
    }

}
