namespace Lokad.Svg.Parsing;

/// <summary>Represents the narrow presentation state permitted on a local use instance.</summary>
internal readonly record struct SvgResolvedLocalReferencePresentation(byte Opacity, bool IsPainted, SvgPaint? FillOverride, string? PaintServerId);

/// <summary>Owns validation and resolution of the supported local-use presentation subset.</summary>
internal static class SvgLocalReferencePresentationResolver
{
    internal static SvgResolvedLocalReferencePresentation Resolve(SvgComputedStyle style, SvgInheritedPresentation inheritedPresentation, IReadOnlyDictionary<string, SvgColor> namedColors, BudgetedParserStringStore strings)
    {
        if (style.Color is not null || style.FillOpacity is not null || style.Stroke is not null || style.FillRule is not null) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only inherited-rectangle fill styles on use elements are implemented.");
        bool isPainted = SvgPresentationResolver.IsPainted(style.Display, style.Visibility, inheritedPresentation.IsDisplayed, inheritedPresentation.IsVisible);
        byte opacity = SvgColorParser.ParseOpacity(style.Opacity);
        if (style.Fill is not null && SvgPatternSyntax.IsPatternFill(style.Fill)) return new SvgResolvedLocalReferencePresentation(opacity, isPainted, null, strings.Copy(SvgGradientSyntax.ParsePaintId(style.Fill)));
        SvgPaint? fillOverride = style.Fill is null ? null : SvgPaintResolver.Resolve(style.Fill, null, null, inheritedPresentation.Paint, namedColors);
        return new SvgResolvedLocalReferencePresentation(opacity, isPainted, fillOverride, null);
    }
}
