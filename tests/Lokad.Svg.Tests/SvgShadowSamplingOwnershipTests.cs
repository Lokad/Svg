namespace Lokad.Svg.Tests;

[Trait("Layer", "Contract")]
public sealed class SvgShadowSamplingOwnershipTests
{
    [Fact]
    public void Expanded_shadow_source_demand_releases_storage_after_refusal_cancellation_and_retry()
        => SvgRasterOwnershipAssertions.ParsesBankCaseAndReleasesAfterFailures(
            "shadow-sampling", "panel-shadow.svg", SvgRenderOptions.Default, 64_000_000, 1);
}
