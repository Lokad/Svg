namespace Lokad.Svg.Parsing;

/// <summary>Distinguishes CSS punctuation from quoted and escaped characters without decoding or retaining input.</summary>
/// <remarks>Mutable state belongs to one synchronous bounded scan. Callers meter traversal,
/// observe cancellation and handle comments outside strings. This value owns no storage.</remarks>
internal struct SvgCssQuotedState
{
    private char _quote;
    private bool _escaped;

    internal readonly bool IsClosed => _quote == '\0';

    internal bool IsSyntax(char character)
    {
        if (_escaped) { _escaped = false; return false; }
        if (character == '\\') { _escaped = true; return false; }
        if (_quote != '\0')
        {
            if (character == _quote) _quote = '\0';
            return false;
        }
        if (character is '\'' or '"') { _quote = character; return false; }
        return true;
    }
}
