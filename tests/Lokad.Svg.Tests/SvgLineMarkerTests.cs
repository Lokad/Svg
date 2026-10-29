using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgLineMarkerTests
{
    public static IEnumerable<object[]> MarkerHosts()
    {
        foreach (string consumer in new[] { "use", "symbol", "alias", "pattern", "mask" })
        foreach (string stroke in new[] { "omitted", "none", "zero" })
        foreach (bool mapped in new[] { false, true })
        foreach (bool instance in new[] { false, true })
        foreach (string role in new[] { "start", "end" })
            yield return [consumer, stroke, mapped, instance, role];
    }

    [Theory]
    [MemberData(nameof(MarkerHosts))]
    public void Markers_paint_without_host_strokes_through_instances_and_resources(
        string consumer, string stroke, bool mapped, bool instance, string role)
    {
        byte[] expected = Render(Document(true, consumer, stroke, mapped, instance, role, false));
        Assert.NotEqual(Render(Input("")), expected);
        foreach (bool forward in new[] { false, true })
            Assert.Equal(expected, Render(Document(false, consumer, stroke, mapped, instance, role, forward)));
    }

    [Theory]
    [InlineData("use")]
    [InlineData("symbol")]
    [InlineData("alias")]
    [InlineData("pattern")]
    public void Pending_markers_and_retained_corners_release_owners_on_denial_and_retry(string consumer)
    {
        byte[] input = Document(false, consumer, "none", true, false, "end", true);
        byte[] original = input.ToArray();
        byte[] expected = Render(Document(true, consumer, "none", true, false, "end", false));
        RenderBudget measured = new RenderBudget(new HostMemoryBudget(2_000_000));
        using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, measured, null, CancellationToken.None)) { }
        Assert.Equal(0, measured.Snapshot().CurrentBytes);
        for (long limit = 1; limit < measured.Snapshot().PeakBytes; limit += 512) DenyMemory(limit);
        DenyMemory(measured.Snapshot().PeakBytes - 1);
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        RenderBudget canceled = new RenderBudget(new HostMemoryBudget(2_000_000));
        Assert.Equal(cancellation.Token, Assert.ThrowsAny<OperationCanceledException>(() =>
            SvgRootParser.Parse(input, SvgRenderLimits.Default, canceled, null, cancellation.Token)).CancellationToken);
        Assert.Equal(0, canceled.Snapshot().CurrentBytes);
        using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, measured, null, CancellationToken.None))
        {
            long held = measured.Snapshot().CurrentBytes;
            byte[] pixels;
            using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, SvgRenderOptions.Default, measured, CancellationToken.None)) pixels = surface.Pixels.ToArray();
            Assert.Contains(pixels, value => value != 0);
            using (measured.Reserve(2_000_000 - held - 1024, SvgMemoryCategory.Parser))
            {
                long borrowed = measured.Snapshot().CurrentBytes;
                Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() =>
                {
                    using var denied = SvgConverter.Rasterize(root, SvgRenderOptions.Default, measured, CancellationToken.None);
                }).ErrorCode);
                Assert.Equal(borrowed, measured.Snapshot().CurrentBytes);
            }
            using (SvgConverter.RasterizedSurface retry = SvgConverter.Rasterize(root, SvgRenderOptions.Default, measured, CancellationToken.None)) Assert.Equal(pixels, retry.Pixels);
            Assert.Equal(held, measured.Snapshot().CurrentBytes);
        }
        Assert.Equal(0, measured.Snapshot().CurrentBytes);
        Assert.Equal(expected, Render(input));
        Assert.Equal(original, input);

        void DenyMemory(long limit)
        {
            RenderBudget denied = new RenderBudget(new HostMemoryBudget(limit + 73));
            using RenderBudget.BudgetReservation borrower = denied.Reserve(73, SvgMemoryCategory.Parser);
            Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() =>
                SvgRootParser.Parse(input, SvgRenderLimits.Default, denied, null, CancellationToken.None)).ErrorCode);
            Assert.Equal(73, denied.Snapshot().CurrentBytes);
        }
    }

    [Theory]
    [InlineData(32)]
    [InlineData(64)]
    [InlineData(128)]
    [InlineData(256)]
    public void Repeated_forward_marker_instances_keep_linear_work(int count)
    {
        string xml = System.Text.Encoding.UTF8.GetString(Document(false, "use", "none", true, false, "end", true));
        const string use = "<use href='#s'/>";
        xml = xml.Replace(use, string.Concat(Enumerable.Repeat(use, count)), StringComparison.Ordinal);
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(10_000_000));
        using (SvgRoot root = SvgRootParser.Parse(System.Text.Encoding.UTF8.GetBytes(xml), SvgRenderLimits.Default, budget, null, CancellationToken.None)) { }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    private static byte[] Document(bool path, string consumer, string stroke, bool mapped, bool instance, string role, bool forward)
    {
        const string marker = "<defs><marker id='m' markerUnits='userSpaceOnUse' markerWidth='8' markerHeight='8' refX='4' refY='4' orient='0'><rect width='4' height='8' fill='blue'/><rect x='4' width='4' height='8' fill='green'/></marker></defs>";
        string attributes = " marker-" + role + "='url(#m)'";
        string source = (path ? "<path d='M4 8L12 8'" : "<line x1='4' y1='8' x2='12' y2='8'") + " id='s' fill='none'"
            + (stroke == "none" ? " stroke='none'" : stroke == "zero" ? " stroke='black' stroke-width='0'" : "") + (instance ? "" : attributes) + "/>";
        if (instance && consumer is "pattern" or "mask") source = "<g" + attributes + ">" + source + "</g>";
        string use = "<use href='#s'" + (instance ? attributes : "") + "/>";
        string body = consumer switch
        {
            "symbol" => "<defs><symbol id='v' viewBox='0 0 32 32'>" + source + "</symbol></defs><use href='#v' width='32' height='32'" + (instance ? attributes : "") + "/>",
            "alias" => "<defs>" + source + "<g id='a'>" + use + "</g></defs><use href='#a'/>",
            "pattern" => "<defs><pattern id='p' patternUnits='userSpaceOnUse' width='32' height='32'>" + source + "</pattern></defs><rect width='32' height='32' fill='url(#p)'/>",
            "mask" => "<defs><mask id='q' mask-type='alpha' maskUnits='userSpaceOnUse' x='0' y='0' width='32' height='32'>" + source + "</mask></defs><rect width='32' height='32' fill='blue' mask='url(#q)'/>",
            _ => "<defs>" + source + "</defs>" + use
        };
        body = "<g" + (mapped ? " transform='matrix(1 .125 .125 1 1 1)'" : "") + ">" + body + "</g>";
        return Input(forward ? body + marker : marker + body);
    }

    private static byte[] Input(string body) => System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'>" + body + "</svg>");
    private static byte[] Render(byte[] input) => SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None).ToArray();
}
