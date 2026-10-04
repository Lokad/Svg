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

/// <summary>Holds a sampled premultiplied paint and distinguishes uniform color from shader output.</summary>
internal readonly record struct SvgAnalyticPaint(byte Red, byte Green, byte Blue, byte Alpha, bool IsSolid)
{
    internal static SvgAnalyticPaint Solid(SvgColor color)
    {
        return new(Premultiply(color.Red, color.Alpha), Premultiply(color.Green, color.Alpha), Premultiply(color.Blue, color.Alpha), color.Alpha, true);

        static byte Premultiply(byte color, byte alpha) => (byte)((color * alpha + 127) / 255);
    }

    internal static SvgAnalyticPaint Shader(SvgColor color)
        => new((byte)((color.Red * (color.Alpha + 1)) >> 8), (byte)((color.Green * (color.Alpha + 1)) >> 8), (byte)((color.Blue * (color.Alpha + 1)) >> 8), color.Alpha, false);

    /// <summary>Paints one operation onto a premultiplied RGBA8 pixel without collapsing it into another operation.</summary>
    /// <remarks>The uniform source uses the matched software blitter's initial round/255 premultiplication. A8 masks scale each lane with (lane*coverage+lane)/256; direct single/run operations scale first and then source-over. Fringe pairs interpolate before rounding and use their own coverage scale. Shader samples already contain premultiplied material output and retain their separate coverage route.</remarks>
    internal void Blend(Span<byte> destination, byte coverage, SvgAnalyticPaintKind kind)
    {
        if (coverage == 0 || Alpha == 0) return;
        if (coverage == 255 && Alpha == 255)
        {
            destination[0] = Red;
            destination[1] = Green;
            destination[2] = Blue;
            destination[3] = 255;
            return;
        }
        // The native opaque-black pair specialization preserves byte coverage
        // directly; the general opaque color interpolation rescales it.
        if (IsSolid && kind == SvgAnalyticPaintKind.Pair && !(Alpha == 255 && Red == 0 && Green == 0 && Blue == 0))
        {
            int sourceScale;
            int destinationScale;
            if (Alpha == 255)
            {
                sourceScale = coverage + (coverage >> 7);
                destinationScale = 256 - sourceScale;
            }
            else
            {
                sourceScale = coverage + 1;
                int inverse = 65535 - Alpha * sourceScale;
                destinationScale = (inverse + (inverse >> 8)) >> 8;
            }
            destination[0] = (byte)((Red * sourceScale + destination[0] * destinationScale) >> 8);
            destination[1] = (byte)((Green * sourceScale + destination[1] * destinationScale) >> 8);
            destination[2] = (byte)((Blue * sourceScale + destination[2] * destinationScale) >> 8);
            destination[3] = (byte)((Alpha * sourceScale + destination[3] * destinationScale) >> 8);
            return;
        }
        int scale = coverage + 1;
        int red = (Red * scale) >> 8;
        int green = (Green * scale) >> 8;
        int blue = (Blue * scale) >> 8;
        int alpha = (Alpha * scale) >> 8;
        int inverseScale = 256 - alpha;
        destination[0] = (byte)Math.Min(red + ((destination[0] * inverseScale) >> 8), 255);
        destination[1] = (byte)Math.Min(green + ((destination[1] * inverseScale) >> 8), 255);
        destination[2] = (byte)Math.Min(blue + ((destination[2] * inverseScale) >> 8), 255);
        destination[3] = (byte)(alpha + ((destination[3] * inverseScale) >> 8));
    }
}
