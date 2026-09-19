namespace Lokad.Svg.Parsing;

/// <summary>Represents resolved inherited paint and visibility state for a root or container.</summary>
internal readonly record struct SvgResolvedContainerPresentation(SvgPaint Paint, bool IsDisplayed, bool IsVisible, byte Opacity);

/// <summary>Resolves the shared root/group presentation subset after the bounded cascade.</summary>
internal static class SvgContainerPresentationResolver
{
    internal static SvgResolvedContainerPresentation Resolve(SvgComputedStyle style, SvgPaint inheritedPaint, bool inheritedDisplayed, bool inheritedVisibility, IReadOnlyDictionary<string, SvgColor> namedColors)
    {
        if (style.FillRule is not null) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Fill rules are only implemented on shapes.");
        SvgShapePresentationResolver.ValidateStroke(style.Stroke);
        SvgPaint paint = SvgPaintResolver.Resolve(style.Fill, style.FillOpacity, style.Color, inheritedPaint, namedColors);
        bool isDisplayed = inheritedDisplayed && SvgShapePresentationResolver.ParseDisplay(style.Display);
        bool isVisible = SvgShapePresentationResolver.ParseVisibility(style.Visibility, inheritedVisibility);
        return new SvgResolvedContainerPresentation(paint, isDisplayed, isVisible, SvgColorParser.ParseOpacity(style.Opacity));
    }
}
