using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgFilterBitmapRestoreTests
{
    [Fact]
    [Trait("Layer", "Mechanism")]
    public void Final_linear_transfer_keeps_fractional_channels_until_destination_composition()
    {
        const float scale = 1f / 255f;
        SvgShaderPaint paint = SvgFilterColorTransfer.Convert(new(scale, scale, scale, 2 * scale), SvgFilterColorSpace.Srgb);
        byte[] destination = [187, 216, 245, 255];
        paint.Blend(destination, 255);
        Assert.Equal(new byte[] { 187, 216, 245, 255 }, destination);
    }

    [Fact]
    [Trait("Layer", "Contract")]
    public void Tiled_linear_capture_releases_storage_after_refusal_cancellation_and_retry()
        => SvgRasterOwnershipAssertions.ParsesBankCaseAndReleasesAfterFailures(
            "filter-bitmap-restoration", "wide-linear-colour.svg", SvgRenderOptions.Default, 8_000_000, 2);
}
