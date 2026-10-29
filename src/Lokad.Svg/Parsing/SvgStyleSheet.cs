using System.Xml;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Parsing;

/// <summary>Parses and resolves the deliberately narrow embedded stylesheet subset.</summary>
internal static class SvgStyleSheet
{
    internal static void Parse(string value, int maximumRules, BudgetedParserList<CssRule> rules, BudgetedParserStringStore strings, RenderBudget budget, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using OwnedStyleSheetText commentless = StripComments(value, budget, cancellationToken);
        value = commentless.Value;
        int position = 0;
        while (position < value.Length)
        {
            position = SkipWhitespace(value, position, cancellationToken);
            if (position == value.Length) break;
            int openingBrace = FindCharacter(value, '{', position, cancellationToken);
            if (openingBrace < 0) throw new SvgException(SvgErrorCode.MalformedInput, "Stylesheet rules must contain a declaration block.");
            int closingBrace = FindCharacter(value, '}', openingBrace + 1, cancellationToken);
            if (closingBrace < 0 || ContainsCharacter(value, '{', openingBrace + 1, closingBrace, cancellationToken)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Nested stylesheet rules are not implemented.");
            InlineStyle style = SvgInlineStyleParser.Parse(value.AsSpan(openingBrace + 1, closingBrace - openingBrace - 1), strings, cancellationToken, true);
            CssSelectorListReader selectors = new(value.AsSpan(position, openingBrace - position), cancellationToken);
            while (selectors.TryRead(out ReadOnlySpan<char> selector))
            {
                if (rules.Count >= maximumRules) throw new SvgException(SvgErrorCode.LimitExceeded, "Stylesheet rule count exceeds the configured structural limit.");
                rules.Add(ParseSelector(selector, style, strings, cancellationToken));
            }
            position = closingBrace + 1;
        }
    }

    internal static InlineStyle Resolve(XmlReader reader, BudgetedParserList<CssRule> rules, SvgCssAncestorContext ancestors, CancellationToken cancellationToken)
    {
        if (reader is SvgRetainedTextSource.Reader retained)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return retained.StyleSheet;
        }
        SvgStyleCascade cascade = new();
        SvgCssElement element = default;
        for (int ruleIndex = 0; ruleIndex < rules.Count; ruleIndex++)
        {
            CssRule rule = rules[ruleIndex];
            cancellationToken.ThrowIfCancellationRequested();
            // The reader stays on this element throughout the cascade. Read after the
            // first cancellation check, and leave the no-rule path untouched.
            if (ruleIndex == 0) element = SvgCssElement.Read(reader);
            if (!rule.Matches(element, ancestors, cancellationToken)) continue;
            cascade.Apply(rule.Style, rule.Specificity);
        }
        return cascade.ToInlineStyle();
    }

    internal static OwnedStyleSheetText ReadElementText(XmlReader reader, RenderBudget budget, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (reader.IsEmptyElement) return new(string.Empty, null);
        using XmlReader subtree = reader.ReadSubtree();
        if (!subtree.Read()) throw new XmlException("Stylesheet content is missing.");
        using BudgetedStyleSheetTextBuffer text = new(budget, cancellationToken);
        while (subtree.Read())
        {
            switch (subtree.NodeType)
            {
                case XmlNodeType.Text:
                case XmlNodeType.CDATA:
                case XmlNodeType.Whitespace:
                case XmlNodeType.SignificantWhitespace:
                    text.Append(subtree);
                    break;
                case XmlNodeType.Element:
                    throw new XmlException("Stylesheet content must not contain child elements.");
                case XmlNodeType.EndElement:
                    return text.Build();
            }
        }
        throw new XmlException("Stylesheet content is not closed.");
    }

    internal static bool HasCssClass(string? value, ReadOnlySpan<char> expected, CancellationToken cancellationToken)
    {
        if (value is null) return false;
        ValidateIdentifierValue(value);
        ReadOnlySpan<char> classes = value.AsSpan();
        int position = 0;
        while (position < classes.Length)
        {
            ConsumeCheckpoint(position, cancellationToken);
            while (position < classes.Length && SvgCssWhitespace.Is(classes[position])) { ConsumeCheckpoint(position, cancellationToken); position++; }
            int start = position;
            while (position < classes.Length && !SvgCssWhitespace.Is(classes[position])) { ConsumeCheckpoint(position, cancellationToken); position++; }
            if (classes[start..position].Equals(expected, StringComparison.Ordinal)) return true;
        }
        return false;
    }

    private static OwnedStyleSheetText StripComments(string value, RenderBudget budget, CancellationToken cancellationToken)
    {
        int length = SvgCssComments.Normalize(value, [], SvgCssCommentContext.StyleSheet, cancellationToken);
        if (length == value.Length) return new(value, null);
        if (length == 0) return new(string.Empty, null);
        RenderBudget.BudgetReservation reservation = budget.Reserve(GetStringByteCount(length), SvgMemoryCategory.Parser);
        try
        {
            return new(string.Create(length, (Value: value, Token: cancellationToken), static (destination, state) =>
            {
                SvgCssComments.Normalize(state.Value, destination, SvgCssCommentContext.StyleSheet, state.Token);
            }), reservation);
        }
        catch { reservation.Dispose(); throw; }
    }

    private static long GetStringByteCount(int characterCount) => checked(32 + (long)characterCount * sizeof(char));

    /// <summary>Owns the charged mutable character storage used while a stylesheet text node is read incrementally.</summary>
    private sealed class BudgetedStyleSheetTextBuffer : IDisposable
    {
        private const int InitialCapacity = 4_096;
        private const int CopyCheckpointCharacters = 4_096;
        private readonly RenderBudget _budget;
        private readonly CancellationToken _cancellationToken;
        private char[] _characters = [];
        private RenderBudget.BudgetReservation? _reservation;
        private int _length;

        internal BudgetedStyleSheetTextBuffer(RenderBudget budget, CancellationToken cancellationToken)
        {
            _budget = budget;
            _cancellationToken = cancellationToken;
        }

        internal void Append(XmlReader reader)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            using RenderBudget.BudgetReservation probeOwner = _budget.Reserve(ParserCollectionMemory.GetArrayByteCount<char>(1), SvgMemoryCategory.Parser);
            char[] probe = new char[1];
            while (true)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                if (_length == _characters.Length)
                {
                    // Prove that another character exists before growing a full buffer.
                    if (reader.ReadValueChunk(probe, 0, 1) == 0) return;
                    EnsureCapacity(_length + 1);
                    _characters[_length++] = probe[0];
                }
                int read = reader.ReadValueChunk(_characters, _length, _characters.Length - _length);
                if (read == 0) return;
                _length += read;
            }
        }

        internal OwnedStyleSheetText Build()
        {
            if (_length == 0) return new(string.Empty, null);
            RenderBudget.BudgetReservation reservation = _budget.Reserve(GetStringByteCount(_length), SvgMemoryCategory.Parser);
            try
            {
                string value = string.Create(_length, (Characters: _characters, CancellationToken: _cancellationToken), static (destination, state) =>
                {
                    for (int offset = 0; offset < destination.Length; offset += CopyCheckpointCharacters)
                    {
                        state.CancellationToken.ThrowIfCancellationRequested();
                        int count = Math.Min(CopyCheckpointCharacters, destination.Length - offset);
                        state.Characters.AsSpan(offset, count).CopyTo(destination.Slice(offset, count));
                    }
                });
                return new OwnedStyleSheetText(value, reservation);
            }
            catch
            {
                reservation.Dispose();
                throw;
            }
        }

        public void Dispose()
        {
            _reservation?.Dispose();
            _reservation = null;
            _characters = [];
            _length = 0;
        }

        private void EnsureCapacity(int required)
        {
            if (required <= _characters.Length) return;
            int capacity;
            try { capacity = _characters.Length == 0 ? InitialCapacity : checked(_characters.Length * 2); }
            catch (OverflowException) { throw new SvgException(SvgErrorCode.LimitExceeded, "Stylesheet text exceeds the supported storage capacity."); }
            if (capacity < required) capacity = required;
            RenderBudget.BudgetReservation reservation = _budget.Reserve(GetStringByteCount(capacity), SvgMemoryCategory.Parser);
            char[] replacement;
            try { replacement = new char[capacity]; }
            catch { reservation.Dispose(); throw; }
            try
            {
                for (int offset = 0; offset < _length; offset += CopyCheckpointCharacters)
                {
                    _cancellationToken.ThrowIfCancellationRequested();
                    int count = Math.Min(CopyCheckpointCharacters, _length - offset);
                    _characters.AsSpan(offset, count).CopyTo(replacement.AsSpan(offset, count));
                }
            }
            catch
            {
                reservation.Dispose();
                throw;
            }
            _reservation?.Dispose();
            _reservation = reservation;
            _characters = replacement;
        }
    }

    private static CssRule ParseSelector(ReadOnlySpan<char> selector, InlineStyle style, BudgetedParserStringStore strings, CancellationToken cancellationToken)
    {
        if (selector.Length > SvgSyntaxLimits.MaximumCssSelectorCharacters) throw new SvgException(SvgErrorCode.LimitExceeded, "CSS selectors exceed the configured syntax limit.");
        CssSelectorTokenReader tokens = new(selector, cancellationToken);
        if (!tokens.TryRead(out ReadOnlySpan<char> pattern) || pattern.SequenceEqual(">")) throw Unsupported();
        CssSpecificity specificity = SvgCssSelectorSyntax.Specificity(pattern, cancellationToken);
        bool combinator = false;
        while (tokens.TryRead(out pattern))
        {
            if (pattern.SequenceEqual(">") && !tokens.TryRead(out pattern)) throw Unsupported();
            if (pattern.SequenceEqual(">")) throw Unsupported();
            specificity += SvgCssSelectorSyntax.Specificity(pattern, cancellationToken);
            combinator = true;
        }
        return new CssRule(strings.Copy(selector), specificity, style, combinator);

        static SvgException Unsupported() => new(SvgErrorCode.UnsupportedFeature, "Selector combinators require a compound selector on each side.");
    }

    internal static void ValidateIdentifierValue(string value)
    {
        if (value.Length > SvgSyntaxLimits.MaximumIdentifierCharacters) throw new SvgException(SvgErrorCode.LimitExceeded, "SVG identifiers and class values exceed the configured syntax limit.");
    }

    private static int SkipWhitespace(string value, int position, CancellationToken cancellationToken)
    {
        while (position < value.Length && SvgCssWhitespace.Is(value[position])) { ConsumeCheckpoint(position, cancellationToken); position++; }
        return position;
    }

    private static int FindCharacter(string value, char character, int position, CancellationToken cancellationToken)
    {
        SvgCssQuotedState quotes = default;
        while (position < value.Length)
        {
            ConsumeCheckpoint(position, cancellationToken);
            if (quotes.IsSyntax(value[position]) && value[position] == character) return position;
            position++;
        }
        return -1;
    }

    private static bool ContainsCharacter(string value, char character, int start, int end, CancellationToken cancellationToken)
    {
        SvgCssQuotedState quotes = default;
        for (int position = start; position < end; position++)
        {
            ConsumeCheckpoint(position, cancellationToken);
            if (quotes.IsSyntax(value[position]) && value[position] == character) return true;
        }
        return false;
    }

    private static void ConsumeCheckpoint(int position, CancellationToken cancellationToken)
    {
        if ((position & 4095) == 0) cancellationToken.ThrowIfCancellationRequested();
    }

    private ref struct CssSelectorTokenReader
    {
        private readonly ReadOnlySpan<char> _value;
        private readonly CancellationToken _cancellationToken;
        private int _position;

        internal CssSelectorTokenReader(ReadOnlySpan<char> value, CancellationToken cancellationToken)
        {
            _value = value;
            _cancellationToken = cancellationToken;
        }

        internal bool TryRead(out ReadOnlySpan<char> token)
        {
            while (_position < _value.Length && SvgCssWhitespace.Is(_value[_position])) { ConsumeCheckpoint(_position, _cancellationToken); _position++; }
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
            int start = _position;
            bool attribute = false;
            while (_position < _value.Length)
            {
                char value = _value[_position];
                if (value == '[') attribute = true;
                else if (value == ']') attribute = false;
                else if (!attribute && (SvgCssWhitespace.Is(value) || value == '>')) break;
                ConsumeCheckpoint(_position, _cancellationToken);
                _position++;
            }
            token = _value[start.._position];
            return true;
        }
    }

    private ref struct CssSelectorListReader
    {
        private readonly ReadOnlySpan<char> _value;
        private readonly CancellationToken _cancellationToken;
        private int _position;

        internal CssSelectorListReader(ReadOnlySpan<char> value, CancellationToken cancellationToken)
        {
            _value = value;
            _cancellationToken = cancellationToken;
        }

        internal bool TryRead(out ReadOnlySpan<char> selector)
        {
            while (_position < _value.Length)
            {
                int start = _position;
                while (_position < _value.Length && _value[_position] != ',') { ConsumeCheckpoint(_position, _cancellationToken); _position++; }
                selector = SvgCssWhitespace.Trim(_value[start.._position]);
                if (_position < _value.Length) _position++;
                if (!selector.IsEmpty) return true;
            }
            selector = default;
            return false;
        }
    }
}

internal readonly record struct CssRule(string Selector, CssSpecificity Specificity, InlineStyle Style, bool HasCombinator)
{
    internal bool Matches(SvgCssElement element, SvgCssAncestorContext ancestors, CancellationToken token)
        => HasCombinator
            ? SvgCssSelectorSyntax.MatchesChain(Selector, element, ancestors, token)
            : SvgCssSelectorSyntax.Matches(Selector.AsSpan(), element, token);

    internal bool MatchesRoot(string? id, string? classes, CancellationToken token)
        => !HasCombinator && SvgCssSelectorSyntax.Matches(Selector.AsSpan(), new("svg", id, classes, true), token);
}

/// <summary>Owns the temporary budget reservation for a comment-stripped stylesheet copy.</summary>
internal readonly record struct OwnedStyleSheetText(string Value, RenderBudget.BudgetReservation? Reservation) : IDisposable
{
    public void Dispose() => Reservation?.Dispose();
}
