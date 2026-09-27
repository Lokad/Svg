namespace Lokad.Svg.Parsing;

/// <summary>Carries inherited stroke presentation from containers to their descendants.</summary>
internal readonly record struct SvgInheritedStrokeStyle(string? Linecap, string? Linejoin, string? Dasharray, string? Dashoffset, string? Width, string? Miterlimit)
{
    internal static SvgInheritedStrokeStyle Default => new(null, null, null, null, null, null);

    /// <summary>Resolves the child container stroke from its computed style falling back to the parent.</summary>
    internal SvgInheritedStrokeStyle ResolveChild(SvgComputedStyle child)
    {
        return new SvgInheritedStrokeStyle(child.Linecap ?? Linecap, child.Linejoin ?? Linejoin, child.Dasharray ?? Dasharray, child.Dashoffset ?? Dashoffset, child.Width ?? Width, child.Miterlimit ?? Miterlimit);
    }

    /// <summary>Applies inherited stroke values to a leaf computed style when the leaf carries none.</summary>
    internal SvgComputedStyle ApplyTo(SvgComputedStyle leaf)
    {
        return leaf with { Linecap = leaf.Linecap ?? Linecap, Linejoin = leaf.Linejoin ?? Linejoin, Dasharray = leaf.Dasharray ?? Dasharray, Dashoffset = leaf.Dashoffset ?? Dashoffset, Width = leaf.Width ?? Width, Miterlimit = leaf.Miterlimit ?? Miterlimit };
    }
}
