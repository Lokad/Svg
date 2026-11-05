using TextEncoding = System.Text.Encoding;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgFilterSourceDependencyTests
{
    [Theory]
    [Trait("Layer", "Mechanism")]
    [InlineData("", false, new bool[] { })]
    [InlineData("<feFlood/>", false, new bool[] { true })]
    [InlineData("<feMerge/>", false, new bool[] { true })]
    [InlineData("<feMerge><feMergeNode in='SourceGraphic'/></feMerge>", true, new bool[] { true })]
    [InlineData("<feMerge><feMergeNode in='SourceAlpha'/></feMerge>", true, new bool[] { true })]
    [InlineData("<feGaussianBlur in='SourceGraphic'/>", true, new bool[] { true })]
    [InlineData("<feGaussianBlur in='SourceAlpha'/>", true, new bool[] { true })]
    [InlineData("<feOffset in='SourceGraphic'/>", true, new bool[] { true })]
    [InlineData("<feDropShadow in='SourceGraphic'/>", true, new bool[] { true })]
    [InlineData("<feColorMatrix in='SourceGraphic' type='saturate' values='.5'/>", true, new bool[] { true })]
    [InlineData("<feBlend in='SourceGraphic' in2='SourceAlpha'/>", true, new bool[] { true })]
    [InlineData("<feComposite in='SourceGraphic' in2='SourceAlpha'/>", true, new bool[] { true })]
    [InlineData("<feFlood result='p'/><feBlend in='p' in2='SourceAlpha'/>", true, new bool[] { true, true })]
    [InlineData("<feFlood result='p'/><feComposite in='p' in2='SourceAlpha'/>", true, new bool[] { true, true })]
    [InlineData("<feGaussianBlur in='SourceAlpha' result='unused'/><feFlood result='p'/><feColorMatrix in='p'/>", false, new bool[] { false, true, true })]
    [InlineData("<feGaussianBlur in='SourceAlpha' result='live'/><feFlood/><feMerge><feMergeNode in='SourceGraphic'/><feMergeNode in='live'/></feMerge>", true, new bool[] { true, false, true })]
    [InlineData("<feFlood result='p'/><feColorMatrix in='p' result='m'/><feOffset in='m' result='o'/><feMerge><feMergeNode in='m'/><feMergeNode in='o'/></feMerge>", false, new bool[] { true, true, true, true })]
    [InlineData("<feFlood result='p'/><feComposite in='p' in2='p'/>", false, new bool[] { true, true })]
    public void Bounds_and_pixel_demand_share_reachable_stages_and_source_dependence(string graph, bool source, bool[] expected)
    {
        using HostMemoryBudget host = new(2_000_000);
        RenderBudget budget = new(host);
        using (SvgRoot root = Parse(graph, budget))
        {
            SvgFilterResource resource = Resource(root);
            Span<bool> needed = stackalloc bool[resource.Primitives.Items.Length];
            needed.Fill(true);
            int count = SvgFilterPlayback.MarkPixelDependencies(resource, needed, CancellationToken.None, out bool dependsOnSource);
            Assert.Equal(expected.Count(static value => value), count);
            Assert.Equal(expected, needed.ToArray());
            Assert.Equal(source, dependsOnSource);
        }
        Assert.Equal(0, host.Reserved);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Theory]
    [Trait("Layer", "Mechanism")]
    [InlineData(false)]
    [InlineData(true)]
    public void Maximum_chains_retain_every_stage_and_the_first_nodes_source_dependence(bool source)
    {
        string graph = (source ? "<feOffset/>" : "<feFlood/>")
            + string.Concat(Enumerable.Repeat("<feOffset/>", 255));
        using HostMemoryBudget host = new(2_000_000);
        RenderBudget budget = new(host);
        using (SvgRoot root = Parse(graph, budget))
        {
            Span<bool> needed = stackalloc bool[256];
            Assert.Equal(256, SvgFilterPlayback.MarkPixelDependencies(Resource(root), needed, CancellationToken.None, out bool dependsOnSource));
            Assert.All(needed.ToArray(), Assert.True);
            Assert.Equal(source, dependsOnSource);
        }
        Assert.Equal(0, host.Reserved);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    [Trait("Layer", "Contract")]
    public void Source_independent_output_retains_nested_mapped_validation_and_cleanup()
    {
        byte[] input = TextEncoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='64' height='64'><defs>"
            + "<filter id='outer' filterUnits='userSpaceOnUse' x='0' y='0' width='64' height='64'><feFlood result='p'/><feColorMatrix in='p'/></filter>"
            + "<filter id='inner' filterUnits='userSpaceOnUse' x='0' y='0' width='64' height='64'><feDropShadow stdDeviation='136'/></filter>"
            + "</defs><g filter='url(#outer)'><rect x='4' y='4' width='56' height='56' filter='url(#inner)'/></g></svg>");
        using HostMemoryBudget host = new(2_000_000);
        RenderBudget budget = new(host);
        using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None))
        {
            SvgException error = Assert.Throws<SvgException>(() =>
            {
                using SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, SvgRenderOptions.Default, budget, CancellationToken.None);
            });
            Assert.Equal(SvgErrorCode.UnsupportedFeature, error.ErrorCode);
            Assert.Equal("Drop-shadow deviations above 135 device pixels per axis are not implemented.", error.Message);
        }
        Assert.Equal(0, host.Reserved);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    private static SvgRoot Parse(string graph, RenderBudget budget) => SvgRootParser.Parse(
        TextEncoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><filter id='f'>"
            + graph + "</filter></defs><rect width='8' height='8' filter='url(#f)'/></svg>"),
        SvgRenderLimits.Default, budget, CancellationToken.None);

    private static SvgFilterResource Resource(SvgRoot root)
        => (root.FilterResources ?? throw new InvalidDataException("Missing filter resource.")).ResourceAt(0);
}
