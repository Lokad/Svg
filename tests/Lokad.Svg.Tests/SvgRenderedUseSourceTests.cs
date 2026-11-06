using TextEncoding = System.Text.Encoding;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgRenderedUseSourceTests
{
    [Theory]
    [Trait("Layer", "Mechanism")]
    [InlineData("clip-path='url(#c)'", "<rect width='8' height='8'/>")]
    [InlineData("mask='url(#m)'", "<rect width='8' height='8'/>")]
    [InlineData("", "<svg width='16' height='16'><rect width='8' height='8'/></svg>")]
    public void Referenced_rendered_groups_reject_source_clip_mask_and_nested_viewport_scopes(string attributes, string children)
    {
        const string definitions = "<defs><clipPath id='c'><rect width='8' height='8'/></clipPath><mask id='m'><rect width='8' height='8' fill='white'/></mask></defs>";
        string source = definitions + "<g id='source' " + attributes + ">" + children + "</g>";
        RenderBudget budget = new(new HostMemoryBudget(1_000_000));
        using (SvgRoot scene = SvgRootParser.Parse(Document(source), SvgRenderLimits.Default, budget, CancellationToken.None))
            Assert.False(scene.HasUnsupportedChild);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        SvgException error = Assert.Throws<SvgException>(() =>
            SvgRootParser.Parse(Document(source + "<use href='#source'/>"), SvgRenderLimits.Default, budget, CancellationToken.None));
        Assert.Equal(SvgErrorCode.UnsupportedFeature, error.ErrorCode);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Theory]
    [Trait("Layer", "Mechanism")]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Named_rendered_uses_retain_forward_sources_and_paint_order(bool group, bool forward)
    {
        const string definitions = "<defs><path id='shape' d='M2 2H10V10H2Z'/></defs>";
        string uses = "<use href='#placed' y='14'/><use id='placed' href='#shape' x='12'/>";
        if (group) uses = "<g id='container' transform='translate(3 4)'>" + uses + "</g>";
        byte[] input = Document((forward ? "" : definitions) + uses + (forward ? definitions : ""));
        using SvgRoot scene = SvgRootParser.Parse(input, SvgRenderLimits.Default, CancellationToken.None);
        Assert.Equal(2, scene.Geometries.Length);
        for (int index = 0; index < 2; index++)
        {
            SvgAuthoredPath path = scene.Geometries[index].AuthoredPath ?? throw new InvalidOperationException("Missing authored instance path.");
            SvgRectangle bounds = SvgPathBounds.Compute(path.Commands, path.SceneMap, CancellationToken.None);
            Assert.Equal((group ? 17d : 14d, (index == 0 ? 16d : 2d) + (group ? 4 : 0), 8d, 8d),
                (bounds.X, bounds.Y, bounds.Width, bounds.Height));
        }
    }

    [Theory]
    [Trait("Layer", "Mechanism")]
    [InlineData("")]
    [InlineData("href='' ")]
    public void A_named_absent_source_remains_an_empty_local_target(string href)
    {
        byte[] input = Document("<use id='empty' " + href + "/><use href='#empty'/>");
        using SvgRoot scene = SvgRootParser.Parse(input, SvgRenderLimits.Default, CancellationToken.None);
        RenderBudget budget = new(new HostMemoryBudget(1_000_000));
        using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(scene, SvgRenderOptions.Default, budget, CancellationToken.None))
            Assert.All(surface.Pixels, value => Assert.Equal(0, value));
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Theory]
    [Trait("Layer", "Contract")]
    [InlineData("<use id='a' href='#a'/>", SvgErrorCode.MalformedInput)]
    [InlineData("<use id='a' href='#b'/><use id='b' href='#a'/>", SvgErrorCode.MalformedInput)]
    [InlineData("<g id='container'><use id='a' href='#b'/><use id='b' href='#a'/></g>", SvgErrorCode.MalformedInput)]
    [InlineData("<use id='a' href='#missing'/>", SvgErrorCode.UnsupportedFeature)]
    [InlineData("<use id='a'/><path id='a' d='M1 1H3V3Z'/>", SvgErrorCode.MalformedInput)]
    [InlineData("<path id='a' d='M1 1H3V3Z'/><use id='a'/>", SvgErrorCode.MalformedInput)]
    [InlineData("<g id='a'/><use id='a'/>", SvgErrorCode.MalformedInput)]
    [InlineData("<use id='a'/><use id='a'/>", SvgErrorCode.MalformedInput)]
    public void Named_use_graph_failures_and_duplicate_ids_release_sources(string content, SvgErrorCode error)
    {
        RenderBudget budget = new(new HostMemoryBudget(1_000_000));
        Assert.Equal(error, Assert.Throws<SvgException>(() =>
            SvgRootParser.Parse(Document(content), SvgRenderLimits.Default, budget, CancellationToken.None)).ErrorCode);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Theory]
    [Trait("Layer", "Contract")]
    [InlineData(false)]
    [InlineData(true)]
    public void Standalone_and_borrowed_use_sources_release_at_admission_failures(bool group)
    {
        string content = "<use href='#a' y='14'/><use id='a' href='#b' x='4'/><use id='b' href='#shape' x='8'/>";
        if (group) content = "<g id='container' opacity='.6'>" + content + "</g>";
        byte[] input = Document(content + "<defs><path id='shape' d='M2 2H10V10H2Z'/></defs>");
        SvgParseOwnershipAssertions.PreservesBorrowedStorageOnDenialCancellationAndRetry(input);
        RenderBudget measured = new(new HostMemoryBudget(1_000_000));
        using (SvgRoot scene = SvgRootParser.Parse(input, SvgRenderLimits.Default, measured, CancellationToken.None)) { }
        Assert.Equal(0, measured.Snapshot().CurrentBytes);
        for (long limit = 1; limit < measured.Snapshot().PeakBytes; limit += 256)
        {
            RenderBudget denied = new(new HostMemoryBudget(limit));
            Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() =>
                SvgRootParser.Parse(input, SvgRenderLimits.Default, denied, CancellationToken.None)).ErrorCode);
            Assert.Equal(0, denied.Snapshot().CurrentBytes);
        }
    }

    [Fact]
    [Trait("Layer", "Rendering")]
    public void Group_use_replay_matches_independently_placed_source_geometry()
    {
        const string path = "<path fill='red' d='M1 1 Q7 1 7 7 L1 7 Z'/>";
        byte[] shared = TextEncoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='8'><g id='g'>" + path + "</g><use href='#g' x='8'/></svg>");
        byte[] independent = TextEncoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='8'>" + path + "<g transform='translate(8 0)'>" + path + "</g></svg>");
        Assert.Equal(SvgConverter.ConvertToPng(independent, SvgRenderOptions.Default, CancellationToken.None).ToArray(), SvgConverter.ConvertToPng(shared, SvgRenderOptions.Default, CancellationToken.None).ToArray());
    }

    private static byte[] Document(string content)
        => TextEncoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'>" + content + "</svg>");
}
