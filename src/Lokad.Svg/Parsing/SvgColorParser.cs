using System.Globalization;

namespace Lokad.Svg.Parsing;

/// <summary>Parses the bounded CSS color and opacity token subset used by paint resolution.</summary>
internal static class SvgColorParser
{
    internal static byte ParseOpacity(string? value) => value is null ? (byte)255 : ParseOpacity(value.AsSpan());

    internal static byte ParseOpacity(ReadOnlySpan<char> value)
        => ParsePaintOpacity(value).Alpha;

    internal static SvgPaintOpacity ParsePaintOpacity(string? value)
        => value is null ? SvgPaintOpacity.Opaque : ParsePaintOpacity(value.AsSpan());

    private static SvgPaintOpacity ParsePaintOpacity(ReadOnlySpan<char> value)
    {
        if (value.Length > SvgSyntaxLimits.MaximumValueTokenCharacters) throw new SvgException(SvgErrorCode.LimitExceeded, "Opacity values exceed the configured syntax limit.");
        ReadOnlySpan<char> token = value.Trim();
        if (token.EndsWith("%"))
        {
            if (!double.TryParse(token[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out double percentage) || !double.IsFinite(percentage) || percentage < 0 || percentage > 100) throw new SvgException(SvgErrorCode.MalformedInput, "Opacity percentages must be finite values from zero through one hundred.");
            return new SvgPaintOpacity(checked((byte)Math.Round(percentage * 255 / 100, MidpointRounding.AwayFromZero)), (float)(percentage / 100));
        }
        if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out double opacity) || !double.IsFinite(opacity) || opacity < 0 || opacity > 1) throw new SvgException(SvgErrorCode.MalformedInput, "Fill opacity must be a finite value from zero through one.");
        return new SvgPaintOpacity(checked((byte)Math.Round(opacity * 255, MidpointRounding.AwayFromZero)), (float)opacity);
    }

    internal static SvgColor Parse(string value, IReadOnlyDictionary<string, SvgColor> namedColors)
    {
        if (value.Length > SvgSyntaxLimits.MaximumValueTokenCharacters) throw new SvgException(SvgErrorCode.LimitExceeded, "CSS color values exceed the configured syntax limit.");
        if (value.Equals("transparent", StringComparison.OrdinalIgnoreCase)) return SvgColor.Transparent;
        if (namedColors.TryGetValue(value, out SvgColor namedColor)) return namedColor;
        if (TryParseFunctionalRgb(value, out SvgColor functionalColor)) return functionalColor;
        if (TryParseFunctionalHsl(value, out functionalColor)) return functionalColor;
        if (value.Length == 9 && value[0] == '#' && byte.TryParse(value.AsSpan(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte red8) && byte.TryParse(value.AsSpan(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte green8) && byte.TryParse(value.AsSpan(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte blue8) && byte.TryParse(value.AsSpan(7, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte alpha8)) return new SvgColor(red8, green8, blue8, alpha8);
        if (value.Length == 7 && value[0] == '#' && byte.TryParse(value.AsSpan(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte red) && byte.TryParse(value.AsSpan(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte green) && byte.TryParse(value.AsSpan(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte blue)) return new SvgColor(red, green, blue, 255);
        if (value.Length == 5 && value[0] == '#' && byte.TryParse(value.AsSpan(1, 1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte red4) && byte.TryParse(value.AsSpan(2, 1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte green4) && byte.TryParse(value.AsSpan(3, 1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte blue4) && byte.TryParse(value.AsSpan(4, 1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte alpha4)) return new SvgColor((byte)(red4 * 17), (byte)(green4 * 17), (byte)(blue4 * 17), (byte)(alpha4 * 17));
        if (value.Length == 4 && value[0] == '#' && byte.TryParse(value.AsSpan(1, 1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out red) && byte.TryParse(value.AsSpan(2, 1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out green) && byte.TryParse(value.AsSpan(3, 1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out blue)) return new SvgColor((byte)(red * 17), (byte)(green * 17), (byte)(blue * 17), 255);
        throw new SvgException(SvgErrorCode.UnsupportedFeature, "The CSS color name or syntax is not supported.");
    }

    private static byte ParseAlpha(ReadOnlySpan<char> value)
    {
        if (!value.EndsWith('%')) return ParseOpacity(value);
        if (!double.TryParse(value[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out double percentage) || !double.IsFinite(percentage) || percentage < 0 || percentage > 100) throw new SvgException(SvgErrorCode.MalformedInput, "Alpha percentages must be finite values from zero through one hundred.");
        return checked((byte)Math.Round(percentage * 255 / 100, MidpointRounding.AwayFromZero));
    }

    private static bool TryParseFunctionalRgb(string value, out SvgColor color)
    {
        bool hasAlpha = value.StartsWith("rgba(", StringComparison.OrdinalIgnoreCase) && value.EndsWith(')');
        bool isRgb = value.StartsWith("rgb(", StringComparison.OrdinalIgnoreCase) && value.EndsWith(')');
        if (!hasAlpha && !isRgb) { color = default; return false; }
        FunctionalColorTokenReader tokens = new(value.AsSpan((hasAlpha ? 5 : 4)..^1), value.Contains(','));
        if (!tokens.TryRead(out ReadOnlySpan<char> redToken) || !tokens.TryRead(out ReadOnlySpan<char> greenToken) || !tokens.TryRead(out ReadOnlySpan<char> blueToken) || !TryParseRgbChannel(redToken, out byte red) || !TryParseRgbChannel(greenToken, out byte green) || !TryParseRgbChannel(blueToken, out byte blue)) throw new SvgException(SvgErrorCode.MalformedInput, "RGB color channels must be decimal bytes.");
        byte alpha = 255;
        if (tokens.IsCommaSeparated)
        {
            if (hasAlpha)
            {
                if (!tokens.TryRead(out ReadOnlySpan<char> alphaToken) || tokens.TryRead(out _)) throw new SvgException(SvgErrorCode.MalformedInput, "RGB color channels must be decimal bytes.");
                alpha = ParseAlpha(alphaToken);
            }
            else if (tokens.TryRead(out _)) throw new SvgException(SvgErrorCode.MalformedInput, "RGB color channels must be decimal bytes.");
        }
        else if (tokens.TryRead(out ReadOnlySpan<char> separator))
        {
            if (!separator.SequenceEqual("/".AsSpan()) || !tokens.TryRead(out ReadOnlySpan<char> alphaToken) || tokens.TryRead(out _)) throw new SvgException(SvgErrorCode.MalformedInput, "RGB color channels must be decimal bytes.");
            alpha = ParseAlpha(alphaToken);
        }
        color = new SvgColor(red, green, blue, alpha);
        return true;
    }

    private static bool TryParseRgbChannel(ReadOnlySpan<char> token, out byte value)
    {
        if (token.EndsWith('%'))
        {
            bool valid = double.TryParse(token[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out double percentage) && double.IsFinite(percentage) && percentage >= 0 && percentage <= 100;
            value = valid ? checked((byte)Math.Round(percentage * 255 / 100, MidpointRounding.AwayFromZero)) : default;
            return valid;
        }
        return byte.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }

    private static bool TryParseFunctionalHsl(string value, out SvgColor color)
    {
        bool hasAlpha = value.StartsWith("hsla(", StringComparison.OrdinalIgnoreCase) && value.EndsWith(')');
        bool isHsl = value.StartsWith("hsl(", StringComparison.OrdinalIgnoreCase) && value.EndsWith(')');
        if (!hasAlpha && !isHsl) { color = default; return false; }
        FunctionalColorTokenReader tokens = new(value.AsSpan((hasAlpha ? 5 : 4)..^1), value.Contains(','));
        if (!tokens.TryRead(out ReadOnlySpan<char> hueToken) || !tokens.TryRead(out ReadOnlySpan<char> saturationToken) || !tokens.TryRead(out ReadOnlySpan<char> lightnessToken) || !double.TryParse(hueToken, NumberStyles.Float, CultureInfo.InvariantCulture, out double hue) || !double.IsFinite(hue) || !TryParsePercentage(saturationToken, out double saturation) || !TryParsePercentage(lightnessToken, out double lightness)) throw new SvgException(SvgErrorCode.MalformedInput, "HSL colors require a hue and percentage saturation/lightness channels.");
        byte alpha = 255;
        bool hasSpecifiedAlpha = false;
        if (tokens.IsCommaSeparated)
        {
            if (hasAlpha)
            {
                if (!tokens.TryRead(out ReadOnlySpan<char> alphaToken) || tokens.TryRead(out _)) throw new SvgException(SvgErrorCode.MalformedInput, "HSL colors require a hue and percentage saturation/lightness channels.");
                alpha = ParseAlpha(alphaToken);
                hasSpecifiedAlpha = true;
            }
            else if (tokens.TryRead(out _)) throw new SvgException(SvgErrorCode.MalformedInput, "HSL colors require a hue and percentage saturation/lightness channels.");
        }
        else if (tokens.TryRead(out ReadOnlySpan<char> separator))
        {
            if (!separator.SequenceEqual("/".AsSpan()) || !tokens.TryRead(out ReadOnlySpan<char> alphaToken) || tokens.TryRead(out _)) throw new SvgException(SvgErrorCode.MalformedInput, "HSL colors require a hue and percentage saturation/lightness channels.");
            alpha = ParseAlpha(alphaToken);
            hasSpecifiedAlpha = true;
        }
        if (hasAlpha && !hasSpecifiedAlpha) throw new SvgException(SvgErrorCode.MalformedInput, "HSL colors require a hue and percentage saturation/lightness channels.");
        hue = ((hue % 360) + 360) % 360;
        double chroma = (1 - Math.Abs(2 * lightness - 1)) * saturation;
        double secondary = chroma * (1 - Math.Abs((hue / 60 % 2) - 1));
        (double red, double green, double blue) = hue < 60 ? (chroma, secondary, 0d) : hue < 120 ? (secondary, chroma, 0d) : hue < 180 ? (0d, chroma, secondary) : hue < 240 ? (0d, secondary, chroma) : hue < 300 ? (secondary, 0d, chroma) : (chroma, 0d, secondary);
        double match = lightness - chroma / 2;
        color = new SvgColor((byte)Math.Round((red + match) * 255, MidpointRounding.AwayFromZero), (byte)Math.Round((green + match) * 255, MidpointRounding.AwayFromZero), (byte)Math.Round((blue + match) * 255, MidpointRounding.AwayFromZero), alpha);
        return true;
    }

    private static bool TryParsePercentage(ReadOnlySpan<char> token, out double value)
    {
        value = 0;
        bool valid = token.EndsWith('%') && double.TryParse(token[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out value) && double.IsFinite(value) && value >= 0 && value <= 100;
        value /= valid ? 100 : 1;
        return valid;
    }

    private ref struct FunctionalColorTokenReader
    {
        private readonly ReadOnlySpan<char> _value;
        private int _position;
        private bool _finished;

        internal FunctionalColorTokenReader(ReadOnlySpan<char> value, bool isCommaSeparated)
        {
            _value = value;
            IsCommaSeparated = isCommaSeparated;
        }

        internal bool IsCommaSeparated { get; }

        internal bool TryRead(out ReadOnlySpan<char> token)
        {
            if (IsCommaSeparated)
            {
                if (_finished) { token = default; return false; }
                int start = _position;
                while (_position < _value.Length && _value[_position] != ',') _position++;
                token = _value[start.._position].Trim();
                if (_position == _value.Length) _finished = true;
                else _position++;
                return true;
            }

            while (_position < _value.Length && char.IsWhiteSpace(_value[_position])) _position++;
            if (_position == _value.Length) { token = default; return false; }
            if (_value[_position] == '/') { token = _value.Slice(_position, 1); _position++; return true; }
            int tokenStart = _position;
            while (_position < _value.Length && !char.IsWhiteSpace(_value[_position]) && _value[_position] != '/') _position++;
            token = _value[tokenStart.._position];
            return true;
        }
    }
}
