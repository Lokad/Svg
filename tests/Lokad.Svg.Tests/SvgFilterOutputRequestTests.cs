using System;
using System.Text;
using System.Threading;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;
using Xunit;

namespace Lokad.Svg.Tests;

[Trait("Layer", "Mechanisms")]
public sealed class SvgFilterOutputRequestTests
{
    [Theory]
    [InlineData(.5, 0, 2, 2, 6, 6, 1, 2, 6, 6)]
    [InlineData(-.5, 0, 2, 2, 6, 6, 2, 2, 7, 6)]
    [InlineData(1.5, 0, 2, 2, 6, 6, 0, 2, 5, 6)]
    [InlineData(-1.5, 0, 2, 2, 6, 6, 3, 2, 8, 6)]
    [InlineData(.5, .5, 2, 2, 6, 6, 1, 1, 6, 6)]
    [InlineData(-.5, -.5, 2, 2, 6, 6, 2, 2, 7, 7)]
    [InlineData(.5, 0, 3, 3, 4, 4, 2, 3, 4, 4)]
    [InlineData(.5, 0, 0, 0, 8, 8, 0, 0, 8, 8)]
    public void Materialized_blur_retains_the_downstream_requested_subset(double dx, double dy,
        float left, float top, float right, float bottom, float imageLeft, float imageTop, float imageRight, float imageBottom)
    {
        RenderBudget parser = new RenderBudget(new HostMemoryBudget(1_000_000)), scratch = new RenderBudget(new HostMemoryBudget(1_000_000));
        string x = dx.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
        string y = dy.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
        byte[] document = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs>"
            + "<filter id='f' filterUnits='userSpaceOnUse' x='-20' y='-20' width='40' height='40' color-interpolation-filters='sRGB'>"
            + "<feGaussianBlur in='SourceAlpha' stdDeviation='.5' result='b'/><feOffset in='b' dx='" + x + "' dy='" + y
            + "'/></filter></defs><rect width='4' height='4' filter='url(#f)'/></svg>");
        using (SvgRoot scene = SvgRootParser.Parse(document, SvgRenderLimits.Default, parser, CancellationToken.None))
        {
            SvgFilterArena arena = scene.SceneStorage?.Filters ?? throw new InvalidOperationException();
            SvgFilterResource resource = arena.ResourceAt(arena.BindingAt(scene.Commands[0].MaskContentIndex).ResourceIndex);
            SvgFilterDeviceMap map = new(1, 1)
            {
                Regions = new(SvgAffineTransform.Identity, new(8, 8), new(-20, -20, 20, 20))
                { SourceBounds = new(2, 2, 6, 6), DeferFinalOffset = true }
            };
            byte[] source = new byte[8 * 8 * 4];
            for (int row = 2; row < 6; row++)
            for (int column = 2; column < 6; column++) source[(row * 8 + column) * 4 + 3] = 128;
            byte[] unchanged = source.AsSpan().ToArray();
            using (SvgFilterResult result = SvgFilterPlayback.Apply(source, 8, 8, resource, map,
                new(left, top, right, bottom), scratch, CancellationToken.None))
            {
                SvgFilterDeferredImage image = Assert.IsType<SvgFilterDeferredImage>(result.DeferredImage);
                Assert.Equal(new SvgPaintBounds(imageLeft, imageTop, imageRight, imageBottom), image.SourceBounds);
                Assert.Equal(SvgFilterImageSampling.Nearest, image.Sampling);
            }
            Assert.Equal(0, scratch.Snapshot().CurrentBytes);
            Assert.Equal(unchanged, source);
        }
        Assert.Equal(0, parser.Snapshot().CurrentBytes);
    }

    [Theory]
    [InlineData(.5, 0, 2, 2, 6, 6, false)]
    [InlineData(-.5, 0, 2, 2, 6, 6, false)]
    [InlineData(1.5, 0, 2, 2, 6, 6, false)]
    [InlineData(.5, 0, 0, 0, 8, 8, true)]
    [InlineData(-.5, 0, 0, 0, 8, 8, true)]
    [InlineData(0, .5, 0, 0, 8, 8, true)]
    public void Pending_uniform_color_resolves_before_a_transform_crosses_its_input_crop(double dx, double dy,
        float left, float top, float right, float bottom, bool materialized)
    {
        RenderBudget parser = new RenderBudget(new HostMemoryBudget(1_000_000)), scratch = new RenderBudget(new HostMemoryBudget(1_000_000));
        string x = dx.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
        string y = dy.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
        byte[] document = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs>"
            + "<filter id='f' filterUnits='userSpaceOnUse' x='0' y='0' width='8' height='8' color-interpolation-filters='linearRGB'>"
            + "<feFlood flood-color='#4070b0' flood-opacity='.25' result='b'/><feOffset in='b' dx='" + x + "' dy='" + y
            + "'/></filter></defs><rect width='4' height='4' filter='url(#f)'/></svg>");
        using (SvgRoot scene = SvgRootParser.Parse(document, SvgRenderLimits.Default, parser, CancellationToken.None))
        {
            SvgFilterArena arena = scene.SceneStorage?.Filters ?? throw new InvalidOperationException();
            SvgFilterResource resource = arena.ResourceAt(arena.BindingAt(scene.Commands[0].MaskContentIndex).ResourceIndex);
            SvgFilterDeviceMap map = new(1, 1)
            {
                Regions = new(SvgAffineTransform.Identity, new(8, 8), new(0, 0, 8, 8))
                { SourceBounds = new(2, 2, 6, 6), DeferFinalOffset = true }
            };
            using (SvgFilterResult result = SvgFilterPlayback.Apply(new byte[8 * 8 * 4], 8, 8, resource, map,
                new(left, top, right, bottom), scratch, CancellationToken.None))
            {
                SvgFilterDeferredImage image = Assert.IsType<SvgFilterDeferredImage>(result.DeferredImage);
                SvgShaderPaint paint = image.Constant ?? throw new InvalidOperationException();
                // A pending quarter-alpha color has no byte store. Resolving it
                // stores native alpha 64, then later image sampling reads 64/255.
                Assert.Equal(materialized ? 64 / 255f : .25f, paint.Alpha);
            }
            Assert.Equal(0, scratch.Snapshot().CurrentBytes);
        }
        Assert.Equal(0, parser.Snapshot().CurrentBytes);
    }

    [Theory]
    [InlineData(179, .5, 0, 2, 3, 0, 128)]
    [InlineData(179, -.5, 0, 5, 3, 45, 128)]
    [InlineData(179, 0, .5, 3, 2, 0, 128)]
    [InlineData(179, 0, -.5, 3, 5, 45, 128)]
    [InlineData(3, -.25, 0, 5, 3, 1, 192)]
    [InlineData(128, -.25, 0, 5, 3, 32, 192)]
    [InlineData(179, -.25, 0, 5, 3, 45, 192)]
    [InlineData(255, .25, 0, 2, 3, 64, 192)]
    [InlineData(179, 0, -.25, 3, 5, 45, 192)]
    [InlineData(179, -.25, -.25, 5, 5, 45, 144)]
    public void Composition_samples_a_pending_offset_while_a_draw_keeps_image_edge_coverage(byte alpha,
        double dx, double dy, int column, int row, byte sampledAlpha, byte drawCoverage)
    {
        RenderBudget parser = new RenderBudget(new HostMemoryBudget(1_000_000)), scratch = new RenderBudget(new HostMemoryBudget(1_000_000));
        string x = dx.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
        string y = dy.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
        string offset = "<feOffset in='SourceAlpha' dx='" + x + "' dy='" + y + "' result='b'/>";
        // Native quarter-pixel fixed coverage is 192 (two axes: 144).
        // Use the committed compiled native store table, not a guessed 255-scale
        // coverage or values copied from the implementation under test.
        byte[] native = System.IO.File.ReadAllBytes(System.IO.Path.Combine(AppContext.BaseDirectory, "TestData/clip-alpha-native.bin"));
        byte[] source = new byte[8 * 8 * 4];
        for (int py = 2; py < 6; py++)
        for (int px = 2; px < 6; px++) source[(py * 8 + px) * 4 + 3] = alpha;
        byte[] unchanged = source.AsSpan().ToArray();
        SvgFilterDeviceMap map = new(1, 1)
        {
            Regions = new(SvgAffineTransform.Identity, new(8, 8), new(-20, -20, 20, 20))
            { SourceBounds = new(2, 2, 6, 6) }
        };
        using (SvgRoot scene = Parse(offset, parser))
        {
            using (SvgFilterResult result = SvgFilterPlayback.Apply(source, 8, 8, Resource(scene), map,
                new(0, 0, 8, 8), scratch, CancellationToken.None))
                Assert.Equal(native[alpha * 256 + drawCoverage], result.Pixels[(row * 8 + column) * 4 + 3]);
            Assert.Equal(0, scratch.Snapshot().CurrentBytes);
        }
        using (SvgRoot scene = Parse(offset + "<feFlood flood-color='#4070b0' flood-opacity='.25' result='f'/>"
            + "<feComposite in='f' in2='b' operator='in'/>", parser))
        {
            using (SvgFilterResult result = SvgFilterPlayback.Apply(source, 8, 8, Resource(scene), map,
                new(0, 0, 8, 8), scratch, CancellationToken.None))
                Assert.Equal(sampledAlpha, result.Pixels[(row * 8 + column) * 4 + 3]);
            Assert.Equal(0, scratch.Snapshot().CurrentBytes);
        }
        Assert.Equal(0, parser.Snapshot().CurrentBytes);
        Assert.Equal(unchanged, source);

        static SvgRoot Parse(string graph, RenderBudget budget)
        {
            byte[] document = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs>"
                + "<filter id='f' filterUnits='userSpaceOnUse' x='-20' y='-20' width='40' height='40' color-interpolation-filters='linearRGB'>"
                + graph + "</filter></defs><rect width='4' height='4' filter='url(#f)'/></svg>");
            return SvgRootParser.Parse(document, SvgRenderLimits.Default, budget, CancellationToken.None);
        }
        static SvgFilterResource Resource(SvgRoot scene)
        {
            SvgFilterArena arena = scene.SceneStorage?.Filters ?? throw new InvalidOperationException();
            return arena.ResourceAt(arena.BindingAt(scene.Commands[0].MaskContentIndex).ResourceIndex);
        }
    }
}
