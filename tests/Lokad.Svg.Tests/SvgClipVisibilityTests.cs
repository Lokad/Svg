using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgClipVisibilityTests
{
    [Theory]
    [InlineData("rect")]
    [InlineData("path")]
    [InlineData("circle")]
    [InlineData("ellipse")]
    [InlineData("use")]
    public void Hidden_children_do_not_contribute_for_attributes_inline_or_stylesheet_rules(string kind)
    {
        byte[] empty = Render(string.Empty);
        Assert.NotEqual(empty, Render(Clipped(Shape(kind).Replace("$", "", StringComparison.Ordinal), "")));
        foreach (string declaration in new[] { "display='none'", "visibility='hidden'", "style='display:none'", "style='visibility:hidden'", "class='hidden'" })
        {
            string content = Shape(kind).Replace("$", declaration, StringComparison.Ordinal);
            Assert.Equal(empty, Render(Clipped(content, "") + "<style>.hidden{display:none}</style>"));
        }
    }

    [Theory]
    [InlineData("rect")]
    [InlineData("path")]
    [InlineData("circle")]
    [InlineData("ellipse")]
    [InlineData("use")]
    public void Fill_stroke_and_opacity_do_not_suppress_raw_clip_geometry(string kind)
        => Assert.Equal(Render(Clipped(Shape(kind).Replace("$", "", StringComparison.Ordinal), "")),
            Render(Clipped(Shape(kind).Replace("$", "fill='none' stroke='none' opacity='0'", StringComparison.Ordinal), "")));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Resource_visibility_inherits_from_its_own_ancestors_and_children_can_override_it(bool ancestor)
    {
        string content = Clipped("<rect width='8' height='8'/>", "style='visibility:hidden'");
        string visible = Clipped("<rect width='8' height='8' visibility='visible'/>", "style='visibility:hidden'");
        if (ancestor)
        {
            content = "<g visibility='hidden'>" + content.Replace("style='visibility:hidden'", "", StringComparison.Ordinal).Replace("</defs>", "</defs></g>", StringComparison.Ordinal);
            visible = "<g visibility='hidden'>" + visible.Replace("style='visibility:hidden'", "", StringComparison.Ordinal).Replace("</defs>", "</defs></g>", StringComparison.Ordinal);
        }
        Assert.Equal(Render(string.Empty), Render(content));
        Assert.Equal(Render(string.Empty), Render(content.Replace("<rect width='8' height='8'/>", "<rect width='8' height='8' style='visibility:inherit'/>", StringComparison.Ordinal)));
        Assert.Equal(Render("<rect width='8' height='8' fill='red'/>"), Render(visible));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Own_display_none_disables_the_clip_while_ancestor_display_preserves_it(bool ancestor)
    {
        string content = Clipped("<rect width='8' height='8'/>", "style='display:none'");
        if (ancestor) content = "<g display='none'>" + content.Replace("style='display:none'", "", StringComparison.Ordinal).Replace("</defs>", "</defs></g>", StringComparison.Ordinal);
        string expected = ancestor ? "<rect width='8' height='8' fill='red'/>" : "<rect width='16' height='16' fill='red'/>";
        Assert.Equal(Render(expected), Render(content));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Hidden_union_members_do_not_change_the_visible_members_coverage(bool hiddenFirst)
    {
        const string visible = "<rect x='2.25' y='2.25' width='4.5' height='4.5' rx='1.25'/>";
        const string hidden = "<circle cx='11' cy='8' r='3.5' visibility='hidden'/>";
        Assert.Equal(Render(Clipped(visible, "")), Render(Clipped(hiddenFirst ? hidden + visible : visible + hidden, "")));
    }

    [Fact]
    public void Object_box_hidden_children_leave_an_empty_clip()
    {
        const string child = "<rect x='.125' y='.125' width='.5' height='.5'/>";
        Assert.NotEqual(Render(string.Empty), Render(Clipped(child, "clipPathUnits='objectBoundingBox'")));
        Assert.Equal(Render(string.Empty), Render(Clipped(child.Replace("/>", " visibility='hidden'/>", StringComparison.Ordinal), "clipPathUnits='objectBoundingBox'")));
    }

    [Theory]
    [InlineData("", "visibility='hidden'", false)]
    [InlineData("visibility='visible'", "visibility='hidden'", true)]
    [InlineData("visibility='hidden'", "visibility='visible'", false)]
    [InlineData("display='none'", "visibility='visible'", false)]
    [InlineData("visibility='visible'", "display='none'", false)]
    [InlineData("fill='none' opacity='0'", "", true)]
    public void Referenced_source_visibility_overrides_inheritance_but_not_display_none(string sourceAttributes, string useAttributes, bool visible)
    {
        string source = "<path id='p' d='M2 2H10V10H2Z' " + sourceAttributes + "/>";
        string content = Clipped("<use href='#p' " + useAttributes + "/>", "");
        byte[] expected = visible ? Render("<rect x='2' y='2' width='8' height='8' fill='red'/>") : Render(string.Empty);
        foreach (bool forward in new[] { false, true })
        {
            string definition = "<defs>" + source + "</defs>";
            Assert.Equal(expected, Render(forward ? content + definition : definition + content));
        }
    }

    [Fact]
    public void Cascade_and_importance_select_visibility_before_clip_construction()
    {
        const string child = "<rect id='child' width='8' height='8' visibility='hidden' style='visibility:hidden'/>";
        Assert.Equal(Render("<rect width='8' height='8' fill='red'/>"), Render(Clipped(child, "") + "<style>#child{visibility:visible!important}</style>"));
        Assert.Equal(Render(string.Empty), Render(Clipped("<rect id='child' width='8' height='8' style='display:block'/>" , "") + "<style>#child{display:none!important}</style>"));
    }

    [Fact]
    public void A_clip_local_source_keeps_its_independent_ordinary_use_visibility()
    {
        const string source = "<path id='p' d='M1 1H5V5H1Z'/>";
        const string instance = "<use href='#p' x='8' fill='blue'/>";
        Assert.Equal(Render("<defs>" + source + "</defs>" + instance), Render(Clipped(source, "style='visibility:hidden'") + instance));
    }

    [Theory]
    [InlineData("<use href='#missing' display='none'/>")]
    [InlineData("<path d='M0 0X1 1' display='none'/>")]
    public void Hidden_content_keeps_existing_unsupported_admission_and_releases_owners(string child)
    {
        RenderBudget budget = new(1_000_000);
        Assert.Equal(SvgErrorCode.UnsupportedFeature, Assert.Throws<SvgException>(() => SvgRootParser.Parse(Input(Clipped(child, "")), SvgRenderLimits.Default, budget, CancellationToken.None)).ErrorCode);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Hidden_clip_containers_remain_unsupported()
        => Assert.Equal(SvgErrorCode.UnsupportedFeature, Assert.Throws<SvgException>(() => Render(Clipped("<g display='none'><rect width='8' height='8'/></g>", ""))).ErrorCode);

    [Fact]
    public void Hidden_referenced_compound_paths_keep_the_single_contour_admission_boundary()
    {
        const string source = "<defs><path id='compound' d='M1 1H5V5H1Z M8 8H12V12H8Z'/></defs>";
        Assert.Equal(SvgErrorCode.UnsupportedFeature, Assert.Throws<SvgException>(() => Render(source + Clipped("<use href='#compound' visibility='hidden'/>", ""))).ErrorCode);
    }

    [Fact]
    public void Clip_visibility_denial_and_cancellation_release_storage_and_allow_retry()
    {
        const string visible = "<rect x='1' y='1' width='3' height='3'/><path d='M1 1C2 0 6 0 7 1V7H1Z'/>";
        string content = Clipped(visible + "<path d='M8 1C12 0 14 4 8 8Z' visibility='hidden'/>", "");
        byte[] input = Input(content);
        RenderBudget baseline = new(1_000_000);
        ParserWorkCounter measured = new(1_000_000);
        using (SvgRoot scene = SvgRootParser.Parse(input, SvgRenderLimits.Default, baseline, null, measured, CancellationToken.None)) { }
        Assert.Equal(0, baseline.Snapshot().CurrentBytes);
        RenderBudget denied = new(baseline.Snapshot().PeakBytes - 1);
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, denied, CancellationToken.None)).ErrorCode);
        Assert.Equal(0, denied.Snapshot().CurrentBytes);
        RenderBudget workDenied = new(1_000_000);
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, workDenied, null, new(measured.Used - 1), CancellationToken.None)).ErrorCode);
        Assert.Equal(0, workDenied.Snapshot().CurrentBytes);
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        RenderBudget cancelled = new(1_000_000);
        Assert.ThrowsAny<OperationCanceledException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, cancelled, cancellation.Token));
        Assert.Equal(0, cancelled.Snapshot().CurrentBytes);
        Assert.Equal(Render(Clipped(visible, "")), Render(content));
    }

    private static string Shape(string kind) => kind switch
    {
        "rect" => "<rect x='2' y='2' width='8' height='8' $/>",
        "path" => "<path d='M2 2H10V10H2Z' $/>",
        "circle" => "<circle cx='6' cy='6' r='4' $/>",
        "ellipse" => "<ellipse cx='6' cy='6' rx='4' ry='3' $/>",
        "use" => "<use href='#source' $/>",
        _ => throw new ArgumentException("Unexpected clip shape.", nameof(kind))
    };

    private static string Clipped(string children, string attributes)
        => "<defs><path id='source' d='M2 2H10V10H2Z'/><clipPath id='c' " + attributes + ">" + children + "</clipPath></defs><rect width='16' height='16' fill='red' clip-path='url(#c)'/>";

    private static byte[] Input(string content)
        => System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'>" + content + "</svg>");

    private static byte[] Render(string content)
        => SvgConverter.ConvertToPng(Input(content), SvgRenderOptions.Default, CancellationToken.None).ToArray();
}
