namespace Lokad.Svg.Parsing;

/// <summary>Resolves the common inherited paint, display, visibility and opacity state before element-specific validation.</summary>
internal readonly record struct SvgResolvedPresentation(SvgPaint Paint, bool IsDisplayed, bool IsVisible, byte Opacity)
{
    internal bool IsPainted => IsDisplayed && IsVisible;
}

/// <summary>Owns presentation properties shared by container and paintable-shape resolution.</summary>
internal static class SvgPresentationResolver
{
    internal static SvgResolvedPresentation Resolve(SvgComputedStyle style, SvgPaint inheritedPaint, bool inheritedDisplayed, bool inheritedVisibility, IReadOnlyDictionary<string, SvgColor> namedColors)
    {
        SvgPaint paint = SvgPaintResolver.Resolve(style.Fill, style.FillOpacity, style.Color, inheritedPaint, namedColors);
        bool isDisplayed = inheritedDisplayed && ParseDisplay(style.Display);
        bool isVisible = ParseVisibility(style.Visibility, inheritedVisibility);
        return new SvgResolvedPresentation(paint, isDisplayed, isVisible, SvgColorParser.ParseOpacity(style.Opacity));
    }

    internal static bool IsPainted(string? display, string? visibility, bool inheritedDisplayed, bool inheritedVisibility)
        => inheritedDisplayed && ParseDisplay(display) && ParseVisibility(visibility, inheritedVisibility);

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
}
