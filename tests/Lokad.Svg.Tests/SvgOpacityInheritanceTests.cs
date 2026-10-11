using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgOpacityInheritanceTests
{
    [Theory]
    [InlineData("1")]
    [InlineData("0.5")]
    [InlineData("0.333333333")]
    [InlineData("33.3%")]
    [InlineData("0")]
    public void Leaf_inherit_uses_parent_own_opacity_with_its_original_precision(string parent)
    {
        string prefix = Start + "<g opacity='" + parent + "' fill='red' stroke='black' stroke-width='4'>";
        string shape = "<rect id='box' x='8' y='8' width='24' height='24' opacity='";
        Assert.Equal(Render(prefix + shape + parent + "'/></g></svg>"),
            Render(prefix + shape + "0.75'/></g><style>#box{opacity:inherit!important}</style></svg>"));
    }

    [Theory]
    [InlineData("opacity='inherit'")]
    [InlineData("opacity='0.75' style='opacity:INHERIT!important'")]
    public void Omitted_intermediate_opacity_resets_the_logical_parent_to_one(string declaration)
    {
        string prefix = Start + "<g opacity='0.5'><g>";
        Assert.Equal(Render(prefix + Box + "opacity='1'/></g></g></svg>"), Render(prefix + Box + declaration + "/></g></g></svg>"));
    }

    [Theory]
    [InlineData("initial")]
    [InlineData("unset")]
    public void Noninherited_keywords_use_the_initial_opacity(string keyword)
    {
        string prefix = Start + "<g opacity='0.5'>";
        Assert.Equal(Render(prefix + Box + "opacity='1'/></g></svg>"),
            Render(prefix + Box + "opacity='0.75' style='opacity:" + keyword + "!important'/></g></svg>"));
    }

    [Theory]
    [InlineData("symbol")]
    [InlineData("leaf")]
    [InlineData("group")]
    [InlineData("use")]
    public void Retained_inheritance_rebinds_for_independent_instances(string location)
    {
        string Source(string opacity, string id)
        {
            string leaf = Box + (location == "leaf" ? "opacity='" + opacity + "'" : "") + "/>";
            if (location == "group") leaf = "<g opacity='" + opacity + "'>" + leaf + "</g>";
            if (location == "use") leaf = "<use href='#" + id + "-part' opacity='" + opacity + "'/>";
            return "<defs><symbol id='" + id + "' viewBox='0 0 40 40' " + (location == "symbol" ? "opacity='" + opacity + "'" : "") + ">" + leaf + "</symbol>"
                + (location == "use" ? "<rect id='" + id + "-part' x='8' y='8' width='24' height='24' fill='red'/>" : "") + "</defs>";
        }
        // Each clone binds to its own use or intervening container. Symbol and
        // group opacity default to one, so their children inherit that own value.
        string start = Start.Replace("width='40'", "width='80'", StringComparison.Ordinal);
        string Use(string id, string x, string opacity) => "<use href='#" + id + "' x='" + x + "' opacity='" + opacity + "' width='40' height='40'/>";
        string expected = start + Source(location == "symbol" ? "0.5" : "1", "first") + Source(location == "symbol" ? "0.75" : "1", "second")
            + Use("first", "0", "0.5") + Use("second", "40", "0.75") + "</svg>";
        string actual = start + Source("inherit", "item") + Use("item", "0", "0.5") + Use("item", "40", "0.75") + "</svg>";
        Assert.Equal(Render(expected), Render(actual));
    }

    [Fact]
    public void Root_inherit_uses_initial_opacity()
        => Assert.Equal(Render(Start + Box + "/></svg>"), Render(Start.Replace("width=", "opacity='inherit' width=", StringComparison.Ordinal) + Box + "/></svg>"));

    [Fact]
    public void Inherited_group_opacity_preserves_nested_composition_boundary()
    {
        SvgException expected = Assert.Throws<SvgException>(() => Render(Start + "<g opacity='0.5'><g opacity='0.5'>" + Box + "/></g></g></svg>"));
        SvgException actual = Assert.Throws<SvgException>(() => Render(Start + "<g opacity='0.5'><g opacity='inherit'>" + Box + "/></g></g></svg>"));
        Assert.Equal(SvgErrorCode.UnsupportedFeature, actual.ErrorCode);
        Assert.Equal(expected.Message, actual.Message);
    }

    [Theory]
    [InlineData("0.5", "", "1")]
    [InlineData("1", "opacity='0.5'", "0.5")]
    [InlineData("1", "opacity='inherit'", "1")]
    public void Viewport_own_opacity_separates_parent_layers_from_child_inheritance(string rootOpacity, string viewportStyle, string expected)
    {
        string prefix = Start.Replace("width=", "opacity='" + rootOpacity + "' width=", StringComparison.Ordinal)
            + "<svg width='40' height='40' " + viewportStyle + ">";
        Assert.Equal(Render(prefix + Box + "opacity='" + expected + "'/></svg></svg>"), Render(prefix + Box + "opacity='inherit'/></svg></svg>"));
    }

    [Fact]
    public void Parent_mask_alpha_does_not_replace_its_own_opacity()
    {
        string prefix = Start + "<defs><mask id='cut'><rect width='40' height='40' fill='white' fill-opacity='0.5'/></mask></defs>"
            + "<g opacity='0.5' mask='url(#cut)'>";
        Assert.Equal(Render(prefix + Box + "opacity='0.5'/></g></svg>"), Render(prefix + Box + "opacity='inherit'/></g></svg>"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Plain_definition_inheritance_binds_to_each_use_instead_of_lexical_ancestors(bool forward)
    {
        string Definitions(string opacity, string id) => "<defs>" + Box + "id='" + id + "' opacity='" + opacity + "'/></defs>";
        string uses = "<use href='#item' opacity='0.5'/><use href='#item' x='40' opacity='0.75'/>";
        string definitions = Definitions("inherit", "item");
        string actual = Start.Replace("width='40'", "width='80'", StringComparison.Ordinal) + (forward ? uses + definitions : definitions + uses) + "</svg>";
        string expected = Start.Replace("width='40'", "width='80'", StringComparison.Ordinal) + Definitions("0.5", "first") + Definitions("0.75", "second")
            + "<use href='#first' opacity='0.5'/><use href='#second' x='40' opacity='0.75'/></svg>";
        Assert.Equal(Render(expected), Render(actual));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Marker_inheritance_uses_its_definition_parent(bool inDefinitions)
    {
        string Source(string opacity) => "<marker id='m' viewBox='0 0 8 8' markerWidth='8' markerHeight='8' markerUnits='userSpaceOnUse' refX='4' refY='4' orient='0' opacity='"
            + opacity + "'><rect width='8' height='8' fill='red'/></marker>";
        string marker = Source("inherit");
        string expectedMarker = Source(inDefinitions ? "1" : "0.5");
        if (inDefinitions)
        {
            marker = "<defs>" + marker + "</defs>";
            expectedMarker = "<defs>" + expectedMarker + "</defs>";
        }
        string start = Start.Replace("width=", "opacity='0.5' width=", StringComparison.Ordinal);
        string line = "<line x1='8' y1='20' x2='32' y2='20' stroke='blue' stroke-width='2' marker-end='url(#m)'/></svg>";
        Assert.Equal(Render(start + expectedMarker + line), Render(start + marker + line));
    }

    private const string Start = "<svg xmlns='http://www.w3.org/2000/svg' width='40' height='40'>";
    private const string Box = "<rect x='8' y='8' width='24' height='24' fill='red' ";
    private static byte[] Render(string input)
        => SvgConverter.ConvertToPng(System.Text.Encoding.UTF8.GetBytes(input), SvgRenderOptions.Default, CancellationToken.None).ToArray();
}
