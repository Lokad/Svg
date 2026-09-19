using System.Xml;
namespace Lokad.Svg.Parsing;

/// <summary>Parses and lowers the currently supported paintable SVG leaves independently of XML traversal.</summary>
internal static class SvgLeafParser
{
    internal static SvgGeometry ParseRectangle(XmlReader reader, SvgPaintableElementContext context)
    {
        SvgComputedStyle style = context.ResolveStyle(reader);
        SvgRectangleSyntax syntax = SvgRectangleSyntax.Parse(reader);
        AxisTransform transform = context.ResolveTransform(reader);
        SvgResolvedShapePresentation presentation = context.ResolvePresentation(style, true);
        SvgRectangle rectangle = transform.Apply(new SvgRectangle(syntax.X, syntax.Y, syntax.Width, syntax.Height, presentation.Color, presentation.HasFill, null));
        SvgStroke? stroke = ParseRectangleStroke(presentation.Style.Stroke, syntax.StrokeWidth, presentation.Style.Color, context.InheritedPaint, transform, rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height, presentation.Opacity, rectangle.HasFill, context.NamedColors);
        return SvgGeometry.FromRectangle(rectangle with { Stroke = presentation.IsPainted ? stroke : null });
    }

    internal static SvgGeometry ParseLine(XmlReader reader, SvgPaintableElementContext context)
    {
        SvgComputedStyle style = context.ResolveStyle(reader);
        SvgLineSyntax syntax = SvgLineSyntax.Parse(reader);
        AxisTransform transform = context.ResolveTransform(reader);
        string? stroke = style.Stroke;
        byte opacity = SvgColorParser.ParseOpacity(style.Opacity);
        if (transform != AxisTransform.Identity || stroke is null || stroke.Equals("none", StringComparison.OrdinalIgnoreCase) || opacity != 255 || (syntax.X1 != syntax.X2 && syntax.Y1 != syntax.Y2) || (syntax.X1 == syntax.X2 && syntax.Y1 == syntax.Y2)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only opaque untransformed horizontal or vertical lines are implemented.");
        SvgColor strokeColor = SvgPaintResolver.ResolveStrokeColor(stroke, style.Color, context.InheritedPaint, context.NamedColors);
        int strokeWidth = SvgGeometrySyntax.ParseInteger(syntax.StrokeWidth ?? "1");
        if (strokeColor.Alpha != 255 || strokeWidth <= 0 || strokeWidth % 2 != 0) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only opaque even-width line strokes are implemented.");
        bool painted = context.InheritedDisplayed && SvgShapePresentationResolver.ParseDisplay(style.Display) && SvgShapePresentationResolver.ParseVisibility(style.Visibility, context.InheritedVisibility);
        SvgColor color = SvgPaintResolver.ApplyOpacity(strokeColor, opacity);
        return SvgGeometry.FromAxisAlignedLine(new SvgAxisAlignedLine(syntax.X1, syntax.Y1, syntax.X2, syntax.Y2, color, painted, strokeWidth));
    }

    private static SvgStroke? ParseRectangleStroke(string? value, string? widthValue, string? currentColor, SvgPaint inheritedPaint, AxisTransform transform, double x, double y, double width, double height, byte opacity, bool hasFill, IReadOnlyDictionary<string, SvgColor> namedColors)
    {
        if (value is null || value.Equals("none", StringComparison.OrdinalIgnoreCase)) { if (widthValue is not null) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Stroke width without a stroke is not implemented."); return null; }
        if (transform != AxisTransform.Identity || x != Math.Truncate(x) || y != Math.Truncate(y) || width != Math.Truncate(width) || height != Math.Truncate(height) || (hasFill && opacity != 255)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only untransformed integer rectangle strokes, with opacity only when fill-less, are implemented.");
        SvgColor color = SvgPaintResolver.ResolveStrokeColor(value, currentColor, inheritedPaint, namedColors);
        int strokeWidth = SvgGeometrySyntax.ParseInteger(widthValue ?? "1");
        if (color.Alpha != 255 || strokeWidth <= 0 || strokeWidth % 2 != 0 || strokeWidth > width || strokeWidth > height) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only opaque even-width rectangle strokes no larger than the rectangle dimensions are implemented.");
        return new SvgStroke(SvgPaintResolver.ApplyOpacity(color, opacity), strokeWidth);
    }
}
