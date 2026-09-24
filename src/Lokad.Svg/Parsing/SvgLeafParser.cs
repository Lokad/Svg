using System.Xml;
using Lokad.Svg.Encoding;
using Lokad.Svg.Rendering;
namespace Lokad.Svg.Parsing;

/// <summary>Parses and lowers the currently supported paintable SVG leaves independently of XML traversal.</summary>
internal static class SvgLeafParser
{
    internal static SvgParsedGeometry ParseRectangle(XmlReader reader, SvgPaintableElementContext context, SvgViewportFrame? frame, SvgDocumentState state)
    {
        SvgRectangleSyntax syntax = SvgRectangleSyntax.Parse(reader);
        SvgComputedStyle patternStyle = context.ResolveStyle(reader);
        SvgStrokeStyleSyntax.RequireButtCapJoin(reader, "rectangle");
        AxisTransform earlyTransform = context.ResolveTransform(reader);
        SvgRectangle earlyPlacement = earlyTransform.Apply(new SvgRectangle(syntax.X, syntax.Y, syntax.Width, syntax.Height, default, true, null));
        SvgViewportFrame? effectiveFrame = SvgClipPathSyntax.ResolveLeafClipOrPlacement(reader, state, frame, earlyPlacement);
        if (SvgPatternSyntax.IsPatternFill(patternStyle.Fill)) return RetainSingleGeometry(LowerPaintServerRect(reader, context, patternStyle, state, effectiveFrame), state, context);
        if (SvgGradientSyntax.IsGradientFill(patternStyle.Fill)) return RetainSingleGeometry(SvgGradientSyntax.LowerGradientRect(reader, context, patternStyle, state, effectiveFrame), state, context);
        if (SvgGradientSyntax.IsGradientStrokeUrl(patternStyle.Stroke)) return SvgGradientSyntax.LowerGradientStrokeRect(reader, context, patternStyle, state, effectiveFrame);
        double cornerX = syntax.CornerX ?? syntax.CornerY ?? 0;
        double cornerY = syntax.CornerY ?? syntax.CornerX ?? 0;
        if (cornerX != 0 || cornerY != 0) return RetainSingleGeometry(LowerRoundedRect(reader, context, syntax, cornerX, cornerY, effectiveFrame, state), state, context);
        SvgResolvedPaintableElement element = context.ResolveShape(reader, true);
        SvgRectangle rectangle = element.Transform.Apply(new SvgRectangle(syntax.X, syntax.Y, syntax.Width, syntax.Height, element.Presentation.Color, element.Presentation.HasFill, null));
        SvgStroke? stroke = ParseRectangleStroke(element.Presentation.StrokeColor, syntax.StrokeWidth, element.Transform, rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height, element.Presentation.Opacity, rectangle.HasFill);
        if (stroke is not null && effectiveFrame is not null && effectiveFrame.Value.IsScaled) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Strokes in scaled viewports are not implemented.");
        if (effectiveFrame is not null) rectangle = ClipStrokedRectangle(rectangle, stroke, effectiveFrame.Value);
        return RetainSingleGeometry(SvgGeometry.FromRectangle(rectangle with { Stroke = element.Presentation.IsPainted ? stroke : null }), state, context);
    }

    private static SvgParsedGeometry RetainSingleGeometry(SvgGeometry geometry, SvgDocumentState state, SvgPaintableElementContext context)
    {
        ArgumentNullException.ThrowIfNull(state);
        using BudgetedParserList<SvgGeometry> retained = new(state.Budget, SvgMemoryCategory.Geometry, context.WorkCounter, context.CancellationToken);
        retained.Add(geometry);
        return new SvgParsedGeometry(retained.Detach(), 0);
    }

    internal static SvgGeometry ParseImage(XmlReader reader, SvgPaintableElementContext context, SvgViewportFrame? frame, double? baseWidth, double? baseHeight, SvgRenderLimits limits, RenderBudget budget)
    {
        SvgImageSyntax syntax = SvgImageSyntax.Parse(reader, baseWidth, baseHeight);
        SvgResolvedPaintableElement element = context.ResolveShape(reader, false);
        double width = syntax.Width ?? baseWidth ?? throw new SvgException(SvgErrorCode.UnsupportedFeature, "Omitted image dimensions default to 100 percent of unknown parent dimensions, which is not implemented.");
        double height = syntax.Height ?? baseHeight ?? throw new SvgException(SvgErrorCode.UnsupportedFeature, "Omitted image dimensions default to 100 percent of unknown parent dimensions, which is not implemented.");
        DecodedSolidImage image = DecodeImage(syntax.Href, element, limits, budget, context.WorkCounter, context.CancellationToken);
        string? aspect = reader.GetAttribute("preserveAspectRatio");
        SvgRectangle placement = SvgImageSyntax.MapPlacement(aspect, syntax, image, width, height);
        SvgRectangle rectangle = element.Transform.Apply(placement with { Color = image.Color, HasFill = element.Presentation.HasFill });
        if (frame is not null) rectangle = frame.Value.Clip.ClipRectangle(frame.Value.Shift(rectangle));
        return SvgGeometry.FromRectangle(rectangle);
    }

    private static DecodedSolidImage DecodeImage(string href, SvgResolvedPaintableElement element, SvgRenderLimits limits, RenderBudget budget, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        const string Prefix = "data:image/png;base64,";
        if (!href.StartsWith(Prefix, StringComparison.Ordinal)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only base64 PNG data-URI embedded images are implemented.");
        ArgumentNullException.ThrowIfNull(limits);
        ArgumentNullException.ThrowIfNull(budget);
        ArgumentNullException.ThrowIfNull(workCounter);
        ReadOnlySpan<char> encoded = href.AsSpan(Prefix.Length);
        long maxDecoded = ((long)encoded.Length + 3) / 4 * 3;
        if (maxDecoded > limits.MaximumAccountedBytes) throw new SvgException(SvgErrorCode.LimitExceeded, "Embedded image data exceeds the configured accounting limit.");
        if (maxDecoded > int.MaxValue) throw new SvgException(SvgErrorCode.LimitExceeded, "Embedded image data exceeds the configured accounting limit.");
        using RenderBudget.BudgetReservation? reservation = maxDecoded > 0 ? budget.Reserve(maxDecoded, SvgMemoryCategory.Parser) : null;
        workCounter.ConsumeBytes(encoded.Length, cancellationToken);
        byte[] decoded = new byte[(int)maxDecoded];
        if (!Convert.TryFromBase64Chars(encoded, decoded, out int written)) throw new SvgException(SvgErrorCode.MalformedInput, "Embedded image data is not valid base64.");
        DecodedSolidImage image = PngImageDecoder.DecodeSolidColor(decoded.AsMemory(0, written), limits, budget, workCounter, cancellationToken);
        return new DecodedSolidImage(StampOpacity(image.Color, element.Presentation.Opacity), image.Width, image.Height);
    }

    private static SvgColor StampOpacity(SvgColor color, byte opacity)
        => new(color.Red, color.Green, color.Blue, SvgReferenceResolver.MultiplyAlpha(color.Alpha, opacity));

    private static SvgGeometry LowerPaintServerRect(XmlReader reader, SvgPaintableElementContext context, SvgComputedStyle patternStyle, SvgDocumentState state, SvgViewportFrame? frame)
    {
        string paintId = SvgGradientSyntax.ParsePaintId(patternStyle.Fill);
        if (state.Patterns.TryGetValue(paintId, out _)) return SvgPatternSyntax.LowerPatternRect(reader, context, patternStyle, state, frame);
        return SvgGradientSyntax.LowerGradientRect(reader, context, patternStyle, state, frame);
    }

    /// <summary>Parses one rectangle definition with inherited presentation but the element-local transform only; use clones exclude definition ancestors.</summary>
    private static SvgGeometry LowerRoundedRect(XmlReader reader, SvgPaintableElementContext context, SvgRectangleSyntax syntax, double cornerX, double cornerY, SvgViewportFrame? frame, SvgDocumentState state)
    {
        SvgComputedStyle style = context.ResolveStyle(reader);
        if (SvgPatternSyntax.IsPatternFill(style.Fill)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Pattern fills on rounded rectangles are not implemented.");
        if (SvgGradientSyntax.IsGradientFill(style.Fill)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Gradient fills on rounded rectangles are not implemented.");
        if (SvgGradientSyntax.IsGradientStrokeUrl(style.Stroke)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Gradient strokes on rounded rectangles are not implemented.");
        SvgResolvedPaintableElement element = context.ResolveShape(style, reader, false);
        using BudgetedParserList<SvgPoint> noPoints = new(state.Budget, SvgMemoryCategory.Geometry, context.WorkCounter, context.CancellationToken);
        using BudgetedParserList<int> noEnds = new(state.Budget, SvgMemoryCategory.Geometry, context.WorkCounter, context.CancellationToken);
        using SvgOrthogonalGeometryLowering lowering = new(noPoints, noEnds, "rect", false, element.Style.FillRule is not null && element.Style.FillRule.Equals("evenodd", StringComparison.OrdinalIgnoreCase), int.MaxValue, state.Budget, context.WorkCounter, context.CancellationToken);
        using SvgParsedGeometry parsed = lowering.LowerRoundedRectFill(syntax.X, syntax.Y, syntax.Width, syntax.Height, cornerX, cornerY, element, frame, state.Budget);
        return parsed.Geometries[0];
    }

    internal static SvgParsedRectangle ParseRectangleWithStyle(XmlReader reader, SvgPaintableElementContext context)
    {
        SvgRectangleSyntax syntax = SvgRectangleSyntax.Parse(reader);
        SvgStrokeStyleSyntax.RequireButtCapJoin(reader, "rectangle");
        if ((syntax.CornerX ?? syntax.CornerY ?? 0) != 0 || (syntax.CornerY ?? syntax.CornerX ?? 0) != 0) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Rounded rectangles in definitions stay unsupported.");
        SvgResolvedPaintableElement element = context.ResolveShape(reader, true);
        AxisTransform localTransform = SvgGeometrySyntax.ParseTransform(reader.GetAttribute("transform"));
        SvgRectangle rectangle = localTransform.Apply(new SvgRectangle(syntax.X, syntax.Y, syntax.Width, syntax.Height, element.Presentation.Color, element.Presentation.HasFill, null));
        SvgStroke? stroke = ParseRectangleStroke(element.Presentation.StrokeColor, syntax.StrokeWidth, localTransform, rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height, element.Presentation.Opacity, rectangle.HasFill);
        return new SvgParsedRectangle(SvgGeometry.FromRectangle(rectangle with { Stroke = element.Presentation.IsPainted ? stroke : null }), element.Style);
    }

    internal static SvgParsedGeometry ParseCircle(XmlReader reader, SvgPaintableElementContext context, SvgViewportFrame? frame, SvgDocumentState state)
    {
        SvgEllipseSyntax syntax = SvgEllipseSyntax.ParseCircle(reader);
        return LowerEllipseShape(reader, context, syntax, "circle", frame, state);
    }

    internal static SvgParsedGeometry ParseEllipse(XmlReader reader, SvgPaintableElementContext context, SvgViewportFrame? frame, SvgDocumentState state)
    {
        SvgEllipseSyntax syntax = SvgEllipseSyntax.ParseEllipse(reader);
        return LowerEllipseShape(reader, context, syntax, "ellipse", frame, state);
    }

    private static SvgParsedGeometry LowerEllipseShape(XmlReader reader, SvgPaintableElementContext context, SvgEllipseSyntax syntax, string elementName, SvgViewportFrame? frame, SvgDocumentState state)
    {
        SvgComputedStyle style = context.ResolveStyle(reader);
        if (SvgPatternSyntax.IsPatternFill(style.Fill)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Pattern fills on " + elementName + " elements are not implemented.");
        if (SvgGradientSyntax.IsGradientFill(style.Fill)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Gradient fills on " + elementName + " elements are not implemented.");
        if (SvgGradientSyntax.IsGradientStrokeUrl(style.Stroke)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Gradient strokes on " + elementName + " elements are not implemented.");
        SvgResolvedPaintableElement element = context.ResolveShape(style, reader, elementName is "circle" or "ellipse");
        using BudgetedParserList<SvgPoint> noPoints = new(state.Budget, SvgMemoryCategory.Geometry, context.WorkCounter, context.CancellationToken);
        using BudgetedParserList<int> noEnds = new(state.Budget, SvgMemoryCategory.Geometry, context.WorkCounter, context.CancellationToken);
        using SvgOrthogonalGeometryLowering lowering = new(noPoints, noEnds, elementName, false, element.Style.FillRule is not null && element.Style.FillRule.Equals("evenodd", StringComparison.OrdinalIgnoreCase), int.MaxValue, state.Budget, context.WorkCounter, context.CancellationToken);
        SvgParsedGeometry filled = lowering.LowerEllipseFill(syntax.CenterX, syntax.CenterY, syntax.RadiusX, syntax.RadiusY, element, frame, state.Budget);
        bool nonScalingStroke = SvgStrokeStyleSyntax.ParseVectorEffect(reader.GetAttribute("vector-effect"));
        if (elementName != "circle")
        {
            SvgStroke? ellipseStroke = ParseCircleStroke(element, reader);
            if (ellipseStroke is null || !element.Presentation.IsPainted) return filled;
            string? ellipseDashes = reader.GetAttribute("stroke-dasharray");
            if (ellipseDashes is not null && !ellipseDashes.AsSpan().Trim().Equals("none".AsSpan(), StringComparison.OrdinalIgnoreCase)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Dashed ellipse strokes are not implemented.");
            try
            {
                using SvgParsedGeometry ring = lowering.LowerEllipseStrokeFill(syntax.CenterX, syntax.CenterY, syntax.RadiusX, syntax.RadiusY, element, ellipseStroke.Value, nonScalingStroke, frame, state.Budget);
                return ConcatGeometries(filled, ring, state.Budget, context.WorkCounter, context.CancellationToken);
            }
            catch { filled.Dispose(); throw; }
        }
        SvgStroke? stroke = ParseCircleStroke(element, reader);
        if (stroke is null || !element.Presentation.IsPainted) return filled;
        string? circleDashes = reader.GetAttribute("stroke-dasharray");
        if (circleDashes is not null && !circleDashes.AsSpan().Trim().Equals("none".AsSpan(), StringComparison.OrdinalIgnoreCase)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Dashed circle strokes are not implemented.");
        try
        {
            using SvgParsedGeometry ring = lowering.LowerCircleStrokeFill(syntax.CenterX, syntax.CenterY, syntax.RadiusX, element, stroke.Value, nonScalingStroke, frame, state.Budget);
            return ConcatGeometries(filled, ring, state.Budget, context.WorkCounter, context.CancellationToken);
        }
        catch { filled.Dispose(); throw; }

        static SvgParsedGeometry ConcatGeometries(SvgParsedGeometry first, SvgParsedGeometry second, RenderBudget budget, ParserWorkCounter workCounter, CancellationToken cancellationToken)
        {
            try
            {
                using BudgetedParserList<SvgGeometry> combined = new(budget, SvgMemoryCategory.Geometry, workCounter, cancellationToken);
                combined.AddRange(first.Geometries);
                combined.AddRange(second.Geometries);
                SvgParsedGeometry result = new(combined.Detach(), first.ElementCount + second.ElementCount);
                first.Dispose();
                second.Dispose();
                return result;
            }
            catch
            {
                first.Dispose();
                second.Dispose();
                throw;
            }
        }
    }

    private static SvgStroke? ParseCircleStroke(SvgResolvedPaintableElement element, XmlReader reader)
    {
        if (element.Presentation.StrokeColor is null)
        {
            if (reader.GetAttribute("stroke-width") is not null) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Stroke width without a stroke is not implemented.");
            return null;
        }
        _ = SvgStrokeStyleSyntax.ParseMiterLimit(reader.GetAttribute("stroke-miterlimit"));
        int width = SvgGeometrySyntax.ParseInteger(reader.GetAttribute("stroke-width") ?? "1");
        if (width <= 0) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only positive circle stroke widths are implemented.");
        return new SvgStroke(SvgPaintResolver.ApplyOpacity(element.Presentation.StrokeColor.Value, element.Presentation.Opacity), width);
    }


    internal static SvgGeometry ParseLine(XmlReader reader, SvgPaintableElementContext context, SvgViewportFrame? frame, SvgDocumentState state)
    {
        SvgLineSyntax syntax = SvgLineSyntax.Parse(reader);
        SvgStrokeStyleSyntax.RequireButtCapJoin(reader, "line");
        SvgComputedStyle style = context.ResolveStyle(reader);
        if (SvgGradientSyntax.IsGradientStrokeUrl(style.Stroke)) return SvgGradientSyntax.LowerGradientStrokeLine(reader, context, style, syntax, state, frame);
        SvgResolvedPaintableElement element = context.ResolveShape(style, reader, true);
        if (frame is not null && frame.Value.IsScaled) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Strokes in scaled viewports are not implemented.");
        SvgColor? strokeColor = element.Presentation.StrokeColor;
        if (element.Transform != AxisTransform.Identity || strokeColor is null || (syntax.X1 != syntax.X2 && syntax.Y1 != syntax.Y2) || (syntax.X1 == syntax.X2 && syntax.Y1 == syntax.Y2)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only untransformed horizontal or vertical lines are implemented.");
        int strokeWidth = SvgGeometrySyntax.ParseInteger(syntax.StrokeWidth ?? "1");
        if (strokeWidth <= 0 || strokeWidth % 2 != 0) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only even-width line strokes are implemented.");
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
