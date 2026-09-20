namespace Lokad.Svg.Parsing;

/// <summary>Represents the supported specified-style declarations before cascade resolution.</summary>
internal readonly record struct InlineStyle(string? Color, string? Fill, string? FillOpacity, string? Stroke, string? Display, string? Visibility, string? Opacity, string? FillRule)
{
    internal SvgTextInlineFontProperties TextFont { get; init; }
}

/// <summary>Parses the bounded inline declaration subset shared by element and stylesheet rules.</summary>
internal static class SvgInlineStyleParser
{
    internal static InlineStyle Parse(string? value) => value is null ? default : Parse(value.AsSpan());

    internal static InlineStyle Parse(string? value, ParserWorkCounter workCounter, CancellationToken cancellationToken)
        => value is null ? default : Parse(value.AsSpan(), workCounter, cancellationToken);

    /// <summary>Parses inline paint declarations while tolerating the direct-text font declarations handled by text syntax.</summary>
    internal static InlineStyle Parse(string? value, ParserWorkCounter workCounter, CancellationToken cancellationToken, bool allowsDirectTextFontProperties)
        => value is null ? default : Parse(value.AsSpan(), workCounter, cancellationToken, allowsDirectTextFontProperties);

    internal static InlineStyle Parse(ReadOnlySpan<char> value)
        => ParseCore(value, null, default, null, false);

    internal static InlineStyle Parse(ReadOnlySpan<char> value, ParserWorkCounter workCounter, CancellationToken cancellationToken)
        => ParseCore(value, workCounter, cancellationToken, null, false);

    private static InlineStyle Parse(ReadOnlySpan<char> value, ParserWorkCounter workCounter, CancellationToken cancellationToken, bool allowsDirectTextFontProperties)
        => ParseCore(value, workCounter, cancellationToken, null, allowsDirectTextFontProperties);

    internal static InlineStyle Parse(ReadOnlySpan<char> value, BudgetedParserStringStore strings, ParserWorkCounter workCounter, CancellationToken cancellationToken)
        => ParseCore(value, workCounter, cancellationToken, strings, false);

    internal static InlineStyle Parse(ReadOnlySpan<char> value, BudgetedParserStringStore strings, ParserWorkCounter workCounter, CancellationToken cancellationToken, bool allowsDirectTextFontProperties)
        => ParseCore(value, workCounter, cancellationToken, strings, allowsDirectTextFontProperties);

    private static InlineStyle ParseCore(ReadOnlySpan<char> value, ParserWorkCounter? workCounter, CancellationToken cancellationToken, BudgetedParserStringStore? strings, bool allowsDirectTextFontProperties)
    {
        string? color = null, fill = null, fillOpacity = null, stroke = null, display = null, visibility = null, opacity = null, fillRule = null;
        string? family = null, weight = null, fontStyle = null, size = null;
        SvgInlineDeclarationReader declarations = new(value, workCounter, cancellationToken);
        while (declarations.TryRead(out ReadOnlySpan<char> declaration))
        {
            int separator = declaration.IndexOf(':');
            if (separator <= 0 || declaration[(separator + 1)..].Contains(':')) throw new SvgException(SvgErrorCode.MalformedInput, "Inline style declarations must use property:value syntax.");
            ReadOnlySpan<char> property = declaration[..separator].Trim();
            ReadOnlySpan<char> propertyValue = declaration[(separator + 1)..].Trim();
            if (property.Length > SvgSyntaxLimits.MaximumValueTokenCharacters || propertyValue.Length > SvgSyntaxLimits.MaximumValueTokenCharacters) throw new SvgException(SvgErrorCode.LimitExceeded, "Inline style property names and values exceed the configured syntax limit.");
            if (propertyValue.Length == 0 || propertyValue.Contains("!important".AsSpan(), StringComparison.OrdinalIgnoreCase)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Empty and !important inline style declarations are not implemented.");
            if (property.Equals("color", StringComparison.OrdinalIgnoreCase)) color = Copy(propertyValue, strings);
            else if (property.Equals("fill", StringComparison.OrdinalIgnoreCase)) fill = Copy(propertyValue, strings);
            else if (property.Equals("fill-opacity", StringComparison.OrdinalIgnoreCase)) fillOpacity = Copy(propertyValue, strings);
            else if (property.Equals("stroke", StringComparison.OrdinalIgnoreCase)) stroke = Copy(propertyValue, strings);
            else if (property.Equals("display", StringComparison.OrdinalIgnoreCase)) display = Copy(propertyValue, strings);
            else if (property.Equals("visibility", StringComparison.OrdinalIgnoreCase)) visibility = Copy(propertyValue, strings);
            else if (property.Equals("opacity", StringComparison.OrdinalIgnoreCase)) opacity = Copy(propertyValue, strings);
            else if (property.Equals("fill-rule", StringComparison.OrdinalIgnoreCase)) fillRule = Copy(propertyValue, strings);
            else if (allowsDirectTextFontProperties && property.Equals("font-family", StringComparison.OrdinalIgnoreCase)) family = Copy(propertyValue, strings);
            else if (allowsDirectTextFontProperties && property.Equals("font-weight", StringComparison.OrdinalIgnoreCase)) weight = Copy(propertyValue, strings);
            else if (allowsDirectTextFontProperties && property.Equals("font-style", StringComparison.OrdinalIgnoreCase)) fontStyle = Copy(propertyValue, strings);
            else if (allowsDirectTextFontProperties && property.Equals("font-size", StringComparison.OrdinalIgnoreCase)) size = Copy(propertyValue, strings);
            else throw new SvgException(SvgErrorCode.UnsupportedFeature, "Inline style property '" + property.ToString() + "' is not implemented.");
        }
        return new InlineStyle(color, fill, fillOpacity, stroke, display, visibility, opacity, fillRule) { TextFont = new SvgTextInlineFontProperties(family, weight, fontStyle, size) };
    }

    private static string Copy(ReadOnlySpan<char> value, BudgetedParserStringStore? strings) => strings is null ? value.ToString() : strings.Copy(value);

    /// <summary>Streams nonempty semicolon-delimited declarations while charging each 4 KiB scan checkpoint once.</summary>
    internal ref struct SvgInlineDeclarationReader
    {
        private readonly ReadOnlySpan<char> _value;
        private readonly ParserWorkCounter? _workCounter;
        private readonly CancellationToken _cancellationToken;
        private int _position;
        private int _nextCheckpoint;

        internal SvgInlineDeclarationReader(ReadOnlySpan<char> value, ParserWorkCounter? workCounter, CancellationToken cancellationToken)
        {
            _value = value;
            _workCounter = workCounter;
            _cancellationToken = cancellationToken;
        }

        internal bool TryRead(out ReadOnlySpan<char> declaration)
        {
            while (_position < _value.Length)
            {
                ConsumeCheckpoints();
                int start = _position;
                while (_position < _value.Length && _value[_position] != ';') { _position++; ConsumeCheckpoints(); }
                declaration = _value[start.._position].Trim();
                if (_position < _value.Length) _position++;
                ConsumeCheckpoints();
                if (!declaration.IsEmpty) return true;
            }
            declaration = default;
            return false;
        }

        private void ConsumeCheckpoints()
        {
            while (_nextCheckpoint <= _value.Length && _position >= _nextCheckpoint)
            {
                _workCounter?.Consume(_cancellationToken);
                _nextCheckpoint += 4096;
            }
        }
    }
}
