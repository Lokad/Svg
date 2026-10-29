using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

[Trait("Layer", "Mechanisms")]
public sealed class SvgFilterAlphaStageMechanismTests
{
    [Theory]
    [InlineData("<feOffset dx='0' dy='0'/>", false)]
    [InlineData("<feOffset in='SourceAlpha' dx='0' dy='0'/>", true)]
    [InlineData("<feGaussianBlur in='SourceAlpha' stdDeviation='0'/>", true)]
    [InlineData("<feGaussianBlur in='SourceAlpha' stdDeviation='.02999999'/>", true)]
    [InlineData("<feGaussianBlur in='SourceAlpha' stdDeviation='.030000002'/>", false)]
    [InlineData("<feGaussianBlur in='SourceAlpha' stdDeviation='.5'/>", false)]
    [InlineData("<feMerge><feMergeNode in='SourceAlpha'/></feMerge>", true)]
    [InlineData("<feMerge><feMergeNode in='SourceAlpha'/><feMergeNode in='SourceAlpha'/></feMerge>", false)]
    [InlineData("<feOffset in='SourceAlpha' dx='1' dy='0'/>", true)]
    [InlineData("<feOffset in='SourceAlpha' dx='.0001' dy='0'/>", true)]
    [InlineData("<feOffset in='SourceAlpha' dx='0' dy='0' result='a'/><feGaussianBlur in='a' stdDeviation='0' result='b'/><feMerge><feMergeNode in='b'/></feMerge>", true)]
    [InlineData("<feGaussianBlur in='SourceAlpha' stdDeviation='.5' result='a'/><feGaussianBlur in='a' stdDeviation='0'/>", false)]
    [InlineData("<feGaussianBlur in='SourceAlpha' stdDeviation='.5' result='a'/><feOffset in='a' dx='0' dy='0'/>", false)]
    [InlineData("<feOffset in='SourceAlpha' dx='.5' dy='0'/>", true)]
    [InlineData("<feOffset in='SourceAlpha' dx='.5' dy='0' result='a'/><feOffset in='a' dx='.5' dy='0'/>", false)]
    [InlineData("<feOffset in='SourceAlpha' dx='.5' dy='0' result='a'/><feGaussianBlur in='a' stdDeviation='0' result='b'/><feOffset in='b' dx='.5' dy='0'/>", false)]
    [InlineData("<feOffset in='SourceAlpha' dx='.5' dy='0' result='a'/><feMerge result='b'><feMergeNode in='a'/></feMerge><feOffset in='b' dx='.5' dy='0'/>", false)]
    public void Image_free_aliases_and_nearest_offsets_retain_the_source_alpha_sampling_stage(string graph, bool retained)
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='1' height='1'><defs><filter id='f' color-interpolation-filters='sRGB'>"
            + graph + "</filter></defs><g filter='url(#f)'><rect width='1' height='1'/></g></svg>");
        RenderBudget parser = new RenderBudget(new HostMemoryBudget(1_000_000));
        using SvgRoot scene = SvgRootParser.Parse(input, SvgRenderLimits.Default, parser, CancellationToken.None);
        SvgFilterArena arena = scene.SceneStorage?.Filters ?? throw new InvalidOperationException();
        SvgFilterResource resource = arena.ResourceAt(arena.BindingAt(scene.Commands[0].MaskContentIndex).ResourceIndex);
        RenderBudget raster = new RenderBudget(new HostMemoryBudget(1_000_000));
        using SvgFilterResult result = SvgFilterPlayback.ApplyFullFrame([32, 64, 96, 160], 1, 1, resource, new(1, 1), raster, CancellationToken.None);
        Assert.Equal(retained ? SvgFilterDeferredColorFilter.SourceAlpha : SvgFilterDeferredColorFilter.None, result.SamplingColorFilter);
    }
}
