using System.Globalization;

namespace Lokad.Svg.Parsing;

/// <summary>Validates presentation values whose admitted meanings add no static painting behavior.</summary>
internal static class SvgInertPresentationSyntax
{
    /// <summary>Validates the neutral rendering keywords and unused geometric solid-paint declarations emitted by exporters.</summary>
    /// <remarks>Borrowed values are bounded, validated and discarded. Only initial rendering behavior is admitted;
    /// nondefault isolation, blending and rendering hints retain explicit unsupported outcomes. Solid-paint values
    /// apply only to nontext geometry here; solid-color paint servers remain outside this declaration slice.</remarks>
    internal static bool TryValidateRenderingDefaults(ReadOnlySpan<char> property, ReadOnlySpan<char> value,
        bool isGeometry, CancellationToken token)
    {
        string? expected = property.Equals("isolation", StringComparison.OrdinalIgnoreCase)
            || property.Equals("color-rendering", StringComparison.OrdinalIgnoreCase)
            || property.Equals("image-rendering", StringComparison.OrdinalIgnoreCase)
            || property.Equals("shape-rendering", StringComparison.OrdinalIgnoreCase)
            || property.Equals("text-rendering", StringComparison.OrdinalIgnoreCase) ? "auto"
            : property.Equals("mix-blend-mode", StringComparison.OrdinalIgnoreCase) ? "normal"
            : property.Equals("color-interpolation-filters", StringComparison.OrdinalIgnoreCase) ? "linearRGB" : null;
        bool solidColor = isGeometry && property.Equals("solid-color", StringComparison.OrdinalIgnoreCase);
        bool solidOpacity = isGeometry && property.Equals("solid-opacity", StringComparison.OrdinalIgnoreCase);
        if (expected is null && !solidColor && !solidOpacity) return false;
        if (value.Length > SvgSyntaxLimits.MaximumValueTokenCharacters)
            throw new SvgException(SvgErrorCode.LimitExceeded, "Rendering declaration exceeds the syntax limit.");
        token.ThrowIfCancellationRequested();
        if (solidColor) _ = SvgColorParser.Parse(value.ToString(), SvgRootParser.CssNamedColors);
        else if (solidOpacity) _ = SvgColorParser.ParsePaintOpacity(value.ToString());
        else if (!value.Equals(expected, StringComparison.OrdinalIgnoreCase))
            throw new SvgException(SvgErrorCode.UnsupportedFeature, "Nondefault rendering declaration is not implemented.");
        return true;
    }

    /// <summary>Validates bounded typography on geometry that contains no text or font-relative lengths.</summary>
    /// <remarks>Values are borrowed and discarded; no font or string owner is created. Scalar scans
    /// observe cancellation. Text/container callers retain their separate font rules.</remarks>
    internal static bool TryValidateTypography(ReadOnlySpan<char> property, ReadOnlySpan<char> value, CancellationToken token)
    {
        const string properties = "font-family font-size font-style font-weight font-stretch font-variant text-align line-height writing-mode text-anchor letter-spacing word-spacing text-decoration white-space -inkscape-font-specification";
        bool recognized = false;
        foreach (Range range in properties.AsSpan().Split(' '))
            if (property.Equals(properties.AsSpan()[range], StringComparison.OrdinalIgnoreCase)) { recognized = true; break; }
        if (!recognized) return false;
        if (value.Length > SvgSyntaxLimits.MaximumValueTokenCharacters) throw new SvgException(SvgErrorCode.LimitExceeded, "Typography value exceeds the syntax limit.");
        token.ThrowIfCancellationRequested();
        value = value.Trim();
        if (value.IsEmpty) throw new SvgException(SvgErrorCode.MalformedInput, "Typography values cannot be empty.");
        if (Keyword(value, "inherit initial unset revert")) return true;
        if (property.Equals("text-decoration", StringComparison.OrdinalIgnoreCase)) { _ = SvgTextDecorationSyntax.Parse(value); return true; }
        bool valid;
        if (property.Equals("font-family", StringComparison.OrdinalIgnoreCase))
        {
            _ = Fonts.SvgFontFamilySyntax.Parse(value.ToString(), token);
            return true;
        }
        if (property.Equals("-inkscape-font-specification", StringComparison.OrdinalIgnoreCase))
        {
            if (value.Length >= 2 && (value[0] == '\'' || value[0] == '"') && value[^1] == value[0]) value = value[1..^1];
            if (value.IsEmpty) throw new SvgException(SvgErrorCode.MalformedInput, "A font family name is required.");
            foreach (char character in value)
            {
                token.ThrowIfCancellationRequested();
                if (!char.IsLetterOrDigit(character) && character is not (' ' or '-' or '_'))
                    throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only a single plain inert font family name is implemented.");
            }
            return true;
        }
        if (property.Equals("font-size", StringComparison.OrdinalIgnoreCase))
        {
            if (Keyword(value, "xx-small x-small small medium large x-large xx-large smaller larger")) return true;
            Length(value, false); return true;
        }
        if (property.Equals("line-height", StringComparison.OrdinalIgnoreCase))
        { if (!Keyword(value, "normal")) Length(value, false); return true; }
        if (property.Equals("letter-spacing", StringComparison.OrdinalIgnoreCase) || property.Equals("word-spacing", StringComparison.OrdinalIgnoreCase))
        { if (!Keyword(value, "normal")) Length(value, true); return true; }
        if (property.Equals("font-weight", StringComparison.OrdinalIgnoreCase))
        {
            if (Keyword(value, "normal bold bolder lighter")) return true;
            double weight = Number(value);
            if (weight < 1 || weight > 1000) throw new SvgException(SvgErrorCode.MalformedInput, "Inert font weights must be between 1 and 1000.");
            return true;
        }
        if (property.Equals("font-stretch", StringComparison.OrdinalIgnoreCase))
        {
            if (Keyword(value, "normal ultra-condensed extra-condensed condensed semi-condensed semi-expanded expanded extra-expanded ultra-expanded")) return true;
            if (!value.EndsWith("%", StringComparison.Ordinal)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only named or percentage inert font stretches are implemented.");
            if (Number(value[..^1]) <= 0) throw new SvgException(SvgErrorCode.MalformedInput, "Inert font stretches must be positive.");
            return true;
        }
        if (property.Equals("font-style", StringComparison.OrdinalIgnoreCase)) valid = Keyword(value, "normal italic oblique");
        else if (property.Equals("font-variant", StringComparison.OrdinalIgnoreCase)) valid = Keyword(value, "normal none small-caps");
        else if (property.Equals("text-align", StringComparison.OrdinalIgnoreCase)) valid = Keyword(value, "left right center justify start end");
        else if (property.Equals("writing-mode", StringComparison.OrdinalIgnoreCase)) valid = Keyword(value, "horizontal-tb vertical-rl vertical-lr lr-tb rl-tb tb-rl tb-lr lr rl tb");
        else if (property.Equals("text-anchor", StringComparison.OrdinalIgnoreCase)) valid = Keyword(value, "start middle end");
        else valid = Keyword(value, "normal pre nowrap pre-wrap pre-line break-spaces");
        if (!valid) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Inert typography value is not implemented.");
        return true;

        static bool Keyword(ReadOnlySpan<char> input, string choices)
        {
            foreach (Range range in choices.AsSpan().Split(' '))
                if (input.Equals(choices.AsSpan()[range], StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        static void Length(ReadOnlySpan<char> input, bool allowNegative)
        {
            if (input.Contains('(')) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Computed inert typography lengths are not implemented.");
            foreach (Range range in "rem px pt pc cm mm in em ex %".AsSpan().Split(' '))
            {
                ReadOnlySpan<char> suffix = "rem px pt pc cm mm in em ex %".AsSpan()[range];
                if (input.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) { input = input[..^suffix.Length]; break; }
            }
            double number = Number(input);
            if (!allowNegative && number < 0) throw new SvgException(SvgErrorCode.MalformedInput, "Inert typography dimensions must be nonnegative.");
        }

        static double Number(ReadOnlySpan<char> input)
        {
            if (!double.TryParse(input, NumberStyles.Float, CultureInfo.InvariantCulture, out double number) || !double.IsFinite(number))
                throw new SvgException(SvgErrorCode.MalformedInput, "Inert typography scalars must be finite numbers.");
            return number;
        }
    }

    /// <summary>Validates a backdrop hint without storing it or creating a backdrop. Backdrop-dependent filters remain unsupported.</summary>
    internal static void ValidateBackground(ReadOnlySpan<char> value, CancellationToken cancellationToken)
    {
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
