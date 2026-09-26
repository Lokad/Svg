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
        double cornerX = syntax.CornerX ?? syntax.CornerY ?? 0;
        double cornerY = syntax.CornerY ?? syntax.CornerX ?? 0;
        bool rounded = cornerX != 0 || cornerY != 0;
        if (SvgPatternSyntax.IsPatternStroke(patternStyle.Stroke, state) && !rounded) return SvgPatternSyntax.LowerPatternStrokeRect(reader, context, patternStyle, state, effectiveFrame);
        if (SvgGradientSyntax.IsGradientStrokeUrl(patternStyle.Stroke) && SvgGradientSyntax.IsGradientFill(patternStyle.Fill) && !rounded)
        {
            string fillId = SvgGradientSyntax.ParsePaintId(patternStyle.Fill);
            if (!state.Patterns.TryGetValue(fillId, out _)) return SvgGradientSyntax.LowerGradientStrokeRect(reader, context, patternStyle, state, effectiveFrame);
        }
        if (SvgPatternSyntax.IsPatternFill(patternStyle.Fill) && !rounded) return LowerPaintServerRect(reader, context, patternStyle, state, effectiveFrame);
        if (SvgGradientSyntax.IsGradientFill(patternStyle.Fill) && !rounded) return RetainSingleGeometry(SvgGradientSyntax.LowerGradientRect(reader, context, patternStyle, state, effectiveFrame), state, context);
        if (SvgGradientSyntax.IsGradientStrokeUrl(patternStyle.Stroke) && !rounded) return SvgGradientSyntax.LowerGradientStrokeRect(reader, context, patternStyle, state, effectiveFrame);
        if (rounded) return RetainSingleGeometry(LowerRoundedRect(reader, context, syntax, cornerX, cornerY, effectiveFrame, state), state, context);
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

    private static SvgParsedGeometry LowerPaintServerRect(XmlReader reader, SvgPaintableElementContext context, SvgComputedStyle patternStyle, SvgDocumentState state, SvgViewportFrame? frame)
    {
        string paintId = SvgGradientSyntax.ParsePaintId(patternStyle.Fill);
        if (state.Patterns.TryGetValue(paintId, out _)) return SvgPatternSyntax.LowerPatternRect(reader, context, patternStyle, state, frame);
        return RetainSingleGeometry(SvgGradientSyntax.LowerGradientRect(reader, context, patternStyle, state, frame), state, context);
    }

    /// <summary>Parses one rectangle definition with inherited presentation but the element-local transform only; use clones exclude definition ancestors.</summary>
    private static SvgGeometry LowerRoundedRect(XmlReader reader, SvgPaintableElementContext context, SvgRectangleSyntax syntax, double cornerX, double cornerY, SvgViewportFrame? frame, SvgDocumentState state)
    {
        SvgComputedStyle style = context.ResolveStyle(reader);
        if (SvgGradientSyntax.IsGradientStrokeUrl(style.Stroke)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Gradient strokes on rounded rectangles are not implemented.");
        SvgPatternTile tile = default;
        bool paintPattern = SvgPatternSyntax.IsPatternFill(style.Fill) && state.Patterns.TryGetValue(SvgPatternSyntax.ParsePatternId(style.Fill), out tile);
        bool paintGradient = SvgGradientSyntax.IsGradientFill(style.Fill) && !paintPattern;
        SvgComputedStyle fillStyle = paintGradient || paintPattern ? style with { Fill = "#ffffff" } : style;
        SvgResolvedPaintableElement element = context.ResolveShape(fillStyle, reader, false);
        AxisTransform transform = context.ResolveTransform(reader);
        if (paintPattern)
        {
            if (frame is not null && frame.Value.IsScaled) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Pattern fills in scaled viewports are not implemented.");
            if (transform.ScaleX < 0 || transform.ScaleY < 0) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only non-reflected pattern fills are implemented.");
        }
        SvgLinearGradient linear = default;
        SvgRadialGradient radial = default;
        bool isLinear = false;
        byte opacity = 255;
        SvgRectangle box = default;
        if (paintGradient || paintPattern) opacity = SvgReferenceResolver.MultiplyAlpha(SvgColorParser.ParseOpacity(style.Opacity), SvgColorParser.ParseOpacity(style.FillOpacity));
        if (paintGradient)
        {
            string paintId = SvgGradientSyntax.ParsePaintId(style.Fill);
            if (state.Gradients.TryGetValue(paintId, out SvgLinearGradient foundLinear))
            {
                linear = SvgGradientSyntax.MirrorBoundingBoxVector(transform, SvgGradientSyntax.MapUserSpaceVector(transform, foundLinear, frame));
                isLinear = true;
            }
            else if (!state.RadialGradients.TryGetValue(paintId, out radial)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only defined local gradient references are implemented.");
            else radial = SvgGradientSyntax.MapUserSpaceRadial(transform, radial, frame);
            SvgRectangle placement = transform.Apply(new SvgRectangle(syntax.X, syntax.Y, syntax.Width, syntax.Height, default, true, null));
            box = frame is null ? placement : frame.Value.Shift(placement);
        }
        using BudgetedParserList<SvgPoint> noPoints = new(state.Budget, SvgMemoryCategory.Geometry, context.WorkCounter, context.CancellationToken);
        using BudgetedParserList<int> noEnds = new(state.Budget, SvgMemoryCategory.Geometry, context.WorkCounter, context.CancellationToken);
        using SvgOrthogonalGeometryLowering lowering = new(noPoints, noEnds, "rect", false, element.Style.FillRule is not null && element.Style.FillRule.Equals("evenodd", StringComparison.OrdinalIgnoreCase), int.MaxValue, state.Budget, context.WorkCounter, context.CancellationToken);
        SvgParsedGeometry parsed = lowering.LowerRoundedRectFill(syntax.X, syntax.Y, syntax.Width, syntax.Height, cornerX, cornerY, element, frame, state.Budget);
        if (!paintGradient && !paintPattern)
        {
            SvgGeometry single = parsed.Geometries[0];
            parsed.Dispose();
            return single;
        }
        try
        {
            SvgGeometry repainted;
            if (paintPattern) repainted = SvgPatternPolygon.RepaintOutline(parsed.Geometries[0].Polygon, tile, opacity, state.Budget);
            else repainted = isLinear
                ? SvgGradientPolygon.RepaintOutline(parsed.Geometries[0].Polygon, linear, box, opacity, state.Budget)
                : SvgRadialPolygon.RepaintOutline(parsed.Geometries[0].Polygon, radial, box, opacity, state.Budget);
            parsed.Dispose();
            return repainted;
        }
        catch { parsed.Dispose(); throw; }
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
        SvgPatternTile tile = default;
        bool paintPattern = SvgPatternSyntax.IsPatternFill(style.Fill) && state.Patterns.TryGetValue(SvgPatternSyntax.ParsePatternId(style.Fill), out tile);
        bool paintGradient = SvgGradientSyntax.IsGradientFill(style.Fill) && !paintPattern;
        if (SvgGradientSyntax.IsGradientStrokeUrl(style.Stroke)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Gradient strokes on " + elementName + " elements are not implemented.");
        if (paintGradient && style.Stroke is not null && !style.Stroke.Equals("none", StringComparison.OrdinalIgnoreCase)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Gradient-filled " + elementName + "s with strokes are not implemented.");
        if (paintPattern && style.Stroke is not null && !style.Stroke.Equals("none", StringComparison.OrdinalIgnoreCase)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Pattern-filled " + elementName + "s with strokes are not implemented.");
        SvgComputedStyle fillStyle = paintGradient || paintPattern ? style with { Fill = "#ffffff" } : style;
        SvgResolvedPaintableElement element = context.ResolveShape(fillStyle, reader, elementName is "circle" or "ellipse");
        AxisTransform transform = element.Transform;
        if (paintPattern)
        {
            if (frame is not null && frame.Value.IsScaled) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Pattern fills in scaled viewports are not implemented.");
            if (transform.ScaleX < 0 || transform.ScaleY < 0) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only non-reflected pattern fills are not implemented.");
        }
        SvgLinearGradient linear = default;
        SvgRadialGradient radial = default;
        bool isLinear = false;
        byte opacity = 255;
        SvgRectangle box = default;
        if (paintGradient)
        {
            string paintId = SvgGradientSyntax.ParsePaintId(style.Fill);
            if (state.Gradients.TryGetValue(paintId, out SvgLinearGradient foundLinear))
            {
                linear = SvgGradientSyntax.MirrorBoundingBoxVector(transform, SvgGradientSyntax.MapUserSpaceVector(transform, foundLinear, frame));
                isLinear = true;
            }
            else if (!state.RadialGradients.TryGetValue(paintId, out radial)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only defined local gradient references are implemented.");
            else radial = SvgGradientSyntax.MapUserSpaceRadial(transform, radial, frame);
            opacity = SvgReferenceResolver.MultiplyAlpha(SvgColorParser.ParseOpacity(style.Opacity), SvgColorParser.ParseOpacity(style.FillOpacity));
            SvgRectangle placement = transform.Apply(new SvgRectangle(syntax.CenterX - syntax.RadiusX, syntax.CenterY - syntax.RadiusY, 2 * syntax.RadiusX, 2 * syntax.RadiusY, default, true, null));
            box = frame is null ? placement : frame.Value.Shift(placement);
        }
        using BudgetedParserList<SvgPoint> noPoints = new(state.Budget, SvgMemoryCategory.Geometry, context.WorkCounter, context.CancellationToken);
        using BudgetedParserList<int> noEnds = new(state.Budget, SvgMemoryCategory.Geometry, context.WorkCounter, context.CancellationToken);
        using SvgOrthogonalGeometryLowering lowering = new(noPoints, noEnds, elementName, false, element.Style.FillRule is not null && element.Style.FillRule.Equals("evenodd", StringComparison.OrdinalIgnoreCase), int.MaxValue, state.Budget, context.WorkCounter, context.CancellationToken);
        SvgParsedGeometry filled = lowering.LowerEllipseFill(syntax.CenterX, syntax.CenterY, syntax.RadiusX, syntax.RadiusY, element, frame, state.Budget);
        if (paintGradient || paintPattern)
        {
            SvgGeometry repainted;
            if (paintPattern) repainted = SvgPatternPolygon.RepaintOutline(filled.Geometries[0].Polygon, tile, opacity, state.Budget);
            else repainted = isLinear
                ? SvgGradientPolygon.RepaintOutline(filled.Geometries[0].Polygon, linear, box, opacity, state.Budget)
                : SvgRadialPolygon.RepaintOutline(filled.Geometries[0].Polygon, radial, box, opacity, state.Budget);
            filled.Dispose();
            return RetainSingleGeometry(repainted, state, context);
        }
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


    internal static SvgParsedGeometry ParseLine(XmlReader reader, SvgPaintableElementContext context, SvgViewportFrame? frame, SvgDocumentState state)
    {
        SvgLineSyntax syntax = SvgLineSyntax.Parse(reader);
        SvgStrokeStyleSyntax.RequireLineCapJoin(reader, "line");
        SvgComputedStyle style = context.ResolveStyle(reader);
        if (SvgPatternSyntax.IsPatternStroke(style.Stroke, state)) return SvgPatternSyntax.LowerPatternStrokeLine(reader, context, style, syntax, state, frame);
        if (SvgGradientSyntax.IsGradientStrokeUrl(style.Stroke)) return SvgGradientSyntax.LowerGradientStrokeLine(reader, context, style, syntax, state, frame);
        SvgResolvedPaintableElement element = context.ResolveShape(style, reader, true);
        if (frame is not null && frame.Value.IsScaled) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Strokes in scaled viewports are not implemented.");
        SvgColor? strokeColor = element.Presentation.StrokeColor;
        if (element.Transform != AxisTransform.Identity || strokeColor is null || (syntax.X1 != syntax.X2 && syntax.Y1 != syntax.Y2) || (syntax.X1 == syntax.X2 && syntax.Y1 == syntax.Y2)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only untransformed horizontal or vertical lines are implemented.");
        int strokeWidth = SvgGeometrySyntax.ParseInteger(syntax.StrokeWidth ?? "1");
        if (strokeWidth <= 0 || strokeWidth % 2 != 0) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only even-width line strokes are implemented.");
        SvgStrokeCap linecap = SvgStrokeStyleSyntax.ParseLinecap(reader.GetAttribute("stroke-linecap"));
        double[]? dashes = SvgStrokeStyleSyntax.ParseDasharray(reader.GetAttribute("stroke-dasharray"));
        if (linecap == SvgStrokeCap.Square && dashes is null) syntax = syntax.WithSquareCaps(strokeWidth);
        SvgColor color = SvgPaintResolver.ApplyOpacity(strokeColor.Value, element.Presentation.Opacity);
        bool painted = element.Presentation.IsPainted;
        if (dashes is null)
        {
            if (linecap == SvgStrokeCap.Round)
            {
                if (color.Alpha != 255) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Translucent round-capped lines are not implemented.");
                return LowerRoundCapLine();
            }
            SvgAxisAlignedLine line = new(syntax.X1, syntax.Y1, syntax.X2, syntax.Y2, color, painted, strokeWidth);
            return RetainSingleGeometry(frame is null ? SvgGeometry.FromAxisAlignedLine(line) : frame.Value.ClipShifted(line), state, context);
        }
        if (linecap == SvgStrokeCap.Round) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Round caps with dashed lines are not implemented.");
        double dashOffset = SvgStrokeStyleSyntax.ParseDashoffset(reader.GetAttribute("stroke-dashoffset"));
        double? pathLength = SvgStrokeStyleSyntax.ParsePathLength(reader.GetAttribute("pathLength"));
        if (linecap == SvgStrokeCap.Square && color.Alpha != 255) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Translucent square-capped dashed lines are not implemented.");
        return LowerDashedLine();

        SvgParsedGeometry LowerRoundCapLine()
        {
            SvgAxisAlignedLine line = new(syntax.X1, syntax.Y1, syntax.X2, syntax.Y2, color, painted, strokeWidth);
            SvgGeometry band = frame is null ? SvgGeometry.FromAxisAlignedLine(line) : frame.Value.ClipShifted(line);
            SvgPaint discPaint = new(color, true, 255, color, false);
            SvgResolvedPaintableElement discElement = element with { Presentation = new SvgResolvedShapePresentation(discPaint, color, null, 255, painted) };
            double radius = strokeWidth / 2d;
            using BudgetedParserList<SvgPoint> noPoints = new(state.Budget, SvgMemoryCategory.Geometry, context.WorkCounter, context.CancellationToken);
            using BudgetedParserList<int> noEnds = new(state.Budget, SvgMemoryCategory.Geometry, context.WorkCounter, context.CancellationToken);
            using SvgOrthogonalGeometryLowering lowering = new(noPoints, noEnds, "line", false, false, int.MaxValue, state.Budget, context.WorkCounter, context.CancellationToken);
            using SvgParsedGeometry first = lowering.LowerEllipseFill(syntax.X1, syntax.Y1, radius, radius, discElement, frame, state.Budget);
            using SvgParsedGeometry second = lowering.LowerEllipseFill(syntax.X2, syntax.Y2, radius, radius, discElement, frame, state.Budget);
            using BudgetedParserList<SvgGeometry> all = new(state.Budget, SvgMemoryCategory.Geometry, context.WorkCounter, context.CancellationToken);
            all.Add(band);
            all.AddRange(first.Geometries);
            all.AddRange(second.Geometries);
            return new(all.Detach(), first.ElementCount + second.ElementCount);
        }

        SvgParsedGeometry LowerDashedLine()
        {
            using BudgetedParserList<SvgRectangle> runRects = new(state.Budget, SvgMemoryCategory.Geometry, context.WorkCounter, context.CancellationToken);
            bool paintsAny = AppendLineDashRunRects(syntax, strokeWidth, linecap == SvgStrokeCap.Square, dashes, dashOffset, pathLength, frame, state.Budget, context.WorkCounter, context.CancellationToken, runRects);
            using BudgetedParserList<SvgGeometry> runs = new(state.Budget, SvgMemoryCategory.Geometry, context.WorkCounter, context.CancellationToken);
            for (int runIndex = 0; runIndex < runRects.Count; runIndex++) runs.Add(SvgGeometry.FromOrthogonalBand(runRects[runIndex] with { Color = color, HasFill = paintsAny && painted }));
            return new SvgParsedGeometry(runs.Detach(), 0);
        }
    }

    /// <summary>Appends one clipped rectangle per dash-pattern on-run along a horizontal or vertical line, reporting whether any run paints.</summary>
    /// <remarks><para>Callers paint over the returned rectangles and leave the single quiet rectangle unpainted, sharing the solid phase convention across paints.</para></remarks>
    internal static bool AppendLineDashRunRects(SvgLineSyntax syntax, int strokeWidth, bool squareCaps, double[] dashes, double dashOffset, double? pathLength, SvgViewportFrame? frame, RenderBudget budget, ParserWorkCounter workCounter, CancellationToken cancellationToken, BudgetedParserList<SvgRectangle> runs)
    {
        ArgumentNullException.ThrowIfNull(dashes);
        ArgumentNullException.ThrowIfNull(budget);
        ArgumentNullException.ThrowIfNull(workCounter);
        ArgumentNullException.ThrowIfNull(runs);
        bool horizontal = syntax.X1 != syntax.X2;
        double origin = horizontal ? Math.Min(syntax.X1, syntax.X2) : Math.Min(syntax.Y1, syntax.Y2);
        double length = horizontal ? Math.Abs(syntax.X2 - syntax.X1) : Math.Abs(syntax.Y2 - syntax.Y1);
        double halfWidth = strokeWidth / 2d;
        double fixedAxis = horizontal ? syntax.Y1 : syntax.X1;
        double capExtension = squareCaps ? halfWidth : 0;
        if (pathLength == 0)
        {
            SvgRectangle pinned = horizontal
                ? new SvgRectangle(origin - capExtension, fixedAxis - halfWidth, length + 2 * capExtension, strokeWidth, default, true, null)
                : new SvgRectangle(fixedAxis - halfWidth, origin - capExtension, strokeWidth, length + 2 * capExtension, default, true, null);
            runs.Add(frame is null ? pinned : frame.Value.ClipShifted(pinned));
            return true;
        }
        double[] pattern = (double[])dashes.Clone();
        double dashFactor = SvgOrthogonalGeometryLowering.RescaleDashPattern(pattern, length, pathLength);
        double total = 0;
        if (squareCaps) for (int entryIndex = 0; entryIndex < pattern.Length; entryIndex += 2) if (pattern[entryIndex] == 0) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Zero-length square-capped dashes are not implemented.");
        foreach (double patternEntry in pattern) total += patternEntry;
        if (!(total > 0))
        {
            SvgRectangle quiet = horizontal
                ? new SvgRectangle(origin - capExtension, fixedAxis - halfWidth, length + 2 * capExtension, strokeWidth, default, false, null)
                : new SvgRectangle(fixedAxis - halfWidth, origin - capExtension, strokeWidth, length + 2 * capExtension, default, false, null);
            runs.Add(frame is null ? quiet : frame.Value.ClipShifted(quiet));
            return false;
        }
        double scaledOffset = dashOffset * dashFactor;
        if (!double.IsFinite(scaledOffset)) throw new SvgException(SvgErrorCode.LimitExceeded, "Dashed line strokes exceed the admitted numeric range.");
        double phase = scaledOffset - Math.Floor(scaledOffset / total) * total;
        if (phase < 0) phase = 0;
        else if (phase >= total) phase = 0;
        int entry = 0;
        double entered = 0;
        while (entered + pattern[entry] <= phase)
        {
            entered += pattern[entry];
            entry = (entry + 1) % pattern.Length;
        }
        double remaining = entered + pattern[entry] - phase;
        while (remaining == 0)
        {
            entry = (entry + 1) % pattern.Length;
            entered = phase;
            remaining = pattern[entry];
        }
        bool onEntry = (entry & 1) == 0;
        double cursor = 0;
        int produced = 0;
        while (cursor < length)
        {
            if ((produced & 127) == 0) cancellationToken.ThrowIfCancellationRequested();
            double take = Math.Min(remaining, length - cursor);
            if (onEntry && take > 0)
            {
                SvgRectangle run = horizontal
                    ? new SvgRectangle(origin + cursor - capExtension, fixedAxis - halfWidth, take + 2 * capExtension, strokeWidth, default, true, null)
                    : new SvgRectangle(fixedAxis - halfWidth, origin + cursor - capExtension, strokeWidth, take + 2 * capExtension, default, true, null);
                runs.Add(frame is null ? run : frame.Value.ClipShifted(run));
            }
            workCounter.Consume(cancellationToken);
            cursor += take;
            entry = (entry + 1) % pattern.Length;
            onEntry = (entry & 1) == 0;
            remaining = pattern[entry];
            while (remaining == 0)
            {
                entry = (entry + 1) % pattern.Length;
                onEntry = (entry & 1) == 0;
                remaining = pattern[entry];
            }
            produced++;
        }
        return true;
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
