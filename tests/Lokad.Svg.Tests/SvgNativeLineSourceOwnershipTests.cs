namespace Lokad.Svg.Tests;

public sealed class SvgNativeLineSourceOwnershipTests
{
    [Fact]
    [Trait("Layer", "Contract")]
    public void Unpainted_native_line_source_releases_shared_owners_on_refusal_cancellation_and_retry()
    {
        byte[] source = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "line-dash-transforms", "empty-gradient-parent.svg"));
        SvgRasterOwnershipAssertions.SharedBudgetReleasesAfterFailures(source, SvgRenderOptions.Default, 2_000_000);
    }
}
