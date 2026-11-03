using System.Globalization;

namespace Lokad.Svg.Parsing;

internal enum SvgTextCoordinateKind { Length, Rotation }

/// <summary>Resolves a declaration's text positions against its own computed font size.</summary>
/// <remarks>Immutable and allocation-free. Lists retain this value while descendants change fonts;
/// fixed lengths reuse CSS unit conversion, and rotation remains a finite unitless number.
/// Viewport percentages and physical-font x-height units require distinct contexts and reject here.</remarks>
internal readonly record struct SvgTextPositionBasis
{
    internal SvgTextPositionBasis(double fontSize)
    {
        if (!double.IsFinite(fontSize) || fontSize <= 0) throw new ArgumentOutOfRangeException(nameof(fontSize));
        FontSize = fontSize;
    }

    internal double FontSize { get; }

    internal double Read(ReadOnlySpan<char> token, SvgTextCoordinateKind kind)
    {
        double factor = 1;
        if (kind == SvgTextCoordinateKind.Length)
        {
            if (token.EndsWith("em", StringComparison.OrdinalIgnoreCase))
            {
                if (FontSize == 0) throw new InvalidOperationException("Font-relative positions require a declaration font size.");
                token = token[..^2];
                factor = FontSize;
            }
            else if (!SvgCssLength.TrySplitFixedUnit(token, out token, out factor))
                throw new SvgException(SvgErrorCode.UnsupportedFeature, "Text coordinate lists must contain finite numbers.");
        }
        else if (kind != SvgTextCoordinateKind.Rotation) throw new ArgumentOutOfRangeException(nameof(kind));
        if (!double.TryParse(token, SvgFloatNumber.TokenStyles, CultureInfo.InvariantCulture, out double value)
            || !double.IsFinite(value) || !double.IsFinite(value *= factor))
            throw new SvgException(SvgErrorCode.UnsupportedFeature, "Text coordinate lists must contain finite numbers.");
        return value;
    }
}
