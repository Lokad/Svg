using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed partial class OpenTypeFontTests
{
    [Fact]
    public void Empty_graph_surfaces_preserve_borrowed_pixels_and_release_after_denial_or_cancellation()
    {
        using SvgFilterResource resource = new(new(new(0, false), new(0, false), new(8, false), new(8, false), SvgFilterUnits.UserSpace),
            SvgFilterUnits.UserSpace, SvgFilterColorSpace.Srgb, new([], null), null);
        byte[] source = [255, 0, 0, 255];
        RenderBudget budget = new(1_000_000);
        long work;
        using (SvgFilterResult result = SvgFilterPlayback.Apply(source, 1, 1, resource, new(1, 1), budget, long.MaxValue, CancellationToken.None))
        {
            Assert.Equal(new byte[4], result.Pixels);
            work = result.WorkUnits;
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        RenderBudget tight = new(budget.Snapshot().PeakBytes - 1);
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgFilterPlayback.Apply(source, 1, 1, resource, new(1, 1), tight, work, CancellationToken.None)).ErrorCode);
        Assert.Equal(0, tight.Snapshot().CurrentBytes);
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgFilterPlayback.Apply(source, 1, 1, resource, new(1, 1), budget, work - 1, CancellationToken.None)).ErrorCode);
        using CancellationTokenSource cancelled = new();
        cancelled.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => SvgFilterPlayback.Apply(source, 1, 1, resource, new(1, 1), budget, work, cancelled.Token));
        using (SvgFilterResult retry = SvgFilterPlayback.Apply(source, 1, 1, resource, new(1, 1), budget, work, CancellationToken.None))
            Assert.Equal(new byte[4], retry.Pixels);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        Assert.Equal(new byte[] { 255, 0, 0, 255 }, source);
    }

    public static IEnumerable<object[]> EmptyFilterGraphCases()
    {
        foreach (string context in new[] { "direct", "group", "use-owner", "use-source", "symbol", "pattern" })
            foreach (string form in new[] { "self-closing", "balanced", "comment" })
                foreach (bool userSpace in new[] { false, true }) yield return [context, form, userSpace];
    }

    [Theory]
    [MemberData(nameof(EmptyFilterGraphCases))]
    public async Task An_empty_filter_graph_produces_empty_output_not_the_source(string context, string form, bool userSpace)
    {
        (var catalog, var provider, _) = CreateExamplePack(CreateTriangleFont());
        foreach (SvgColor canvas in new[] { SvgColor.Transparent, new SvgColor(255, 255, 255, 255) })
        {
            SvgRenderOptions options = new(null, 1, canvas, SvgRenderLimits.Default);
            byte[] expected = SvgConverter.ConvertToPng(Document("<rect width='1' height='1' fill='lime'/>"), options, CancellationToken.None).ToArray();
            foreach (bool effects in new[] { false, true })
            {
                byte[] input = Input("", effects);
                Assert.Equal(expected, SvgConverter.ConvertToPng(input, options, CancellationToken.None).ToArray());
                using SvgPreparedDocument prepared = await SvgPreparedDocument.CreateAsync(input, catalog, provider, options, CancellationToken.None);
                Assert.Equal(expected, prepared.ConvertToPng(CancellationToken.None).ToArray());
                Assert.NotEqual(expected, SvgConverter.ConvertToPng(Input("<feOffset/>", effects), options, CancellationToken.None).ToArray());
            }
        }

        byte[] Input(string primitive, bool effects)
        {
            string region = userSpace ? "filterUnits='userSpaceOnUse' x='0' y='0' width='8' height='8'" : "x='0' y='0' width='1' height='1'";
            string filter = "<filter id='f' " + region;
            filter += primitive.Length == 0 && form == "self-closing" ? "/>" : ">" + primitive + (form == "comment" ? "<!-- empty -->" : "") + "</filter>";
            const string source = "<rect x='2' y='2' width='4' height='4' fill='red'";
            string body = context switch
            {
                "group" => "<g filter='url(#f)'>" + source + "/></g>",
                "use-owner" => "<defs>" + source + " id='s'/></defs><use href='#s' filter='url(#f)'/>",
                "use-source" => "<defs>" + source + " id='s' filter='url(#f)'/></defs><use href='#s'/>",
                "symbol" => "<defs><symbol id='s'>" + source + " filter='url(#f)'/></symbol></defs><use href='#s'/>",
                "pattern" => "<defs><pattern id='s' width='8' height='8' patternUnits='userSpaceOnUse'>" + source + " filter='url(#f)'/></pattern></defs><rect width='8' height='8' fill='url(#s)'/>",
                _ => source + " filter='url(#f)'/>"
            };
            if (effects) body = "<g opacity='.5' clip-path='url(#c)' mask='url(#m)'>" + body + "</g>";
            return Document("<defs>" + filter + "<clipPath id='c'><rect width='7' height='7'/></clipPath><mask id='m' mask-type='alpha'><rect width='8' height='8' fill='white'/></mask></defs>" + body + "<rect width='1' height='1' fill='lime'/>");
        }
    }
}
