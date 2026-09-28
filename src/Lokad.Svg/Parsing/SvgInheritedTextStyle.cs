using System.Xml;

namespace Lokad.Svg.Parsing;

/// <summary>Represents the admitted single-font text properties inherited by a rendered text descendant.</summary>
internal sealed class SvgInheritedTextStyle
{
    internal static SvgInheritedTextStyle Default { get; } = new(null, 400, false, 16, SvgTextWhitespaceMode.Default, 0);

    internal SvgInheritedTextStyle(string? family, int weight, bool italic, double fontSize, SvgTextWhitespaceMode whitespaceMode, double letterSpacing)
    {
        if (weight <= 0) throw new ArgumentOutOfRangeException(nameof(weight));
        if (!double.IsFinite(fontSize) || fontSize <= 0) throw new ArgumentOutOfRangeException(nameof(fontSize));
        if (whitespaceMode is not (SvgTextWhitespaceMode.Default or SvgTextWhitespaceMode.Preserve)) throw new ArgumentOutOfRangeException(nameof(whitespaceMode));
        if (!double.IsFinite(letterSpacing)) throw new ArgumentOutOfRangeException(nameof(letterSpacing));
        Family = family;
        Weight = weight;
        Italic = italic;
        FontSize = fontSize;
        WhitespaceMode = whitespaceMode;
        LetterSpacing = letterSpacing;
    }

    /// <summary>Returns this style with only the whitespace mode replaced.</summary>
    internal SvgInheritedTextStyle WithWhitespaceMode(SvgTextWhitespaceMode whitespaceMode)
    {
        if (whitespaceMode is not (SvgTextWhitespaceMode.Default or SvgTextWhitespaceMode.Preserve)) throw new ArgumentOutOfRangeException(nameof(whitespaceMode));
        return new SvgInheritedTextStyle(Family, Weight, Italic, FontSize, whitespaceMode, LetterSpacing);
    }

    internal string? Family { get; }
    internal int Weight { get; }
    internal bool Italic { get; }
    internal double FontSize { get; }
    internal SvgTextWhitespaceMode WhitespaceMode { get; }
    internal double LetterSpacing { get; }
}

/// <summary>Combines inline, presentation, and ancestor text properties using the direct-text precedence subset.</summary>
internal static class SvgTextInheritance
{
    internal static SvgInheritedTextStyle Resolve(XmlReader reader, SvgInheritedTextStyle inherited, SvgTextInlineFontProperties inline, string? whiteSpace, string? letterSpacing)
    {
        ArgumentNullException.ThrowIfNull(reader);
        string? family = inline.Family ?? reader.GetAttribute("font-family") ?? inherited.Family;
        if (family is not null) family = SvgTextFontSyntax.ParseFamily(family);
        string? weightValue = inline.Weight ?? reader.GetAttribute("font-weight");
        int weight = weightValue is null ? inherited.Weight : SvgTextFontSyntax.ParseWeight(weightValue);
        string? styleValue = inline.Style ?? reader.GetAttribute("font-style");
        bool italic = styleValue is null ? inherited.Italic : SvgTextFontSyntax.ParseItalic(styleValue);
        string? sizeValue = inline.Size ?? reader.GetAttribute("font-size");
        double fontSize = sizeValue is null ? inherited.FontSize : SvgGeometrySyntax.ParseTextCoordinate(sizeValue);
        if (fontSize <= 0) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Text font-size must be positive.");
        SvgTextWhitespaceMode whitespaceMode = whiteSpace is null ? inherited.WhitespaceMode : SvgTextSyntax.ParseCssWhitespaceMode(whiteSpace);
        string? letterSpacingValue = letterSpacing ?? reader.GetAttribute("letter-spacing");
        double resolvedLetterSpacing = letterSpacingValue is null ? inherited.LetterSpacing : SvgGeometrySyntax.ParseTextCoordinate(letterSpacingValue);
        return new SvgInheritedTextStyle(family, weight, italic, fontSize, whitespaceMode, resolvedLetterSpacing);
    }
}
