namespace Lokad.Svg.Parsing;

/// <summary>Represents the narrow presentation state permitted on a local use instance.</summary>
internal readonly record struct SvgResolvedLocalReferencePresentation(byte Opacity, bool IsPainted, SvgPaint? FillOverride);

/// <summary>Owns validation and resolution of the supported local-use presentation subset.</summary>
internal static class SvgLocalReferencePresentationResolver
{
    internal static SvgResolvedLocalReferencePresentation Resolve(SvgComputedStyle style, SvgInheritedPresentation inheritedPresentation, IReadOnlyDictionary<string, SvgColor> namedColors)
    {
        if (style.Color is not null || style.FillOpacity is not null || style.Stroke is not null || style.FillRule is not null) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only inherited-rectangle fill styles on use elements are implemented.");
        SvgPaint? fillOverride = style.Fill is null ? null : SvgPaintResolver.Resolve(style.Fill, null, null, inheritedPresentation.Paint, namedColors);
        bool isPainted = SvgPresentationResolver.IsPainted(style.Display, style.Visibility, inheritedPresentation.IsDisplayed, inheritedPresentation.IsVisible);
        return new SvgResolvedLocalReferencePresentation(SvgColorParser.ParseOpacity(style.Opacity), isPainted, fillOverride);
    }
}
