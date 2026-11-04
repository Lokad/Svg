namespace Lokad.Svg.Tests;

public sealed class SvgUseStrokeStyleTests
{
    [Theory]
    [Trait("Layer", "Contract")]
    [InlineData("plain-rectangle-transparent")]
    [InlineData("circle-transparent")]
    [InlineData("symbol-mixed-transparent")]
    [InlineData("nested-use-own-transparent")]
    [InlineData("dashed-path-transparent")]
    [InlineData("css-source-transparent")]
    [InlineData("magnified-symbol-transparent")]
    public void Stroke_style_reconstruction_and_source_adapters_release_all_owners_on_denial_and_cancellation(string identity)
    {
        byte[] input = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "use-stroke-style", identity + ".svg"));
        SvgRasterOwnershipAssertions.SharedBudgetReleasesAfterFailures(input, SvgRenderOptions.Default, 8_000_000);
    }
}
