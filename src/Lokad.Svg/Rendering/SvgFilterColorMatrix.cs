// Matrix evaluation follows pinned SkRasterPipeline_opts.h and SkMatrixColorFilter.cpp.
// Copyright 2018 Google LLC; 2011 Google Inc. See THIRD_PARTY_NOTICES.txt.
namespace Lokad.Svg.Rendering;

/// <summary>Owns twenty finite coefficients for one straight-RGBA color operation.</summary>
/// <remarks>Immutable value storage; evaluation borrows a premultiplied sample and allocates nothing.
/// The enclosing pixel loop owns cancellation and any retained image storage.</remarks>
internal readonly record struct SvgFilterColorMatrix
{
    private readonly Row _red, _green, _blue, _alpha;

    internal SvgFilterColorMatrix(ReadOnlySpan<float> coefficients)
    {
        if (coefficients.Length != 20) throw new ArgumentException("A color matrix requires twenty coefficients.", nameof(coefficients));
        foreach (float coefficient in coefficients)
            if (!float.IsFinite(coefficient)) throw new ArgumentException("Color matrix coefficients must be finite.", nameof(coefficients));
        _red = new(coefficients[0], coefficients[1], coefficients[2], coefficients[3], coefficients[4]);
        _green = new(coefficients[5], coefficients[6], coefficients[7], coefficients[8], coefficients[9]);
        _blue = new(coefficients[10], coefficients[11], coefficients[12], coefficients[13], coefficients[14]);
        _alpha = new(coefficients[15], coefficients[16], coefficients[17], coefficients[18], coefficients[19]);
    }

    internal static SvgFilterColorMatrix Identity => new([1, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 1, 0]);

    /// <summary>Identifies operations that can paint outside the input image's nontransparent extent.</summary>
    internal bool AffectsTransparentBlack => _alpha.Offset > 0;

    internal SvgShaderPaint Apply(SvgShaderPaint input)
    {
        float inverse = SvgShaderPaint.UnpremultiplyScale(input.Alpha);
        float red = input.Red * inverse, green = input.Green * inverse, blue = input.Blue * inverse;
        float alpha = Math.Clamp(_alpha.Evaluate(red, green, blue, input.Alpha), 0, 1);
        return new(Math.Clamp(_red.Evaluate(red, green, blue, input.Alpha), 0, 1) * alpha,
            Math.Clamp(_green.Evaluate(red, green, blue, input.Alpha), 0, 1) * alpha,
            Math.Clamp(_blue.Evaluate(red, green, blue, input.Alpha), 0, 1) * alpha, alpha);
    }

    private readonly record struct Row(float Red, float Green, float Blue, float Alpha, float Offset)
    {
        internal float Evaluate(float red, float green, float blue, float alpha)
            => MathF.FusedMultiplyAdd(red, Red, MathF.FusedMultiplyAdd(green, Green,
                MathF.FusedMultiplyAdd(blue, Blue, MathF.FusedMultiplyAdd(alpha, Alpha, Offset))));
    }
}
