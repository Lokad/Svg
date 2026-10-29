using System.Globalization;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgLineDashDirectionTests
{
    private const string Paints = "<defs><linearGradient id='g' gradientUnits='userSpaceOnUse' x2='32'><stop stop-color='red'/><stop offset='1' stop-color='blue'/></linearGradient><pattern id='p' patternUnits='userSpaceOnUse' width='4' height='4'><rect width='2' height='4' fill='red'/></pattern></defs>";

    [Theory]
    [InlineData(false, "butt")]
    [InlineData(true, "butt")]
    [InlineData(false, "square")]
    [InlineData(true, "square")]
    public void Reversed_dashes_follow_authored_endpoints_with_offsets_and_path_length(bool vertical, string cap)
    {
        foreach (string dash in new[] { "5 3", "3 1 2" })
        foreach (string offset in new[] { "0", "1", "-2", ".5" })
        foreach (string length in new[] { "", " pathLength='9'", " pathLength='12'", " pathLength='0'" })
        foreach (string opacity in new[] { "1", ".5" })
        {
            const int start = 25, end = 7;
            string style = " fill='none' stroke='red' stroke-width='4' stroke-linecap='" + cap + "' stroke-dasharray='" + dash + "' stroke-dashoffset='" + offset + "' opacity='" + opacity + "'" + length + "/>";
            string path = vertical ? "M16 " + start + "V" + end : "M" + start + " 16H" + end;
            Assert.True(Render("<path d='" + path + "'" + style).AsSpan().SequenceEqual(Render(Line(vertical, start, end) + style)),
                "dash=" + dash + "; offset=" + offset + "; length=" + length + "; opacity=" + opacity);
        }
    }

    [Theory]
    [InlineData(false, "butt")]
    [InlineData(true, "butt")]
    [InlineData(false, "square")]
    [InlineData(true, "square")]
    public void Forward_dashes_retain_analytical_painted_coverage(bool vertical, string cap)
    {
        int extension = cap == "square" ? 2 : 0;
        string rectangles = string.Empty;
        foreach ((int origin, int length) in new[] { (7, 5), (15, 5), (23, 2) })
        {
            rectangles += vertical
                ? FormattableString.Invariant($"<rect x='14' y='{origin - extension}' width='4' height='{length + 2 * extension}' fill='red'/>")
                : FormattableString.Invariant($"<rect x='{origin - extension}' y='14' width='{length + 2 * extension}' height='4' fill='red'/>");
        }
        Assert.Equal(Render(rectangles), Render(Line(vertical, 7, 25) + " stroke='red' stroke-width='4' stroke-linecap='" + cap + "' stroke-dasharray='5 3'/>"));
    }

    [Theory]
    [InlineData(false, "butt")]
    [InlineData(true, "butt")]
    [InlineData(false, "square")]
    [InlineData(true, "square")]
    public void Reversed_dashes_match_analytical_painted_rectangles(bool vertical, string cap)
    {
        int extension = cap == "square" ? 2 : 0;
        string rectangles = string.Empty;
        foreach ((int origin, int length) in new[] { (20, 5), (12, 5), (7, 2) })
        {
            rectangles += vertical
                ? FormattableString.Invariant($"<rect x='14' y='{origin - extension}' width='4' height='{length + 2 * extension}' fill='red'/>")
                : FormattableString.Invariant($"<rect x='{origin - extension}' y='14' width='{length + 2 * extension}' height='4' fill='red'/>");
        }
        Assert.Equal(Render(rectangles), Render(Line(vertical, 25, 7) + " stroke='red' stroke-width='4' stroke-linecap='" + cap + "' stroke-dasharray='5 3'/>"));
    }

    [Theory]
    [InlineData("red", "butt")]
    [InlineData("red", "square")]
    [InlineData("url(#g)", "butt")]
    [InlineData("url(#g)", "square")]
    [InlineData("url(#p)", "butt")]
    [InlineData("url(#p)", "square")]
    public void Instances_preserve_direction_paints_opacity_and_clips(string paint, string cap)
    {
        const string clip = "<defs><clipPath id='c'><rect x='10' y='10' width='12' height='12'/></clipPath></defs>";
        string style = " id='s' fill='none' stroke='" + paint + "' stroke-width='4' stroke-linecap='" + cap + "' stroke-dasharray='5 3' stroke-dashoffset='-2' opacity='.5'/></defs>";
        string line = "<defs><line x1='25' y1='16' x2='7' y2='16'" + style;
        string path = "<defs><path d='M25 16H7'" + style;
        foreach (bool forward in new[] { false, true })
        foreach (bool clipped in new[] { false, true })
        foreach (string viewport in new[] { "", "<svg x='2' y='1' width='28' height='28'>", "<svg width='32' height='32' viewBox='0 0 16 16'>" })
        {
            string use = viewport + "<use href='#s'" + (clipped ? " clip-path='url(#c)'" : string.Empty) + "/>" + (viewport.Length == 0 ? string.Empty : "</svg>");
            Assert.Equal(Render(Paints + clip + (forward ? use + path : path + use)), Render(Paints + clip + (forward ? use + line : line + use)));
        }
    }

    [Fact]
    public void Native_dash_placement_keeps_authored_marker_endpoints()
    {
        const string marker = "<defs><marker id='m' markerUnits='userSpaceOnUse' markerWidth='2' markerHeight='2' refY='1'><rect width='2' height='2' fill='blue'/></marker></defs>";
        const string style = " fill='none' stroke='red' stroke-width='4' stroke-linecap='square' stroke-dasharray='5 3' marker-start='url(#m)' marker-end='url(#m)'/>";
        Assert.Equal(Render(marker + "<path d='M25 16H7'" + style), Render(marker + "<line x1='25' y1='16' x2='7' y2='16'" + style));
    }

    [Fact]
    public void Reversed_dash_storage_releases_on_denial_cancellation_and_retry()
    {
        const string style = " fill='none' stroke='red' stroke-width='4' stroke-dasharray='5 3'/>";
        byte[] input = Input("<line x1='25' y1='16' x2='7' y2='16'" + style);
        RenderBudget measured = new(1_000_000);
        using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, measured, null, CancellationToken.None)) { }
        Assert.Equal(0, measured.Snapshot().CurrentBytes);
        RenderBudget denied = new(measured.Snapshot().PeakBytes - 1);
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, denied, CancellationToken.None)).ErrorCode);
        Assert.Equal(0, denied.Snapshot().CurrentBytes);
        RenderBudget workDenied = new(1_000_000);
        Assert.Equal(0, workDenied.Snapshot().CurrentBytes);
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        RenderBudget cancelled = new(1_000_000);
        Assert.Equal(cancellation.Token, Assert.Throws<OperationCanceledException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, cancelled, cancellation.Token)).CancellationToken);
        Assert.Equal(0, cancelled.Snapshot().CurrentBytes);
        Assert.Equal(Render("<path d='M25 16H7'" + style), SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None).ToArray());
    }

    private static string Line(bool vertical, int start, int end)
        => vertical
            ? string.Create(CultureInfo.InvariantCulture, $"<line x1='16' y1='{start}' x2='16' y2='{end}'")
            : string.Create(CultureInfo.InvariantCulture, $"<line x1='{start}' y1='16' x2='{end}' y2='16'");

    private static byte[] Input(string content)
        => System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'>" + content + "</svg>");

    private static byte[] Render(string content)
        => SvgConverter.ConvertToPng(Input(content), SvgRenderOptions.Default, CancellationToken.None).ToArray();
}
