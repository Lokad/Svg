using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

[Trait("Layer", "Mechanism")]
public sealed class SvgFilterStoredImageBoundsTests
{
    [Theory]
    [InlineData(1, .25, 192)]
    [InlineData(3, .25, 192)]
    [InlineData(17, .25, 192)]
    [InlineData(26, .25, 192)]
    [InlineData(128, .25, 192)]
    [InlineData(252, .25, 192)]
    [InlineData(255, .25, 192)]
    [InlineData(3, .125, 224)]
    [InlineData(252, .125, 224)]
    public void Materializing_source_alpha_keeps_native_shader_coverage_rounding(byte alpha, double offset, byte coverage)
    {
        string dx = offset.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
        RenderBudget parser = new RenderBudget(new HostMemoryBudget(1_000_000)), scratch = new RenderBudget(new HostMemoryBudget(1_000_000));
        using (SvgRoot scene = Parse("<feOffset in='SourceAlpha' dx='" + dx + "' dy='0' result='a'/><feOffset in='a' dx='.5' dy='0'/>", parser))
        {
            byte[] source = Source(alpha), unchanged = source.ToArray();
            // Compiled pinned SkMulDiv255Round vectors are indexed by source
            // alpha and coverage. Packed bitmap and constant stores differ.
            byte[] native = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData/clip-alpha-native.bin"));
            using (SvgFilterResult result = SvgFilterPlayback.ApplyFullFrame(source, 8, 8, Resource(scene), Map, scratch, CancellationToken.None))
                Assert.Equal(native[alpha * 256 + coverage], result.Pixels[(3 * 8 + 2) * 4 + 3]);
            Assert.Equal(0, scratch.Snapshot().CurrentBytes);
            Assert.Equal(unchanged, source);
        }
        Assert.Equal(0, parser.Snapshot().CurrentBytes);
    }

    [Theory]
    [InlineData("<feGaussianBlur in='SourceAlpha' stdDeviation='.5' result='a'/>", 0, 0, 8, 8)]
    [InlineData("<feOffset in='SourceAlpha' dx='1' dy='-1' result='a'/>", 3, 1, 7, 5)]
    [InlineData("<feOffset in='SourceAlpha' dx='.5' dy='.5' result='a'/>", 2, 2, 7, 7)]
    [InlineData("<feOffset in='SourceAlpha' dx='-.5' dy='0' result='a'/>", 1, 2, 6, 6)]
    [InlineData("<feMerge result='a'><feMergeNode in='SourceAlpha'/><feMergeNode in='SourceGraphic'/></feMerge>", 2, 2, 6, 6)]
    [InlineData("<feGaussianBlur in='SourceAlpha' stdDeviation='.5' result='b'/><feMerge result='a'><feMergeNode in='SourceAlpha'/><feMergeNode in='b'/></feMerge>", 0, 0, 8, 8)]
    [InlineData("<feOffset in='SourceAlpha' dx='-1' dy='0' result='b'/><feMerge result='a'><feMergeNode in='SourceAlpha'/><feMergeNode in='b'/></feMerge>", 1, 2, 6, 6)]
    [InlineData("<feGaussianBlur in='SourceAlpha' stdDeviation='0' x='3' y='3' width='2' height='2' result='a'/>", 3, 3, 5, 5)]
    public void Final_nearest_offset_retains_its_input_image_extent_instead_of_the_filter_region(string inputGraph,
        float left, float top, float right, float bottom)
    {
        RenderBudget parser = new RenderBudget(new HostMemoryBudget(1_000_000));
        using (SvgRoot scene = Parse(inputGraph + "<feOffset in='a' dx='.5' dy='0'/>", parser))
        {
            byte[] source = Source(128);
            byte[] unchanged = source.ToArray();
            RenderBudget scratch = new RenderBudget(new HostMemoryBudget(1_000_000));
            using (SvgFilterResult result = SvgFilterPlayback.ApplyFullFrame(source, 8, 8, Resource(scene), Map, scratch, CancellationToken.None))
            {
                SvgFilterDeferredImage image = Assert.IsType<SvgFilterDeferredImage>(result.DeferredImage);
                Assert.Equal(SvgFilterImageSampling.Nearest, image.Sampling);
                Assert.Equal(new SvgPaintBounds(left, top, right, bottom), image.SourceBounds);
            }
            Assert.Equal(0, scratch.Snapshot().CurrentBytes);
            Assert.Equal(unchanged, source);
        }
        Assert.Equal(0, parser.Snapshot().CurrentBytes);
    }

    private static SvgFilterDeviceMap Map => new(1, 1)
    {
        Regions = new(SvgAffineTransform.Identity, new(8, 8), new(-20, -20, 20, 20))
        {
            SourceBounds = new(2, 2, 6, 6), DeferFinalOffset = true
        }
    };

    private static SvgRoot Parse(string graph, RenderBudget budget)
    {
        byte[] document = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs>"
            + "<filter id='f' filterUnits='userSpaceOnUse' x='-20' y='-20' width='40' height='40' color-interpolation-filters='sRGB'>"
            + graph + "</filter></defs><rect width='4' height='4' filter='url(#f)'/></svg>");
        return SvgRootParser.Parse(document, SvgRenderLimits.Default, budget, CancellationToken.None);
    }

    private static SvgFilterResource Resource(SvgRoot scene)
    {
        SvgFilterArena arena = scene.SceneStorage?.Filters ?? throw new InvalidOperationException();
        return arena.ResourceAt(arena.BindingAt(scene.Commands[0].MaskContentIndex).ResourceIndex);
    }

    private static byte[] Source(byte alpha)
    {
        byte[] source = new byte[8 * 8 * 4];
        for (int y = 2; y < 6; y++)
        for (int x = 2; x < 6; x++) source[(y * 8 + x) * 4 + 3] = alpha;
        return source;
    }
}
