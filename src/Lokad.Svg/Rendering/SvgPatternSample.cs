using Lokad.Svg.Parsing;

namespace Lokad.Svg.Rendering;

/// <summary>Prepares picture texture addresses for optional bilinear interpolation.</summary>
/// <remarks>Owns no storage. The caller retains the texture and mutable coordinate cache, charges
/// four interpolation units before Sample when filtering is required, and checks cancellation.
/// Prepared addresses are valid only for the same validated texture dimensions.</remarks>
internal readonly record struct SvgPatternSample(int First, int Second, int Third, int Fourth, float FractionX, float FractionY)
{
    internal bool RequiresFiltering => FractionX != 0 || FractionY != 0;

    internal static SvgPatternSample Prepare(SvgPatternGeometry pattern, int x, int y,
        SvgCpuBitmapOrigin origin, ref SvgShaderCoordinates coordinates)
    {
        float sampleX = x - (float)(pattern.OriginX + pattern.ShaderPlacement.E);
        float sampleY = y - (float)(pattern.OriginY + pattern.ShaderPlacement.F);
        float nativeFractionX = 0, nativeFractionY = 0;
        if (pattern.NativeImageMap is not null)
        {
            if (!coordinates.TryMap(x, y, origin, out (float X, float Y) point))
                throw new SvgException(SvgErrorCode.UnsupportedFeature, "Pattern image coordinates are not finite and invertible.");
            (sampleX, sampleY) = point;
            float weightX = sampleX + .5f, weightY = sampleY + .5f;
            nativeFractionX = weightX - MathF.Floor(weightX);
            nativeFractionY = weightY - MathF.Floor(weightY);
            sampleX -= .5f; sampleY -= .5f;
        }
        float floorX = MathF.Floor(sampleX), floorY = MathF.Floor(sampleY);
        int tileX = (int)((floorX % pattern.TextureWidth + pattern.TextureWidth) % pattern.TextureWidth);
        int tileY = (int)((floorY % pattern.TextureHeight + pattern.TextureHeight) % pattern.TextureHeight);
        float fx = pattern.NativeImageMap is null ? sampleX - floorX : nativeFractionX;
        float fy = pattern.NativeImageMap is null ? sampleY - floorY : nativeFractionY;
        int nextX = tileX + 1 == pattern.TextureWidth ? 0 : tileX + 1;
        int nextY = tileY + 1 == pattern.TextureHeight ? 0 : tileY + 1;
        return new((tileY * pattern.TextureWidth + tileX) * 4, (tileY * pattern.TextureWidth + nextX) * 4,
            (nextY * pattern.TextureWidth + tileX) * 4, (nextY * pattern.TextureWidth + nextX) * 4, fx, fy);
    }

    internal SvgShaderPaint Sample(ReadOnlySpan<byte> pixels, float alpha)
    {
        if (!RequiresFiltering) return SvgShaderPaint.Pattern(pixels[First], pixels[First + 1], pixels[First + 2], pixels[First + 3], alpha);
        return SvgShaderPaint.InterpolatePremultiplied(
            new(pixels[First], pixels[First + 1], pixels[First + 2], pixels[First + 3]),
            new(pixels[Second], pixels[Second + 1], pixels[Second + 2], pixels[Second + 3]),
            new(pixels[Third], pixels[Third + 1], pixels[Third + 2], pixels[Third + 3]),
            new(pixels[Fourth], pixels[Fourth + 1], pixels[Fourth + 2], pixels[Fourth + 3]), FractionX, FractionY, alpha);
    }
}
