using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgMarkerPaintBindingTests
{
    public static IEnumerable<object[]> PaintSources()
    {
        foreach (string paint in new[] { "linear", "radial", "pattern" })
        foreach (bool stroke in new[] { false, true })
        foreach (string shape in new[] { "rect", "circle", "path" })
        foreach (string binding in new[] { "source", "marker", "group" })
        foreach (string source in new[] { "direct", "named", "alias" })
            yield return [paint, stroke, shape, binding, source];
    }

    [Theory]
    [MemberData(nameof(PaintSources))]
    public void Marker_paints_bind_after_all_local_definitions_without_losing_inheritance(
        string paint, bool stroke, string shape, string binding, string source)
    {
        byte[] expected = Render(Document(paint, stroke, shape, binding, source, false));
        Assert.Equal(expected, Render(Document(paint, stroke, shape, binding, source, true)));
    }

    [Theory]
    [InlineData("linear", false)]
    [InlineData("linear", true)]
    [InlineData("radial", false)]
    [InlineData("radial", true)]
    public void Marker_hosts_in_pattern_tiles_rebind_document_paints(string paint, bool stroke)
    {
        byte[] early = Tile(Document(paint, stroke, "path", "marker", "direct", false));
        byte[] late = Tile(Document(paint, stroke, "path", "marker", "direct", true));
        Assert.Equal(Render(early), Render(late));
        Assert.Equal(Render(Document(paint, stroke, "path", "marker", "direct", false)), Render(late));

        static byte[] Tile(byte[] input)
        {
            const string host = "<line x2='1' marker-start='url(#m)'/>";
            string xml = System.Text.Encoding.UTF8.GetString(input).Replace(host, "", StringComparison.Ordinal);
            return System.Text.Encoding.UTF8.GetBytes(xml.Replace("</svg>",
                "<defs><pattern id='tile' patternUnits='userSpaceOnUse' width='32' height='32'><path d='M0 0L1 0' marker-start='url(#m)'/>"
                + "</pattern></defs><rect width='32' height='32' fill='url(#tile)'/></svg>", StringComparison.Ordinal));
        }
    }

    [Theory]
    [InlineData("linear", false)]
    [InlineData("radial", false)]
    [InlineData("pattern", false)]
    [InlineData("linear", true)]
    [InlineData("radial", true)]
    [InlineData("pattern", true)]
    public void Deferred_marker_owners_release_on_denial_cancellation_and_retry(string paint, bool stroke)
    {
        byte[] input = Document(paint, stroke, "path", "marker", "alias", true);
        byte[] original = input.ToArray();
        byte[] expected = Render(Document(paint, stroke, "path", "marker", "alias", false));
        RenderBudget measured = new(2_000_000);
        using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, measured, null, CancellationToken.None)) { }
        Assert.Equal(0, measured.Snapshot().CurrentBytes);
        for (long limit = 1; limit < measured.Snapshot().PeakBytes; limit += 512) DenyMemory(limit);
        DenyMemory(measured.Snapshot().PeakBytes - 1);
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        RenderBudget canceled = new(2_000_000);
        Assert.Equal(cancellation.Token, Assert.ThrowsAny<OperationCanceledException>(() =>
            SvgRootParser.Parse(input, SvgRenderLimits.Default, canceled, null, cancellation.Token)).CancellationToken);
        Assert.Equal(0, canceled.Snapshot().CurrentBytes);
        Assert.Equal(expected, Render(input));
        Assert.Equal(original, input);
        Assert.Equal(0, measured.Snapshot().CurrentBytes);

        void DenyMemory(long limit)
        {
            RenderBudget denied = new(limit + 73);
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
    public void Repeated_retained_marker_hosts_keep_linear_work(int count)
    {
        string xml = System.Text.Encoding.UTF8.GetString(Document("linear", false, "rect", "marker", "direct", true));
        const string host = "<line x2='1' marker-start='url(#m)'/>";
        xml = xml.Replace(host, string.Concat(Enumerable.Repeat(host, count)), StringComparison.Ordinal);
        RenderBudget budget = new(10_000_000);
        using (SvgRoot root = SvgRootParser.Parse(System.Text.Encoding.UTF8.GetBytes(xml), SvgRenderLimits.Default, budget, null, CancellationToken.None)) { }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    private static byte[] Document(string paint, bool stroke, string shape, string binding, string source, bool late)
    {
        string resource = paint switch
        {
            "linear" => "<linearGradient id='p'><stop stop-color='blue'/><stop offset='1' stop-color='blue'/></linearGradient>",
            "radial" => "<radialGradient id='p'><stop stop-color='blue'/><stop offset='1' stop-color='blue'/></radialGradient>",
            _ => "<pattern id='p' patternUnits='userSpaceOnUse' width='4' height='4'><rect width='4' height='4' fill='blue'/></pattern>"
        };
        resource = "<defs>" + resource + "</defs>";
        string start = shape switch
        {
            "rect" => "<rect x='4' y='4' width='16' height='16'",
            "circle" => "<circle cx='12' cy='12' r='8'",
            _ => "<path d='M4 4H20V20H4Z'"
        };
        string attributes = stroke ? " fill='none' stroke='url(#p)' stroke-width='2'" : " fill='url(#p)'";
        string leaf = start + (binding == "source" ? attributes : "") + (source == "direct" ? "" : " id='s'") + "/>";
        string content = binding == "group" ? "<g" + attributes + ">" + leaf + "</g>" : leaf;
        string extra = source == "direct" ? "" : "<defs>" + content + (source == "alias" ? "<g id='a'><use href='#s'/></g>" : "") + "</defs>";
        if (source != "direct") content = "<use href='#" + (source == "alias" ? "a" : "s") + "'/>";
        string marker = extra + "<defs><marker id='m' markerUnits='userSpaceOnUse' markerWidth='32' markerHeight='32' orient='0'"
            + (binding == "marker" ? attributes : "") + ">" + content + "</marker></defs>";
        const string host = "<line x2='1' marker-start='url(#m)'/>";
        return System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'>"
            + (late ? marker + host + resource : resource + marker + host) + "</svg>");
    }

    private static byte[] Render(byte[] input) => SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None).ToArray();
}
