namespace Lokad.Svg.Parsing;

/// <summary>Retains effective solid text paint until normalized content identifies the span that paints it.</summary>
internal readonly record struct SvgTextPaintStyle(SvgPaint Paint, bool IsDisplayed, bool IsVisible, string? Stroke, byte TextOpacity)
{
    internal static SvgTextPaintStyle Default => new(SvgPaint.Default, true, true, null, 255);
    internal SvgTextGlyphPaint GlyphPaint => new(SvgPaintResolver.ResolveFillColor(Paint, TextOpacity), IsDisplayed && IsVisible && Paint.HasFill);

    internal static SvgTextPaintStyle ResolveElement(SvgComputedStyle style, SvgPaint inheritedPaint, bool inheritedDisplayed, bool inheritedVisible, string? inheritedStroke)
    {
        ValidateStyle(style);
        SvgResolvedPresentation resolved = SvgPresentationResolver.Resolve(style, inheritedPaint, inheritedDisplayed, inheritedVisible, SvgRootParser.CssNamedColors);
        return new(resolved.Paint, resolved.IsDisplayed, resolved.IsVisible, style.Stroke ?? inheritedStroke, resolved.Opacity);
    }

    internal static SvgTextPaintStyle ResolveSpan(SvgComputedStyle style, SvgTextPaintStyle inherited)
    {
        ValidateStyle(style);
        SvgResolvedPresentation resolved = SvgPresentationResolver.Resolve(style, inherited.Paint, inherited.IsDisplayed, inherited.IsVisible, SvgRootParser.CssNamedColors);
        return new(resolved.Paint, resolved.IsDisplayed, resolved.IsVisible, style.Stroke ?? inherited.Stroke, inherited.TextOpacity);
    }

    internal void ValidateEmission()
    {
        if (IsDisplayed && IsVisible && Stroke is not null && !Stroke.Equals("none", StringComparison.OrdinalIgnoreCase))
            throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only unpainted strokes are implemented for direct text.");
    }

    private static void ValidateStyle(SvgComputedStyle style)
    {
        SvgShapePresentationResolver.ValidateFillRule(style.FillRule);
        if (style.Filter is string filter && filter.Trim() != "none")
            throw new SvgException(SvgErrorCode.UnsupportedFeature, "Filters on retained definition content are not implemented.");
    }
}

/// <summary>Pairs a glyph's resolved fill color with whether its span paints a fill.</summary>
internal readonly record struct SvgTextGlyphPaint(SvgColor Color, bool HasFill);

/// <summary>Identifies a normalized UTF-16 interval with one effective glyph paint.</summary>
internal readonly record struct SvgTextPaintSpan(int Start, int End, SvgTextGlyphPaint Paint);
