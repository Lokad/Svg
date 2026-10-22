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
    internal string? WordSpacing { get; init; }
    internal string? TextDecoration { get; init; }
    internal string? Linecap { get; init; }
    internal string? Linejoin { get; init; }
    internal string? Dasharray { get; init; }
    internal string? Dashoffset { get; init; }
    internal string? Width { get; init; }
    internal string? GeometryWidth { get; init; }
    internal string? GeometryHeight { get; init; }
    internal string? Miterlimit { get; init; }
    internal string? StrokeOpacity { get; init; }
    internal string? PathLength { get; init; }
    internal string? ClipRule { get; init; }
    internal string? PaintOrder { get; init; }
    internal string? MarkerStart { get; init; }
    internal string? MarkerMid { get; init; }
    internal string? MarkerEnd { get; init; }
    internal string? Filter { get; init; }
    /// <summary>Retains overflow independently so hidden/visible declarations follow ordinary cascade priority.</summary>
    internal string? Overflow { get; init; }
}


/// <summary>Identifies the specified-style declarations carrying !important priority.</summary>
[Flags]
internal enum InlineImportantProperties : long
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
    Miterlimit = 1 << 19,
    StrokeOpacity = 1 << 20,
    PathLength = 1 << 21,
    GeometryWidth = 1 << 22,
    GeometryHeight = 1 << 23,
    ClipRule = 1 << 24,
    PaintOrder = 1 << 25,
    MarkerStart = 1 << 26,
    MarkerMid = 1 << 27,
    MarkerEnd = 1 << 28,
    Filter = 1 << 29,
    WordSpacing = 1 << 30,
    TextDecoration = 1L << 31,
    Overflow = 1L << 32,
}

/// <summary>Parses the bounded inline declaration subset shared by element and stylesheet rules.</summary>
internal static class SvgInlineStyleParser
{
    private enum Target : byte { Presentation, DirectText, Geometry, LocalUse }
    internal static InlineStyle Parse(string? value) => value is null ? default : Parse(value.AsSpan());

    internal static InlineStyle Parse(string? value, ParserWorkCounter workCounter, CancellationToken cancellationToken)
        => value is null ? default : Parse(value.AsSpan(), workCounter, cancellationToken);

    /// <summary>Parses inline paint declarations while tolerating the direct-text font declarations handled by text syntax.</summary>
    internal static InlineStyle Parse(string? value, ParserWorkCounter workCounter, CancellationToken cancellationToken, bool allowsDirectTextFontProperties)
        => value is null ? default : Parse(value.AsSpan(), workCounter, cancellationToken, allowsDirectTextFontProperties);

    internal static InlineStyle Parse(ReadOnlySpan<char> value)
        => ParseCore(value, null, default, null, Target.Presentation);

    internal static InlineStyle Parse(ReadOnlySpan<char> value, ParserWorkCounter workCounter, CancellationToken cancellationToken)
        => ParseCore(value, workCounter, cancellationToken, null, Target.Presentation);

    private static InlineStyle Parse(ReadOnlySpan<char> value, ParserWorkCounter workCounter, CancellationToken cancellationToken, bool allowsDirectTextFontProperties)
        => ParseCore(value, workCounter, cancellationToken, null, allowsDirectTextFontProperties ? Target.DirectText : Target.Presentation);

    internal static InlineStyle Parse(ReadOnlySpan<char> value, BudgetedParserStringStore strings, ParserWorkCounter workCounter, CancellationToken cancellationToken)
        => ParseCore(value, workCounter, cancellationToken, strings, Target.Presentation);

    internal static InlineStyle Parse(ReadOnlySpan<char> value, BudgetedParserStringStore strings, ParserWorkCounter workCounter, CancellationToken cancellationToken, bool allowsDirectTextFontProperties)
        => ParseCore(value, workCounter, cancellationToken, strings, allowsDirectTextFontProperties ? Target.DirectText : Target.Presentation);

    internal static InlineStyle ParseGeometry(string? value, ParserWorkCounter workCounter, CancellationToken cancellationToken)
        => value is null ? default : ParseCore(value.AsSpan(), workCounter, cancellationToken, null, Target.Geometry);

    /// <summary>Parses use declarations once, retaining the existing paint-only and container admission rules.</summary>
    internal static InlineStyle ParseUse(string? value, ParserWorkCounter workCounter, CancellationToken cancellationToken)
        => value is null ? default : ParseCore(value.AsSpan(), workCounter, cancellationToken, null, Target.LocalUse);

    private static InlineStyle ParseCore(ReadOnlySpan<char> value, ParserWorkCounter? workCounter, CancellationToken cancellationToken, BudgetedParserStringStore? strings, Target target)
    {
        bool allowsDirectTextFontProperties = target == Target.DirectText;
        string? color = null, fill = null, fillOpacity = null, stroke = null, display = null, visibility = null, opacity = null, fillRule = null, clipRule = null, paintOrder = null;
        string? family = null, weight = null, fontStyle = null, size = null, whiteSpace = null, letterSpacing = null, wordSpacing = null, textDecoration = null;
        string? linecap = null, linejoin = null, dasharray = null, dashoffset = null, width = null, miterlimit = null, strokeOpacity = null, pathLength = null, geometryWidth = null, geometryHeight = null;
        string? markerStart = null, markerMid = null, markerEnd = null, filter = null, overflow = null;
        InlineImportantProperties important = InlineImportantProperties.None;
        Span<char> commentScratch = stackalloc char[2 * SvgSyntaxLimits.MaximumValueTokenCharacters + 1];
        SvgInlineDeclarationReader declarations = new(value, workCounter, cancellationToken);
        while (declarations.TryRead(out ReadOnlySpan<char> rawDeclaration))
        {
            ReadOnlySpan<char> declaration = SvgCssComments.NormalizeDeclaration(rawDeclaration, commentScratch, workCounter, cancellationToken);
            if (declaration.IsEmpty) continue;
            int separator = declaration.IndexOf(':');
            if (separator <= 0) throw new SvgException(SvgErrorCode.MalformedInput, "Inline style declarations must use property:value syntax.");
            ReadOnlySpan<char> property = declaration[..separator].Trim();
            ReadOnlySpan<char> propertyValue = declaration[(separator + 1)..].Trim();
            if (propertyValue.Contains(':') && !property.Equals("font-family", StringComparison.OrdinalIgnoreCase))
                throw new SvgException(SvgErrorCode.MalformedInput, "Inline style declarations must use property:value syntax.");
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
            else if (property.Equals("paint-order", StringComparison.OrdinalIgnoreCase)) { TryStripImportant(propertyValue, out ReadOnlySpan<char> orderValue); _ = SvgPaintOrderSyntax.Parse(orderValue); Set(ref paintOrder, InlineImportantProperties.PaintOrder, property, propertyValue, strings, ref important, false); }
            else if (!allowsDirectTextFontProperties && (property.Equals("stop-color", StringComparison.OrdinalIgnoreCase) || property.Equals("stop-opacity", StringComparison.OrdinalIgnoreCase)))
            {
                // A geometry leaf cannot contain gradient stops. Validate exporter stop
                // presentation here without applying it to fill or stroke. Containers
                // still reject it until inherited gradient-stop presentation is admitted.
                TryStripImportant(propertyValue, out ReadOnlySpan<char> stopValue);
                if (property.Equals("stop-opacity", StringComparison.OrdinalIgnoreCase)) _ = SvgColorParser.ParsePaintOpacity(stopValue.ToString());
                else if (!stopValue.Equals("currentColor", StringComparison.OrdinalIgnoreCase)) _ = SvgColorParser.Parse(stopValue.ToString(), SvgRootParser.CssNamedColors);
                if (target == Target.LocalUse) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Inline style property '" + property.ToString() + "' is not implemented.");
            }
            else if (property.Equals("stroke-linecap", StringComparison.OrdinalIgnoreCase)) { ValidateStrokeDeclaration("stroke-linecap", propertyValue); Set(ref linecap, InlineImportantProperties.Linecap, property, propertyValue, strings, ref important, true); }
            else if (property.Equals("stroke-linejoin", StringComparison.OrdinalIgnoreCase)) { ValidateStrokeDeclaration("stroke-linejoin", propertyValue); Set(ref linejoin, InlineImportantProperties.Linejoin, property, propertyValue, strings, ref important, true); }
            else if (property.Equals("stroke-dasharray", StringComparison.OrdinalIgnoreCase)) { ValidateStrokeDeclaration("stroke-dasharray", propertyValue); Set(ref dasharray, InlineImportantProperties.Dasharray, property, propertyValue, strings, ref important, true); }
            else if (property.Equals("stroke-dashoffset", StringComparison.OrdinalIgnoreCase)) { ValidateStrokeDeclaration("stroke-dashoffset", propertyValue); Set(ref dashoffset, InlineImportantProperties.Dashoffset, property, propertyValue, strings, ref important, true); }
            else if (property.Equals("stroke-width", StringComparison.OrdinalIgnoreCase)) Set(ref width, InlineImportantProperties.Width, property, propertyValue, strings, ref important, true);
            else if (property.Equals("stroke-miterlimit", StringComparison.OrdinalIgnoreCase)) Set(ref miterlimit, InlineImportantProperties.Miterlimit, property, propertyValue, strings, ref important, true);
            else if (property.Equals("stroke-opacity", StringComparison.OrdinalIgnoreCase)) Set(ref strokeOpacity, InlineImportantProperties.StrokeOpacity, property, propertyValue, strings, ref important, true);
            else if (property.Equals("pathLength", StringComparison.OrdinalIgnoreCase)) Set(ref pathLength, InlineImportantProperties.PathLength, property, propertyValue, strings, ref important, false);
            else if (property.Equals("width", StringComparison.OrdinalIgnoreCase)) Set(ref geometryWidth, InlineImportantProperties.GeometryWidth, property, propertyValue, strings, ref important, false);
            else if (property.Equals("height", StringComparison.OrdinalIgnoreCase)) Set(ref geometryHeight, InlineImportantProperties.GeometryHeight, property, propertyValue, strings, ref important, false);
            else if (property.Equals("clip-rule", StringComparison.OrdinalIgnoreCase)) { ValidateClipDeclaration(propertyValue); Set(ref clipRule, InlineImportantProperties.ClipRule, property, propertyValue, strings, ref important, true); }
            else if (property.Equals("overflow", StringComparison.OrdinalIgnoreCase))
            {
                TryStripImportant(propertyValue, out ReadOnlySpan<char> stripped);
                if (!stripped.Equals("visible".AsSpan(), StringComparison.OrdinalIgnoreCase)) SvgNestedViewportSyntax.ValidateOverflow(stripped);
                Set(ref overflow, InlineImportantProperties.Overflow, property, propertyValue, strings, ref important, false);
            }
            else if (property.Equals("preserveAspectRatio", StringComparison.OrdinalIgnoreCase)) ValidateImagePreserveAspectRatioDeclaration(propertyValue);
            else if (target == Target.Geometry && ValidateInertTypography(property, propertyValue, workCounter, cancellationToken)) { }
            else if (ValidateRenderingDefaults(property, propertyValue, target == Target.Geometry, workCounter, cancellationToken)) { }
            else if (property.Equals("font-variant", StringComparison.OrdinalIgnoreCase)) ValidateFontVariantDeclaration(propertyValue);
            else if (property.Equals("color-interpolation", StringComparison.OrdinalIgnoreCase)) ValidateColorInterpolationDeclaration(propertyValue);
            else if (property.Equals("marker", StringComparison.OrdinalIgnoreCase) || property.Equals("marker-start", StringComparison.OrdinalIgnoreCase) || property.Equals("marker-mid", StringComparison.OrdinalIgnoreCase) || property.Equals("marker-end", StringComparison.OrdinalIgnoreCase))
            {
                TryStripImportant(propertyValue, out ReadOnlySpan<char> stripped);
                SvgMarkerSyntax.ValidateValue(stripped);
                if (property.Equals("marker", StringComparison.OrdinalIgnoreCase) || property.Equals("marker-start", StringComparison.OrdinalIgnoreCase)) Set(ref markerStart, InlineImportantProperties.MarkerStart, property, propertyValue, strings, ref important, false);
                if (property.Equals("marker", StringComparison.OrdinalIgnoreCase) || property.Equals("marker-mid", StringComparison.OrdinalIgnoreCase)) Set(ref markerMid, InlineImportantProperties.MarkerMid, property, propertyValue, strings, ref important, false);
                if (property.Equals("marker", StringComparison.OrdinalIgnoreCase) || property.Equals("marker-end", StringComparison.OrdinalIgnoreCase)) Set(ref markerEnd, InlineImportantProperties.MarkerEnd, property, propertyValue, strings, ref important, false);
            }
            else if (property.Equals("filter", StringComparison.OrdinalIgnoreCase)) Set(ref filter, InlineImportantProperties.Filter, property, propertyValue, strings, ref important, false);
            else if (property.Equals("enable-background", StringComparison.OrdinalIgnoreCase))
            {
                TryStripImportant(propertyValue, out ReadOnlySpan<char> stripped);
                SvgInertPresentationSyntax.ValidateBackground(stripped, workCounter, cancellationToken);
            }
            else if (allowsDirectTextFontProperties && property.Equals("font-family", StringComparison.OrdinalIgnoreCase)) Set(ref family, InlineImportantProperties.Family, property, propertyValue, strings, ref important, false);
            else if (allowsDirectTextFontProperties && property.Equals("font-weight", StringComparison.OrdinalIgnoreCase)) Set(ref weight, InlineImportantProperties.Weight, property, propertyValue, strings, ref important, false);
            else if (allowsDirectTextFontProperties && property.Equals("font-style", StringComparison.OrdinalIgnoreCase)) Set(ref fontStyle, InlineImportantProperties.FontStyle, property, propertyValue, strings, ref important, false);
            else if (allowsDirectTextFontProperties && property.Equals("font-size", StringComparison.OrdinalIgnoreCase))
            {
                TryStripImportant(propertyValue, out ReadOnlySpan<char> fontSize);
                // Nonzero CSS lengths require a unit. Ignore this invalid
                // declaration without replacing an earlier valid value;
                // unitless presentation attributes keep their SVG semantics.
                if (double.TryParse(fontSize, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double numericSize)
                    && double.IsFinite(numericSize) && numericSize != 0) continue;
                Set(ref size, InlineImportantProperties.FontSize, property, propertyValue, strings, ref important, false);
            }
            else if (allowsDirectTextFontProperties && property.Equals("white-space", StringComparison.OrdinalIgnoreCase)) Set(ref whiteSpace, InlineImportantProperties.WhiteSpace, property, propertyValue, strings, ref important, false);
            else if (allowsDirectTextFontProperties && property.Equals("letter-spacing", StringComparison.OrdinalIgnoreCase)) Set(ref letterSpacing, InlineImportantProperties.LetterSpacing, property, propertyValue, strings, ref important, false);
            else if (allowsDirectTextFontProperties && property.Equals("word-spacing", StringComparison.OrdinalIgnoreCase)) Set(ref wordSpacing, InlineImportantProperties.WordSpacing, property, propertyValue, strings, ref important, false);
            else if (allowsDirectTextFontProperties && property.Equals("text-decoration", StringComparison.OrdinalIgnoreCase)) Set(ref textDecoration, InlineImportantProperties.TextDecoration, property, propertyValue, strings, ref important, false);
            else if (property.Equals("pointer-events", StringComparison.OrdinalIgnoreCase) || property.Equals("cursor", StringComparison.OrdinalIgnoreCase)) { } // UI behavior never affects static pixels.
            else throw new SvgException(SvgErrorCode.UnsupportedFeature, "Inline style property '" + property.ToString() + "' is not implemented.");
        }
        return new InlineStyle(color, fill, fillOpacity, stroke, display, visibility, opacity, fillRule) { TextFont = new SvgTextInlineFontProperties(family, weight, fontStyle, size), WhiteSpace = whiteSpace, LetterSpacing = letterSpacing, WordSpacing = wordSpacing, TextDecoration = textDecoration, Linecap = linecap, Linejoin = linejoin, Dasharray = dasharray, Dashoffset = dashoffset, Width = width, Miterlimit = miterlimit, StrokeOpacity = strokeOpacity, PathLength = pathLength, ClipRule = clipRule, PaintOrder = paintOrder, MarkerStart = markerStart, MarkerMid = markerMid, MarkerEnd = markerEnd, Filter = filter, Overflow = overflow, GeometryWidth = geometryWidth, GeometryHeight = geometryHeight, Important = important };

        static void Set(ref string? slot, InlineImportantProperties flag, ReadOnlySpan<char> property, ReadOnlySpan<char> rawValue, BudgetedParserStringStore? strings, ref InlineImportantProperties important, bool mapKeywords)
        {
            bool isImportant = TryStripImportant(rawValue, out ReadOnlySpan<char> stripped);
            if (stripped.IsEmpty) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Empty inline style declarations are not implemented.");
            if (ContainsUnquotedImportant(stripped)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Misplaced !important inline style declarations are not implemented.");
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
            if (TryMapPaintKeyword(name.AsSpan(), stripped, out _)) return;
            SvgStrokeStyleSyntax.ValidateAttribute(name, stripped.ToString(), true, true, true, true, true);
        }

        static void ValidateImagePreserveAspectRatioDeclaration(ReadOnlySpan<char> rawValue)
        {
            TryStripImportant(rawValue, out ReadOnlySpan<char> stripped);
            _ = SvgImageSyntax.ParsePlacementAspectRatio(stripped.ToString());
        }

        static void ValidateClipDeclaration(ReadOnlySpan<char> rawValue)
        {
            TryStripImportant(rawValue, out ReadOnlySpan<char> stripped);
            if (TryMapPaintKeyword("clip-rule".AsSpan(), stripped, out _)) return;
            SvgClipPathSyntax.ValidateClipRule(stripped);
        }

        static void ValidateFontVariantDeclaration(ReadOnlySpan<char> rawValue)
        {
            TryStripImportant(rawValue, out ReadOnlySpan<char> stripped);
            SvgTextFontSyntax.ValidateFontVariant(stripped);
        }

        static bool ValidateInertTypography(ReadOnlySpan<char> property, ReadOnlySpan<char> raw, ParserWorkCounter? work, CancellationToken token)
        {
            TryStripImportant(raw, out ReadOnlySpan<char> value);
            if (ContainsUnquotedImportant(value))
                throw new SvgException(SvgErrorCode.UnsupportedFeature, "Misplaced !important inline style declarations are not implemented.");
            return SvgInertPresentationSyntax.TryValidateTypography(property, value, work, token);
        }

        static bool ValidateRenderingDefaults(ReadOnlySpan<char> property, ReadOnlySpan<char> raw, bool isGeometry, ParserWorkCounter? work, CancellationToken token)
        {
            TryStripImportant(raw, out ReadOnlySpan<char> value);
            if (ContainsUnquotedImportant(value))
                throw new SvgException(SvgErrorCode.UnsupportedFeature, "Misplaced !important inline style declarations are not implemented.");
            return SvgInertPresentationSyntax.TryValidateRenderingDefaults(property, value, isGeometry, work, token);
        }

        static void ValidateColorInterpolationDeclaration(ReadOnlySpan<char> rawValue)
        {
            TryStripImportant(rawValue, out ReadOnlySpan<char> stripped);
            SvgGradientSyntax.ValidateColorInterpolation(stripped);
        }

        static bool ContainsUnquotedImportant(ReadOnlySpan<char> value)
        {
            SvgCssQuotedState quotes = default;
            for (int index = 0; index < value.Length; index++)
                if (quotes.IsSyntax(value[index]) && value[index] == '!' && value[index..].StartsWith("!important".AsSpan(), StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
    }

    /// <summary>Strips a trailing unquoted importance marker, preserving the declaration's other text.</summary>
    internal static bool TryStripImportant(ReadOnlySpan<char> value, out ReadOnlySpan<char> stripped)
    {
        stripped = value;
        ReadOnlySpan<char> trimmed = value.Trim();
        if (trimmed.Length <= 9 || !trimmed[^9..].Equals("important".AsSpan(), StringComparison.OrdinalIgnoreCase)) return false;
        ReadOnlySpan<char> before = trimmed[..^9].TrimEnd();
        if (before.IsEmpty || before[^1] != '!') return false;
        SvgCssQuotedState quotes = default;
        for (int index = 0; index < before.Length; index++)
            if (!quotes.IsSyntax(before[index]) && index == before.Length - 1) return false;
        stripped = before[..^1].TrimEnd();
        return true;
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

    internal const string InheritedValue = "inherit";

    /// <summary>Preserves inheritance until the cascade has selected a winning declaration.</summary>
    /// <returns>True when the value is a supported global keyword. Throws for unplumbed inheritance.</returns>
    internal static bool TryMapPaintKeyword(ReadOnlySpan<char> attribute, ReadOnlySpan<char> value, out string? mapped)
    {
        ReadOnlySpan<char> token = value.Trim();
        if (token.Equals("inherit".AsSpan(), StringComparison.OrdinalIgnoreCase))
        {
            if (IsInheritedPaintProperty(attribute) || attribute.Equals("opacity".AsSpan(), StringComparison.OrdinalIgnoreCase))
            {
                mapped = InheritedValue;
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
            if (IsInheritedPaintProperty(attribute))
            {
                mapped = InheritedValue;
                return true;
            }
            mapped = InitialPaintValue(attribute);
            return true;
        }
        mapped = null;
        return false;
    }

    internal static bool IsInheritedPaintProperty(ReadOnlySpan<char> attribute)
        => attribute.Equals("color".AsSpan(), StringComparison.OrdinalIgnoreCase)
        || attribute.Equals("fill".AsSpan(), StringComparison.OrdinalIgnoreCase)
        || attribute.Equals("fill-opacity".AsSpan(), StringComparison.OrdinalIgnoreCase)
        || attribute.Equals("visibility".AsSpan(), StringComparison.OrdinalIgnoreCase)
        || attribute.Equals("fill-rule".AsSpan(), StringComparison.OrdinalIgnoreCase)
        || attribute.Equals("clip-rule".AsSpan(), StringComparison.OrdinalIgnoreCase)
        || attribute.Equals("stroke".AsSpan(), StringComparison.OrdinalIgnoreCase)
        || attribute.Equals("stroke-width".AsSpan(), StringComparison.OrdinalIgnoreCase)
        || attribute.Equals("stroke-linecap".AsSpan(), StringComparison.OrdinalIgnoreCase)
        || attribute.Equals("stroke-linejoin".AsSpan(), StringComparison.OrdinalIgnoreCase)
        || attribute.Equals("stroke-miterlimit".AsSpan(), StringComparison.OrdinalIgnoreCase)
        || attribute.Equals("stroke-dasharray".AsSpan(), StringComparison.OrdinalIgnoreCase)
        || attribute.Equals("stroke-dashoffset".AsSpan(), StringComparison.OrdinalIgnoreCase)
        || attribute.Equals("stroke-opacity".AsSpan(), StringComparison.OrdinalIgnoreCase);

    private static string InitialPaintValue(ReadOnlySpan<char> attribute)
    {
        if (attribute.Equals("color".AsSpan(), StringComparison.OrdinalIgnoreCase) || attribute.Equals("fill".AsSpan(), StringComparison.OrdinalIgnoreCase)) return "black";
        if (attribute.Equals("fill-opacity".AsSpan(), StringComparison.OrdinalIgnoreCase) || attribute.Equals("opacity".AsSpan(), StringComparison.OrdinalIgnoreCase) || attribute.Equals("stroke-opacity".AsSpan(), StringComparison.OrdinalIgnoreCase) || attribute.Equals("stroke-width".AsSpan(), StringComparison.OrdinalIgnoreCase)) return "1";
        if (attribute.Equals("stroke".AsSpan(), StringComparison.OrdinalIgnoreCase) || attribute.Equals("stroke-dasharray".AsSpan(), StringComparison.OrdinalIgnoreCase)) return "none";
        if (attribute.Equals("display".AsSpan(), StringComparison.OrdinalIgnoreCase)) return "inline";
        if (attribute.Equals("visibility".AsSpan(), StringComparison.OrdinalIgnoreCase)) return "visible";
        if (attribute.Equals("fill-rule".AsSpan(), StringComparison.OrdinalIgnoreCase) || attribute.Equals("clip-rule".AsSpan(), StringComparison.OrdinalIgnoreCase)) return "nonzero";
        if (attribute.Equals("stroke-linecap".AsSpan(), StringComparison.OrdinalIgnoreCase)) return "butt";
        if (attribute.Equals("stroke-linejoin".AsSpan(), StringComparison.OrdinalIgnoreCase)) return "miter";
        if (attribute.Equals("stroke-miterlimit".AsSpan(), StringComparison.OrdinalIgnoreCase)) return "4";
        if (attribute.Equals("stroke-dashoffset".AsSpan(), StringComparison.OrdinalIgnoreCase)) return "0";
        throw new SvgException(SvgErrorCode.UnsupportedFeature, "Initial paint values are not implemented for this property.");
    }

    /// <summary>Streams declarations, preserving quoted/escaped semicolons and charging each 4 KiB scan checkpoint once.</summary>
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
                SvgCssQuotedState quotes = default;
                while (_position < _value.Length)
                {
                    char character = _value[_position];
                    if (quotes.IsSyntax(character))
                    {
                        if (character == ';') break;
                        if (character == '/' && _position + 1 < _value.Length && _value[_position + 1] == '*')
                        {
                            _position = SvgCssComments.SkipComment(_value, _position, _workCounter, _cancellationToken);
                            ConsumeCheckpoints();
                            continue;
                        }
                    }
                    _position++; ConsumeCheckpoints();
                }
                if (!quotes.IsClosed) throw new SvgException(SvgErrorCode.MalformedInput, "An inline style string is not closed.");
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
