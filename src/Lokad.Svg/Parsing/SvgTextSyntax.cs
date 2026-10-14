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

/// <summary>Receives normalized direct text with its owning span depth, active letter spacing, and explicit positioning scope lifecycle.</summary>
/// <remarks>Implementations are parser-thread confined, own retained output, reserve material buffers before growth and observe the borrowed work/cancellation context within bounded chunks.</remarks>
internal interface ISvgTextOutput
{
    /// <summary>Appends an emitted interval; <paramref name="scopeDepth"/> identifies the owning open text span, and the caller retains the value and work context.</summary>
    void Append(ReadOnlySpan<char> value, double letterSpacing, int scopeDepth, int maximumLength, ParserWorkCounter workCounter, CancellationToken cancellationToken);

    /// <summary>Begins an explicitly positioned text span; a matching exit at the same depth is required, and implementations retain no caller-owned data.</summary>
    void EnterPositioningSpan(SvgTextPositionOffset offset, int scopeDepth, ParserWorkCounter workCounter, CancellationToken cancellationToken);

    /// <summary>Begins validated coordinate/rotation lists over this span's normalized scalar subtree; token strings remain borrowed under the parser allowance.</summary>
    void EnterPositioningSpan(SvgTextPositionLists lists, int scopeDepth, ParserWorkCounter workCounter, CancellationToken cancellationToken);

    /// <summary>Ends the most recently entered explicitly positioned text span at <paramref name="scopeDepth"/>.</summary>
    void ExitPositioningSpan(int scopeDepth);
}

/// <summary>Pairs the inherited whitespace and scalar letter-spacing values active in one direct-text span with its optional explicit position offset.</summary>
internal readonly record struct SvgTextSpanStyle
{
    internal SvgTextSpanStyle(SvgTextWhitespaceMode whitespaceMode, double letterSpacing) : this(whitespaceMode, letterSpacing, null) { }

    internal SvgTextSpanStyle(SvgTextWhitespaceMode whitespaceMode, double letterSpacing, SvgTextPositionOffset? positionOffset)
    {
        if (whitespaceMode is not (SvgTextWhitespaceMode.Default or SvgTextWhitespaceMode.Preserve)) throw new ArgumentOutOfRangeException(nameof(whitespaceMode));
        if (!double.IsFinite(letterSpacing)) throw new ArgumentOutOfRangeException(nameof(letterSpacing));
        WhitespaceMode = whitespaceMode;
        LetterSpacing = letterSpacing;
        PositionOffset = positionOffset;
        Paint = SvgTextPaintStyle.Default;
    }

    internal SvgTextWhitespaceMode WhitespaceMode { get; }

    internal double LetterSpacing { get; }

    /// <summary>Gets the scalar position adjustment explicitly declared by this span, if any; it is not inherited as a style value.</summary>
    internal SvgTextPositionOffset? PositionOffset { get; }
    internal SvgTextPositionLists PositionLists { get; init; }
    internal SvgTextPaintStyle Paint { get; init; }
    internal SvgInheritedTextStyle Font { get; init; } = SvgInheritedTextStyle.Default;
}

/// <summary>Identifies one contiguous UTF-16 interval emitted with a non-default scalar letter spacing.</summary>
internal readonly record struct SvgTextLetterSpacingSpan(int Start, int End, double LetterSpacing);

/// <summary>Owns one extracted layout until its consumer disposes it; sibling path runs have independent whitespace and anchoring boundaries.</summary>
internal readonly record struct SvgExtractedTextRun(BudgetedTextLayout Layout, SvgTextPathReference? Path, OwnedParserArray<SvgTextPaintSpan> PaintSpans, OwnedParserArray<SvgTextFontSpan> FontSpans) : IDisposable
{
    public void Dispose() { Layout.Dispose(); PaintSpans.Dispose(); FontSpans.Dispose(); }
}

/// <summary>Retains inherited font declarations over one normalized UTF-16 interval.</summary>
internal readonly record struct SvgTextFontSpan(int Start, int End, SvgInheritedTextStyle Font);

/// <summary>Represents explicit scalar coordinates and offsets before the first retained character in a text span.</summary>
internal readonly record struct SvgTextPositionOffset
{
    internal SvgTextPositionOffset(double? dx, double? dy)
        : this(null, null, dx, dy) { }

    internal SvgTextPositionOffset(double? x, double? y, double? dx, double? dy)
    {
        if (x is null && y is null && dx is null && dy is null) throw new ArgumentException("A text position offset requires at least one axis.", nameof(x));
        if (x is double resolvedX && !double.IsFinite(resolvedX)) throw new ArgumentOutOfRangeException(nameof(x));
        if (y is double resolvedY && !double.IsFinite(resolvedY)) throw new ArgumentOutOfRangeException(nameof(y));
        if (dx is double resolvedDx && !double.IsFinite(resolvedDx)) throw new ArgumentOutOfRangeException(nameof(dx));
        if (dy is double resolvedDy && !double.IsFinite(resolvedDy)) throw new ArgumentOutOfRangeException(nameof(dy));
        X = x;
        Y = y;
        Dx = dx;
        Dy = dy;
    }

    /// <summary>Gets the explicitly specified horizontal absolute coordinate, if present.</summary>
    internal double? X { get; }

    /// <summary>Gets the explicitly specified vertical absolute coordinate, if present.</summary>
    internal double? Y { get; }

    /// <summary>Gets the explicitly specified horizontal relative offset, if present.</summary>
    internal double? Dx { get; }

    /// <summary>Gets the explicitly specified vertical relative offset, if present.</summary>
    internal double? Dy { get; }

    internal bool IsZero => X is null && Y is null && Dx is null or 0 && Dy is null or 0;
}

/// <summary>Identifies a normalized UTF-16 boundary, optional absolute coordinates, and offsets applied before one positioned glyph.</summary>
internal readonly record struct SvgTextPositionAdjustment(int Start, double? X, double? Y, double Dx, double Dy)
{
    internal SvgTextPositionAdjustment(int start, double dx, double dy) : this(start, null, null, dx, dy) { }

    internal bool HasAbsolutePosition => X is not null || Y is not null;
    internal bool OverridesDx { get; init; }
    internal bool OverridesDy { get; init; }
    internal double? Rotation { get; init; }
}

/// <summary>Normalizes direct-text whitespace while preserving the source character limit across reader chunks.</summary>
internal struct SvgTextWhitespaceNormalizer
{
    private readonly SvgTextWhitespaceMode _mode;
    private readonly double _letterSpacing;
    private readonly int _scopeDepth;
    private readonly bool _ownsPositioningSpan;
    private bool _hasOutput;
    private bool _hasPendingWhitespace;
    private int _sourceCharacterCount;

    /// <summary>Gets the number of source UTF-16 characters consumed before normalization.</summary>
    internal int SourceCharacterCount => _sourceCharacterCount;

    /// <summary>Gets whether this element has emitted normalized text content.</summary>
    internal bool HasOutput => _hasOutput;

    /// <summary>Gets this element's inherited XML whitespace mode.</summary>
    internal SvgTextWhitespaceMode Mode => _mode;

    /// <summary>Gets the scalar letter spacing inherited by text emitted from this element.</summary>
    internal double LetterSpacing => _letterSpacing;

    /// <summary>Gets this normalizer's owning direct-text span depth.</summary>
    internal int ScopeDepth => _scopeDepth;

    /// <summary>Gets whether this normalizer owns an open explicit positioning scope.</summary>
    internal bool OwnsPositioningSpan => _ownsPositioningSpan;

    /// <summary>Initializes a normalizer for one direct text element's admitted XML whitespace mode.</summary>
    internal SvgTextWhitespaceNormalizer(SvgTextWhitespaceMode mode) : this(mode, 0, 0, false) { }

    /// <summary>Initializes a normalizer for one direct-text span's whitespace mode and scalar letter spacing.</summary>
    internal SvgTextWhitespaceNormalizer(SvgTextWhitespaceMode mode, double letterSpacing) : this(mode, letterSpacing, 0, false) { }

    /// <summary>Initializes a normalizer for one direct-text span with its element depth and explicit positioning ownership.</summary>
    internal SvgTextWhitespaceNormalizer(SvgTextWhitespaceMode mode, double letterSpacing, int scopeDepth, bool ownsPositioningSpan)
    {
        if (mode is not (SvgTextWhitespaceMode.Default or SvgTextWhitespaceMode.Preserve)) throw new ArgumentOutOfRangeException(nameof(mode));
        if (!double.IsFinite(letterSpacing)) throw new ArgumentOutOfRangeException(nameof(letterSpacing));
        if (scopeDepth < 0) throw new ArgumentOutOfRangeException(nameof(scopeDepth));
        _mode = mode;
        _letterSpacing = letterSpacing;
        _scopeDepth = scopeDepth;
        _ownsPositioningSpan = ownsPositioningSpan;
    }

    /// <summary>Appends direct text according to the configured XML whitespace mode without materializing an unbounded intermediate value.</summary>
    internal void Append(ReadOnlySpan<char> value, ISvgTextOutput output, int maximumSourceCharacters, ParserWorkCounter workCounter, CancellationToken cancellationToken)
        => Append(value, _mode, output, maximumSourceCharacters, workCounter, cancellationToken);

    /// <summary>Appends one inherited direct-text whitespace region without materializing an unbounded intermediate value.</summary>
    internal void Append(ReadOnlySpan<char> value, SvgTextWhitespaceMode mode, ISvgTextOutput output, int maximumSourceCharacters, ParserWorkCounter workCounter, CancellationToken cancellationToken)
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

    private void AppendChunk(ReadOnlySpan<char> value, ISvgTextOutput output, int maximumSourceCharacters, ParserWorkCounter workCounter, CancellationToken cancellationToken)
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
            output.Append(value.Slice(index, end - index), _letterSpacing, _scopeDepth, maximumSourceCharacters, workCounter, cancellationToken);
            _hasOutput = true;
            index = end;
        }
    }

    private void FlushPendingWhitespace(ISvgTextOutput output, int maximumSourceCharacters, ParserWorkCounter workCounter, CancellationToken cancellationToken, bool hasNextContent)
    {
        if (!_hasPendingWhitespace || !hasNextContent) return;
        output.Append(" ", _letterSpacing, _scopeDepth, maximumSourceCharacters, workCounter, cancellationToken);
        _hasPendingWhitespace = false;
        _hasOutput = true;
    }

    /// <summary>Commits whitespace before a child element that has emitted visible text.</summary>
    internal void ObserveChildContent(ISvgTextOutput output, int maximumSourceCharacters, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        FlushPendingWhitespace(output, maximumSourceCharacters, workCounter, cancellationToken, true);
        _hasOutput = true;
    }

    /// <summary>Discards default-mode whitespace trailing this element's own character data.</summary>
    internal void CompleteElement() => _hasPendingWhitespace = false;

    private void AppendPreservedChunk(ReadOnlySpan<char> value, ISvgTextOutput output, int maximumSourceCharacters, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        int replacement = value.IndexOfAny('\t', '\r', '\n');
        if (replacement < 0)
        {
            output.Append(value, _letterSpacing, _scopeDepth, maximumSourceCharacters, workCounter, cancellationToken);
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
        output.Append(normalized, _letterSpacing, _scopeDepth, maximumSourceCharacters, workCounter, cancellationToken);
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
        string family = SvgTextFontSyntax.ParseFamily(inline.Family ?? reader.GetAttribute("font-family") ?? inherited.Family ?? "serif");
        string? weightValue = inline.Weight ?? reader.GetAttribute("font-weight");
        int weight = weightValue is null ? inherited.Weight : SvgTextFontSyntax.ParseWeight(weightValue);
        string? styleValue = inline.Style ?? reader.GetAttribute("font-style");
        bool italic = styleValue is null ? inherited.Italic : SvgTextFontSyntax.ParseItalic(styleValue);
        string? sizeValue = inline.Size ?? reader.GetAttribute("font-size");
        double fontSize = sizeValue is null ? inherited.FontSize : SvgGeometrySyntax.ParseFontSize(sizeValue);
        if (fontSize <= 0) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Text font-size must be positive.");
        string? letterSpacingValue = letterSpacing ?? reader.GetAttribute("letter-spacing");
        double parsedLetterSpacing = letterSpacingValue is null ? inherited.LetterSpacing : SvgGeometrySyntax.ParseTextCoordinate(letterSpacingValue);
        SvgTextAnchor anchor = ParseAnchor(reader.GetAttribute("text-anchor"), inherited.Anchor);
        string? x = reader.GetAttribute("x"), y = reader.GetAttribute("y"), dx = reader.GetAttribute("dx"), dy = reader.GetAttribute("dy"), rotate = reader.GetAttribute("rotate");
        double firstX = SvgTextPositionLists.ReadFirst(x, out bool manyX), firstY = SvgTextPositionLists.ReadFirst(y, out bool manyY);
        double firstDx = SvgTextPositionLists.ReadFirst(dx, out bool manyDx), firstDy = SvgTextPositionLists.ReadFirst(dy, out bool manyDy);
        if (rotate is not null) _ = SvgTextPositionLists.ReadFirst(rotate, false, out _);
        SvgTextPositionLists lists = manyX || manyY || manyDx || manyDy || rotate is not null ? new(x, y, dx, dy, rotate) : default;
        double? textLength = reader.GetAttribute("textLength") is string authoredLength ? SvgGeometrySyntax.ParseTextCoordinate(authoredLength) : null;
        if (textLength < 0) throw new SvgException(SvgErrorCode.MalformedInput, "textLength must be nonnegative.");
        SvgTextLengthAdjust lengthAdjust = reader.GetAttribute("lengthAdjust") switch
        {
            null or "spacing" => SvgTextLengthAdjust.Spacing,
            "spacingAndGlyphs" => SvgTextLengthAdjust.SpacingAndGlyphs,
            _ => throw new SvgException(SvgErrorCode.UnsupportedFeature, "This text length adjustment is not implemented."),
        };
        SvgTextBaseline baseline = reader.GetAttribute("dominant-baseline") switch
        {
            null or "auto" or "alphabetic" => SvgTextBaseline.Alphabetic,
            "middle" => SvgTextBaseline.Middle,
            "hanging" => SvgTextBaseline.Hanging,
            _ => throw new SvgException(SvgErrorCode.UnsupportedFeature, "This dominant text baseline is not implemented."),
        };
        return new SvgTextStyle(new SvgFontFaceRequest(family, weight, italic), fontSize, firstX, firstY, firstDx, firstDy, parsedLetterSpacing, anchor, reader.GetAttribute("id") ?? string.Empty)
        { PositionLists = lists, TextLength = textLength, LengthAdjust = lengthAdjust, Baseline = baseline };
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

    /// <summary>Recognizes bounded nested whitespace, positioning and solid-paint declarations over the parent values.</summary>
    internal static bool TryParseTspanStyle(XmlReader reader, SvgTextSpanStyle inherited, InlineStyle styleSheet, ParserWorkCounter workCounter, CancellationToken cancellationToken, out SvgTextSpanStyle style)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(workCounter);
        style = inherited;
        if (HasUnsupportedProperties(styleSheet)) return false;
        string? xmlSpace = null;
        string? whiteSpace = null;
        InlineStyle parsedInline = default;
        InlineImportantProperties inlineImportant = InlineImportantProperties.None;
        string? inlineLetterSpacing = null;
        string? presentationLetterSpacing = null;
        string? x = null;
        string? y = null;
        string? dx = null;
        string? dy = null;
        string? rotate = null;
        if (!reader.HasAttributes)
        {
            style = new SvgTextSpanStyle(styleSheet.WhiteSpace is string styleSheetWhiteSpace ? ParseCssWhitespaceMode(styleSheetWhiteSpace) : inherited.WhitespaceMode, styleSheet.LetterSpacing is string styleSheetLetterSpacing ? SvgGeometrySyntax.ParseTextCoordinate(styleSheetLetterSpacing) : inherited.LetterSpacing, null)
            { Paint = SvgTextPaintStyle.ResolveSpan(SvgComputedStyle.Resolve(reader, default, styleSheet), inherited.Paint), Font = SvgTextInheritance.Resolve(reader, inherited.Font, styleSheet.TextFont, null, null) };
            return true;
        }
        while (reader.MoveToNextAttribute())
        {
            workCounter.Consume(cancellationToken);
            if (reader.NamespaceURI == "http://www.w3.org/XML/1998/namespace" && reader.LocalName == "space")
            {
                if (xmlSpace is not null) { reader.MoveToElement(); style = inherited; return false; }
                xmlSpace = reader.Value;
                continue;
            }
            if (reader.NamespaceURI.Length == 0 && reader.LocalName == "style")
            {
                if (whiteSpace is not null || inlineLetterSpacing is not null) { reader.MoveToElement(); style = inherited; return false; }
                InlineStyle inline = SvgInlineStyleParser.Parse(reader.Value, workCounter, cancellationToken, true);
                if (HasUnsupportedProperties(inline)) { reader.MoveToElement(); style = inherited; return false; }
                parsedInline = inline;
                whiteSpace = inline.WhiteSpace;
                inlineImportant = inline.Important;
                inlineLetterSpacing = inline.LetterSpacing;
                continue;
            }
            if (reader.NamespaceURI.Length == 0 && reader.LocalName == "letter-spacing")
            {
                if (presentationLetterSpacing is not null) { reader.MoveToElement(); style = inherited; return false; }
                presentationLetterSpacing = reader.Value;
                continue;
            }
            if (reader.NamespaceURI.Length == 0 && reader.LocalName == "x")
            {
                if (x is not null) { reader.MoveToElement(); style = inherited; return false; }
                x = reader.Value;
                continue;
            }
            if (reader.NamespaceURI.Length == 0 && reader.LocalName == "y")
            {
                if (y is not null) { reader.MoveToElement(); style = inherited; return false; }
                y = reader.Value;
                continue;
            }
            if (reader.NamespaceURI.Length == 0 && reader.LocalName == "dx")
            {
                if (dx is not null) { reader.MoveToElement(); style = inherited; return false; }
                dx = reader.Value;
                continue;
            }
            if (reader.NamespaceURI.Length == 0 && reader.LocalName == "dy")
            {
                if (dy is not null) { reader.MoveToElement(); style = inherited; return false; }
                dy = reader.Value;
                continue;
            }
            if (reader.NamespaceURI.Length == 0 && reader.LocalName == "rotate")
            {
                rotate = reader.Value;
                continue;
            }
            if (reader.NamespaceURI.Length == 0 && reader.LocalName is "id" or "class") continue;
            if (reader.NamespaceURI.Length == 0 && reader.LocalName is "font-family" or "font-weight" or "font-style" or "font-size") continue;
            if (reader.NamespaceURI.Length == 0 && reader.LocalName is "color" or "fill" or "fill-opacity" or "stroke" or "fill-rule" or "display" or "visibility"
                or "stroke-width" or "stroke-opacity" or "stroke-linecap" or "stroke-linejoin" or "stroke-miterlimit" or "stroke-dasharray" or "stroke-dashoffset" or "paint-order" or "vector-effect") continue;
            reader.MoveToElement();
            style = inherited;
            return false;
        }
        reader.MoveToElement();
        SvgTextWhitespaceMode mode = xmlSpace is null ? inherited.WhitespaceMode : ParseXmlWhitespaceMode(xmlSpace);
        if (SvgInlineStyleParser.MergeWithImportance(whiteSpace, inlineImportant, InlineImportantProperties.WhiteSpace, styleSheet.WhiteSpace, styleSheet.Important) is string specifiedWhiteSpace) mode = ParseCssWhitespaceMode(specifiedWhiteSpace);
        string? letterSpacing = SvgInlineStyleParser.MergeWithImportance(inlineLetterSpacing, inlineImportant, InlineImportantProperties.LetterSpacing, styleSheet.LetterSpacing, styleSheet.Important) ?? presentationLetterSpacing;
        double? firstX = x is null ? null : SvgTextPositionLists.ReadFirst(x, out _);
        double? firstY = y is null ? null : SvgTextPositionLists.ReadFirst(y, out _);
        double? firstDx = dx is null ? null : SvgTextPositionLists.ReadFirst(dx, out _);
        double? firstDy = dy is null ? null : SvgTextPositionLists.ReadFirst(dy, out _);
        if (rotate is not null) _ = SvgTextPositionLists.ReadFirst(rotate, false, out _);
        SvgTextPositionOffset? positionOffset = x is null && y is null && dx is null && dy is null ? null : new(firstX, firstY, firstDx, firstDy);
        style = new SvgTextSpanStyle(mode, letterSpacing is null ? inherited.LetterSpacing : SvgGeometrySyntax.ParseTextCoordinate(letterSpacing), positionOffset)
        { PositionLists = new(x, y, dx, dy, rotate),
            Paint = SvgTextPaintStyle.ResolveSpan(SvgComputedStyle.Resolve(reader, parsedInline, styleSheet), inherited.Paint),
            Font = SvgTextInheritance.Resolve(reader, inherited.Font, SvgInlineStyleParser.MergeFont(parsedInline.TextFont, parsedInline.Important, styleSheet.TextFont, styleSheet.Important), null, null) };
        return true;

        static bool HasUnsupportedProperties(InlineStyle style)
            => style.Opacity is not null || style.Filter is not null || style.GeometryWidth is not null || style.GeometryHeight is not null;

    }

    /// <summary>Recognizes admitted nested text spans while returning only the inherited whitespace mode needed by font discovery.</summary>
    internal static bool TryParseTspanWhitespaceMode(XmlReader reader, SvgTextWhitespaceMode inherited, InlineStyle styleSheet, ParserWorkCounter workCounter, CancellationToken cancellationToken, out SvgTextWhitespaceMode mode)
    {
        bool parsed = TryParseTspanStyle(reader, new SvgTextSpanStyle(inherited, 0), styleSheet, workCounter, cancellationToken, out SvgTextSpanStyle style);
        mode = style.WhitespaceMode;
        return parsed;
    }

    /// <summary>Resolves one bounded presentation anchor over its inherited value; CSS anchor declarations remain outside this parser.</summary>
    internal static SvgTextAnchor ParseAnchor(string? value, SvgTextAnchor inherited)
    {
        if (value is null) return inherited;
        if (value.Length > SvgSyntaxLimits.MaximumValueTokenCharacters) throw new SvgException(SvgErrorCode.LimitExceeded, "The text anchor exceeds the configured syntax limit.");
        string token = value.Trim();
        if (token.Equals("inherit", StringComparison.OrdinalIgnoreCase)) return inherited;
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

    /// <summary>Validates the admitted default font variant shared by attributes and style declarations.</summary>
    internal static void ValidateFontVariant(ReadOnlySpan<char> value)
    {
        ReadOnlySpan<char> token = value.Trim();
        if (token.Equals("normal".AsSpan(), StringComparison.OrdinalIgnoreCase) || token.Equals("none".AsSpan(), StringComparison.OrdinalIgnoreCase)) return;
        throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only normal font variants are implemented.");
    }

}

/// <summary>Represents the direct-text font declarations found in one inline style value.</summary>
internal readonly record struct SvgTextInlineFontProperties(string? Family, string? Weight, string? Style, string? Size);

/// <summary>Represents the explicit single-font style and baseline origin of one initial text run.</summary>
internal readonly record struct SvgTextStyle(SvgFontFaceRequest Font, double FontSize, double X, double Y, double Dx, double Dy, double LetterSpacing, SvgTextAnchor Anchor, string SourceElementId)
{
    internal SvgTextPositionLists PositionLists { get; init; }
    internal double? TextLength { get; init; }
    internal SvgTextLengthAdjust LengthAdjust { get; init; }
    internal SvgTextBaseline Baseline { get; init; }
}

internal enum SvgTextLengthAdjust { Spacing, SpacingAndGlyphs }
internal enum SvgTextBaseline { Alphabetic, Middle, Hanging }

/// <summary>Identifies the admitted horizontal anchor for one direct text run.</summary>
internal enum SvgTextAnchor { Start, Middle, End }
