namespace Lokad.Svg.VisualTests;

[Trait("Layer", "Rendering")]
public sealed class SvgClipChildRoleReferenceTests
{
    [Theory]
    [InlineData("group-only")]
    [InlineData("mixed")]
    [InlineData("unused")]
    [InlineData("selected-leaf")]
    [InlineData("selected-group")]
    [InlineData("clip-selected-leaf")]
    [InlineData("clip-selected-group")]
    public void Clip_contribution_and_independent_reference_roles_match_native_rendering(string identity)
        => SvgNativeControlAssertions.MatchesCanonicalConversion("clip-child-roles", identity, 8);
}
