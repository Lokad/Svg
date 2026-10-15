using System.Xml;
using Lokad.Svg.Encoding;
using Lokad.Svg.Rendering;
namespace Lokad.Svg.Parsing;

/// <summary>Parses and lowers the currently supported paintable SVG leaves independently of XML traversal.</summary>
internal static class SvgLeafParser
{
    internal static SvgParsedGeometry ParseRectangle(XmlReader reader, SvgPaintableElementContext context, SvgViewportFrame? frame, SvgDocumentState state)
    {
        SvgRecordedPaintStyle source = SvgPaintRecording.Resolve(reader, context, state, frame);
        context = context with { DeferPaintOpacity = true };
        SvgParsedGeometry parsed = ParseRectangleCore(reader, context, frame, state);
        return SvgPaintRecording.Attach(parsed, source, reader.LocalName, state, context.WorkCounter, context.CancellationToken);
    }

    internal static SvgParsedGeometry ParseRectangleCore(XmlReader reader, SvgPaintableElementContext context, SvgViewportFrame? frame, SvgDocumentState state)
    {
        SvgRectangleSyntax syntax = SvgRectangleSyntax.Parse(reader, context.LengthBases);
        SvgComputedStyle patternStyle = context.ResolveStyle(reader);
        _ = SvgStrokeStyleSyntax.ParseLinecap(patternStyle.Linecap);
        _ = SvgStrokeStyleSyntax.ParseLinejoin(patternStyle.Linejoin, true);
        SvgAffineTransform earlyTransform = context.ResolveTransform(reader);
        double cornerX = syntax.CornerX ?? syntax.CornerY ?? 0;
        double cornerY = syntax.CornerY ?? syntax.CornerX ?? 0;
        bool rounded = cornerX != 0 || cornerY != 0;
        if ((!earlyTransform.IsAxisAligned || context.RetainInstanceStrokePath) && !rounded
            && (!SvgGradientSyntax.IsGradientFill(patternStyle.Fill) || !earlyTransform.IsAxisAligned)
            && (patternStyle.Stroke is null or "none" || SvgStrokeStyleSyntax.IsZeroStrokeWidth(patternStyle.Width ?? syntax.StrokeWidth)))
            return LowerTransformedFill(reader, context, syntax, frame, state);
        if (!earlyTransform.IsAxisAligned && !rounded)
        {
            if (reader.GetAttribute("clip-path") is not null && !state.DeferringLeafClip && !state.DeferringFilterClip)
                throw new SvgException(SvgErrorCode.UnsupportedFeature, "Clip paths on transformed rectangle strokes are not implemented.");
            return LowerRoundedRect(reader, context, syntax, 0, 0, frame, state);
        }
        SvgRectangle earlyPlacement = earlyTransform.Apply(new SvgRectangle(syntax.X, syntax.Y, syntax.Width, syntax.Height, default, true, null));
        SvgViewportFrame? effectiveFrame = SvgClipPathSyntax.ResolveLeafClipOrPlacement(reader, state, frame, earlyPlacement);
        if (!rounded && patternStyle.Stroke is string mappedStroke
            && !mappedStroke.AsSpan().Trim().Equals("none", StringComparison.OrdinalIgnoreCase)
            && !SvgStrokeStyleSyntax.IsZeroStrokeWidth(patternStyle.Width ?? syntax.StrokeWidth))
        {
            double nativeWidth = SvgStrokeStyleSyntax.ParseStrokeWidth(patternStyle.Width ?? syntax.StrokeWidth, "rectangle");
            if (context.RetainInstanceStrokePath || !earlyTransform.IsTranslation || nativeWidth > syntax.Width || nativeWidth > syntax.Height)
                return LowerRoundedRect(reader, context, syntax, 0, 0, effectiveFrame, state);
        }
        // A zero width paints no stroke bands, so paint strokes fall through to the fill-only solid path below.
        if (SvgPatternSyntax.IsPatternStroke(patternStyle.Stroke, state) && !rounded && !SvgStrokeStyleSyntax.IsZeroStrokeWidth(patternStyle.Width)) return SvgPatternSyntax.LowerPatternStrokeRect(reader, context, patternStyle, state, effectiveFrame);
        if (SvgGradientSyntax.IsGradientStrokeUrl(patternStyle.Stroke) && SvgGradientSyntax.IsGradientFill(patternStyle.Fill) && !rounded && !SvgStrokeStyleSyntax.IsZeroStrokeWidth(patternStyle.Width))
        {
            string fillId = SvgGradientSyntax.ParsePaintId(patternStyle.Fill);
            if (!state.Patterns.TryGetValue(fillId, out _)) return SvgGradientSyntax.LowerGradientStrokeRect(reader, context, patternStyle, state, effectiveFrame);
        }
        if (SvgPatternSyntax.IsPatternFill(patternStyle.Fill) && !rounded) return LowerPaintServerRect(reader, context, patternStyle, state, effectiveFrame);
        if (SvgGradientSyntax.IsGradientFill(patternStyle.Fill) && !rounded) return RetainSingleGeometry(SvgGradientSyntax.LowerGradientRect(reader, context, patternStyle, state, effectiveFrame), state, context);
        if (SvgGradientSyntax.IsGradientStrokeUrl(patternStyle.Stroke) && !rounded && !SvgStrokeStyleSyntax.IsZeroStrokeWidth(patternStyle.Width)) return SvgGradientSyntax.LowerGradientStrokeRect(reader, context, patternStyle, state, effectiveFrame);
        if (rounded) return LowerRoundedRect(reader, context, syntax, cornerX, cornerY, effectiveFrame, state);
        SvgResolvedPaintableElement element = context.ResolveShape(reader, true);
        SvgRectangle rectangle = element.Transform.Apply(new SvgRectangle(syntax.X, syntax.Y, syntax.Width, syntax.Height, element.Presentation.Color, element.Presentation.HasFill, null));
        SvgStroke? stroke = ParseRectangleStroke(element.Presentation.StrokeColor, element.Style.Width ?? syntax.StrokeWidth, element.Transform, rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height, element.Presentation.Opacity, rectangle.HasFill);
        if (stroke is not null && SvgStrokeStyleSyntax.ParseDasharray(element.Style.Dasharray) is not null) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only solid (none) stroke dash arrays are implemented.");
        if (stroke is not null && effectiveFrame is not null && effectiveFrame.Value.IsScaled) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Strokes in scaled viewports are not implemented.");
        if (effectiveFrame is not null) rectangle = ClipStrokedRectangle(rectangle, stroke, effectiveFrame.Value);
        SvgSolidPaint? solidPaint = element.Style.GetOpacity().Value == 1 ? SvgPaintResolver.ResolveSolidPaint(element.Presentation.Paint) : null;
        return RetainSingleGeometry(SvgGeometry.FromRectangle(rectangle with { Stroke = element.Presentation.IsPainted ? stroke : null }) with { SolidPaint = solidPaint }, state, context);

        static SvgParsedGeometry LowerTransformedFill(XmlReader input, SvgPaintableElementContext elementContext, SvgRectangleSyntax rectangle, SvgViewportFrame? clip, SvgDocumentState document)
        {
            SvgComputedStyle style = elementContext.ResolveStyle(input);
            bool shader = SvgGradientSyntax.IsGradientFill(style.Fill);
            SvgResolvedPaintableElement element = elementContext.ResolveShape(shader ? style with { Fill = "#ffffff", Stroke = "none" } : style, input, true);
            // Contour references use the existing deferred native clip binding.
            // Rectangle/object-box clips still need their own affine admission.
            if (input.GetAttribute("clip-path") is not null && !document.DeferringLeafClip && !document.DeferringFilterClip)
                throw new SvgException(SvgErrorCode.UnsupportedFeature, "Clip paths on transformed rectangle fills are not implemented.");
            if (rectangle.Width == 0 || rectangle.Height == 0 || element.Transform.A * element.Transform.D == element.Transform.B * element.Transform.C)
            {
                using BudgetedGeometryBuilder empty = new(document.Budget, SvgMemoryCategory.Geometry, elementContext.WorkCounter, elementContext.CancellationToken);
                return new SvgParsedGeometry(empty.Detach(), 0);
            }
            using BudgetedParserList<SvgPoint> noPoints = new(document.Budget, SvgMemoryCategory.Geometry, elementContext.WorkCounter, elementContext.CancellationToken);
            using BudgetedParserList<int> noEnds = new(document.Budget, SvgMemoryCategory.Geometry, elementContext.WorkCounter, elementContext.CancellationToken);
            using SvgOrthogonalGeometryLowering lowering = new(noPoints, noEnds, "rect", false, false, int.MaxValue, document.Budget, elementContext.WorkCounter, elementContext.CancellationToken);
            SvgParsedGeometry filled = lowering.LowerRectangleSourceFill(rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height, element, clip, document.Budget);
            if (!shader || !element.Presentation.HasFill || filled.Geometries.Length == 0) return filled;
            using BudgetedGeometryBuilder repainted = new(document.Budget, SvgMemoryCategory.Geometry, elementContext.WorkCounter, elementContext.CancellationToken);
            try
            {
                string paintId = SvgGradientSyntax.ParsePaintId(style.Fill);
                bool linear = document.Gradients.TryGetValue(paintId, out SvgLinearGradient gradient);
                SvgRadialGradient radial = default;
                if (!linear && !document.RadialGradients.TryGetValue(paintId, out radial))
                    throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only defined linear and radial paints on affine rectangle fills are implemented.");
                SvgAuthoredPath source = filled.Geometries[0].AuthoredPath ?? throw new InvalidOperationException("Affine rectangles require retained source commands.");
                SvgRectangle box = new(rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height, default, false, null);
                if (linear) gradient = SvgGradientSyntax.BindLinearSourceMap(gradient, box, source.SceneMap, document, elementContext.WorkCounter, elementContext.CancellationToken);
                else radial = SvgGradientSyntax.BindRadialSourceMap(radial, box, source.SceneMap, elementContext.WorkCounter, elementContext.CancellationToken);
                byte opacity = SvgReferenceResolver.MultiplyAlpha(style.GetOpacity().Alpha, SvgColorParser.ParseOpacity(style.FillOpacity));
                foreach (SvgGeometry geometry in filled.Geometries)
                {
                    SvgGeometry painted = geometry.TransferAuthoredPathTo(linear
                        ? SvgGradientPolygon.RepaintOutline(geometry.Polygon, gradient, box, opacity, document.Budget)
                        : SvgRadialPolygon.RepaintOutline(geometry.Polygon, radial, box, opacity, document.Budget));
                    try { repainted.Add(painted); }
                    catch { painted.Dispose(); throw; }
                }
                filled.Dispose();
                return new(repainted.Detach(), 0);
            }
            catch { foreach (SvgGeometry geometry in repainted.AsSpan()) geometry.Dispose(); filled.Discard(); throw; }
        }
    }

    private static SvgParsedGeometry RetainSingleGeometry(SvgGeometry geometry, SvgDocumentState state, SvgPaintableElementContext context)
    {
        ArgumentNullException.ThrowIfNull(state);
        using BudgetedGeometryBuilder retained = new(state.Budget, SvgMemoryCategory.Geometry, context.WorkCounter, context.CancellationToken);
        retained.Add(geometry);
        return new SvgParsedGeometry(retained.Detach(), 0);
    }

    internal static SvgGeometry ParseImage(XmlReader reader, SvgPaintableElementContext context, SvgViewportFrame? frame, double? baseWidth, double? baseHeight, SvgRenderLimits limits, SvgDocumentState state)
    {
        SvgRecordedPaintStyle source = SvgPaintRecording.Resolve(reader, context, state, frame);
        SvgGeometry geometry = ParseImageCore(reader, context with { DeferPaintOpacity = true }, frame, baseWidth, baseHeight,
            limits, state.Budget, source.ElementOpacity.Value != 1);
        try
        {
            // An image is one paint operation. Its opacity can fold into a single
            // paint, but must restore outside an authored mask like other leaves.
            float alpha = geometry.Kind == SvgGeometryKind.Rectangle ? geometry.Rectangle.Color.Alpha / 255f : 1;
            SvgRecordedPaintValue paint = new(state.NewPaintOperationIdentity(), alpha, true, source.Scope, source.ElementOpacity, source.OpacityScope)
            { ObjectBounds = source.ObjectBounds, VisualBounds = source.VisualBounds };
            return geometry with { RecordedPaint = state.Scene.RegisterRecordedPaint(paint) };
        }
        catch { geometry.Dispose(); throw; }

        static SvgGeometry ParseImageCore(XmlReader reader, SvgPaintableElementContext context, SvgViewportFrame? frame,
            double? baseWidth, double? baseHeight, SvgRenderLimits limits, RenderBudget budget, bool retainBitmap)
        {
            SvgImageSyntax syntax = SvgImageSyntax.Parse(reader, baseWidth, baseHeight);
            SvgComputedStyle style = context.ResolveStyle(reader);
            // SVG fill does not paint image pixels; a container's shader cannot recolor them.
            if (SvgPatternSyntax.IsPatternFill(style.Fill)) style = style with { Fill = "white" };
            SvgResolvedPaintableElement element = context.ResolveShape(style, reader, false);
            double width = syntax.Width ?? baseWidth ?? throw new SvgException(SvgErrorCode.UnsupportedFeature, "Omitted image dimensions default to 100 percent of unknown parent dimensions, which is not implemented.");
            double height = syntax.Height ?? baseHeight ?? throw new SvgException(SvgErrorCode.UnsupportedFeature, "Omitted image dimensions default to 100 percent of unknown parent dimensions, which is not implemented.");
            DecodedPngImage image = DecodeImage(syntax.Href, limits, budget, context.WorkCounter, context.CancellationToken);
            try
            {
                string? aspect = reader.GetAttribute("preserveAspectRatio");
                if (image.Raster is null && !retainBitmap && element.Transform.IsAxisAligned)
                {
                    DecodedSolidImage solid = new(image.Color, image.Width, image.Height);
                    SvgRectangle placement = SvgImageSyntax.MapPlacement(aspect, syntax, solid, width, height);
                    SvgRectangle rectangle = element.Transform.Apply(placement with { Color = solid.Color, HasFill = element.Presentation.HasFill });
                    if (frame is not null) rectangle = frame.Value.Clip.ClipRectangle(frame.Value.Shift(rectangle));
                    return SvgGeometry.FromRectangle(rectangle);
                }
                (SvgRectangle box, SvgShaderMatrix local) = SvgImageSyntax.MapRasterPlacement(aspect, syntax, image.Width, image.Height, width, height);
                SvgRectangle sourceBox = box;
                bool affine = !element.Transform.IsAxisAligned;
                if (affine)
                {
                    SvgPaintBounds bounds = new SvgPaintBounds((float)box.X, (float)box.Y, (float)(box.X + box.Width), (float)(box.Y + box.Height))
                        .Map(element.Transform, context.WorkCounter, context.CancellationToken);
                    box = new(bounds.Left, bounds.Top, bounds.Right - bounds.Left, bounds.Bottom - bounds.Top, default, element.Presentation.HasFill, null);
                }
                else box = element.Transform.Apply(box with { HasFill = element.Presentation.HasFill });
                SvgAffineTransform reference = element.Transform;
                if (frame is { } viewport)
                {
                    box = viewport.Clip.ClipRectangle(viewport.Shift(box));
                    reference = SvgAffineTransform.FromScaleOffset(viewport.ScaleX, viewport.ScaleY, viewport.OffsetX, viewport.OffsetY).ComposeChecked(reference);
                }
                SvgShaderMatrix referenceMap = new((float)reference.A, (float)reference.C, (float)reference.E,
                    (float)reference.B, (float)reference.D, (float)reference.F);
                SvgGeometry geometry = SvgGeometry.FromImage(image.Raster is {} pixels
                    ? new(pixels, image.Width, image.Height, box, referenceMap, local, element.Presentation.Opacity)
                    : new(image.Color, image.Width, image.Height, box, referenceMap, local, element.Presentation.Opacity));
                if (!affine) return geometry;
                try
                {
                    using BudgetedParserList<SvgAuthoredPathCommand> commands = new(budget, SvgMemoryCategory.Scene, context.WorkCounter, context.CancellationToken);
                    double right = sourceBox.X + sourceBox.Width, bottom = sourceBox.Y + sourceBox.Height;
                    commands.Add(new(SvgAuthoredPathVerb.Move, SvgAuthoredArcFlags.None, new(sourceBox.X, sourceBox.Y), default, default));
                    commands.Add(new(SvgAuthoredPathVerb.Line, SvgAuthoredArcFlags.None, new(right, sourceBox.Y), default, default));
                    commands.Add(new(SvgAuthoredPathVerb.Line, SvgAuthoredArcFlags.None, new(right, bottom), default, default));
                    commands.Add(new(SvgAuthoredPathVerb.Line, SvgAuthoredArcFlags.None, new(sourceBox.X, bottom), default, default));
                    commands.Add(new(SvgAuthoredPathVerb.Close, SvgAuthoredArcFlags.None, default, default, default));
                    OwnedParserArray<SvgAuthoredPathCommand> owned = commands.Detach();
                    try { return geometry with { AuthoredPath = SvgAuthoredPath.CreateConvexShape(owned, reference, budget) }; }
                    catch { owned.Dispose(); throw; }
                }
                catch { geometry.Dispose(); throw; }
            }
            catch { image.Raster?.Dispose(); throw; }
        }
    }

    private static DecodedPngImage DecodeImage(string href, SvgRenderLimits limits, RenderBudget budget, ParserWorkCounter workCounter, CancellationToken cancellationToken)
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
        return PngImageDecoder.Decode(decoded.AsMemory(0, written), limits, budget, workCounter, cancellationToken);
    }

    private static SvgParsedGeometry LowerPaintServerRect(XmlReader reader, SvgPaintableElementContext context, SvgComputedStyle patternStyle, SvgDocumentState state, SvgViewportFrame? frame)
    {
        string paintId = SvgGradientSyntax.ParsePaintId(patternStyle.Fill);
        if (state.Patterns.TryGetValue(paintId, out _)) return SvgPatternSyntax.LowerPatternRect(reader, context, patternStyle, state, frame);
        return RetainSingleGeometry(SvgGradientSyntax.LowerGradientRect(reader, context, patternStyle, state, frame), state, context);
    }

    /// <summary>Parses one rectangle definition with inherited presentation but the element-local transform only; use clones exclude definition ancestors.</summary>
    private static SvgParsedGeometry LowerRoundedRect(XmlReader reader, SvgPaintableElementContext context, SvgRectangleSyntax syntax, double cornerX, double cornerY, SvgViewportFrame? frame, SvgDocumentState state)
    {
        SvgComputedStyle style = context.ResolveStyle(reader);
        SvgLinearGradient strokeGradient = default;
        SvgRadialGradient strokeCircle = default;
        SvgPatternTile strokeTile = default;
        bool paintStrokeLinear = SvgOrthogonalShapeParser.TryResolveOutlineGradient(style, state, out strokeGradient);
        bool paintStrokeRadial = SvgOrthogonalShapeParser.TryResolveOutlineRadial(style, state, out strokeCircle);
        bool paintStrokePattern = SvgOrthogonalShapeParser.TryResolveOutlinePattern(style, state, out strokeTile);
        if (SvgGradientSyntax.IsGradientStrokeUrl(style.Stroke) && !paintStrokeLinear && !paintStrokeRadial && !paintStrokePattern && !SvgStrokeStyleSyntax.IsZeroStrokeWidth(style.Width)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Gradient strokes on rounded rectangles are not implemented.");
        SvgPatternTile tile = default;
        bool paintPattern = SvgPatternSyntax.IsPatternFill(style.Fill) && state.Patterns.TryGetValue(SvgPatternSyntax.ParsePatternId(style.Fill), out tile);
        bool paintGradient = SvgGradientSyntax.IsGradientFill(style.Fill) && !paintPattern;
        SvgComputedStyle fillStyle = paintGradient || paintPattern ? style with { Fill = "#ffffff" } : (paintStrokeLinear || paintStrokeRadial || paintStrokePattern) ? style with { Stroke = "none" } : style;
        if (paintStrokeLinear || paintStrokeRadial || paintStrokePattern) fillStyle = fillStyle with { Stroke = "none" };
        SvgResolvedPaintableElement element = context.ResolveShape(fillStyle, reader, true);
        SvgAffineTransform transform = context.ResolveTransform(reader);
        if (!transform.IsAxisAligned && (syntax.Width == 0 || syntax.Height == 0))
        {
            using BudgetedGeometryBuilder empty = new(state.Budget, SvgMemoryCategory.Geometry, context.WorkCounter, context.CancellationToken);
            return new SvgParsedGeometry(empty.Detach(), 0);
        }
        if (paintPattern)
        {
            tile = SvgPatternReferenceResolver.ForPlacement(SvgPatternSyntax.ParsePatternId(style.Fill), tile, transform,
                state, context.WorkCounter, context.CancellationToken);
        }
        SvgLinearGradient linear = default;
        SvgRadialGradient radial = default;
        bool isLinear = false;
        byte opacity = 255;
        SvgRectangle box = default;
        if (paintGradient || paintPattern) opacity = SvgReferenceResolver.MultiplyAlpha(style.GetOpacity().Alpha, SvgColorParser.ParseOpacity(style.FillOpacity));
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
        try
        {
            if (paintGradient || paintPattern)
            {
                SvgGeometry repainted;
                if (paintPattern) repainted = SvgPatternPolygon.RepaintOutline(parsed.Geometries[0].Polygon, tile, opacity, frame is {} viewport ? SvgAffineTransform.FromScaleOffset(viewport.ScaleX, viewport.ScaleY, viewport.OffsetX, viewport.OffsetY).ComposeChecked(transform) : transform, state.Budget);
                else repainted = isLinear
                    ? SvgGradientPolygon.RepaintOutline(parsed.Geometries[0].Polygon, linear, box, opacity, state.Budget)
                    : SvgRadialPolygon.RepaintOutline(parsed.Geometries[0].Polygon, radial, box, opacity, state.Budget);
                repainted = parsed.Geometries[0].TransferAuthoredPathTo(repainted);
                parsed.Dispose();
                parsed = RetainSingleGeometry(repainted, state, context);
            }
            if (paintStrokeLinear || paintStrokeRadial || paintStrokePattern)
                return LowerPaintedRoundedStroke(reader, context, syntax, cornerX, cornerY, element, parsed, strokeGradient, strokeCircle, strokeTile, paintStrokeLinear, paintStrokeRadial, frame, state);
            SvgStrokeStyleSyntax.SvgAdmittedStroke? stroke = SvgStrokeStyleSyntax.ResolveAdmittedStroke(reader, element.Style, element, "rect", true);
            if (stroke is null) return parsed;
            SvgParsedGeometry stroked = LowerRoundedStroke(reader, context, syntax, cornerX, cornerY, element, stroke.Value, frame, state);
            return SvgOrthogonalShapeParser.ConcatParsedGeometries(parsed, stroked, state.Budget, context.WorkCounter, context.CancellationToken);
        }
        catch { parsed.Discard(); throw; }

        static SvgParsedGeometry LowerPaintedRoundedStroke(XmlReader reader, SvgPaintableElementContext context, SvgRectangleSyntax syntax, double cornerX, double cornerY, SvgResolvedPaintableElement fillElement, SvgParsedGeometry filled, SvgLinearGradient outlineGradient, SvgRadialGradient outlineCircle, SvgPatternTile outlineTile, bool useLinear, bool useRadial, SvgViewportFrame? frame, SvgDocumentState state)
        {
            SvgComputedStyle whiteStyle = fillElement.Style with { Stroke = "#ffffff" };
            SvgResolvedPaintableElement whiteShape = context.ResolveShape(whiteStyle, reader, true);
            SvgStrokeStyleSyntax.SvgAdmittedStroke? admitted = SvgStrokeStyleSyntax.ResolveAdmittedStroke(reader, whiteStyle, whiteShape, "rect", false);
            if (admitted is null) return filled;
            if (admitted.Value.Join != SvgStrokeJoin.Miter && admitted.Value.Join != SvgStrokeJoin.Round) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only miter and round stroke linejoins are implemented on rectangle strokes.");
            byte strokeOpacity = SvgReferenceResolver.MultiplyAlpha(whiteShape.Presentation.Opacity, SvgColorParser.ParseOpacity(whiteShape.Style.StrokeOpacity));
            if (strokeOpacity != 255
                || (useLinear && !SvgGradientSyntax.IsOpaqueGradientStroke(outlineGradient, 255))
                || (useRadial && !SvgGradientSyntax.IsOpaqueRadialStroke(outlineCircle, 255))
                || (!useLinear && !useRadial && outlineTile.Content is SvgGeometry paintContent && paintContent.Rectangle.Color.Alpha != 255)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Translucent paint strokes on rounded rectangles are not implemented.");
            SvgLinearGradient mappedLinear = default;
            SvgRadialGradient mappedCircle = default;
            if (useLinear)
            {
                mappedLinear = SvgGradientSyntax.MapUserSpaceVector(whiteShape.Transform, outlineGradient, frame);
                mappedLinear = SvgGradientSyntax.MirrorBoundingBoxVector(whiteShape.Transform, mappedLinear);
            }
            else if (useRadial)
            {
                mappedCircle = SvgGradientSyntax.MapUserSpaceRadial(whiteShape.Transform, outlineCircle, frame);
            }
            bool needsStrokeBox = (useLinear && !outlineGradient.IsUserSpace) || (useRadial && !outlineCircle.IsUserSpace);
            SvgRectangle strokeBox = default;
            if (needsStrokeBox)
            {
                SvgRectangle strokePlacement = whiteShape.Transform.Apply(new SvgRectangle(syntax.X, syntax.Y, syntax.Width, syntax.Height, default, true, null));
                strokeBox = frame is null ? strokePlacement : frame.Value.Shift(strokePlacement);
            }
            SvgParsedGeometry white;
            try { white = LowerRoundedStroke(reader, context, syntax, cornerX, cornerY, whiteShape, admitted.Value, frame, state); }
            catch { filled.Dispose(); throw; }
            bool whiteReleased = false;
            try
            {
                using BudgetedGeometryBuilder repaintList = new(state.Budget, SvgMemoryCategory.Geometry, context.WorkCounter, context.CancellationToken);
                foreach (SvgGeometry existing in white.Geometries)
                {
                    if (useLinear) repaintList.Add(existing.TransferAuthoredPathTo(SvgGradientPolygon.RepaintOutline(existing.Polygon, mappedLinear, strokeBox, strokeOpacity, state.Budget)));
                    else if (useRadial) repaintList.Add(existing.TransferAuthoredPathTo(SvgRadialPolygon.RepaintOutline(existing.Polygon, mappedCircle, strokeBox, strokeOpacity, state.Budget)));
                    else repaintList.Add(existing.TransferAuthoredPathTo(SvgPatternPolygon.RepaintOutline(existing.Polygon, outlineTile, strokeOpacity, state.Budget)));
                }
                int whiteCount = white.ElementCount;
                white.Dispose();
                whiteReleased = true;
                SvgParsedGeometry painted = new(repaintList.Detach(), whiteCount);
                return SvgOrthogonalShapeParser.ConcatParsedGeometries(filled, painted, state.Budget, context.WorkCounter, context.CancellationToken);
            }
            catch
            {
                if (!whiteReleased) white.Discard();
                filled.Discard();
                throw;
            }
        }
        static SvgParsedGeometry LowerRoundedStroke(XmlReader reader, SvgPaintableElementContext context, SvgRectangleSyntax syntax, double cornerX, double cornerY, SvgResolvedPaintableElement element, SvgStrokeStyleSyntax.SvgAdmittedStroke stroke, SvgViewportFrame? frame, SvgDocumentState state)
        {
            using BudgetedParserList<SvgPoint> noStrokePoints = new(state.Budget, SvgMemoryCategory.Geometry, context.WorkCounter, context.CancellationToken);
            using BudgetedParserList<int> noStrokeEnds = new(state.Budget, SvgMemoryCategory.Geometry, context.WorkCounter, context.CancellationToken);
            using SvgOrthogonalGeometryLowering strokeLowering = new(noStrokePoints, noStrokeEnds, "rect", false, element.Style.FillRule is not null && element.Style.FillRule.Equals("evenodd", StringComparison.OrdinalIgnoreCase), int.MaxValue, state.Budget, context.WorkCounter, context.CancellationToken);
            return strokeLowering.LowerRoundedRectStroke(syntax.X, syntax.Y, syntax.Width, syntax.Height, cornerX, cornerY, element, stroke, frame, state.Budget);
        }
    }

    /// <summary>Lowers one rounded-rectangle definition through the shared fill-plus-outline path with no viewport frame; use instances map the retained geometries.</summary>
    internal static SvgParsedGeometry ParseRoundedRectangleDefinition(XmlReader reader, SvgPaintableElementContext context, SvgDocumentState state)
    {
        SvgRecordedPaintStyle source = SvgPaintRecording.Resolve(reader, context, state, null);
        context = context with { DeferPaintOpacity = true };
        SvgParsedGeometry parsed = ParseRoundedRectangleDefinitionCore(reader, context, state);
        return SvgPaintRecording.Attach(parsed, source, reader.LocalName, state, context.WorkCounter, context.CancellationToken);
    }

    internal static SvgParsedGeometry ParseRoundedRectangleDefinitionCore(XmlReader reader, SvgPaintableElementContext context, SvgDocumentState state)
    {
        SvgRectangleSyntax syntax = SvgRectangleSyntax.Parse(reader, context.LengthBases);
        double cornerX = syntax.CornerX ?? syntax.CornerY ?? 0;
        double cornerY = syntax.CornerY ?? syntax.CornerX ?? 0;
        return LowerRoundedRect(reader, context, syntax, cornerX, cornerY, null, state);
    }

    internal static SvgParsedRectangle ParseRectangleWithStyle(XmlReader reader, SvgPaintableElementContext context)
    {
        SvgRectangleSyntax syntax = SvgRectangleSyntax.Parse(reader, context.LengthBases);
        SvgStrokeStyleSyntax.RequireButtCapJoin(reader, "rectangle", context.WorkCounter, context.CancellationToken);
        if ((syntax.CornerX ?? syntax.CornerY ?? 0) != 0 || (syntax.CornerY ?? syntax.CornerX ?? 0) != 0) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Rounded rectangles in definitions stay unsupported.");
        SvgResolvedPaintableElement element = context.ResolveShape(reader, true);
        SvgAffineTransform localTransform = SvgGeometrySyntax.ParseTransform(reader.GetAttribute("transform"), context.WorkCounter, context.CancellationToken);
        SvgRectangle rectangle = localTransform.Apply(new SvgRectangle(syntax.X, syntax.Y, syntax.Width, syntax.Height, element.Presentation.Color, element.Presentation.HasFill, null));
        SvgStroke? stroke = ParseRectangleStroke(element.Presentation.StrokeColor, element.Style.Width ?? syntax.StrokeWidth, localTransform, rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height, element.Presentation.Opacity, rectangle.HasFill);
        if (stroke is not null && SvgStrokeStyleSyntax.ParseDasharray(element.Style.Dasharray) is not null) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only solid (none) stroke dash arrays are implemented.");
        SvgSolidPaint? solidPaint = element.Style.GetOpacity().Value == 1 ? SvgPaintResolver.ResolveSolidPaint(element.Presentation.Paint) : null;
        return new SvgParsedRectangle(SvgGeometry.FromRectangle(rectangle with { Stroke = element.Presentation.IsPainted ? stroke : null }) with { SolidPaint = solidPaint }, element.Style);
    }

    internal static SvgParsedGeometry ParseCircle(XmlReader reader, SvgPaintableElementContext context, SvgViewportFrame? frame, SvgDocumentState state)
    {
        SvgRecordedPaintStyle source = SvgPaintRecording.Resolve(reader, context, state, frame);
        context = context with { DeferPaintOpacity = true };
        SvgParsedGeometry parsed = ParseCircleCore(reader, context, frame, state);
        return SvgPaintRecording.Attach(parsed, source, reader.LocalName, state, context.WorkCounter, context.CancellationToken);
    }

    internal static SvgParsedGeometry ParseCircleCore(XmlReader reader, SvgPaintableElementContext context, SvgViewportFrame? frame, SvgDocumentState state)
    {
        SvgEllipseSyntax syntax = SvgEllipseSyntax.ParseCircle(reader, context.LengthBases);
        return LowerEllipseShape(reader, context, syntax, "circle", frame, state);
    }

    internal static SvgParsedGeometry ParseEllipse(XmlReader reader, SvgPaintableElementContext context, SvgViewportFrame? frame, SvgDocumentState state)
    {
        SvgRecordedPaintStyle source = SvgPaintRecording.Resolve(reader, context, state, frame);
        context = context with { DeferPaintOpacity = true };
        SvgParsedGeometry parsed = ParseEllipseCore(reader, context, frame, state);
        return SvgPaintRecording.Attach(parsed, source, reader.LocalName, state, context.WorkCounter, context.CancellationToken);
    }

    internal static SvgParsedGeometry ParseEllipseCore(XmlReader reader, SvgPaintableElementContext context, SvgViewportFrame? frame, SvgDocumentState state)
    {
        SvgEllipseSyntax syntax = SvgEllipseSyntax.ParseEllipse(reader, context.LengthBases);
        return LowerEllipseShape(reader, context, syntax, "ellipse", frame, state);
    }

    private static SvgParsedGeometry LowerEllipseShape(XmlReader reader, SvgPaintableElementContext context, SvgEllipseSyntax syntax, string elementName, SvgViewportFrame? frame, SvgDocumentState state)
    {
        SvgComputedStyle style = context.ResolveStyle(reader);
        SvgLinearGradient strokeGradient = default;
        SvgRadialGradient strokeCircle = default;
        SvgPatternTile strokeTile = default;
        bool paintStrokeLinear = SvgOrthogonalShapeParser.TryResolveOutlineGradient(style, state, out strokeGradient);
        bool paintStrokeRadial = SvgOrthogonalShapeParser.TryResolveOutlineRadial(style, state, out strokeCircle);
        bool paintStrokePattern = SvgOrthogonalShapeParser.TryResolveOutlinePattern(style, state, out strokeTile);
        SvgPatternTile tile = default;
        bool paintPattern = SvgPatternSyntax.IsPatternFill(style.Fill) && state.Patterns.TryGetValue(SvgPatternSyntax.ParsePatternId(style.Fill), out tile);
        bool paintGradient = SvgGradientSyntax.IsGradientFill(style.Fill) && !paintPattern;
        if (SvgGradientSyntax.IsGradientStrokeUrl(style.Stroke) && !paintStrokeLinear && !paintStrokeRadial && !paintStrokePattern && !SvgStrokeStyleSyntax.IsZeroStrokeWidth(style.Width)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Gradient strokes on " + elementName + " elements are not implemented.");
        // A zero width paints no stroke, so paint-filled ellipses and circles fall through to the fill-only path below.
        SvgComputedStyle fillStyle = paintGradient || paintPattern ? style with { Fill = "#ffffff" } : style;
        if (paintStrokeLinear || paintStrokeRadial || paintStrokePattern) fillStyle = fillStyle with { Stroke = "none" };
        SvgResolvedPaintableElement element = context.ResolveShape(fillStyle, reader, elementName is "circle" or "ellipse");
        SvgAffineTransform transform = element.Transform;
        if (paintPattern)
        {
            tile = SvgPatternReferenceResolver.ForPlacement(SvgPatternSyntax.ParsePatternId(style.Fill), tile, transform,
                state, context.WorkCounter, context.CancellationToken);
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
            opacity = SvgReferenceResolver.MultiplyAlpha(style.GetOpacity().Alpha, SvgColorParser.ParseOpacity(style.FillOpacity));
            if (!isLinear && !transform.IsAxisAligned)
            {
                float left = (float)syntax.CenterX - (float)syntax.RadiusX, top = (float)syntax.CenterY - (float)syntax.RadiusY;
                float right = (float)syntax.CenterX + (float)syntax.RadiusX, bottom = (float)syntax.CenterY + (float)syntax.RadiusY;
                box = new(left, top, right - left, bottom - top, default, true, null);
                SvgAffineTransform sceneMap = frame is {} parent ? SvgAffineTransform.FromScaleOffset(parent.ScaleX, parent.ScaleY, parent.OffsetX, parent.OffsetY).ComposeChecked(transform) : transform;
                radial = SvgGradientSyntax.BindRadialSourceMap(radial, box, sceneMap, context.WorkCounter, context.CancellationToken);
            }
            else
            {
                if (!isLinear) radial = SvgGradientSyntax.MapUserSpaceRadial(transform, radial, frame);
                SvgRectangle placement = transform.Apply(new SvgRectangle(syntax.CenterX - syntax.RadiusX, syntax.CenterY - syntax.RadiusY, 2 * syntax.RadiusX, 2 * syntax.RadiusY, default, true, null));
                box = frame is null ? placement : frame.Value.Shift(placement);
            }
        }
        using BudgetedParserList<SvgPoint> noPoints = new(state.Budget, SvgMemoryCategory.Geometry, context.WorkCounter, context.CancellationToken);
        using BudgetedParserList<int> noEnds = new(state.Budget, SvgMemoryCategory.Geometry, context.WorkCounter, context.CancellationToken);
        using SvgOrthogonalGeometryLowering lowering = new(noPoints, noEnds, elementName, false, element.Style.FillRule is not null && element.Style.FillRule.Equals("evenodd", StringComparison.OrdinalIgnoreCase), int.MaxValue, state.Budget, context.WorkCounter, context.CancellationToken);
        SvgParsedGeometry filled = lowering.LowerEllipseFill(syntax.CenterX, syntax.CenterY, syntax.RadiusX, syntax.RadiusY, element, frame, state.Budget);
        try
        {
            if (paintGradient || paintPattern)
            {
                SvgGeometry repainted;
                if (paintPattern) repainted = SvgPatternPolygon.RepaintOutline(filled.Geometries[0].Polygon, tile, opacity, frame is {} viewport ? SvgAffineTransform.FromScaleOffset(viewport.ScaleX, viewport.ScaleY, viewport.OffsetX, viewport.OffsetY).ComposeChecked(transform) : transform, state.Budget);
                else repainted = isLinear
                    ? SvgGradientPolygon.RepaintOutline(filled.Geometries[0].Polygon, linear, box, opacity, state.Budget)
                    : SvgRadialPolygon.RepaintOutline(filled.Geometries[0].Polygon, radial, box, opacity, state.Budget);
                repainted = filled.Geometries[0].TransferAuthoredPathTo(repainted);
                filled.Dispose();
                // Repaint the fill, then retain the independently styled stroke
                // through the ordinary solid/dashed/painted outline machinery.
                filled = RetainSingleGeometry(repainted, state, context);
            }
            bool nonScalingStroke = element.Style.NonScalingStroke;
            if (elementName != "circle")
            {
                if (paintStrokeLinear || paintStrokeRadial || paintStrokePattern)
                    return LowerPaintedCircleStroke(reader, context, syntax, elementName, false, element, filled, strokeGradient, strokeCircle, strokeTile, paintStrokeLinear, paintStrokeRadial, nonScalingStroke, frame, state);
                SvgStroke? ellipseStroke = ParseCircleStroke(element, reader);
                if (ellipseStroke is null || !element.Presentation.IsPainted) return filled;
                string? ellipseDashes = element.Style.Dasharray;
                if (ellipseDashes is not null && !ellipseDashes.AsSpan().Trim().Equals("none".AsSpan(), StringComparison.OrdinalIgnoreCase))
                {
                    double[] ellipseDashPattern = SvgStrokeStyleSyntax.ParseDasharray(ellipseDashes) ?? throw new SvgException(SvgErrorCode.MalformedInput, "Ellipse dash arrays cannot be empty.");
                    double ellipseDashOffset = SvgStrokeStyleSyntax.ParseDashoffset(element.Style.Dashoffset);
                    double? ellipseDashPathLength = SvgStrokeStyleSyntax.ParsePathLength(element.Style.PathLength ?? reader.GetAttribute("pathLength"));
                    double ellipseDashMiterLimit = SvgStrokeStyleSyntax.ParseMiterLimit(element.Style.Miterlimit ?? reader.GetAttribute("stroke-miterlimit"));
                    try { return LowerNativeEllipseStroke(context, syntax, element, filled, ellipseStroke.Value, ellipseDashPattern, ellipseDashOffset, ellipseDashPathLength, ellipseDashMiterLimit, nonScalingStroke, "ellipse", frame, state); }
                    catch { filled.Dispose(); throw; }
                }
                try
                {
                    using SvgParsedGeometry ring = lowering.LowerEllipseStrokeFill(syntax.CenterX, syntax.CenterY, syntax.RadiusX, syntax.RadiusY, element, ellipseStroke.Value, nonScalingStroke, frame, state.Budget);
                    return ConcatGeometries(filled, ring, state.Budget, context.WorkCounter, context.CancellationToken);
                }
                catch { filled.Dispose(); throw; }
            }
            if (paintStrokeLinear || paintStrokeRadial || paintStrokePattern)
                return LowerPaintedCircleStroke(reader, context, syntax, elementName, true, element, filled, strokeGradient, strokeCircle, strokeTile, paintStrokeLinear, paintStrokeRadial, nonScalingStroke, frame, state);
            SvgStroke? stroke = ParseCircleStroke(element, reader);
            if (stroke is null || !element.Presentation.IsPainted) return filled;
            string? circleDashes = element.Style.Dasharray;
            if (circleDashes is not null && !circleDashes.AsSpan().Trim().Equals("none".AsSpan(), StringComparison.OrdinalIgnoreCase))
            {
                double[] dashPattern = SvgStrokeStyleSyntax.ParseDasharray(circleDashes) ?? throw new SvgException(SvgErrorCode.MalformedInput, "Circle dash arrays cannot be empty.");
                double dashOffset = SvgStrokeStyleSyntax.ParseDashoffset(element.Style.Dashoffset);
                double? dashPathLength = SvgStrokeStyleSyntax.ParsePathLength(element.Style.PathLength ?? reader.GetAttribute("pathLength"));
                double dashMiterLimit = SvgStrokeStyleSyntax.ParseMiterLimit(element.Style.Miterlimit ?? reader.GetAttribute("stroke-miterlimit"));
                try { return LowerNativeEllipseStroke(context, syntax, element, filled, stroke.Value, dashPattern, dashOffset, dashPathLength, dashMiterLimit, nonScalingStroke, "circle", frame, state); }
                catch { filled.Dispose(); throw; }
            }
            try
            {
                using SvgParsedGeometry ring = lowering.LowerCircleStrokeFill(syntax.CenterX, syntax.CenterY, syntax.RadiusX, element, stroke.Value, nonScalingStroke, frame, state.Budget);
                return ConcatGeometries(filled, ring, state.Budget, context.WorkCounter, context.CancellationToken);
            }
            catch { filled.Dispose(); throw; }
        }
        catch { filled.Discard(); throw; }

        static SvgParsedGeometry LowerNativeEllipseStroke(SvgPaintableElementContext context, SvgEllipseSyntax syntax, SvgResolvedPaintableElement shape, SvgParsedGeometry fill, SvgStroke ringStroke, double[] dashes, double dashOffset, double? pathLength, double miterLimit, bool nonScaling, string shapeName, SvgViewportFrame? frame, SvgDocumentState state)
        {
            using BudgetedParserList<SvgPoint> noStrokePoints = new(state.Budget, SvgMemoryCategory.Geometry, context.WorkCounter, context.CancellationToken);
            using BudgetedParserList<int> noStrokeEnds = new(state.Budget, SvgMemoryCategory.Geometry, context.WorkCounter, context.CancellationToken);
            using SvgOrthogonalGeometryLowering strokeLowering = new(noStrokePoints, noStrokeEnds, shapeName, false, shape.Style.FillRule is not null && shape.Style.FillRule.Equals("evenodd", StringComparison.OrdinalIgnoreCase), int.MaxValue, state.Budget, context.WorkCounter, context.CancellationToken);
            SvgParsedGeometry dashed;
            try { dashed = strokeLowering.LowerNativeEllipseStroke(syntax.CenterX, syntax.CenterY, syntax.RadiusX, syntax.RadiusY, shape, ringStroke, dashes, dashOffset, pathLength, miterLimit, nonScaling, frame, state.Budget); }
            catch { fill.Dispose(); throw; }
            return ConcatGeometries(fill, dashed, state.Budget, context.WorkCounter, context.CancellationToken);
        }
        static SvgParsedGeometry LowerPaintedCircleStroke(XmlReader reader, SvgPaintableElementContext context, SvgEllipseSyntax syntax, string shapeName, bool isCircle, SvgResolvedPaintableElement fillElement, SvgParsedGeometry fill, SvgLinearGradient outlineGradient, SvgRadialGradient outlineCircle, SvgPatternTile outlineTile, bool useLinear, bool useRadial, bool nonScaling, SvgViewportFrame? frame, SvgDocumentState state)
        {
            string? paintDashes = fillElement.Style.Dasharray;
            double[]? dashPattern = SvgStrokeStyleSyntax.ParseDasharray(paintDashes);
            SvgComputedStyle whiteStyle = fillElement.Style with { Stroke = "#ffffff" };
            SvgResolvedPaintableElement whiteShape = context.ResolveShape(whiteStyle, reader, true);
            SvgStroke? admitted = ParseCircleStroke(whiteShape, reader);
            if (admitted is null || !whiteShape.Presentation.IsPainted) return fill;
            byte strokeOpacity = SvgReferenceResolver.MultiplyAlpha(whiteShape.Presentation.Opacity, SvgColorParser.ParseOpacity(whiteShape.Style.StrokeOpacity));
            if (strokeOpacity != 255
                || (useLinear && !SvgGradientSyntax.IsOpaqueGradientStroke(outlineGradient, 255))
                || (useRadial && !SvgGradientSyntax.IsOpaqueRadialStroke(outlineCircle, 255))
                || (!useLinear && !useRadial && outlineTile.Content is SvgGeometry paintContent && paintContent.Rectangle.Color.Alpha != 255)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Translucent paint strokes on " + shapeName + " elements are not implemented.");
            SvgLinearGradient mappedLinear = default;
            SvgRadialGradient mappedCircle = default;
            bool sourceMapped = !whiteShape.Transform.IsAxisAligned || useLinear && outlineGradient.RequiresSourceMap;
            if (useLinear && !sourceMapped)
            {
                mappedLinear = SvgGradientSyntax.MapUserSpaceVector(whiteShape.Transform, outlineGradient, frame);
                mappedLinear = SvgGradientSyntax.MirrorBoundingBoxVector(whiteShape.Transform, mappedLinear);
            }
            else if (useRadial && !sourceMapped)
            {
                mappedCircle = SvgGradientSyntax.MapUserSpaceRadial(whiteShape.Transform, outlineCircle, frame);
            }
            bool needsStrokeBox = (useLinear && !outlineGradient.IsUserSpace) || (useRadial && !outlineCircle.IsUserSpace);
            SvgRectangle strokeBox = default;
            SvgAffineTransform strokePlacement = frame is {} strokeViewport
                ? SvgAffineTransform.FromScaleOffset(strokeViewport.ScaleX, strokeViewport.ScaleY, strokeViewport.OffsetX, strokeViewport.OffsetY).ComposeChecked(whiteShape.Transform)
                : whiteShape.Transform;
            if (sourceMapped)
            {
                SvgRectangle sourceBox = new(syntax.CenterX - syntax.RadiusX, syntax.CenterY - syntax.RadiusY, 2 * syntax.RadiusX, 2 * syntax.RadiusY, default, false, null);
                if (useLinear) mappedLinear = SvgGradientSyntax.BindLinearSourceMap(outlineGradient, sourceBox, strokePlacement, state, context.WorkCounter, context.CancellationToken);
                else if (useRadial) mappedCircle = SvgGradientSyntax.BindRadialSourceMap(outlineCircle, sourceBox, strokePlacement, context.WorkCounter, context.CancellationToken);
                needsStrokeBox = false;
            }
            if (needsStrokeBox)
            {
                SvgRectangle placedBox = whiteShape.Transform.Apply(new SvgRectangle(syntax.CenterX - syntax.RadiusX, syntax.CenterY - syntax.RadiusY, 2 * syntax.RadiusX, 2 * syntax.RadiusY, default, true, null));
                strokeBox = frame is null ? placedBox : frame.Value.Shift(placedBox);
            }
            SvgParsedGeometry white;
            try
            {
                using BudgetedParserList<SvgPoint> noStrokePoints = new(state.Budget, SvgMemoryCategory.Geometry, context.WorkCounter, context.CancellationToken);
                using BudgetedParserList<int> noStrokeEnds = new(state.Budget, SvgMemoryCategory.Geometry, context.WorkCounter, context.CancellationToken);
                using SvgOrthogonalGeometryLowering strokeLowering = new(noStrokePoints, noStrokeEnds, shapeName, false, whiteShape.Style.FillRule is not null && whiteShape.Style.FillRule.Equals("evenodd", StringComparison.OrdinalIgnoreCase), int.MaxValue, state.Budget, context.WorkCounter, context.CancellationToken);
                white = dashPattern is not null
                    ? strokeLowering.LowerNativeEllipseStroke(syntax.CenterX, syntax.CenterY, syntax.RadiusX, syntax.RadiusY, whiteShape, admitted.Value, dashPattern,
                        SvgStrokeStyleSyntax.ParseDashoffset(whiteShape.Style.Dashoffset), SvgStrokeStyleSyntax.ParsePathLength(whiteShape.Style.PathLength ?? reader.GetAttribute("pathLength")), SvgStrokeStyleSyntax.ParseMiterLimit(whiteShape.Style.Miterlimit ?? reader.GetAttribute("stroke-miterlimit")), nonScaling, frame, state.Budget)
                    : isCircle
                    ? strokeLowering.LowerCircleStrokeFill(syntax.CenterX, syntax.CenterY, syntax.RadiusX, whiteShape, admitted.Value, nonScaling, frame, state.Budget)
                    : strokeLowering.LowerEllipseStrokeFill(syntax.CenterX, syntax.CenterY, syntax.RadiusX, syntax.RadiusY, whiteShape, admitted.Value, nonScaling, frame, state.Budget);
            }
            catch { fill.Dispose(); throw; }
            bool whiteReleased = false;
            try
            {
                using BudgetedGeometryBuilder repaintList = new(state.Budget, SvgMemoryCategory.Geometry, context.WorkCounter, context.CancellationToken);
                foreach (SvgGeometry existing in white.Geometries)
                {
                    if (useLinear) repaintList.Add(existing.TransferAuthoredPathTo(SvgGradientPolygon.RepaintOutline(existing.Polygon, mappedLinear, strokeBox, strokeOpacity, state.Budget)));
                    else if (useRadial) repaintList.Add(existing.TransferAuthoredPathTo(SvgRadialPolygon.RepaintOutline(existing.Polygon, mappedCircle, strokeBox, strokeOpacity, state.Budget)));
                    else repaintList.Add(existing.TransferAuthoredPathTo(SvgPatternPolygon.RepaintOutline(existing.Polygon, outlineTile, strokeOpacity, strokePlacement, state.Budget)));
                }
                int whiteCount = white.ElementCount;
                white.Dispose();
                whiteReleased = true;
                SvgParsedGeometry painted = new(repaintList.Detach(), whiteCount);
                return ConcatGeometries(fill, painted, state.Budget, context.WorkCounter, context.CancellationToken);
            }
            catch
            {
                if (!whiteReleased) white.Discard();
                fill.Discard();
                throw;
            }
        }
        static SvgParsedGeometry ConcatGeometries(SvgParsedGeometry first, SvgParsedGeometry second, RenderBudget budget, ParserWorkCounter workCounter, CancellationToken cancellationToken)
        {
            try
            {
                using BudgetedParserList<SvgGeometry> combined = new(budget, SvgMemoryCategory.Geometry, workCounter, cancellationToken);
                combined.AddRange(first.Geometries);
                foreach (SvgGeometry stroke in second.Geometries) combined.Add(stroke with { RecordedPaint = SvgRecordedPaint.SourceStroke });
                SvgParsedGeometry result = new(combined.Detach(), first.ElementCount + second.ElementCount);
                first.Dispose();
                second.Dispose();
                return result;
            }
            catch
            {
                first.Discard();
                second.Discard();
                throw;
            }
        }
    }

    private static SvgStroke? ParseCircleStroke(SvgResolvedPaintableElement element, XmlReader reader)
    {
        if (element.Presentation.StrokeColor is null)
        {
            return null;
        }
        _ = SvgStrokeStyleSyntax.ParseMiterLimit(element.Style.Miterlimit ?? reader.GetAttribute("stroke-miterlimit"));
        double width = SvgStrokeStyleSyntax.ParseStrokeWidth(element.Style.Width ?? reader.GetAttribute("stroke-width") ?? "1", "circle");
        if (width < 0) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only positive circle stroke widths are implemented.");
        if (width == 0) return null;
        return new SvgStroke(SvgPaintResolver.ApplyOpacity(element.Presentation.StrokeColor.Value, element.Presentation.Opacity), width);
    }


    internal static SvgParsedGeometry ParseLine(XmlReader reader, SvgPaintableElementContext context, SvgViewportFrame? frame, SvgDocumentState state)
    {
        SvgRecordedPaintStyle source = SvgPaintRecording.Resolve(reader, context, state, frame);
        SvgMarkerStyle markers = SvgMarkerStyle.Resolve(context.ResolveStyle(reader), context.InheritedPresentation.Markers);
        context = context with { DeferPaintOpacity = true };
        SvgParsedGeometry parsed = ParseLineCore(reader, context, frame, state);
        try
        {
            if (parsed.Geometries.Length != 0)
                source = source with { VisualBounds = LineVisualBounds(reader, context, frame) };
        }
        catch { parsed.Discard(); throw; }
        SvgParsedGeometry recorded = SvgPaintRecording.Attach(parsed, markers.HasMarkers ? source with { ElementOpacity = SvgPaintOpacity.Opaque } : source, reader.LocalName, state, context.WorkCounter, context.CancellationToken);
        return SvgMarkerRenderer.AttachLine(recorded, reader, context, source, frame, state, markers);

        static SvgPaintBounds? LineVisualBounds(XmlReader input, SvgPaintableElementContext element, SvgViewportFrame? viewport)
        {
            SvgComputedStyle style = element.ResolveStyle(input);
            if (SvgStrokeStyleSyntax.ParseLinecap(style.Linecap) != SvgStrokeCap.Square) return null;
            element.WorkCounter.Consume(element.CancellationToken);
            SvgLineSyntax line = SvgLineSyntax.Parse(input);
            float halfWidth = (float)SvgStrokeStyleSyntax.ParseStrokeWidth(style.Width ?? line.StrokeWidth ?? "1", "line") / 2f;
            // A square tip's diagonal bounds the source stroke before dash segmentation.
            float inflation = halfWidth * MathF.Sqrt(2);
            // Recording rounds the local visual bounds before the output viewport transform.
            SvgPaintBounds bounds = new(MathF.Floor(Math.Min(line.X1, line.X2) - inflation), MathF.Floor(Math.Min(line.Y1, line.Y2) - inflation),
                MathF.Ceiling(Math.Max(line.X1, line.X2) + inflation), MathF.Ceiling(Math.Max(line.Y1, line.Y2) + inflation));
            return viewport is {} frame
                ? bounds.Map(SvgAffineTransform.FromScaleOffset(frame.ScaleX, frame.ScaleY, frame.OffsetX, frame.OffsetY), element.WorkCounter, element.CancellationToken)
                : bounds;
        }
    }

    internal static SvgParsedGeometry ParseLineCore(XmlReader reader, SvgPaintableElementContext context, SvgViewportFrame? frame, SvgDocumentState state)
    {
        SvgLineSyntax syntax = SvgLineSyntax.Parse(reader);
        SvgStrokeStyleSyntax.RequireLineCapJoin(reader, "line", context.WorkCounter, context.CancellationToken);
        SvgComputedStyle style = context.ResolveStyle(reader);
        style = style with { Stroke = style.Stroke ?? context.InheritedStroke.Stroke };
        if (SvgGradientSyntax.IsGradientStrokeUrl(style.Stroke)) return LowerNativePaintStrokeLine(reader, context, style, syntax, state, frame);
        // A line has no fill operation, independently of its inherited fill server.
        if (SvgPatternSyntax.IsPatternFill(style.Fill)) style = style with { Fill = "none" };
        SvgResolvedPaintableElement element = context.ResolveShape(style, reader, true);
        double lineWidth = SvgStrokeStyleSyntax.ParseStrokeWidth(style.Width ?? syntax.StrokeWidth ?? "1", "line");
        if (context.RetainInstanceStrokePath || NeedsSourceBounds() || element.Transform != SvgAffineTransform.Identity || frame is {} scaled && scaled.IsScaled || syntax.X1 != syntax.X2 && syntax.Y1 != syntax.Y2 || syntax.X1 == syntax.X2 && syntax.Y1 == syntax.Y2 || lineWidth % 2 != 0 || element.Presentation.StrokeColor is null)
            return LowerNativeLine();
        SvgColor? strokeColor = element.Presentation.StrokeColor;
        int strokeWidth = SvgGeometrySyntax.ParseInteger(style.Width ?? syntax.StrokeWidth ?? "1");
        if (strokeWidth == 0)
        {
            using BudgetedParserList<SvgGeometry> none = new(state.Budget, SvgMemoryCategory.Geometry, context.WorkCounter, context.CancellationToken);
            return new SvgParsedGeometry(none.Detach(), 0);
        }

        if (strokeWidth < 0) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only positive line stroke widths are implemented.");
        SvgStrokeCap linecap = SvgStrokeStyleSyntax.ParseLinecap(style.Linecap);
        double[]? dashes = SvgStrokeStyleSyntax.ParseDasharray(style.Dasharray);
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
        double dashOffset = SvgStrokeStyleSyntax.ParseDashoffset(style.Dashoffset);
        double? pathLength = SvgStrokeStyleSyntax.ParsePathLength(style.PathLength ?? reader.GetAttribute("pathLength"));
        if (linecap == SvgStrokeCap.Square && color.Alpha != 255) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Translucent square-capped dashed lines are not implemented.");
        return LowerDashedLine();

        bool NeedsSourceBounds()
        {
            // Forward clip definitions can still select normalized units. Keep
            // original endpoints before caps, dashes or viewport clipping.
            if (state.DeferringLeafClip) return true;
            foreach (PendingGroupClip pending in state.PendingGroupClips.AsSpan())
            {
                context.WorkCounter.Consume(context.CancellationToken);
                if (pending.EndCommandIndex < 0 && (!state.ClipPolygons.TryGetValue(pending.ClipId, out SvgClipPolygon clip) || clip.IsObjectBoundingBox)) return true;
            }
            return false;
        }

        SvgParsedGeometry LowerNativeLine()
        {
            SvgStrokeStyleSyntax.SvgAdmittedStroke? stroke = SvgStrokeStyleSyntax.ResolveAdmittedStroke(reader, style, element, "line", true);
            using BudgetedParserList<SvgPoint> noPoints = new(state.Budget, SvgMemoryCategory.Geometry, context.WorkCounter, context.CancellationToken);
            using BudgetedParserList<int> noEnds = new(state.Budget, SvgMemoryCategory.Geometry, context.WorkCounter, context.CancellationToken);
            if (stroke is null)
            {
                using BudgetedParserList<SvgGeometry> empty = new(state.Budget, SvgMemoryCategory.Geometry, context.WorkCounter, context.CancellationToken);
                return new(empty.Detach(), 0);
            }
            using SvgOrthogonalGeometryLowering lowering = new(noPoints, noEnds, "line", false, false, int.MaxValue, state.Budget, context.WorkCounter, context.CancellationToken);
            using RenderBudget.BudgetReservation pathReservation = state.Budget.Reserve(256, SvgMemoryCategory.Parser);
            string path = FormattableString.Invariant($"M{syntax.X1} {syntax.Y1} L{syntax.X2} {syntax.Y2}");
            return lowering.LowerStrokePath(path, 2, false, false, element, stroke.Value.Color, element.Presentation.IsPainted,
                stroke.Value.Width, stroke.Value.MiterLimit, stroke.Value.Cap, stroke.Value.Join, stroke.Value.DashPattern,
                stroke.Value.DashOffset, stroke.Value.PathLength, stroke.Value.NonScalingStroke, frame, state.Budget);

        }

        SvgParsedGeometry LowerRoundCapLine()
        {
            SvgAxisAlignedLine line = new(syntax.X1, syntax.Y1, syntax.X2, syntax.Y2, color, painted, strokeWidth);
            SvgGeometry band = frame is null ? SvgGeometry.FromAxisAlignedLine(line) : frame.Value.ClipShifted(line);
            SvgPaint discPaint = new(color, true, SvgPaintOpacity.Opaque, color, false);
            SvgResolvedPaintableElement discElement = element with { Presentation = new SvgResolvedShapePresentation(discPaint, color, null, 255, painted) };
            double radius = strokeWidth / 2d;
            using BudgetedParserList<SvgPoint> noPoints = new(state.Budget, SvgMemoryCategory.Geometry, context.WorkCounter, context.CancellationToken);
            using BudgetedParserList<int> noEnds = new(state.Budget, SvgMemoryCategory.Geometry, context.WorkCounter, context.CancellationToken);
            using SvgOrthogonalGeometryLowering lowering = new(noPoints, noEnds, "line", false, false, int.MaxValue, state.Budget, context.WorkCounter, context.CancellationToken);
            using SvgParsedGeometry first = lowering.LowerEllipseFill(syntax.X1, syntax.Y1, radius, radius, discElement, frame, state.Budget);
            try
            {
                using SvgParsedGeometry second = lowering.LowerEllipseFill(syntax.X2, syntax.Y2, radius, radius, discElement, frame, state.Budget);
                try
                {
                    using BudgetedParserList<SvgGeometry> all = new(state.Budget, SvgMemoryCategory.Geometry, context.WorkCounter, context.CancellationToken);
                    all.Add(band);
                    all.AddRange(first.Geometries);
                    all.AddRange(second.Geometries);
                    return new(all.Detach(), first.ElementCount + second.ElementCount);
                }
                catch { second.Discard(); throw; }
            }
            catch { first.Discard(); throw; }
        }

        SvgParsedGeometry LowerDashedLine()
        {
            using BudgetedParserList<SvgRectangle> runRects = new(state.Budget, SvgMemoryCategory.Geometry, context.WorkCounter, context.CancellationToken);
            bool paintsAny = AppendLineDashRunRects(syntax, strokeWidth, linecap == SvgStrokeCap.Square, dashes, dashOffset, pathLength, frame, state.Budget, context.WorkCounter, context.CancellationToken, runRects);
            using BudgetedParserList<SvgGeometry> runs = new(state.Budget, SvgMemoryCategory.Geometry, context.WorkCounter, context.CancellationToken);
            for (int runIndex = 0; runIndex < runRects.Count; runIndex++) runs.Add(SvgGeometry.FromOrthogonalBand(runRects[runIndex] with { Color = color, HasFill = paintsAny && painted }));
            return new SvgParsedGeometry(runs.Detach(), 0);
        }
        // Keep line endpoints in source coordinates for native strokes and shared paint binding.
        // The scene owns contours; tile content is borrowed. Existing lowering bounds work/storage
        // and releases partial results on denial or cancellation.
        static SvgParsedGeometry LowerNativePaintStrokeLine(XmlReader reader, SvgPaintableElementContext context, SvgComputedStyle style, SvgLineSyntax syntax, SvgDocumentState state, SvgViewportFrame? frame)
        {
            SvgResolvedPaintableElement white = context.ResolveShape(style with { Fill = "none", Stroke = "#ffffff" }, reader, true);
            SvgStrokeStyleSyntax.SvgAdmittedStroke? stroke = SvgStrokeStyleSyntax.ResolveAdmittedStroke(reader, white.Style, white, "line", true);
            using BudgetedParserList<SvgPoint> noPoints = new(state.Budget, SvgMemoryCategory.Geometry, context.WorkCounter, context.CancellationToken);
            using BudgetedParserList<int> noEnds = new(state.Budget, SvgMemoryCategory.Geometry, context.WorkCounter, context.CancellationToken);
            using SvgOrthogonalGeometryLowering lowering = new(noPoints, noEnds, "line", false, false, int.MaxValue, state.Budget, context.WorkCounter, context.CancellationToken);
            if (stroke is null)
            {
                using BudgetedGeometryBuilder empty = new(state.Budget, SvgMemoryCategory.Geometry, context.WorkCounter, context.CancellationToken);
                return new(empty.Detach(), 0);
            }
            string id = SvgGradientSyntax.ParsePaintId(style.Stroke);
            SvgAffineTransform map = SvgViewportFrame.MapGeometry(white.Transform, frame);
            SvgRectangle box = new(Math.Min(syntax.X1, syntax.X2), Math.Min(syntax.Y1, syntax.Y2), Math.Abs(syntax.X2 - syntax.X1), Math.Abs(syntax.Y2 - syntax.Y1), default, false, null);
            SvgLinearGradient linear = default;
            SvgRadialGradient radial = default;
            SvgPatternTile tile = default;
            bool useLinear = state.Gradients.TryGetValue(id, out linear);
            bool useRadial = state.RadialGradients.TryGetValue(id, out radial);
            if (useLinear) linear = SvgGradientSyntax.BindLinearSourceMap(linear, box, map, state, context.WorkCounter, context.CancellationToken);
            else if (useRadial) radial = SvgGradientSyntax.BindRadialSourceMap(radial, box, map, context.WorkCounter, context.CancellationToken);
            else if (state.Patterns.TryGetValue(id, out tile)) tile = SvgPatternReferenceResolver.ForPlacement(id, tile, map, state, context.WorkCounter, context.CancellationToken);
            else throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only defined local line paints are implemented.");
            using RenderBudget.BudgetReservation textOwner = state.Budget.Reserve(256, SvgMemoryCategory.Parser);
            string path = FormattableString.Invariant($"M{syntax.X1} {syntax.Y1} L{syntax.X2} {syntax.Y2}");
            SvgParsedGeometry outlines = lowering.LowerStrokePath(path, 2, false, false, white, stroke.Value.Color, white.Presentation.IsPainted && !(useLinear && linear.IsEmpty) && !(useRadial && radial.IsEmpty),
                stroke.Value.Width, stroke.Value.MiterLimit, stroke.Value.Cap, stroke.Value.Join, stroke.Value.DashPattern, stroke.Value.DashOffset,
                stroke.Value.PathLength, stroke.Value.NonScalingStroke, frame, state.Budget);
            try
            {
                using BudgetedGeometryBuilder painted = new(state.Budget, SvgMemoryCategory.Geometry, context.WorkCounter, context.CancellationToken);
                foreach (SvgGeometry geometry in outlines.Geometries)
                {
                    context.WorkCounter.Consume(context.CancellationToken);
                    SvgGeometry result = useLinear ? geometry.TransferAuthoredPathTo(SvgGradientPolygon.RepaintOutline(geometry.Polygon, linear, box, white.Presentation.Opacity, state.Budget))
                        : useRadial ? geometry.TransferAuthoredPathTo(SvgRadialPolygon.RepaintOutline(geometry.Polygon, radial, box, white.Presentation.Opacity, state.Budget))
                        : geometry.TransferAuthoredPathTo(SvgPatternPolygon.RepaintOutline(geometry.Polygon, tile, white.Presentation.Opacity, map, state.Budget));
                    try { painted.Add(result); }
                    catch { result.Dispose(); throw; }
                }
                outlines.Dispose();
                return new(painted.Detach(), outlines.ElementCount);
            }
            catch { outlines.Discard(); throw; }
        }
        // <summary>Appends one clipped rectangle per dash-pattern on-run along a horizontal or vertical line, reporting whether any run paints.</summary>
        // <remarks><para>Callers paint over the returned rectangles and leave the single quiet rectangle unpainted, sharing the solid phase convention across paints.</para></remarks>
        static bool AppendLineDashRunRects(SvgLineSyntax syntax, int strokeWidth, bool squareCaps, double[] dashes, double dashOffset, double? pathLength, SvgViewportFrame? frame, RenderBudget budget, ParserWorkCounter workCounter, CancellationToken cancellationToken, BudgetedParserList<SvgRectangle> runs)
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
    }

    private static SvgRectangle ClipStrokedRectangle(SvgRectangle rectangle, SvgStroke? stroke, SvgViewportFrame frame)
    {
        SvgRectangle shifted = frame.Shift(rectangle);
        if (stroke is null) return frame.Clip.ClipRectangle(shifted);
        double halfWidth = stroke.Value.Width / 2d;
        if (!frame.Clip.Contains(shifted.X - halfWidth, shifted.Y - halfWidth, shifted.X + shifted.Width + halfWidth, shifted.Y + shifted.Height + halfWidth)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Rectangle strokes crossing a nested viewport boundary are not implemented.");
        return frame.Clip.ClipRectangle(shifted);
    }

    internal static SvgStroke? ParseRectangleStroke(SvgColor? color, string? widthValue, SvgAffineTransform transform, double x, double y, double width, double height, byte opacity, bool hasFill)
    {
        if (color is null) { return null; }
        if (!transform.IsTranslation || (hasFill && opacity != 255)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only rectangle strokes under translations, with opacity only when fill-less, are implemented.");
        double strokeWidth = SvgStrokeStyleSyntax.ParseStrokeWidth(widthValue, "rectangle");
        if (strokeWidth < 0 || strokeWidth > width || strokeWidth > height) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only rectangle strokes no larger than the rectangle dimensions are implemented.");
        if (strokeWidth == 0) return null;
        return new SvgStroke(SvgPaintResolver.ApplyOpacity(color.Value, opacity), strokeWidth);
    }
}

/// <summary>Pairs a lowered rectangle with the specified style that determines whether a use instance may supply fill.</summary>
internal readonly record struct SvgParsedRectangle(SvgGeometry Geometry, SvgComputedStyle Style);
