using System.Xml;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Parsing;

/// <summary>Parses and lowers the currently supported paintable SVG leaves independently of XML traversal.</summary>
internal static class SvgLeafParser
{
    internal static SvgGeometry ParseRectangle(XmlReader reader, SvgPaintableElementContext context)
    {
        SvgComputedStyle style = context.ResolveStyle(reader);
        SvgElementAttributePolicy.ValidateRectangle(reader);
        double x = SvgGeometrySyntax.ParseRectangleCoordinate(reader.GetAttribute("x") ?? "0");
        double y = SvgGeometrySyntax.ParseRectangleCoordinate(reader.GetAttribute("y") ?? "0");
        double width = SvgGeometrySyntax.ParseRectangleCoordinate(reader.GetAttribute("width") ?? "0");
        double height = SvgGeometrySyntax.ParseRectangleCoordinate(reader.GetAttribute("height") ?? "0");
        AxisTransform transform = context.ResolveTransform(reader);
        ValidateSquareCorners(reader.GetAttribute("rx"));
        ValidateSquareCorners(reader.GetAttribute("ry"));
        if (width < 0 || height < 0) throw new SvgException(SvgErrorCode.MalformedInput, "Rectangle dimensions cannot be negative.");
        SvgResolvedShapePresentation presentation = context.ResolvePresentation(style, true);
        SvgRectangle rectangle = transform.Apply(new SvgRectangle(x, y, width, height, presentation.Color, presentation.HasFill, null));
        SvgStroke? stroke = ParseRectangleStroke(presentation.Style.Stroke, reader.GetAttribute("stroke-width"), presentation.Style.Color, context.InheritedPaint, transform, rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height, presentation.Opacity, rectangle.HasFill, context.NamedColors);
        return SvgGeometry.FromRectangle(rectangle with { Stroke = presentation.IsPainted ? stroke : null });
    }

    internal static SvgGeometry ParseLine(XmlReader reader, SvgPaintableElementContext context)
    {
        SvgComputedStyle style = context.ResolveStyle(reader);
        SvgElementAttributePolicy.ValidateLine(reader);
        AxisTransform transform = context.ResolveTransform(reader);
        int x1 = SvgGeometrySyntax.ParseInteger(reader.GetAttribute("x1") ?? "0"); int y1 = SvgGeometrySyntax.ParseInteger(reader.GetAttribute("y1") ?? "0"); int x2 = SvgGeometrySyntax.ParseInteger(reader.GetAttribute("x2") ?? "0"); int y2 = SvgGeometrySyntax.ParseInteger(reader.GetAttribute("y2") ?? "0");
        string? stroke = style.Stroke;
        byte opacity = SvgColorParser.ParseOpacity(style.Opacity);
        if (transform != AxisTransform.Identity || stroke is null || stroke.Equals("none", StringComparison.OrdinalIgnoreCase) || opacity != 255 || (x1 != x2 && y1 != y2) || (x1 == x2 && y1 == y2)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only opaque untransformed horizontal or vertical lines are implemented.");
        SvgColor strokeColor = SvgPaintResolver.ResolveStrokeColor(stroke, style.Color, context.InheritedPaint, context.NamedColors);
        int strokeWidth = SvgGeometrySyntax.ParseInteger(reader.GetAttribute("stroke-width") ?? "1");
        if (strokeColor.Alpha != 255 || strokeWidth <= 0 || strokeWidth % 2 != 0) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only opaque even-width line strokes are implemented.");
        double halfWidth = strokeWidth / 2d;
        bool painted = context.InheritedDisplayed && SvgShapePresentationResolver.ParseDisplay(style.Display) && SvgShapePresentationResolver.ParseVisibility(style.Visibility, context.InheritedVisibility);
        SvgColor color = SvgPaintResolver.ApplyOpacity(strokeColor, opacity);
        return SvgGeometry.FromRectangle(x1 != x2 ? new SvgRectangle(Math.Min(x1, x2), y1 - halfWidth, Math.Abs((long)x2 - x1), strokeWidth, color, painted, null) : new SvgRectangle(x1 - halfWidth, Math.Min(y1, y2), strokeWidth, Math.Abs((long)y2 - y1), color, painted, null));
    }

    internal static SvgParsedGeometry ParseOrthogonalPath(XmlReader reader, SvgPaintableElementContext context, int maximumGeometryElements, RenderBudget budget)
    {
        SvgElementAttributePolicy.ValidatePath(reader);
        string d = reader.GetAttribute("d") ?? throw new SvgException(SvgErrorCode.MalformedInput, "Path data is required.");
        using BudgetedParserList<SvgPoint> points = SvgOrthogonalGeometryTokenizer.ParsePath(d, maximumGeometryElements, budget, context.WorkCounter, context.CancellationToken);
        SvgOrthogonalGeometryLowering.ValidateSimpleContour(points, "path", context.WorkCounter, context.CancellationToken);
        SvgComputedStyle style = context.ResolveStyle(reader); AxisTransform transform = context.ResolveTransform(reader);
        SvgResolvedShapePresentation presentation = context.ResolvePresentation(style, false);
        using SvgOrthogonalGeometryLowering lowering = new(points, "path", true, false, maximumGeometryElements, budget, context.WorkCounter, context.CancellationToken);
        return lowering.Lower(presentation.Color, presentation.HasFill, transform);
    }

    internal static SvgParsedGeometry ParseOrthogonalPointList(XmlReader reader, string elementName, SvgPaintableElementContext context, int maximumGeometryElements, RenderBudget budget)
    {
        SvgElementAttributePolicy.ValidatePointList(reader, elementName);
        string pointsValue = reader.GetAttribute("points") ?? throw new SvgException(SvgErrorCode.MalformedInput, elementName + " points are required.");
        using BudgetedParserList<SvgPoint> points = SvgOrthogonalGeometryTokenizer.ParsePointList(pointsValue, elementName, maximumGeometryElements, budget, context.WorkCounter, context.CancellationToken);
        using SvgOrthogonalGeometryLowering lowering = new(points, elementName, false, true, maximumGeometryElements, budget, context.WorkCounter, context.CancellationToken);
        SvgComputedStyle style = context.ResolveStyle(reader); AxisTransform transform = context.ResolveTransform(reader);
        SvgResolvedShapePresentation presentation = context.ResolvePresentation(style, false);
        return lowering.Lower(presentation.Color, presentation.HasFill, transform);
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

    private static void ValidateSquareCorners(string? value)
    {
        if (value is null || SvgGeometrySyntax.ParseRectangleCoordinate(value) == 0) return;
        throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only zero rectangle corner radii are implemented.");
    }
}
