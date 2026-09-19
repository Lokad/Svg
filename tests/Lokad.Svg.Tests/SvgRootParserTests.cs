using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;
using System.Text;

namespace Lokad.Svg.Tests;

public sealed class SvgRootParserTests
{
    [Fact]
    public void Container_presentation_resolves_inherited_paint_display_visibility_and_opacity()
    {
        IReadOnlyDictionary<string, SvgColor> namedColors = new Dictionary<string, SvgColor>(StringComparer.OrdinalIgnoreCase) { ["red"] = new SvgColor(255, 0, 0, 255) };
        SvgComputedStyle style = new("red", "currentColor", "0.5", "none", "none", "hidden", "0.5", null);

        SvgResolvedContainerPresentation presentation = SvgContainerPresentationResolver.Resolve(style, SvgPaint.Default, true, true, namedColors);

        Assert.Equal(new SvgColor(255, 0, 0, 255), presentation.Paint.CurrentColor);
        Assert.True(presentation.Paint.FillUsesCurrentColor);
        Assert.Equal((byte)128, presentation.Paint.FillOpacity);
        Assert.False(presentation.IsDisplayed);
        Assert.False(presentation.IsVisible);
        Assert.Equal((byte)128, presentation.Opacity);
    }

    [Fact]
    public void Rejects_root_attributes_above_the_configured_limit()
    {
        SvgRenderLimits limits = new(1024, 1024, 16, 16, 1024, 16, 16, 16, 16, 2);

        SvgException exception = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg' width='1' height='1'/>"u8.ToArray(), limits, CancellationToken.None));

        Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode);
    }

    [Fact]
    public void Rejects_child_attributes_above_the_configured_limit()
    {
        SvgRenderLimits limits = new(1024, 1024, 16, 16, 1024, 16, 16, 16, 16, 2);

        SvgException exception = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><rect width='1' height='1' fill='red'/></svg>"u8.ToArray(), limits, CancellationToken.None));

        Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode);
    }

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
    public void Expands_axis_aligned_line_definitions_through_typed_use_geometry()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><use href='#line' x='1' y='2' opacity='0.5'/><defs><line id='line' x1='0' y1='1' x2='2' y2='1' stroke='red' stroke-width='2'/></defs></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.AxisAlignedLine, geometry.Kind);
        Assert.Equal(new SvgAxisAlignedLine(1, 3, 3, 3, new SvgColor(255, 0, 0, 128), true, 2), geometry.AxisAlignedLine);
    }

    [Fact]
    public void Expands_axis_aligned_line_symbols_through_typed_use_geometry()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><symbol id='line'><line x2='2' stroke='red' stroke-width='2'/></symbol></defs><use href='#line' x='1' y='1'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.AxisAlignedLine, geometry.Kind);
        Assert.Equal(new SvgAxisAlignedLine(1, 1, 3, 1, new SvgColor(255, 0, 0, 255), true, 2), geometry.AxisAlignedLine);
    }

    [Fact]
    public void Applies_integer_transforms_to_axis_aligned_line_use_instances()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><line id='line' x1='1' y1='1' x2='2' y2='1' stroke='red' stroke-width='2'/></defs><use href='#line' x='3' y='2' transform='scale(2) translate(1, 0)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.AxisAlignedLine, geometry.Kind);
        Assert.Equal(new SvgAxisAlignedLine(10, 6, 12, 6, new SvgColor(255, 0, 0, 255), true, 4), geometry.AxisAlignedLine);
    }

    [Fact]
    public void Rejects_fractional_axis_aligned_line_use_offsets()
    {
        SvgException exception = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><line id='line' x2='2' stroke='red' stroke-width='2'/></defs><use href='#line' x='0.25'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, exception.ErrorCode);
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
    public void Applies_stylesheet_opacity_and_display_to_rectangle_use_instances()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><style>#half { opacity: 0.5; } .hidden { display: none; }</style><defs><rect id='tile' width='1' height='1' fill='red'/></defs><use id='half' href='#tile'/><use class='hidden' href='#tile'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal((byte)128, root.Rectangles[0].Color.Alpha);
        Assert.True(root.Rectangles[0].HasFill);
        Assert.False(root.Rectangles[1].HasFill);
    }

    [Fact]
    public void Applies_fill_style_to_inherited_rectangle_use_instances()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><rect id='tile' width='1' height='1'/></defs><use href='#tile' style='fill: red'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(new SvgRectangle(0, 0, 1, 1, new SvgColor(255, 0, 0, 255), true, null), Assert.Single(root.Rectangles));
    }

    [Fact]
    public void Applies_stylesheet_fill_to_inherited_rectangle_use_instances()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><style>#instance { fill: red; }</style><defs><rect id='tile' width='1' height='1'/></defs><use id='instance' href='#tile'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(new SvgColor(255, 0, 0, 255), Assert.Single(root.Rectangles).Color);
    }

    [Fact]
    public void Applies_fill_style_to_forward_inherited_rectangle_use_instances()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><use href='#tile' fill='red'/><defs><rect id='tile' width='1' height='1'/></defs></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(new SvgColor(255, 0, 0, 255), Assert.Single(root.Rectangles).Color);
    }

    [Fact]
    public void Rejects_fill_style_on_explicitly_painted_rectangle_use_instances()
    {
        SvgException exception = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><rect id='tile' width='1' height='1' fill='blue'/></defs><use href='#tile' fill='red'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, exception.ErrorCode);
    }

    [Fact]
    public void Rejects_fill_style_on_axis_aligned_line_use_instances()
    {
        SvgException exception = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><line id='line' x2='2' stroke='blue' stroke-width='2'/></defs><use href='#line' fill='red'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None));

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
    public void Applies_color_fill_and_element_alpha_in_documented_order()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><rect width='1' height='1' fill='#ff000001' fill-opacity='0.5' opacity='0.5'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(new SvgColor(255, 0, 0, 1), root.Rectangles[0].Color);
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
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><rect width='1' fill='rgb(100% 0% 0% / 50%)'/><rect width='1' fill='hsl(120 100% 50% / 0.5)'/><rect width='1' fill='rgba(1 2 3)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Collection(root.Rectangles,
            rectangle => Assert.Equal(new SvgColor(255, 0, 0, 128), rectangle.Color),
            rectangle => Assert.Equal(new SvgColor(0, 255, 0, 128), rectangle.Color),
            rectangle => Assert.Equal(new SvgColor(1, 2, 3, 255), rectangle.Color));
    }

    [Fact]
    public void Rejects_nonfinite_hsl_hues_as_malformed_input()
    {
        SvgException exception = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><rect width='1' height='1' fill='hsl(NaN 50% 50%)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.MalformedInput, exception.ErrorCode);
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
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><style>/* palette */ re/* type */ct { /* fill */ f/* property */ill: red; }</style><rect width='1'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

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
    public void Matches_class_selectors_without_materializing_class_tokens()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><style>.accent { fill: red; }</style><rect width='1' class='before&#x9;accent&#xA;after'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(new SvgColor(255, 0, 0, 255), root.Rectangles[0].Color);
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
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><style>rect { fill: blue; } [ id ] { fill: red; } [ class = accent ] { fill: lime; }</style><rect width='1'/><rect width='1' id='other'/><rect width='1' class='accent'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

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
    public void Applies_class_stylesheet_rules_to_supported_paths_and_point_lists()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><style>.path { fill: red; } .polygon { fill: blue; } .polyline { fill: lime; }</style><path class='path' d='M 0 0 H 1 V 1 H 0 Z'/><polygon class='polygon' points='1,0 2,0 2,1 1,1'/><polyline class='polyline' points='2,0 3,0 3,1 2,1'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Collection(root.Rectangles,
            rectangle => Assert.Equal(new SvgColor(255, 0, 0, 255), rectangle.Color),
            rectangle => Assert.Equal(new SvgColor(0, 0, 255, 255), rectangle.Color),
            rectangle => Assert.Equal(new SvgColor(0, 255, 0, 255), rectangle.Color));
    }

    [Fact]
    public void Applies_stylesheet_opacity_to_supported_paths()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><style>#path { fill: red; opacity: 0.5; }</style><path id='path' d='M 0 0 H 1 V 1 H 0 Z'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(new SvgColor(255, 0, 0, 128), root.Rectangles[0].Color);
    }

    [Fact]
    public void Applies_generic_stylesheet_selectors_to_supported_lines()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><style>#selected { stroke: red; } .hidden { display: none; }</style><line id='selected' x1='0' y1='1' x2='2' y2='1' stroke-width='2'/><line class='hidden' x1='0' y1='3' x2='2' y2='3' stroke='blue' stroke-width='2'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(new SvgColor(255, 0, 0, 255), root.Rectangles[0].Color);
        Assert.False(root.Rectangles[1].HasFill);
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
    public void Retains_axis_aligned_lines_as_typed_scene_geometry()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><line x1='1' y1='3' x2='5' y2='3' stroke='red' stroke-width='2'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.AxisAlignedLine, geometry.Kind);
        Assert.Equal(new SvgAxisAlignedLine(1, 3, 5, 3, new SvgColor(255, 0, 0, 255), true, 2), geometry.AxisAlignedLine);
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
    public void Retains_lowered_orthogonal_path_bands_as_typed_scene_geometry()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><path d='M 0,0 H 2 V 1 H 0 Z' fill='red'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.OrthogonalBand, geometry.Kind);
        Assert.Equal(new SvgRectangle(0, 0, 2, 1, new SvgColor(255, 0, 0, 255), true, null), geometry.Rectangle);
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
        SvgRenderLimits limits = new(1024, 1024, 10, 100, 1024, 1, 1, 100);
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
        SvgRoot commaSeparated = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg' viewBox='1,2,3,4'/>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(new SvgViewBox(1, 2, 3, 4, SvgViewBoxAlignment.None, SvgViewBoxAlignment.None, SvgViewBoxFit.None), root.ViewBox);
        Assert.Equal(new SvgViewBox(0, 0, 1, 2, SvgViewBoxAlignment.Max, SvgViewBoxAlignment.Min, SvgViewBoxFit.Meet), aligned.ViewBox);
        Assert.Equal(new SvgViewBox(1, 2, 3, 4, SvgViewBoxAlignment.Mid, SvgViewBoxAlignment.Mid, SvgViewBoxFit.Meet), commaSeparated.ViewBox);
    }

    [Fact]
    public void Enforces_the_node_limit_before_expanding_rectangle_storage()
    {
        SvgRenderLimits limits = new(1024, 1024, 10, 100, 1024, 1, 1, 100);
        SvgException exception = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><rect width='1' height='1' fill='#ff0000'/><rect width='1' height='1' fill='#ff0000'/></svg>"u8.ToArray(), limits, CancellationToken.None));
        Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode);
    }

    [Fact]
    public void Enforces_the_node_limit_for_inert_definition_descendants()
    {
        SvgRenderLimits limits = new(1024, 1024, 10, 100, 1024, 1, 1, 100);

        SvgException exception = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><g/></defs></svg>"u8.ToArray(), limits, CancellationToken.None));

        Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode);
    }

    [Fact]
    public void Enforces_the_XML_nesting_limit_before_expanding_group_storage()
    {
        const int nestedGroupCount = 128;
        string svg = "<svg xmlns='http://www.w3.org/2000/svg'>" + string.Concat(Enumerable.Repeat("<g>", nestedGroupCount)) + string.Concat(Enumerable.Repeat("</g>", nestedGroupCount)) + "</svg>";

        SvgException exception = Assert.Throws<SvgException>(() => SvgRootParser.Parse(System.Text.Encoding.UTF8.GetBytes(svg), SvgRenderLimits.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode);
    }

    [Fact]
    public void Uses_the_configured_XML_nesting_limit_before_expanding_group_storage()
    {
        SvgRenderLimits limits = new(1024, 1024, 10, 100, 1024, 16, 16, 100, 2);

        SvgException exception = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><g><rect/></g></svg>"u8.ToArray(), limits, CancellationToken.None));

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
    public void Enforces_the_work_limit_while_scanning_stylesheet_text()
    {
        string stylesheet = new string(' ', 4096) + "rect { fill: red; }";
        byte[] svg = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg'><style>" + stylesheet + "</style></svg>");
        SvgRenderLimits limits = new(10_000, 8_192, 16, 16, 1_024, 16, 16, 4);

        SvgException exception = Assert.Throws<SvgException>(() => SvgRootParser.Parse(svg, limits, CancellationToken.None));

        Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode);
    }

    [Fact]
    public void Enforces_the_work_limit_while_validating_xml_attributes()
    {
        SvgRenderLimits limits = new(100_000, 1_024, 16, 16, 1_024, 16, 16, 3);

        SvgException exception = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg' width='1' height='1'/>"u8.ToArray(), limits, CancellationToken.None));

        Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode);
        Assert.Contains("parsing work", exception.Message);
    }

    [Fact]
    public void Enforces_the_work_limit_while_removing_stylesheet_comments()
    {
        string stylesheet = "/*" + new string('x', 4096) + "*/ rect { fill: red; }";
        SvgRenderLimits limits = new(10_000, 10_000, 16, 16, 1_024, 16, 16, 6);

        SvgException exception = Assert.Throws<SvgException>(() => SvgRootParser.Parse(System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg'><style>" + stylesheet + "</style></svg>"), limits, CancellationToken.None));

        Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode);
    }

    [Fact]
    public void Reserves_comment_stripped_stylesheet_text_during_parsing()
    {
        string stylesheet = "/**/" + new string(' ', 512) + "rect { fill: red; }";
        byte[] svg = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg'><style>" + stylesheet + "</style><rect width='1'/></svg>");
        SvgRenderLimits insufficientLimits = new(132_188, 1_024, 16, 16, 1_024, 16, 16, 10_000);
        SvgRenderLimits sufficientLimits = new(132_944, 1_024, 16, 16, 1_024, 16, 16, 10_000);

        SvgException exception = Assert.Throws<SvgException>(() => SvgRootParser.Parse(svg, insufficientLimits, new RenderBudget(132_188), CancellationToken.None));
        using SvgRoot root = SvgRootParser.Parse(svg, sufficientLimits, new RenderBudget(132_944), CancellationToken.None);

        Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode);
        Assert.Single(root.Rectangles);
    }

    [Fact]
    public void Enforces_the_work_limit_while_scanning_stylesheet_rule_syntax()
    {
        string stylesheet = new('x', 4096);
        SvgRenderLimits limits = new(10_000, 10_000, 16, 16, 1_024, 16, 16, 5);

        SvgException exception = Assert.Throws<SvgException>(() => SvgRootParser.Parse(System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg'><style>" + stylesheet + "</style></svg>"), limits, CancellationToken.None));

        Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode);
    }

    [Fact]
    public void Enforces_the_work_limit_while_scanning_root_and_leaf_inline_style_text()
    {
        string style = new string(' ', 4096) + "fill: red";
        SvgRenderLimits rootLimits = new(10_000, 8_192, 16, 16, 1_024, 16, 16, 4);
        SvgRenderLimits leafLimits = new(10_000, 8_192, 16, 16, 1_024, 16, 16, 5);

        SvgException root = Assert.Throws<SvgException>(() => SvgRootParser.Parse(System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' style='" + style + "'/>") , rootLimits, CancellationToken.None));
        SvgException leaf = Assert.Throws<SvgException>(() => SvgRootParser.Parse(System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg'><rect style='" + style + "'/></svg>") , leafLimits, CancellationToken.None));

        Assert.Equal(SvgErrorCode.LimitExceeded, root.ErrorCode);
        Assert.Equal(SvgErrorCode.LimitExceeded, leaf.ErrorCode);
    }

    [Fact]
    public void Enforces_the_work_limit_while_reading_inline_style_declarations()
    {
        string style = new(';', 4096);
        SvgRenderLimits limits = new(10_000, 10_000, 16, 16, 1_024, 16, 16, 4);

        SvgException exception = Assert.Throws<SvgException>(() => SvgRootParser.Parse(System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' style='" + style + "'/>") , limits, CancellationToken.None));

        Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode);
    }

    [Fact]
    public void Enforces_the_work_limit_while_preflighting_path_and_point_list_text()
    {
        string values = new string(' ', 4097);
        SvgRenderLimits limits = new(10_000, 8_192, 16, 16, 1_024, 16, 16, 5);

        SvgException path = Assert.Throws<SvgException>(() => SvgRootParser.Parse(System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg'><path d='" + values + "'/></svg>") , limits, CancellationToken.None));
        SvgException polygon = Assert.Throws<SvgException>(() => SvgRootParser.Parse(System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg'><polygon points='" + values + "'/></svg>") , limits, CancellationToken.None));

        Assert.Equal(SvgErrorCode.LimitExceeded, path.ErrorCode);
        Assert.Equal(SvgErrorCode.LimitExceeded, polygon.ErrorCode);
    }

    [Fact]
    public void Enforces_the_work_limit_while_reading_geometry_tokens()
    {
        string value = new string(' ', 4096) + "X";
        SvgRenderLimits limits = new(10_000, 10_000, 16, 16, 1_024, 16, 16, 6);

        SvgException path = Assert.Throws<SvgException>(() => SvgRootParser.Parse(System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg'><path d='" + value + "'/></svg>") , limits, CancellationToken.None));
        SvgException polygon = Assert.Throws<SvgException>(() => SvgRootParser.Parse(System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg'><polygon points='" + value + "'/></svg>") , limits, CancellationToken.None));

        Assert.Equal(SvgErrorCode.LimitExceeded, path.ErrorCode);
        Assert.Equal(SvgErrorCode.LimitExceeded, polygon.ErrorCode);
    }

    [Fact]
    public void Enforces_the_identifier_syntax_limit_before_matching_or_reference_storage()
    {
        string identifier = new('a', SvgSyntaxLimits.MaximumIdentifierCharacters + 1);

        SvgException element = Assert.Throws<SvgException>(() => SvgRootParser.Parse(System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg'><rect id='" + identifier + "'/></svg>") , SvgRenderLimits.Default, CancellationToken.None));
        SvgException stylesheet = Assert.Throws<SvgException>(() => SvgRootParser.Parse(System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg'><style>." + identifier + " { fill: red; }</style></svg>") , SvgRenderLimits.Default, CancellationToken.None));
        SvgException reference = Assert.Throws<SvgException>(() => SvgRootParser.Parse(System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg'><use href='#" + identifier + "'/></svg>") , SvgRenderLimits.Default, CancellationToken.None));
        SvgException symbol = Assert.Throws<SvgException>(() => SvgRootParser.Parse(System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg'><defs><symbol id='" + identifier + "'><rect/></symbol></defs></svg>") , SvgRenderLimits.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.LimitExceeded, element.ErrorCode);
        Assert.Equal(SvgErrorCode.LimitExceeded, stylesheet.ErrorCode);
        Assert.Equal(SvgErrorCode.LimitExceeded, reference.ErrorCode);
        Assert.Equal(SvgErrorCode.LimitExceeded, symbol.ErrorCode);
    }

    [Fact]
    public void Enforces_value_and_transform_syntax_limits_before_expensive_parsing()
    {
        string value = new('1', SvgSyntaxLimits.MaximumValueTokenCharacters + 1);
        string transform = new('t', SvgSyntaxLimits.MaximumTransformCharacters + 1);

        SvgException rootLength = Assert.Throws<SvgException>(() => SvgRootParser.Parse(System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='" + value + "'/>") , SvgRenderLimits.Default, CancellationToken.None));
        SvgException coordinate = Assert.Throws<SvgException>(() => SvgRootParser.Parse(System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg'><rect x='" + value + "'/></svg>") , SvgRenderLimits.Default, CancellationToken.None));
        SvgException paint = Assert.Throws<SvgException>(() => SvgRootParser.Parse(System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg'><rect fill='" + value + "'/></svg>") , SvgRenderLimits.Default, CancellationToken.None));
        SvgException inlineStyle = Assert.Throws<SvgException>(() => SvgRootParser.Parse(System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg'><rect style='fill: " + value + "'/></svg>") , SvgRenderLimits.Default, CancellationToken.None));
        SvgException transformValue = Assert.Throws<SvgException>(() => SvgRootParser.Parse(System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg'><rect transform='" + transform + "'/></svg>") , SvgRenderLimits.Default, CancellationToken.None));
        SvgException pathCoordinate = Assert.Throws<SvgException>(() => SvgRootParser.Parse(System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg'><path d='M " + value + " 0'/></svg>") , SvgRenderLimits.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.LimitExceeded, rootLength.ErrorCode);
        Assert.Equal(SvgErrorCode.LimitExceeded, coordinate.ErrorCode);
        Assert.Equal(SvgErrorCode.LimitExceeded, paint.ErrorCode);
        Assert.Equal(SvgErrorCode.LimitExceeded, inlineStyle.ErrorCode);
        Assert.Equal(SvgErrorCode.LimitExceeded, transformValue.ErrorCode);
        Assert.Equal(SvgErrorCode.LimitExceeded, pathCoordinate.ErrorCode);
    }

    [Fact]
    public void Enforces_scalar_attribute_syntax_limits_before_presentation_validation()
    {
        string value = new('x', SvgSyntaxLimits.MaximumValueTokenCharacters + 1);

        SvgException display = Assert.Throws<SvgException>(() => SvgRootParser.Parse(System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' display='" + value + "'/>") , SvgRenderLimits.Default, CancellationToken.None));
        SvgException visibility = Assert.Throws<SvgException>(() => SvgRootParser.Parse(System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' visibility='" + value + "'/>") , SvgRenderLimits.Default, CancellationToken.None));
        SvgException stroke = Assert.Throws<SvgException>(() => SvgRootParser.Parse(System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' stroke='" + value + "'/>") , SvgRenderLimits.Default, CancellationToken.None));
        SvgException fillRule = Assert.Throws<SvgException>(() => SvgRootParser.Parse(System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg'><rect fill-rule='" + value + "'/></svg>") , SvgRenderLimits.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.LimitExceeded, display.ErrorCode);
        Assert.Equal(SvgErrorCode.LimitExceeded, visibility.ErrorCode);
        Assert.Equal(SvgErrorCode.LimitExceeded, stroke.ErrorCode);
        Assert.Equal(SvgErrorCode.LimitExceeded, fillRule.ErrorCode);
    }

    [Fact]
    public void Enforces_the_work_limit_while_resolving_forward_use_references()
    {
        SvgRenderLimits limits = new(1_000_000, 1_000_000, 16, 16, 1_000_000, 16, 16, 17);

        SvgException exception = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><use href='#tile'/><use href='#tile'/><defs><rect id='tile' width='1' height='1'/></defs></svg>"u8.ToArray(), limits, CancellationToken.None));

        Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode);
    }

    [Fact]
    public void Enforces_the_work_limit_while_adding_definition_entries()
    {
        SvgRenderLimits limits = new(1_000_000, 1_000_000, 16, 16, 1_000_000, 16, 16, 8);

        SvgException exception = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><rect id='first'/><rect id='second'/></defs></svg>"u8.ToArray(), limits, CancellationToken.None));

        Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode);
    }

    [Fact]
    public void Charges_parser_collection_sorting_to_the_shared_work_limit()
    {
        using BudgetedParserList<int> values = new(new RenderBudget(1_000), SvgMemoryCategory.Geometry, sizeof(int));
        values.Add(3);
        values.Add(1);
        values.Add(2);

        Assert.Throws<SvgException>(() => values.Sort(new ParserWorkCounter(2), CancellationToken.None));

        values.Sort(new ParserWorkCounter(20), CancellationToken.None);
        Assert.Equal([1, 2, 3], values.AsSpan().ToArray());
    }

    [Fact]
    public void Charges_scene_transfer_copies_to_the_shared_work_limit()
    {
        SvgRectangle rectangle = new(0, 0, 1, 1, new SvgColor(0, 0, 0, 255), true, null);
        using SvgSceneBuilder limited = new(new RenderBudget(10_000), new ParserWorkCounter(1), CancellationToken.None);
        limited.AddGeometries([SvgGeometry.FromRectangle(rectangle)]);

        Assert.Throws<SvgException>(() => limited.Build());

        using SvgSceneBuilder accepted = new(new RenderBudget(10_000), new ParserWorkCounter(20), CancellationToken.None);
        accepted.AddGeometries([SvgGeometry.FromRectangle(rectangle)]);
        using SvgSceneStorage storage = accepted.Build();
        Assert.Equal([rectangle], storage.Rectangles);
    }

    [Fact]
    public void Charges_parser_collection_transfer_copies_to_the_shared_work_limit()
    {
        RenderBudget budget = new(1_000);
        using (BudgetedParserList<int> limited = new(budget, SvgMemoryCategory.Parser, sizeof(int), new ParserWorkCounter(1), CancellationToken.None))
        {
            for (int value = 0; value < 5; value++) limited.Add(value);
            Assert.Throws<SvgException>(() => limited.Detach());
        }
        Assert.Equal(0, budget.Snapshot().BytesByCategory[(int)SvgMemoryCategory.Parser]);

        using BudgetedParserList<int> accepted = new(new RenderBudget(1_000), SvgMemoryCategory.Parser, sizeof(int), new ParserWorkCounter(20), CancellationToken.None);
        for (int value = 0; value < 5; value++) accepted.Add(value);
        using OwnedParserArray<int> storage = accepted.Detach();
        Assert.Equal([0, 1, 2, 3, 4], storage.Items);
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
    public void Parses_px_dimensions_but_keeps_relative_and_overlapping_unit_suffixes_explicitly_unsupported()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg' width='12.5px' height='7px'/>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);
        SvgException percentage = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg' width='100%' height='7'/>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None));
        SvgException relativeUnit = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg' width='1vmin' height='7'/>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None));

        Assert.Equal(12.5, root.Width);
        Assert.Equal(SvgErrorCode.UnsupportedFeature, percentage.ErrorCode);
        Assert.Equal(SvgErrorCode.UnsupportedFeature, relativeUnit.ErrorCode);
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
            command => Assert.Equal(new SvgDrawCommand(SvgDrawCommandKind.BeginGroup, -1, 128), command),
            command => Assert.Equal(new SvgDrawCommand(SvgDrawCommandKind.Geometry, 0, 255), command),
            command => Assert.Equal(new SvgDrawCommand(SvgDrawCommandKind.EndGroup, -1, 255), command));
        Assert.Equal(SvgGeometryKind.Rectangle, Assert.Single(root.Geometries).Kind);
    }

    [Fact]
    public void Represents_root_opacity_as_an_isolated_scene_command()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg' opacity='0.5'><rect width='1' height='1'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Collection(root.Commands,
            command => Assert.Equal(new SvgDrawCommand(SvgDrawCommandKind.BeginGroup, -1, 128), command),
            command => Assert.Equal(new SvgDrawCommand(SvgDrawCommandKind.Geometry, 0, 255), command),
            command => Assert.Equal(new SvgDrawCommand(SvgDrawCommandKind.EndGroup, -1, 255), command));
    }

    [Fact]
    public void Rejects_nested_group_opacity_pending_compositing_calibration()
    {
        SvgException exception = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><g opacity='0.5'><g opacity='0.5'><rect width='1' height='1'/></g></g></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, exception.ErrorCode);
    }

    [Fact]
    public void Categorizes_prohibited_external_entity_processing_as_malformed_input()
    {
        SvgException exception = Assert.Throws<SvgException>(() => SvgRootParser.Parse(System.Text.Encoding.UTF8.GetBytes("<!DOCTYPE svg [<!ENTITY x SYSTEM 'file:///nope'>]><svg xmlns='http://www.w3.org/2000/svg'/>") , SvgRenderLimits.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.MalformedInput, exception.ErrorCode);
    }

    [Fact]
    public void Reserves_secondary_parser_collections_before_they_allocate()
    {
        SvgRenderLimits limits = new(16_385, 1024, 16, 16, 1024, 16, 16, 16);

        SvgException group = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><g></g></svg>"u8.ToArray(), limits, new RenderBudget(16_385), CancellationToken.None));
        SvgException stylesheet = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><style>rect { fill: red; }</style></svg>"u8.ToArray(), limits, new RenderBudget(16_385), CancellationToken.None));
        SvgException definitions = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><rect id='sample'/></defs></svg>"u8.ToArray(), limits, new RenderBudget(16_385), CancellationToken.None));
        SvgException pendingUse = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><use href='#sample'/></svg>"u8.ToArray(), new SvgRenderLimits(17_023, 1024, 16, 16, 1024, 16, 16, 16), new RenderBudget(17_023), CancellationToken.None));

        Assert.Equal(SvgErrorCode.LimitExceeded, group.ErrorCode);
        Assert.Equal(SvgErrorCode.LimitExceeded, stylesheet.ErrorCode);
        Assert.Equal(SvgErrorCode.LimitExceeded, definitions.ErrorCode);
        Assert.Equal(SvgErrorCode.LimitExceeded, pendingUse.ErrorCode);
    }

    [Fact]
    public void Reserves_xml_reader_workspace_from_the_input_and_attribute_limits_before_parsing()
    {
        byte[] svg = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg'><metadata>" + new string('x', 9_000) + "</metadata></svg>");
        SvgRenderLimits limits = new(100_000, 100_000, 16, 16, 1024, 16, 16, 16);
        const long workspace = 256L * 512;

        SvgException exception = Assert.Throws<SvgException>(() => SvgRootParser.Parse(svg, limits, new RenderBudget(workspace - 1), CancellationToken.None));
        using SvgRoot root = SvgRootParser.Parse(svg, limits, new RenderBudget(workspace), CancellationToken.None);

        Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode);
        Assert.Empty(root.Rectangles);
    }

    [Fact]
    public void Reserves_xml_reader_attribute_workspace_from_the_configured_limit_before_parsing()
    {
        SvgRenderLimits limits = new(200_000, 1024, 16, 16, 1024, 16, 16, 16, 128, 257);
        const long workspace = 257L * 512;
        byte[] svg = "<svg xmlns='http://www.w3.org/2000/svg'/>"u8.ToArray();

        SvgException exception = Assert.Throws<SvgException>(() => SvgRootParser.Parse(svg, limits, new RenderBudget(workspace - 1), CancellationToken.None));
        using SvgRoot root = SvgRootParser.Parse(svg, limits, new RenderBudget(workspace), CancellationToken.None);

        Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode);
        Assert.Empty(root.Rectangles);
    }

    [Fact]
    public void Validates_class_lengths_before_stylesheet_matching()
    {
        string classValue = new('a', SvgSyntaxLimits.MaximumIdentifierCharacters + 1);
        using System.Xml.XmlReader reader = System.Xml.XmlReader.Create(new System.IO.StringReader("<svg xmlns='http://www.w3.org/2000/svg'><rect class='" + classValue + "'/></svg>"));
        Assert.True(reader.Read());
        Assert.True(reader.Read());
        RenderBudget budget = new(10_000);
        ParserWorkCounter workCounter = new(1);
        using SvgDocumentState state = new(budget, workCounter, CancellationToken.None);
        state.StyleRules.Add(new CssRule(CssSelectorKind.Class, "selected", null, 10, default));
        SvgRootAttributes root = new(new SvgInheritedPresentation(SvgPaint.Default, true, true, AxisTransform.Identity), null, 255, null, null, null);

        SvgException exception = Assert.Throws<SvgException>(() => SvgRenderedLeafParser.TryParse(reader, true, true, root, state, SvgRenderLimits.Default, budget, new Dictionary<string, SvgColor>(), workCounter, CancellationToken.None));

        Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode);
        Assert.Contains("identifiers and class values", exception.Message);
    }

    [Fact]
    public void Stylesheet_matching_enforces_identifier_limits_independently()
    {
        string classValue = new('a', SvgSyntaxLimits.MaximumIdentifierCharacters + 1);
        using System.Xml.XmlReader reader = System.Xml.XmlReader.Create(new System.IO.StringReader("<svg xmlns='http://www.w3.org/2000/svg'><rect class='" + classValue + "'/></svg>"));
        Assert.True(reader.Read());
        Assert.True(reader.Read());
        CssRule rule = new(CssSelectorKind.Class, "selected", null, 10, default);

        SvgException exception = Assert.Throws<SvgException>(() => rule.Matches(reader, true, false, false, new ParserWorkCounter(10), CancellationToken.None));

        Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode);
        Assert.Contains("identifiers and class values", exception.Message);
    }

    [Fact]
    public void Reserves_retained_stylesheet_strings_before_copying_selectors()
    {
        string identifier = new('a', SvgSyntaxLimits.MaximumIdentifierCharacters);
        byte[] svg = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg'><style>#" + identifier + " { fill: red; }</style><rect id='" + identifier + "' width='1' height='1'/></svg>");
        SvgRenderLimits limits = new(18_384, 4_096, 16, 256, 1_024, 16, 16, 16, 16, 1);

        SvgException exception = Assert.Throws<SvgException>(() => SvgRootParser.Parse(svg, limits, new RenderBudget(limits.MaximumAccountedBytes), CancellationToken.None));

        Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode);
    }

    [Fact]
    public void Reserves_definition_identifiers_before_retaining_them()
    {
        string identifier = new('a', SvgSyntaxLimits.MaximumIdentifierCharacters);
        byte[] svg = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg'><defs><rect id='" + identifier + "' width='1' height='1'/></defs></svg>");
        SvgRenderLimits limits = new(18_384, 4_096, 16, 256, 1_024, 16, 16, 16, 16, 1);

        SvgException exception = Assert.Throws<SvgException>(() => SvgRootParser.Parse(svg, limits, new RenderBudget(limits.MaximumAccountedBytes), CancellationToken.None));

        Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode);
    }

    [Fact]
    public void Reserves_the_definition_dictionary_prime_capacity_before_allocating()
    {
        using BudgetedParserDictionary<string, int> definitions = new(new RenderBudget(2_000), SvgMemoryCategory.Parser, 256);

        SvgException exception = Assert.Throws<SvgException>(() => definitions.TryAdd("id", 1));

        Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode);
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
        RenderBudget budget = new(300_000);

        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><style>rect { fill: red; }</style><defs><rect id='sample'/></defs><use href='#sample'/></svg>"u8.ToArray(), SvgRenderLimits.Default, budget, CancellationToken.None);

        Assert.Equal(0, budget.Snapshot().BytesByCategory[(int)SvgMemoryCategory.Parser]);
    }

    [Fact]
    public void Releases_transient_orthogonal_geometry_reservations_after_scene_construction()
    {
        RenderBudget budget = new(300_000);

        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><path d='M 0 0 H 2 V 2 H 0 Z' fill='red'/></svg>"u8.ToArray(), SvgRenderLimits.Default, budget, CancellationToken.None);

        Assert.Single(root.Rectangles);
        Assert.Equal(0, budget.Snapshot().BytesByCategory[(int)SvgMemoryCategory.Geometry]);
    }
}
