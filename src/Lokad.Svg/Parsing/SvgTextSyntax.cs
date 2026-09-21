namespace Lokad.Svg.Parsing;

using Lokad.Svg.Fonts;
using System.Globalization;
using System.Xml;

/// <summary>Defines the bounded direct-text subset shared by extraction and simple layout.</summary>
internal static class SvgTextLimits
{
    internal const int MaximumCharacters = 32_768;
}

/// <summary>Specifies the admitted XML whitespace treatment for one direct text run.</summary>
internal enum SvgTextWhitespaceMode { Default, Preserve }

/// <summary>Normalizes direct-text whitespace while preserving the source character limit across reader chunks.</summary>
internal struct SvgTextWhitespaceNormalizer
{
    private readonly SvgTextWhitespaceMode _mode;
    private bool _hasOutput;
    private bool _hasPendingWhitespace;
    private int _sourceCharacterCount;

    /// <summary>Gets the number of source UTF-16 characters consumed before normalization.</summary>
    internal int SourceCharacterCount => _sourceCharacterCount;

    /// <summary>Gets whether this element has emitted normalized text content.</summary>
    internal bool HasOutput => _hasOutput;

    /// <summary>Gets this element's inherited XML whitespace mode.</summary>
    internal SvgTextWhitespaceMode Mode => _mode;

    /// <summary>Initializes a normalizer for one direct text element's admitted XML whitespace mode.</summary>
    internal SvgTextWhitespaceNormalizer(SvgTextWhitespaceMode mode) => _mode = mode;

    /// <summary>Appends direct text according to the configured XML whitespace mode without materializing an unbounded intermediate value.</summary>
    internal void Append(ReadOnlySpan<char> value, BudgetedTextBuffer output, int maximumSourceCharacters, ParserWorkCounter workCounter, CancellationToken cancellationToken)
        => Append(value, _mode, output, maximumSourceCharacters, workCounter, cancellationToken);

    /// <summary>Appends one inherited direct-text whitespace region without materializing an unbounded intermediate value.</summary>
    internal void Append(ReadOnlySpan<char> value, SvgTextWhitespaceMode mode, BudgetedTextBuffer output, int maximumSourceCharacters, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(workCounter);
        if (value.Length > maximumSourceCharacters - _sourceCharacterCount) throw new SvgException(SvgErrorCode.LimitExceeded, "The document direct text character count exceeds the admitted layout limit.");
        _sourceCharacterCount += value.Length;
        for (int offset = 0; offset < value.Length; offset += ParserWorkCounter.ByteCheckpointSize)
        {
            workCounter.Consume(cancellationToken);
            int count = Math.Min(ParserWorkCounter.ByteCheckpointSize, value.Length - offset);
            if (mode == SvgTextWhitespaceMode.Preserve)
            {
                FlushPendingWhitespace(output, maximumSourceCharacters, workCounter, cancellationToken, count != 0);
                AppendPreservedChunk(value.Slice(offset, count), output, maximumSourceCharacters, workCounter, cancellationToken);
                _hasOutput |= count != 0;
                continue;
            }
            if (mode != SvgTextWhitespaceMode.Default) throw new ArgumentOutOfRangeException(nameof(mode));
            AppendChunk(value.Slice(offset, count), output, maximumSourceCharacters, workCounter, cancellationToken);
        }
    }

    private void AppendChunk(ReadOnlySpan<char> value, BudgetedTextBuffer output, int maximumSourceCharacters, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        int index = 0;
        while (index < value.Length)
        {
            if (IsCollapsible(value[index]))
            {
                _hasPendingWhitespace |= _hasOutput;
                index++;
                continue;
            }
            FlushPendingWhitespace(output, maximumSourceCharacters, workCounter, cancellationToken, true);
            int end = index + 1;
            while (end < value.Length && !IsCollapsible(value[end])) end++;
            output.Append(value.Slice(index, end - index), maximumSourceCharacters, workCounter, cancellationToken);
            _hasOutput = true;
            index = end;
        }
    }

    private void FlushPendingWhitespace(BudgetedTextBuffer output, int maximumSourceCharacters, ParserWorkCounter workCounter, CancellationToken cancellationToken, bool hasNextContent)
    {
        if (!_hasPendingWhitespace || !hasNextContent) return;
        output.Append(" ", maximumSourceCharacters, workCounter, cancellationToken);
        _hasPendingWhitespace = false;
        _hasOutput = true;
    }

    /// <summary>Commits whitespace before a child element that has emitted visible text.</summary>
    internal void ObserveChildContent(BudgetedTextBuffer output, int maximumSourceCharacters, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        FlushPendingWhitespace(output, maximumSourceCharacters, workCounter, cancellationToken, true);
        _hasOutput = true;
    }

    /// <summary>Discards default-mode whitespace trailing this element's own character data.</summary>
    internal void CompleteElement() => _hasPendingWhitespace = false;

    private static void AppendPreservedChunk(ReadOnlySpan<char> value, BudgetedTextBuffer output, int maximumSourceCharacters, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        int replacement = value.IndexOfAny('\t', '\r', '\n');
        if (replacement < 0)
        {
            output.Append(value, maximumSourceCharacters, workCounter, cancellationToken);
            return;
        }
        Span<char> normalized = stackalloc char[value.Length];
        value.CopyTo(normalized);
        do
        {
            normalized[replacement] = ' ';
            int searchStart = replacement + 1;
            int next = normalized[searchStart..].IndexOfAny('\t', '\r', '\n');
            replacement = next < 0 ? -1 : searchStart + next;
        } while (replacement >= 0);
        output.Append(normalized, maximumSourceCharacters, workCounter, cancellationToken);
    }

    /// <summary>Observes one discovery text chunk and reports whether normalization can produce a glyph.</summary>
    internal static bool HasNonWhitespaceText(ReadOnlySpan<char> value, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workCounter);
        for (int offset = 0; offset < value.Length; offset += ParserWorkCounter.ByteCheckpointSize)
        {
            workCounter.Consume(cancellationToken);
            int count = Math.Min(ParserWorkCounter.ByteCheckpointSize, value.Length - offset);
            foreach (char character in value.Slice(offset, count)) if (!IsCollapsible(character)) return true;
        }
        return false;
    }

    /// <summary>Observes one preserve-mode discovery chunk and reports whether it has text to lay out.</summary>
    internal static bool HasText(ReadOnlySpan<char> value, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workCounter);
        for (int offset = 0; offset < value.Length; offset += ParserWorkCounter.ByteCheckpointSize)
        {
            workCounter.Consume(cancellationToken);
            if (Math.Min(ParserWorkCounter.ByteCheckpointSize, value.Length - offset) != 0) return true;
        }
        return false;
    }

    private static bool IsCollapsible(char value) => value is ' ' or '\t' or '\r' or '\n';
}

/// <summary>Parses the initial explicit single-font SVG text attribute subset.</summary>
internal static class SvgTextSyntax
{
    internal static SvgTextStyle Parse(XmlReader reader)
        => Parse(reader, new ParserWorkCounter(long.MaxValue), CancellationToken.None, SvgInheritedTextStyle.Default);

    /// <summary>Parses direct text font syntax while charging inline-property scans to the current conversion.</summary>
    internal static SvgTextStyle Parse(XmlReader reader, ParserWorkCounter workCounter, CancellationToken cancellationToken)
        => Parse(reader, workCounter, cancellationToken, SvgInheritedTextStyle.Default);

    internal static SvgTextStyle Parse(XmlReader reader, ParserWorkCounter workCounter, CancellationToken cancellationToken, SvgInheritedTextStyle inherited)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(workCounter);
        InlineStyle inline = SvgInlineStyleParser.Parse(reader.GetAttribute("style"), workCounter, cancellationToken, true);
        return Parse(reader, inline.TextFont, inherited, inline.LetterSpacing);
    }

    /// <summary>Parses direct text font syntax from declarations already scanned by style resolution.</summary>
    internal static SvgTextStyle Parse(XmlReader reader, SvgTextInlineFontProperties inline)
        => Parse(reader, inline, SvgInheritedTextStyle.Default, null);

    /// <summary>Parses direct text declarations when no resolved scalar letter spacing is present.</summary>
    internal static SvgTextStyle Parse(XmlReader reader, SvgTextInlineFontProperties inline, SvgInheritedTextStyle inherited)
        => Parse(reader, inline, inherited, null);

    /// <summary>Parses direct text font syntax from declarations already scanned by style resolution and bounded ancestor inheritance.</summary>
    internal static SvgTextStyle Parse(XmlReader reader, SvgTextInlineFontProperties inline, SvgInheritedTextStyle inherited, string? letterSpacing)
    {
        ArgumentNullException.ThrowIfNull(reader);
        string family = SvgTextFontSyntax.ParseFamily(inline.Family ?? reader.GetAttribute("font-family") ?? inherited.Family ?? throw new SvgException(SvgErrorCode.UnsupportedFeature, "Text requires an explicit font-family in the initial supported subset."));
        string? weightValue = inline.Weight ?? reader.GetAttribute("font-weight");
        int weight = weightValue is null ? inherited.Weight : SvgTextFontSyntax.ParseWeight(weightValue);
        string? styleValue = inline.Style ?? reader.GetAttribute("font-style");
        bool italic = styleValue is null ? inherited.Italic : SvgTextFontSyntax.ParseItalic(styleValue);
        string? sizeValue = inline.Size ?? reader.GetAttribute("font-size");
        double fontSize = sizeValue is null ? inherited.FontSize : SvgGeometrySyntax.ParseTextCoordinate(sizeValue);
        if (fontSize <= 0) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Text font-size must be positive.");
        string? letterSpacingValue = letterSpacing ?? reader.GetAttribute("letter-spacing");
        double parsedLetterSpacing = letterSpacingValue is null ? inherited.LetterSpacing : SvgGeometrySyntax.ParseTextCoordinate(letterSpacingValue);
        SvgTextAnchor anchor = ParseAnchor(reader.GetAttribute("text-anchor") ?? "start");
        return new SvgTextStyle(new SvgFontFaceRequest(family, weight, italic), fontSize, SvgGeometrySyntax.ParseTextCoordinate(reader.GetAttribute("x") ?? "0"), SvgGeometrySyntax.ParseTextCoordinate(reader.GetAttribute("y") ?? "0"), SvgGeometrySyntax.ParseTextCoordinate(reader.GetAttribute("dx") ?? "0"), SvgGeometrySyntax.ParseTextCoordinate(reader.GetAttribute("dy") ?? "0"), parsedLetterSpacing, anchor, reader.GetAttribute("id") ?? string.Empty);
    }

    /// <summary>Resolves CSS and XML direct-text whitespace declarations over the inherited CSS mode.</summary>
    internal static SvgTextWhitespaceMode ParseWhitespaceMode(XmlReader reader, string? whiteSpace, SvgTextWhitespaceMode inherited)
    {
        ArgumentNullException.ThrowIfNull(reader);
        string? value = reader.GetAttribute("space", "http://www.w3.org/XML/1998/namespace");
        SvgTextWhitespaceMode xmlMode = value is null ? inherited : ParseXmlWhitespaceMode(value);
        return whiteSpace is null ? xmlMode : ParseCssWhitespaceMode(whiteSpace);
    }

    /// <summary>Parses the direct-text-only XML whitespace mode with default CSS inheritance.</summary>
    internal static SvgTextWhitespaceMode ParseWhitespaceMode(XmlReader reader)
        => ParseWhitespaceMode(reader, null, SvgTextWhitespaceMode.Default);

    /// <summary>Parses the bounded CSS whitespace values shared by direct text and its inheriting containers.</summary>
    internal static SvgTextWhitespaceMode ParseCssWhitespaceMode(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        string token = value.Trim();
        if (token.Equals("normal", StringComparison.OrdinalIgnoreCase)) return SvgTextWhitespaceMode.Default;
        if (token.Equals("pre", StringComparison.OrdinalIgnoreCase)) return SvgTextWhitespaceMode.Preserve;
        throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only normal and pre CSS white-space values are supported for direct text.");
    }

    private static SvgTextWhitespaceMode ParseXmlWhitespaceMode(string value)
    {
        if (value == "default") return SvgTextWhitespaceMode.Default;
        if (value == "preserve") return SvgTextWhitespaceMode.Preserve;
        throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only default and preserve xml:space values are supported for direct text.");
    }

    /// <summary>Recognizes nested whitespace-only tspan declarations and resolves them over the parent mode.</summary>
    internal static bool TryParseTspanWhitespaceMode(XmlReader reader, SvgTextWhitespaceMode inherited, ParserWorkCounter workCounter, CancellationToken cancellationToken, out SvgTextWhitespaceMode mode)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(workCounter);
        mode = inherited;
        string? xmlSpace = null;
        string? whiteSpace = null;
        if (!reader.HasAttributes) return true;
        while (reader.MoveToNextAttribute())
        {
            workCounter.Consume(cancellationToken);
            if (reader.NamespaceURI == "http://www.w3.org/XML/1998/namespace" && reader.LocalName == "space")
            {
                if (xmlSpace is not null) { reader.MoveToElement(); mode = inherited; return false; }
                xmlSpace = reader.Value;
                continue;
            }
            if (reader.NamespaceURI.Length == 0 && reader.LocalName == "style")
            {
                if (whiteSpace is not null) { reader.MoveToElement(); mode = inherited; return false; }
                InlineStyle style = SvgInlineStyleParser.Parse(reader.Value, workCounter, cancellationToken, true);
                if (!IsWhitespaceOnly(style)) { reader.MoveToElement(); mode = inherited; return false; }
                whiteSpace = style.WhiteSpace;
                continue;
            }
            reader.MoveToElement();
            mode = inherited;
            return false;
        }
        reader.MoveToElement();
        mode = xmlSpace is null ? inherited : ParseXmlWhitespaceMode(xmlSpace);
        if (whiteSpace is not null) mode = ParseCssWhitespaceMode(whiteSpace);
        return true;

        static bool IsWhitespaceOnly(InlineStyle style)
            => style.WhiteSpace is not null && style.Color is null && style.Fill is null && style.FillOpacity is null && style.Stroke is null && style.Display is null && style.Visibility is null && style.Opacity is null && style.FillRule is null && style.TextFont.Family is null && style.TextFont.Weight is null && style.TextFont.Style is null && style.TextFont.Size is null;
    }

    private static SvgTextAnchor ParseAnchor(string value)
    {
        string token = value.Trim();
        if (token.Equals("start", StringComparison.OrdinalIgnoreCase)) return SvgTextAnchor.Start;
        if (token.Equals("middle", StringComparison.OrdinalIgnoreCase)) return SvgTextAnchor.Middle;
        if (token.Equals("end", StringComparison.OrdinalIgnoreCase)) return SvgTextAnchor.End;
        throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only start, middle, and end text-anchor values are supported.");
    }
}

/// <summary>Normalizes the explicit single-family text font tokens shared by discovery and direct rendering.</summary>
internal static class SvgTextFontSyntax
{
    internal static string ParseFamily(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        string family = value.Trim();
        if (family.Length >= 2 && ((family[0] == '\'' && family[^1] == '\'') || (family[0] == '"' && family[^1] == '"'))) family = family[1..^1];
        if (family.Length == 0 || family.Contains(',')) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only one nonempty text font family is supported.");
        return family;
    }

    internal static int ParseWeight(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        string token = value.Trim();
        if (token.Equals("normal", StringComparison.OrdinalIgnoreCase)) return 400;
        if (token.Equals("bold", StringComparison.OrdinalIgnoreCase)) return 700;
        if (int.TryParse(token, CultureInfo.InvariantCulture, out int weight) && weight > 0) return weight;
        throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only positive numeric, normal, and bold text font weights are supported.");
    }

    internal static bool ParseItalic(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Trim().Equals("normal", StringComparison.OrdinalIgnoreCase)) return false;
        if (value.Trim().Equals("italic", StringComparison.OrdinalIgnoreCase)) return true;
        throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only normal and italic text font styles are supported.");
    }

}

/// <summary>Represents the direct-text font declarations found in one inline style value.</summary>
internal readonly record struct SvgTextInlineFontProperties(string? Family, string? Weight, string? Style, string? Size)
{
    /// <summary>Uses this declaration's explicitly present values before the lower-precedence declarations.</summary>
    internal SvgTextInlineFontProperties Overlay(SvgTextInlineFontProperties lower)
        => new(Family ?? lower.Family, Weight ?? lower.Weight, Style ?? lower.Style, Size ?? lower.Size);
}

/// <summary>Represents the explicit single-font style and baseline origin of one initial text run.</summary>
internal readonly record struct SvgTextStyle(SvgFontFaceRequest Font, double FontSize, double X, double Y, double Dx, double Dy, double LetterSpacing, SvgTextAnchor Anchor, string SourceElementId);

/// <summary>Identifies the admitted horizontal anchor for one direct text run.</summary>
internal enum SvgTextAnchor { Start, Middle, End }
