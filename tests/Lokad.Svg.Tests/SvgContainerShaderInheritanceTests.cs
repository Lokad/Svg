using Lokad.Svg.Fonts;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgContainerShaderInheritanceTests
{
    [Theory]
    [InlineData("translate(4.25 2.5)")]
    [InlineData("scale(2)")]
    public void Fractional_point_list_patterns_render_through_inherited_placement_and_release_storage(string transform)
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='128' height='96'><defs><pattern id='p' patternUnits='userSpaceOnUse' width='8' height='6'><rect width='4' height='3'/></pattern></defs><g fill='url(#p)' transform='" + transform + "'><polygon points='4.5,6.25 25.25,7.5 23.5,23.25 3.25,21.5'/></g></svg>");
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(8_000_000));
        using (SvgRoot scene = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, null, CancellationToken.None))
        {
            using SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(scene, SvgRenderOptions.Default, budget, CancellationToken.None);
            Assert.Contains(surface.Pixels, value => value != 0);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Theory]
    [InlineData("linear-root-direct-transparent")]
    [InlineData("radial-group-direct-transparent")]
    [InlineData("pattern-symbol-fill-transparent")]
    [InlineData("linear-group-copy-transparent")]
    [InlineData("linear-root-use-transparent")]
    [InlineData("linear-alias-excludes-transparent")]
    [InlineData("linear-definition-excludes-transparent")]
    [InlineData("linear-native-shapes-transparent")]
    [InlineData("pattern-native-shapes-transparent")]
    [InlineData("pattern-group-copy-transparent")]
    [InlineData("tile-alpha-e93d8f-white")]
    [InlineData("current-color-border-transparent")]
    [InlineData("group-opacity-overlap-transparent")]
    [InlineData("masked-leaf-transparent")]
    [InlineData("image-no-fill-transparent")]
    public void Container_shader_inheritance_releases_all_owners_on_denial_and_cancellation(string identity)
    {
        byte[] input = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "container-shader-inheritance", identity + ".svg"));
        RenderBudget discovery = new RenderBudget(new HostMemoryBudget(8_000_000));
        Assert.Empty(SvgFontRequestDiscovery.Discover(input, SvgRenderLimits.Default, discovery, CancellationToken.None));
        Assert.Equal(0, discovery.Snapshot().CurrentBytes);
        // Container URL storage, native source replay and tile scratch share the existing budget.
        RenderBudget measured = new RenderBudget(new HostMemoryBudget(8_000_000));
        using (SvgRoot scene = SvgRootParser.Parse(input, SvgRenderLimits.Default, measured, null, CancellationToken.None))
        using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(scene, SvgRenderOptions.Default, measured, CancellationToken.None))
            {}
        Assert.Equal(0, measured.Snapshot().CurrentBytes);
        long peak = measured.Snapshot().PeakBytes;
        for (long limit = 1; limit < peak; limit += 4096) DenyMemory(limit);
        DenyMemory(peak - 1);
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        RenderBudget canceled = new RenderBudget(new HostMemoryBudget(8_000_000));
        Assert.Equal(cancellation.Token, Assert.Throws<OperationCanceledException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, canceled, null, cancellation.Token)).CancellationToken);
        Assert.Equal(0, canceled.Snapshot().CurrentBytes);
        using (SvgRoot scene = SvgRootParser.Parse(input, SvgRenderLimits.Default, canceled, null, CancellationToken.None))
        {
            Assert.Equal(cancellation.Token, Assert.Throws<OperationCanceledException>(() => SvgConverter.Rasterize(scene, SvgRenderOptions.Default, canceled, cancellation.Token)).CancellationToken);
            using SvgConverter.RasterizedSurface retried = SvgConverter.Rasterize(scene, SvgRenderOptions.Default, canceled, CancellationToken.None);
        }
        Assert.Equal(0, canceled.Snapshot().CurrentBytes);

        void DenyMemory(long limit)
        {
            RenderBudget budget = new RenderBudget(new HostMemoryBudget(limit));
            Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() =>
            {
                using SvgRoot scene = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, null, CancellationToken.None);
                using SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(scene, SvgRenderOptions.Default, budget, CancellationToken.None);
            }).ErrorCode);
            Assert.Equal(0, budget.Snapshot().CurrentBytes);
        }
    }
}
