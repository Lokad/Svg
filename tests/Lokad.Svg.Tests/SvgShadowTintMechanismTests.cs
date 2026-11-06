using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

[Trait("Layer", "Mechanism")]
public sealed class SvgShadowTintMechanismTests
{
    [Theory]
    [InlineData(false, 78, 80, 80, 87)]
    [InlineData(true, 79, 80, 81, 88)]
    public void Shadow_foreground_retains_the_pending_color_filter_draw_stage(
        bool filtered, byte red, byte green, byte blue, byte alpha)
    {
        // Independent one-pixel native controls distinguish packed source-over
        // from the retained color program's floating draw; see foreground-mechanisms.json.
        string graph = (filtered ? "<feColorMatrix type='saturate' values='1'/>" : "")
            + "<feDropShadow dx='0' dy='0' stdDeviation='0' flood-color='#163750' flood-opacity='.18'/>";
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='1' height='1'>"
            + "<defs><filter id='f' color-interpolation-filters='sRGB'>" + graph
            + "</filter></defs><rect width='1' height='1' filter='url(#f)'/></svg>");
        RenderBudget parser = new(new HostMemoryBudget(1_000_000));
        RenderBudget raster = new(new HostMemoryBudget(1_000_000));
        using (SvgRoot scene = SvgRootParser.Parse(input, SvgRenderLimits.Default, parser, CancellationToken.None))
        {
            SvgFilterArena arena = scene.FilterResources ?? throw new InvalidOperationException();
            SvgFilterResource resource = arena.ResourceAt(arena.BindingAt(scene.Commands[0].MaskContentIndex).ResourceIndex);
            SvgFilterDeviceMap map = new(1, 1)
            {
                Regions = new(SvgAffineTransform.Identity, new(1, 1), new(-20, -20, 21, 21))
                { SourceBounds = new(0, 0, 1, 1) }
            };
            using SvgFilterResult result = SvgFilterPlayback.ApplyFullFrame([78, 78, 78, 78], 1, 1,
                resource, map, raster, CancellationToken.None);
            Assert.Equal(new byte[] { red, green, blue, alpha }, result.Pixels);
        }
        Assert.Equal(0, parser.Snapshot().CurrentBytes);
        Assert.Equal(0, raster.Snapshot().CurrentBytes);
    }

    [Theory]
    [InlineData(false, 0f, 0, 0, 0, 0)]
    [InlineData(false, .1f, 7, 11, 18, 26)]
    [InlineData(false, .25f, 16, 28, 44, 64)]
    [InlineData(false, .5f, 32, 56, 88, 128)]
    [InlineData(false, .7f, 45, 79, 124, 179)]
    [InlineData(false, 1f, 64, 112, 176, 255)]
    [InlineData(true, 0f, 0, 0, 0, 0)]
    [InlineData(true, .1f, 1, 4, 11, 26)]
    [InlineData(true, .25f, 3, 10, 28, 64)]
    [InlineData(true, .5f, 7, 21, 56, 128)]
    [InlineData(true, .7f, 9, 29, 78, 179)]
    [InlineData(true, 1f, 13, 41, 111, 255)]
    public void Native_unpremultiplied_color_and_alpha_admission_precede_packed_tint(bool linear, float opacity,
        byte red, byte green, byte blue, byte alpha)
    {
        SvgShaderPaint tint = SvgFilterPlayback.PrepareShadowShaderTint(new(new(64, 112, 176, 255), opacity),
            linear ? SvgFilterColorSpace.LinearRgb : SvgFilterColorSpace.Srgb);
        Assert.Equal(new SvgConstantShaderPaint(red, green, blue, alpha), tint.AsConstant(1));
    }

    [Fact]
    public void Pending_linear_color_filter_preserves_tint_precision_until_coverage()
    {
        SvgShaderPaint tint = SvgFilterPlayback.PrepareShadowShaderTint(new(new(64, 112, 176, 255), .25f), SvgFilterColorSpace.LinearRgb);
        const float weight = 120f / 255f;
        byte[] pixel = new byte[4];
        new SvgShaderPaint(tint.Red * weight, tint.Green * weight, tint.Blue * weight, tint.Alpha * weight).Blend(pixel, 255);
        // Packing the tint first loses the low red lane (1 instead of 2).
        Assert.Equal(new byte[] { 2, 5, 13, 30 }, pixel);
    }
}
