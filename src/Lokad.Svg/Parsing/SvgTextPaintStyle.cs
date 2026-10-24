namespace Lokad.Svg.Parsing;

using Lokad.Svg.Rendering;

/// <summary>Retains effective text paint and local fill binding until normalized content identifies the span that paints it.</summary>
internal readonly record struct SvgTextPaintStyle(SvgPaint Paint, bool IsDisplayed, bool IsVisible, string? Stroke, byte TextOpacity)
{
    internal static SvgTextPaintStyle Default => new(SvgPaint.Default, true, true, null, 255);
    internal SvgDisplay ElementDisplay { get; init; }
    internal string? FillServer { get; init; }
    internal SvgInheritedStrokeStyle StrokeStyle { get; init; }
    internal SvgPaintOrder PaintOrder { get; init; }
    internal bool NonScalingStroke { get; init; }
    internal SvgTextDecorationStyle? Decoration { get; init; }
    internal SvgTextGlyphPaint GlyphPaint => new(SvgPaintResolver.ResolveFillColor(Paint, TextOpacity), IsDisplayed && IsVisible && Paint.HasFill)
    {
        FillServer = IsDisplayed && IsVisible && Paint.HasFill ? FillServer : null,
        Decoration = IsDisplayed && IsVisible ? Decoration : null,
        Stroke = IsDisplayed && IsVisible && Stroke is not null && !Stroke.Equals("none", StringComparison.OrdinalIgnoreCase)
            ? new(StrokeStyle with { Stroke = Stroke }, Paint, TextOpacity) { PaintOrder = PaintOrder, NonScaling = NonScalingStroke } : null
    };

    internal static SvgTextPaintStyle ResolveElement(SvgComputedStyle style, SvgPaint inheritedPaint, bool inheritedDisplayed, bool inheritedVisible, string? inheritedStroke)
    {
        ValidateStyle(style);
        SvgResolvedPresentation resolved = SvgPresentationResolver.Resolve(ForColorResolution(style), inheritedPaint, inheritedDisplayed, inheritedVisible, SvgRootParser.CssNamedColors);
        return new(resolved.Paint, resolved.IsDisplayed, resolved.IsVisible, style.Stroke ?? inheritedStroke, resolved.Opacity)
        { ElementDisplay = style.GetDisplay(), FillServer = IsLocalFill(style.Fill) ? style.Fill : null, StrokeStyle = SvgInheritedStrokeStyle.Default.ResolveChild(style), PaintOrder = style.PaintOrder ?? SvgPaintOrder.FillStrokeMarkers, NonScalingStroke = SvgStrokeStyleSyntax.ParseVectorEffect(style.VectorEffect) };
    }

    internal static SvgTextPaintStyle ResolveSpan(SvgComputedStyle style, SvgTextPaintStyle inherited)
    {
        style = style.ResolveDisplay(inherited.ElementDisplay);
        ValidateStyle(style);
        SvgResolvedPresentation resolved = SvgPresentationResolver.Resolve(ForColorResolution(style), inherited.Paint, inherited.IsDisplayed, inherited.IsVisible, SvgRootParser.CssNamedColors);
        return new(resolved.Paint, resolved.IsDisplayed, resolved.IsVisible, style.Stroke ?? inherited.Stroke, inherited.TextOpacity)
        { ElementDisplay = style.GetDisplay(), FillServer = style.Fill is null ? inherited.FillServer : IsLocalFill(style.Fill) ? style.Fill : null, StrokeStyle = inherited.StrokeStyle.ResolveChild(style), PaintOrder = style.PaintOrder ?? inherited.PaintOrder, NonScalingStroke = SvgStrokeStyleSyntax.ParseVectorEffect(style.VectorEffect) };
    }

    internal void ValidateEmission()
    {
        _ = GlyphPaint.Stroke?.Resolve();
        if (Decoration?.Paint.FillServer is not null)
            throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only solid text decoration fills are implemented.");
        if (Decoration?.Paint.Stroke is {} decoration && decoration.Resolve() is {})
            SvgStrokeStyleSyntax.RejectPaintServerStroke(decoration.Style.Stroke, "text decorations");
    }

    /// <summary>Uses opaque white only for color/opacity inheritance; the original local URL stays on the glyph paint.</summary>
    internal static SvgComputedStyle ForColorResolution(SvgComputedStyle style)
        => IsLocalFill(style.Fill) ? style with { Fill = "white" } : style;

    internal static bool IsLocalFill(string? fill) => fill is not null && SvgLocalUrl.TryParseId(fill, out _);

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
    internal string? FillServer { get; init; }
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
    /// <summary>Borrows the element's native screen-font scale, excluding character rotations and textLength stretching.</summary>
    internal double FontScale { get; init; } = 1;
    internal SvgStroke? Resolve()
    {
        double width = SvgStrokeStyleSyntax.ParseStrokeWidth(Style.Width, "text");
        if (width == 0) return null;
        _ = SvgStrokeStyleSyntax.ParseLinecap(Style.Linecap);
        _ = SvgStrokeStyleSyntax.ParseLinejoin(Style.Linejoin, true);
        _ = SvgStrokeStyleSyntax.ParseMiterLimit(Style.Miterlimit);
        SvgStrokeStyleSyntax.ValidateDasharray(Style.Dasharray, true);
        _ = SvgStrokeStyleSyntax.ParseDashoffset(Style.Dashoffset);
        // A shader supplies color at replay. White retains the independent
        // stroke/element opacity while the shared outline uses only the width.
        SvgColor color = Style.Stroke is {} value && SvgLocalUrl.TryParseId(value, out _)
            ? new SvgColor(255, 255, 255, 255)
            : SvgPaintResolver.ResolveStrokeColor(Style.Stroke ?? "none", null, Paint, SvgRootParser.CssNamedColors);
        color = SvgPaintResolver.ApplyOpacity(color, Style.StrokeOpacity is null ? (byte)255 : SvgColorParser.ParseOpacity(Style.StrokeOpacity));
        return new(SvgPaintResolver.ApplyOpacity(color, Opacity), width);
    }
}

/// <summary>Identifies a normalized UTF-16 interval with one effective glyph paint.</summary>
internal readonly record struct SvgTextPaintSpan(int Start, int End, SvgTextGlyphPaint Paint);
