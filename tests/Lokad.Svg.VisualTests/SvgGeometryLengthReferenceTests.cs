namespace Lokad.Svg.VisualTests;

[Trait("Layer", "Rendering")]
public sealed class SvgGeometryLengthReferenceTests
{
    [Fact]
    public void Fixed_units_preserve_local_geometry_reference_placement_and_resource_grids()
        => SvgNativeControlAssertions.MatchesCanonicalConversion("geometry-fixed-lengths", "mixed-absolute-units", 1);
}
