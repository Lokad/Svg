using Lokad.Svg.Encoding;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgFilterDisplayTests
{
    private const string Definitions = "<defs><filter id='f' filterUnits='userSpaceOnUse' x='0' y='0' width='32' height='32'><feFlood flood-color='red'/></filter><rect id='source' x='4' y='4' width='8' height='8'/></defs>";
    private const string Sibling = "<rect x='28' y='28' width='2' height='2' fill='blue'/>";

    [Theory]
    [InlineData("rect")]
    [InlineData("path")]
    [InlineData("circle")]
    [InlineData("ellipse")]
    [InlineData("polygon")]
    [InlineData("line")]
    [InlineData("image")]
    [InlineData("use")]
    public void Hidden_filtered_targets_and_their_ancestors_do_not_paint(string kind)
    {
        string shape = kind switch
        {
            "rect" => "<rect x='4' y='4' width='8' height='8' $/>",
            "path" => "<path d='M4 4H12V12H4Z' $/>",
            "circle" => "<circle cx='8' cy='8' r='4' $/>",
            "ellipse" => "<ellipse cx='8' cy='8' rx='4' ry='3' $/>",
            "polygon" => "<polygon points='4,4 12,4 12,12 4,12' $/>",
            "line" => "<line x1='4' y1='4' x2='12' y2='12' stroke='blue' $/>",
            "image" => "<image x='4' y='4' width='8' height='8' href='data:image/png;base64," + Convert.ToBase64String(PngEncoder.Encode(1, 1, [255, 0, 0, 255], CancellationToken.None)) + "' $/>",
            "use" => "<use href='#source' $/>",
            _ => throw new ArgumentException("Unexpected shape.", nameof(kind))
        };
        string visible = shape.Replace("$", "filter='url(#f)'", StringComparison.Ordinal);
        Assert.Equal(Render("<rect width='32' height='32' fill='red'/>"), Render(visible));
        byte[] expected = Render(Sibling);
        foreach (string declaration in new[] { "display='none'", "style='display:none'", "class='hidden'" })
            Assert.Equal(expected, Render(shape.Replace("$", "filter='url(#f)' " + declaration, StringComparison.Ordinal) + "<style>.hidden{display:none}</style>" + Sibling));
        Assert.Equal(expected, Render("<g display='none'>" + visible + "</g>" + Sibling));
    }

    [Theory]
    [InlineData("<defs><rect id='s' width='8' height='8' filter='url(#f)'/></defs><use href='#s' display='none'/>")]
    [InlineData("<use href='#s' display='none' filter='url(#f)'/><defs><rect id='s' width='8' height='8'/></defs>")]
    [InlineData("<defs><rect id='s' width='8' height='8' display='none' filter='url(#f)'/></defs><use href='#s'/>")]
    [InlineData("<defs><symbol id='s'><rect width='8' height='8' display='none' filter='url(#f)'/></symbol></defs><use href='#s'/>")]
    [InlineData("<defs><symbol id='s'><g display='none' filter='url(#f)'><rect width='8' height='8'/></g></symbol></defs><use href='#s'/>")]
    [InlineData("<defs><symbol id='s'><use href='#source' display='none' filter='url(#f)'/></symbol></defs><use href='#s'/>")]
    [InlineData("<defs><g id='s'><rect width='8' height='8' filter='url(#f)'/></g></defs><use href='#s' display='none'/>")]
    [InlineData("<defs><symbol id='s'><rect width='8' height='8'/></symbol></defs><use href='#s' display='none' filter='url(#f)'/>")]
    public void Retained_sources_and_instances_respect_effective_display(string content)
        => Assert.Equal(Render(Sibling), Render(content + Sibling));

    [Theory]
    [InlineData("<defs><pattern id='p' patternUnits='userSpaceOnUse' width='32' height='32'><rect width='8' height='8' display='none' filter='url(#f)'/></pattern></defs><rect width='32' height='32' fill='url(#p)'/>")]
    [InlineData("<defs><pattern id='p' patternUnits='userSpaceOnUse' width='32' height='32'><use href='#source' display='none' filter='url(#f)'/></pattern></defs><rect width='32' height='32' fill='url(#p)'/>")]
    [InlineData("<defs><mask id='m' mask-type='alpha' maskUnits='userSpaceOnUse' x='0' y='0' width='32' height='32'><use href='#source' display='none' filter='url(#f)'/></mask></defs><rect width='32' height='32' fill='red' mask='url(#m)'/>")]
    public void Hidden_resource_children_do_not_contribute_filter_output(string content)
        => Assert.Equal(Render(Sibling), Render(content + Sibling));

    [Theory]
    [InlineData("fill='none'")]
    [InlineData("fill-opacity='0'")]
    [InlineData("fill='rgba(0,0,0,0)'")]
    public void Visible_paintless_sources_can_still_generate_filter_output(string paint)
        => Assert.Equal(Render("<rect width='32' height='32' fill='red'/>"), Render("<rect width='8' height='8' " + paint + " filter='url(#f)'/>"));

    [Fact]
    public void Element_opacity_applies_after_the_filter()
        => Assert.Equal(Render(Sibling), Render("<rect width='8' height='8' opacity='0' filter='url(#f)'/>" + Sibling));

    [Fact]
    public void Winning_cascade_controls_filter_activation()
    {
        Assert.Equal(Render("<rect width='32' height='32' fill='red'/>"), Render("<rect id='target' width='8' height='8' display='none' filter='url(#f)'/><style>#target{display:block!important}</style>"));
        Assert.Equal(Render(Sibling), Render("<rect id='target' width='8' height='8' style='display:block' filter='url(#f)'/><style>#target{display:none!important}</style>" + Sibling));
    }

    [Fact]
    public void Display_on_a_filter_resource_ancestor_does_not_disable_the_resource()
    {
        const string filter = "<g display='none'><defs><filter id='other' filterUnits='userSpaceOnUse' x='0' y='0' width='32' height='32'><feFlood flood-color='red'/></filter></defs></g>";
        Assert.Equal(Render("<rect width='32' height='32' fill='red'/>"), Render(filter + "<rect width='8' height='8' filter='url(#other)'/>"));
    }

    [Theory]
    [InlineData("<rect width='-1' height='8' display='none' filter='url(#f)'/>", SvgErrorCode.MalformedInput)]
    [InlineData("<rect width='8' height='8' display='none' transform='unknown(1)' filter='url(#f)'/>", SvgErrorCode.UnsupportedFeature)]
    [InlineData("<rect width='8' height='8' display='none' filter='blur(1px)'/>", SvgErrorCode.UnsupportedFeature)]
    [InlineData("<defs><mask id='m' mask-type='alpha' maskUnits='userSpaceOnUse' x='0' y='0' width='32' height='32'><rect width='8' height='8' display='none' filter='url(#f)'/></mask></defs><rect width='32' height='32' fill='red' mask='url(#m)'/>", SvgErrorCode.UnsupportedFeature)]
    public void Hidden_targets_keep_geometry_and_filter_syntax_admission(string content, SvgErrorCode code)
    {
        RenderBudget budget = new(1_000_000);
        Assert.Equal(code, Assert.Throws<SvgException>(() => SvgRootParser.Parse(Input(content), SvgRenderLimits.Default, budget, CancellationToken.None)).ErrorCode);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Denial_cancellation_and_retry_release_filter_and_replay_owners()
    {
        const string content = "<use href='#s' display='none' filter='url(#f)'/><defs><rect id='s' width='8' height='8' filter='url(#f)'/></defs>" + Sibling;
        byte[] input = Input(content);
        RenderBudget baseline = new(1_000_000);
        using (SvgRoot scene = SvgRootParser.Parse(input, SvgRenderLimits.Default, baseline, null, CancellationToken.None)) { }
        Assert.Equal(0, baseline.Snapshot().CurrentBytes);
        RenderBudget denied = new(baseline.Snapshot().PeakBytes - 1);
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, denied, CancellationToken.None)).ErrorCode);
        Assert.Equal(0, denied.Snapshot().CurrentBytes);
        RenderBudget workDenied = new(1_000_000);
        Assert.Equal(0, workDenied.Snapshot().CurrentBytes);
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        RenderBudget cancelled = new(1_000_000);
        Assert.ThrowsAny<OperationCanceledException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, cancelled, cancellation.Token));
        Assert.Equal(0, cancelled.Snapshot().CurrentBytes);
        Assert.Equal(Render(Sibling), SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None).ToArray());
    }

    private static byte[] Input(string content)
        => System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'>" + Definitions + content + "</svg>");

    private static byte[] Render(string content)
        => SvgConverter.ConvertToPng(Input(content), SvgRenderOptions.Default, CancellationToken.None).ToArray();
}
