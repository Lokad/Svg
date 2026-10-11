using Lokad.Svg.Rendering;

namespace Lokad.Svg.Parsing;

/// <summary>Retains authored pattern attributes until local href inheritance supplies omissions.</summary>
internal readonly record struct SvgPatternTemplate(double? X, double? Y, double? Width, double? Height,
    SvgAffineTransform? Transform, bool? UserSpaceUnits, bool? UserSpaceContentUnits, string? Href,
    SvgViewBox? ViewBox, (SvgViewBoxAlignment HorizontalAlignment, SvgViewBoxAlignment VerticalAlignment, SvgViewBoxFit Fit)? AspectRatio)
{
    internal SvgPatternTemplate Inherit(SvgPatternTemplate parent)
        => new(X ?? parent.X, Y ?? parent.Y, Width ?? parent.Width, Height ?? parent.Height,
            Transform ?? parent.Transform, UserSpaceUnits ?? parent.UserSpaceUnits,
            UserSpaceContentUnits ?? parent.UserSpaceContentUnits, null, ViewBox ?? parent.ViewBox, AspectRatio ?? parent.AspectRatio);

    internal SvgPatternTile Grid
    {
        get
        {
            SvgViewBox? viewport = ViewBox;
            if (viewport is {} box && AspectRatio is {} aspect)
                viewport = box with { HorizontalAlignment = aspect.HorizontalAlignment, VerticalAlignment = aspect.VerticalAlignment, Fit = aspect.Fit };
            return new(null, X ?? 0, Y ?? 0, Width ?? 0, Height ?? 0, Transform ?? SvgAffineTransform.Identity) { ContentViewBox = viewport };
        }
    }

    internal bool KeepsLegacyRectangle => Href is null && ViewBox is null && UserSpaceUnits == true
        && Grid.Transform.IsAxisAligned && Grid.Transform.A == 1 && Grid.Transform.D == 1
        && Grid.OriginX == Math.Truncate(Grid.OriginX) && Grid.OriginY == Math.Truncate(Grid.OriginY)
        && Grid.TileWidth == Math.Truncate(Grid.TileWidth) && Grid.TileHeight == Math.Truncate(Grid.TileHeight)
        && Grid.ShiftX == Math.Truncate(Grid.ShiftX) && Grid.ShiftY == Math.Truncate(Grid.ShiftY);

    internal void ValidateUnits()
    {
        if (UserSpaceUnits != true) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only userSpaceOnUse pattern units are implemented.");
        if (UserSpaceContentUnits == false && ViewBox is null) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only userSpaceOnUse pattern content units are implemented without a viewBox.");
    }
}

/// <summary>Borrowed definition content and its bounded graph state; the parent scene owns all tile scenes.</summary>
internal readonly record struct SvgPatternDefinition(string Id, SvgPatternTemplate Template, SvgPatternTile Tile,
    SvgDefinitionLeafSource? RectangleSource, SvgPaintableElementContext RectangleContext, bool IsVisiting);

/// <summary>Stores one template graph frame in the bounded iterative resolver.</summary>
internal readonly record struct SvgPatternReferenceFrame(string Id);
