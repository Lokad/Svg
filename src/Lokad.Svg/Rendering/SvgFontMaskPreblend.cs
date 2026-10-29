// Adapts pinned Skia's SkMaskGamma.cpp/.h, SkScalerContext.cpp and SkColorData.h.
// Copyright 2012 Google Inc. See THIRD_PARTY_NOTICES.txt.
namespace Lokad.Svg.Rendering;

using Lokad.Svg.Parsing;

/// <summary>Builds the measured platform font-mask preblend into caller-owned byte storage.</summary>
/// <remarks>Uses pinned three-bit canonical luminance and platform gamma/contrast. No mask, font or destination storage is retained. Each entry consumes work and observes cancellation; concurrent callers use independent buffers. Paint alpha and clipping apply after preblend.</remarks>
internal static class SvgFontMaskPreblend
{
    internal static void BuildLinuxGrayTable(Span<byte> table, SvgColor color, CancellationToken token)
        => BuildTable(table, color, false, token);

    internal static void BuildWindowsGrayTable(Span<byte> table, SvgColor color, CancellationToken token)
        => BuildTable(table, color, true, token);

    private static void BuildTable(Span<byte> table, SvgColor color, bool useSrgb, CancellationToken token)
    {
        if (table.Length != 256) throw new ArgumentException("A font preblend table must contain 256 entries.", nameof(table));
        int red = Canonical(color.Red), green = Canonical(color.Green), blue = Canonical(color.Blue);
        float source = Canonical((red * 54 + green * 183 + blue * 19) >> 8) / 255f;
        float destination = 1f - source;
        const float gamma = 76f / 64f;
        float linearSource = useSrgb ? ToSrgbLuma(source) : MathF.Pow(source, gamma);
        float linearDestination = useSrgb ? ToSrgbLuma(destination) : MathF.Pow(destination, gamma);
        float contrast = (useSrgb ? 1f : .2f) * linearDestination;
        for (int index = 0; index < table.Length; index++)
        {
            token.ThrowIfCancellationRequested();
            float alpha = index / 255f;
            alpha += (1f - alpha) * contrast * alpha;
            float linearOutput = linearSource * alpha + (1f - alpha) * linearDestination;
            float output = useSrgb ? FromSrgbLuma(linearOutput) : MathF.Pow(linearOutput, 1f / gamma);
            float corrected = (output - destination) / (source - destination);
            table[index] = (byte)Math.Clamp((int)MathF.Floor(corrected * 255f + .5f), 0, 255);
        }

        static int Canonical(int value) { int index = value >> 5; return (index << 5) | (index << 2) | (index >> 1); }
        static float ToSrgbLuma(float value) => value <= .04045f ? value / 12.92f : MathF.Pow((value + .055f) / 1.055f, 2.4f);
        static float FromSrgbLuma(float value) => value <= .0031308f ? value * 12.92f : 1.055f * MathF.Pow(value, 1f / 2.4f) - .055f;
    }
}
