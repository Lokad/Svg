using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgZeroDashTests
{
    private const string Paints = "<defs><linearGradient id='g' gradientUnits='userSpaceOnUse' x2='32'><stop stop-color='red'/><stop offset='1' stop-color='blue'/></linearGradient><pattern id='p' width='4' height='4' patternUnits='userSpaceOnUse'><rect width='4' height='4' fill='red'/></pattern></defs>";

    [Theory]
    [InlineData("<line x1='4' y1='16' x2='28' y2='16'")]
    [InlineData("<polyline points='4,8 28,8 28,24'")]
    [InlineData("<polygon points='4,8 28,8 28,24 4,24'")]
    [InlineData("<path d='M4 8H28V24H4Z'")]
    [InlineData("<path d='M4 16Q16 0 28 16'")]
    [InlineData("<rect x='4' y='8' width='24' height='16'")]
    [InlineData("<rect x='4' y='8' width='24' height='16' rx='3'")]
    [InlineData("<circle cx='16' cy='16' r='10'")]
    [InlineData("<ellipse cx='16' cy='16' rx='10' ry='6'")]
    public void Zero_lists_match_solid_strokes_for_each_shape_paint_and_cap(string shape)
    {
        foreach (string paint in new[] { "red", "url(#g)", "url(#p)" })
        foreach (string cap in new[] { "butt", "round", "square" })
        {
            string styled = shape + " fill='none' stroke='" + paint + "' stroke-width='2' stroke-linecap='" + cap + "'";
            byte[] expected = Render(Paints + styled + "/>");
            Assert.NotEqual(Render(string.Empty), expected);
            foreach (string dash in new[] { "0", "0 0", "0 0 0", "0px -0px", "0e0, 0" })
                Assert.Equal(expected, Render(Paints + styled + " stroke-dasharray='" + dash + "'/>"));
            if (cap == "butt")
                Assert.Equal(Render(string.Empty), Render(Paints + styled + " stroke-dasharray='0 4'/>"));
        }
    }

    [Theory]
    [InlineData("<path d='M4 16Q16 0 28 16'")]
    [InlineData("<circle cx='16' cy='16' r='10'")]
    public void Cascade_and_instances_preserve_zero_list_solid_strokes(string shape)
    {
        foreach (bool forward in new[] { false, true })
        foreach (string dash in new[] { "none", "0 0" })
        {
            string source = "<defs>" + shape + " id='s' fill='none' stroke='red' stroke-width='2'/></defs>";
            string use = "<use href='#s' stroke-dasharray='" + dash + "'/>";
            byte[] expected = Render(shape + " fill='none' stroke='red' stroke-width='2'/>");
            Assert.Equal(expected, Render(forward ? use + source : source + use));
            Assert.Equal(expected, Render("<g stroke-dasharray='" + dash + "'>" + shape + " fill='none' stroke='red' stroke-width='2'/></g>"));
            Assert.Equal(expected, Render(shape + " fill='none' stroke='red' stroke-width='2' style='stroke-dasharray:" + dash + "'/>"));
            Assert.Equal(expected, Render("<style>.zero{stroke-dasharray:" + dash + "}</style>" + shape + " class='zero' fill='none' stroke='red' stroke-width='2'/>"));
            Assert.Equal(expected, Render(shape + " fill='none' stroke='red' stroke-width='2' stroke-dasharray='" + dash + "' pathLength='0'/>"));
        }
    }

    [Fact]
    public void Denial_and_cancellation_release_zero_list_geometry_and_allow_retry()
    {
        byte[] input = Input("<circle cx='16' cy='16' r='10' fill='none' stroke='red' stroke-width='2' stroke-dasharray='0 0'/>");
        RenderBudget measured = new(SvgRenderLimits.Default.MaximumAccountedBytes);
        using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, measured, CancellationToken.None)) { }
        Assert.Equal(0, measured.Snapshot().CurrentBytes);
        RenderBudget denied = new(measured.Snapshot().PeakBytes - 1);
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, denied, CancellationToken.None)).ErrorCode);
        Assert.Equal(0, denied.Snapshot().CurrentBytes);
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        RenderBudget cancelled = new(SvgRenderLimits.Default.MaximumAccountedBytes);
        Assert.Equal(cancellation.Token, Assert.Throws<OperationCanceledException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, cancelled, cancellation.Token)).CancellationToken);
        Assert.Equal(0, cancelled.Snapshot().CurrentBytes);
        Assert.Equal(Render("<circle cx='16' cy='16' r='10' fill='none' stroke='red' stroke-width='2'/>"), SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None).ToArray());
    }

    private static byte[] Render(string content)
        => SvgConverter.ConvertToPng(Input(content), SvgRenderOptions.Default, CancellationToken.None).ToArray();

    private static byte[] Input(string content)
        => System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'>" + content + "</svg>");
}
