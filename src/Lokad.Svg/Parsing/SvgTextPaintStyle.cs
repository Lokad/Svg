namespace Lokad.Svg.Parsing;

using Lokad.Svg.Rendering;

/// <summary>Retains effective solid text paint until normalized content identifies the span that paints it.</summary>
internal readonly record struct SvgTextPaintStyle(SvgPaint Paint, bool IsDisplayed, bool IsVisible, string? Stroke, byte TextOpacity)
{
    internal static SvgTextPaintStyle Default => new(SvgPaint.Default, true, true, null, 255);
    internal SvgInheritedStrokeStyle StrokeStyle { get; init; }
    internal SvgPaintOrder PaintOrder { get; init; }
    internal bool NonScalingStroke { get; init; }
    internal SvgTextDecorationStyle? Decoration { get; init; }
    internal SvgTextGlyphPaint GlyphPaint => new(SvgPaintResolver.ResolveFillColor(Paint, TextOpacity), IsDisplayed && IsVisible && Paint.HasFill)
    {
        Decoration = IsDisplayed && IsVisible ? Decoration : null,
        Stroke = IsDisplayed && IsVisible && Stroke is not null && !Stroke.Equals("none", StringComparison.OrdinalIgnoreCase)
            ? new(StrokeStyle with { Stroke = Stroke }, Paint, TextOpacity) { PaintOrder = PaintOrder, NonScaling = NonScalingStroke } : null
    };

    internal static SvgTextPaintStyle ResolveElement(SvgComputedStyle style, SvgPaint inheritedPaint, bool inheritedDisplayed, bool inheritedVisible, string? inheritedStroke)
    {
        ValidateStyle(style);
        SvgResolvedPresentation resolved = SvgPresentationResolver.Resolve(style, inheritedPaint, inheritedDisplayed, inheritedVisible, SvgRootParser.CssNamedColors);
        return new(resolved.Paint, resolved.IsDisplayed, resolved.IsVisible, style.Stroke ?? inheritedStroke, resolved.Opacity)
        { StrokeStyle = SvgInheritedStrokeStyle.Default.ResolveChild(style), PaintOrder = style.PaintOrder ?? SvgPaintOrder.FillStrokeMarkers, NonScalingStroke = SvgStrokeStyleSyntax.ParseVectorEffect(style.VectorEffect) };
    }

    internal static SvgTextPaintStyle ResolveSpan(SvgComputedStyle style, SvgTextPaintStyle inherited)
    {
        ValidateStyle(style);
        SvgResolvedPresentation resolved = SvgPresentationResolver.Resolve(style, inherited.Paint, inherited.IsDisplayed, inherited.IsVisible, SvgRootParser.CssNamedColors);
        return new(resolved.Paint, resolved.IsDisplayed, resolved.IsVisible, style.Stroke ?? inherited.Stroke, inherited.TextOpacity)
        { StrokeStyle = inherited.StrokeStyle.ResolveChild(style), PaintOrder = style.PaintOrder ?? inherited.PaintOrder, NonScalingStroke = SvgStrokeStyleSyntax.ParseVectorEffect(style.VectorEffect) };
    }

    internal void ValidateEmission()
    {
        _ = GlyphPaint.Stroke?.Resolve();
        if (Decoration?.Paint.HasStroke == true) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Stroked text decorations are not implemented.");
    }

    private static void ValidateStyle(SvgComputedStyle style)
    {
        SvgShapePresentationResolver.ValidateFillRule(style.FillRule);
        if (style.Filter is string filter && filter.Trim() != "none")
            throw new SvgException(SvgErrorCode.UnsupportedFeature, "Filters on retained definition content are not implemented.");
    }
}

/// <summary>Pairs a glyph's resolved fill color with whether its span paints a fill.</summary>
internal readonly record struct SvgTextGlyphPaint(SvgColor Color, bool HasFill)
{
    internal SvgTextDecorationStyle? Decoration { get; init; }
    internal SvgTextStrokePaint? Stroke { get; init; }
    internal bool HasStroke => Stroke?.Resolve() is {};
    internal bool IsPainted => HasFill || HasStroke || Decoration?.Paint.IsPainted == true;
}

/// <summary>Borrows immutable inherited stroke strings until actual emitted text selects them; no storage or host font access is owned.</summary>
internal readonly record struct SvgTextStrokePaint(SvgInheritedStrokeStyle Style, SvgPaint Paint, byte Opacity)
{
    internal SvgPaintOrder PaintOrder { get; init; }
    internal bool NonScaling { get; init; }
    internal SvgStroke? Resolve()
    {
        double width = SvgStrokeStyleSyntax.ParseStrokeWidth(Style.Width, "text");
        if (width == 0) return null;
        if (NonScaling) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Non-scaling direct text strokes are not implemented.");
        if (PaintOrder is not SvgPaintOrder.FillStrokeMarkers and not SvgPaintOrder.FillMarkersStroke and not SvgPaintOrder.MarkersFillStroke)
            throw new SvgException(SvgErrorCode.UnsupportedFeature, "Stroke-before-fill direct text paint is not implemented.");
        SvgStrokeStyleSyntax.RejectPaintServerStroke(Style.Stroke, "text");
        _ = SvgStrokeStyleSyntax.ParseLinecap(Style.Linecap);
        _ = SvgStrokeStyleSyntax.ParseLinejoin(Style.Linejoin, true);
        _ = SvgStrokeStyleSyntax.ParseMiterLimit(Style.Miterlimit);
        if (Style.Dasharray is string dash && !dash.Trim().Equals("none", StringComparison.OrdinalIgnoreCase))
            throw new SvgException(SvgErrorCode.UnsupportedFeature, "Dashed direct text strokes are not implemented.");
        SvgColor color = SvgPaintResolver.ResolveStrokeColor(Style.Stroke ?? "none", null, Paint, SvgRootParser.CssNamedColors);
        color = SvgPaintResolver.ApplyOpacity(color, Style.StrokeOpacity is null ? (byte)255 : SvgColorParser.ParseOpacity(Style.StrokeOpacity));
        return new(SvgPaintResolver.ApplyOpacity(color, Opacity), width);
    }
}

/// <summary>Identifies a normalized UTF-16 interval with one effective glyph paint.</summary>
internal readonly record struct SvgTextPaintSpan(int Start, int End, SvgTextGlyphPaint Paint);
