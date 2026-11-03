namespace Lokad.Svg.Tests;

[Trait("Layer", "Contract")]
public sealed class SvgFilterColorMatrixOwnershipTests
{
    [Fact]
    public void Pending_color_operations_borrow_scene_coefficients_through_refusal_cancellation_and_retry()
        => SvgRasterOwnershipAssertions.ParsesBankCaseAndReleasesAfterFailures(
            "filter-color-matrix", "affine-offset.svg", SvgRenderOptions.Default, 4_000_000, 3);
}
