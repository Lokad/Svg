using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgFilterColorProgramCoverageTests
{
    [Fact]
    [Trait("Layer", "Mechanism")]
    public void Matrix_alpha_remains_fractional_until_clip_coverage_is_applied()
    {
        SvgFilterColorMatrix matrix = SvgFilterColorMatrixSyntax.Parse("matrix",
            "0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 .5 0", CancellationToken.None);
        SvgShaderPaint paint = matrix.Apply(new(0, 0, 0, 19 * (1f / 255f)));
        byte[] pixel = new byte[4];
        paint.Blend(pixel, 192);
        Assert.Equal(new byte[] { 0, 0, 0, 7 }, pixel);
    }

    [Fact]
    [Trait("Layer", "Contract")]
    public void Retained_terminal_colour_releases_samples_after_refusal_cancellation_and_retry()
        => SvgRasterOwnershipAssertions.ParsesBankCaseAndReleasesAfterFailures(
            "filter-color-program-coverage", "coloured-srgb.svg", SvgRenderOptions.Default, 8_000_000, 1);
}
