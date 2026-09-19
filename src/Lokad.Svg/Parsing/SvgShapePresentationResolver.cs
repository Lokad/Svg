namespace Lokad.Svg.Parsing;

/// <summary>Represents the supported computed presentation values for a paintable leaf.</summary>
internal readonly record struct SvgResolvedShapePresentation(SvgComputedStyle Style, SvgPaint Paint, SvgColor Color, byte Opacity, bool IsPainted)
{
    internal bool HasFill => Paint.HasFill && IsPainted;
}

/// <summary>Resolves supported leaf paint, visibility, and validation after element syntax is accepted.</summary>
internal static class SvgShapePresentationResolver
{
    internal static SvgResolvedShapePresentation Resolve(SvgComputedStyle style, SvgPaint inheritedPaint, bool inheritedDisplayed, bool inheritedVisibility, bool allowsPaintedStroke, IReadOnlyDictionary<string, SvgColor> namedColors)
    {
        SvgPaint paint = SvgPaintResolver.Resolve(style.Fill, style.FillOpacity, style.Color, inheritedPaint, namedColors);
        ValidateFillRule(style.FillRule);
        if (!allowsPaintedStroke) ValidateStroke(style.Stroke);
        byte opacity = SvgColorParser.ParseOpacity(style.Opacity);
        SvgColor color = SvgPaintResolver.ResolveFillColor(paint, opacity);
        bool isPainted = inheritedDisplayed && ParseDisplay(style.Display) && ParseVisibility(style.Visibility, inheritedVisibility);
        return new SvgResolvedShapePresentation(style, paint, color, opacity, isPainted);
    }

    internal static bool ParseDisplay(string? value)
    {
        if (value is null || value.Equals("inline", StringComparison.OrdinalIgnoreCase)) return true;
        if (value.Equals("none", StringComparison.OrdinalIgnoreCase)) return false;
        throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only inline and none display values are implemented.");
    }

    internal static bool ParseVisibility(string? value, bool inheritedVisibility)
    {
        if (value is null) return inheritedVisibility;
        if (value.Equals("visible", StringComparison.OrdinalIgnoreCase)) return true;
        if (value.Equals("hidden", StringComparison.OrdinalIgnoreCase)) return false;
        throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only visible and hidden visibility values are implemented.");
    }

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
}
