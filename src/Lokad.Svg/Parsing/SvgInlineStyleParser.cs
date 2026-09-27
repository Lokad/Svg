namespace Lokad.Svg.Parsing;

/// <summary>Represents the supported specified-style declarations before cascade resolution.</summary>
internal readonly record struct InlineStyle(string? Color, string? Fill, string? FillOpacity, string? Stroke, string? Display, string? Visibility, string? Opacity, string? FillRule)
{
    internal SvgTextInlineFontProperties TextFont { get; init; }

    /// <summary>Gets the per-property !important markers carried by the specified declarations.</summary>
    internal InlineImportantProperties Important { get; init; }

    /// <summary>Gets the specified CSS whitespace declaration when direct text admits it.</summary>
    internal string? WhiteSpace { get; init; }

    /// <summary>Gets the specified scalar CSS letter-spacing declaration when direct text admits it.</summary>
    internal string? LetterSpacing { get; init; }
    internal string? Linecap { get; init; }
    internal string? Linejoin { get; init; }
    internal string? Dasharray { get; init; }
    internal string? Dashoffset { get; init; }
    internal string? Width { get; init; }
}


/// <summary>Identifies the specified-style declarations carrying !important priority.</summary>
[Flags]
internal enum InlineImportantProperties
{
    None = 0,
    Color = 1 << 0,
    Fill = 1 << 1,
    FillOpacity = 1 << 2,
    Stroke = 1 << 3,
    Display = 1 << 4,
    Visibility = 1 << 5,
    Opacity = 1 << 6,
    FillRule = 1 << 7,
    Family = 1 << 8,
    Weight = 1 << 9,
    FontStyle = 1 << 10,
    FontSize = 1 << 11,
    WhiteSpace = 1 << 12,
    LetterSpacing = 1 << 13,
    Linecap = 1 << 14,
    Linejoin = 1 << 15,
    Dasharray = 1 << 16,
    Dashoffset = 1 << 17,
    Width = 1 << 18,
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
        string? family = null, weight = null, fontStyle = null, size = null, whiteSpace = null, letterSpacing = null;
        string? linecap = null, linejoin = null, dasharray = null, dashoffset = null, width = null;
        InlineImportantProperties important = InlineImportantProperties.None;
        SvgInlineDeclarationReader declarations = new(value, workCounter, cancellationToken);
        while (declarations.TryRead(out ReadOnlySpan<char> declaration))
        {
            int separator = declaration.IndexOf(':');
            if (separator <= 0 || declaration[(separator + 1)..].Contains(':')) throw new SvgException(SvgErrorCode.MalformedInput, "Inline style declarations must use property:value syntax.");
            ReadOnlySpan<char> property = declaration[..separator].Trim();
            ReadOnlySpan<char> propertyValue = declaration[(separator + 1)..].Trim();
            if (property.Length > SvgSyntaxLimits.MaximumValueTokenCharacters || propertyValue.Length > SvgSyntaxLimits.MaximumValueTokenCharacters) throw new SvgException(SvgErrorCode.LimitExceeded, "Inline style property names and values exceed the configured syntax limit.");
            if (propertyValue.Length == 0) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Empty inline style declarations are not implemented.");
            if (property.Equals("color", StringComparison.OrdinalIgnoreCase)) Set(ref color, InlineImportantProperties.Color, property, propertyValue, strings, ref important, true);
            else if (property.Equals("fill", StringComparison.OrdinalIgnoreCase)) Set(ref fill, InlineImportantProperties.Fill, property, propertyValue, strings, ref important, true);
            else if (property.Equals("fill-opacity", StringComparison.OrdinalIgnoreCase)) Set(ref fillOpacity, InlineImportantProperties.FillOpacity, property, propertyValue, strings, ref important, true);
            else if (property.Equals("stroke", StringComparison.OrdinalIgnoreCase)) Set(ref stroke, InlineImportantProperties.Stroke, property, propertyValue, strings, ref important, true);
            else if (property.Equals("display", StringComparison.OrdinalIgnoreCase)) Set(ref display, InlineImportantProperties.Display, property, propertyValue, strings, ref important, true);
            else if (property.Equals("visibility", StringComparison.OrdinalIgnoreCase)) Set(ref visibility, InlineImportantProperties.Visibility, property, propertyValue, strings, ref important, true);
            else if (property.Equals("opacity", StringComparison.OrdinalIgnoreCase)) Set(ref opacity, InlineImportantProperties.Opacity, property, propertyValue, strings, ref important, true);
            else if (property.Equals("fill-rule", StringComparison.OrdinalIgnoreCase)) Set(ref fillRule, InlineImportantProperties.FillRule, property, propertyValue, strings, ref important, true);
            else if (property.Equals("stroke-linecap", StringComparison.OrdinalIgnoreCase)) { ValidateStrokeDeclaration("stroke-linecap", propertyValue); Set(ref linecap, InlineImportantProperties.Linecap, property, propertyValue, strings, ref important, false); }
            else if (property.Equals("stroke-linejoin", StringComparison.OrdinalIgnoreCase)) { ValidateStrokeDeclaration("stroke-linejoin", propertyValue); Set(ref linejoin, InlineImportantProperties.Linejoin, property, propertyValue, strings, ref important, false); }
            else if (property.Equals("stroke-dasharray", StringComparison.OrdinalIgnoreCase)) { ValidateStrokeDeclaration("stroke-dasharray", propertyValue); Set(ref dasharray, InlineImportantProperties.Dasharray, property, propertyValue, strings, ref important, false); }
            else if (property.Equals("stroke-dashoffset", StringComparison.OrdinalIgnoreCase)) { ValidateStrokeDeclaration("stroke-dashoffset", propertyValue); Set(ref dashoffset, InlineImportantProperties.Dashoffset, property, propertyValue, strings, ref important, false); }
            else if (property.Equals("stroke-width", StringComparison.OrdinalIgnoreCase)) Set(ref width, InlineImportantProperties.Width, property, propertyValue, strings, ref important, false);
            else if (property.Equals("pathLength", StringComparison.OrdinalIgnoreCase)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Inline path lengths are not implemented.");
            else if (property.Equals("clip-rule", StringComparison.OrdinalIgnoreCase)) ValidateClipDeclaration(propertyValue);
            else if (property.Equals("overflow", StringComparison.OrdinalIgnoreCase)) ValidateOverflowDeclaration(propertyValue);
            else if (property.Equals("preserveAspectRatio", StringComparison.OrdinalIgnoreCase)) ValidateImagePreserveAspectRatioDeclaration(propertyValue);
            else if (property.Equals("font-variant", StringComparison.OrdinalIgnoreCase)) ValidateFontVariantDeclaration(propertyValue);
            else if (property.Equals("color-interpolation", StringComparison.OrdinalIgnoreCase)) ValidateColorInterpolationDeclaration(propertyValue);
            else if (allowsDirectTextFontProperties && property.Equals("font-family", StringComparison.OrdinalIgnoreCase)) Set(ref family, InlineImportantProperties.Family, property, propertyValue, strings, ref important, false);
            else if (allowsDirectTextFontProperties && property.Equals("font-weight", StringComparison.OrdinalIgnoreCase)) Set(ref weight, InlineImportantProperties.Weight, property, propertyValue, strings, ref important, false);
            else if (allowsDirectTextFontProperties && property.Equals("font-style", StringComparison.OrdinalIgnoreCase)) Set(ref fontStyle, InlineImportantProperties.FontStyle, property, propertyValue, strings, ref important, false);
            else if (allowsDirectTextFontProperties && property.Equals("font-size", StringComparison.OrdinalIgnoreCase)) Set(ref size, InlineImportantProperties.FontSize, property, propertyValue, strings, ref important, false);
            else if (allowsDirectTextFontProperties && property.Equals("white-space", StringComparison.OrdinalIgnoreCase)) Set(ref whiteSpace, InlineImportantProperties.WhiteSpace, property, propertyValue, strings, ref important, false);
            else if (allowsDirectTextFontProperties && property.Equals("letter-spacing", StringComparison.OrdinalIgnoreCase)) Set(ref letterSpacing, InlineImportantProperties.LetterSpacing, property, propertyValue, strings, ref important, false);
            else if (property.Equals("pointer-events", StringComparison.OrdinalIgnoreCase) || property.Equals("cursor", StringComparison.OrdinalIgnoreCase)) { } // UI behavior never affects static pixels.
            else throw new SvgException(SvgErrorCode.UnsupportedFeature, "Inline style property '" + property.ToString() + "' is not implemented.");
        }
        return new InlineStyle(color, fill, fillOpacity, stroke, display, visibility, opacity, fillRule) { TextFont = new SvgTextInlineFontProperties(family, weight, fontStyle, size), WhiteSpace = whiteSpace, LetterSpacing = letterSpacing, Linecap = linecap, Linejoin = linejoin, Dasharray = dasharray, Dashoffset = dashoffset, Width = width, Important = important };

        static void Set(ref string? slot, InlineImportantProperties flag, ReadOnlySpan<char> property, ReadOnlySpan<char> rawValue, BudgetedParserStringStore? strings, ref InlineImportantProperties important, bool mapKeywords)
        {
            bool isImportant = TryStripImportant(rawValue, out ReadOnlySpan<char> stripped);
            if (stripped.IsEmpty) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Empty inline style declarations are not implemented.");
            if (stripped.Contains("!important".AsSpan(), StringComparison.OrdinalIgnoreCase)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Misplaced !important inline style declarations are not implemented.");
            if (mapKeywords && TryMapPaintKeyword(property, stripped, out string? mapped))
            {
                if (!isImportant && (important & flag) != 0) return;
                slot = mapped;
                if (isImportant) important |= flag;
                else important &= ~flag;
                return;
            }
            if (!isImportant && (important & flag) != 0) return;
            slot = strings is null ? stripped.ToString() : strings.Copy(stripped);
            if (isImportant) important |= flag;
            else important &= ~flag;
        }


        static void ValidateStrokeDeclaration(string name, ReadOnlySpan<char> rawValue)
        {
            TryStripImportant(rawValue, out ReadOnlySpan<char> stripped);
            if (name == "stroke-dasharray" || name == "stroke-dashoffset") SvgStrokeStyleSyntax.ValidateAttribute(name, stripped.ToString(), true, true, true, true, true);
            else SvgStrokeStyleSyntax.ValidateAttribute(name, stripped.ToString());
        }

        static void ValidateImagePreserveAspectRatioDeclaration(ReadOnlySpan<char> rawValue)
        {
            TryStripImportant(rawValue, out ReadOnlySpan<char> stripped);
            _ = SvgImageSyntax.ParsePlacementAspectRatio(stripped.ToString());
        }

        static void ValidateOverflowDeclaration(ReadOnlySpan<char> rawValue)
        {
            TryStripImportant(rawValue, out ReadOnlySpan<char> stripped);
            SvgNestedViewportSyntax.ValidateOverflow(stripped);
        }

        static void ValidateClipDeclaration(ReadOnlySpan<char> rawValue)
        {
            TryStripImportant(rawValue, out ReadOnlySpan<char> stripped);
            SvgClipPathSyntax.ValidateClipRule(stripped);
        }

        static void ValidateFontVariantDeclaration(ReadOnlySpan<char> rawValue)
        {
            TryStripImportant(rawValue, out ReadOnlySpan<char> stripped);
            SvgTextFontSyntax.ValidateFontVariant(stripped);
        }

        static void ValidateColorInterpolationDeclaration(ReadOnlySpan<char> rawValue)
        {
            TryStripImportant(rawValue, out ReadOnlySpan<char> stripped);
            SvgGradientSyntax.ValidateColorInterpolation(stripped);
        }

        static bool TryStripImportant(ReadOnlySpan<char> value, out ReadOnlySpan<char> stripped)
        {
            stripped = value;
            ReadOnlySpan<char> trimmed = value.Trim();
            if (trimmed.Length <= 9 || !trimmed[^9..].Equals("important".AsSpan(), StringComparison.OrdinalIgnoreCase)) return false;
            ReadOnlySpan<char> before = trimmed[..^9].TrimEnd();
            if (before.IsEmpty || before[^1] != '!') return false;
            stripped = before[..^1].TrimEnd();
            return true;
        }
    }


    /// <summary>Merges one specified-style value with !important priority before fallback order.</summary>
    internal static string? MergeWithImportance(string? upper, InlineImportantProperties upperImportant, InlineImportantProperties flag, string? lower, InlineImportantProperties lowerImportant)
    {
        if ((upperImportant & flag) != 0 && upper is not null) return upper;
        if ((lowerImportant & flag) != 0 && lower is not null) return lower;
        return upper ?? lower;
    }

    /// <summary>Merges one inline font declaration block with !important priority.</summary>
    internal static SvgTextInlineFontProperties MergeFont(SvgTextInlineFontProperties upper, InlineImportantProperties upperImportant, SvgTextInlineFontProperties lower, InlineImportantProperties lowerImportant)
        => new(MergeWithImportance(upper.Family, upperImportant, InlineImportantProperties.Family, lower.Family, lowerImportant),
            MergeWithImportance(upper.Weight, upperImportant, InlineImportantProperties.Weight, lower.Weight, lowerImportant),
            MergeWithImportance(upper.Style, upperImportant, InlineImportantProperties.FontStyle, lower.Style, lowerImportant),
            MergeWithImportance(upper.Size, upperImportant, InlineImportantProperties.FontSize, lower.Size, lowerImportant));

    /// <summary>Maps an inherit, initial or unset paint keyword to its specified value.</summary>
    /// <returns>True when the value is a keyword, with the mapped value (null for inherited absence). Throws for unplumbed inherit.</returns>
    internal static bool TryMapPaintKeyword(ReadOnlySpan<char> attribute, ReadOnlySpan<char> value, out string? mapped)
    {
        ReadOnlySpan<char> token = value.Trim();
        if (token.Equals("inherit".AsSpan(), StringComparison.OrdinalIgnoreCase))
        {
            if (attribute.Equals("color".AsSpan(), StringComparison.OrdinalIgnoreCase) || attribute.Equals("fill".AsSpan(), StringComparison.OrdinalIgnoreCase) || attribute.Equals("fill-opacity".AsSpan(), StringComparison.OrdinalIgnoreCase) || attribute.Equals("visibility".AsSpan(), StringComparison.OrdinalIgnoreCase))
            {
                mapped = null;
                return true;
            }
            throw new SvgException(SvgErrorCode.UnsupportedFeature, "Inherited paint values are not implemented for this property.");
        }
        if (token.Equals("initial".AsSpan(), StringComparison.OrdinalIgnoreCase))
        {
            mapped = InitialPaintValue(attribute);
            return true;
        }
        if (token.Equals("unset".AsSpan(), StringComparison.OrdinalIgnoreCase))
        {
            if (attribute.Equals("color".AsSpan(), StringComparison.OrdinalIgnoreCase) || attribute.Equals("fill".AsSpan(), StringComparison.OrdinalIgnoreCase) || attribute.Equals("fill-opacity".AsSpan(), StringComparison.OrdinalIgnoreCase) || attribute.Equals("visibility".AsSpan(), StringComparison.OrdinalIgnoreCase))
            {
                mapped = null;
                return true;
            }
            mapped = InitialPaintValue(attribute);
            return true;
        }
        mapped = null;
        return false;
    }

    private static string InitialPaintValue(ReadOnlySpan<char> attribute)
    {
        if (attribute.Equals("color".AsSpan(), StringComparison.OrdinalIgnoreCase) || attribute.Equals("fill".AsSpan(), StringComparison.OrdinalIgnoreCase)) return "black";
        if (attribute.Equals("fill-opacity".AsSpan(), StringComparison.OrdinalIgnoreCase) || attribute.Equals("opacity".AsSpan(), StringComparison.OrdinalIgnoreCase)) return "1";
        if (attribute.Equals("stroke".AsSpan(), StringComparison.OrdinalIgnoreCase)) return "none";
        if (attribute.Equals("display".AsSpan(), StringComparison.OrdinalIgnoreCase)) return "inline";
        if (attribute.Equals("visibility".AsSpan(), StringComparison.OrdinalIgnoreCase)) return "visible";
        if (attribute.Equals("fill-rule".AsSpan(), StringComparison.OrdinalIgnoreCase)) return "nonzero";
        throw new SvgException(SvgErrorCode.UnsupportedFeature, "Initial paint values are not implemented for this property.");
    }

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
