using System.Xml;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Parsing;

/// <summary>Parses and resolves the deliberately narrow embedded stylesheet subset.</summary>
internal static class SvgStyleSheet
{
    internal static void Parse(string value, int maximumRules, BudgetedParserList<CssRule> rules, BudgetedParserStringStore strings, RenderBudget budget, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        for (int index = 0; index < value.Length; index += 4096) workCounter.Consume(cancellationToken);
        using OwnedStyleSheetText commentless = StripComments(value, budget, workCounter, cancellationToken);
        value = commentless.Value;
        int position = 0;
        while (position < value.Length)
        {
            position = SkipWhitespace(value, position, workCounter, cancellationToken);
            if (position == value.Length) break;
            int openingBrace = FindCharacter(value, '{', position, workCounter, cancellationToken);
            if (openingBrace < 0) throw new SvgException(SvgErrorCode.MalformedInput, "Stylesheet rules must contain a declaration block.");
            int closingBrace = FindCharacter(value, '}', openingBrace + 1, workCounter, cancellationToken);
            if (closingBrace < 0 || ContainsCharacter(value, '{', openingBrace + 1, closingBrace, workCounter, cancellationToken)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Nested stylesheet rules are not implemented.");
            InlineStyle style = SvgInlineStyleParser.Parse(value.AsSpan(openingBrace + 1, closingBrace - openingBrace - 1), strings, workCounter, cancellationToken);
            CssSelectorListReader selectors = new(value.AsSpan(position, openingBrace - position), workCounter, cancellationToken);
            while (selectors.TryRead(out ReadOnlySpan<char> selector))
            {
                if (rules.Count >= maximumRules) throw new SvgException(SvgErrorCode.LimitExceeded, "Stylesheet rule count exceeds the configured structural limit.");
                rules.Add(ParseSelector(selector, style, strings, workCounter, cancellationToken));
            }
            position = closingBrace + 1;
        }
        if (rules.Count == 0) throw new SvgException(SvgErrorCode.UnsupportedFeature, "A stylesheet must contain a rect, ID, or class selector rule.");
    }

    internal static InlineStyle Resolve(XmlReader reader, BudgetedParserList<CssRule> rules, bool directChild, bool hasGroupAncestor, bool parentIsGroup, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        string? color = null, fill = null, fillOpacity = null, stroke = null, display = null, visibility = null, opacity = null, fillRule = null;
        int colorSpecificity = -1, fillSpecificity = -1, fillOpacitySpecificity = -1, strokeSpecificity = -1, displaySpecificity = -1, visibilitySpecificity = -1, opacitySpecificity = -1, fillRuleSpecificity = -1;
        for (int ruleIndex = 0; ruleIndex < rules.Count; ruleIndex++)
        {
            CssRule rule = rules[ruleIndex];
            workCounter.Consume(cancellationToken);
            if (!rule.Matches(reader, directChild, hasGroupAncestor, parentIsGroup)) continue;
            if (rule.Style.Color is not null && rule.Specificity >= colorSpecificity) { color = rule.Style.Color; colorSpecificity = rule.Specificity; }
            if (rule.Style.Fill is not null && rule.Specificity >= fillSpecificity) { fill = rule.Style.Fill; fillSpecificity = rule.Specificity; }
            if (rule.Style.FillOpacity is not null && rule.Specificity >= fillOpacitySpecificity) { fillOpacity = rule.Style.FillOpacity; fillOpacitySpecificity = rule.Specificity; }
            if (rule.Style.Stroke is not null && rule.Specificity >= strokeSpecificity) { stroke = rule.Style.Stroke; strokeSpecificity = rule.Specificity; }
            if (rule.Style.Display is not null && rule.Specificity >= displaySpecificity) { display = rule.Style.Display; displaySpecificity = rule.Specificity; }
            if (rule.Style.Visibility is not null && rule.Specificity >= visibilitySpecificity) { visibility = rule.Style.Visibility; visibilitySpecificity = rule.Specificity; }
            if (rule.Style.Opacity is not null && rule.Specificity >= opacitySpecificity) { opacity = rule.Style.Opacity; opacitySpecificity = rule.Specificity; }
            if (rule.Style.FillRule is not null && rule.Specificity >= fillRuleSpecificity) { fillRule = rule.Style.FillRule; fillRuleSpecificity = rule.Specificity; }
        }
        return new InlineStyle(color, fill, fillOpacity, stroke, display, visibility, opacity, fillRule);
    }

    internal static bool TargetsRootId(string? rootId, BudgetedParserList<CssRule> rules, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        if (rootId is null) return false;
        for (int ruleIndex = 0; ruleIndex < rules.Count; ruleIndex++)
        {
            workCounter.Consume(cancellationToken);
            CssRule rule = rules[ruleIndex];
            if ((rule.Kind == CssSelectorKind.Id && string.Equals(rule.Value, rootId, StringComparison.Ordinal)) || (rule.Kind == CssSelectorKind.AttributePresent && rule.Value == "id") || (rule.Kind == CssSelectorKind.AttributeEquals && rule.Value == "id" && string.Equals(rule.ExpectedValue, rootId, StringComparison.Ordinal))) return true;
        }
        return false;
    }

    internal static string ReadElementText(XmlReader reader)
    {
        using XmlReader subtree = reader.ReadSubtree();
        subtree.Read();
        return subtree.ReadElementContentAsString();
    }

    internal static bool HasCssClass(string? value, string expected)
    {
        if (value is null) return false;
        ReadOnlySpan<char> classes = value.AsSpan();
        int position = 0;
        while (position < classes.Length)
        {
            while (position < classes.Length && char.IsWhiteSpace(classes[position])) position++;
            int start = position;
            while (position < classes.Length && !char.IsWhiteSpace(classes[position])) position++;
            if (classes[start..position].Equals(expected, StringComparison.Ordinal)) return true;
        }
        return false;
    }

    private static OwnedStyleSheetText StripComments(string value, RenderBudget budget, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        int sourceIndex = 0;
        int removedCharacters = 0;
        while (sourceIndex < value.Length)
        {
            if ((sourceIndex & 4095) == 0) workCounter.Consume(cancellationToken);
            if (value[sourceIndex] != '/' || sourceIndex + 1 == value.Length || value[sourceIndex + 1] != '*') { sourceIndex++; continue; }
            int commentStart = sourceIndex;
            sourceIndex += 2;
            while (sourceIndex + 1 < value.Length && (value[sourceIndex] != '*' || value[sourceIndex + 1] != '/'))
            {
                if ((sourceIndex & 4095) == 0) workCounter.Consume(cancellationToken);
                sourceIndex++;
            }
            if (sourceIndex + 1 >= value.Length) throw new SvgException(SvgErrorCode.MalformedInput, "Stylesheet comments must be terminated.");
            sourceIndex += 2;
            removedCharacters += sourceIndex - commentStart;
        }
        if (removedCharacters == 0) return new(value, null);
        int commentlessLength = value.Length - removedCharacters;
        if (commentlessLength == 0) return new(string.Empty, null);
        RenderBudget.BudgetReservation reservation = budget.Reserve(GetStringByteCount(commentlessLength), SvgMemoryCategory.Parser);
        try
        {
            return new(string.Create(commentlessLength, (Value: value, WorkCounter: workCounter, CancellationToken: cancellationToken), static (destination, state) =>
            {
                int sourceIndex = 0;
                int destinationIndex = 0;
                while (sourceIndex < state.Value.Length)
                {
                    if ((sourceIndex & 4095) == 0) state.WorkCounter.Consume(state.CancellationToken);
                    if (state.Value[sourceIndex] != '/' || sourceIndex + 1 == state.Value.Length || state.Value[sourceIndex + 1] != '*')
                    {
                        destination[destinationIndex++] = state.Value[sourceIndex++];
                        continue;
                    }
                    sourceIndex += 2;
                    while (state.Value[sourceIndex] != '*' || state.Value[sourceIndex + 1] != '/')
                    {
                        if ((sourceIndex & 4095) == 0) state.WorkCounter.Consume(state.CancellationToken);
                        sourceIndex++;
                    }
                    sourceIndex += 2;
                }
            }), reservation);
        }
        catch
        {
            reservation.Dispose();
            throw;
        }
    }

    private static long GetStringByteCount(int characterCount) => checked(32 + (long)characterCount * sizeof(char));

    private static CssRule ParseSelector(ReadOnlySpan<char> selector, InlineStyle style, BudgetedParserStringStore strings, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        CssSelectorTokenReader tokens = new(selector, workCounter, cancellationToken);
        if (!tokens.TryRead(out ReadOnlySpan<char> first)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only rect type, ID, class, id/class attribute, g descendant, and g child stylesheet selectors are implemented.");
        if (tokens.TryRead(out ReadOnlySpan<char> second))
        {
            if (first.Equals("g", StringComparison.OrdinalIgnoreCase) && second.Equals("rect", StringComparison.OrdinalIgnoreCase) && !tokens.TryRead(out _)) return new CssRule(CssSelectorKind.DescendantGroup, string.Empty, null, 2, style);
            if (first.Equals("g", StringComparison.OrdinalIgnoreCase) && second.SequenceEqual(">".AsSpan()) && tokens.TryRead(out ReadOnlySpan<char> third) && third.Equals("rect", StringComparison.OrdinalIgnoreCase) && !tokens.TryRead(out _)) return new CssRule(CssSelectorKind.ChildGroup, string.Empty, null, 2, style);
            throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only rect type, ID, class, id/class attribute, g descendant, and g child stylesheet selectors are implemented.");
        }
        if (TryParseCompoundSelector(first, "rect", CssSelectorKind.RectangleClass, CssSelectorKind.RectangleId, style, strings, out CssRule compoundRule)) return compoundRule;
        if (TryParseCompoundSelector(first, "g", CssSelectorKind.GroupClass, CssSelectorKind.GroupId, style, strings, out compoundRule)) return compoundRule;
        if (first.Equals("rect", StringComparison.OrdinalIgnoreCase)) return new CssRule(CssSelectorKind.Rectangle, string.Empty, null, 1, style);
        if (first.Equals("g", StringComparison.OrdinalIgnoreCase)) return new CssRule(CssSelectorKind.Group, string.Empty, null, 1, style);
        if (first.Length > 1 && first[0] == '#' && IsCssIdentifier(first[1..])) return new CssRule(CssSelectorKind.Id, strings.Copy(first[1..]), null, 100, style);
        if (first.Length > 1 && first[0] == '.' && IsCssIdentifier(first[1..])) return new CssRule(CssSelectorKind.Class, strings.Copy(first[1..]), null, 10, style);
        if (first.Length > 2 && first[0] == '[' && first[^1] == ']') return ParseAttributeSelector(first[1..^1], style, strings, workCounter, cancellationToken);
        throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only rect type, ID, class, id/class attribute, g descendant, and g child stylesheet selectors are implemented.");
    }

    private static CssRule ParseAttributeSelector(ReadOnlySpan<char> selector, InlineStyle style, BudgetedParserStringStore strings, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        int separator = FindCharacter(selector, '=', 0, workCounter, cancellationToken);
        ReadOnlySpan<char> property = (separator < 0 ? selector : selector[..separator]).Trim();
        bool isId = property.SequenceEqual("id".AsSpan());
        bool isClass = property.SequenceEqual("class".AsSpan());
        if (!isId && !isClass) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only id and class presence/equality stylesheet attribute selectors are implemented.");
        if (separator < 0) return new CssRule(CssSelectorKind.AttributePresent, isId ? "id" : "class", null, 10, style);
        ReadOnlySpan<char> expected = TrimQuotes(selector[(separator + 1)..].Trim());
        if (expected.IsEmpty) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only id and class presence/equality stylesheet attribute selectors are implemented.");
        if (!IsCssIdentifier(expected)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only simple CSS identifier attribute selector values are implemented.");
        return new CssRule(CssSelectorKind.AttributeEquals, isId ? "id" : "class", strings.Copy(expected), 10, style);
    }

    private static bool TryParseCompoundSelector(ReadOnlySpan<char> selector, string elementName, CssSelectorKind classKind, CssSelectorKind idKind, InlineStyle style, BudgetedParserStringStore strings, out CssRule rule)
    {
        if (selector.StartsWith(elementName + ".", StringComparison.OrdinalIgnoreCase) && IsCssIdentifier(selector[(elementName.Length + 1)..])) { rule = new CssRule(classKind, strings.Copy(selector[(elementName.Length + 1)..]), null, 11, style); return true; }
        if (selector.StartsWith(elementName + "#", StringComparison.OrdinalIgnoreCase) && IsCssIdentifier(selector[(elementName.Length + 1)..])) { rule = new CssRule(idKind, strings.Copy(selector[(elementName.Length + 1)..]), null, 101, style); return true; }
        rule = default;
        return false;
    }

    private static ReadOnlySpan<char> TrimQuotes(ReadOnlySpan<char> value)
    {
        while (!value.IsEmpty && value[0] is '\'' or '"') value = value[1..];
        while (!value.IsEmpty && value[^1] is '\'' or '"') value = value[..^1];
        return value;
    }

    private static bool IsCssIdentifier(ReadOnlySpan<char> value)
    {
        if (value.Length > SvgSyntaxLimits.MaximumIdentifierCharacters) throw new SvgException(SvgErrorCode.LimitExceeded, "CSS identifiers exceed the configured syntax limit.");
        foreach (char character in value) if (!(char.IsAsciiLetterOrDigit(character) || character is '-' or '_')) return false;
        return true;
    }

    private static int SkipWhitespace(string value, int position, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        while (position < value.Length && char.IsWhiteSpace(value[position])) { ConsumeCheckpoint(position, workCounter, cancellationToken); position++; }
        return position;
    }

    private static int FindCharacter(string value, char character, int position, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        while (position < value.Length)
        {
            ConsumeCheckpoint(position, workCounter, cancellationToken);
            if (value[position] == character) return position;
            position++;
        }
        return -1;
    }

    private static int FindCharacter(ReadOnlySpan<char> value, char character, int position, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        while (position < value.Length)
        {
            ConsumeCheckpoint(position, workCounter, cancellationToken);
            if (value[position] == character) return position;
            position++;
        }
        return -1;
    }

    private static bool ContainsCharacter(string value, char character, int start, int end, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        for (int position = start; position < end; position++)
        {
            ConsumeCheckpoint(position, workCounter, cancellationToken);
            if (value[position] == character) return true;
        }
        return false;
    }

    private static void ConsumeCheckpoint(int position, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        if ((position & 4095) == 0) workCounter.Consume(cancellationToken);
    }

    private ref struct CssSelectorTokenReader
    {
        private readonly ReadOnlySpan<char> _value;
        private readonly ParserWorkCounter _workCounter;
        private readonly CancellationToken _cancellationToken;
        private int _position;

        internal CssSelectorTokenReader(ReadOnlySpan<char> value, ParserWorkCounter workCounter, CancellationToken cancellationToken)
        {
            _value = value;
            _workCounter = workCounter;
            _cancellationToken = cancellationToken;
        }

        internal bool TryRead(out ReadOnlySpan<char> token)
        {
            while (_position < _value.Length && char.IsWhiteSpace(_value[_position])) { ConsumeCheckpoint(_position, _workCounter, _cancellationToken); _position++; }
            if (_position == _value.Length)
            {
                token = default;
                return false;
            }
            if (_value[_position] == '>')
            {
                token = _value.Slice(_position, 1);
                _position++;
                return true;
            }
            if (_value[_position] == '[')
            {
                int attributeStart = _position++;
                while (_position < _value.Length && _value[_position] != ']') { ConsumeCheckpoint(_position, _workCounter, _cancellationToken); _position++; }
                if (_position < _value.Length) _position++;
                token = _value[attributeStart.._position];
                return true;
            }
            int start = _position;
            while (_position < _value.Length && !char.IsWhiteSpace(_value[_position]) && _value[_position] != '>') { ConsumeCheckpoint(_position, _workCounter, _cancellationToken); _position++; }
            token = _value[start.._position];
            return true;
        }
    }

    private ref struct CssSelectorListReader
    {
        private readonly ReadOnlySpan<char> _value;
        private readonly ParserWorkCounter _workCounter;
        private readonly CancellationToken _cancellationToken;
        private int _position;

        internal CssSelectorListReader(ReadOnlySpan<char> value, ParserWorkCounter workCounter, CancellationToken cancellationToken)
        {
            _value = value;
            _workCounter = workCounter;
            _cancellationToken = cancellationToken;
        }

        internal bool TryRead(out ReadOnlySpan<char> selector)
        {
            while (_position < _value.Length)
            {
                int start = _position;
                while (_position < _value.Length && _value[_position] != ',') { ConsumeCheckpoint(_position, _workCounter, _cancellationToken); _position++; }
                selector = _value[start.._position].Trim();
                if (_position < _value.Length) _position++;
                if (!selector.IsEmpty) return true;
            }
            selector = default;
            return false;
        }
    }
}

internal sealed class ParserWorkCounter
{
    private readonly long _maximumWorkUnits;
    private long _used;

    internal ParserWorkCounter(long maximumWorkUnits) => _maximumWorkUnits = maximumWorkUnits;

    internal long Used => _used;

    internal void ConsumeText(int characterCount, CancellationToken cancellationToken)
    {
        for (int index = 0; index < characterCount; index += 4096) Consume(cancellationToken);
    }

    internal void Consume(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_used >= _maximumWorkUnits) throw new SvgException(SvgErrorCode.LimitExceeded, "SVG parsing work exceeds the configured limit.");
        _used++;
    }
}

internal enum CssSelectorKind { Rectangle, Group, Id, Class, RectangleClass, RectangleId, GroupClass, GroupId, AttributePresent, AttributeEquals, DescendantGroup, ChildGroup }

internal readonly record struct CssRule(CssSelectorKind Kind, string Value, string? ExpectedValue, int Specificity, InlineStyle Style)
{
    internal bool Matches(XmlReader reader, bool directChild, bool hasGroupAncestor, bool parentIsGroup) => Kind switch
    {
        CssSelectorKind.Rectangle => reader.LocalName == "rect",
        CssSelectorKind.Group => reader.LocalName == "g",
        CssSelectorKind.Id => string.Equals(Value, reader.GetAttribute("id"), StringComparison.Ordinal),
        CssSelectorKind.Class => SvgStyleSheet.HasCssClass(reader.GetAttribute("class"), Value),
        CssSelectorKind.RectangleClass => reader.LocalName == "rect" && SvgStyleSheet.HasCssClass(reader.GetAttribute("class"), Value),
        CssSelectorKind.RectangleId => reader.LocalName == "rect" && string.Equals(Value, reader.GetAttribute("id"), StringComparison.Ordinal),
        CssSelectorKind.GroupClass => reader.LocalName == "g" && SvgStyleSheet.HasCssClass(reader.GetAttribute("class"), Value),
        CssSelectorKind.GroupId => reader.LocalName == "g" && string.Equals(Value, reader.GetAttribute("id"), StringComparison.Ordinal),
        CssSelectorKind.AttributePresent => reader.GetAttribute(Value) is not null,
        CssSelectorKind.AttributeEquals => string.Equals(reader.GetAttribute(Value), ExpectedValue, StringComparison.Ordinal),
        CssSelectorKind.DescendantGroup => hasGroupAncestor,
        CssSelectorKind.ChildGroup => !directChild && parentIsGroup,
        _ => false
    };
}

/// <summary>Owns the temporary budget reservation for a comment-stripped stylesheet copy.</summary>
internal readonly record struct OwnedStyleSheetText(string Value, RenderBudget.BudgetReservation? Reservation) : IDisposable
{
    public void Dispose() => Reservation?.Dispose();
}
