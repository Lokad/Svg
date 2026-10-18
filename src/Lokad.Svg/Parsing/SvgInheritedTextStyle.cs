using System.Xml;

using Lokad.Svg;
using Lokad.Svg.Fonts;

namespace Lokad.Svg.Parsing;

/// <summary>Represents the admitted font request and text properties inherited by a rendered text descendant.</summary>
internal sealed class SvgInheritedTextStyle
{
    // The initial CSS family is a request into the caller's catalogue, never a host-font lookup.
    internal static SvgInheritedTextStyle Default { get; } = new("serif", 400, false, 16, SvgTextWhitespaceMode.Default, 0, SvgTextAnchor.Start);

    internal SvgInheritedTextStyle(string? family, int weight, bool italic, double fontSize, SvgTextWhitespaceMode whitespaceMode, double letterSpacing, SvgTextAnchor anchor)
    {
        if (weight <= 0) throw new ArgumentOutOfRangeException(nameof(weight));
        if (!double.IsFinite(fontSize) || fontSize <= 0) throw new ArgumentOutOfRangeException(nameof(fontSize));
        if (whitespaceMode is not (SvgTextWhitespaceMode.Default or SvgTextWhitespaceMode.Preserve)) throw new ArgumentOutOfRangeException(nameof(whitespaceMode));
        if (!double.IsFinite(letterSpacing)) throw new ArgumentOutOfRangeException(nameof(letterSpacing));
        if (anchor is not (SvgTextAnchor.Start or SvgTextAnchor.Middle or SvgTextAnchor.End)) throw new ArgumentOutOfRangeException(nameof(anchor));
        Family = family;
        Weight = weight;
        Italic = italic;
        FontSize = fontSize;
        WhitespaceMode = whitespaceMode;
        LetterSpacing = letterSpacing;
        Anchor = anchor;
    }

    /// <summary>Returns this style with only the whitespace mode replaced.</summary>
    internal SvgInheritedTextStyle WithWhitespaceMode(SvgTextWhitespaceMode whitespaceMode)
    {
        if (whitespaceMode is not (SvgTextWhitespaceMode.Default or SvgTextWhitespaceMode.Preserve)) throw new ArgumentOutOfRangeException(nameof(whitespaceMode));
        return new SvgInheritedTextStyle(Family, Weight, Italic, FontSize, whitespaceMode, LetterSpacing, Anchor) { WordSpacing = WordSpacing, Decoration = Decoration };
    }

    internal SvgInheritedTextStyle WithDecoration(SvgTextDecorationStyle? decoration)
        => new(Family, Weight, Italic, FontSize, WhitespaceMode, LetterSpacing, Anchor) { WordSpacing = WordSpacing, Decoration = decoration };

    internal string? Family { get; }
    internal int Weight { get; }
    internal bool Italic { get; }
    internal double FontSize { get; }
    internal SvgTextWhitespaceMode WhitespaceMode { get; }
    internal double LetterSpacing { get; }
    internal double WordSpacing { get; init; }
    internal SvgTextDecorationStyle? Decoration { get; init; }
    internal SvgTextAnchor Anchor { get; }
}

/// <summary>Combines inline, presentation, and ancestor text properties using the direct-text precedence subset.</summary>
internal static class SvgTextInheritance
{
    /// <summary>Parses a container xml:space declaration with the inherited mode as its default.</summary>
    private static SvgTextWhitespaceMode ParseContainerXmlSpace(string value)
    {
        if (value == "default") return SvgTextWhitespaceMode.Default;
        if (value == "preserve") return SvgTextWhitespaceMode.Preserve;
        throw new SvgException(SvgErrorCode.MalformedInput, "Container xml:space values must be default or preserve.");
    }

    internal static SvgInheritedTextStyle Resolve(XmlReader reader, SvgInheritedTextStyle inherited, SvgTextInlineFontProperties inline, string? whiteSpace, string? letterSpacing)
        => Resolve(reader, inherited, inline, whiteSpace, letterSpacing, null);

    internal static SvgInheritedTextStyle Resolve(XmlReader reader, SvgInheritedTextStyle inherited, SvgTextInlineFontProperties inline, string? whiteSpace, string? letterSpacing, string? wordSpacing)
    {
        ArgumentNullException.ThrowIfNull(reader);
        string family = SvgTextFontSyntax.ResolveFamily(inline.Family ?? reader.GetAttribute("font-family"), inherited.Family);
        string? weightValue = inline.Weight ?? reader.GetAttribute("font-weight");
        int weight = weightValue is null ? inherited.Weight : SvgTextFontSyntax.ParseWeight(weightValue);
        string? styleValue = inline.Style ?? reader.GetAttribute("font-style");
        bool italic = styleValue is null ? inherited.Italic : SvgTextFontSyntax.ParseItalic(styleValue);
        string? sizeValue = inline.Size ?? reader.GetAttribute("font-size");
        double fontSize = sizeValue is null ? inherited.FontSize : SvgGeometrySyntax.ParseFontSize(sizeValue);
        if (fontSize <= 0) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Text font-size must be positive.");
        string? xmlSpace = reader.GetAttribute("space", "http://www.w3.org/XML/1998/namespace");
        SvgTextWhitespaceMode xmlMode = xmlSpace is null ? inherited.WhitespaceMode : ParseContainerXmlSpace(xmlSpace);
        SvgTextWhitespaceMode whitespaceMode = whiteSpace is null ? xmlMode : SvgTextSyntax.ParseCssWhitespaceMode(whiteSpace);
        string? letterSpacingValue = letterSpacing ?? reader.GetAttribute("letter-spacing");
        double resolvedLetterSpacing = letterSpacingValue is null ? inherited.LetterSpacing : SvgGeometrySyntax.ParseTextCoordinate(letterSpacingValue);
        string? wordSpacingValue = wordSpacing ?? reader.GetAttribute("word-spacing");
        double resolvedWordSpacing = wordSpacingValue is null ? inherited.WordSpacing : SvgTextSyntax.ParseWordSpacing(wordSpacingValue);
        return new SvgInheritedTextStyle(family, weight, italic, fontSize, whitespaceMode, resolvedLetterSpacing, SvgTextSyntax.ParseAnchor(reader.GetAttribute("text-anchor"), inherited.Anchor)) { WordSpacing = resolvedWordSpacing, Decoration = inherited.Decoration };
    }
}
