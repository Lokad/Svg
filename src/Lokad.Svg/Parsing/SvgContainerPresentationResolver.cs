namespace Lokad.Svg.Parsing;

/// <summary>Validates root/group-specific presentation constraints before shared presentation resolution.</summary>
internal static class SvgContainerPresentationResolver
{
    internal static SvgResolvedPresentation Resolve(SvgComputedStyle style, SvgPaint inheritedPaint, bool inheritedDisplayed, bool inheritedVisibility, IReadOnlyDictionary<string, SvgColor> namedColors)
    {
        SvgShapePresentationResolver.ValidateFillRule(style.FillRule);
        SvgPresentationResolver.ValidateStroke(style.Stroke);
        return SvgPresentationResolver.Resolve(style, inheritedPaint, inheritedDisplayed, inheritedVisibility, namedColors);
    }
}
