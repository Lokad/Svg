// Managed adaptation of Skia affine concatenation and inversion.
// Copyright 2006 The Android Open Source Project; 2018 Google LLC.
// See THIRD_PARTY_NOTICES.txt.
namespace Lokad.Svg.Rendering;

/// <summary>Retains a finite affine shader matrix in native row order.</summary>
/// <remarks>Immutable and allocation-free. Coordinates use float storage; general
/// concatenation and inversion retain the native double intermediates.</remarks>
internal readonly record struct SvgShaderMatrix(float ScaleX, float SkewX, float TranslateX, float SkewY, float ScaleY, float TranslateY)
{
    internal bool IsFinite => float.IsFinite(ScaleX) && float.IsFinite(SkewX) && float.IsFinite(TranslateX)
        && float.IsFinite(SkewY) && float.IsFinite(ScaleY) && float.IsFinite(TranslateY);

    internal static SvgShaderMatrix Concat(SvgShaderMatrix outer, SvgShaderMatrix inner)
    {
        if (outer.SkewX == 0 && outer.SkewY == 0 && inner.SkewX == 0 && inner.SkewY == 0)
            return new(outer.ScaleX * inner.ScaleX, 0, outer.ScaleX * inner.TranslateX + outer.TranslateX,
                0, outer.ScaleY * inner.ScaleY, outer.ScaleY * inner.TranslateY + outer.TranslateY);
        return new((float)((double)outer.ScaleX * inner.ScaleX + (double)outer.SkewX * inner.SkewY),
            (float)((double)outer.ScaleX * inner.SkewX + (double)outer.SkewX * inner.ScaleY),
            (float)((double)outer.ScaleX * inner.TranslateX + (double)outer.SkewX * inner.TranslateY) + outer.TranslateX,
            (float)((double)outer.SkewY * inner.ScaleX + (double)outer.ScaleY * inner.SkewY),
            (float)((double)outer.SkewY * inner.SkewX + (double)outer.ScaleY * inner.ScaleY),
            (float)((double)outer.SkewY * inner.TranslateX + (double)outer.ScaleY * inner.TranslateY) + outer.TranslateY);
    }

    internal bool TryInvert(out SvgShaderMatrix inverse)
    {
        inverse = default;
        if (SkewX == 0 && SkewY == 0)
        {
            float x = 1f / ScaleX, y = 1f / ScaleY;
            inverse = new(x, 0, -TranslateX * x, 0, y, -TranslateY * y);
        }
        else
        {
            double determinant = (double)ScaleX * ScaleY - (double)SkewX * SkewY;
            // Native affine inversion uses the cube of its nearly-zero threshold.
            if (MathF.Abs((float)determinant) <= 1f / (4096f * 4096f * 4096f)) return false;
            double scale = 1 / determinant;
            inverse = new((float)(ScaleY * scale), (float)(-SkewX * scale),
                (float)(((double)SkewX * TranslateY - (double)ScaleY * TranslateX) * scale),
                (float)(-SkewY * scale), (float)(ScaleX * scale),
                (float)(((double)SkewY * TranslateX - (double)ScaleX * TranslateY) * scale));
        }
        return inverse.IsFinite;
    }

    internal (float X, float Y) Map(float x, float y)
        => (MathF.FusedMultiplyAdd(x, ScaleX, MathF.FusedMultiplyAdd(y, SkewX, TranslateX)),
            MathF.FusedMultiplyAdd(x, SkewY, MathF.FusedMultiplyAdd(y, ScaleY, TranslateY)));
}
