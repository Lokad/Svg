using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgLineFilterBoundsTests
{
    private const string Filter = "<defs><filter id='f' filterUnits='userSpaceOnUse' x='-64' y='-64' width='128' height='128' color-interpolation-filters='sRGB'><feOffset/></filter></defs>";
    private const string Paints = "<defs><linearGradient id='g' gradientUnits='userSpaceOnUse' x2='32'><stop stop-color='red'/><stop offset='1' stop-color='blue'/></linearGradient><pattern id='p' patternUnits='userSpaceOnUse' width='4' height='4'><rect width='2' height='4' fill='red'/><rect x='2' width='2' height='4' fill='blue'/></pattern></defs>";

    [Theory]
    [InlineData("butt", false)]
    [InlineData("round", false)]
    [InlineData("square", false)]
    [InlineData("butt", true)]
    [InlineData("round", true)]
    [InlineData("square", true)]
    public void Identity_filters_preserve_placed_line_strokes(string cap, bool groupFilter)
    {
        foreach ((string geometry, string path) in new[] { ("x1='4' y1='8' x2='12' y2='8'", "M4 8H12"), ("x1='8' y1='4' x2='8' y2='12'", "M8 4V12"), ("x1='4' y1='4' x2='12' y2='12'", "M4 4L12 12") })
        foreach (string transform in new[] { "translate(0 0)", "translate(8 8)", "scale(2 1)", "matrix(-1 0 0 1 24 8)" })
        foreach (string viewport in new[] { "", "<svg x='2' y='1' width='28' height='28'>", "<svg width='32' height='32' viewBox='0 0 16 16'>" })
        {
            string line = "<line " + geometry + " fill='none' stroke='red' stroke-width='4' stroke-linecap='" + cap + "'";
            // A path's default miter hint is wider than a line's, even without joins.
            // Matching those bounds keeps native source-layer raster rounding comparable.
            string control = path == "M4 4L12 12"
                ? "<path d='" + path + "' fill='none' stroke='red' stroke-width='4' stroke-linecap='" + cap + "' stroke-miterlimit='1' filter='url(#f)'/>"
                : line + "/>";
            string expected = "<g transform='" + transform + "'>" + control + "</g>";
            string actual = groupFilter
                ? "<g transform='" + transform + "' filter='url(#f)'>" + line + "/></g>"
                : line + " transform='" + transform + "' filter='url(#f)'/>";
            string end = viewport.Length == 0 ? string.Empty : "</svg>";
            Assert.True(Render(Filter + viewport + expected + end).AsSpan().SequenceEqual(Render(Filter + viewport + actual + end)),
                "Identity filter: " + geometry + "; " + transform + "; " + viewport);
        }
    }

    [Theory]
    [InlineData("red")]
    [InlineData("url(#g)")]
    [InlineData("url(#p)")]
    public void Instances_preserve_painted_line_bounds_in_both_source_orders(string paint)
    {
        foreach (string cap in new[] { "butt", "round", "square" })
        foreach (bool forward in new[] { false, true })
        foreach (bool filterOnSource in new[] { false, true })
        {
            string source = "<defs><line id='s' x1='4' y1='8' x2='12' y2='8' stroke='" + paint + "' stroke-width='4' stroke-linecap='" + cap + "' stroke-miterlimit='1'";
            const string use = "<use href='#s' transform='translate(8 8)'";
            string definition = source + (filterOnSource ? " filter='url(#f)'" : string.Empty) + "/></defs>";
            string instance = use + (filterOnSource ? string.Empty : " filter='url(#f)'") + "/>";
            string control = definition.Replace("<line id='s' x1='4' y1='8' x2='12' y2='8'", "<path id='s' d='M4 8H12'", StringComparison.Ordinal);
            Assert.True(Render(Filter + Paints + (forward ? instance + control : control + instance)).AsSpan().SequenceEqual(Render(Filter + Paints + (forward ? instance + definition : definition + instance))),
                "Instance filter: " + cap + "; forward=" + forward + "; filterOnSource=" + filterOnSource);
        }
    }

    [Theory]
    [InlineData("butt", "<feOffset dx='2' dy='-1'/>")]
    [InlineData("round", "<feOffset dx='2' dy='-1'/>")]
    [InlineData("square", "<feOffset dx='2' dy='-1'/>")]
    [InlineData("butt", "<feGaussianBlur stdDeviation='1'/>")]
    [InlineData("round", "<feGaussianBlur stdDeviation='1'/>")]
    [InlineData("square", "<feGaussianBlur stdDeviation='1'/>")]
    public void Filtered_lines_match_equivalent_paths(string cap, string primitive)
    {
        string filter = Filter.Replace("<feOffset/>", primitive, StringComparison.Ordinal);
        foreach (string transform in new[] { "translate(0 0)", "translate(8 8)", "scale(2 1)" })
        foreach (string paint in new[] { "red", "url(#g)", "url(#p)" })
        {
            string style = " transform='" + transform + "' fill='none' stroke='" + paint + "' stroke-width='4' stroke-linecap='" + cap + "' stroke-miterlimit='1' filter='url(#f)'/>";
            Assert.True(Render(filter + Paints + "<path d='M4 8H12'" + style).AsSpan().SequenceEqual(Render(filter + Paints + "<line x1='4' y1='8' x2='12' y2='8'" + style)),
                "Path equivalence: " + transform + "; " + paint);
        }
    }

    [Fact]
    public void Filter_bounds_preserve_marker_endpoints_and_element_opacity()
    {
        const string marker = "<defs><marker id='m' markerUnits='userSpaceOnUse' markerWidth='2' markerHeight='2' refY='1'><rect width='2' height='2' fill='blue'/></marker></defs>";
        const string line = "<line x1='4' y1='8' x2='12' y2='8' transform='translate(8 8)' stroke='red' stroke-width='4' stroke-linecap='square' marker-end='url(#m)' opacity='.5'";
        Assert.Equal(Render(marker + line + "/>"), Render(Filter + marker + line + " filter='url(#f)'/>"));
    }

    [Fact]
    public void Placed_filter_bounds_release_owners_after_denial_cancellation_and_retry()
    {
        const string line = "<line x1='4' y1='8' x2='12' y2='8' transform='translate(8 8)' stroke='red' stroke-width='4' stroke-linecap='square'";
        byte[] input = Input(Filter + line + " filter='url(#f)'/>");
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
        Assert.Equal(Render(line + "/>"), SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None).ToArray());
    }

    private static byte[] Input(string content)
        => System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'>" + content + "</svg>");

    private static byte[] Render(string content)
        => SvgConverter.ConvertToPng(Input(content), SvgRenderOptions.Default, CancellationToken.None).ToArray();
}
