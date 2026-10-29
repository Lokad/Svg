namespace Lokad.Svg.Parsing;

/// <summary>Shares bounded compound-selector syntax between admission and allocation-free matching.</summary>
internal static class SvgCssSelectorSyntax
{
    internal static CssSpecificity Specificity(ReadOnlySpan<char> pattern, CancellationToken token)
    {
        ComponentReader reader = new(pattern);
        CssSpecificity result = default;
        while (reader.Read(out ComponentKind kind, out _, out _))
        {
            token.ThrowIfCancellationRequested();
            result += kind == ComponentKind.Type ? new CssSpecificity(0, 0, 1) : kind == ComponentKind.Id ? new CssSpecificity(1, 0, 0) : new CssSpecificity(0, 1, 0);
        }
        return result;
    }

    internal static bool Matches(ReadOnlySpan<char> pattern, SvgCssElement element, CancellationToken token)
    {
        if (!element.IsSvg) return false;
        ComponentReader reader = new(pattern);
        while (reader.Read(out ComponentKind kind, out ReadOnlySpan<char> value, out ReadOnlySpan<char> expected))
        {
            token.ThrowIfCancellationRequested();
            bool matched = kind switch
            {
                ComponentKind.Type => value.Equals(element.Name.AsSpan(), StringComparison.Ordinal),
                ComponentKind.Id => EqualIdentifier(element.Id, value),
                ComponentKind.Class => SvgStyleSheet.HasCssClass(element.Classes, value, token),
                ComponentKind.AttributePresent => (value.SequenceEqual("id") ? element.Id : element.Classes) is not null,
                ComponentKind.AttributeEquals => EqualIdentifier(value.SequenceEqual("id") ? element.Id : element.Classes, expected),
                _ => false
            };
            if (!matched) return false;
        }
        return true;

        static bool EqualIdentifier(string? actual, ReadOnlySpan<char> wanted)
        {
            if (actual is null) return false;
            SvgStyleSheet.ValidateIdentifierValue(actual);
            return wanted.Equals(actual.AsSpan(), StringComparison.Ordinal);
        }
    }

    /// <summary>Matches selector chains with two dynamic-programming rows rather than recursive backtracking.
    /// Traversal visits selector components times open ancestors and checks cancellation.</summary>
    internal static bool MatchesChain(string selector, SvgCssElement current, SvgCssAncestorContext context, CancellationToken token)
    {
        ReverseReader reader = new(selector.AsSpan());
        if (!reader.Read(out ReadOnlySpan<char> pattern, out _)) return false;
        if (!Matches(pattern, current, token)) return false;
        ReadOnlySpan<SvgCssElement> ancestors = context.Ancestors;
        int length = ancestors.Length + 1;
        Span<byte> rows = context.MatchRows(length);
        Span<byte> previous = rows[..length], next = rows[length..];
        previous[^1] = 1;
        while (reader.Read(out pattern, out bool child))
        {
            bool reachable = false, suffix = false;
            next.Clear();
            for (int index = ancestors.Length - 1; index >= 0; index--)
            {
                token.ThrowIfCancellationRequested();
                suffix |= previous[index + 1] != 0;
                bool linked = child ? previous[index + 1] != 0 : suffix;
                if (!linked || !Matches(pattern, ancestors[index], token)) continue;
                next[index] = 1;
                reachable = true;
            }
            if (!reachable) return false;
            Span<byte> swap = previous; previous = next; next = swap;
        }
        return true;
    }

    private enum ComponentKind { Type, Id, Class, AttributePresent, AttributeEquals }

    private ref struct ComponentReader
    {
        private readonly ReadOnlySpan<char> _value;
        private int _position;

        internal ComponentReader(ReadOnlySpan<char> value) => _value = value;

        internal bool Read(out ComponentKind kind, out ReadOnlySpan<char> value, out ReadOnlySpan<char> expected)
        {
            kind = default; value = default; expected = default;
            if (_position == _value.Length) return false;
            char first = _value[_position];
            if (first == '[')
            {
                int end = _value[_position..].IndexOf(']');
                if (end < 0) throw Unsupported();
                ReadOnlySpan<char> attribute = _value.Slice(_position + 1, end - 1);
                _position += end + 1;
                int separator = attribute.IndexOf('=');
                value = SvgCssWhitespace.Trim(separator < 0 ? attribute : attribute[..separator]);
                if (!value.SequenceEqual("id") && !value.SequenceEqual("class")) throw Unsupported();
                if (separator < 0) { kind = ComponentKind.AttributePresent; return true; }
                expected = SvgCssWhitespace.Trim(attribute[(separator + 1)..]);
                if (expected.Length >= 2 && expected[0] is '\'' or '"' && expected[^1] == expected[0]) expected = expected[1..^1];
                ValidateIdentifier(expected);
                kind = ComponentKind.AttributeEquals;
                return true;
            }
            if (first is '#' or '.')
            {
                kind = first == '#' ? ComponentKind.Id : ComponentKind.Class;
                _position++;
            }
            else if (_position == 0) kind = ComponentKind.Type;
            else throw Unsupported();
            int start = _position;
            while (_position < _value.Length && IsIdentifierCharacter(_value[_position])) _position++;
            value = _value[start.._position];
            ValidateIdentifier(value);
            return true;
        }

        private static bool IsIdentifierCharacter(char value) => char.IsAsciiLetterOrDigit(value) || value is '-' or '_';

        private static void ValidateIdentifier(ReadOnlySpan<char> value)
        {
            if (value.Length > SvgSyntaxLimits.MaximumIdentifierCharacters) throw new SvgException(SvgErrorCode.LimitExceeded, "CSS identifiers exceed the configured syntax limit.");
            if (value.IsEmpty) throw Unsupported();
            foreach (char character in value) if (!IsIdentifierCharacter(character)) throw Unsupported();
        }
    }

    private static SvgException Unsupported()
        => new(SvgErrorCode.UnsupportedFeature, "Only bounded SVG element, ID, class, id/class attribute, descendant and child selectors are implemented.");

    private ref struct ReverseReader
    {
        private readonly ReadOnlySpan<char> _value;
        private int _position;
        private bool _started;

        internal ReverseReader(ReadOnlySpan<char> value) { _value = value; _position = value.Length; }

        internal bool Read(out ReadOnlySpan<char> pattern, out bool child)
        {
            child = false; pattern = default;
            while (_position > 0 && SvgCssWhitespace.Is(_value[_position - 1])) _position--;
            if (_position == 0) return false;
            if (_started && _value[_position - 1] == '>')
            {
                child = true;
                _position--;
                while (_position > 0 && SvgCssWhitespace.Is(_value[_position - 1])) _position--;
            }
            int end = _position;
            bool attribute = false;
            while (_position > 0)
            {
                char value = _value[_position - 1];
                if (value == ']') attribute = true;
                else if (value == '[') attribute = false;
                else if (!attribute && (SvgCssWhitespace.Is(value) || value == '>')) break;
                _position--;
            }
            pattern = _value[_position..end];
            _started = true;
            return true;
        }
    }
}
