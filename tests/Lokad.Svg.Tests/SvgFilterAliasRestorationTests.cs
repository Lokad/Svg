namespace Lokad.Svg.Tests;

[Trait("Layer", "Rendering")]
public sealed class SvgFilterAliasRestorationTests
{
    [Theory]
    [InlineData("<feGaussianBlur stdDeviation='0'/>", "black", "#7f7f7f", "1", false, 127)]
    [InlineData("<feGaussianBlur stdDeviation='.02999999'/>", "black", "#7f7f7f", "1", false, 127)]
    [InlineData("<feGaussianBlur stdDeviation='0' in='SourceAlpha'/>", "red", "#7f7f7f", "1", false, 127)]
    [InlineData("<feGaussianBlur stdDeviation='0'/>", "black", "#ffffff", ".625", false, 255)]
    [InlineData("<feGaussianBlur stdDeviation='0'/>", "black", "#7f7f7f", "1", true, 126)]
    public void Gaussian_aliases_restore_pending_color_filters_at_final_composition(
        string graph, string fill, string backdrop, string opacity, bool srgb, byte expected)
    {
        string source = "<!-- SPDX-License-Identifier: CC0-1.0 -->\n<svg xmlns='http://www.w3.org/2000/svg' width='1' height='1'><defs><filter id='f'"
            + (srgb ? " color-interpolation-filters='sRGB'" : "") + ">" + graph
            + "</filter></defs><rect width='1' height='1' fill='" + backdrop + "'/><rect width='1' height='1' fill='" + fill
            + "' fill-opacity='.00392156862745098'" + (opacity == "1" ? "" : " opacity='" + opacity + "'")
            + " filter='url(#f)'/></svg>\n";
        byte[] pixels = SvgRasterTestHelper.RenderPixels(source, out int width, out int height);
        Assert.Equal((1, 1), (width, height));
        Assert.Equal(new byte[] { expected, expected, expected, 255 }, pixels);
    }
}
