using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgAffineRectangleStrokeTests
{
    [Theory]
    [InlineData("0", "24", "matrix(1 .25 -.3 1 8 2)")]
    [InlineData("32", "0", "matrix(1 .25 -.3 1 8 2)")]
    [InlineData("0", "24", "matrix(0 1 -1 0 60 0)")]
    [InlineData("32", "0", "matrix(0 1 -1 0 60 0)")]
    public void Empty_mapped_rectangles_do_not_paint_or_remove_siblings(string width, string height, string transform)
    {
        string sibling = "<rect width='4' height='4' fill='blue'/>";
        string rectangle = "<rect width='" + width + "' height='" + height + "' stroke='red' stroke-width='8' transform='" + transform + "'/>";
        Assert.Equal(Render(sibling), Render(rectangle + sibling));
    }

    [Theory]
    [InlineData("-.5")]
    [InlineData("2147483648")]
    public void Invalid_mapped_stroke_widths_reject_and_release(string width)
    {
        byte[] input = Input("<rect x='12' y='12' width='32' height='24' fill='none' stroke='red' stroke-width='" + width + "' transform='matrix(1 .25 -.3 1 8 2)'/>");
        RenderBudget budget = new(1_000_000);
        Assert.Equal(SvgErrorCode.UnsupportedFeature, Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None)).ErrorCode);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Theory]
    [InlineData("userSpaceOnUse")]
    [InlineData("objectBoundingBox")]
    public void Rectangular_clip_maps_preserve_unit_outcomes_and_release(string units)
    {
        byte[] input = Input("<defs><clipPath id='c' clipPathUnits='" + units + "'><rect width='1' height='1'/></clipPath></defs><rect width='32' height='24' stroke='red' transform='rotate(17)' clip-path='url(#c)'/>");
        RenderBudget budget = new(1_000_000);
        if (units == "userSpaceOnUse")
            Assert.Equal(SvgErrorCode.UnsupportedFeature, Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None)).ErrorCode);
        else
        {
            using SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None);
            Assert.Equal(Render("<defs><clipPath id='c' clipPathUnits='objectBoundingBox'><path d='M0 0 H1 V1 H0 Z'/></clipPath></defs><rect width='32' height='24' stroke='red' transform='rotate(17)' clip-path='url(#c)'/>"),
                SvgConverter.ConvertToPng(root, SvgRenderOptions.Default, budget, CancellationToken.None).ToArray());
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void General_rectangle_source_replay_observes_work_memory_and_cancellation()
    {
        byte[] input = Input("<g transform='translate(2.25 1.5)'><rect x='12' y='12' width='32' height='24' fill='orange' stroke='blue' stroke-width='3.75' transform='matrix(1 .25 -.3 1 8 2)'/></g>");
        byte[] expected = SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None).ToArray();
        RenderBudget measured = new(1_000_000);
        ParserWorkCounter work = new(100_000);
        using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, measured, null, work, CancellationToken.None))
            Assert.NotEmpty(root.Geometries.ToArray());
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
        Assert.Equal(expected, SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None).ToArray());
    }

    private static byte[] Input(string content) => System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='96' height='72'>" + content + "</svg>");
    private static byte[] Render(string content) => SvgConverter.ConvertToPng(Input(content), SvgRenderOptions.Default, CancellationToken.None).ToArray();
}
