using Lokad.Svg.Fonts;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed partial class OpenTypeFontTests
{
    [Fact]
    public async Task Nested_viewport_text_keeps_intervening_group_positions_and_device_font_size()
    {
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync([CreateResolution(CreateTriangleFont())], CancellationToken.None);
        byte[] input = "<svg xmlns='http://www.w3.org/2000/svg' width='160' height='130'><g transform='translate(9 7)'><svg x='13' y='11' width='90' height='90' viewBox='0 0 45 45'><g transform='translate(3 2)'><text font-family='Example' font-size='50' x='1' y='30'>A</text></g></svg></g></svg>"u8.ToArray();
        byte[] expected = "<svg xmlns='http://www.w3.org/2000/svg' width='160' height='130'><text font-family='Example' font-size='100' x='30' y='82'>A</text></svg>"u8.ToArray();
        RenderBudget actualBudget = new(long.MaxValue), expectedBudget = new(long.MaxValue);
        using (SvgRoot actual = SvgRootParser.Parse(input, SvgRenderLimits.Default, actualBudget, snapshot, new(long.MaxValue), CancellationToken.None))
        using (SvgRoot direct = SvgRootParser.Parse(expected, SvgRenderLimits.Default, expectedBudget, snapshot, new(long.MaxValue), CancellationToken.None))
        {
            Assert.Equal(direct.Geometries.Single().Glyph.Contours.SelectMany(contour => contour.Segments), actual.Geometries.Single().Glyph.Contours.SelectMany(contour => contour.Segments));
        }
        Assert.Equal(0, actualBudget.Snapshot().CurrentBytes);
        Assert.Equal(0, expectedBudget.Snapshot().CurrentBytes);
    }

    [Fact]
    public async Task Cascade_and_viewport_glyph_owners_release_after_denial_and_cancellation()
    {
        byte[] input = "<svg xmlns='http://www.w3.org/2000/svg' id='root' width='128' height='100'><style>#root{font-family:Example;font-size:100px;fill:red} #v{fill:blue}</style><svg id='v' x='15' y='5' width='80' height='80'><text y='75'>A</text></svg><text y='95'>A</text></svg>"u8.ToArray();
        (SvgFontPackCatalog catalog, CountingFontProvider provider, CountingFontSource source) = CreateExamplePack(CreateTriangleFont());
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync(input, catalog, provider, CancellationToken.None);
        Assert.Equal("Example", Assert.Single(SvgFontRequestDiscovery.Discover(input, CancellationToken.None)).Family);
        Assert.Equal(1, provider.RequestCount); Assert.Equal(1, source.RequestCount);
        byte[] unchanged = input.ToArray();
        RenderBudget baseline = new(long.MaxValue); ParserWorkCounter work = new(long.MaxValue);
        using (SvgRoot scene = SvgRootParser.Parse(input, SvgRenderLimits.Default, baseline, snapshot, work, CancellationToken.None))
        {
            Assert.Equal(2, scene.Geometries.Length);
            Assert.Contains(scene.Commands.ToArray(), command => command.Kind == SvgDrawCommandKind.BeginGroup && command.RegionKind is SvgLayerRegionKind.Viewport or SvgLayerRegionKind.AffineViewport);
        }
        Assert.Equal(0, baseline.Snapshot().CurrentBytes);
        RenderBudget denied = new(baseline.Snapshot().PeakBytes - 1);
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, denied, snapshot, new(long.MaxValue), CancellationToken.None)).ErrorCode);
        Assert.Equal(0, denied.Snapshot().CurrentBytes);
        RenderBudget workDenied = new(long.MaxValue);
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, workDenied, snapshot, new ParserWorkCounter(work.Used - 1), CancellationToken.None)).ErrorCode);
        Assert.Equal(0, workDenied.Snapshot().CurrentBytes);
        using CancellationTokenSource cancellation = new(); cancellation.Cancel();
        Assert.Equal(cancellation.Token, Assert.ThrowsAny<OperationCanceledException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, workDenied, snapshot, new(long.MaxValue), cancellation.Token)).CancellationToken);
        Assert.Equal(0, workDenied.Snapshot().CurrentBytes);
        using (SvgRoot retry = SvgRootParser.Parse(input, SvgRenderLimits.Default, workDenied, snapshot, new(long.MaxValue), CancellationToken.None)) Assert.Equal(2, retry.Geometries.Length);
        Assert.Equal(0, workDenied.Snapshot().CurrentBytes);
        Assert.Equal(unchanged, input);
    }
}
