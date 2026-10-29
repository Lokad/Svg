using System.Text;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgPatternDeferredPaintTests
{
    [Theory]
    [InlineData("rect", false)]
    [InlineData("rect", true)]
    [InlineData("circle", false)]
    [InlineData("circle", true)]
    [InlineData("ellipse", false)]
    [InlineData("path", false)]
    [InlineData("path", true)]
    [InlineData("polygon", false)]
    [InlineData("polyline", true)]
    [InlineData("line", false)]
    [InlineData("stroke", true)]
    [InlineData("promoted", false)]
    [InlineData("effects", false)]
    [InlineData("inherited", true)]
    public void Direct_tile_paints_render_identically_before_and_after_their_definitions(string kind, bool radial)
    {
        string leaf = kind switch
        {
            "rect" => "<rect x='.5' width='4' height='4' fill='url(#g)'/>",
            "circle" => "<circle cx='4' cy='4' r='3' fill='url(#g)'/>",
            "ellipse" => "<ellipse cx='4' cy='4' rx='3' ry='2' fill='url(#g)'/>",
            "path" => "<path d='M0 0H4V4H0Z' fill='url(#g)'/>",
            "polygon" => "<polygon points='0,0 4,0 2,6' fill='url(#g)'/>",
            "polyline" => "<polyline points='0,0 4,0 2,6' fill='none' stroke='url(#g)' stroke-width='1'/>",
            "line" => "<line x1='1' y1='1' x2='6' y2='6' stroke='url(#g)' stroke-width='1'/>",
            "stroke" => "<rect x='1' y='1' width='4' height='4' fill='none' stroke='url(#g)' stroke-width='1'/>",
            "promoted" => "<rect width='2' height='2' fill='blue'/><rect x='2' width='4' height='4' fill='url(#g)'/>",
            "effects" => "<rect width='6' height='6' fill='url(#g)' clip-path='url(#c)' mask='url(#m)' filter='url(#f)' opacity='.7'/>",
            "inherited" => "<g fill='url(#g)' opacity='.7'><rect width='4' height='4'/></g>",
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        Assert.Equal(Render(Build(false)), Render(Build(true)));

        byte[] Build(bool forward) => Input("<defs>" + (forward ? Pattern("p", leaf) + Gradient(radial) : Gradient(radial) + Pattern("p", leaf)) + Resources + "</defs>" + Output);
    }

    [Theory]
    [InlineData("patternContentUnits='objectBoundingBox'")]
    [InlineData("viewBox='0 0 8 8' preserveAspectRatio='none'")]
    [InlineData("patternTransform='translate(.5 .5) scale(.8)'")]
    public void Forward_paints_keep_tile_coordinate_and_viewport_maps(string attributes)
    {
        string leaf = attributes.Contains("objectBoundingBox", StringComparison.Ordinal)
            ? "<rect width='.3' height='.3' fill='url(#g)'/>" : "<rect width='4' height='4' fill='url(#g)'/>";
        Assert.Equal(Render(Build(false)), Render(Build(true)));

        byte[] Build(bool forward)
        {
            string pattern = "<pattern id='p' patternUnits='userSpaceOnUse' width='8' height='8' " + attributes + ">" + leaf + "</pattern>";
            return Input("<svg x='1' width='14' height='8'><defs>" + (forward ? pattern + Gradient(false) : Gradient(false) + pattern) + "</defs>" + Output + "</svg>");
        }
    }

    [Fact]
    public void Gradient_templates_resolve_before_tile_paint_replay()
    {
        const string leaf = "<rect width='4' height='4' fill='url(#g)'/>";
        string alias = Gradient(false).Replace("id='g'", "id='base'", StringComparison.Ordinal) + "<linearGradient id='g' href='#base'/>";
        Assert.Equal(Render(Input("<defs>" + Gradient(false) + Pattern("p", leaf) + "</defs>" + Output)),
            Render(Input("<defs>" + Pattern("p", leaf) + alias + "</defs>" + Output)));
    }

    [Fact]
    public void Paints_uses_and_scope_boundaries_expand_in_one_tile_order()
        => Assert.Equal(Render(Mixed(false)), Render(Mixed(true)));

    [Fact]
    public void Unused_and_anonymous_tile_paints_leave_neighboring_scenes_intact()
    {
        const string leaf = "<rect width='4' height='4' fill='url(#g)' mask='url(#m)' filter='url(#f)'/>";
        string unused = Pattern("unused", leaf) + Pattern("", leaf);
        byte[] expected = Input("<defs>" + Gradient(false) + Pattern("p", leaf) + Resources + "</defs><rect width='16' height='8' fill='url(#g)'/>" + Output);
        byte[] actual = Input("<defs>" + Pattern("p", leaf) + unused + Gradient(false) + Resources + "</defs><rect width='16' height='8' fill='url(#g)'/>" + Output);
        Assert.Equal(Render(expected), Render(actual));
    }

    [Theory]
    [InlineData(128)]
    [InlineData(512)]
    public void Tile_paint_replay_does_not_scan_unrelated_source_ranges(int count)
    {
        StringBuilder xml = new();
        for (int index = 0; index < count; index++) xml.Append("<g clip-path='url(#c)'><rect width='4' height='4' fill='url(#g)'/></g>");
        xml.Append("<defs>");
        for (int index = 0; index < count; index++) xml.Append(Pattern("p" + index, "<rect width='4' height='4' fill='url(#g)'/>"));
        xml.Append(Gradient(false)).Append(Resources).Append("</defs><rect width='16' height='8' fill='url(#p0)'/>");
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(40_000_000));
        using (SvgRoot root = SvgRootParser.Parse(Input(xml.ToString()), SvgRenderLimits.Default, budget, null, CancellationToken.None)) { }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Theory]
    [InlineData("", SvgErrorCode.UnsupportedFeature)]
    [InlineData("<pattern id='g' width='2' height='2' patternUnits='userSpaceOnUse'><rect width='1' height='1'/></pattern>", SvgErrorCode.UnsupportedFeature)]
    public void Missing_and_nested_pattern_paints_keep_their_public_error_categories(string resource, SvgErrorCode expected)
    {
        byte[] input = Input("<defs>" + Pattern("p", "<rect width='4' height='4' fill='url(#g)'/>") + resource + "</defs>" + Output);
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(2_000_000));
        Assert.Equal(expected, Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None)).ErrorCode);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        Assert.Equal(expected, Assert.Throws<SvgException>(() => Render(input)).ErrorCode);
    }

    [Fact]
    public void Tile_paint_replay_releases_owners_on_memory_denial_and_cancellation()
    {
        byte[] input = Mixed(true);
        RenderBudget measured = new RenderBudget(new HostMemoryBudget(2_000_000));
        using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, measured, null, CancellationToken.None)) { }
        Assert.Equal(0, measured.Snapshot().CurrentBytes);
        long peak = measured.Snapshot().PeakBytes;
        for (long limit = 1; limit < peak; limit += 4096) DenyMemory(limit);
        DenyMemory(peak - 1);
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        RenderBudget cancelled = new RenderBudget(new HostMemoryBudget(2_000_000));
        Assert.Equal(cancellation.Token, Assert.ThrowsAny<OperationCanceledException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, cancelled, null, cancellation.Token)).CancellationToken);
        Assert.Equal(0, cancelled.Snapshot().CurrentBytes);

        void DenyMemory(long limit)
        {
            RenderBudget denied = new RenderBudget(new HostMemoryBudget(limit));
            Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, denied, CancellationToken.None)).ErrorCode);
            Assert.Equal(0, denied.Snapshot().CurrentBytes);
        }
    }

    private static byte[] Mixed(bool forward)
    {
        const string content = "<use href='#r'/><g opacity='.7' clip-path='url(#c)' mask='url(#m)'><rect width='4' height='4' fill='url(#g)'/><use href='#r' x='4'/><g clip-path='url(#c)'><circle cx='4' cy='4' r='3' fill='url(#g)' filter='url(#f)' mask='url(#m)'/></g></g><use href='#r' x='6'/><rect x='6' y='6' width='2' height='2' fill='url(#g)'/>";
        string patterns = Pattern("p", content) + Pattern("q", "<rect width='2' height='2' fill='url(#g)'/>") + Pattern("", "<rect width='3' height='3' fill='url(#g)'/>");
        return Input("<defs>" + (forward ? patterns + Gradient(false) : Gradient(false) + patterns) + Resources + "</defs>" + Output);
    }

    private static string Gradient(bool radial) => radial
        ? "<radialGradient id='g'><stop stop-color='red'/><stop offset='1' stop-color='blue'/></radialGradient>"
        : "<linearGradient id='g'><stop stop-color='red'/><stop offset='1' stop-color='blue'/></linearGradient>";

    private static string Pattern(string id, string content)
        => "<pattern " + (id.Length == 0 ? "" : "id='" + id + "' ") + "width='8' height='8' patternUnits='userSpaceOnUse'>" + content + "</pattern>";

    private const string Resources = "<rect id='r' width='1' height='1' fill='green'/><clipPath id='c'><rect width='6' height='8'/></clipPath><mask id='m'><rect width='8' height='8' fill='white'/></mask><filter id='f'><feOffset dx='0' dy='0'/></filter>";
    private const string Output = "<rect width='16' height='8' fill='url(#p)'/>";

    private static byte[] Input(string content) => System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='8'>" + content + "</svg>");
    private static byte[] Render(byte[] input) => SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None).ToArray();
}
