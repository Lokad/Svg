using System.Globalization;

namespace Lokad.Svg.Parsing;

/// <summary>Reads validated SVG numeric tokens with separately rounded float place values.</summary>
/// <remarks>Callers retain their grammar, token bounds and finite-double validation. This bounded, allocation-free additional pass observes the cancellation token. Numeric range classification remains with the caller. There is no retained state or storage ownership.</remarks>
internal static class SvgFloatNumber
{
    /// <summary>Admits one numeric token after the caller handles grammar whitespace and units.</summary>
    internal const NumberStyles TokenStyles = NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent;

    internal static float ReadValidated(ReadOnlySpan<char> token, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        int signEnd = token[0] is '+' or '-' ? 1 : 0;
        int exponentAt = token.IndexOfAny('e', 'E');
        ReadOnlySpan<char> mantissa = exponentAt < 0 ? token : token[..exponentAt];
        int dotAt = mantissa.IndexOf('.');
        int integerEnd = dotAt < 0 ? mantissa.Length : dotAt;
        float whole = 0;
        float place = 1;
        for (int digit = integerEnd - 1; digit >= signEnd; digit--)
        {
            whole += (mantissa[digit] - '0') * place;
            place *= 10;
        }
        float fraction = 0;
        float weight = 1;
        if (dotAt >= 0)
        {
            foreach (char digit in mantissa[(dotAt + 1)..])
            {
                weight *= 0.1f;
                fraction += (digit - '0') * weight;
            }
        }
        float value = whole + fraction;
        if (token[0] == '-') value = -value;
        if (exponentAt >= 0 && value != 0)
        {
            double exponent = double.Parse(token[(exponentAt + 1)..], SvgFloatNumber.TokenStyles, CultureInfo.InvariantCulture);
            value *= (float)Math.Pow(10, exponent);
        }
        return value;
    }
}
