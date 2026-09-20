using System.Xml;

namespace Lokad.Svg.Parsing;

/// <summary>Represents the admitted single-font text properties inherited by a rendered text descendant.</summary>
internal readonly record struct SvgInheritedTextStyle(string? Family, int Weight, bool Italic, double FontSize)
{
    internal static SvgInheritedTextStyle Default { get; } = new(null, 400, false, 16);
}

/// <summary>Combines inline, presentation, and ancestor text properties using the direct-text precedence subset.</summary>
internal static class SvgTextInheritance
{
    internal static SvgInheritedTextStyle Resolve(XmlReader reader, SvgInheritedTextStyle inherited, SvgTextInlineFontProperties inline)
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
        return new SvgInheritedTextStyle(family, weight, italic, fontSize);
    }
}
