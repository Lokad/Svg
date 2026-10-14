using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgCssSelectorTests
{
    [Theory]
    [InlineData(".outer .target", "<g class='outer'><g>{0}</g></g>", true)]
    [InlineData(".outer>.target", "<g class='outer'><g>{0}</g></g>", false)]
    [InlineData(".outer > rect.target", "<g class='outer'>{0}</g>", true)]
    [InlineData("svg .outer rect", "<g class='outer'><g>{0}</g></g>", true)]
    [InlineData("a.outer rect", "<a class='outer'>{0}</a>", true)]
    [InlineData("g.outer.extra > rect#box.target[class = 'target'][id]", "<g class='outer extra'>{0}</g>", true)]
    [InlineData("g.outer.extra > rect#box.other", "<g class='outer extra'>{0}</g>", false)]
    [InlineData(".a>.b .target", "<g class='a'><g class='b'><g><g class='b'>{0}</g></g></g></g>", true)]
    [InlineData(".a>.b>.target", "<g class='a'><g class='b'><g><g class='b'>{0}</g></g></g></g>", false)]
    [InlineData("g rect", "<g><a>{0}</a></g>", true)]
    [InlineData("g circle", "<g>{0}</g>", false)]
    [InlineData("RECT", "<g>{0}</g>", false)]
    [InlineData(".Outer rect", "<g class='outer'>{0}</g>", false)]
    [InlineData("svg > svg rect", "<svg width='12' height='12'><g>{0}</g></svg>", true)]
    public void Compound_selectors_match_the_actual_ancestor_chain(string selector, string containers, bool matches)
    {
        string geometry = "<rect id='box' class='target' x='2' y='2' width='7' height='7'{0}/>";
        string actual = string.Format(containers, string.Format(geometry, string.Empty));
        string expected = string.Format(containers, string.Format(geometry, matches ? " fill='red'" : string.Empty));

        Assert.Equal(Render(actual + "<style>" + selector + "{fill:red}</style>"), Render(expected));
    }

    [Fact]
    public void Class_specificity_outranks_any_number_of_element_selectors()
    {
        string groups = string.Concat(Enumerable.Repeat("<g>", 11));
        string endings = string.Concat(Enumerable.Repeat("</g>", 11));
        string geometry = groups + "<rect class='target' width='7' height='7'{0}/>" + endings;
        string selector = string.Join(' ', Enumerable.Repeat("g", 11)) + " rect";

        Assert.Equal(Render(string.Format(geometry, " fill='red'")), Render(string.Format(geometry, string.Empty) + "<style>.target{fill:red}" + selector + "{fill:blue}</style>"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Id_specificity_outranks_any_number_of_class_selectors(bool important)
    {
        string suffix = important ? "!important" : string.Empty;
        string classes = string.Concat(Enumerable.Repeat(".target", 12));
        string geometry = "<rect id='box' class='target' width='7' height='7'{0}/>";

        Assert.Equal(Render(string.Format(geometry, " fill='red'")), Render(string.Format(geometry, string.Empty) + "<style>#box{fill:red" + suffix + "}" + classes + "{fill:blue" + suffix + "}</style>"));
    }

    [Fact]
    public void Important_declarations_outrank_high_specificity_and_normal_inline_values()
    {
        string ids = string.Concat(Enumerable.Repeat("#box", 12));
        string geometry = "<rect id='box' class='target' width='7' height='7'{0}/>";

        Assert.Equal(Render(string.Format(geometry, " fill='red'")), Render(string.Format(geometry, " style='fill:green'") + "<style>.target{fill:red!important}" + ids + "{fill:blue}</style>"));
    }

    [Fact]
    public void Unmatched_supported_element_rules_leave_other_geometry_unchanged()
    {
        Assert.Equal(Render("<rect/>"), Render("<style>circle { fill: red; }</style><rect/>"));
        Assert.Equal(Render("<circle cx='5' cy='5' r='3' fill='red'/>"), Render("<style>circle { fill: red; }</style><circle cx='5' cy='5' r='3'/>"));
    }

    [Fact]
    public void Ancestors_of_a_previous_sibling_do_not_match_the_next_sibling()
    {
        string first = "<g class='outer'><g><rect width='3' height='3'/></g></g>";
        string second = "<g><rect class='target' x='5' y='5' width='3' height='3'/></g>";

        Assert.Equal(Render(first + second), Render(first + second + "<style>.outer .target{fill:red}</style>"));
    }

    [Fact]
    public void Definition_source_selectors_survive_local_instance_replay()
    {
        string geometry = "<defs><symbol id='shapes'><g class='outer'><rect width='7' height='7'{0}/></g></symbol></defs><use href='#shapes' x='2' y='2'/>";

        Assert.Equal(Render(string.Format(geometry, " fill='red'")), Render(string.Format(geometry, string.Empty) + "<style>defs .outer>rect{fill:red}</style>"));
    }

    [Theory]
    [InlineData("svg")]
    [InlineData("svg.root")]
    [InlineData("svg#root[id='root']")]
    public void Matching_root_rules_supply_inherited_paint(string selector)
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' id='root' class='root' width='12' height='12'><style>" + selector + "{fill:red}</style><rect width='7' height='7'/></svg>");

        Assert.Equal(Render("<rect width='7' height='7' fill='red'/>"), SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None).ToArray());
    }

    [Fact]
    public void Root_stylesheet_dimensions_and_font_device_map_share_the_same_cascade()
    {
        byte[] input = "<svg xmlns='http://www.w3.org/2000/svg' id='root' width='10' height='10' viewBox='0 0 10 10' style='width:30px'><rect width='10' height='10' fill='red'/><style>#root{width:20px!important;height:40px}</style></svg>"u8.ToArray();
        byte[] expected = "<svg xmlns='http://www.w3.org/2000/svg' width='20' height='40' viewBox='0 0 10 10'><rect width='10' height='10' fill='red'/></svg>"u8.ToArray();
        Assert.Equal(SvgConverter.ConvertToPng(expected, SvgRenderOptions.Default, CancellationToken.None).ToArray(), SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None).ToArray());
        RenderBudget budget = new(long.MaxValue);
        SvgAffineTransform map = SvgConverter.TryResolveOutputMap(input, SvgRenderOptions.Default, budget, CancellationToken.None);
        Assert.Equal(SvgAffineTransform.FromScaleOffset(2, 2, 0, 10), map);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Theory]
    [InlineData("svg>svg", "<svg width='12' height='12'><rect width='7' height='7'/></svg>")]
    [InlineData("svg>defs", "<defs><symbol id='shapes'><rect width='7' height='7'/></symbol></defs><use href='#shapes'/>")]
    public void Container_cascades_preserve_source_ancestor_exclusion(string selector, string content)
    {
        string fill = selector == "svg>svg" ? "red" : "black";
        Assert.Equal(Render("<rect width='7' height='7' fill='" + fill + "'/>"), Render(content + "<style>" + selector + "{fill:red}</style>"));
    }

    [Theory]
    [InlineData(".outer>>rect")]
    [InlineData(">rect")]
    [InlineData("rect>")]
    [InlineData("g+rect")]
    [InlineData("g~rect")]
    [InlineData("rect:first-child")]
    [InlineData("rect[fill='red']")]
    [InlineData("*")]
    public void Selectors_outside_the_declared_subset_reject_before_rendering(string selector)
    {
        SvgException error = Assert.Throws<SvgException>(() => Render("<g><rect width='7' height='7'/></g><style>" + selector + "{fill:red}</style>"));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, error.ErrorCode);
    }

    [Fact]
    public void Compound_selectors_preserve_the_established_identifier_length_boundary()
    {
        string identifier = new('a', SvgSyntaxLimits.MaximumIdentifierCharacters);
        string geometry = "<rect id='" + identifier + "' class='" + identifier + "' width='7' height='7'{0}/>";

        Assert.Equal(Render(string.Format(geometry, " fill='red'")), Render(string.Format(geometry, string.Empty) + "<style>#" + identifier + "." + identifier + "{fill:red}</style>"));
    }

    [Fact]
    public void Excessive_selector_chains_retain_a_finite_syntax_limit()
    {
        string selector = string.Join(' ', Enumerable.Repeat(".a", SvgSyntaxLimits.MaximumCssSelectorCharacters / 2));

        SvgException error = Assert.Throws<SvgException>(() => Render("<g class='a'><rect width='7' height='7'/></g><style>" + selector + "{fill:red}</style>"));

        Assert.Equal(SvgErrorCode.LimitExceeded, error.ErrorCode);
        Assert.Contains("selectors", error.Message);
    }

    [Fact]
    public void Long_descendant_chains_obey_shared_work_limits_and_release_parser_storage()
    {
        string ancestors = string.Concat(Enumerable.Repeat("<g class='a'>", 30));
        string endings = string.Concat(Enumerable.Repeat("</g>", 30));
        string selector = string.Join(' ', Enumerable.Repeat(".a", 30)) + " rect";
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg'>" + ancestors + "<rect width='1' height='1'/>" + endings + "<style>" + selector + "{fill:red}</style></svg>");
        RenderBudget budget = new(SvgRenderLimits.Default.MaximumAccountedBytes);

        SvgException error = Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, null, new ParserWorkCounter(1_000), CancellationToken.None));

        Assert.Equal(SvgErrorCode.LimitExceeded, error.ErrorCode);
        Assert.Contains("work", error.Message);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    private static byte[] Render(string content)
        => SvgConverter.ConvertToPng(System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='12' height='12'>" + content + "</svg>"), SvgRenderOptions.Default, CancellationToken.None).ToArray();
}
