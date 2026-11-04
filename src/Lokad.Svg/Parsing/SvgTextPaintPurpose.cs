using System.Xml;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Parsing;

internal enum SvgTextPaintPurpose { Paint, ClipMask }

/// <summary>Resolves the paint operation independently of the retained text and its layout.</summary>
internal static class SvgTextPaintPurposeSyntax
{
    internal static SvgComputedStyle Resolve(SvgComputedStyle style, SvgTextPaintPurpose purpose)
        => purpose == SvgTextPaintPurpose.Paint ? style : style with
        {
            Fill = "black", FillOpacity = null, Color = "black", Stroke = "none",
            StrokeOpacity = null, Filter = null
        };

    /// <summary>Borrows lexical typography for a clip resource without selecting its ignored paints.</summary>
    internal static SvgInheritedContainer ResolveClipInheritance(XmlReader reader, SvgInheritedContainer inherited,
        InlineStyle sheet, bool definitionsDisplayed, CancellationToken token)
    {
        InlineStyle inline = SvgInlineStyleParser.Read(reader, token, true);
        SvgComputedStyle style = SvgComputedStyle.Resolve(reader, inline, sheet)
            .ResolveDisplay(inherited.Presentation.ElementDisplay).ResolveOpacity(inherited.Presentation.ElementOpacity);
        SvgInheritedTextStyle font = SvgTextInheritance.Resolve(reader, inherited.TextStyle, inline, sheet);
        font = font.WithDecoration(SvgTextDecorationSyntax.Resolve(reader, inline, sheet, inherited.TextStyle.Decoration,
            SvgTextPaintStyle.Default.GlyphPaint));
        return new(new(SvgPaint.Default, definitionsDisplayed && style.GetDisplay() != SvgDisplay.None,
            SvgPresentationResolver.ParseVisibility(style.Visibility, inherited.Presentation.IsVisible), SvgAffineTransform.Identity)
            { OwnOpacity = style.GetOpacity(), ElementDisplay = style.GetDisplay() }, font,
            inherited.FillRule, 255, SvgInheritedStrokeStyle.Default);
    }
}
