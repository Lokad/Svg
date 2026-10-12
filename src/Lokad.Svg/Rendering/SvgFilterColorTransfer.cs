// Adapts pinned SkRasterPipeline_opts.h transfer evaluation and skcms.cc inverse initialization.
// Copyright 2018 Google Inc.; 2018, 2022 Google LLC. See THIRD_PARTY_NOTICES.txt.
using Lokad.Svg.Parsing;

namespace Lokad.Svg.Rendering;

/// <summary>Converts a borrowed premultiplied shader sample without intermediate byte quantization.</summary>
/// <remarks>Immutable and allocation-free; callers own storage, charge pixel work and observe cancellation.</remarks>
internal static class SvgFilterColorTransfer
{
    private const float SrgbA = (float)(1 / 1.055), SrgbB = (float)(.055 / 1.055), SrgbC = (float)(1 / 12.92);
    private const float InverseG = 1 / 2.4f;
    private static readonly float InverseA = ScalarPower(SrgbA, -2.4f);
    private static readonly float InverseE = 1 - ScalarPower(InverseA, InverseG);

    internal static SvgShaderPaint Convert(SvgShaderPaint sample, SvgFilterColorSpace destination)
    {
        if (sample.Alpha <= 0) return default;
        float inverse = 1 / sample.Alpha;
        return new(Channel(sample.Red * inverse, destination) * sample.Alpha,
            Channel(sample.Green * inverse, destination) * sample.Alpha,
            Channel(sample.Blue * inverse, destination) * sample.Alpha, sample.Alpha);

        static float Channel(float value, SvgFilterColorSpace destination)
        {
            value = Math.Clamp(value, 0, 1);
            return destination == SvgFilterColorSpace.LinearRgb
                ? value <= .04045f ? value * SrgbC : NativePower(MathF.FusedMultiplyAdd(SrgbA, value, SrgbB), 2.4f)
                : value <= SrgbC * .04045f ? value * (1 / SrgbC) : NativePower(InverseA * value, InverseG) + InverseE;
        }

        static float NativePower(float value, float exponent)
        {
            if (value == 0 || value == 1) return value;
            uint bits = BitConverter.SingleToUInt32Bits(value);
            float e = bits * (1f / 8388608);
            float m = BitConverter.UInt32BitsToSingle((bits & 0x007fffff) | 0x3f000000);
            float logarithm = MathF.FusedMultiplyAdd(-m, 1.498030302f, e - 124.225514990f) - 1.725879990f / (.3520887068f + m);
            float x = logarithm * exponent, fraction = x - MathF.Floor(x);
            float approximation = MathF.FusedMultiplyAdd(-fraction, 1.490129070f, x + 121.274057500f);
            approximation += 27.728023300f / (4.84252568f - fraction);
            approximation = Math.Clamp(approximation * 8388608, 0, 2139095040f);
            return BitConverter.UInt32BitsToSingle((uint)MathF.Round(approximation, MidpointRounding.ToEven));
        }
    }

    private static float ScalarPower(float value, float exponent)
    {
        if (value <= 0) return 0;
        if (value == 1) return 1;
        uint bits = BitConverter.SingleToUInt32Bits(value);
        float e = bits * (1f / 8388608);
        float m = BitConverter.UInt32BitsToSingle((bits & 0x007fffff) | 0x3f000000);
        float logarithm = (e - 124.225514990f - 1.498030302f * m) - 1.725879990f / (.3520887068f + m);
        float x = logarithm * exponent, fraction = x - MathF.Floor(x);
        float approximation = 8388608 * ((x + 121.274057500f - 1.490129070f * fraction) + 27.728023300f / (4.84252568f - fraction));
        return BitConverter.UInt32BitsToSingle((uint)approximation);
    }

}
