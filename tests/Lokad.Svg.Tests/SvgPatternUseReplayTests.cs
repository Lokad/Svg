using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgPatternUseReplayTests
{
    [Theory]
    [InlineData("<use href='#missing'/>", "", SvgErrorCode.UnsupportedFeature)]
    [InlineData("<use href='other.svg#shape'/>", "", SvgErrorCode.UnsupportedFeature)]
    [InlineData("<use href='#loop'/>", "<g id='loop'><use href='#loop'/></g>", SvgErrorCode.MalformedInput)]
    [InlineData("<use href='#shape'/>", "<g id='shape'><rect width='8' height='8' fill='url(#p)'/></g>", SvgErrorCode.UnsupportedFeature)]
    [InlineData("<use href='#shape' mask='url(#m)'/>", "<rect id='shape' width='8' height='8'/>", SvgErrorCode.UnsupportedFeature)]
    [InlineData("<use href='#shape' clip-path='url(#c)'/>", "<rect id='shape' width='8' height='8'/>", SvgErrorCode.UnsupportedFeature)]
    public void Invalid_tile_reference_dependencies_reject_and_release(string use, string definitions, SvgErrorCode expected)
    {
        byte[] input = Input("<defs><pattern id='p' patternUnits='userSpaceOnUse' width='12' height='12' viewBox='0 0 12 12'>" + use + "</pattern>" + definitions + "</defs><rect width='64' height='48' fill='url(#p)'/>");
        RenderBudget budget = new(1_000_000);
        Assert.Equal(expected, Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None)).ErrorCode);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Theory]
    [InlineData("<desc><animate attributeName='x'/></desc>")]
    [InlineData("<metadata><script>invalid</script></metadata>")]
    [InlineData("<title/><rect width='1' height='1'/>")]
    [InlineData("<desc><style>rect { fill: red }</style></desc>")]
    public void Active_or_rendered_children_still_reject_inside_geometric_leaves(string children)
    {
        byte[] input = Input("<path d='M0 0H64V48H0Z'>" + children + "</path>");
        Assert.Equal(SvgErrorCode.UnsupportedFeature, Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None)).ErrorCode);
    }

    [Fact]
    public void Multiple_pending_tile_scenes_observe_denial_cancellation_and_cleanup()
    {
        byte[] input = Input("<defs><pattern id='p' patternUnits='userSpaceOnUse' width='12' height='12' viewBox='0 0 12 12'><rect width='12' height='12' fill='blue'/><use href='#shape'/><rect x='6' y='6' width='5' height='5' fill='red'/></pattern><pattern id='q' patternUnits='userSpaceOnUse' width='12' height='12' viewBox='0 0 12 12'><use href='#shape'/><use href='#shape' x='3'/></pattern><g id='shape'><circle cx='4' cy='4' r='3' fill='green'/></g></defs><rect width='32' height='48' fill='url(#p)'/><rect x='32' width='32' height='48' fill='url(#q)'/>");
        RenderBudget measured = new(1_000_000);
        ParserWorkCounter work = new(100_000);
        using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, measured, null, work, CancellationToken.None))
        {
            Assert.False(root.HasUnsupportedChild);
            Assert.Equal(2, root.Geometries.Length);
            foreach (SvgGeometry geometry in root.Geometries)
                Assert.NotEmpty((geometry.Pattern.VectorContent ?? throw new InvalidOperationException()).Scene.Commands);
        }
        Assert.Equal(0, measured.Snapshot().CurrentBytes);
        foreach (long limit in new[] { 1, work.Used / 2, work.Used - 1 })
        {
            RenderBudget denied = new(1_000_000);
            Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, denied, null, new(limit), CancellationToken.None)).ErrorCode);
            Assert.Equal(0, denied.Snapshot().CurrentBytes);
        }
        RenderBudget tight = new(measured.Snapshot().PeakBytes - 1);
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, tight, null, new(100_000), CancellationToken.None)).ErrorCode);
        Assert.Equal(0, tight.Snapshot().CurrentBytes);
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        Assert.Equal(cancellation.Token, Assert.ThrowsAny<OperationCanceledException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, measured, null, new(100_000), cancellation.Token)).CancellationToken);
        Assert.Equal(0, measured.Snapshot().CurrentBytes);
        Assert.NotEmpty(SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None).ToArray());
    }

    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    public void Radial_end_stop_padding_uses_absolute_radius_coordinates(int stopCount)
    {
        SvgColor red = new(255, 0, 0, 255), blue = new(0, 0, 255, 255);
        SvgGradientStop[] stops = stopCount == 2 ? [new(.25, red), new(.75, blue)]
            : [new(.25, red), new(.4, red), new(.6, blue), new(.75, blue)];
        SvgShaderPaint inner = SvgShaderPaint.RadialRamp(stops, red, blue, .25, .75, .1, 1, 32);
        SvgShaderPaint outer = SvgShaderPaint.RadialRamp(stops, red, blue, .25, .75, .9, 1, 32);
        Assert.InRange(inner.Red, .99f, 1f);
        Assert.InRange(inner.Blue, 0f, .01f);
        Assert.InRange(outer.Red, 0f, .01f);
        Assert.InRange(outer.Blue, .99f, 1f);
    }

    private static byte[] Input(string content) => System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='64' height='48'>" + content + "</svg>");
}
