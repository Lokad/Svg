// Adapts SkRasterPipeline_opts.h repeat, bilinear tap and gather stages.
// Copyright 2016 Google Inc.; 2018 Google LLC. See THIRD_PARTY_NOTICES.txt.
using Lokad.Svg.Parsing;

namespace Lokad.Svg.Rendering;

/// <summary>Prepares picture texture addresses for optional bilinear interpolation.</summary>
/// <remarks>Owns no storage. The caller retains the admitted texture and draw-local mutable
/// coordinate cache and checks cancellation before sampling.
/// Prepared addresses are valid only for the same validated texture dimensions.</remarks>
internal readonly record struct SvgPatternSample(int First, int Second, int Third, int Fourth, float FractionX, float FractionY)
{
    internal bool RequiresFiltering => FractionX != 0 || FractionY != 0;

    internal static SvgPatternSample Prepare(SvgPatternGeometry pattern, int x, int y,
        SvgCpuBitmapOrigin origin, ref SvgShaderCoordinates coordinates)
    {
        int tileX, tileY, nextX, nextY;
        float fx, fy;
        if (pattern.NativeImageMap is not null)
        {
            if (!coordinates.TryMap(x, y, origin, out (float X, float Y) point))
                throw new SvgException(SvgErrorCode.UnsupportedFeature, "Pattern image coordinates are not finite and invertible.");
            float weightX = point.X + .5f, weightY = point.Y + .5f;
            fx = weightX - MathF.Floor(weightX);
            fy = weightY - MathF.Floor(weightY);
            tileX = Repeat(point.X - .5f, pattern.TextureWidth);
            nextX = Repeat(point.X + .5f, pattern.TextureWidth);
            tileY = Repeat(point.Y - .5f, pattern.TextureHeight);
            nextY = Repeat(point.Y + .5f, pattern.TextureHeight);
        }
        else
        {
            float sampleX = x - (float)(pattern.OriginX + pattern.ShaderPlacement.E);
            float sampleY = y - (float)(pattern.OriginY + pattern.ShaderPlacement.F);
            float floorX = MathF.Floor(sampleX), floorY = MathF.Floor(sampleY);
            tileX = (int)((floorX % pattern.TextureWidth + pattern.TextureWidth) % pattern.TextureWidth);
            tileY = (int)((floorY % pattern.TextureHeight + pattern.TextureHeight) % pattern.TextureHeight);
            fx = sampleX - floorX; fy = sampleY - floorY;
            nextX = tileX + 1 == pattern.TextureWidth ? 0 : tileX + 1;
            nextY = tileY + 1 == pattern.TextureHeight ? 0 : tileY + 1;
        }
        return new((tileY * pattern.TextureWidth + tileX) * 4, (tileY * pattern.TextureWidth + nextX) * 4,
            (nextY * pattern.TextureWidth + tileX) * 4, (nextY * pattern.TextureWidth + nextX) * 4, fx, fy);

        static int Repeat(float value, int size)
        {
            // Repeat each floating tap before gathering. A rounded reciprocal
            // can cross a period below its integer boundary; integer modulo
            // and an inferred neighboring address lose that native stage.
            float extent = size;
            float repeated = value - MathF.Floor(value * (1f / extent)) * extent;
            return (int)Math.Clamp(repeated, 0, MathF.BitDecrement(extent));
        }
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
