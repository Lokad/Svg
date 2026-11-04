namespace Lokad.Svg.Tests;

public sealed class SvgInstanceShaderOpacityTests
{
    [Theory]
    [Trait("Layer", "Contract")]
    [InlineData("linear-use-fill-transparent")]
    [InlineData("linear-definition-transparent")]
    [InlineData("radial-symbol-transparent")]
    [InlineData("pattern-symbol-transparent")]
    [InlineData("current-color-stroke-transparent")]
    [InlineData("mixed-symbol-use-fill-transparent")]
    [InlineData("nested-shader-use-fill-transparent")]
    [InlineData("css-source-transparent")]
    [InlineData("path-use-fill-transparent")]
    [InlineData("pattern-root-minified-transparent")]
    [InlineData("pattern-negative-phase-transparent")]
    [InlineData("pattern-scaled-instance-transparent")]
    public void Instance_shader_opacity_releases_all_owners_on_denial_and_cancellation(string identity)
    {
        byte[] input = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "instance-shader-opacity", identity + ".svg"));
        SvgRasterOwnershipAssertions.SharedBudgetReleasesAfterFailures(input, SvgRenderOptions.Default, 8_000_000);
    }
}
