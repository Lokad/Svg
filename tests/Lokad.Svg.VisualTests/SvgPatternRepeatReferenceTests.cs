namespace Lokad.Svg.VisualTests;

[Trait("Layer", "Rendering")]
public sealed class SvgPatternRepeatReferenceTests
{
    [Fact]
    public void Nested_picture_edges_match_native_floating_repeat_and_gather()
        => SvgNativeControlAssertions.MatchesCanonicalConversion("pattern-repeat", "repeat-edge", 1);
}
