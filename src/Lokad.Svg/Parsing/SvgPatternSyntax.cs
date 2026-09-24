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
        using BudgetedParserList<SvgPoint> points = SvgOrthogonalShapeParser.TokenizeBands(reader, elementName, isPath, maximumGeometryElements, budget, context, contourEnds);
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

    internal static SvgGeometry LowerPatternRect(XmlReader reader, SvgPaintableElementContext context, SvgComputedStyle style, SvgDocumentState state, SvgViewportFrame? frame)
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
        SvgStroke? stroke = ParsePatternStroke(reader, style, context, transform, placement, opacity, painted);
        if (frame is not null) placement = frame.Value.Clip.ClipRectangle(frame.Value.Shift(placement));
        SvgRectangle fill = placement with { HasFill = painted };
        if (tile.TileWidth == 0 || tile.TileHeight == 0 || tile.Content is null) return SvgGeometry.FromOrthogonalBand(new SvgRectangle(fill.X, fill.Y, fill.Width, fill.Height, SvgColor.Transparent, false, null));
        SvgRectangle content = tile.Content.Value.Rectangle;
        return SvgGeometry.FromPatternFill(new SvgPatternGeometry(content, tile.OriginX, tile.OriginY, tile.TileWidth, tile.TileHeight, fill, fillOpacity, painted ? stroke : null));
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
}
