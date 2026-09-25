using System.Xml;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Parsing;

/// <summary>Retains one admitted pattern tile with its integer grid while streaming.</summary>
internal readonly record struct SvgPatternTile(SvgGeometry? Content, int OriginX, int OriginY, int TileWidth, int TileHeight, int ShiftX, int ShiftY);

/// <summary>Parses the bounded pattern definition syntax and lowers pattern-filled rectangles.</summary>
internal static class SvgPatternSyntax
{
    internal static SvgPatternTile ParsePatternElement(XmlReader reader)
    {
        string? units = reader.GetAttribute("patternUnits");
        if (units is null) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Pattern units default to objectBoundingBox, which is not implemented.");
        if (!units.Equals("userSpaceOnUse", StringComparison.Ordinal)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only userSpaceOnUse pattern units are implemented.");
        string? contentUnits = reader.GetAttribute("patternContentUnits");
        if (contentUnits is not null && !contentUnits.Equals("userSpaceOnUse", StringComparison.Ordinal)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only userSpaceOnUse pattern content units are implemented.");
        int x = ParseGridCoordinate(reader.GetAttribute("x") ?? "0");
        int y = ParseGridCoordinate(reader.GetAttribute("y") ?? "0");
        int width = ParseGridExtent(reader.GetAttribute("width"));
        int height = ParseGridExtent(reader.GetAttribute("height"));
        (int shiftX, int shiftY) = ParsePatternTransform(reader.GetAttribute("patternTransform"));
        return new SvgPatternTile(null, x, y, width, height, shiftX, shiftY);

    }

    /// <summary>Parses the admitted integer translate-only pattern transform.</summary>
    internal static (int X, int Y) ParsePatternTransform(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return (0, 0);
        if (value.Length > SvgSyntaxLimits.MaximumTransformCharacters) throw new SvgException(SvgErrorCode.LimitExceeded, "Transform values exceed the configured syntax limit.");
        ReadOnlySpan<char> input = value.AsSpan().Trim();
        int separator = input.IndexOf('(');
        if (separator <= 0 || !input.EndsWith(")")) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only integer translate() pattern transforms are implemented.");
        if (!input[..separator].Trim().Equals("translate".AsSpan(), StringComparison.OrdinalIgnoreCase)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only integer translate() pattern transforms are implemented.");
        ReadOnlySpan<char> arguments = input[(separator + 1)..^1];
        int first = 0;
        int second = 0;
        int valueCount = 0;
        SvgGeometrySyntax.DelimitedTokenReader tokens = new(arguments);
        while (tokens.TryReadValue(out ReadOnlySpan<char> token))
        {
            if (valueCount == 2) throw new SvgException(SvgErrorCode.MalformedInput, "Pattern translate() requires one or two coordinates.");
            double parsed = SvgGeometrySyntax.ParseRectangleCoordinate(token.ToString());
            if (parsed != Math.Truncate(parsed)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only integer translate() pattern transforms are implemented.");
            if (valueCount == 0) first = (int)parsed;
            else second = (int)parsed;
            valueCount++;
        }
        if (valueCount == 0) throw new SvgException(SvgErrorCode.MalformedInput, "Pattern translate() requires one or two coordinates.");
        return valueCount == 1 ? (first, 0) : (first, second);
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
        if (style.Stroke is not null && !style.Stroke.Equals("none", StringComparison.OrdinalIgnoreCase)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Pattern-filled paths with strokes are not implemented.");
        if (!state.Patterns.TryGetValue(ParsePatternId(style.Fill), out SvgPatternTile tile)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only defined local pattern references are implemented.");
        if (frame is not null && frame.Value.IsScaled) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Pattern fills in scaled viewports are not implemented.");
        AxisTransform transform = context.ResolveTransform(reader);
        if (transform.ScaleX < 0 || transform.ScaleY < 0) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only non-reflected pattern fills are implemented.");
        bool painted = SvgPresentationResolver.IsPainted(style.Display, style.Visibility, context.InheritedPresentation.IsDisplayed, context.InheritedPresentation.IsVisible);
        byte opacity = SvgReferenceResolver.MultiplyAlpha(SvgColorParser.ParseOpacity(style.Opacity), SvgColorParser.ParseOpacity(style.FillOpacity));
        SvgShapePresentationResolver.ValidateFillRule(style.FillRule);
        bool isEvenOdd = style.FillRule is not null && style.FillRule.Equals("evenodd", StringComparison.OrdinalIgnoreCase);
        using BudgetedParserList<int> contourEnds = new(budget, SvgMemoryCategory.Geometry, context.WorkCounter, context.CancellationToken);
        using BudgetedParserList<SvgPoint> points = SvgOrthogonalShapeParser.TokenizeBands(reader, elementName, isPath, false, maximumGeometryElements, budget, context, contourEnds);
        using SvgOrthogonalGeometryLowering lowering = new(points, contourEnds, elementName, isPath, isEvenOdd, maximumGeometryElements, budget, context.WorkCounter, context.CancellationToken);
        if (tile.TileWidth == 0 || tile.TileHeight == 0 || tile.Content is null) return lowering.Lower(default, false, transform, frame);
        SvgRectangle content = tile.Content.Value.Rectangle;
        SvgParsedGeometry solid = lowering.Lower(default, painted, transform, frame);
        SvgGeometry[] bands = solid.Storage.Items;
        for (int bandIndex = 0; bandIndex < bands.Length; bandIndex++)
        {
            context.WorkCounter.Consume(context.CancellationToken);
            bands[bandIndex] = SvgGeometry.FromPatternFill(new SvgPatternGeometry(content, tile.OriginX, tile.OriginY, tile.TileWidth, tile.TileHeight, bands[bandIndex].Rectangle, opacity, null));
        }
        return solid;
    }

    /// <summary>Lowers one pattern fill region shared by fill-only and fill-plus-stroke lowerings.</summary>
    internal static SvgGeometry LowerPatternFillGeometry(SvgPatternTile tile, SvgRectangle fill, bool fillHasFill, byte fillOpacity, SvgStroke? stroke)
    {
        if (tile.TileWidth == 0 || tile.TileHeight == 0 || tile.Content is null) return SvgGeometry.FromOrthogonalBand(new SvgRectangle(fill.X, fill.Y, fill.Width, fill.Height, SvgColor.Transparent, false, null));
        return SvgGeometry.FromPatternFill(new SvgPatternGeometry(tile.Content.Value.Rectangle, tile.OriginX, tile.OriginY, tile.TileWidth, tile.TileHeight, fill with { HasFill = fillHasFill }, fillOpacity, stroke));
    }

    internal static SvgParsedGeometry LowerPatternRect(XmlReader reader, SvgPaintableElementContext context, SvgComputedStyle style, SvgDocumentState state, SvgViewportFrame? frame)
    {
        if (!state.Patterns.TryGetValue(ParsePatternId(style.Fill), out SvgPatternTile tile)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only defined local pattern references are implemented.");
        if (frame is not null && frame.Value.IsScaled) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Pattern fills in scaled viewports are not implemented.");
        SvgRectangleSyntax syntax = SvgRectangleSyntax.Parse(reader);
        AxisTransform transform = context.ResolveTransform(reader);
        SvgRectangle placement = transform.Apply(new SvgRectangle(syntax.X, syntax.Y, syntax.Width, syntax.Height, default, true, null));
        if (transform.ScaleX < 0 || transform.ScaleY < 0) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only non-reflected pattern fills are implemented.");
        bool painted = SvgPresentationResolver.IsPainted(style.Display, style.Visibility, context.InheritedPresentation.IsDisplayed, context.InheritedPresentation.IsVisible);
        byte opacity = SvgColorParser.ParseOpacity(style.Opacity);
        byte fillOpacity = SvgReferenceResolver.MultiplyAlpha(opacity, SvgColorParser.ParseOpacity(style.FillOpacity));
        SvgLinearGradient strokeGradient = default;
        SvgRadialGradient strokeCircle = default;
        bool hasLinearStroke = false;
        bool hasRadialStroke = false;
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
            else throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only defined local gradient references are implemented.");
            SvgStroke? probe = SvgLeafParser.ParseRectangleStroke(new SvgColor(255, 255, 255, 255), reader.GetAttribute("stroke-width"), transform, placement.X, placement.Y, placement.Width, placement.Height, 255, painted);
            if (probe is null) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only even-width gradient strokes no larger than the rectangle dimensions are implemented.");
            halfWidth = probe.Value.Width / 2d;
            strokeWidth = probe.Value.Width;
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
        using BudgetedParserList<SvgGeometry> lowered = new(state.Budget, SvgMemoryCategory.Geometry, context.WorkCounter, context.CancellationToken);
        lowered.Add(LowerPatternFillGeometry(tile, placement, painted, fillOpacity, combined ? null : (painted ? stroke : null)));
        if (combined)
        {
            SvgRectangle area = placement with { HasFill = false };
            bool solidRing = (hasLinearStroke && (strokeGradient.Stops.Length == 1 || (strokeGradient.X1 == strokeGradient.X2 && strokeGradient.Y1 == strokeGradient.Y2 && !strokeGradient.IsEmpty))) || (hasRadialStroke && (strokeCircle.Stops.Length == 1 || (strokeCircle.Radius == 0 && !strokeCircle.IsEmpty)));
            if (hasLinearStroke && strokeGradient.Stops.Length > 1 && strokeGradient.X1 == strokeGradient.X2 && strokeGradient.Y1 == strokeGradient.Y2 && strokeGradient.Spread != SvgGradientSpread.Pad) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only pad-spread degenerate linear gradient strokes are implemented.");
            if (hasRadialStroke && strokeCircle.Stops.Length > 1 && strokeCircle.Radius == 0 && strokeCircle.Spread != SvgGradientSpread.Pad) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only pad-spread degenerate radial gradient strokes are implemented.");
            if (solidRing)
            {
                SvgColor ringColor = hasLinearStroke ? strokeGradient.End : strokeCircle.End;
                SvgStroke solidStroke = new(SvgPaintResolver.ApplyOpacity(ringColor, opacity), strokeWidth);
                lowered.Add(SvgGeometry.FromRectangle(area with { Stroke = painted ? solidStroke : null }));
            }
            else if (hasLinearStroke) lowered.Add(SvgGeometry.FromGradientStroke(new SvgGradientStrokeGeometry(area, top, bottom, left, right, strokeGradient, box, opacity, painted)));
            else
            {
                bool paintBands = painted && !strokeCircle.IsEmpty;
                lowered.Add(SvgGeometry.FromRadialFill(new SvgRadialGeometry(strokeCircle, box, top with { HasFill = paintBands }, opacity, null)));
                lowered.Add(SvgGeometry.FromRadialFill(new SvgRadialGeometry(strokeCircle, box, bottom with { HasFill = paintBands }, opacity, null)));
                lowered.Add(SvgGeometry.FromRadialFill(new SvgRadialGeometry(strokeCircle, box, left with { HasFill = paintBands }, opacity, null)));
                lowered.Add(SvgGeometry.FromRadialFill(new SvgRadialGeometry(strokeCircle, box, right with { HasFill = paintBands }, opacity, null)));
            }
        }
        return new SvgParsedGeometry(lowered.Detach(), 0);
    }

    private static SvgStroke? ParsePatternStroke(XmlReader reader, SvgComputedStyle style, SvgPaintableElementContext context, AxisTransform transform, SvgRectangle placement, byte opacity, bool painted)
    {
        if (style.Stroke is null || style.Stroke.Equals("none", StringComparison.OrdinalIgnoreCase)) return null;
        SvgColor strokeColor = SvgPaintResolver.ResolveStrokeColor(style.Stroke, style.Color, context.InheritedPresentation.Paint, context.NamedColors);
        return SvgLeafParser.ParseRectangleStroke(strokeColor, reader.GetAttribute("stroke-width"), transform, placement.X, placement.Y, placement.Width, placement.Height, opacity, painted);
    }

    private static int ParseGridCoordinate(string value)
    {
        double parsed = SvgGeometrySyntax.ParseRectangleCoordinate(value);
        if (parsed != Math.Truncate(parsed)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only integer pattern grid coordinates are implemented.");
        return (int)parsed;
    }

    private static int ParseGridExtent(string? value)
    {
        if (value is null) return 0;
        double parsed = SvgGeometrySyntax.ParseRectangleCoordinate(value);
        if (parsed < 0) throw new SvgException(SvgErrorCode.MalformedInput, "Pattern dimensions cannot be negative.");
        if (parsed != Math.Truncate(parsed)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only integer pattern grid dimensions are implemented.");
        return (int)parsed;
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
        SvgStrokeStyleSyntax.RequireButtCapJoin(reader, "rectangle");
        if (!state.Patterns.TryGetValue(ParsePatternId(style.Stroke), out SvgPatternTile tile)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only defined local pattern references are implemented.");
        if (frame is not null && frame.Value.IsScaled) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Strokes in scaled viewports are not implemented.");
        SvgRectangleSyntax syntax = SvgRectangleSyntax.Parse(reader);
        AxisTransform transform = context.ResolveTransform(reader);
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
        SvgStroke? probe = SvgLeafParser.ParseRectangleStroke(new SvgColor(255, 255, 255, 255), reader.GetAttribute("stroke-width"), transform, placement.X, placement.Y, placement.Width, placement.Height, 255, painted);
        if (probe is null) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only even-width pattern strokes no larger than the rectangle dimensions are implemented.");
        double halfWidth = probe.Value.Width / 2d;
        int strokeWidth = probe.Value.Width;
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
            if (!frame.Value.Clip.Contains(placement.X - halfWidth, placement.Y - halfWidth, placement.X + placement.Width + halfWidth, placement.Y + placement.Height + halfWidth)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Rectangle strokes crossing a nested viewport boundary are not implemented.");
            SvgRectangle shifted = frame.Value.Shift(placement);
            fill = frame.Value.Clip.ClipRectangle(shifted) with { Color = fillColor, HasFill = fillHasFill };
            box = frame.Value.Shift(box);
            top = frame.Value.Clip.ClipRectangle(frame.Value.Shift(top));
            bottom = frame.Value.Clip.ClipRectangle(frame.Value.Shift(bottom));
            left = frame.Value.Clip.ClipRectangle(frame.Value.Shift(left));
            right = frame.Value.Clip.ClipRectangle(frame.Value.Shift(right));
        }
        SvgGeometry? tileContent = tile.Content;
        bool paintBands = painted && tile.TileWidth != 0 && tile.TileHeight != 0 && tileContent.HasValue;
        SvgRectangle content = tileContent.HasValue ? tileContent.Value.Rectangle : default;
        using BudgetedParserList<SvgGeometry> stroked = new(state.Budget, SvgMemoryCategory.Geometry, context.WorkCounter, context.CancellationToken);
        if (hasLinearFill) stroked.Add(SvgGradientSyntax.LowerLinearFillGeometry(fillGradient, box, fill, fillHasFill, fillOpacity));
        else if (hasRadialFill) stroked.Add(SvgGradientSyntax.LowerRadialFillGeometry(fillCircle, box, fill, fillHasFill, fillOpacity));
        else if (hasPatternFill) stroked.Add(LowerPatternFillGeometry(fillPatternTile, fill, fillHasFill, fillOpacity, null));
        else stroked.Add(SvgGeometry.FromRectangle(fill));
        stroked.Add(SvgGeometry.FromPatternFill(new SvgPatternGeometry(content, tile.OriginX, tile.OriginY, tile.TileWidth, tile.TileHeight, top with { HasFill = paintBands }, opacity, null)));
        stroked.Add(SvgGeometry.FromPatternFill(new SvgPatternGeometry(content, tile.OriginX, tile.OriginY, tile.TileWidth, tile.TileHeight, bottom with { HasFill = paintBands }, opacity, null)));
        stroked.Add(SvgGeometry.FromPatternFill(new SvgPatternGeometry(content, tile.OriginX, tile.OriginY, tile.TileWidth, tile.TileHeight, left with { HasFill = paintBands }, opacity, null)));
        stroked.Add(SvgGeometry.FromPatternFill(new SvgPatternGeometry(content, tile.OriginX, tile.OriginY, tile.TileWidth, tile.TileHeight, right with { HasFill = paintBands }, opacity, null)));
        return new SvgParsedGeometry(stroked.Detach(), 0);
    }

    /// <summary>Lowers one pattern-stroked axis-aligned line into a pattern-evaluated stroke band.</summary>
    internal static SvgParsedGeometry LowerPatternStrokeLine(XmlReader reader, SvgPaintableElementContext context, SvgComputedStyle style, SvgLineSyntax syntax, SvgDocumentState state, SvgViewportFrame? frame)
    {
        SvgStrokeStyleSyntax.RequireButtOrSquareCapJoin(reader, "line");
        AxisTransform transform = context.ResolveTransform(reader);
        if (transform != AxisTransform.Identity) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only untransformed horizontal or vertical lines are implemented.");
        if (frame is not null && frame.Value.IsScaled) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Strokes in scaled viewports are not implemented.");
        if ((syntax.X1 != syntax.X2 && syntax.Y1 != syntax.Y2) || (syntax.X1 == syntax.X2 && syntax.Y1 == syntax.Y2)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only untransformed horizontal or vertical lines are implemented.");
        int strokeWidth = SvgGeometrySyntax.ParseInteger(syntax.StrokeWidth ?? "1");
        if (strokeWidth <= 0 || strokeWidth % 2 != 0) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only even-width line strokes are implemented.");
        double[]? dashEntries = SvgStrokeStyleSyntax.ParseDasharray(reader.GetAttribute("stroke-dasharray"));
        if (SvgStrokeStyleSyntax.ParseLinecap(reader.GetAttribute("stroke-linecap")) == SvgStrokeCap.Square)
        {
            if (dashEntries is not null) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Square caps with dashed line strokes are not implemented.");
            syntax = syntax.WithSquareCaps(strokeWidth);
        }
        double? pathLength = dashEntries is null ? null : SvgStrokeStyleSyntax.ParsePathLength(reader.GetAttribute("pathLength"));
        if (!state.Patterns.TryGetValue(ParsePatternId(style.Stroke), out SvgPatternTile tile)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only defined local pattern references are implemented.");
        bool painted = SvgPresentationResolver.IsPainted(style.Display, style.Visibility, context.InheritedPresentation.IsDisplayed, context.InheritedPresentation.IsVisible);
        byte opacity = SvgColorParser.ParseOpacity(style.Opacity);
        if (dashEntries is not null) return LowerDashedPatternStrokeLine(syntax, strokeWidth, dashEntries, SvgStrokeStyleSyntax.ParseDashoffset(reader.GetAttribute("stroke-dashoffset")), pathLength, tile, painted, opacity, frame, state, context);
        SvgRectangle band = new SvgAxisAlignedLine(syntax.X1, syntax.Y1, syntax.X2, syntax.Y2, default, painted, strokeWidth).ToStrokeBand();
        if (frame is not null) band = frame.Value.ClipShifted(band);
        if (tile.TileWidth == 0 || tile.TileHeight == 0 || tile.Content is null) return RetainSingleBand(SvgGeometry.FromOrthogonalBand(band with { HasFill = false }), state, context);
        SvgRectangle content = tile.Content.Value.Rectangle;
        return RetainSingleBand(SvgGeometry.FromPatternFill(new SvgPatternGeometry(content, tile.OriginX, tile.OriginY, tile.TileWidth, tile.TileHeight, band, opacity, null)), state, context);
    }

    /// <summary>Lowers one dashed pattern-tile line stroke into one tile-evaluated band per dash run.</summary>
    private static SvgParsedGeometry LowerDashedPatternStrokeLine(SvgLineSyntax syntax, int strokeWidth, double[] dashes, double dashOffset, double? pathLength, SvgPatternTile tile, bool painted, byte opacity, SvgViewportFrame? frame, SvgDocumentState state, SvgPaintableElementContext context)
    {
        using BudgetedParserList<SvgRectangle> runRects = new(state.Budget, SvgMemoryCategory.Geometry, context.WorkCounter, context.CancellationToken);
        bool paintsAny = SvgLeafParser.AppendLineDashRunRects(syntax, strokeWidth, dashes, dashOffset, pathLength, frame, state.Budget, context.WorkCounter, context.CancellationToken, runRects);
        using BudgetedParserList<SvgGeometry> runs = new(state.Budget, SvgMemoryCategory.Geometry, context.WorkCounter, context.CancellationToken);
        for (int runIndex = 0; runIndex < runRects.Count; runIndex++)
        {
            SvgRectangle run = runRects[runIndex];
            if (tile.Content is not SvgGeometry tileContent || tile.TileWidth == 0 || tile.TileHeight == 0) runs.Add(SvgGeometry.FromOrthogonalBand(run with { HasFill = false }));
            else runs.Add(SvgGeometry.FromPatternFill(new SvgPatternGeometry(tileContent.Rectangle, tile.OriginX, tile.OriginY, tile.TileWidth, tile.TileHeight, run with { HasFill = paintsAny && painted }, opacity, null)));
        }
        return new SvgParsedGeometry(runs.Detach(), 0);
    }

    private static SvgParsedGeometry RetainSingleBand(SvgGeometry geometry, SvgDocumentState state, SvgPaintableElementContext context)
    {
        ArgumentNullException.ThrowIfNull(state);
        using BudgetedParserList<SvgGeometry> retained = new(state.Budget, SvgMemoryCategory.Geometry, context.WorkCounter, context.CancellationToken);
        retained.Add(geometry);
        return new SvgParsedGeometry(retained.Detach(), 0);
    }
}
