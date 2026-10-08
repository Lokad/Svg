namespace Lokad.Svg.Parsing;

/// <summary>Validates root/group-specific presentation constraints before shared presentation resolution.</summary>
internal static class SvgContainerPresentationResolver
{
    internal static SvgResolvedPresentation Resolve(SvgComputedStyle style, SvgPaint inheritedPaint, string? inheritedPaintServerFill, bool inheritedDisplayed, bool inheritedVisibility, IReadOnlyDictionary<string, SvgColor> namedColors)
    {
        SvgShapePresentationResolver.ValidateFillRule(style.FillRule);
        bool shader = SvgPatternSyntax.IsPatternFill(style.Fill);
        SvgComputedStyle numeric = shader ? style with { Fill = "#ffffff" } : style;
        return SvgPresentationResolver.Resolve(numeric, inheritedPaint, inheritedDisplayed, inheritedVisibility, namedColors)
            with { PaintServerFill = style.Fill is null ? inheritedPaintServerFill : shader ? style.Fill : null };
    }
}
