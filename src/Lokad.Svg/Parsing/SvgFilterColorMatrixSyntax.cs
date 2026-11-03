using System.Globalization;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Parsing;

/// <summary>Validates color-matrix declarations before their immutable coefficients enter a filter graph.</summary>
internal static class SvgFilterColorMatrixSyntax
{
    internal static SvgFilterColorMatrix Parse(string? type, string? values, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        string mode = type ?? "matrix";
        if (mode is not ("matrix" or "saturate" or "hueRotate" or "luminanceToAlpha"))
            throw new SvgException(SvgErrorCode.UnsupportedFeature, "This color-matrix type is not implemented.");
        if (mode == "luminanceToAlpha") return new([0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, .2125f, .7154f, .0721f, 0, 0]);
        if (values is null) return SvgFilterColorMatrix.Identity;
        int required = mode == "matrix" ? 20 : 1;
        Span<float> coefficients = stackalloc float[20];
        SvgGeometrySyntax.DelimitedTokenReader reader = new(values.AsSpan(), SvgNumberListSyntax.Svg, 0);
        int count = 0;
        while (reader.TryReadValue(out ReadOnlySpan<char> number))
        {
            token.ThrowIfCancellationRequested();
            if (number.Length > SvgSyntaxLimits.MaximumValueTokenCharacters)
                throw new SvgException(SvgErrorCode.LimitExceeded, "Color-matrix numbers exceed the syntax limit.");
            if (count == required || !float.TryParse(number, SvgFloatNumber.TokenStyles, CultureInfo.InvariantCulture, out float value)
                || !float.IsFinite(value))
                throw new SvgException(SvgErrorCode.MalformedInput, "Color-matrix values require the declared type's finite number count.");
            coefficients[count++] = value;
        }
        if (count != required) throw new SvgException(SvgErrorCode.MalformedInput, "Color-matrix values require the declared type's finite number count.");
        if (mode == "matrix") return new(coefficients);
        float amount = coefficients[0];
        if (mode == "saturate") return new([
            .213f + .787f * amount, .715f - .715f * amount, .072f - .072f * amount, 0, 0,
            .213f - .213f * amount, .715f + .285f * amount, .072f - .072f * amount, 0, 0,
            .213f - .213f * amount, .715f - .715f * amount, .072f + .928f * amount, 0, 0,
            0, 0, 0, 1, 0]);
        float radians = amount * (MathF.PI / 180), cosine = MathF.Cos(radians), sine = MathF.Sin(radians);
        return new([
            .213f + cosine * .787f - sine * .213f, .715f - cosine * .715f - sine * .715f, .072f - cosine * .072f + sine * .928f, 0, 0,
            .213f - cosine * .213f + sine * .143f, .715f + cosine * .285f + sine * .140f, .072f - cosine * .072f - sine * .283f, 0, 0,
            .213f - cosine * .213f - sine * .787f, .715f - cosine * .715f + sine * .715f, .072f + cosine * .928f + sine * .072f, 0, 0,
            0, 0, 0, 1, 0]);
    }
}
