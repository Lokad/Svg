namespace Lokad.Svg.Tests;

public sealed class SvgRenderedGroupStyleTests
{
    [Theory]
    [Trait("Layer", "Contract")]
    [InlineData("current-color-fill-transparent")]
    [InlineData("nested-groups-transparent")]
    [InlineData("forward-chain-transparent")]
    [InlineData("dashed-path-transparent")]
    [InlineData("masked-leaf-style-transparent")]
    [InlineData("masked-use-style-transparent")]
    [InlineData("empty-forward-transparent")]
    [InlineData("thin-scaled-ellipse-transparent")]
    [InlineData("paint-server-ancestor-transparent")]
    [InlineData("css-source-transparent")]
    [InlineData("fill-rule-transparent")]
    public void Rendered_group_source_replay_releases_all_owners_on_denial_and_cancellation(string identity)
    {
        byte[] input = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "rendered-group-styles", identity + ".svg"));
        SvgRasterOwnershipAssertions.SharedBudgetReleasesAfterFailures(input, SvgRenderOptions.Default, 8_000_000);
    }
}
