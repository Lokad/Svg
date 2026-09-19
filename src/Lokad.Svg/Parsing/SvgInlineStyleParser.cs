namespace Lokad.Svg.Parsing;

/// <summary>Represents the supported specified-style declarations before cascade resolution.</summary>
internal readonly record struct InlineStyle(string? Color, string? Fill, string? FillOpacity, string? Stroke, string? Display, string? Visibility, string? Opacity, string? FillRule);

/// <summary>Parses the bounded inline declaration subset shared by element and stylesheet rules.</summary>
internal static class SvgInlineStyleParser
{
    internal static InlineStyle Parse(string? value) => value is null ? default : Parse(value.AsSpan());

    internal static InlineStyle Parse(ReadOnlySpan<char> value)
    {
        string? color = null, fill = null, fillOpacity = null, stroke = null, display = null, visibility = null, opacity = null, fillRule = null;
        StyleDeclarationReader declarations = new(value);
        while (declarations.TryRead(out ReadOnlySpan<char> declaration))
        {
            int separator = declaration.IndexOf(':');
            if (separator <= 0 || declaration[(separator + 1)..].Contains(':')) throw new SvgException(SvgErrorCode.MalformedInput, "Inline style declarations must use property:value syntax.");
            ReadOnlySpan<char> property = declaration[..separator].Trim();
            ReadOnlySpan<char> propertyValue = declaration[(separator + 1)..].Trim();
            if (propertyValue.Length == 0 || propertyValue.Contains("!important".AsSpan(), StringComparison.OrdinalIgnoreCase)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Empty and !important inline style declarations are not implemented.");
            if (property.Equals("color", StringComparison.OrdinalIgnoreCase)) color = propertyValue.ToString();
            else if (property.Equals("fill", StringComparison.OrdinalIgnoreCase)) fill = propertyValue.ToString();
            else if (property.Equals("fill-opacity", StringComparison.OrdinalIgnoreCase)) fillOpacity = propertyValue.ToString();
            else if (property.Equals("stroke", StringComparison.OrdinalIgnoreCase)) stroke = propertyValue.ToString();
            else if (property.Equals("display", StringComparison.OrdinalIgnoreCase)) display = propertyValue.ToString();
            else if (property.Equals("visibility", StringComparison.OrdinalIgnoreCase)) visibility = propertyValue.ToString();
            else if (property.Equals("opacity", StringComparison.OrdinalIgnoreCase)) opacity = propertyValue.ToString();
            else if (property.Equals("fill-rule", StringComparison.OrdinalIgnoreCase)) fillRule = propertyValue.ToString();
            else throw new SvgException(SvgErrorCode.UnsupportedFeature, "Inline style property '" + property.ToString() + "' is not implemented.");
        }
        return new InlineStyle(color, fill, fillOpacity, stroke, display, visibility, opacity, fillRule);
    }

    private ref struct StyleDeclarationReader
    {
        private readonly ReadOnlySpan<char> _value;
        private int _position;

        internal StyleDeclarationReader(ReadOnlySpan<char> value) => _value = value;

        internal bool TryRead(out ReadOnlySpan<char> declaration)
        {
            while (_position < _value.Length)
            {
                int start = _position;
                while (_position < _value.Length && _value[_position] != ';') _position++;
                declaration = _value[start.._position].Trim();
                if (_position < _value.Length) _position++;
                if (!declaration.IsEmpty) return true;
            }
            declaration = default;
            return false;
        }
    }
}
