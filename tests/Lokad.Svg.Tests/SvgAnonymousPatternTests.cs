using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgAnonymousPatternTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Unused_filtered_masked_pattern_leaves_root_output_unchanged(bool named)
        => Assert.Equal(Render(Input("", "")), Render(Input(Pattern(named), "")));

    [Fact]
    public void Anonymous_cleanup_preserves_neighboring_pattern_uses_and_scopes()
    {
        const string before = "<pattern id='before' width='8' height='8' patternUnits='userSpaceOnUse'><g clip-path='url(#c)'><use href='#r'/></g></pattern>";
        const string after = "<pattern id='after' width='8' height='8' patternUnits='userSpaceOnUse'><g opacity='.5' mask='url(#m)'><use href='#r'/></g></pattern>";
        const string anonymous = "<pattern width='8' height='8' patternUnits='userSpaceOnUse'><g clip-path='url(#c)'><use href='#r'/></g><rect width='4' height='4' filter='url(#f)' mask='url(#m)'/></pattern>";
        const string output = "<rect width='4' height='8' fill='url(#before)'/><rect x='4' width='4' height='8' fill='url(#after)'/>";
        Assert.Equal(Render(Input(before + after, output)), Render(Input(before + anonymous + after, output)));
    }

    [Fact]
    public void Anonymous_cleanup_releases_owners_on_work_memory_denial_and_cancellation()
    {
        byte[] input = Input(Pattern(false), "");
        RenderBudget measured = new(2_000_000);
        ParserWorkCounter work = new(100_000);
        using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, measured, null, work, CancellationToken.None)) { }
        Assert.Equal(0, measured.Snapshot().CurrentBytes);
        for (long limit = 0; limit < work.Used; limit++)
        {
            RenderBudget denied = new(2_000_000);
            Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, denied, null, new(limit), CancellationToken.None)).ErrorCode);
            Assert.Equal(0, denied.Snapshot().CurrentBytes);
        }
        long peak = measured.Snapshot().PeakBytes;
        for (long limit = 1; limit < peak; limit += 4096) DenyMemory(limit);
        DenyMemory(peak - 1);
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        RenderBudget cancelled = new(2_000_000);
        Assert.Equal(cancellation.Token, Assert.ThrowsAny<OperationCanceledException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, cancelled, null, new(100_000), cancellation.Token)).CancellationToken);
        Assert.Equal(0, cancelled.Snapshot().CurrentBytes);

        void DenyMemory(long limit)
        {
            RenderBudget denied = new(limit);
            Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, denied, CancellationToken.None)).ErrorCode);
            Assert.Equal(0, denied.Snapshot().CurrentBytes);
        }
    }

    private static string Pattern(bool named)
        => "<pattern " + (named ? "id='unused' " : "") + "width='8' height='8' patternUnits='userSpaceOnUse'><rect width='4' height='4' fill='red' mask='url(#m)' filter='url(#f)'/></pattern>";

    private static byte[] Input(string patterns, string output)
        => System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs>" + patterns
            + "<rect id='r' width='8' height='8' fill='red'/><clipPath id='c'><rect width='8' height='8'/></clipPath><filter id='f'><feOffset dx='0' dy='0'/></filter><mask id='m'><rect width='8' height='8' fill='white'/></mask></defs><rect width='8' height='8' fill='blue'/>" + output + "</svg>");

    private static byte[] Render(byte[] input) => SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None).ToArray();
}
