using Lokad.Svg.Rendering;

namespace Lokad.Svg.Parsing;

/// <summary>Retains authored pattern attributes until local href inheritance supplies omissions.</summary>
internal readonly record struct SvgPatternTemplate(double? X, double? Y, double? Width, double? Height,
    SvgAffineTransform? Transform, bool? UserSpaceUnits, bool? UserSpaceContentUnits, string? Href,
    SvgViewBox? ViewBox, (SvgViewBoxAlignment HorizontalAlignment, SvgViewBoxAlignment VerticalAlignment, SvgViewBoxFit Fit)? AspectRatio)
{
    /// <summary>Retains this resource's own availability; href inheritance does not override its display.</summary>
    internal bool IsDisplayed { get; init; } = true;

    /// <summary>Records local element children, including nonpainting/foreign elements; text nodes do not override inherited content.</summary>
    internal bool HasChildElements { get; init; }

    internal SvgPatternTemplate Inherit(SvgPatternTemplate parent)
        // Hidden templates supply no inherited attributes; aliases can supply their own dimensions and content.
        => !parent.IsDisplayed ? this with { Href = null } : new(X ?? parent.X, Y ?? parent.Y, Width ?? parent.Width, Height ?? parent.Height,
            Transform ?? parent.Transform, UserSpaceUnits ?? parent.UserSpaceUnits,
            UserSpaceContentUnits ?? parent.UserSpaceContentUnits, null, ViewBox ?? parent.ViewBox, AspectRatio ?? parent.AspectRatio)
        { IsDisplayed = IsDisplayed, HasChildElements = HasChildElements };

    internal SvgPatternTile Grid
    {
        get
        {
            SvgViewBox? viewport = ViewBox;
            if (viewport is {} box && AspectRatio is {} aspect)
                viewport = box with { HorizontalAlignment = aspect.HorizontalAlignment, VerticalAlignment = aspect.VerticalAlignment, Fit = aspect.Fit };
            SvgAffineTransform transform = Transform ?? SvgAffineTransform.Identity;
            bool objectBoxTiles = UserSpaceUnits != true;
            bool objectBoxContent = UserSpaceContentUnits == false && viewport is null;
            return new(null, X ?? 0, Y ?? 0, Width ?? 0, Height ?? 0, transform)
            {
                ContentViewBox = viewport,
                Coordinates = objectBoxTiles || objectBoxContent ? new(objectBoxTiles, objectBoxContent) : null
            };
        }
    }

    internal bool KeepsLegacyRectangle => Href is null && ViewBox is null && UserSpaceUnits == true && UserSpaceContentUnits != false
        && Grid.Transform.IsAxisAligned && Grid.Transform.A == 1 && Grid.Transform.D == 1
        && Grid.OriginX == Math.Truncate(Grid.OriginX) && Grid.OriginY == Math.Truncate(Grid.OriginY)
        && Grid.TileWidth == Math.Truncate(Grid.TileWidth) && Grid.TileHeight == Math.Truncate(Grid.TileHeight)
        && Grid.ShiftX == Math.Truncate(Grid.ShiftX) && Grid.ShiftY == Math.Truncate(Grid.ShiftY);
}

/// <summary>Borrowed definition content and its bounded graph state; the parent scene owns all tile scenes.</summary>
internal readonly record struct SvgPatternDefinition(SvgPatternTemplate Template, SvgPatternTile Tile,
    SvgDefinitionLeafSource? RectangleSource, SvgPaintableElementContext RectangleContext, bool IsVisiting);

/// <summary>Links one retained alias to the next waiter on its target, or -1 at the end.</summary>
internal readonly record struct SvgPendingPatternReference(string Id, int Next);
