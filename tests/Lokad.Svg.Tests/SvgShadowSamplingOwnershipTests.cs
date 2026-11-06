namespace Lokad.Svg.Tests;

[Trait("Layer", "Contract")]
public sealed class SvgShadowSamplingOwnershipTests
{
    [Theory]
    [InlineData("shadow-sampling", "panel-shadow.svg")]
    [InlineData("shadow-foreground", "color-image.svg")]
    public void Expanded_shadow_source_demand_releases_storage_after_refusal_cancellation_and_retry(string bank, string input)
        => SvgRasterOwnershipAssertions.ParsesBankCaseAndReleasesAfterFailures(
            bank, input, SvgRenderOptions.Default, 64_000_000, 1);
}
