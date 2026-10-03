using Lokad.Svg.Rendering;

namespace Lokad.Svg.Parsing;

/// <summary>Represents the narrow presentation state permitted on a local use instance.</summary>
/// <summary>Carries one use-element solid stroke override with definition-inherited join mechanics.</summary>
internal readonly record struct SvgUseStrokeOverride(SvgPaint Paint, double Width, SvgStrokeJoin Join, double? MiterLimit);

internal readonly record struct SvgResolvedLocalReferencePresentation(byte Opacity, bool IsPainted, SvgPaint? FillOverride, string? PaintServerId, SvgUseStrokeOverride? StrokeOverride);

/// <summary>Owns validation and resolution of the supported local-use presentation subset.</summary>
internal static class SvgLocalReferencePresentationResolver
{
    internal static SvgResolvedLocalReferencePresentation Resolve(SvgComputedStyle style, SvgInheritedPresentation inheritedPresentation, IReadOnlyDictionary<string, SvgColor> namedColors, BudgetedParserStringStore strings)
    {
        if (style.Color is not null || style.FillOpacity is not null || style.FillRule is not null) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only inherited-rectangle fill styles on use elements are implemented.");
        bool isPainted = SvgPresentationResolver.IsPainted(style.Display, style.Visibility, inheritedPresentation.IsDisplayed, inheritedPresentation.IsVisible);
        byte opacity = SvgColorParser.ParseOpacity(style.Opacity);
        if (style.Fill is not null && SvgPatternSyntax.IsPatternFill(style.Fill)) return new SvgResolvedLocalReferencePresentation(opacity, isPainted, null, strings.Copy(SvgGradientSyntax.ParsePaintId(style.Fill)), ResolveStrokeOverride(style, inheritedPresentation, namedColors));
        SvgPaint? fillOverride = style.Fill is null ? null : SvgPaintResolver.Resolve(style.Fill, null, null, inheritedPresentation.Paint, namedColors);
        return new SvgResolvedLocalReferencePresentation(opacity, isPainted, fillOverride, null, ResolveStrokeOverride(style, inheritedPresentation, namedColors));

        static SvgUseStrokeOverride? ResolveStrokeOverride(SvgComputedStyle style, SvgInheritedPresentation inheritedPresentation, IReadOnlyDictionary<string, SvgColor> namedColors)
        {
            if (style.Stroke is null) return null;
            if (SvgStrokeStyleSyntax.IsZeroStrokeWidth(style.Width)) return null;
            SvgStrokeStyleSyntax.RejectPaintServerStroke(style.Stroke, "use");
            if (style.StrokeOpacity is not null) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Stroke opacity on use elements is not implemented.");
            if (style.Dasharray is not null && !style.Dasharray.AsSpan().Trim().Equals("none".AsSpan(), StringComparison.OrdinalIgnoreCase)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Dashed strokes on use elements are not implemented.");
            double width = SvgStrokeStyleSyntax.ParseStrokeWidth(style.Width, "use");
            if (width == 0) return null;
            SvgStrokeJoin join = style.Linejoin is null ? SvgStrokeJoin.Miter : SvgStrokeStyleSyntax.ParseLinejoin(style.Linejoin, false);
            double? miterLimit = style.Miterlimit is null ? null : SvgStrokeStyleSyntax.ParseMiterLimit(style.Miterlimit);
            SvgPaint paint = SvgPaintResolver.Resolve(style.Stroke, null, null, inheritedPresentation.Paint, namedColors);
            return new SvgUseStrokeOverride(paint, width, join, miterLimit);
        }
    }
}
