using System.Text;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgFilterClipBindingTests
{
    [Theory]
    [InlineData(128)]
    [InlineData(512)]
    [InlineData(2048)]
    public void Separate_filtered_object_clips_keep_linear_parser_work(int count)
    {
        RenderBudget budget = new(40_000_000);
        using (SvgRoot root = SvgRootParser.Parse(Repeated(count, true), SvgRenderLimits.Default, budget, null, CancellationToken.None)) { }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Ten_thousand_filtered_object_clips_match_user_space_clips_under_default_limits()
        => Assert.Equal(Render(Repeated(10_000, false)), Render(Repeated(10_000, true)));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Nested_filters_and_tile_scenes_keep_native_object_bounds_and_composition(bool polygon)
        => Assert.Equal(Render(Mixed(false, polygon)), Render(Mixed(true, polygon)));

    [Fact]
    public void Markers_do_not_expand_filtered_object_clip_bounds()
    {
        const string marker = "<marker id='dot' markerUnits='userSpaceOnUse' markerWidth='20' markerHeight='20' overflow='visible'><rect x='-10' y='-10' width='20' height='20' fill='blue'/></marker>";
        const string target = "<path d='M0 0H8V8H0Z' fill='red' marker-end='url(#dot)' filter='url(#f)' clip-path='url(#c)'/>";
        Assert.Equal(Render(Input(marker + Clip(false, false, true) + Filter, target)), Render(Input(marker + Clip(true, false, true) + Filter, target)));
    }

    [Fact]
    public void Clip_binding_releases_scratch_on_memory_denial_and_cancellation()
    {
        byte[] input = Mixed(true, true);
        RenderBudget measured = new(2_000_000);
        using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, measured, null, CancellationToken.None)) { }
        Assert.Equal(0, measured.Snapshot().CurrentBytes);
        long peak = measured.Snapshot().PeakBytes;
        for (long limit = 1; limit < peak; limit += 4096) DenyMemory(limit);
        DenyMemory(peak - 1);
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        RenderBudget cancelled = new(2_000_000);
        Assert.Equal(cancellation.Token, Assert.ThrowsAny<OperationCanceledException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, cancelled, null, cancellation.Token)).CancellationToken);
        Assert.Equal(0, cancelled.Snapshot().CurrentBytes);

        void DenyMemory(long limit)
        {
            RenderBudget denied = new(limit);
            Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, denied, CancellationToken.None)).ErrorCode);
            Assert.Equal(0, denied.Snapshot().CurrentBytes);
        }
    }

    private static byte[] Repeated(int count, bool objectBox)
    {
        StringBuilder body = new();
        for (int index = 0; index < count; index++) body.Append("<rect width='4' height='4' fill='red' filter='url(#f)' clip-path='url(#c)'/>");
        return Input(Clip(objectBox, false, false) + Filter, body.ToString());
    }

    private static byte[] Mixed(bool objectBox, bool polygon)
    {
        const string tile = "<pattern id='p' width='8' height='8' patternUnits='userSpaceOnUse'><rect width='8' height='8' fill='blue' filter='url(#f)' clip-path='url(#c)'/></pattern>";
        const string body = "<g filter='url(#f)' clip-path='url(#c)' opacity='.7'><g filter='url(#f)' clip-path='url(#c)'><use href='#r'/></g><rect width='8' height='8' fill='blue' stroke='green' stroke-width='2'/></g><rect width='8' height='8' fill='red' filter='url(#f)' clip-path='url(#c)' mask='url(#m)' opacity='.5'/><rect x='8' width='8' height='8' fill='url(#p)'/>";
        return Input(tile + Clip(objectBox, polygon, true) + Filter + "<rect id='r' width='8' height='8' fill='red'/><mask id='m'><rect width='16' height='8' fill='white'/></mask>", body);
    }

    private static string Clip(bool objectBox, bool polygon, bool half)
    {
        string width = objectBox ? half ? ".5" : "1" : half ? "4" : "8";
        string height = objectBox ? "1" : "8";
        string content = polygon ? "<path d='M0 0H" + width + "V" + height + "H0Z'/>" : "<rect width='" + width + "' height='" + height + "'/>";
        return "<clipPath id='c'" + (objectBox ? " clipPathUnits='objectBoundingBox'" : "") + ">" + content + "</clipPath>";
    }

    private const string Filter = "<filter id='f'><feOffset dx='0' dy='0'/></filter>";
    private static byte[] Input(string definitions, string body)
        => System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='8'><defs>" + definitions + "</defs>" + body + "</svg>");
    private static byte[] Render(byte[] input) => SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None).ToArray();
}
