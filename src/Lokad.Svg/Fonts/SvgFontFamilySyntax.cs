namespace Lokad.Svg.Fonts;

using System.Globalization;
using System.Text;
using Lokad.Svg.Parsing;

/// <summary>Identifies one named or generic family in an ordered CSS font request.</summary>
public sealed class SvgFontFamily
{
    internal SvgFontFamily(string name, bool isGeneric) { Name = name; IsGeneric = isGeneric; }
    /// <summary>Gets the decoded family name, without CSS quoting or escapes.</summary>
    public string Name { get; }
    /// <summary>Gets whether the unquoted token requests a host-defined generic family.</summary>
    public bool IsGeneric { get; }
}

internal readonly record struct SvgFontFamilyList(SvgFontFamily[] Families, string Text);

/// <summary>Shares bounded CSS family tokenization and canonical inheritance keys.</summary>
internal static class SvgFontFamilySyntax
{
    internal const int MaximumCharacters = 4096;
    internal const int MaximumFamilies = 256;

    internal static SvgFontFamilyList Parse(string value, ParserWorkCounter? work, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Length > MaximumCharacters) throw new SvgException(SvgErrorCode.LimitExceeded, "The CSS font-family list exceeds the supported character bound.");
        var families = new List<SvgFontFamily>();
        var canonical = new StringBuilder();
        int position = 0;
        SkipSeparators();
        while (position < value.Length)
        {
            if (families.Count == MaximumFamilies) throw new SvgException(SvgErrorCode.LimitExceeded, "The CSS font-family list exceeds the supported family count.");
            var name = new StringBuilder();
            bool quoted = value[position] is '\'' or '"';
            bool generic = false;
            if (quoted)
            {
                char quote = Read();
                bool closed = false;
                while (position < value.Length)
                {
                    char current = Read();
                    if (current == quote) { closed = true; break; }
                    if (current is '\n' or '\r' or '\f') throw Invalid();
                    if (current == '\\')
                    {
                        if (position < value.Length && value[position] is '\n' or '\r' or '\f') { ReadNewline(); continue; }
                        ReadEscape(name);
                    }
                    else AppendCharacter(name, current);
                }
                if (!closed) throw Invalid();
                SkipSeparators();
            }
            else
            {
                int words = 0;
                while (true)
                {
                    int first = name.Length;
                    if (position >= value.Length || !StartsIdentifier(position)) throw Invalid();
                    while (position < value.Length)
                    {
                        char current = value[position];
                        if (current == '\\') { Read(); ReadEscape(name); }
                        else if (IsName(current)) AppendCharacter(name, Read());
                        else break;
                    }
                    string word = name.ToString(first, name.Length - first);
                    if (words == 0) generic = IsGeneric(word);
                    words++;
                    bool separated = SkipSeparators();
                    if (position == value.Length || value[position] == ',') break;
                    if (!separated) throw Invalid();
                    name.Append(' ');
                }
                if (IsReserved(name.ToString()) || generic && words != 1) throw Invalid();
            }
            string family = name.ToString();
            if (string.IsNullOrWhiteSpace(family)) throw Invalid();
            if (family.Length > SvgSyntaxLimits.MaximumIdentifierCharacters) throw new SvgException(SvgErrorCode.LimitExceeded, "The decoded font family exceeds the supported name bound.");
            if (generic) family = family.ToLowerInvariant();
            families.Add(new(family, generic));
            if (canonical.Length != 0) canonical.Append(", ");
            if (generic || CanBeUnquoted(family)) canonical.Append(family);
            else
            {
                canonical.Append('"');
                foreach (char character in family)
                {
                    if (character is '\\' or '"') canonical.Append('\\');
                    if (character < ' ' || character == '\x7F') canonical.Append('\\').Append(((int)character).ToString("X", CultureInfo.InvariantCulture)).Append(' ');
                    else canonical.Append(character);
                }
                canonical.Append('"');
            }
            if (canonical.Length > MaximumCharacters)
                throw new SvgException(SvgErrorCode.LimitExceeded, "The canonical CSS font-family list exceeds the supported character bound.");
            if (position == value.Length) break;
            if (Read() != ',') throw Invalid();
            SkipSeparators();
            if (position == value.Length) throw Invalid();
        }
        if (families.Count == 0) throw Invalid();
        return new(families.ToArray(), canonical.ToString());

        char Read()
        {
            if (work is null) token.ThrowIfCancellationRequested(); else work.Consume(token);
            return value[position++];
        }
        void ReadNewline()
        {
            char character = Read();
            if (character == '\r' && position < value.Length && value[position] == '\n') Read();
        }
        bool SkipSeparators()
        {
            int first = position;
            while (position < value.Length)
            {
                if (IsWhitespace(value[position])) { Read(); continue; }
                if (value[position] != '/' || position + 1 == value.Length || value[position + 1] != '*') break;
                Read(); Read();
                while (position + 1 < value.Length && (value[position] != '*' || value[position + 1] != '/')) Read();
                if (position + 1 >= value.Length) throw Invalid();
                Read(); Read();
            }
            return position != first;
        }
        bool StartsIdentifier(int index)
        {
            char first = value[index];
            if (first == '\\') return index + 1 < value.Length && value[index + 1] is not ('\n' or '\r' or '\f');
            if (IsStart(first)) return true;
            return first == '-' && index + 1 < value.Length && (IsStart(value[index + 1]) || value[index + 1] is '-' or '\\');
        }
        void ReadEscape(StringBuilder output)
        {
            if (position == value.Length) throw Invalid();
            if (value[position] is '\n' or '\r' or '\f') throw Invalid();
            if (!char.IsAsciiHexDigit(value[position])) { AppendCharacter(output, Read()); return; }
            int scalar = 0, digits = 0;
            while (position < value.Length && digits < 6 && char.IsAsciiHexDigit(value[position]))
            {
                char digit = Read();
                scalar = scalar * 16 + (digit <= '9' ? digit - '0' : (digit | 32) - 'a' + 10);
                digits++;
            }
            if (position < value.Length && IsWhitespace(value[position]))
            {
                char separator = Read();
                if (separator == '\r' && position < value.Length && value[position] == '\n') Read();
            }
            output.Append(char.ConvertFromUtf32(scalar == 0 || !Rune.IsValid(scalar) ? 0xFFFD : scalar));
        }
        void AppendCharacter(StringBuilder output, char character)
        {
            if (character == '\0') output.Append('\uFFFD');
            else if (char.IsHighSurrogate(character) && position < value.Length && char.IsLowSurrogate(value[position])) output.Append(character).Append(Read());
            else output.Append(char.IsSurrogate(character) ? '\uFFFD' : character);
        }
        static bool CanBeUnquoted(string name)
        {
            if (IsReserved(name)) return false;
            bool firstWord = true;
            foreach (string word in name.Split(' '))
            {
                if (word.Length == 0 || firstWord && IsGeneric(word)) return false;
                int first = word[0] == '-' ? 1 : 0;
                if (first == word.Length || !(IsStart(word[first]) || first == 1 && word[first] == '-')) return false;
                foreach (char character in word) if (!IsName(character)) return false;
                firstWord = false;
            }
            return true;
        }
    }

    internal static bool IsGeneric(string name) => name.ToLowerInvariant() is "serif" or "sans-serif" or "monospace" or "cursive" or "fantasy"
        or "system-ui" or "ui-serif" or "ui-sans-serif" or "ui-monospace" or "ui-rounded" or "emoji" or "math" or "fangsong";

    private static bool IsReserved(string name) => name.ToLowerInvariant() is "inherit" or "initial" or "unset" or "revert" or "revert-layer" or "default";
    private static bool IsWhitespace(char character) => character is ' ' or '\t' or '\n' or '\r' or '\f';
    private static bool IsStart(char character) => char.IsAsciiLetter(character) || character == '_' || character >= '\x80';
    private static bool IsName(char character) => IsStart(character) || char.IsAsciiDigit(character) || character == '-';
    private static SvgException Invalid() => new(SvgErrorCode.MalformedInput, "The CSS font-family value is not a nonempty list of names or generic families.");
}
