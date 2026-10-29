using System.Globalization;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgLineClipCoverageTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Viewport_and_rectangle_clips_keep_the_visible_stroke_area(bool vertical, bool reversed)
    {
        foreach (int coordinate in new[] { -3, -2, -1, 0, 1, 8, 15, 16, 17, 18, 19 })
        {
            string relative = Line(vertical, reversed, coordinate, 0, 16);
            string absolute = Line(vertical, reversed, coordinate + 8, 8, 24);
            int start = Math.Max(8, 8 + coordinate - 2), end = Math.Min(24, 8 + coordinate + 2);
            string expected = end <= start ? string.Empty : vertical
                ? FormattableString.Invariant($"<rect x='{start}' y='8' width='{end - start}' height='16' fill='red'/>")
                : FormattableString.Invariant($"<rect x='8' y='{start}' width='16' height='{end - start}' fill='red'/>");
            Assert.Equal(Render(expected), Render("<svg x='8' y='8' width='16' height='16'>" + relative + " stroke='red' stroke-width='4'/></svg>"));
            const string clip = "<defs><clipPath id='c'><rect x='8' y='8' width='16' height='16'/></clipPath></defs>";
            string clipped = absolute + " stroke='red' stroke-width='4' clip-path='url(#c)'/>";
            Assert.Equal(Render(expected), Render(clip + clipped));
            Assert.Equal(Render(expected), Render(clipped + clip));
        }
    }

    [Theory]
    [InlineData("butt")]
    [InlineData("round")]
    [InlineData("square")]
    public void Caps_dashes_opacity_and_instances_match_equivalent_paths(string cap)
    {
        foreach (string dash in new[] { "none", "0 0", "99 1" })
        foreach (string opacity in new[] { "1", ".5" })
        {
            string style = " fill='none' stroke='red' stroke-width='4' stroke-linecap='" + cap + "' stroke-dasharray='" + dash + "' opacity='" + opacity + "'";
            string line = "<line x1='0' y1='0' x2='16' y2='0'" + style + "/>";
            string path = "<path d='M0 0H16'" + style + "/>";
            const string viewport = "<svg x='8' y='8' width='16' height='16'>";
            byte[] expected = Render(viewport + path + "</svg>");
            Assert.Equal(expected, Render(viewport + line + "</svg>"));
            foreach (bool forward in new[] { false, true })
            {
                string source = "<defs>" + line.Replace("<line ", "<line id='s' ", StringComparison.Ordinal) + "</defs>";
                string use = viewport + "<use href='#s'/></svg>";
                Assert.Equal(expected, Render(forward ? use + source : source + use));
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Fractional_clips_preserve_path_equivalent_coverage(bool vertical)
    {
        const string clip = "<defs><clipPath id='c'><rect x='8.25' y='8.5' width='16.5' height='15.75'/></clipPath></defs>";
        string line = Line(vertical, false, 8, 0, 32);
        string path = vertical ? "<path d='M8 0V32'" : "<path d='M0 8H32'";
        const string style = " fill='none' stroke='red' stroke-width='4' opacity='.5' clip-path='url(#c)'/>";
        Assert.Equal(Render(clip + path + style), Render(clip + line + style));
    }

    [Fact]
    public void Clipping_keeps_source_end_markers_at_the_authored_endpoint()
    {
        const string marker = "<defs><marker id='m' markerUnits='userSpaceOnUse' markerWidth='4' markerHeight='4' viewBox='0 0 4 4' refX='2' refY='2'><rect width='4' height='4' fill='blue'/></marker></defs>";
        const string viewport = "<svg x='8' y='8' width='16' height='16'>";
        const string style = " fill='none' stroke='red' stroke-width='4' marker-end='url(#m)'/></svg>";
        Assert.Equal(Render(marker + viewport + "<path d='M0 0H32'" + style), Render(marker + viewport + "<line x1='0' y1='0' x2='32' y2='0'" + style));
    }

    [Fact]
    public void Denial_and_cancellation_release_clipped_line_storage_and_allow_retry()
    {
        byte[] input = Input("<svg x='8' y='8' width='16' height='16'><line x1='0' y1='-1' x2='16' y2='-1' stroke='red' stroke-width='4'/></svg>");
        RenderBudget measured = new(SvgRenderLimits.Default.MaximumAccountedBytes);
        using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, measured, null, CancellationToken.None)) { }
        Assert.Equal(0, measured.Snapshot().CurrentBytes);
        RenderBudget denied = new(measured.Snapshot().PeakBytes - 1);
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, denied, CancellationToken.None)).ErrorCode);
        Assert.Equal(0, denied.Snapshot().CurrentBytes);
        RenderBudget workDenied = new(SvgRenderLimits.Default.MaximumAccountedBytes);
        Assert.Equal(0, workDenied.Snapshot().CurrentBytes);
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        RenderBudget cancelled = new(SvgRenderLimits.Default.MaximumAccountedBytes);
        Assert.Equal(cancellation.Token, Assert.Throws<OperationCanceledException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, cancelled, cancellation.Token)).CancellationToken);
        Assert.Equal(0, cancelled.Snapshot().CurrentBytes);
        Assert.Equal(Render("<rect x='8' y='8' width='16' height='1' fill='red'/>"), SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None).ToArray());
    }

    private static string Line(bool vertical, bool reversed, int coordinate, int start, int end)
    {
        if (reversed) (start, end) = (end, start);
        return vertical
            ? string.Create(CultureInfo.InvariantCulture, $"<line x1='{coordinate}' y1='{start}' x2='{coordinate}' y2='{end}'")
            : string.Create(CultureInfo.InvariantCulture, $"<line x1='{start}' y1='{coordinate}' x2='{end}' y2='{coordinate}'");
    }

    private static byte[] Input(string content)
        => System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'>" + content + "</svg>");

    private static byte[] Render(string content)
        => SvgConverter.ConvertToPng(Input(content), SvgRenderOptions.Default, CancellationToken.None).ToArray();
}
