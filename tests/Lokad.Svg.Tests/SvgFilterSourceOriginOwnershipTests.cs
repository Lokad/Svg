namespace Lokad.Svg.Tests;

[Trait("Layer", "Contract")]
public sealed class SvgFilterSourceOriginOwnershipTests
{
    [Fact]
    public void Tiled_source_capture_releases_storage_after_refusal_cancellation_and_retry()
        => SvgRasterOwnershipAssertions.ParsesBankCaseAndReleasesAfterFailures(
            "filter-source-origins", "wide-solid.svg", SvgRenderOptions.Default, 8_000_000, 1);
}
