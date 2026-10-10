using System.Globalization;

namespace Lokad.Svg.Parsing;

/// <summary>Validates presentation values whose admitted meanings add no static painting behavior.</summary>
internal static class SvgInertPresentationSyntax
{
    /// <summary>Validates a backdrop hint without storing it or creating a backdrop. Active filters remain unsupported.</summary>
    internal static void ValidateBackground(ReadOnlySpan<char> value, ParserWorkCounter? workCounter, CancellationToken cancellationToken)
    {
        workCounter?.ConsumeText(value.Length, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        int count = 0;
        bool newBackground = false;
        foreach (Range range in value.SplitAny(" \t\r\n".AsSpan()))
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReadOnlySpan<char> token = value[range];
            if (token.IsEmpty) continue;
            if (count == 0)
            {
                newBackground = token.Equals("new".AsSpan(), StringComparison.OrdinalIgnoreCase);
                if (!newBackground && !token.Equals("accumulate".AsSpan(), StringComparison.OrdinalIgnoreCase) && !token.Equals("inherit".AsSpan(), StringComparison.OrdinalIgnoreCase))
                    throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only new, accumulate and inherit enable-background values are implemented.");
            }
            else
            {
                if (!newBackground || count > 4) throw new SvgException(SvgErrorCode.MalformedInput, "Only new enable-background values take an optional four-number rectangle.");
                if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out double coordinate) || !double.IsFinite(coordinate))
                    throw new SvgException(SvgErrorCode.MalformedInput, "Enable-background rectangles must be finite numbers.");
                if (count >= 3 && coordinate < 0) throw new SvgException(SvgErrorCode.MalformedInput, "Enable-background rectangle dimensions cannot be negative.");
            }
            count++;
        }
        if (count == 0) throw new SvgException(SvgErrorCode.UnsupportedFeature, "An enable-background value is required.");
        // SVG 1.1 disables backdrop access for numeric partial rectangles.
        // With backdrop-dependent filters rejected, these forms are inert too.
    }
}
