using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

[Trait("Layer", "Mechanism")]
public sealed class SvgRootBorderLayoutTests
{
    [Theory]
    [InlineData("border:2px solid red", 2, 2, 2, 2)]
    [InlineData("border:red solid 2px", 2, 2, 2, 2)]
    [InlineData("border-style:solid", 3, 3, 3, 3)]
    [InlineData("border-width:2px", 0, 0, 0, 0)]
    [InlineData("border:2px none red", 0, 0, 0, 0)]
    [InlineData("border:2px hidden red", 0, 0, 0, 0)]
    [InlineData("border-style:solid;border-width:1px 2px", 1, 2, 1, 2)]
    [InlineData("border-style:solid;border-width:1px 2px 3px", 1, 2, 3, 2)]
    [InlineData("border-style:solid;border-width:1px 2px 3px 4px", 1, 2, 3, 4)]
    [InlineData("border:2px solid red;border-left-width:4px", 2, 2, 2, 4)]
    [InlineData("border:2px solid red!important;border-left-width:4px", 2, 2, 2, 2)]
    [InlineData("border-left-width:4px!important;border:2px solid red", 2, 2, 2, 4)]
    [InlineData("border:2px solid red;border:initial", 0, 0, 0, 0)]
    [InlineData("border:2px solid red;border:inherit", 0, 0, 0, 0)]
    [InlineData("border:2px solid red;border:unset", 0, 0, 0, 0)]
    [InlineData("border:thin solid red", 1, 1, 1, 1)]
    [InlineData("border:medium solid red", 3, 3, 3, 3)]
    [InlineData("border:thick solid red", 5, 5, 5, 5)]
    [InlineData("border:.25px solid red", 1, 1, 1, 1)]
    [InlineData("border:1.99px solid red", 1, 1, 1, 1)]
    [InlineData("border:0 solid red", 0, 0, 0, 0)]
    [InlineData("border:1e-50px solid red", 0, 0, 0, 0)]
    public void Border_width_style_priority_and_used_width_keep_distinct_stages(string declarations,
        double top, double right, double bottom, double left)
    {
        using SvgRoot root = Parse(declarations, "");
        SvgRootBorder border = root.BoxLayout.Border;
        Assert.Equal((top, right, bottom, left), (border.Top.Width, border.Right.Width, border.Bottom.Width, border.Left.Width));
        Assert.Equal(top + right + bottom + left > 0, root.BoxLayout.HasInsets);
    }

    [Theory]
    [InlineData("svg{border:4px solid red!important}", 4)]
    [InlineData("svg{border:4px solid red}#r{border-left:2px solid blue}svg{border:3px solid red}", 1)]
    public void Root_border_uses_original_rule_priority_before_inline_declarations(string sheet, double left)
    {
        using SvgRoot root = Parse("border:1px solid lime", sheet);
        Assert.Equal(left, root.BoxLayout.Border.Left.Width);
    }

    [Fact]
    public void Border_colors_keep_functional_tokens_and_resolve_current_color_at_the_root()
    {
        using SvgRoot root = Parse("color:#2080c0;border:1px solid;border-color:rgba(200, 40, 20, .5) currentColor red blue", "");
        Assert.Equal(new SvgColor(200, 40, 20, 128), root.BoxLayout.Border.Top.Paint.Color);
        Assert.Equal(new SvgColor(32, 128, 192, 255), root.BoxLayout.Border.Right.Paint.Color);
        Assert.Equal(new SvgColor(255, 0, 0, 255), root.BoxLayout.Border.Bottom.Paint.Color);
        Assert.Equal(new SvgColor(0, 0, 255, 255), root.BoxLayout.Border.Left.Paint.Color);
    }

    [Theory]
    [InlineData("border:-1px solid red")]
    [InlineData("border:1 solid red")]
    [InlineData("border:1% solid red")]
    [InlineData("border:1px 2px solid red")]
    [InlineData("border:solid hidden red")]
    [InlineData("border:red blue solid")]
    [InlineData("border:initial red")]
    [InlineData("border:1px dashed red")]
    [InlineData("border:1px solid red;border-color:initial red")]
    [InlineData("box-sizing:unused")]
    public void Unsupported_root_box_values_fail_before_lowering(string declarations)
        => Assert.Equal(SvgErrorCode.UnsupportedFeature, Assert.Throws<SvgException>(() => Parse(declarations, "")).ErrorCode);

    [Fact]
    public void Border_and_padding_share_content_insets_but_keep_the_border_paint_hole()
    {
        using SvgRoot root = Parse("border:2px solid red;padding:1px 3px", "");
        SvgRootImageMap map = SvgRootImageMap.Resolve(null, new(17, 11), 1.5, root.BoxLayout);
        Assert.Equal((5d, 3d), (map.Layout.E, map.Layout.F));
        Assert.Equal(new SvgClipBox(5, 3, 17, 11), map.ContentViewport);
        Assert.Equal(new SvgClipBox(0, 0, 27, 17), map.BoxPaintBounds);
        Assert.Equal(new SvgClipBox(2, 2, 23, 13), map.BorderInnerBounds);
        Assert.Single(root.Commands.ToArray(), command => command.Kind == SvgDrawCommandKind.RootBorder);
        Assert.Empty(root.Geometries);
    }

    [Fact]
    public void Nonapplicable_borders_do_not_change_svg_geometry_or_root_layout()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><g style='border:unused;box-sizing:unused'><rect width='4' height='4' style='border:unused'/></g></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);
        Assert.False(root.BoxLayout.HasInsets);
        Assert.Single(root.Geometries);
    }

    private static SvgRoot Parse(string inline, string sheet)
        => SvgRootParser.Parse(System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' id='r' style='" + inline + "'><style>" + sheet + "</style></svg>"), SvgRenderLimits.Default, CancellationToken.None);
}
