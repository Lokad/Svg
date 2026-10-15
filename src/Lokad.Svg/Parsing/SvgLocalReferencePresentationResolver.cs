using Lokad.Svg.Rendering;

namespace Lokad.Svg.Parsing;

/// <summary>Represents the narrow presentation state permitted on a local use instance.</summary>
/// <summary>Carries one use-element solid stroke override with definition-inherited join mechanics.</summary>
internal readonly record struct SvgUseStrokeOverride(SvgPaint Paint, double Width, SvgStrokeJoin Join, double? MiterLimit);

internal readonly record struct SvgResolvedLocalReferencePresentation(byte Opacity, bool IsPainted, SvgPaint? FillOverride, string? PaintServerId, SvgUseStrokeOverride? StrokeOverride)
{
    internal bool AllowsSolidPaintAnalysis { get; init; }
    internal SvgPaint Paint { get; init; } = SvgPaint.Default;
    internal SvgPaintOpacity StrokeOpacity { get; init; } = SvgPaintOpacity.Opaque;
}

/// <summary>Owns validation and resolution of the supported local-use presentation subset.</summary>
internal static class SvgLocalReferencePresentationResolver
{
    internal static SvgResolvedLocalReferencePresentation Resolve(SvgComputedStyle style, SvgInheritedPresentation inheritedPresentation, IReadOnlyDictionary<string, SvgColor> namedColors, BudgetedParserStringStore strings)
    {
        SvgShapePresentationResolver.ValidateFillRule(style.FillRule);
        bool isPainted = SvgPresentationResolver.IsPainted(style.Display, style.Visibility, inheritedPresentation.IsDisplayed, inheritedPresentation.IsVisible);
        byte opacity = style.GetOpacity().Alpha;
        bool paintServer = style.Fill is not null && SvgPatternSyntax.IsPatternFill(style.Fill);
        SvgPaint paint = SvgPaintResolver.Resolve(paintServer ? "#ffffff" : style.Fill, style.FillOpacity, style.Color, inheritedPresentation.Paint, namedColors);
        SvgUseStrokeOverride? stroke = ResolveStrokeOverride(style, inheritedPresentation with { Paint = paint }, namedColors);
        SvgPaintOpacity strokeOpacity = SvgColorParser.ParsePaintOpacity(style.StrokeOpacity);
        if (paintServer) return new SvgResolvedLocalReferencePresentation(opacity, isPainted, null, strings.Copy(SvgGradientSyntax.ParsePaintId(style.Fill)), stroke) { Paint = paint, StrokeOpacity = strokeOpacity };
        SvgPaint? fillOverride = style.Fill is null ? null : paint;
        return new SvgResolvedLocalReferencePresentation(opacity, isPainted, fillOverride, null, stroke) { Paint = paint, StrokeOpacity = strokeOpacity, AllowsSolidPaintAnalysis = style.GetOpacity().Value == 1 && !SvgGradientSyntax.IsGradientStrokeUrl(style.Stroke) };

        static SvgUseStrokeOverride? ResolveStrokeOverride(SvgComputedStyle style, SvgInheritedPresentation inheritedPresentation, IReadOnlyDictionary<string, SvgColor> namedColors)
        {
            if (style.Stroke is null) return null;
            if (SvgStrokeStyleSyntax.IsZeroStrokeWidth(style.Width)) return null;
            // Shader strokes remain in the retained inherited style. Source
            // replay resolves their paint after the referenced leaf is known;
            // this legacy override represents solid colors only.
            if (SvgGradientSyntax.IsGradientStrokeUrl(style.Stroke))
            {
                _ = SvgGradientSyntax.ParsePaintId(style.Stroke);
                return null;
            }
            double width = SvgStrokeStyleSyntax.ParseStrokeWidth(style.Width, "use");
            if (width == 0) return null;
            SvgStrokeJoin join = style.Linejoin is null ? SvgStrokeJoin.Miter : SvgStrokeStyleSyntax.ParseLinejoin(style.Linejoin, true);
            double? miterLimit = style.Miterlimit is null ? null : SvgStrokeStyleSyntax.ParseMiterLimit(style.Miterlimit);
            SvgPaint paint = SvgPaintResolver.Resolve(style.Stroke, style.StrokeOpacity, null, inheritedPresentation.Paint with { FillOpacity = SvgPaintOpacity.Opaque }, namedColors);
            return new SvgUseStrokeOverride(paint, width, join, miterLimit);
        }
    }
}
