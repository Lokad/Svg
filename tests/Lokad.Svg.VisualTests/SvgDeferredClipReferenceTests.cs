namespace Lokad.Svg.VisualTests;

[Trait("Layer", "Rendering")]
public sealed class SvgDeferredClipReferenceTests
{
    [Theory]
    [InlineData("multiple-uses")]
    [InlineData("mixed-child")]
    [InlineData("forward-uses")]
    [InlineData("object-box-use")]
    [InlineData("resource-intersection")]
    [InlineData("child-intersection")]
    [InlineData("point-list-children")]
    [InlineData("intersection-maps")]
    [InlineData("direct-child-context")]
    [InlineData("fractional-target-context")]
    [InlineData("rectangle-parent-context")]
    [InlineData("affine-parent-context")]
    public void Sources_and_intersections_match_independent_native_coverage(string identity)
        => SvgNativeControlAssertions.MatchesCanonicalConversion("clip-definition-sources", identity, 12);
}
