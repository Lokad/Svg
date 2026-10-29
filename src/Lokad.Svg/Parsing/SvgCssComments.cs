namespace Lokad.Svg.Parsing;

internal enum SvgCssCommentContext { StyleSheet, Declaration }

/// <summary>Normalizes comments without owning storage; callers provide budgeted text or bounded stack scratch.</summary>
/// <remarks>Quoted text stays literal. Scans observe cancellation while visiting original characters;
/// selector token joins reject rather than turning malformed CSS into another admitted selector.</remarks>
internal static class SvgCssComments
{
    internal static ReadOnlySpan<char> NormalizeDeclaration(ReadOnlySpan<char> value, Span<char> scratch,
CancellationToken token)
    {
        if (value.IndexOf("/*") < 0) return value;
        int length = Normalize(value, [], SvgCssCommentContext.Declaration, token);
        if (length > scratch.Length) throw new SvgException(SvgErrorCode.LimitExceeded, "Inline style declarations exceed the configured syntax limit.");
        Normalize(value, scratch, SvgCssCommentContext.Declaration, token);
        return scratch[..length];
    }

    /// <summary>Measures when destination is empty; otherwise copies the same normalized characters into caller storage.</summary>
    internal static int Normalize(ReadOnlySpan<char> value, Span<char> destination, SvgCssCommentContext context,
CancellationToken token)
    {
        bool trim = context == SvgCssCommentContext.Declaration, declarations = trim;
        int written = 0, trimmedLength = 0;
        char previous = '\0';
        SvgCssQuotedState quotes = default;
        for (int position = 0; position < value.Length; position++)
        {
            if ((position & 4095) == 0) { token.ThrowIfCancellationRequested(); }
            char character = value[position];
            bool syntax = quotes.IsSyntax(character);
            if (syntax && character == '/' && position + 1 < value.Length && value[position + 1] == '*')
            {
                position = SkipComment(value, position, token) - 1;
                if (!declarations)
                {
                    char next = position + 1 < value.Length ? value[position + 1] : '\0';
                    if ((Identifier(previous) || previous == '#') && Identifier(next) || Identifier(previous) && next == '(')
                        throw new SvgException(SvgErrorCode.UnsupportedFeature, "Comments splitting CSS selector tokens are not implemented.");
                    continue;
                }
                // A comment separates declaration tokens even when it contains punctuation.
                character = ' ';
            }
            else if (syntax && !trim)
            {
                if (character == '{') declarations = true;
                else if (character == '}') declarations = false;
            }
            bool whitespace = trim && syntax && SvgCssWhitespace.Is(character);
            Append(character, whitespace, destination, ref written, ref trimmedLength);
            previous = character;
        }
        return trim ? trimmedLength : written;

        static bool Identifier(char character) => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '\\' || character >= 0x80;

        static void Append(char character, bool trimmable, Span<char> target, ref int position, ref int length)
        {
            if (trimmable && position == 0) return;
            if (!target.IsEmpty)
            {
                if (position < target.Length) target[position] = character;
                else if (!trimmable) throw new SvgException(SvgErrorCode.LimitExceeded, "CSS normalization exceeds its bounded storage.");
            }
            position++;
            if (!trimmable) length = position;
        }
    }

    internal static int SkipComment(ReadOnlySpan<char> value, int start, CancellationToken token)
    {
        for (int position = start + 2; position + 1 < value.Length; position++)
        {
            if ((position & 4095) == 0) { token.ThrowIfCancellationRequested(); }
            if (value[position] == '*' && value[position + 1] == '/') return position + 2;
        }
        throw new SvgException(SvgErrorCode.MalformedInput, "CSS comments must be terminated.");
    }
}
