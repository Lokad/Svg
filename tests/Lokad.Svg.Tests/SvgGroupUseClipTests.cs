using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgGroupUseClipTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(100)]
    public void Rendered_group_instances_do_not_disable_other_polygon_clips(int x)
    {
        foreach (bool forward in new[] { false, true })
        foreach (bool useFirst in new[] { false, true })
        {
            string source = "<g id='g'><rect width='2' height='2' fill='blue'/></g>";
            string use = "<use href='#g' x='" + x + "'/>";
            string clip = "<polygon points='0,0 8,0 0,8' fill='red' clip-path='url(#c)'/>";
            string body = useFirst ? use + clip : clip + use;
            body = forward ? body + source : source + body;
            byte[] input = Input(body);
            byte[] control = Input(body.Replace(use, "<rect x='" + x + "' width='2' height='2' fill='blue'/>"));
            Assert.Equal(Render(control), Render(input));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Polygon_clips_on_rendered_group_instances_match_direct_shapes(bool forward)
    {
        string source = "<g id='g'><rect width='8' height='8' fill='blue'/></g>";
        string use = "<use href='#g' clip-path='url(#c)'/>";
        string body = forward ? use + source : source + use;
        Assert.Equal(Render(Input(body.Replace(use, "<rect width='8' height='8' fill='blue' clip-path='url(#c)'/>"))), Render(Input(body)));
    }

    [Fact]
    public void Group_use_and_polygon_clip_release_storage_at_work_and_memory_boundaries()
    {
        byte[] input = Input("<g id='g'><rect width='2' height='2' fill='blue'/></g><use href='#g' x='100'/><polygon points='0,0 8,0 0,8' fill='red' clip-path='url(#c)'/>");
        RenderBudget measured = new(1_000_000);
        ParserWorkCounter work = new(10_000);
        using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, measured, null, work, CancellationToken.None)) { }
        Assert.Equal(0, measured.Snapshot().CurrentBytes);
        for (long limit = 0; limit < work.Used; limit++)
        {
            RenderBudget denied = new(1_000_000);
            Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, denied, null, new(limit), CancellationToken.None)).ErrorCode);
            Assert.Equal(0, denied.Snapshot().CurrentBytes);
        }
        long peak = measured.Snapshot().PeakBytes;
        for (long limit = 1; limit < peak; limit += 4096) DenyMemory(limit);
        DenyMemory(peak - 1);

        void DenyMemory(long limit)
        {
            RenderBudget denied = new(limit);
            Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, denied, CancellationToken.None)).ErrorCode);
            Assert.Equal(0, denied.Snapshot().CurrentBytes);
        }
    }

    private static byte[] Input(string body) => System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><clipPath id='c'><path d='M0 0 L8 4 L0 8Z'/></clipPath></defs>" + body + "</svg>");
    private static byte[] Render(byte[] input) => SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None).ToArray();
}
