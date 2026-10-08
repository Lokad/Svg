using System.Xml;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Parsing;

/// <summary>Retains one admitted pattern tile with its source-coordinate grid while streaming.</summary>
internal readonly record struct SvgPatternTile(SvgGeometry? Content, double OriginX, double OriginY, double TileWidth, double TileHeight, double ShiftX, double ShiftY)
{
    internal SvgVectorContent? VectorContent { get; init; }
    internal bool HasContent => Content is not null || VectorContent is not null;
    /// <summary>Maps source vector tiles once; reconstructed instance tiles carry placement on the paint geometry.</summary>
    internal SvgAffineTransform VectorPlacement => VectorContent is null ? SvgAffineTransform.Identity : SvgAffineTransform.FromScaleOffset(1, 1, ShiftX, ShiftY);
}

/// <summary>Parses the bounded pattern definition syntax and lowers pattern-filled rectangles.</summary>
internal static class SvgPatternSyntax
{
    /// <summary>Retains authored attributes separately from defaults so local templates can fill omissions.</summary>
    internal static SvgPatternTemplate ParsePatternElement(XmlReader reader, SvgDocumentState state)
    {
        string? href = reader.GetAttribute("href") ?? reader.GetAttribute("href", "http://www.w3.org/1999/xlink");
        string? target = null;
        if (href is not null)
        {
            if (!href.StartsWith('#') || href.Length == 1) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only local pattern templates are implemented.");
            if (href.Length > SvgSyntaxLimits.MaximumIdentifierCharacters) throw new SvgException(SvgErrorCode.LimitExceeded, "Pattern template identifiers exceed the syntax limit.");
            target = state.Strings.Copy(href.AsSpan(1));
        }
        string? transform = reader.GetAttribute("patternTransform");
        return new(ParseOptional("x"), ParseOptional("y"), ParseExtent("width"), ParseExtent("height"),
            transform is null ? null : ParsePatternTransform(transform), ParseUnits("patternUnits"), ParseUnits("patternContentUnits"), target);

        double? ParseOptional(string name) => reader.GetAttribute(name) is {} value ? SvgGeometrySyntax.ParseRectangleCoordinate(value) : null;
        double? ParseExtent(string name)
        {
            double? value = ParseOptional(name);
            if (value < 0) throw new SvgException(SvgErrorCode.MalformedInput, "Pattern dimensions cannot be negative.");
            return value;
        }
        bool? ParseUnits(string name) => reader.GetAttribute(name) switch
        {
            null => null,
            "userSpaceOnUse" => true,
            "objectBoundingBox" => false,
            _ => throw new SvgException(SvgErrorCode.UnsupportedFeature, "Pattern units must be userSpaceOnUse or objectBoundingBox."),
        };
    }

    /// <summary>Parses the bounded translate-only pattern transform, retaining fractional phase.</summary>
    internal static (double X, double Y) ParsePatternTransform(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return (0, 0);
        if (value.Length > SvgSyntaxLimits.MaximumTransformCharacters) throw new SvgException(SvgErrorCode.LimitExceeded, "Transform values exceed the configured syntax limit.");
        ReadOnlySpan<char> input = value.AsSpan().Trim();
        int separator = input.IndexOf('(');
        if (separator <= 0 || !input.EndsWith(")") || !input[..separator].Trim().Equals("translate".AsSpan(), StringComparison.OrdinalIgnoreCase))
            throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only translate() pattern transforms are implemented.");
        double first = 0, second = 0;
        int count = 0;
        SvgGeometrySyntax.DelimitedTokenReader tokens = new(input[(separator + 1)..^1]);
        while (tokens.TryReadValue(out ReadOnlySpan<char> token))
        {
            if (count == 2) throw new SvgException(SvgErrorCode.MalformedInput, "Pattern translate() requires one or two coordinates.");
            double parsed = SvgGeometrySyntax.ParseRectangleCoordinate(token.ToString());
            if (count++ == 0) first = parsed;
            else second = parsed;
        }
        if (count == 0) throw new SvgException(SvgErrorCode.MalformedInput, "Pattern translate() requires one or two coordinates.");
        return (first, second);
    }

    internal static string ParsePatternId(string? value)
    {
        if (value is null) throw new SvgException(SvgErrorCode.MalformedInput, "A pattern reference is required.");
        if (!SvgLocalUrl.TryParseId(value, out string id)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only local pattern references are implemented.");
        return id;
    }

    internal static bool IsPatternFill(string? fill)
        => fill is not null && fill.StartsWith("url(", StringComparison.Ordinal);

    /// <summary>Lowers one pattern-filled orthogonal path or point-list shape into pattern-evaluated rectangle bands.</summary>
    internal static SvgParsedGeometry LowerPatternBands(XmlReader reader, SvgPaintableElementContext context, SvgComputedStyle style, SvgDocumentState state, string elementName, bool isPath, int maximumGeometryElements, RenderBudget budget, SvgViewportFrame? frame)
    {
        SvgStrokeStyleSyntax.SvgAdmittedStroke? bandStroke = null;
        SvgResolvedPaintableElement bandStrokeShape = default;
        if (style.Stroke is not null && !style.Stroke.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            // Solid strokes compose over pattern fills through the admitted outline kernel; paint strokes stay gated below.
            bandStrokeShape = context.ResolveShape(style with { Fill = "black" }, reader, true);
            bandStroke = SvgStrokeStyleSyntax.ResolveAdmittedStroke(reader, style, bandStrokeShape, elementName, elementName == "polyline");
        }
        if (!state.Patterns.TryGetValue(ParsePatternId(style.Fill), out SvgPatternTile tile)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only defined local pattern references are implemented.");
        if (frame is not null && frame.Value.IsScaled) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Pattern fills in scaled viewports are not implemented.");
        SvgAffineTransform transform = context.ResolveTransform(reader);
        if (!transform.IsAxisAligned) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only axis-aligned pattern transforms are implemented.");
        if (transform.A < 0 || transform.D < 0) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only non-reflected pattern fills are implemented.");
        bool painted = SvgPresentationResolver.IsPainted(style.Display, style.Visibility, context.InheritedPresentation.IsDisplayed, context.InheritedPresentation.IsVisible);
        byte opacity = SvgReferenceResolver.MultiplyAlpha(SvgColorParser.ParseOpacity(style.Opacity), SvgColorParser.ParseOpacity(style.FillOpacity));
        SvgShapePresentationResolver.ValidateFillRule(style.FillRule);
        bool isEvenOdd = style.FillRule is not null && style.FillRule.Equals("evenodd", StringComparison.OrdinalIgnoreCase);
        if (isPath && painted && tile.HasContent && tile.TileWidth != 0 && tile.TileHeight != 0)
        {
            using SvgRenderablePathData renderData = SvgRenderablePathData.Read(reader.GetAttribute("d"), maximumGeometryElements, budget, context.WorkCounter, context.CancellationToken);
            string d = renderData.Value;
            (_, bool hasCurve, bool hasArc, _) = SvgOrthogonalShapeParser.ScanPathData(d, context.WorkCounter, context.CancellationToken);
            int commandCount = renderData.CommandCount;

            // Retain authored path contours and repaint their shared fill with the tile.
            SvgComputedStyle solidFill = style with { Fill = "black" };
            SvgResolvedPaintableElement curveShape = context.ResolveShape(solidFill, reader, true);
            using BudgetedParserList<SvgPoint> noPoints = new(budget, SvgMemoryCategory.Geometry, context.WorkCounter, context.CancellationToken);
            using BudgetedParserList<int> noEnds = new(budget, SvgMemoryCategory.Geometry, context.WorkCounter, context.CancellationToken);
            using SvgOrthogonalGeometryLowering curveLowering = new(noPoints, noEnds, elementName, true, isEvenOdd, maximumGeometryElements, budget, context.WorkCounter, context.CancellationToken);
            SvgPolygonMapping mapping = hasCurve ? SvgPolygonMapping.FlattenedCurves : SvgPolygonMapping.FiniteEdges;
            SvgParsedGeometry filled = curveLowering.LowerDeviceFill(d, commandCount, hasArc, hasCurve, mapping, curveShape, frame, false, budget);
            using BudgetedGeometryBuilder repainted = new(budget, SvgMemoryCategory.Geometry, context.WorkCounter, context.CancellationToken);
            try
            {
                foreach (SvgGeometry existing in filled.Geometries)
                {
                    SvgGeometry repaint = existing.TransferAuthoredPathTo(SvgPatternPolygon.RepaintOutline(existing.Polygon, tile, opacity, frame is {} viewport ? SvgAffineTransform.FromScaleOffset(1, 1, viewport.OffsetX, viewport.OffsetY).ComposeChecked(transform) : transform, budget));
                    try { repainted.Add(repaint); }
                    catch { repaint.Dispose(); throw; }
                }
                filled.Dispose();
                return SvgGradientSyntax.AppendPaintFillStrokePath(new SvgParsedGeometry(repainted.Detach(), filled.ElementCount), d, commandCount, hasArc, hasCurve, reader, context, elementName, frame, maximumGeometryElements, budget, bandStroke, bandStrokeShape);
            }
            catch { foreach (SvgGeometry geometry in repainted.AsSpan()) geometry.Dispose(); filled.Discard(); throw; }
        }
        using BudgetedParserList<int> contourEnds = new(budget, SvgMemoryCategory.Geometry, context.WorkCounter, context.CancellationToken);
        using BudgetedParserList<SvgPoint> points = SvgOrthogonalShapeParser.TokenizeBands(reader, elementName, isPath, true, maximumGeometryElements, budget, context, contourEnds);
        using SvgOrthogonalGeometryLowering lowering = new(points, contourEnds, elementName, isPath, isEvenOdd, maximumGeometryElements, budget, context.WorkCounter, context.CancellationToken);
        if (tile.TileWidth == 0 || tile.TileHeight == 0 || !tile.HasContent) return lowering.Lower(default, false, transform, frame);
        if (SvgOrthogonalShapeParser.HasDiagonalContour(points, contourEnds))
        {
            // Diagonal pattern fills lower through shared polygon outlines repainted with the tile, like gradient outlines.
            SvgParsedGeometry white = lowering.LowerPolygonFill(default, painted, transform, frame, budget);
            try
            {
                using BudgetedGeometryBuilder repainted = new(budget, SvgMemoryCategory.Geometry, context.WorkCounter, context.CancellationToken);
                foreach (SvgGeometry existing in white.Geometries) repainted.Add(existing.TransferAuthoredPathTo(SvgPatternPolygon.RepaintOutline(existing.Polygon, tile, opacity, frame is {} viewport ? SvgAffineTransform.FromScaleOffset(1, 1, viewport.OffsetX, viewport.OffsetY).ComposeChecked(transform) : transform, budget)));
                white.Dispose();
                return SvgGradientSyntax.AppendPaintFillStroke(new SvgParsedGeometry(repainted.Detach(), white.ElementCount), reader, context, elementName, isPath, points, contourEnds, transform, frame, maximumGeometryElements, budget, bandStroke, bandStrokeShape);
            }
            catch { white.Dispose(); throw; }
        }
        SvgRectangle content = tile.Content?.Rectangle ?? default;
        SvgParsedGeometry solid = lowering.Lower(default, painted, transform, frame);
        try
        {
            SvgGeometry[] bands = solid.Storage.Items;
            for (int bandIndex = 0; bandIndex < bands.Length; bandIndex++)
            {
                context.WorkCounter.Consume(context.CancellationToken);
                bands[bandIndex] = SvgGeometry.FromPatternFill(new SvgPatternGeometry(content, tile.OriginX, tile.OriginY, tile.TileWidth, tile.TileHeight, bands[bandIndex].Rectangle, opacity, null) { ContentPaint = tile.Content?.SolidPaint, VectorContent = tile.VectorContent, ShaderPlacement = tile.VectorContent is null ? SvgAffineTransform.Identity : (frame is {} viewport ? SvgAffineTransform.FromScaleOffset(1, 1, viewport.OffsetX, viewport.OffsetY).ComposeChecked(transform) : transform).ComposeChecked(tile.VectorPlacement) });
            }
            return SvgGradientSyntax.AppendPaintFillStroke(solid, reader, context, elementName, isPath, points, contourEnds, transform, frame, maximumGeometryElements, budget, bandStroke, bandStrokeShape);
        }
        catch { solid.Discard(); throw; }
    }

    /// <summary>Lowers one pattern fill region shared by fill-only and fill-plus-stroke lowerings.</summary>
    internal static SvgGeometry LowerPatternFillGeometry(SvgPatternTile tile, SvgRectangle fill, bool fillHasFill, byte fillOpacity, SvgStroke? stroke, SvgAffineTransform shaderPlacement)
    {
        if (tile.TileWidth == 0 || tile.TileHeight == 0 || !tile.HasContent) return SvgGeometry.FromOrthogonalBand(new SvgRectangle(fill.X, fill.Y, fill.Width, fill.Height, SvgColor.Transparent, false, null));
        return SvgGeometry.FromPatternFill(new SvgPatternGeometry(tile.Content?.Rectangle ?? default, tile.OriginX, tile.OriginY, tile.TileWidth, tile.TileHeight, fill with { HasFill = fillHasFill }, fillOpacity, stroke) { ShaderPlacement = shaderPlacement.ComposeChecked(tile.VectorPlacement), ContentPaint = tile.Content?.SolidPaint, VectorContent = tile.VectorContent });
    }

    internal static SvgParsedGeometry LowerPatternRect(XmlReader reader, SvgPaintableElementContext context, SvgComputedStyle style, SvgDocumentState state, SvgViewportFrame? frame)
    {
        if (!state.Patterns.TryGetValue(ParsePatternId(style.Fill), out SvgPatternTile tile)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only defined local pattern references are implemented.");
        if (frame is not null && frame.Value.IsScaled) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Pattern fills in scaled viewports are not implemented.");
        SvgRectangleSyntax syntax = SvgRectangleSyntax.Parse(reader);
        SvgAffineTransform transform = context.ResolveTransform(reader);
        SvgRectangle placement = transform.Apply(new SvgRectangle(syntax.X, syntax.Y, syntax.Width, syntax.Height, default, true, null));
        if (!transform.IsAxisAligned) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only axis-aligned pattern transforms are implemented.");
        if (transform.A < 0 || transform.D < 0) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only non-reflected pattern fills are implemented.");
        bool painted = SvgPresentationResolver.IsPainted(style.Display, style.Visibility, context.InheritedPresentation.IsDisplayed, context.InheritedPresentation.IsVisible);
        byte opacity = SvgColorParser.ParseOpacity(style.Opacity);
        byte fillOpacity = SvgReferenceResolver.MultiplyAlpha(opacity, SvgColorParser.ParseOpacity(style.FillOpacity));
        SvgLinearGradient strokeGradient = default;
        SvgRadialGradient strokeCircle = default;
        bool hasLinearStroke = false;
        bool hasRadialStroke = false;
        bool zeroPaintStrokeWidth = false;
        int strokeWidth = 0;
        double halfWidth = 0;
        SvgRectangle top = default;
        SvgRectangle bottom = default;
        SvgRectangle left = default;
        SvgRectangle right = default;
        SvgRectangle box = placement;
        SvgStroke? stroke = null;
        if (SvgGradientSyntax.IsGradientStrokeUrl(style.Stroke))
        {
            if (state.RadialGradients.TryGetValue(SvgGradientSyntax.ParseGradientId(style.Stroke), out SvgRadialGradient resolvedCircle))
            {
                strokeCircle = SvgGradientSyntax.MapUserSpaceRadial(transform, resolvedCircle, frame);
                hasRadialStroke = true;
            }
            else if (state.Gradients.TryGetValue(SvgGradientSyntax.ParseGradientId(style.Stroke), out SvgLinearGradient resolvedGradient))
            {
                strokeGradient = SvgGradientSyntax.MapUserSpaceVector(transform, resolvedGradient, frame);
                hasLinearStroke = true;
            }
            else if (SvgStrokeStyleSyntax.IsZeroStrokeWidth(style.Width ?? reader.GetAttribute("stroke-width"))) zeroPaintStrokeWidth = true;
            else throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only defined local gradient references are implemented.");
            SvgStroke? probe = SvgLeafParser.ParseRectangleStroke(new SvgColor(255, 255, 255, 255), style.Width ?? reader.GetAttribute("stroke-width"), transform, placement.X, placement.Y, placement.Width, placement.Height, 255, painted);
            if (probe is null && !SvgStrokeStyleSyntax.IsZeroStrokeWidth(style.Width ?? reader.GetAttribute("stroke-width"))) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only even-width gradient strokes no larger than the rectangle dimensions are implemented.");
            halfWidth = probe is null ? 0 : probe.Value.Width / 2d;
            strokeWidth = probe is null ? 0 : checked((int)probe.Value.Width);
            zeroPaintStrokeWidth = probe is null;
            top = new(placement.X - halfWidth, placement.Y - halfWidth, placement.Width + strokeWidth, strokeWidth, default, true, null);
            bottom = new(placement.X - halfWidth, placement.Y + placement.Height - halfWidth, placement.Width + strokeWidth, strokeWidth, default, true, null);
            left = new(placement.X - halfWidth, placement.Y + halfWidth, strokeWidth, placement.Height - strokeWidth, default, true, null);
            right = new(placement.X + placement.Width - halfWidth, placement.Y + halfWidth, strokeWidth, placement.Height - strokeWidth, default, true, null);
        }
        else stroke = ParsePatternStroke(reader, style, context, transform, placement, opacity, painted);
        if (frame is not null)
        {
            placement = frame.Value.Clip.ClipRectangle(frame.Value.Shift(placement));
            box = frame.Value.Shift(box);
            top = frame.Value.Clip.ClipRectangle(frame.Value.Shift(top));
            bottom = frame.Value.Clip.ClipRectangle(frame.Value.Shift(bottom));
            left = frame.Value.Clip.ClipRectangle(frame.Value.Shift(left));
            right = frame.Value.Clip.ClipRectangle(frame.Value.Shift(right));
        }
        bool combined = hasLinearStroke || hasRadialStroke;
        using BudgetedGeometryBuilder lowered = new(state.Budget, SvgMemoryCategory.Geometry, context.WorkCounter, context.CancellationToken);
        SvgAffineTransform shaderPlacement = frame is {} viewport ? SvgAffineTransform.FromScaleOffset(1, 1, viewport.OffsetX, viewport.OffsetY).ComposeChecked(transform) : transform;
        lowered.Add(LowerPatternFillGeometry(tile, placement, painted, fillOpacity, combined ? null : (painted ? stroke : null), shaderPlacement));
        // A zero width paints no stroke bands, so the pattern fill above reaches the scene alone; probe nulls only on zero widths here.
        if (combined && !zeroPaintStrokeWidth)
        {
            SvgRectangle area = placement with { HasFill = false };
            bool solidRing = (hasLinearStroke && (strokeGradient.Stops.Length == 1 || (strokeGradient.X1 == strokeGradient.X2 && strokeGradient.Y1 == strokeGradient.Y2 && !strokeGradient.IsEmpty))) || (hasRadialStroke && (strokeCircle.Stops.Length == 1 || (strokeCircle.Radius == 0 && !strokeCircle.IsEmpty)));
            if (hasLinearStroke && strokeGradient.Stops.Length > 1 && strokeGradient.X1 == strokeGradient.X2 && strokeGradient.Y1 == strokeGradient.Y2 && strokeGradient.Spread != SvgGradientSpread.Pad) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only pad-spread degenerate linear gradient strokes are implemented.");
            if (hasRadialStroke && strokeCircle.Stops.Length > 1 && strokeCircle.Radius == 0 && strokeCircle.Spread != SvgGradientSpread.Pad) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only pad-spread degenerate radial gradient strokes are implemented.");
            if (solidRing)
            {
                SvgColor ringColor = hasLinearStroke ? strokeGradient.End : strokeCircle.End;
                SvgStroke solidStroke = new(SvgPaintResolver.ApplyOpacity(ringColor, SvgReferenceResolver.MultiplyAlpha(opacity, SvgColorParser.ParseOpacity(style.StrokeOpacity))), strokeWidth);
                lowered.Add(SvgGeometry.FromRectangle(area with { Stroke = painted ? solidStroke : null }));
            }
            else if (hasLinearStroke) lowered.Add(SvgGeometry.FromGradientStroke(new SvgGradientStrokeGeometry(area, top, bottom, left, right, strokeGradient, box, opacity, painted)));
            else
            {
                bool paintBands = painted && !strokeCircle.IsEmpty;
                lowered.Add(SvgGeometry.FromRadialFill(SvgRadialGeometry.Create(strokeCircle, box, top with { HasFill = paintBands }, opacity, null, state.Budget)));
                lowered.Add(SvgGeometry.FromRadialFill(SvgRadialGeometry.Create(strokeCircle, box, bottom with { HasFill = paintBands }, opacity, null, state.Budget)));
                lowered.Add(SvgGeometry.FromRadialFill(SvgRadialGeometry.Create(strokeCircle, box, left with { HasFill = paintBands }, opacity, null, state.Budget)));
                lowered.Add(SvgGeometry.FromRadialFill(SvgRadialGeometry.Create(strokeCircle, box, right with { HasFill = paintBands }, opacity, null, state.Budget)));
            }
        }
        return new SvgParsedGeometry(lowered.Detach(), 0);
    }

    private static SvgStroke? ParsePatternStroke(XmlReader reader, SvgComputedStyle style, SvgPaintableElementContext context, SvgAffineTransform transform, SvgRectangle placement, byte opacity, bool painted)
    {
        if (style.Stroke is null || style.Stroke.Equals("none", StringComparison.OrdinalIgnoreCase)) return null;
        SvgColor strokeColor = SvgPaintResolver.ResolveStrokeColor(style.Stroke, style.Color, context.InheritedPresentation.Paint, context.NamedColors);
        if (style.StrokeOpacity is not null) strokeColor = SvgPaintResolver.ApplyOpacity(strokeColor, SvgColorParser.ParseOpacity(style.StrokeOpacity));
        return SvgLeafParser.ParseRectangleStroke(strokeColor, style.Width ?? reader.GetAttribute("stroke-width"), transform, placement.X, placement.Y, placement.Width, placement.Height, opacity, painted);
    }

    /// <summary>Reports whether a stroke references a locally defined pattern tile.</summary>
    internal static bool IsPatternStroke(string? stroke, SvgDocumentState state)
    {
        if (stroke is null || !stroke.StartsWith("url(", StringComparison.Ordinal)) return false;
        return state.Patterns.TryGetValue(ParsePatternId(stroke), out _);
    }

    /// <summary>Lowers one pattern-stroked rectangle into one solid fill plus four pattern-evaluated stroke bands sharing the tile grid.</summary>
    internal static SvgParsedGeometry LowerPatternStrokeRect(XmlReader reader, SvgPaintableElementContext context, SvgComputedStyle style, SvgDocumentState state, SvgViewportFrame? frame)
    {
        SvgStrokeStyleSyntax.RequireButtCapJoin(reader, "rectangle", context.WorkCounter, context.CancellationToken);
        if (!state.Patterns.TryGetValue(ParsePatternId(style.Stroke), out SvgPatternTile tile)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only defined local pattern references are implemented.");
        if (frame is not null && frame.Value.IsScaled) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Strokes in scaled viewports are not implemented.");
        SvgRectangleSyntax syntax = SvgRectangleSyntax.Parse(reader);
        SvgAffineTransform transform = context.ResolveTransform(reader);
        SvgRectangle placement = transform.Apply(new SvgRectangle(syntax.X, syntax.Y, syntax.Width, syntax.Height, default, true, null));
        SvgLinearGradient fillGradient = default;
        SvgRadialGradient fillCircle = default;
        SvgPatternTile fillPatternTile = default;
        bool hasLinearFill = false;
        bool hasRadialFill = false;
        bool hasPatternFill = false;
        if (SvgGradientSyntax.IsGradientFill(style.Fill))
        {
            string fillId = SvgGradientSyntax.ParseGradientId(style.Fill);
            if (state.Gradients.TryGetValue(fillId, out SvgLinearGradient resolvedFill))
            {
                fillGradient = SvgGradientSyntax.MapUserSpaceVector(transform, resolvedFill, frame);
                fillGradient = SvgGradientSyntax.MirrorBoundingBoxVector(transform, fillGradient);
                hasLinearFill = true;
            }
            else if (state.RadialGradients.TryGetValue(fillId, out SvgRadialGradient resolvedCircle))
            {
                fillCircle = SvgGradientSyntax.MapUserSpaceRadial(transform, resolvedCircle, frame);
                hasRadialFill = true;
            }
            else if (state.Patterns.TryGetValue(fillId, out SvgPatternTile fillTile))
            {
                fillPatternTile = fillTile;
                hasPatternFill = true;
            }
            else throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only defined local pattern references are implemented.");
        }
        bool combined = hasLinearFill || hasRadialFill || hasPatternFill;
        SvgShapePresentationResolver.ValidateFillRule(style.FillRule);
        bool painted;
        byte opacity;
        SvgColor fillColor = default;
        bool hasFill;
        if (combined)
        {
            painted = SvgPresentationResolver.IsPainted(style.Display, style.Visibility, context.InheritedPresentation.IsDisplayed, context.InheritedPresentation.IsVisible);
            opacity = SvgColorParser.ParseOpacity(style.Opacity);
            hasFill = painted;
        }
        else
        {
            SvgResolvedPresentation presentation = SvgPresentationResolver.Resolve(style, context.InheritedPresentation.Paint, context.InheritedPresentation.IsDisplayed, context.InheritedPresentation.IsVisible, context.NamedColors);
            painted = presentation.IsPainted;
            opacity = presentation.Opacity;
            fillColor = SvgPaintResolver.ResolveFillColor(presentation.Paint, opacity);
            hasFill = presentation.Paint.HasFill && painted;
        }
        SvgStroke? probe = SvgLeafParser.ParseRectangleStroke(new SvgColor(255, 255, 255, 255), style.Width ?? reader.GetAttribute("stroke-width"), transform, placement.X, placement.Y, placement.Width, placement.Height, 255, painted);
        if (probe is null && !SvgStrokeStyleSyntax.IsZeroStrokeWidth(style.Width ?? reader.GetAttribute("stroke-width"))) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only even-width pattern strokes no larger than the rectangle dimensions are implemented.");
        double halfWidth = probe is null ? 0 : probe.Value.Width / 2d;
        int strokeWidth = probe is null ? 0 : checked((int)probe.Value.Width);
        SvgRectangle top = new(placement.X - halfWidth, placement.Y - halfWidth, placement.Width + strokeWidth, strokeWidth, default, true, null);
        SvgRectangle bottom = new(placement.X - halfWidth, placement.Y + placement.Height - halfWidth, placement.Width + strokeWidth, strokeWidth, default, true, null);
        SvgRectangle left = new(placement.X - halfWidth, placement.Y + halfWidth, strokeWidth, placement.Height - strokeWidth, default, true, null);
        SvgRectangle right = new(placement.X + placement.Width - halfWidth, placement.Y + halfWidth, strokeWidth, placement.Height - strokeWidth, default, true, null);
        byte fillOpacity = combined ? SvgReferenceResolver.MultiplyAlpha(opacity, SvgColorParser.ParseOpacity(style.FillOpacity)) : opacity;
        bool fillHasFill = hasFill;
        if (hasLinearFill) fillHasFill = hasFill && !fillGradient.IsEmpty;
        else if (hasRadialFill) fillHasFill = hasFill && !fillCircle.IsEmpty;
        SvgRectangle box = placement;
        SvgRectangle fill = placement with { Color = fillColor, HasFill = fillHasFill };
        if (frame is not null)
        {
            if (probe is not null && !frame.Value.Clip.Contains(placement.X - halfWidth, placement.Y - halfWidth, placement.X + placement.Width + halfWidth, placement.Y + placement.Height + halfWidth)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Rectangle strokes crossing a nested viewport boundary are not implemented.");
            SvgRectangle shifted = frame.Value.Shift(placement);
            fill = frame.Value.Clip.ClipRectangle(shifted) with { Color = fillColor, HasFill = fillHasFill };
            box = frame.Value.Shift(box);
            top = frame.Value.Clip.ClipRectangle(frame.Value.Shift(top));
            bottom = frame.Value.Clip.ClipRectangle(frame.Value.Shift(bottom));
            left = frame.Value.Clip.ClipRectangle(frame.Value.Shift(left));
            right = frame.Value.Clip.ClipRectangle(frame.Value.Shift(right));
        }
        SvgGeometry? tileContent = tile.Content;
        bool paintBands = painted && tile.TileWidth != 0 && tile.TileHeight != 0 && tile.HasContent;
        SvgRectangle content = tileContent.HasValue ? tileContent.Value.Rectangle : default;
        using BudgetedGeometryBuilder stroked = new(state.Budget, SvgMemoryCategory.Geometry, context.WorkCounter, context.CancellationToken);
        if (hasLinearFill) stroked.Add(SvgGradientSyntax.LowerLinearFillGeometry(fillGradient, box, fill, fillHasFill, fillOpacity));
        else if (hasRadialFill) stroked.Add(SvgGradientSyntax.LowerRadialFillGeometry(fillCircle, box, fill, fillHasFill, fillOpacity, state.Budget));
        else if (hasPatternFill) stroked.Add(LowerPatternFillGeometry(fillPatternTile, fill, fillHasFill, fillOpacity, null, frame is {} viewport ? SvgAffineTransform.FromScaleOffset(1, 1, viewport.OffsetX, viewport.OffsetY).ComposeChecked(transform) : transform));
        else stroked.Add(SvgGeometry.FromRectangle(fill));
        if (probe is not null)
        {
            // A zero width paints no stroke bands, so the pattern fill above reaches the scene alone.
            stroked.Add(SvgGeometry.FromPatternFill(new SvgPatternGeometry(content, tile.OriginX, tile.OriginY, tile.TileWidth, tile.TileHeight, top with { HasFill = paintBands }, opacity, null) { ContentPaint = tile.Content?.SolidPaint, VectorContent = tile.VectorContent, ShaderPlacement = tile.VectorPlacement }) with { RecordedPaint = SvgRecordedPaint.SourceStroke });
            stroked.Add(SvgGeometry.FromPatternFill(new SvgPatternGeometry(content, tile.OriginX, tile.OriginY, tile.TileWidth, tile.TileHeight, bottom with { HasFill = paintBands }, opacity, null) { ContentPaint = tile.Content?.SolidPaint, VectorContent = tile.VectorContent, ShaderPlacement = tile.VectorPlacement }) with { RecordedPaint = SvgRecordedPaint.SourceStroke });
            stroked.Add(SvgGeometry.FromPatternFill(new SvgPatternGeometry(content, tile.OriginX, tile.OriginY, tile.TileWidth, tile.TileHeight, left with { HasFill = paintBands }, opacity, null) { ContentPaint = tile.Content?.SolidPaint, VectorContent = tile.VectorContent, ShaderPlacement = tile.VectorPlacement }) with { RecordedPaint = SvgRecordedPaint.SourceStroke });
            stroked.Add(SvgGeometry.FromPatternFill(new SvgPatternGeometry(content, tile.OriginX, tile.OriginY, tile.TileWidth, tile.TileHeight, right with { HasFill = paintBands }, opacity, null) { ContentPaint = tile.Content?.SolidPaint, VectorContent = tile.VectorContent, ShaderPlacement = tile.VectorPlacement }) with { RecordedPaint = SvgRecordedPaint.SourceStroke });
        }
        return new SvgParsedGeometry(stroked.Detach(), 0);
    }

    /// <summary>Lowers one pattern-stroked axis-aligned line into a pattern-evaluated stroke band.</summary>
    internal static SvgParsedGeometry LowerPatternStrokeLine(XmlReader reader, SvgPaintableElementContext context, SvgComputedStyle style, SvgLineSyntax syntax, SvgDocumentState state, SvgViewportFrame? frame)
    {
        // A zero width paints nothing for any paint, so it returns empty before dash, cap, and paint handling like the solid line kernel.
        if (SvgGeometrySyntax.ParseInteger(style.Width ?? syntax.StrokeWidth ?? "1") == 0)
        {
            using BudgetedGeometryBuilder none = new(state.Budget, SvgMemoryCategory.Geometry, context.WorkCounter, context.CancellationToken);
            return new SvgParsedGeometry(none.Detach(), 0);
        }
        // Round caps without dashes lower through the paint-specific round-cap path below; everything else keeps the band gate.
        if (SvgStrokeStyleSyntax.ParseDasharray(reader.GetAttribute("stroke-dasharray")) is not null || SvgStrokeStyleSyntax.ParseLinecap(reader.GetAttribute("stroke-linecap")) != SvgStrokeCap.Round) SvgStrokeStyleSyntax.RequireButtOrSquareCapJoin(reader, "line", context.WorkCounter, context.CancellationToken);
        SvgAffineTransform transform = context.ResolveTransform(reader);
        if (transform != SvgAffineTransform.Identity) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only untransformed horizontal or vertical lines are implemented.");
        if (frame is not null && frame.Value.IsScaled) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Strokes in scaled viewports are not implemented.");
        if ((syntax.X1 != syntax.X2 && syntax.Y1 != syntax.Y2) || (syntax.X1 == syntax.X2 && syntax.Y1 == syntax.Y2)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only untransformed horizontal or vertical lines are implemented.");
        int strokeWidth = SvgGeometrySyntax.ParseInteger(style.Width ?? syntax.StrokeWidth ?? "1");
        if (strokeWidth <= 0 || strokeWidth % 2 != 0) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only even-width line strokes are implemented.");
        double[]? dashEntries = SvgStrokeStyleSyntax.ParseDasharray(style.Dasharray);
        SvgStrokeCap dashLinecap = SvgStrokeStyleSyntax.ParseLinecap(style.Linecap);
        if (dashLinecap == SvgStrokeCap.Square && dashEntries is null) syntax = syntax.WithSquareCaps(strokeWidth);
        double? pathLength = dashEntries is null ? null : SvgStrokeStyleSyntax.ParsePathLength(style.PathLength ?? reader.GetAttribute("pathLength"));
        if (!state.Patterns.TryGetValue(ParsePatternId(style.Stroke), out SvgPatternTile tile)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only defined local pattern references are implemented.");
        bool painted = SvgPresentationResolver.IsPainted(style.Display, style.Visibility, context.InheritedPresentation.IsDisplayed, context.InheritedPresentation.IsVisible);
        byte opacity = SvgReferenceResolver.MultiplyAlpha(SvgColorParser.ParseOpacity(style.Opacity), SvgColorParser.ParseOpacity(style.StrokeOpacity));
        if (dashLinecap == SvgStrokeCap.Square && dashEntries is not null && (opacity != 255 || (tile.Content is SvgGeometry tileContent && tileContent.Rectangle.Color.Alpha != 255))) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Translucent square-capped dashed lines are not implemented.");
        if (dashLinecap == SvgStrokeCap.Round && dashEntries is null)
        {
            if (opacity != 255 || (tile.Content is SvgGeometry tilePaint && tilePaint.Rectangle.Color.Alpha != 255)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Translucent round-capped paint lines are not implemented.");
            return LowerRoundCapPatternStrokeLine(syntax, strokeWidth, style, transform, tile, painted, opacity, frame, state, context);
        }
        if (dashEntries is not null) return LowerDashedPatternStrokeLine(syntax, strokeWidth, dashLinecap == SvgStrokeCap.Square, dashEntries, SvgStrokeStyleSyntax.ParseDashoffset(style.Dashoffset), pathLength, tile, painted, opacity, frame, state, context);
        SvgRectangle band = new SvgAxisAlignedLine(syntax.X1, syntax.Y1, syntax.X2, syntax.Y2, default, painted, strokeWidth).ToStrokeBand();
        if (frame is not null) band = frame.Value.ClipShifted(band);
        if (tile.TileWidth == 0 || tile.TileHeight == 0 || !tile.HasContent) return RetainSingleBand(SvgGeometry.FromOrthogonalBand(band with { HasFill = false }), state, context);
        SvgRectangle content = tile.Content?.Rectangle ?? default;
        return RetainSingleBand(SvgGeometry.FromPatternFill(new SvgPatternGeometry(content, tile.OriginX, tile.OriginY, tile.TileWidth, tile.TileHeight, band, opacity, null) { ContentPaint = tile.Content?.SolidPaint, VectorContent = tile.VectorContent, ShaderPlacement = tile.VectorPlacement }), state, context);
    }

    /// <summary>Lowers one round-capped pattern-tile line stroke into one tile-evaluated band with two repainted tip discs.</summary>
    private static SvgParsedGeometry LowerRoundCapPatternStrokeLine(SvgLineSyntax syntax, int strokeWidth, SvgComputedStyle style, SvgAffineTransform transform, SvgPatternTile tile, bool painted, byte opacity, SvgViewportFrame? frame, SvgDocumentState state, SvgPaintableElementContext context)
    {
        SvgGeometry? paintContent = tile.Content;
        if (!tile.HasContent || tile.TileWidth == 0 || tile.TileHeight == 0)
        {
            (SvgParsedGeometry blank, SvgRectangle _) = SvgLeafParser.LowerWhiteRoundCapLine(syntax, strokeWidth, style, transform, painted, false, frame, state, context);
            return blank;
        }
        (SvgParsedGeometry white, SvgRectangle _) = SvgLeafParser.LowerWhiteRoundCapLine(syntax, strokeWidth, style, transform, painted, painted, frame, state, context);
        try
        {
            SvgRectangle content = paintContent?.Rectangle ?? default;
            using BudgetedGeometryBuilder repainted = new(state.Budget, SvgMemoryCategory.Geometry, context.WorkCounter, context.CancellationToken);
            foreach (SvgGeometry existing in white.Geometries)
            {
                if (existing.Kind == SvgGeometryKind.OrthogonalBand) repainted.Add(SvgGeometry.FromPatternFill(new SvgPatternGeometry(content, tile.OriginX, tile.OriginY, tile.TileWidth, tile.TileHeight, existing.Rectangle, opacity, null) { ContentPaint = tile.Content?.SolidPaint, VectorContent = tile.VectorContent, ShaderPlacement = tile.VectorPlacement }));
                else repainted.Add(existing.TransferAuthoredPathTo(SvgPatternPolygon.RepaintOutline(existing.Polygon, tile, opacity, state.Budget)));
            }
            white.Dispose();
            return new SvgParsedGeometry(repainted.Detach(), white.ElementCount);
        }
        catch { white.Dispose(); throw; }
    }
    /// <summary>Lowers one dashed pattern-tile line stroke into one tile-evaluated band per dash run.</summary>
    private static SvgParsedGeometry LowerDashedPatternStrokeLine(SvgLineSyntax syntax, int strokeWidth, bool squareCaps, double[] dashes, double dashOffset, double? pathLength, SvgPatternTile tile, bool painted, byte opacity, SvgViewportFrame? frame, SvgDocumentState state, SvgPaintableElementContext context)
    {
        using BudgetedParserList<SvgRectangle> runRects = new(state.Budget, SvgMemoryCategory.Geometry, context.WorkCounter, context.CancellationToken);
        bool paintsAny = SvgLeafParser.AppendLineDashRunRects(syntax, strokeWidth, squareCaps, dashes, dashOffset, pathLength, frame, state.Budget, context.WorkCounter, context.CancellationToken, runRects);
        using BudgetedGeometryBuilder runs = new(state.Budget, SvgMemoryCategory.Geometry, context.WorkCounter, context.CancellationToken);
        for (int runIndex = 0; runIndex < runRects.Count; runIndex++)
        {
            SvgRectangle run = runRects[runIndex];
            if (!tile.HasContent || tile.TileWidth == 0 || tile.TileHeight == 0) runs.Add(SvgGeometry.FromOrthogonalBand(run with { HasFill = false }));
            else runs.Add(SvgGeometry.FromPatternFill(new SvgPatternGeometry(tile.Content?.Rectangle ?? default, tile.OriginX, tile.OriginY, tile.TileWidth, tile.TileHeight, run with { HasFill = paintsAny && painted }, opacity, null) { ContentPaint = tile.Content?.SolidPaint, VectorContent = tile.VectorContent, ShaderPlacement = tile.VectorPlacement }));
        }
        return new SvgParsedGeometry(runs.Detach(), 0);
    }

    private static SvgParsedGeometry RetainSingleBand(SvgGeometry geometry, SvgDocumentState state, SvgPaintableElementContext context)
    {
        ArgumentNullException.ThrowIfNull(state);
        using BudgetedGeometryBuilder retained = new(state.Budget, SvgMemoryCategory.Geometry, context.WorkCounter, context.CancellationToken);
        retained.Add(geometry);
        return new SvgParsedGeometry(retained.Detach(), 0);
    }
}
