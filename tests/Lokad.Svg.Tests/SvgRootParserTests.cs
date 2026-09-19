using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;
using System.Text;
using System.Xml;

namespace Lokad.Svg.Tests;

public sealed class SvgRootParserTests
{
    [Fact]
    public void Parses_root_dimensions_without_copying_to_a_document_string()
    {
        SvgRoot root = SvgRootParser.Parse(System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='12.5' height='7'/>"), SvgRenderLimits.Default, CancellationToken.None);
        Assert.Equal(12.5, root.Width);
        Assert.Equal(7, root.Height);
        Assert.False(root.HasUnsupportedChild);
    }

    [Fact]
    public void Records_visible_child_elements_for_the_scene_stage()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><rect width='1' height='1' fill='#ff0000'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);
        Assert.Single(root.Rectangles);
    }

    [Fact]
    public void Expands_a_prior_rectangle_definition_through_use()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><rect id='tile' x='1' y='2' width='3' height='4' fill='red'/></defs><use href='#tile' x='5' y='6'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Single(root.Rectangles);
        Assert.Equal(new SvgRectangle(6, 8, 3, 4, new SvgColor(255, 0, 0, 255), true, null), root.Rectangles[0]);
    }

    [Fact]
    public void Applies_stylesheet_rules_to_rectangle_definitions()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><style>rect { fill: red; }</style><defs><rect id='tile' width='1' height='1'/></defs><use href='#tile'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(new SvgColor(255, 0, 0, 255), root.Rectangles[0].Color);
    }

    [Fact]
    public void Resolves_a_forward_rectangle_definition_through_use_in_document_order()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><use href='#tile' x='2'/><defs><rect id='tile' width='1' height='1' fill='red'/></defs><rect x='3' width='1' height='1' fill='blue'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Collection(root.Rectangles,
            rectangle => Assert.Equal(new SvgRectangle(2, 0, 1, 1, new SvgColor(255, 0, 0, 255), true, null), rectangle),
            rectangle => Assert.Equal(new SvgRectangle(3, 0, 1, 1, new SvgColor(0, 0, 255, 255), true, null), rectangle));
    }

    [Fact]
    public void Applies_integer_transforms_to_rectangle_use_instances()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><rect id='tile' x='1' y='1' width='2' height='1' fill='red'/></defs><use href='#tile' x='3' y='2' transform='scale(2) translate(1, 0)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(new SvgRectangle(10, 6, 4, 2, new SvgColor(255, 0, 0, 255), true, null), root.Rectangles[0]);
    }

    [Fact]
    public void Expands_a_rectangle_symbol_through_use()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><symbol id='tile'><rect width='1' height='1' fill='red'/></symbol></defs><use href='#tile' x='2'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(new SvgRectangle(2, 0, 1, 1, new SvgColor(255, 0, 0, 255), true, null), root.Rectangles[0]);
    }

    [Fact]
    public void Applies_opacity_and_display_to_rectangle_use_instances()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><rect id='tile' width='1' height='1' fill='red'/></defs><use href='#tile' opacity='0.5'/><use href='#tile' display='none'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal((byte)128, root.Rectangles[0].Color.Alpha);
        Assert.True(root.Rectangles[0].HasFill);
        Assert.False(root.Rectangles[1].HasFill);
    }

    [Fact]
    public void Rejects_paint_styles_on_rectangle_use_instances()
    {
        SvgException exception = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><rect id='tile' width='1' height='1'/></defs><use href='#tile' style='fill: red'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, exception.ErrorCode);
    }

    [Fact]
    public void Parses_fractional_rectangle_coordinates()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><rect x='0.25' y='0.5' width='5.5' height='3.25'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(new SvgRectangle(0.25, 0.5, 5.5, 3.25, new SvgColor(0, 0, 0, 255), true, null), root.Rectangles[0]);
    }

    [Fact]
    public void Multiplies_fill_opacity_with_color_alpha()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><rect width='1' height='1' fill='rgba(255, 0, 0, 0.5)' fill-opacity='0.5'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal((byte)64, root.Rectangles[0].Color.Alpha);
    }

    [Fact]
    public void Resolves_inherited_fill_opacity_and_current_color_at_each_element()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><g fill-opacity='0.5'><rect width='1' fill='red'/></g><g fill='red' fill-opacity='0.5'><rect width='1' fill-opacity='1'/></g><g color='red' fill='currentColor'><rect width='1' color='blue'/></g></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Collection(root.Rectangles,
            rectangle => Assert.Equal(new SvgColor(255, 0, 0, 128), rectangle.Color),
            rectangle => Assert.Equal(new SvgColor(255, 0, 0, 255), rectangle.Color),
            rectangle => Assert.Equal(new SvgColor(0, 0, 255, 255), rectangle.Color));
    }

    [Fact]
    public void Parses_basic_css_named_colors()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><rect width='1' fill='yellow'/><rect width='1' fill='Teal'/><rect width='1' fill='fuchsia'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Collection(root.Rectangles,
            rectangle => Assert.Equal(new SvgColor(255, 255, 0, 255), rectangle.Color),
            rectangle => Assert.Equal(new SvgColor(0, 128, 128, 255), rectangle.Color),
            rectangle => Assert.Equal(new SvgColor(255, 0, 255, 255), rectangle.Color));
    }

    [Fact]
    public void Parses_extended_css_named_colors()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><rect width='1' fill='orange'/><rect width='1' fill='RebeccaPurple'/><rect width='1' fill='darkslategray'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Collection(root.Rectangles,
            rectangle => Assert.Equal(new SvgColor(255, 165, 0, 255), rectangle.Color),
            rectangle => Assert.Equal(new SvgColor(102, 51, 153, 255), rectangle.Color),
            rectangle => Assert.Equal(new SvgColor(47, 79, 79, 255), rectangle.Color));
    }

    [Fact]
    public void Maps_css_gray_and_grey_aliases_to_the_same_color()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><rect width='1' fill='darkgray'/><rect width='1' fill='darkgrey'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Collection(root.Rectangles,
            rectangle => Assert.Equal(new SvgColor(169, 169, 169, 255), rectangle.Color),
            rectangle => Assert.Equal(new SvgColor(169, 169, 169, 255), rectangle.Color));
    }

    [Fact]
    public void Parses_space_separated_functional_css_colors()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><rect width='1' fill='rgb(100% 0% 0% / 50%)'/><rect width='1' fill='hsl(120 100% 50% / 0.5)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Collection(root.Rectangles,
            rectangle => Assert.Equal(new SvgColor(255, 0, 0, 128), rectangle.Color),
            rectangle => Assert.Equal(new SvgColor(0, 255, 0, 128), rectangle.Color));
    }

    [Fact]
    public void Applies_a_preceding_rect_stylesheet_rule_below_inline_style_and_above_presentation_attributes()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><style>rect { fill: blue; fill-opacity: 0.5; }</style><rect width='1'/><rect width='1' fill='red'/><rect width='1' style='fill: lime'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Collection(root.Rectangles,
            rectangle => Assert.Equal(new SvgColor(0, 0, 255, 128), rectangle.Color),
            rectangle => Assert.Equal(new SvgColor(0, 0, 255, 128), rectangle.Color),
            rectangle => Assert.Equal(new SvgColor(0, 255, 0, 128), rectangle.Color));
    }

    [Fact]
    public void Ignores_standard_block_comments_in_stylesheets()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><style>/* palette */ rect { /* fill */ fill: red; }</style><rect width='1'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(new SvgColor(255, 0, 0, 255), root.Rectangles[0].Color);
    }

    [Fact]
    public void Cascades_rect_id_and_class_stylesheet_rules_by_specificity_and_source_order()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><style>rect { fill: blue; fill-opacity: 0.5; } .accent { fill: red; } .accent { fill: teal; } #selected { fill: lime; } #selected { opacity: 0.5; }</style><rect width='1'/><rect width='1' class='accent'/><rect width='1' id='selected' class='accent'/><rect width='1' class='accent' style='fill: yellow'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Collection(root.Rectangles,
            rectangle => Assert.Equal(new SvgColor(0, 0, 255, 128), rectangle.Color),
            rectangle => Assert.Equal(new SvgColor(0, 128, 128, 128), rectangle.Color),
            rectangle => Assert.Equal(new SvgColor(0, 255, 0, 64), rectangle.Color),
            rectangle => Assert.Equal(new SvgColor(255, 255, 0, 128), rectangle.Color));
    }

    [Fact]
    public void Applies_descendant_and_child_group_stylesheet_rules_to_nested_rectangles()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><style>rect { fill: blue; } g rect { fill: red; } g > rect { fill: lime; }</style><rect width='1'/><g><rect width='1'/></g></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Collection(root.Rectangles,
            rectangle => Assert.Equal(new SvgColor(0, 0, 255, 255), rectangle.Color),
            rectangle => Assert.Equal(new SvgColor(0, 255, 0, 255), rectangle.Color));
    }

    [Fact]
    public void Applies_id_and_class_presence_and_equality_attribute_selectors()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><style>rect { fill: blue; } [id] { fill: red; } [class=accent] { fill: lime; }</style><rect width='1'/><rect width='1' id='other'/><rect width='1' class='accent'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Collection(root.Rectangles,
            rectangle => Assert.Equal(new SvgColor(0, 0, 255, 255), rectangle.Color),
            rectangle => Assert.Equal(new SvgColor(255, 0, 0, 255), rectangle.Color),
            rectangle => Assert.Equal(new SvgColor(0, 255, 0, 255), rectangle.Color));
    }

    [Fact]
    public void Applies_id_stylesheet_rules_to_supported_paths_and_polygons()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><style>#path { fill: red; } #polygon { fill: blue; }</style><path id='path' d='M 0 0 H 1 V 1 H 0 Z'/><polygon id='polygon' points='1,0 2,0 2,1 1,1'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Collection(root.Rectangles,
            rectangle => Assert.Equal(new SvgColor(255, 0, 0, 255), rectangle.Color),
            rectangle => Assert.Equal(new SvgColor(0, 0, 255, 255), rectangle.Color));
    }

    [Fact]
    public void Inherits_group_stylesheet_paint_and_visibility()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><style>g { fill: red; } #hidden { display: none; }</style><g><rect width='1'/></g><g id='hidden'><rect width='1'/></g></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(new SvgColor(255, 0, 0, 255), root.Rectangles[0].Color);
        Assert.True(root.Rectangles[0].HasFill);
        Assert.False(root.Rectangles[1].HasFill);
    }

    [Fact]
    public void Applies_compound_type_class_and_type_id_stylesheet_selectors()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><style>rect { fill: blue; } rect.accent { fill: red; } rect#selected { fill: lime; } g.hidden { display: none; }</style><rect width='1' class='accent'/><rect width='1' id='selected'/><g class='hidden'><rect width='1'/></g></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Collection(root.Rectangles,
            rectangle => Assert.Equal(new SvgColor(255, 0, 0, 255), rectangle.Color),
            rectangle => Assert.Equal(new SvgColor(0, 255, 0, 255), rectangle.Color),
            rectangle => Assert.False(rectangle.HasFill));
    }

    [Fact]
    public void Rejects_uncalibrated_non_quarter_rectangle_coordinates()
    {
        SvgException exception = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><rect x='0.1' width='1'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, exception.ErrorCode);
    }

    [Fact]
    public void Parses_rectangles_nested_in_attribute_free_groups_in_document_order()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><g><rect width='1' height='1' fill='red'/><g><rect width='1' height='1' fill='blue'/></g></g></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.False(root.HasUnsupportedChild);
        Assert.Collection(root.Rectangles, rectangle => Assert.Equal((byte)255, rectangle.Color.Red), rectangle => Assert.Equal((byte)255, rectangle.Color.Blue));
    }

    [Fact]
    public void Rejects_group_attributes_that_remain_unimplemented()
    {
        SvgException exception = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><g clip-path='url(#clip)'><rect width='1' height='1'/></g></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, exception.ErrorCode);
    }

    [Fact]
    public void Rejects_visible_rectangle_attributes_that_are_not_implemented()
    {
        SvgException exception = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><rect width='1' height='1' fill='#ff0000' transform='rotate(1)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None));
        Assert.Equal(SvgErrorCode.UnsupportedFeature, exception.ErrorCode);
    }

    [Fact]
    public void Rejects_supported_transform_values_that_overflow_the_coordinate_range()
    {
        SvgException composition = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><rect width='1' height='1' transform='scale(2147483647) translate(2)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None));
        SvgException path = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><path d='M 2147483647,0 H 2147483646 V 1 H 2147483647 Z' transform='scale(2)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, composition.ErrorCode);
        Assert.Equal(SvgErrorCode.UnsupportedFeature, path.ErrorCode);
    }

    [Fact]
    public void Parses_the_bounded_fill_less_integer_rectangle_stroke()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><rect x='1' y='1' width='4' height='4' fill='none' stroke='red' stroke-width='2'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(new SvgStroke(new SvgColor(255, 0, 0, 255), 2), root.Rectangles[0].Stroke);
        Assert.False(root.Rectangles[0].HasFill);
    }

    [Fact]
    public void Does_not_apply_fill_opacity_to_strokes()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><g fill-opacity='0.5'><rect x='1' y='1' width='4' height='4' fill='none' stroke='red' stroke-width='2'/></g></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(new SvgStroke(new SvgColor(255, 0, 0, 255), 2), root.Rectangles[0].Stroke);
    }

    [Theory]
    [InlineData("display='none'")]
    [InlineData("visibility='hidden'")]
    public void Hides_rectangle_strokes_when_the_rectangle_is_not_painted(string visibilityAttribute)
    {
        byte[] svg = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg'><rect x='1' y='1' width='4' height='4' fill='none' stroke='red' stroke-width='2' " + visibilityAttribute + "/></svg>");

        SvgRoot root = SvgRootParser.Parse(svg, SvgRenderLimits.Default, CancellationToken.None);

        Assert.False(root.Rectangles[0].HasFill);
        Assert.Null(root.Rectangles[0].Stroke);
    }

    [Fact]
    public void Rejects_rectangle_strokes_outside_the_calibrated_subset()
    {
        SvgException oddWidth = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><rect width='4' height='4' fill='none' stroke='red'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None));
        SvgException transformed = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><rect width='4' height='4' fill='blue' stroke='red' stroke-width='2' transform='translate(1)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, oddWidth.ErrorCode);
        Assert.Equal(SvgErrorCode.UnsupportedFeature, transformed.ErrorCode);
    }

    [Fact]
    public void Retains_fill_and_bounded_stroke_on_a_rectangle()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><rect width='4' height='4' fill='blue' stroke='red' stroke-width='2'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.True(root.Rectangles[0].HasFill);
        Assert.Equal(new SvgStroke(new SvgColor(255, 0, 0, 255), 2), root.Rectangles[0].Stroke);
    }

    [Fact]
    public void Lowers_bounded_axis_aligned_lines_to_stroke_bands()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><line x1='1' y1='3' x2='5' y2='3' stroke='red' stroke-width='2'/><line x1='4' y1='1' x2='4' y2='5' stroke='blue' stroke-width='2'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Collection(root.Rectangles,
            line => Assert.Equal(new SvgRectangle(1, 2, 4, 2, new SvgColor(255, 0, 0, 255), true, null), line),
            line => Assert.Equal(new SvgRectangle(3, 1, 2, 4, new SvgColor(0, 0, 255, 255), true, null), line));
    }

    [Fact]
    public void Preserves_the_full_extent_of_extreme_axis_aligned_line_coordinates()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><line x1='-2147483648' y1='2' x2='2147483647' y2='2' stroke='red' stroke-width='2'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(-2_147_483_648, root.Rectangles[0].X);
        Assert.Equal(4_294_967_295d, root.Rectangles[0].Width);
    }

    [Fact]
    public void Preserves_the_full_extent_of_extreme_orthogonal_path_coordinates()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><path d='M -2147483648,0 H 2147483647 V 1 H -2147483648 Z'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(-2_147_483_648, root.Rectangles[0].X);
        Assert.Equal(4_294_967_295d, root.Rectangles[0].Width);
    }

    [Fact]
    public void Applies_integer_translate_transforms_through_nested_groups()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><g transform='translate(1, 2)'><g transform='translate(3)'><rect x='4' y='5' width='1' height='1' transform='translate(-1, 2)'/></g></g></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(7, root.Rectangles[0].X);
        Assert.Equal(9, root.Rectangles[0].Y);
    }

    [Fact]
    public void Composes_positive_integer_scale_and_translate_transforms()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><g transform='translate(1, 2)'><g transform='scale(2, 3)'><rect x='4' y='5' width='1' height='2'/></g></g></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(9, root.Rectangles[0].X);
        Assert.Equal(17, root.Rectangles[0].Y);
        Assert.Equal(2, root.Rectangles[0].Width);
        Assert.Equal(6, root.Rectangles[0].Height);
    }

    [Fact]
    public void Preserves_svg_order_for_integer_transform_lists()
    {
        SvgRoot translatedThenScaled = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><rect x='4' y='5' width='1' height='1' transform='translate(1, 2) scale(2, 3)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);
        SvgRoot scaledThenTranslated = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><rect x='4' y='5' width='1' height='1' transform='scale(2, 3) translate(1, 2)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(new SvgRectangle(9, 17, 2, 3, new SvgColor(0, 0, 0, 255), true, null), translatedThenScaled.Rectangles[0]);
        Assert.Equal(new SvgRectangle(10, 21, 2, 3, new SvgColor(0, 0, 0, 255), true, null), scaledThenTranslated.Rectangles[0]);
    }

    [Fact]
    public void Parses_axis_aligned_integer_matrix_transforms()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><rect x='2' y='3' width='1' height='1' transform='matrix(2 0 0 3 4 5)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(8, root.Rectangles[0].X);
        Assert.Equal(14, root.Rectangles[0].Y);
        Assert.Equal(2, root.Rectangles[0].Width);
        Assert.Equal(3, root.Rectangles[0].Height);
    }

    [Fact]
    public void Lowers_closed_axis_aligned_rectangle_paths()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><path d='M 1,2 H 4 V 6 H 1 Z' fill='red'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(new SvgRectangle(1, 2, 3, 4, new SvgColor(255, 0, 0, 255), true, null), root.Rectangles[0]);
    }

    [Fact]
    public void Lowers_paths_with_adjacent_commands_without_materializing_tokens()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><path d='M0,0H3V1H1V3H0Z' fill='red'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Collection(root.Rectangles,
            rectangle => Assert.Equal(new SvgRectangle(0, 0, 3, 1, new SvgColor(255, 0, 0, 255), true, null), rectangle),
            rectangle => Assert.Equal(new SvgRectangle(0, 1, 1, 2, new SvgColor(255, 0, 0, 255), true, null), rectangle));
    }

    [Fact]
    public void Lowers_closed_axis_aligned_line_rectangle_paths()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><path d='M 1,2 L 4,2 L 4,6 L 1,6 z' fill='red'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(new SvgRectangle(1, 2, 3, 4, new SvgColor(255, 0, 0, 255), true, null), root.Rectangles[0]);
    }

    [Fact]
    public void Lowers_closed_axis_aligned_line_paths_to_non_overlapping_rectangle_bands()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><path d='M 0,0 H 3 V 1 H 1 V 3 H 0 Z' fill='red'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Collection(root.Rectangles,
            rectangle => Assert.Equal(new SvgRectangle(0, 0, 3, 1, new SvgColor(255, 0, 0, 255), true, null), rectangle),
            rectangle => Assert.Equal(new SvgRectangle(0, 1, 1, 2, new SvgColor(255, 0, 0, 255), true, null), rectangle));
    }

    [Fact]
    public void Lowers_closed_axis_aligned_polygons_to_non_overlapping_rectangle_bands()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><polygon points='0,0 3,0 3,1 1,1 1,3 0,3' fill='red'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Collection(root.Rectangles,
            rectangle => Assert.Equal(new SvgRectangle(0, 0, 3, 1, new SvgColor(255, 0, 0, 255), true, null), rectangle),
            rectangle => Assert.Equal(new SvgRectangle(0, 1, 1, 2, new SvgColor(255, 0, 0, 255), true, null), rectangle));
    }

    [Fact]
    public void Lowers_filled_axis_aligned_polylines_by_closing_the_fill_contour()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><polyline points='0,0 3,0 3,1 1,1 1,3 0,3' fill='red'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Collection(root.Rectangles,
            rectangle => Assert.Equal(new SvgRectangle(0, 0, 3, 1, new SvgColor(255, 0, 0, 255), true, null), rectangle),
            rectangle => Assert.Equal(new SvgRectangle(0, 1, 1, 2, new SvgColor(255, 0, 0, 255), true, null), rectangle));
    }

    [Fact]
    public void Rejects_diagonal_polygon_segments()
    {
        SvgException exception = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><polygon points='0,0 2,1 2,2 0,2'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, exception.ErrorCode);
    }

    [Fact]
    public void Rejects_self_intersecting_orthogonal_contours()
    {
        SvgException polygon = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><polygon points='0,0 3,0 3,3 1,3 1,1 4,1 4,4 0,4'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None));
        SvgException path = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><path d='M 0,0 H 3 V 3 H 1 V 1 H 4 V 4 H 0 Z'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, polygon.ErrorCode);
        Assert.Equal(SvgErrorCode.UnsupportedFeature, path.ErrorCode);
    }

    [Fact]
    public void Enforces_the_path_command_limit_before_expanding_polygon_storage()
    {
        SvgRenderLimits limits = new(1024, 1024, 10, 100, 1024, 1, 1, 1);
        SvgException exception = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><polygon points='0,0 3,0 3,1 1,1 1,3 0,3'/></svg>"u8.ToArray(), limits, CancellationToken.None));

        Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode);
    }

    [Fact]
    public void Enforces_the_path_command_limit_across_supported_path_elements()
    {
        SvgRenderLimits limits = new(1024, 1024, 10, 100, 1024, 16, 9, 100);

        SvgException exception = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><path d='M 0,0 H 1 V 1 H 0 Z'/><path d='M 2,0 H 3 V 1 H 2 Z'/></svg>"u8.ToArray(), limits, CancellationToken.None));

        Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode);
    }

    [Fact]
    public void Rejects_relative_rectangle_paths_until_relative_commands_are_implemented()
    {
        SvgException exception = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><path d='m 1,2 h 3 v 4 h -3 z'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, exception.ErrorCode);
    }

    [Fact]
    public void Parses_supported_root_view_box_mapping()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg' viewBox='1 2 3 4' preserveAspectRatio='none'/>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);
        SvgRoot aligned = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 1 2' preserveAspectRatio='xMaxYMin meet'/>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(new SvgViewBox(1, 2, 3, 4, SvgViewBoxAlignment.None, SvgViewBoxAlignment.None, SvgViewBoxFit.None), root.ViewBox);
        Assert.Equal(new SvgViewBox(0, 0, 1, 2, SvgViewBoxAlignment.Max, SvgViewBoxAlignment.Min, SvgViewBoxFit.Meet), aligned.ViewBox);
    }

    [Fact]
    public void Enforces_the_node_limit_before_expanding_rectangle_storage()
    {
        SvgRenderLimits limits = new(1024, 1024, 10, 100, 1024, 1, 1, 1);
        SvgException exception = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><rect width='1' height='1' fill='#ff0000'/><rect width='1' height='1' fill='#ff0000'/></svg>"u8.ToArray(), limits, CancellationToken.None));
        Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode);
    }

    [Fact]
    public void Enforces_the_node_limit_for_inert_definition_descendants()
    {
        SvgRenderLimits limits = new(1024, 1024, 10, 100, 1024, 1, 1, 1);

        SvgException exception = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><g/></defs></svg>"u8.ToArray(), limits, CancellationToken.None));

        Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode);
    }

    [Fact]
    public void Enforces_the_structural_limit_while_expanding_stylesheet_selectors()
    {
        SvgRenderLimits limits = new(1024, 1024, 10, 100, 1024, 2, 16, 100);

        SvgException exception = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><style>rect, #one, .two { fill: red; }</style></svg>"u8.ToArray(), limits, CancellationToken.None));

        Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode);
    }

    [Fact]
    public void Enforces_the_work_limit_while_matching_stylesheet_rules()
    {
        SvgRenderLimits limits = new(1024, 1024, 10, 100, 1024, 16, 16, 1);

        SvgException exception = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><style>rect, #one { fill: red; }</style><rect width='1'/></svg>"u8.ToArray(), limits, CancellationToken.None));

        Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode);
    }

    [Fact]
    public void Tolerates_inert_root_metadata_without_claiming_visible_support()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><title>sample</title><metadata><editor:note xmlns:editor='urn:editor'>ignored</editor:note></metadata><rect width='1' height='1' fill='#ff0000'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);
        Assert.False(root.HasUnsupportedChild);
        Assert.Single(root.Rectangles);
    }

    [Fact]
    public void Parses_px_dimensions_but_keeps_percentages_explicitly_unsupported()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg' width='12.5px' height='7px'/>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);
        SvgException exception = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg' width='100%' height='7'/>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None));

        Assert.Equal(12.5, root.Width);
        Assert.Equal(SvgErrorCode.UnsupportedFeature, exception.ErrorCode);
    }

    [Fact]
    public void Inline_styles_override_presentation_attributes_and_inherit_through_groups()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg' fill='red' style='color: lime'><g fill='blue' style='fill: currentColor; fill-opacity: 0.5'><rect width='1' height='1'/></g><rect width='1' height='1' fill='red' style='fill: blue; opacity: 0.5'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Collection(root.Rectangles,
            rectangle => Assert.Equal(new SvgColor(0, 255, 0, 128), rectangle.Color),
            rectangle => Assert.Equal(new SvgColor(0, 0, 255, 128), rectangle.Color));
    }

    [Fact]
    public void Rejects_inline_styles_that_are_not_in_the_supported_rectangle_subset()
    {
        SvgException unknownProperty = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><rect width='1' height='1' style='stroke-width: 1'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None));
        Assert.Equal(SvgErrorCode.UnsupportedFeature, unknownProperty.ErrorCode);
    }

    [Fact]
    public void Represents_group_opacity_as_an_isolated_scene_command()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><g opacity='0.5'><rect width='1' height='1'/></g></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Collection(root.Commands,
            command => Assert.Equal(new SvgDrawCommand(SvgDrawCommandKind.BeginGroup, default, 128), command),
            command => Assert.Equal(SvgDrawCommandKind.Rectangle, command.Kind),
            command => Assert.Equal(new SvgDrawCommand(SvgDrawCommandKind.EndGroup, default, 255), command));
    }

    [Fact]
    public void Represents_root_opacity_as_an_isolated_scene_command()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg' opacity='0.5'><rect width='1' height='1'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Collection(root.Commands,
            command => Assert.Equal(new SvgDrawCommand(SvgDrawCommandKind.BeginGroup, default, 128), command),
            command => Assert.Equal(SvgDrawCommandKind.Rectangle, command.Kind),
            command => Assert.Equal(new SvgDrawCommand(SvgDrawCommandKind.EndGroup, default, 255), command));
    }

    [Fact]
    public void Rejects_nested_group_opacity_pending_compositing_calibration()
    {
        SvgException exception = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><g opacity='0.5'><g opacity='0.5'><rect width='1' height='1'/></g></g></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, exception.ErrorCode);
    }

    [Fact]
    public void Rejects_external_entity_processing()
    {
        Assert.Throws<XmlException>(() => SvgRootParser.Parse(System.Text.Encoding.UTF8.GetBytes("<!DOCTYPE svg [<!ENTITY x SYSTEM 'file:///nope'>]><svg xmlns='http://www.w3.org/2000/svg'/>") , SvgRenderLimits.Default, CancellationToken.None));
    }

    [Fact]
    public void Reserves_secondary_parser_collections_before_they_allocate()
    {
        SvgRenderLimits limits = new(1, 1024, 16, 16, 1024, 16, 16, 16);

        SvgException group = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><g></g></svg>"u8.ToArray(), limits, new RenderBudget(1), CancellationToken.None));
        SvgException stylesheet = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><style>rect { fill: red; }</style></svg>"u8.ToArray(), limits, new RenderBudget(1), CancellationToken.None));
        SvgException definitions = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><rect id='sample'/></defs></svg>"u8.ToArray(), limits, new RenderBudget(1), CancellationToken.None));
        SvgException pendingUse = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><use href='#sample'/></svg>"u8.ToArray(), new SvgRenderLimits(700, 1024, 16, 16, 1024, 16, 16, 16), new RenderBudget(700), CancellationToken.None));

        Assert.Equal(SvgErrorCode.LimitExceeded, group.ErrorCode);
        Assert.Equal(SvgErrorCode.LimitExceeded, stylesheet.ErrorCode);
        Assert.Equal(SvgErrorCode.LimitExceeded, definitions.ErrorCode);
        Assert.Equal(SvgErrorCode.LimitExceeded, pendingUse.ErrorCode);
    }

    [Fact]
    public void Reserves_geometry_collection_capacity_before_allocation()
    {
        RenderBudget budget = new(63);
        using BudgetedParserList<SvgPoint> points = new(budget, SvgMemoryCategory.Geometry, 8);

        SvgException exception = Assert.Throws<SvgException>(() => points.Add(new SvgPoint(0, 0)));

        Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode);
    }

    [Fact]
    public void Releases_transient_parser_collection_reservations_after_scene_construction()
    {
        RenderBudget budget = new(10_000);

        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><style>rect { fill: red; }</style><defs><rect id='sample'/></defs><use href='#sample'/></svg>"u8.ToArray(), SvgRenderLimits.Default, budget, CancellationToken.None);

        Assert.Equal(0, budget.Snapshot().BytesByCategory[(int)SvgMemoryCategory.Parser]);
    }
}
