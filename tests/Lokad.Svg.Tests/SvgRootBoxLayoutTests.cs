using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

[Trait("Layer", "Mechanism")]
public sealed class SvgRootBoxLayoutTests
{
    [Theory]
    [InlineData("padding:1px", 1, 1, 1, 1)]
    [InlineData("padding:1px 2px", 1, 2, 1, 2)]
    [InlineData("padding:1px 2px 3px", 1, 2, 3, 2)]
    [InlineData("padding:1px 2px 3px 4px", 1, 2, 3, 4)]
    [InlineData("padding:1px!important;padding-left:4px", 1, 1, 1, 1)]
    [InlineData("padding-left:4px!important;padding:1px", 1, 1, 1, 4)]
    [InlineData("padding:4px;padding:initial", 0, 0, 0, 0)]
    [InlineData("padding:4px;padding:unset", 0, 0, 0, 0)]
    [InlineData("padding:4px;padding:inherit", 0, 0, 0, 0)]
    [InlineData("padding:1in 1pt 1pc 1cm", 96, 96d / 72, 16, 96d / 2.54)]
    public void Padding_shorthand_and_longhands_keep_independent_edge_priority(string declarations,
        double top, double right, double bottom, double left)
    {
        using SvgRoot root = Parse(declarations, "");
        Assert.Equal(new SvgRootBoxLayout(new(top, false), new(right, false), new(bottom, false), new(left, false)), root.BoxLayout);
    }

    [Theory]
    [InlineData("padding:1px", "svg{padding:4px!important}", 4)]
    [InlineData("padding:1px!important", "svg{padding:4px!important}", 1)]
    [InlineData("padding:1px", "svg{padding:4px}", 1)]
    [InlineData("", "svg{padding:4px}#r{padding-left:2px}svg{padding:3px}", 2)]
    public void Padding_uses_stylesheet_specificity_source_order_and_inline_priority(string inline, string sheet, double left)
    {
        using SvgRoot root = Parse(inline, sheet);
        Assert.Equal(left, root.BoxLayout.Left.Value);
    }

    [Theory]
    [InlineData("padding:1.3px", 1.296875)]
    [InlineData("padding:1.31px", 1.296875)]
    [InlineData("padding:1.32px", 1.3125)]
    [InlineData("padding:10%", 1.6875)]
    public void Padding_resolves_on_the_css_layout_grid_before_image_projection(string declarations, double expected)
    {
        using SvgRoot root = Parse(declarations, "");
        SvgRootImageMap image = SvgRootImageMap.Resolve(null, new(17.4, 11.4), 1.5, root.BoxLayout);
        Assert.Equal(SvgAffineTransform.FromScale(1.5, 1.5), image.BoxDrawing.Recording);
        Assert.Equal(expected, image.Layout.E);
        Assert.Equal(expected, image.Layout.F);
        Assert.Equal(Math.Round(expected, MidpointRounding.AwayFromZero), image.ContentViewport?.X);
    }

    [Theory]
    [InlineData("padding:-1px")]
    [InlineData("padding:1")]
    [InlineData("padding:1px 2px 3px 4px 5px")]
    [InlineData("padding-left:NaNpx")]
    [InlineData("padding:1e999px")]
    [InlineData("padding:initial 1px")]
    public void Invalid_root_padding_fails_at_the_layout_boundary(string declarations)
        => Assert.Equal(SvgErrorCode.UnsupportedFeature, Assert.Throws<SvgException>(() => Parse(declarations, "")).ErrorCode);

    [Fact]
    public void Nonapplicable_padding_does_not_retain_root_layout_or_resolve_its_value()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><style>g,.n{padding:unused}</style><g><rect width='4' height='4' style='padding:unused'/></g><svg class='n' width='4' height='4'><rect width='1' height='1'/></svg></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);
        Assert.False(root.BoxLayout.HasPadding);
        Assert.NotEmpty(root.Geometries);
    }

    [Fact]
    public void Nonzero_specified_padding_keeps_its_clip_when_layout_offsets_round_to_zero()
    {
        using SvgRoot root = Parse("padding:.001px", "");
        SvgRootImageMap ordinary = SvgRootImageMap.Resolve(null, new(17, 11), 1.5);
        SvgRootImageMap padded = SvgRootImageMap.Resolve(null, new(17, 11), 1.5, root.BoxLayout);
        Assert.True(root.BoxLayout.HasPadding);
        Assert.Equal(ordinary.Drawing, padded.Drawing);
        Assert.Equal(ordinary.BoxPaintBounds, padded.BoxPaintBounds);
        Assert.Equal(new SvgClipBox(0, 0, 17, 11), padded.ContentViewport);
    }

    [Fact]
    public void Css_content_origin_keeps_viewbox_fitting_separate_from_box_placement()
    {
        SvgRootBoxLayout padding = new(new(3, false), new(2, false), new(1, false), new(4, false));
        SvgViewBox box = new(10, 20, 40, 12, SvgViewBoxAlignment.Mid, SvgViewBoxAlignment.Mid, SvgViewBoxFit.Meet);
        SvgRootImageMap ordinary = SvgRootImageMap.Resolve(box, new(64, 48), 1.5);
        SvgRootImageMap actual = SvgRootImageMap.Resolve(box, new(64, 48), 1.5, padding);
        Assert.Equal(ordinary.Layout.A, actual.Layout.A);
        Assert.Equal(ordinary.Layout.D, actual.Layout.D);
        Assert.Equal(ordinary.Layout.E + 4, actual.Layout.E);
        Assert.Equal(ordinary.Layout.F + 3, actual.Layout.F);
        Assert.Equal(ordinary.Projection, actual.BoxDrawing.Recording);
        Assert.Equal(ordinary.Destination, actual.Destination);
        Assert.Equal(ordinary.Boundary, actual.Boundary);
    }

    private static SvgRoot Parse(string inline, string sheet)
        => SvgRootParser.Parse(System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' id='r' style='" + inline + "'><style>" + sheet + "</style><rect width='1' height='1'/></svg>"), SvgRenderLimits.Default, CancellationToken.None);
}
