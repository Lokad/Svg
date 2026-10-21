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
            InlineStyle style = SvgInlineStyleParser.Parse(value.AsSpan(openingBrace + 1, closingBrace - openingBrace - 1), strings, workCounter, cancellationToken, true);
            CssSelectorListReader selectors = new(value.AsSpan(position, openingBrace - position), workCounter, cancellationToken);
            while (selectors.TryRead(out ReadOnlySpan<char> selector))
            {
                if (rules.Count >= maximumRules) throw new SvgException(SvgErrorCode.LimitExceeded, "Stylesheet rule count exceeds the configured structural limit.");
                rules.Add(ParseSelector(selector, style, strings, workCounter, cancellationToken));
            }
            position = closingBrace + 1;
        }
    }

    internal static InlineStyle Resolve(XmlReader reader, BudgetedParserList<CssRule> rules, SvgCssAncestorContext ancestors, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        if (reader is SvgRetainedTextSource.Reader retained)
        {
            workCounter.Consume(cancellationToken);
            return retained.StyleSheet;
        }
        SvgStyleCascade cascade = new();
        SvgCssElement element = default;
        for (int ruleIndex = 0; ruleIndex < rules.Count; ruleIndex++)
        {
            CssRule rule = rules[ruleIndex];
            workCounter.Consume(cancellationToken);
            // The reader stays on this element throughout the cascade. Read after the
            // first work/cancellation check, and leave the no-rule path untouched.
            if (ruleIndex == 0) element = SvgCssElement.Read(reader);
            if (!rule.Matches(element, ancestors, workCounter, cancellationToken)) continue;
            cascade.Apply(rule.Style, rule.Specificity);
        }
        return cascade.ToInlineStyle();
    }

    internal static OwnedStyleSheetText ReadElementText(XmlReader reader, RenderBudget budget, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (reader.IsEmptyElement) return new(string.Empty, null);
        using XmlReader subtree = reader.ReadSubtree();
        if (!subtree.Read()) throw new XmlException("Stylesheet content is missing.");
        using BudgetedStyleSheetTextBuffer text = new(budget, workCounter, cancellationToken);
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

    internal static bool HasCssClass(string? value, ReadOnlySpan<char> expected, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        if (value is null) return false;
        ValidateIdentifierValue(value);
        ReadOnlySpan<char> classes = value.AsSpan();
        int position = 0;
        while (position < classes.Length)
        {
            ConsumeCheckpoint(position, workCounter, cancellationToken);
            while (position < classes.Length && char.IsWhiteSpace(classes[position])) { ConsumeCheckpoint(position, workCounter, cancellationToken); position++; }
            int start = position;
            while (position < classes.Length && !char.IsWhiteSpace(classes[position])) { ConsumeCheckpoint(position, workCounter, cancellationToken); position++; }
            if (classes[start..position].Equals(expected, StringComparison.Ordinal)) return true;
        }
        return false;
    }

    private static OwnedStyleSheetText StripComments(string value, RenderBudget budget, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        int sourceIndex = 0;
        int removedCharacters = 0;
        SvgCssQuotedState quotes = default;
        bool declarations = false, propertyValue = false;
        while (sourceIndex < value.Length)
        {
            if ((sourceIndex & 4095) == 0) workCounter.Consume(cancellationToken);
            bool syntax = quotes.IsSyntax(value[sourceIndex]);
            if (syntax && value[sourceIndex] == '{') { declarations = true; propertyValue = false; }
            else if (syntax && value[sourceIndex] == '}') { declarations = false; propertyValue = false; }
            else if (syntax && declarations && value[sourceIndex] == ':') propertyValue = true;
            else if (syntax && value[sourceIndex] == ';') propertyValue = false;
            if (!syntax || value[sourceIndex] != '/' || sourceIndex + 1 == value.Length || value[sourceIndex + 1] != '*') { sourceIndex++; continue; }
            int commentStart = sourceIndex;
            sourceIndex += 2;
            while (sourceIndex + 1 < value.Length && (value[sourceIndex] != '*' || value[sourceIndex + 1] != '/'))
            {
                if ((sourceIndex & 4095) == 0) workCounter.Consume(cancellationToken);
                sourceIndex++;
            }
            if (sourceIndex + 1 >= value.Length) throw new SvgException(SvgErrorCode.MalformedInput, "Stylesheet comments must be terminated.");
            sourceIndex += 2;
            // Preserve value token boundaries and the established removal
            // of comments inside selectors and property names.
            removedCharacters += sourceIndex - commentStart - (propertyValue ? 1 : 0);
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
                SvgCssQuotedState quotes = default;
                bool declarations = false, propertyValue = false;
                while (sourceIndex < state.Value.Length)
                {
                    if ((sourceIndex & 4095) == 0) state.WorkCounter.Consume(state.CancellationToken);
                    bool syntax = quotes.IsSyntax(state.Value[sourceIndex]);
                    if (syntax && state.Value[sourceIndex] == '{') { declarations = true; propertyValue = false; }
                    else if (syntax && state.Value[sourceIndex] == '}') { declarations = false; propertyValue = false; }
                    else if (syntax && declarations && state.Value[sourceIndex] == ':') propertyValue = true;
                    else if (syntax && state.Value[sourceIndex] == ';') propertyValue = false;
                    if (!syntax || state.Value[sourceIndex] != '/' || sourceIndex + 1 == state.Value.Length || state.Value[sourceIndex + 1] != '*')
                    {
                        destination[destinationIndex++] = state.Value[sourceIndex++];
                        continue;
                    }
                    sourceIndex += 2;
                    if (propertyValue) destination[destinationIndex++] = ' ';
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

    /// <summary>Owns the charged mutable character storage used while a stylesheet text node is read incrementally.</summary>
    private sealed class BudgetedStyleSheetTextBuffer : IDisposable
    {
        private const int InitialCapacity = 4_096;
        private const int CopyCheckpointCharacters = 4_096;
        private readonly RenderBudget _budget;
        private readonly ParserWorkCounter _workCounter;
        private readonly CancellationToken _cancellationToken;
        private char[] _characters = [];
        private RenderBudget.BudgetReservation? _reservation;
        private int _length;

        internal BudgetedStyleSheetTextBuffer(RenderBudget budget, ParserWorkCounter workCounter, CancellationToken cancellationToken)
        {
            _budget = budget;
            _workCounter = workCounter;
            _cancellationToken = cancellationToken;
        }

        internal void Append(XmlReader reader)
        {
            while (true)
            {
                _workCounter.Consume(_cancellationToken);
                EnsureCapacity(_length + 1);
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
                string value = string.Create(_length, (Characters: _characters, WorkCounter: _workCounter, CancellationToken: _cancellationToken), static (destination, state) =>
                {
                    for (int offset = 0; offset < destination.Length; offset += CopyCheckpointCharacters)
                    {
                        state.WorkCounter.Consume(state.CancellationToken);
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
                    _workCounter.Consume(_cancellationToken);
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

    private static CssRule ParseSelector(ReadOnlySpan<char> selector, InlineStyle style, BudgetedParserStringStore strings, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        if (selector.Length > SvgSyntaxLimits.MaximumCssSelectorCharacters) throw new SvgException(SvgErrorCode.LimitExceeded, "CSS selectors exceed the configured syntax limit.");
        CssSelectorTokenReader tokens = new(selector, workCounter, cancellationToken);
        if (!tokens.TryRead(out ReadOnlySpan<char> pattern) || pattern.SequenceEqual(">")) throw Unsupported();
        CssSpecificity specificity = SvgCssSelectorSyntax.Specificity(pattern, workCounter, cancellationToken);
        bool combinator = false;
        while (tokens.TryRead(out pattern))
        {
            if (pattern.SequenceEqual(">") && !tokens.TryRead(out pattern)) throw Unsupported();
            if (pattern.SequenceEqual(">")) throw Unsupported();
            specificity += SvgCssSelectorSyntax.Specificity(pattern, workCounter, cancellationToken);
            combinator = true;
        }
        return new CssRule(strings.Copy(selector), specificity, style, combinator);

        static SvgException Unsupported() => new(SvgErrorCode.UnsupportedFeature, "Selector combinators require a compound selector on each side.");
    }

    internal static void ValidateIdentifierValue(string value)
    {
        if (value.Length > SvgSyntaxLimits.MaximumIdentifierCharacters) throw new SvgException(SvgErrorCode.LimitExceeded, "SVG identifiers and class values exceed the configured syntax limit.");
    }

    private static int SkipWhitespace(string value, int position, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        while (position < value.Length && char.IsWhiteSpace(value[position])) { ConsumeCheckpoint(position, workCounter, cancellationToken); position++; }
        return position;
    }

    private static int FindCharacter(string value, char character, int position, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        SvgCssQuotedState quotes = default;
        while (position < value.Length)
        {
            ConsumeCheckpoint(position, workCounter, cancellationToken);
            if (quotes.IsSyntax(value[position]) && value[position] == character) return position;
            position++;
        }
        return -1;
    }

    private static bool ContainsCharacter(string value, char character, int start, int end, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        SvgCssQuotedState quotes = default;
        for (int position = start; position < end; position++)
        {
            ConsumeCheckpoint(position, workCounter, cancellationToken);
            if (quotes.IsSyntax(value[position]) && value[position] == character) return true;
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
            int start = _position;
            bool attribute = false;
            while (_position < _value.Length)
            {
                char value = _value[_position];
                if (value == '[') attribute = true;
                else if (value == ']') attribute = false;
                else if (!attribute && (char.IsWhiteSpace(value) || value == '>')) break;
                ConsumeCheckpoint(_position, _workCounter, _cancellationToken);
                _position++;
            }
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
    internal const int ByteCheckpointSize = 4_096;
    private readonly long _maximumWorkUnits;
    private long _used;

    internal ParserWorkCounter(long maximumWorkUnits) => _maximumWorkUnits = maximumWorkUnits;

    internal long Used => _used;

    internal void ConsumeText(int characterCount, CancellationToken cancellationToken)
    {
        ConsumeBytes(characterCount, cancellationToken);
    }

    internal void ConsumeBytes(int byteCount, CancellationToken cancellationToken)
    {
        if (byteCount < 0) throw new ArgumentOutOfRangeException(nameof(byteCount));
        for (int index = 0; index < byteCount; index += ByteCheckpointSize) Consume(cancellationToken);
    }

    internal void Consume(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_used >= _maximumWorkUnits) throw new SvgException(SvgErrorCode.LimitExceeded, "SVG parsing work exceeds the configured limit.");
        _used++;
    }
}

internal readonly record struct CssRule(string Selector, CssSpecificity Specificity, InlineStyle Style, bool HasCombinator)
{
    internal bool Matches(SvgCssElement element, SvgCssAncestorContext ancestors, ParserWorkCounter work, CancellationToken token)
        => HasCombinator
            ? SvgCssSelectorSyntax.MatchesChain(Selector, element, ancestors, work, token)
            : SvgCssSelectorSyntax.Matches(Selector.AsSpan(), element, work, token);

    internal bool MatchesRoot(string? id, string? classes, ParserWorkCounter work, CancellationToken token)
        => !HasCombinator && SvgCssSelectorSyntax.Matches(Selector.AsSpan(), new("svg", id, classes, true), work, token);
}

/// <summary>Owns the temporary budget reservation for a comment-stripped stylesheet copy.</summary>
internal readonly record struct OwnedStyleSheetText(string Value, RenderBudget.BudgetReservation? Reservation) : IDisposable
{
    public void Dispose() => Reservation?.Dispose();
}
