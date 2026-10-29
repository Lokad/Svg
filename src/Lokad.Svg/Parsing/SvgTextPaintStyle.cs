namespace Lokad.Svg.Parsing;

using Lokad.Svg.Rendering;

/// <summary>Retains effective text paint and local fill binding until normalized content identifies the span that paints it.</summary>
internal readonly record struct SvgTextPaintStyle(SvgPaint Paint, bool IsDisplayed, bool IsVisible, string? Stroke, SvgPaintOpacity TextOpacity)
{
    internal static SvgTextPaintStyle Default => new(SvgPaint.Default, true, true, null, SvgPaintOpacity.Opaque);
    internal SvgDisplay ElementDisplay { get; init; }
    private SvgPaintResourceSelection? Resources { get; init; }
    private bool InactiveFill { get; init; }
    private bool InactiveStroke { get; init; }
    private bool SuppressFilter { get; init; }

    /// <summary>Borrows preparation metadata through extraction; emitted glyph paint retains no index or owner.</summary>
    internal SvgTextPaintStyle ResolveResources(SvgPaintResourceSelection? resources, bool suppressFilter)
        => this with
        {
            Resources = resources, SuppressFilter = suppressFilter,
            InactiveFill = resources?.IsInactivePaint(FillServer) == true,
            InactiveStroke = resources?.IsInactivePaint(Stroke) == true
        };
    internal string? FillServer { get; init; }
    internal SvgInheritedStrokeStyle StrokeStyle { get; init; }
    internal SvgPaintOrder PaintOrder { get; init; }
    internal bool NonScalingStroke { get; init; }
    internal SvgTextDecorationStyle? Decoration { get; init; }
    private SvgTextGlyphPaint AuthoredGlyphPaint => new(new(SvgPaintResolver.ResolveFillColor(Paint, TextOpacity.Alpha), SvgPaintResolver.ResolveSolidPaint(Paint).Alpha * TextOpacity.Value), IsDisplayed && IsVisible && Paint.HasFill)
    {
        FillServer = IsDisplayed && IsVisible && Paint.HasFill ? FillServer : null,
        Decoration = IsDisplayed && IsVisible ? Decoration : null,
        Stroke = IsDisplayed && IsVisible && Stroke is not null && !Stroke.Equals("none", StringComparison.OrdinalIgnoreCase)
            ? new(StrokeStyle with { Stroke = Stroke }, Paint, TextOpacity) { PaintOrder = PaintOrder, NonScaling = NonScalingStroke } : null
    };

    internal SvgTextGlyphPaint GlyphPaint
    {
        get
        {
            SvgTextGlyphPaint paint = AuthoredGlyphPaint;
            return paint with
            {
                HasFill = paint.HasFill && !InactiveFill && !SuppressFilter,
                FillServer = InactiveFill || SuppressFilter ? null : paint.FillServer,
                Stroke = InactiveStroke || SuppressFilter ? null : paint.Stroke,
                Decoration = SuppressFilter ? null : paint.Decoration
            };
        }
    }

    internal static SvgTextPaintStyle ResolveElement(SvgComputedStyle style, SvgPaint inheritedPaint, bool inheritedDisplayed, bool inheritedVisible, string? inheritedStroke)
    {
        ValidateStyle(style);
        SvgResolvedPresentation resolved = SvgPresentationResolver.Resolve(ForColorResolution(style), inheritedPaint, inheritedDisplayed, inheritedVisible, SvgRootParser.CssNamedColors);
        return new(resolved.Paint, resolved.IsDisplayed, resolved.IsVisible, style.Stroke ?? inheritedStroke, style.GetOpacity())
        { ElementDisplay = style.GetDisplay(), FillServer = IsLocalFill(style.Fill) ? style.Fill : null, StrokeStyle = SvgInheritedStrokeStyle.Default.ResolveChild(style), PaintOrder = style.PaintOrder ?? SvgPaintOrder.FillStrokeMarkers, NonScalingStroke = SvgStrokeStyleSyntax.ParseVectorEffect(style.VectorEffect) };
    }

    internal static SvgTextPaintStyle ResolveSpan(SvgComputedStyle style, SvgTextPaintStyle inherited)
    {
        style = style.ResolveDisplay(inherited.ElementDisplay);
        ValidateStyle(style);
        SvgResolvedPresentation resolved = SvgPresentationResolver.Resolve(ForColorResolution(style), inherited.Paint, inherited.IsDisplayed, inherited.IsVisible, SvgRootParser.CssNamedColors);
        SvgTextPaintStyle result = new(resolved.Paint, resolved.IsDisplayed, resolved.IsVisible, style.Stroke ?? inherited.Stroke, inherited.TextOpacity)
        { ElementDisplay = style.GetDisplay(), FillServer = style.Fill is null ? inherited.FillServer : IsLocalFill(style.Fill) ? style.Fill : null, StrokeStyle = inherited.StrokeStyle.ResolveChild(style), PaintOrder = style.PaintOrder ?? inherited.PaintOrder, NonScalingStroke = SvgStrokeStyleSyntax.ParseVectorEffect(style.VectorEffect) };
        return result.ResolveResources(inherited.Resources, inherited.SuppressFilter);
    }

    internal void ValidateEmission()
    {
        SvgStroke? validatedStroke = AuthoredGlyphPaint.Stroke?.Resolve();
        if (SuppressFilter || InactiveFill || InactiveStroke)
        {
            Resources?.ValidateTextPaint(AuthoredGlyphPaint.FillServer);
            if (validatedStroke is not null) Resources?.ValidateTextPaint(Stroke);
        }
        if (AuthoredGlyphPaint.Decoration?.Paint.FillServer is not null)
            throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only solid text decoration fills are implemented.");
        if (AuthoredGlyphPaint.Decoration?.Paint.Stroke is {} decoration && decoration.Resolve() is {})
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
internal readonly record struct SvgTextGlyphPaint(SvgSolidPaint FillPaint, bool HasFill)
{
    internal SvgColor Color => FillPaint.Color;
    internal string? FillServer { get; init; }
    internal SvgTextDecorationStyle? Decoration { get; init; }
    internal SvgTextStrokePaint? Stroke { get; init; }
    internal bool HasStroke => Stroke?.Resolve() is {};
    internal bool IsPainted => HasFill || HasStroke || Decoration?.Paint.IsPainted == true;
}

/// <summary>Borrows immutable inherited stroke strings until actual emitted text selects them; no storage or host font access is owned.</summary>
internal readonly record struct SvgTextStrokePaint(SvgInheritedStrokeStyle Style, SvgPaint Paint, SvgPaintOpacity Opacity)
{
    internal SvgPaintOrder PaintOrder { get; init; }
    internal bool NonScaling { get; init; }
    /// <summary>Borrows the element's native screen-font scale, excluding character rotations and textLength stretching.</summary>
    internal double FontScale { get; init; } = 1;
    internal SvgSolidPaint ResolvePaint()
    {
        SvgSolidPaint source = Style.Stroke is {} value && SvgLocalUrl.TryParseId(value, out _)
            ? new(new(255, 255, 255, 255), 1)
            : SvgPaintResolver.ResolveStrokePaint(Style.Stroke ?? "none", null, Paint, SvgRootParser.CssNamedColors);
        return source with { Alpha = source.Alpha * SvgColorParser.ParsePaintOpacity(Style.StrokeOpacity).Value * Opacity.Value };
    }

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
        return new(SvgPaintResolver.ApplyOpacity(color, Opacity.Alpha), width);
    }
}

/// <summary>Identifies a normalized UTF-16 interval with one effective glyph paint.</summary>
internal readonly record struct SvgTextPaintSpan(int Start, int End, SvgTextGlyphPaint Paint);

/// <summary>Preserves text metrics for an enclosing active filter even when inner paint is unavailable.</summary>
/// <remarks>Value-only traversal state, reset at resource boundaries. Enter borrows preparation metadata,
/// observes cancellation during resource lookup, and owns no storage. An outer unavailable
/// filter suppresses inner filters too; an active outer filter still needs descendant source bounds.</remarks>
internal readonly record struct SvgTextMetricScope(bool Suppressed, bool NeedsBounds)
{
    internal bool CanSuppress => Suppressed && !NeedsBounds;
    internal SvgTextMetricScope Enter(string? filter, SvgPaintResourceSelection? resources)
    {
        if (SvgLocalUrl.GetReference(filter) is null) return this;
        bool inactive = resources?.IsInactiveFilter(filter) == true;
        return new(Suppressed || inactive, NeedsBounds || !Suppressed && !inactive);
    }
}
