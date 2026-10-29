using Lokad.Svg.Encoding;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgPatternDisplayTests
{
    private const string Target = "<rect width='32' height='32' fill='url(#p)'/>";
    private const string Rectangle = "<rect width='8' height='8' fill='red'/>";

    [Theory]
    [InlineData("rect")]
    [InlineData("circle")]
    [InlineData("path")]
    [InlineData("group")]
    [InlineData("gradient")]
    [InlineData("image")]
    [InlineData("use")]
    public void Own_display_suppresses_tiles_while_definition_ancestor_display_preserves_them(string kind)
    {
        string content = kind switch
        {
            "rect" => Rectangle,
            "circle" => "<circle cx='4' cy='4' r='4' fill='red'/>",
            "path" => "<path d='M0 0H8V8H0Z' fill='red'/>",
            "group" => "<g opacity='.5'>" + Rectangle + "</g>",
            "gradient" => "<rect width='8' height='8' fill='url(#gradient)'/>",
            "image" => "<image width='8' height='8' href='data:image/png;base64," + Convert.ToBase64String(PngEncoder.Encode(1, 1, [255, 0, 0, 255], CancellationToken.None)) + "'/>",
            "use" => "<use href='#shape'/>",
            _ => throw new ArgumentException("Unexpected tile kind.", nameof(kind))
        };
        const string resources = "<style>.hidden{display:none}</style><defs><linearGradient id='gradient'><stop stop-color='red'/><stop offset='1' stop-color='blue'/></linearGradient><rect id='shape' width='8' height='8' fill='red'/></defs>";
        byte[] expected = Render(resources + "<defs>" + Pattern("p", "", content) + "</defs>" + Target);
        Assert.False(Render("").AsSpan().SequenceEqual(expected));
        foreach (string display in new[] { "display='none'", "style='display:none'", "class='hidden'" })
        {
            Assert.Equal(Render(""), Render(resources + "<defs>" + Pattern("p", display, content) + "</defs>" + Target));
            Assert.Equal(expected, Render(resources + "<g " + display + "><defs>" + Pattern("p", "", content) + "</defs></g>" + Target));
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Templates_only_paint_content_when_both_base_and_alias_are_displayed(bool hiddenBase, bool hiddenAlias)
    {
        foreach (string content in new[] { Rectangle, "<circle cx='4' cy='4' r='4' fill='red'/>" })
        foreach (bool forward in new[] { false, true })
        {
            string source = Pattern("base", hiddenBase ? "display='none'" : "", content);
            string alias = "<pattern id='p' href='#base'" + (hiddenAlias ? " display='none'" : "") + "/>";
            Assert.Equal(Render(hiddenBase || hiddenAlias ? "" : "<defs>" + Pattern("p", "", content) + "</defs>" + Target),
                Render("<defs>" + (forward ? alias + source : source + alias) + "</defs>" + Target));
        }
    }

    [Theory]
    [InlineData("<rect width='8' height='8' display='none'/>")]
    [InlineData("<circle cx='4' cy='4' r='4' style='display:none'/>")]
    [InlineData("<g display='none'><rect width='8' height='8' display='inline'/></g>")]
    [InlineData("<g style='display:none'><circle cx='4' cy='4' r='4'/></g>")]
    [InlineData("<rect width='8' height='8' visibility='hidden'/>")]
    public void Display_and_visibility_on_actual_tile_children_remain_effective(string content)
        => Assert.Equal(Render(""), Render("<defs>" + Pattern("p", "", content) + "</defs>" + Target));

    [Theory]
    [InlineData("visibility='hidden'", "<rect width='8' height='8'/>", false)]
    [InlineData("visibility='hidden'", "<rect width='8' height='8' visibility='visible'/>", true)]
    [InlineData("visibility='visible'", "<rect width='8' height='8'/>", true)]
    public void Resource_display_reset_preserves_inherited_paint_and_visibility(string visibility, string content, bool visible)
    {
        string definition = "<g fill='red' " + visibility + "><defs>" + Pattern("p", "", content) + "</defs></g>";
        string hiddenDefinition = definition.Replace("<g ", "<g display='none' ", StringComparison.Ordinal);
        byte[] expected = Render(visible ? "<rect width='32' height='32' fill='red'/>" : "");
        Assert.Equal(expected, Render(definition + Target));
        Assert.Equal(expected, Render(hiddenDefinition + Target));
    }

    [Theory]
    [InlineData("opacity='.5'")]
    [InlineData("clip-path='url(#c)'")]
    [InlineData("mask='url(#m)'")]
    [InlineData("filter='url(#f)'")]
    [InlineData("transform='translate(2 3) scale(.75 1)'")]
    public void Available_patterns_keep_target_composition(string effect)
    {
        const string resources = "<defs><clipPath id='c'><rect width='12' height='32'/></clipPath><mask id='m' mask-type='alpha' maskUnits='userSpaceOnUse' x='0' y='0' width='32' height='32'><rect width='12' height='32' fill='white'/></mask><filter id='f' filterUnits='userSpaceOnUse' x='0' y='0' width='32' height='32'><feOffset dx='1' dy='1'/></filter></defs>";
        string target = Target.Replace("/>", " " + effect + "/>", StringComparison.Ordinal);
        string content = "<g opacity='.5'><circle cx='4' cy='4' r='4' fill='red'/></g>";
        Assert.Equal(Render(resources + "<defs>" + Pattern("p", "", content) + "</defs>" + target),
            Render(resources + "<g display='none'><defs>" + Pattern("p", "", content) + "</defs></g>" + target));
    }

    [Theory]
    [InlineData("display='none'")]
    [InlineData("visibility='hidden'")]
    public void Hidden_targets_stay_hidden(string hidden)
    {
        string definition = "<defs>" + Pattern("p", "", Rectangle) + "</defs>";
        Assert.Equal(Render(""), Render(definition + Target.Replace("/>", " " + hidden + "/>", StringComparison.Ordinal)));
        Assert.Equal(Render(""), Render(definition + "<g " + hidden + ">" + Target + "</g>"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Pattern_owners_release_on_denial_cancellation_and_retry(bool ownDisplayNone)
    {
        string definition = "<g display='none'><defs>" + Pattern("p", ownDisplayNone ? "display='none'" : "", "<use href='#shape'/>") + "</defs></g><defs><circle id='shape' cx='4' cy='4' r='4' fill='red'/></defs>";
        byte[] input = Input(definition + Target);
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
        Assert.Equal(Render(ownDisplayNone ? "" : "<defs>" + Pattern("p", "", "<circle cx='4' cy='4' r='4' fill='red'/>") + "</defs>" + Target), SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None).ToArray());
    }

    private static string Pattern(string id, string attributes, string content)
        => "<pattern id='" + id + "' patternUnits='userSpaceOnUse' width='8' height='8' " + attributes + ">" + content + "</pattern>";

    private static byte[] Input(string content)
        => System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'>" + content + "</svg>");

    private static byte[] Render(string content)
        => SvgConverter.ConvertToPng(Input(content), SvgRenderOptions.Default, CancellationToken.None).ToArray();
}
