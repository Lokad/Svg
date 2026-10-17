namespace Lokad.Svg.Parsing;

/// <summary>Carries inherited stroke presentation from containers to their descendants.</summary>
internal readonly record struct SvgInheritedStrokeStyle(string? Linecap, string? Linejoin, string? Dasharray, string? Dashoffset, string? Width, string? Miterlimit, string? StrokeOpacity, string? Stroke)
{
    internal static SvgInheritedStrokeStyle Default => new(null, null, null, null, null, null, null, null);

    internal SvgInheritedStrokeStyle InheritFrom(SvgInheritedStrokeStyle parent)
        => new(Linecap ?? parent.Linecap, Linejoin ?? parent.Linejoin, Dasharray ?? parent.Dasharray,
            Dashoffset ?? parent.Dashoffset, Width ?? parent.Width, Miterlimit ?? parent.Miterlimit,
            StrokeOpacity ?? parent.StrokeOpacity, Stroke ?? parent.Stroke);

    /// <summary>Resolves the child container stroke from its computed style falling back to the parent.</summary>
    internal SvgInheritedStrokeStyle ResolveChild(SvgComputedStyle child)
    {
        return new SvgInheritedStrokeStyle(child.Linecap ?? Linecap, child.Linejoin ?? Linejoin, child.Dasharray ?? Dasharray, child.Dashoffset ?? Dashoffset, child.Width ?? Width, child.Miterlimit ?? Miterlimit, child.StrokeOpacity ?? StrokeOpacity, child.Stroke ?? Stroke);
    }

    /// <summary>Resolves inherited stroke paint and mechanics before a leaf selects its paint-specific lowering route.</summary>
    internal SvgComputedStyle ApplyTo(SvgComputedStyle leaf)
    {
        return leaf with { Stroke = leaf.Stroke ?? Stroke, Linecap = leaf.Linecap ?? Linecap, Linejoin = leaf.Linejoin ?? Linejoin, Dasharray = leaf.Dasharray ?? Dasharray, Dashoffset = leaf.Dashoffset ?? Dashoffset, Width = leaf.Width ?? Width, Miterlimit = leaf.Miterlimit ?? Miterlimit, StrokeOpacity = leaf.StrokeOpacity ?? StrokeOpacity };
    }
}
