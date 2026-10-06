namespace Lokad.Svg.Rendering;

/// <summary>Identifies the material arithmetic of one analytic paint operation.</summary>
internal enum SvgAnalyticPaintKind : byte
{
    Mask,
    Direct,
    Pair,
}

/// <summary>Retains an ordered direct paint for one pixel; Next is a one-based row-event index.</summary>
internal readonly record struct SvgDirectPaintOperation(int Next, int Sequence, byte Alpha, SvgAnalyticPaintKind Kind);

/// <summary>Retains a full-coverage direct span without one event allocation per covered pixel.</summary>
internal readonly record struct SvgDirectPaintSpan(int Sequence);

/// <summary>Indexes one pixel's ordered events without allocating one collection per pixel.</summary>
internal readonly record struct SvgDirectPaintLink(int First, int Last, int Span);

/// <summary>Retains either premultiplied solid bytes or a floating shader sample for ordered paint replay.</summary>
/// <remarks>Immutable and allocation-free. The caller owns destination storage and charges pixel/event work; the coverage walker checks cancellation between events. Samples retain no document or tile storage.</remarks>
internal readonly record struct SvgAnalyticPaint
{
    private readonly SvgColor _solid;
    private readonly SvgShaderPaint? _shader;

    private SvgAnalyticPaint(SvgColor solid, SvgShaderPaint? shader)
    {
        _solid = solid;
        _shader = shader;
    }

    internal static SvgAnalyticPaint Solid(SvgColor color)
    {
        return new(new SvgColor(Premultiply(color.Red, color.Alpha), Premultiply(color.Green, color.Alpha), Premultiply(color.Blue, color.Alpha), color.Alpha), null);

        static byte Premultiply(byte color, byte alpha) => (byte)((color * alpha + 127) / 255);
    }

    internal static SvgAnalyticPaint Shader(SvgShaderPaint sample) => new(default, sample);

    /// <summary>Paints one operation onto a premultiplied RGBA8 pixel without collapsing it into another operation.</summary>
    /// <remarks>The uniform source uses the matched software blitter's initial round/255 premultiplication. A8 masks scale each lane with (lane*coverage+lane)/256; direct single/run operations scale first and then source-over. Fringe pairs interpolate before rounding and use their own coverage scale. Shader samples retain float lanes through coverage and source-over; each event stores its result separately.</remarks>
    internal void Blend(Span<byte> destination, byte coverage, SvgAnalyticPaintKind kind)
    {
        if (_shader is SvgShaderPaint shader)
        {
            shader.Blend(destination, coverage);
            return;
        }
        byte sourceRed = _solid.Red, sourceGreen = _solid.Green, sourceBlue = _solid.Blue, sourceAlpha = _solid.Alpha;
        if (coverage == 0 || sourceAlpha == 0) return;
        if (coverage == 255 && sourceAlpha == 255)
        {
            destination[0] = sourceRed;
            destination[1] = sourceGreen;
            destination[2] = sourceBlue;
            destination[3] = 255;
            return;
        }
        // The native opaque-black pair specialization preserves byte coverage
        // directly; the general opaque color interpolation rescales it.
        if (kind == SvgAnalyticPaintKind.Pair && !(sourceAlpha == 255 && sourceRed == 0 && sourceGreen == 0 && sourceBlue == 0))
        {
            int sourceScale;
            int destinationScale;
            if (sourceAlpha == 255)
            {
                sourceScale = coverage + (coverage >> 7);
                destinationScale = 256 - sourceScale;
            }
            else
            {
                sourceScale = coverage + 1;
                int inverse = 65535 - sourceAlpha * sourceScale;
                destinationScale = (inverse + (inverse >> 8)) >> 8;
            }
            destination[0] = (byte)((sourceRed * sourceScale + destination[0] * destinationScale) >> 8);
            destination[1] = (byte)((sourceGreen * sourceScale + destination[1] * destinationScale) >> 8);
            destination[2] = (byte)((sourceBlue * sourceScale + destination[2] * destinationScale) >> 8);
            destination[3] = (byte)((sourceAlpha * sourceScale + destination[3] * destinationScale) >> 8);
            return;
        }
        int scale = coverage + 1;
        int red = (sourceRed * scale) >> 8;
        int green = (sourceGreen * scale) >> 8;
        int blue = (sourceBlue * scale) >> 8;
        int alpha = (sourceAlpha * scale) >> 8;
        int inverseScale = 256 - alpha;
        destination[0] = (byte)Math.Min(red + ((destination[0] * inverseScale) >> 8), 255);
        destination[1] = (byte)Math.Min(green + ((destination[1] * inverseScale) >> 8), 255);
        destination[2] = (byte)Math.Min(blue + ((destination[2] * inverseScale) >> 8), 255);
        destination[3] = (byte)(alpha + ((destination[3] * inverseScale) >> 8));
    }
}
