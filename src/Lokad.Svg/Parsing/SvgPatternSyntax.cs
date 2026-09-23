using System.Xml;

namespace Lokad.Svg.Parsing;

/// <summary>Retains one admitted pattern tile with its integer grid while streaming.</summary>
internal readonly record struct SvgPatternTile(SvgGeometry? Content, int OriginX, int OriginY, int TileWidth, int TileHeight);

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
        return new SvgPatternTile(null, x, y, width, height);
    }

    internal static string ParsePatternId(string? value)
    {
        if (value is null) throw new SvgException(SvgErrorCode.MalformedInput, "A pattern reference is required.");
        if (!SvgLocalUrl.TryParseId(value, out string id)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only local pattern references are implemented.");
        return id;
    }

    internal static bool IsPatternFill(string? fill)
        => fill is not null && fill.StartsWith("url(", StringComparison.Ordinal);

    internal static SvgGeometry LowerPatternRect(XmlReader reader, SvgPaintableElementContext context, SvgComputedStyle style, SvgDocumentState state, SvgViewportFrame? frame)
    {
        if (style.FillOpacity is not null) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Fill opacity with pattern fills is not implemented.");
        if (!state.Patterns.TryGetValue(ParsePatternId(style.Fill), out SvgPatternTile tile)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only defined local pattern references are implemented.");
        if (frame is not null && frame.Value.IsScaled) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Pattern fills in scaled viewports are not implemented.");
        SvgRectangleSyntax syntax = SvgRectangleSyntax.Parse(reader);
        AxisTransform transform = context.ResolveTransform(reader);
        SvgRectangle placement = transform.Apply(new SvgRectangle(syntax.X, syntax.Y, syntax.Width, syntax.Height, default, true, null));
        if (transform.ScaleX < 0 || transform.ScaleY < 0) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only non-reflected pattern fills are implemented.");
        bool painted = SvgPresentationResolver.IsPainted(style.Display, style.Visibility, context.InheritedPresentation.IsDisplayed, context.InheritedPresentation.IsVisible);
        byte opacity = SvgColorParser.ParseOpacity(style.Opacity);
        SvgStroke? stroke = ParsePatternStroke(reader, style, context, transform, placement, opacity, painted);
        if (frame is not null) placement = frame.Value.Clip.ClipRectangle(frame.Value.Shift(placement));
        SvgRectangle fill = placement with { HasFill = painted };
        if (tile.TileWidth == 0 || tile.TileHeight == 0 || tile.Content is null) return SvgGeometry.FromOrthogonalBand(new SvgRectangle(fill.X, fill.Y, fill.Width, fill.Height, SvgColor.Transparent, false, null));
        SvgRectangle content = tile.Content.Value.Rectangle;
        return SvgGeometry.FromPatternFill(new SvgPatternGeometry(content, tile.OriginX, tile.OriginY, tile.TileWidth, tile.TileHeight, fill, opacity, painted ? stroke : null));
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