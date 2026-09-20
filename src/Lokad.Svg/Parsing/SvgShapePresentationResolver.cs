namespace Lokad.Svg.Parsing;

/// <summary>Represents the supported computed presentation values for a paintable leaf.</summary>
internal readonly record struct SvgResolvedShapePresentation(SvgComputedStyle Style, SvgPaint Paint, SvgColor Color, SvgColor? StrokeColor, byte Opacity, bool IsPainted)
{
    internal bool HasFill => Paint.HasFill && IsPainted;
}

/// <summary>Resolves supported leaf paint, visibility, and validation after element syntax is accepted.</summary>
internal static class SvgShapePresentationResolver
{
    internal static SvgResolvedShapePresentation Resolve(SvgComputedStyle style, SvgPaint inheritedPaint, bool inheritedDisplayed, bool inheritedVisibility, bool allowsPaintedStroke, IReadOnlyDictionary<string, SvgColor> namedColors)
    {
        SvgResolvedPresentation presentation = SvgPresentationResolver.Resolve(style, inheritedPaint, inheritedDisplayed, inheritedVisibility, namedColors);
        ValidateFillRule(style.FillRule);
        if (!allowsPaintedStroke) ValidateStroke(style.Stroke);
        SvgColor color = SvgPaintResolver.ResolveFillColor(presentation.Paint, presentation.Opacity);
        SvgColor? strokeColor = allowsPaintedStroke ? ResolveStrokeColor(style.Stroke, style.Color, inheritedPaint, namedColors) : null;
        return new SvgResolvedShapePresentation(style, presentation.Paint, color, strokeColor, presentation.Opacity, presentation.IsPainted);
    }

    internal static bool IsPainted(string? display, string? visibility, bool inheritedDisplayed, bool inheritedVisibility)
        => SvgPresentationResolver.IsPainted(display, visibility, inheritedDisplayed, inheritedVisibility);

    internal static void ValidateFillRule(string? value)
    {
        if (value is null || value.Equals("nonzero", StringComparison.OrdinalIgnoreCase) || value.Equals("evenodd", StringComparison.OrdinalIgnoreCase)) return;
        throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only nonzero and evenodd fill rules are implemented.");
    }

    internal static void ValidateStroke(string? value)
    {
        if (value is null || value.Equals("none", StringComparison.OrdinalIgnoreCase)) return;
        throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only non-painting stroke='none' is implemented.");
    }

    private static SvgColor? ResolveStrokeColor(string? value, string? currentColor, SvgPaint inheritedPaint, IReadOnlyDictionary<string, SvgColor> namedColors)
        => value is null || value.Equals("none", StringComparison.OrdinalIgnoreCase) ? null : SvgPaintResolver.ResolveStrokeColor(value, currentColor, inheritedPaint, namedColors);
}
