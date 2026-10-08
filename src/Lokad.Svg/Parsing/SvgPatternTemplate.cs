namespace Lokad.Svg.Parsing;

/// <summary>Retains authored pattern attributes until local href inheritance supplies omissions.</summary>
internal readonly record struct SvgPatternTemplate(double? X, double? Y, double? Width, double? Height,
    (double X, double Y)? Translation, bool? UserSpaceUnits, bool? UserSpaceContentUnits, string? Href)
{
    internal SvgPatternTemplate Inherit(SvgPatternTemplate parent)
        => new(X ?? parent.X, Y ?? parent.Y, Width ?? parent.Width, Height ?? parent.Height,
            Translation ?? parent.Translation, UserSpaceUnits ?? parent.UserSpaceUnits,
            UserSpaceContentUnits ?? parent.UserSpaceContentUnits, null);

    internal SvgPatternTile Grid
    {
        get
        {
            (double x, double y) = Translation ?? (0, 0);
            return new(null, X ?? 0, Y ?? 0, Width ?? 0, Height ?? 0, x, y);
        }
    }

    internal bool KeepsLegacyRectangle => Href is null && UserSpaceUnits == true
        && Grid.OriginX == Math.Truncate(Grid.OriginX) && Grid.OriginY == Math.Truncate(Grid.OriginY)
        && Grid.TileWidth == Math.Truncate(Grid.TileWidth) && Grid.TileHeight == Math.Truncate(Grid.TileHeight)
        && Grid.ShiftX == Math.Truncate(Grid.ShiftX) && Grid.ShiftY == Math.Truncate(Grid.ShiftY);

    internal void ValidateUnits()
    {
        if (UserSpaceUnits != true) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only userSpaceOnUse pattern units are implemented.");
        if (UserSpaceContentUnits == false) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only userSpaceOnUse pattern content units are implemented.");
    }
}

/// <summary>Borrowed definition content and its bounded graph state; the parent scene owns all tile scenes.</summary>
internal readonly record struct SvgPatternDefinition(string Id, SvgPatternTemplate Template, SvgPatternTile Tile,
    SvgDefinitionLeafSource? RectangleSource, SvgPaintableElementContext RectangleContext, bool IsVisiting);

/// <summary>Stores one template graph frame in the bounded iterative resolver.</summary>
internal readonly record struct SvgPatternReferenceFrame(string Id);
