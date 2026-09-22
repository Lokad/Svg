using System.Xml;
using Lokad.Svg.Encoding;
using Lokad.Svg.Rendering;
namespace Lokad.Svg.Parsing;

/// <summary>Parses and lowers the currently supported paintable SVG leaves independently of XML traversal.</summary>
internal static class SvgLeafParser
{
    internal static SvgGeometry ParseRectangle(XmlReader reader, SvgPaintableElementContext context, SvgViewportFrame? frame, SvgDocumentState state)
    {
        SvgRectangleSyntax syntax = SvgRectangleSyntax.Parse(reader);
        SvgComputedStyle patternStyle = context.ResolveStyle(reader);
        if (SvgPatternSyntax.IsPatternFill(patternStyle.Fill)) return SvgPatternSyntax.LowerPatternRect(reader, context, patternStyle, state, frame);
        SvgResolvedPaintableElement element = context.ResolveShape(reader, true);
        SvgRectangle rectangle = element.Transform.Apply(new SvgRectangle(syntax.X, syntax.Y, syntax.Width, syntax.Height, element.Presentation.Color, element.Presentation.HasFill, null));
        SvgStroke? stroke = ParseRectangleStroke(element.Presentation.StrokeColor, syntax.StrokeWidth, element.Transform, rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height, element.Presentation.Opacity, rectangle.HasFill);
        if (frame is not null) rectangle = ClipStrokedRectangle(rectangle, stroke, frame.Value);
        return SvgGeometry.FromRectangle(rectangle with { Stroke = element.Presentation.IsPainted ? stroke : null });
    }

    internal static SvgGeometry ParseImage(XmlReader reader, SvgPaintableElementContext context, SvgViewportFrame? frame, double? baseWidth, double? baseHeight, SvgRenderLimits limits, RenderBudget budget)
    {
        SvgImageSyntax syntax = SvgImageSyntax.Parse(reader);
        SvgResolvedPaintableElement element = context.ResolveShape(reader, false);
        double width = syntax.Width ?? baseWidth ?? throw new SvgException(SvgErrorCode.UnsupportedFeature, "Omitted image dimensions default to 100 percent of unknown parent dimensions, which is not implemented.");
        double height = syntax.Height ?? baseHeight ?? throw new SvgException(SvgErrorCode.UnsupportedFeature, "Omitted image dimensions default to 100 percent of unknown parent dimensions, which is not implemented.");
        SvgColor color = DecodeImage(syntax.Href, element, limits, budget, context.WorkCounter, context.CancellationToken);
        SvgRectangle rectangle = element.Transform.Apply(new SvgRectangle(syntax.X, syntax.Y, width, height, color, element.Presentation.HasFill, null));
        if (frame is not null) rectangle = frame.Value.Clip.ClipRectangle(frame.Value.Shift(rectangle));
        return SvgGeometry.FromRectangle(rectangle);
    }

    private static SvgColor DecodeImage(string href, SvgResolvedPaintableElement element, SvgRenderLimits limits, RenderBudget budget, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        const string Prefix = "data:image/png;base64,";
        if (!href.StartsWith(Prefix, StringComparison.Ordinal)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only base64 PNG data-URI embedded images are implemented.");
        byte[] bytes;
        try { bytes = Convert.FromBase64String(href.Substring(Prefix.Length)); }
        catch (FormatException) { throw new SvgException(SvgErrorCode.MalformedInput, "Embedded image data is not valid base64."); }
        using RenderBudget.BudgetReservation reservation = budget.Reserve(bytes.Length, SvgMemoryCategory.Parser);
        return StampOpacity(PngImageDecoder.DecodeSolidColor(bytes, limits, budget, workCounter, cancellationToken), element.Presentation.Opacity);
    }

    private static SvgColor StampOpacity(SvgColor color, byte opacity)
        => new(color.Red, color.Green, color.Blue, SvgReferenceResolver.MultiplyAlpha(color.Alpha, opacity));

    internal static SvgParsedRectangle ParseRectangleWithStyle(XmlReader reader, SvgPaintableElementContext context)
    {
        SvgRectangleSyntax syntax = SvgRectangleSyntax.Parse(reader);
        SvgResolvedPaintableElement element = context.ResolveShape(reader, true);
        SvgRectangle rectangle = element.Transform.Apply(new SvgRectangle(syntax.X, syntax.Y, syntax.Width, syntax.Height, element.Presentation.Color, element.Presentation.HasFill, null));
        SvgStroke? stroke = ParseRectangleStroke(element.Presentation.StrokeColor, syntax.StrokeWidth, element.Transform, rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height, element.Presentation.Opacity, rectangle.HasFill);
        return new SvgParsedRectangle(SvgGeometry.FromRectangle(rectangle with { Stroke = element.Presentation.IsPainted ? stroke : null }), element.Style);
    }

    internal static SvgGeometry ParseLine(XmlReader reader, SvgPaintableElementContext context, SvgViewportFrame? frame)
    {
        SvgLineSyntax syntax = SvgLineSyntax.Parse(reader);
        SvgResolvedPaintableElement element = context.ResolveShape(reader, true);
        SvgColor? strokeColor = element.Presentation.StrokeColor;
        if (element.Transform != AxisTransform.Identity || strokeColor is null || element.Presentation.Opacity != 255 || (syntax.X1 != syntax.X2 && syntax.Y1 != syntax.Y2) || (syntax.X1 == syntax.X2 && syntax.Y1 == syntax.Y2)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only opaque untransformed horizontal or vertical lines are implemented.");
        int strokeWidth = SvgGeometrySyntax.ParseInteger(syntax.StrokeWidth ?? "1");
        if (strokeColor.Value.Alpha != 255 || strokeWidth <= 0 || strokeWidth % 2 != 0) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only opaque even-width line strokes are implemented.");
        SvgColor color = SvgPaintResolver.ApplyOpacity(strokeColor.Value, element.Presentation.Opacity);
        bool painted = element.Presentation.IsPainted;
        SvgAxisAlignedLine line = new(syntax.X1, syntax.Y1, syntax.X2, syntax.Y2, color, painted, strokeWidth);
        return frame is null ? SvgGeometry.FromAxisAlignedLine(line) : frame.Value.ClipShifted(line);
    }

    private static SvgRectangle ClipStrokedRectangle(SvgRectangle rectangle, SvgStroke? stroke, SvgViewportFrame frame)
    {
        SvgRectangle shifted = frame.Shift(rectangle);
        if (stroke is null) return frame.Clip.ClipRectangle(shifted);
        double halfWidth = stroke.Value.Width / 2d;
        if (!frame.Clip.Contains(shifted.X - halfWidth, shifted.Y - halfWidth, shifted.X + shifted.Width + halfWidth, shifted.Y + shifted.Height + halfWidth)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Rectangle strokes crossing a nested viewport boundary are not implemented.");
        return shifted;
    }

    internal static SvgStroke? ParseRectangleStroke(SvgColor? color, string? widthValue, AxisTransform transform, double x, double y, double width, double height, byte opacity, bool hasFill)
    {
        if (color is null) { if (widthValue is not null) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Stroke width without a stroke is not implemented."); return null; }
        if (transform != AxisTransform.Identity || x != Math.Truncate(x) || y != Math.Truncate(y) || width != Math.Truncate(width) || height != Math.Truncate(height) || (hasFill && opacity != 255)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only untransformed integer rectangle strokes, with opacity only when fill-less, are implemented.");
        int strokeWidth = SvgGeometrySyntax.ParseInteger(widthValue ?? "1");
        if (color.Value.Alpha != 255 || strokeWidth <= 0 || strokeWidth % 2 != 0 || strokeWidth > width || strokeWidth > height) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only opaque even-width rectangle strokes no larger than the rectangle dimensions are implemented.");
        return new SvgStroke(SvgPaintResolver.ApplyOpacity(color.Value, opacity), strokeWidth);
    }
}

/// <summary>Pairs a lowered rectangle with the specified style that determines whether a use instance may supply fill.</summary>
internal readonly record struct SvgParsedRectangle(SvgGeometry Geometry, SvgComputedStyle Style);
