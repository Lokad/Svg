using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;
using System.Text;

namespace Lokad.Svg.Tests;

public sealed class SvgRootParserTests
{
    [Fact]
    public void Root_parser_uses_a_caller_owned_work_counter()
    {
        var budget = new RenderBudget(1_024 * 1_024);

        SvgException exception = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'/>"u8.ToArray(), SvgRenderLimits.Default, budget, null, new ParserWorkCounter(0), CancellationToken.None));

        Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode);
    }

    [Fact]
    public void Container_presentation_resolves_inherited_paint_display_visibility_and_opacity()
    {
        IReadOnlyDictionary<string, SvgColor> namedColors = new Dictionary<string, SvgColor>(StringComparer.OrdinalIgnoreCase) { ["red"] = new SvgColor(255, 0, 0, 255) };
        SvgComputedStyle style = new("red", "currentColor", "0.5", "none", "none", "hidden", "0.5", null);

        SvgResolvedPresentation presentation = SvgContainerPresentationResolver.Resolve(style, SvgPaint.Default, true, true, namedColors);

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
    public void Expands_a_group_nested_rectangle_definition_through_use()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><g><defs><rect id='tile' x='1' y='2' width='3' height='4' fill='red'/></defs></g><use href='#tile' x='5' y='6'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Single(root.Rectangles);
        Assert.Equal(new SvgRectangle(6, 8, 3, 4, new SvgColor(255, 0, 0, 255), true, null), root.Rectangles[0]);
        Assert.False(root.HasUnsupportedChild);
    }

    [Fact]
    public void Expands_a_group_nested_definition_for_a_local_use_instance()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><g><defs><rect id='tile' width='2' height='2' fill='red'/></defs><use href='#tile'/></g></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Single(root.Rectangles);
        Assert.Equal(new SvgRectangle(0, 0, 2, 2, new SvgColor(255, 0, 0, 255), true, null), root.Rectangles[0]);
        Assert.False(root.HasUnsupportedChild);
    }

    [Fact]
    public void Does_not_paint_group_nested_definitions_without_use()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><g><defs><rect id='tile' width='2' height='2' fill='red'/></defs></g></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Empty(root.Geometries);
        Assert.False(root.HasUnsupportedChild);
    }

    [Fact]
    public void Rejects_group_wrapped_definition_content_instead_of_painting_it()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><g><defs><g><rect width='5' height='5' fill='red'/></g></defs></g></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Empty(root.Geometries);
        Assert.True(root.HasUnsupportedChild);
    }

    [Fact]
    public void Rejects_group_wrapped_definition_paths_instead_of_painting_them()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><g><defs><path d='M0,0 H5 V5 H0 Z' fill='red'/></defs></g></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Empty(root.Geometries);
        Assert.True(root.HasUnsupportedChild);
    }

    [Fact]
    public void Rejects_group_wrapped_definitions_at_conversion()
    {
        byte[] svg = "<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><g><defs><g><rect width='5' height='5' fill='red'/></g></defs></g></svg>"u8.ToArray();

        SvgException exception = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(svg, SvgRenderOptions.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, exception.ErrorCode);
    }

    [Fact]
    public void Leaves_direct_definition_content_inert()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><rect width='5' height='5' fill='red'/></defs></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Empty(root.Geometries);
        Assert.False(root.HasUnsupportedChild);
    }

    [Fact]
    public void Renders_only_the_first_switch_child()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><switch><rect x='1' y='2' width='3' height='4' fill='red'/><rect x='5' y='6' width='7' height='8' fill='blue'/></switch></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Single(root.Rectangles);
        Assert.Equal(new SvgRectangle(1, 2, 3, 4, new SvgColor(255, 0, 0, 255), true, null), root.Rectangles[0]);
        Assert.False(root.HasUnsupportedChild);
    }

    [Fact]
    public void Switch_applies_group_transforms_to_its_first_child()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><switch transform='translate(1, 2)'><rect width='2' height='2' fill='red'/><rect width='2' height='2' fill='blue'/></switch></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Single(root.Rectangles);
        Assert.Equal(new SvgRectangle(1, 2, 2, 2, new SvgColor(255, 0, 0, 255), true, null), root.Rectangles[0]);
        Assert.False(root.HasUnsupportedChild);
    }

    [Fact]
    public void Empty_switch_renders_nothing()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><switch/><rect width='1' height='1' fill='red'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Single(root.Rectangles);
        Assert.False(root.HasUnsupportedChild);
    }

    [Fact]
    public void Rejects_switch_children_with_conditional_attributes()
    {
        SvgException exception = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><switch><rect requiredFeatures='http://www.w3.org/TR/SVG11/feature#BasicStructure' width='1' height='1' fill='red'/></switch></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, exception.ErrorCode);
    }

    [Fact]
    public void Renders_switch_content_byte_identical_to_direct_content()
    {
        byte[] direct = "<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><rect x='1' y='1' width='2' height='2' fill='red'/></svg>"u8.ToArray();
        byte[] switched = "<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><switch><rect x='1' y='1' width='2' height='2' fill='red'/></switch></svg>"u8.ToArray();

        ReadOnlyMemory<byte> directPng = SvgConverter.ConvertToPng(direct, SvgRenderOptions.Default, CancellationToken.None);
        ReadOnlyMemory<byte> switchedPng = SvgConverter.ConvertToPng(switched, SvgRenderOptions.Default, CancellationToken.None);

        Assert.True(directPng.Span.SequenceEqual(switchedPng.Span));
    }

    [Fact]
    public void Rejects_link_nested_definitions()
    {
        SvgException exception = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><a><defs><rect id='tile' width='2' height='2' fill='red'/></defs></a><use href='#tile'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, exception.ErrorCode);
    }

    [Fact]
    public void Renders_group_nested_definitions_byte_identical_to_direct_definitions()
    {
        byte[] direct = "<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><defs><rect id='r' width='2' height='2' fill='red'/></defs><use href='#r'/></svg>"u8.ToArray();
        byte[] nested = "<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><g><defs><rect id='r' width='2' height='2' fill='red'/></defs></g><use href='#r'/></svg>"u8.ToArray();

        ReadOnlyMemory<byte> directPng = SvgConverter.ConvertToPng(direct, SvgRenderOptions.Default, CancellationToken.None);
        ReadOnlyMemory<byte> nestedPng = SvgConverter.ConvertToPng(nested, SvgRenderOptions.Default, CancellationToken.None);

        Assert.True(directPng.Span.SequenceEqual(nestedPng.Span));
    }

    [Fact]
    public void Expands_a_prior_rectangle_definition_through_a_fractional_use_offset()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><rect id='tile' width='2' height='2' fill='red'/></defs><use href='#tile' x='1.5'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Single(root.Rectangles);
        Assert.Equal(new SvgRectangle(1.5, 0, 2, 2, new SvgColor(255, 0, 0, 255), true, null), root.Rectangles[0]);
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
    public void Important_stylesheet_fill_beats_higher_specificity_normal_fill()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><style>rect { fill: blue !important; } #r { fill: red; }</style><rect id='r' width='1' height='1'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(new SvgColor(0, 0, 255, 255), root.Rectangles[0].Color);
    }

    [Fact]
    public void Important_stylesheet_fill_beats_normal_inline_fill()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><style>#r { fill: blue !important; }</style><rect id='r' width='1' height='1' style='fill: red'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(new SvgColor(0, 0, 255, 255), root.Rectangles[0].Color);
    }

    [Fact]
    public void Important_inline_fill_beats_important_stylesheet_fill()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><style>#r { fill: blue !important; }</style><rect id='r' width='1' height='1' style='fill: red !important'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(new SvgColor(255, 0, 0, 255), root.Rectangles[0].Color);
    }

    [Fact]
    public void Important_inline_fill_beats_later_normal_inline_fill()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><rect width='1' height='1' style='fill: red !important; fill: blue'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

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
    public void Quantizes_decimal_and_percentage_opacity_identically()
    {
        for (int percent = 0; percent <= 100; percent++)
        {
            double value = percent / 100.0;
            byte decimalForm = SvgColorParser.ParseOpacity(value.ToString("G17", System.Globalization.CultureInfo.InvariantCulture));
            byte percentForm = SvgColorParser.Parse("rgba(255, 0, 0, " + percent.ToString(System.Globalization.CultureInfo.InvariantCulture) + "%)", SvgRootParser.CssNamedColors).Alpha;

            Assert.True(decimalForm == percentForm, "Opacity " + percent + "% quantizes differently across decimal and percentage spellings.");
        }
    }

    [Fact]
    public void Quantizes_attribute_opacity_percentages_identically()
    {
        for (int percent = 0; percent <= 100; percent++)
        {
            double value = percent / 100.0;
            byte decimalForm = SvgColorParser.ParseOpacity(value.ToString("G17", System.Globalization.CultureInfo.InvariantCulture));
            byte percentForm = SvgColorParser.ParseOpacity(percent.ToString(System.Globalization.CultureInfo.InvariantCulture) + "%");

            Assert.True(decimalForm == percentForm, "Opacity " + percent + "% quantizes differently across decimal and attribute percentage spellings.");
        }
    }

    [Fact]
    public void Rejects_out_of_range_opacity_percentages()
    {
        SvgException above = Assert.Throws<SvgException>(() => SvgColorParser.ParseOpacity("101%"));
        SvgException below = Assert.Throws<SvgException>(() => SvgColorParser.ParseOpacity("-1%"));
        SvgException malformed = Assert.Throws<SvgException>(() => SvgColorParser.ParseOpacity("lots%"));

        Assert.Equal(SvgErrorCode.MalformedInput, above.ErrorCode);
        Assert.Equal(SvgErrorCode.MalformedInput, below.ErrorCode);
        Assert.Equal(SvgErrorCode.MalformedInput, malformed.ErrorCode);
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
    public void Resolves_current_color_for_bounded_rectangle_and_line_strokes()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><rect width='4' height='4' fill='none' color='blue' stroke='currentColor' stroke-width='2'/><line x1='0' y1='6' x2='4' y2='6' color='red' stroke='currentColor' stroke-width='2'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(new SvgStroke(new SvgColor(0, 0, 255, 255), 2), root.Rectangles[0].Stroke);
        Assert.Equal(new SvgRectangle(0, 5, 4, 2, new SvgColor(255, 0, 0, 255), true, null), root.Rectangles[1]);
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
    public void Lowers_evenodd_counter_path_to_bands_with_hole()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><path d='M8,8 H56 V56 H8 Z M20,20 V44 H44 V20 Z' fill-rule='evenodd' fill='red'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Collection(root.Rectangles,
            rectangle => Assert.Equal(new SvgRectangle(8, 8, 48, 12, new SvgColor(255, 0, 0, 255), true, null), rectangle),
            rectangle => Assert.Equal(new SvgRectangle(8, 20, 12, 24, new SvgColor(255, 0, 0, 255), true, null), rectangle),
            rectangle => Assert.Equal(new SvgRectangle(44, 20, 12, 24, new SvgColor(255, 0, 0, 255), true, null), rectangle),
            rectangle => Assert.Equal(new SvgRectangle(8, 44, 48, 12, new SvgColor(255, 0, 0, 255), true, null), rectangle));
    }

    [Fact]
    public void Lowers_same_winding_nonzero_counter_path_to_solid_bands()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><path d='M8,8 H56 V56 H8 Z M20,20 H44 V44 H20 Z' fill-rule='nonzero' fill='red'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Collection(root.Rectangles,
            rectangle => Assert.Equal(new SvgRectangle(8, 8, 48, 12, new SvgColor(255, 0, 0, 255), true, null), rectangle),
            rectangle => Assert.Equal(new SvgRectangle(8, 20, 12, 24, new SvgColor(255, 0, 0, 255), true, null), rectangle),
            rectangle => Assert.Equal(new SvgRectangle(20, 20, 24, 24, new SvgColor(255, 0, 0, 255), true, null), rectangle),
            rectangle => Assert.Equal(new SvgRectangle(44, 20, 12, 24, new SvgColor(255, 0, 0, 255), true, null), rectangle),
            rectangle => Assert.Equal(new SvgRectangle(8, 44, 48, 12, new SvgColor(255, 0, 0, 255), true, null), rectangle));
    }

    [Fact]
    public void Rejects_intersecting_path_contours()
    {
        SvgException exception = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><path d='M0,0 H4 V4 H0 Z M2,2 H6 V6 H2 Z' fill='red'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, exception.ErrorCode);
    }

    [Fact]
    public void Rejects_unclosed_path_subpath()
    {
        SvgException exception = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><path d='M0,0 H2 V2 H0 Z M4,4 H6 V6 H4'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, exception.ErrorCode);
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
    public void Lowers_relative_rectangle_path_to_same_bands_as_absolute()
    {
        SvgRoot relative = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><path d='m 1,2 h 3 v 4 h -3 z' fill='red'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);
        SvgRoot absolute = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><path d='M 1,2 H 4 V 6 H 1 Z' fill='red'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(absolute.Rectangles, relative.Rectangles);
        Assert.Collection(relative.Rectangles,
            rectangle => Assert.Equal(new SvgRectangle(1, 2, 3, 4, new SvgColor(255, 0, 0, 255), true, null), rectangle));
    }

    [Fact]
    public void Lowers_implicit_lineto_path_to_same_bands_as_explicit()
    {
        SvgRoot implicitPath = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><path d='M0,0 3,0 3,1 1,1 1,3 0,3Z' fill='red'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Collection(implicitPath.Rectangles,
            rectangle => Assert.Equal(new SvgRectangle(0, 0, 3, 1, new SvgColor(255, 0, 0, 255), true, null), rectangle),
            rectangle => Assert.Equal(new SvgRectangle(0, 1, 1, 2, new SvgColor(255, 0, 0, 255), true, null), rectangle));
    }

    [Fact]
    public void Lowers_compact_repeated_commands_to_same_bands()
    {
        SvgRoot compact = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><path d='M0,0H2 3V1H0Z' fill='red'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Collection(compact.Rectangles,
            rectangle => Assert.Equal(new SvgRectangle(0, 0, 3, 1, new SvgColor(255, 0, 0, 255), true, null), rectangle));
    }

    [Fact]
    public void Lowers_relative_counter_subpath_to_the_same_hole()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><path d='M8,8H56V56H8Z m12,12 v24 h24 v-24 Z' fill-rule='evenodd' fill='red'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Collection(root.Rectangles,
            rectangle => Assert.Equal(new SvgRectangle(8, 8, 48, 12, new SvgColor(255, 0, 0, 255), true, null), rectangle),
            rectangle => Assert.Equal(new SvgRectangle(8, 20, 12, 24, new SvgColor(255, 0, 0, 255), true, null), rectangle),
            rectangle => Assert.Equal(new SvgRectangle(44, 20, 12, 24, new SvgColor(255, 0, 0, 255), true, null), rectangle),
            rectangle => Assert.Equal(new SvgRectangle(8, 44, 48, 12, new SvgColor(255, 0, 0, 255), true, null), rectangle));
    }

    [Fact]
    public void Rejects_relative_diagonal_segments()
    {
        SvgException exception = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><path d='m0,0 l2,1 h-2 z'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None));

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
    public void Reserves_raw_and_comment_stripped_stylesheet_text_during_parsing()
    {
        string stylesheet = "/**/" + new string(' ', 512) + "rect { fill: red; }";
        byte[] svg = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg'><style>" + stylesheet + "</style><rect width='1'/></svg>");
        SvgRenderLimits insufficientLimits = new(140_397, 1_024, 16, 16, 1_024, 16, 16, 10_000);
        SvgRenderLimits sufficientLimits = new(140_398, 1_024, 16, 16, 1_024, 16, 16, 10_000);

        SvgException exception = Assert.Throws<SvgException>(() => SvgRootParser.Parse(svg, insufficientLimits, new RenderBudget(140_397), CancellationToken.None));
        using SvgRoot root = SvgRootParser.Parse(svg, sufficientLimits, new RenderBudget(140_398), CancellationToken.None);

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
        using BudgetedParserList<int> values = new(new RenderBudget(1_000), SvgMemoryCategory.Geometry);
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
    public void Scene_growth_reserves_replacement_geometry_before_releasing_its_previous_capacity()
    {
        RenderBudget budget = new(10_000);
        SvgRectangle rectangle = new(0, 0, 1, 1, new SvgColor(0, 0, 0, 255), true, null);
        using var scene = new SvgSceneBuilder(budget, new ParserWorkCounter(100), CancellationToken.None);
        for (int index = 0; index < 4; index++) scene.AddGeometry(SvgGeometry.FromRectangle(rectangle));
        long beforeGrowth = budget.Snapshot().CurrentBytes;

        scene.AddGeometry(SvgGeometry.FromRectangle(rectangle));

        RenderBudgetSnapshot afterGrowth = budget.Snapshot();
        Assert.True(afterGrowth.PeakBytes > afterGrowth.CurrentBytes);
        Assert.True(afterGrowth.PeakBytes > beforeGrowth);
    }

    [Fact]
    public void Charges_parser_collection_transfer_copies_to_the_shared_work_limit()
    {
        RenderBudget budget = new(1_000);
        using (BudgetedParserList<int> limited = new(budget, SvgMemoryCategory.Parser, new ParserWorkCounter(1), CancellationToken.None))
        {
            for (int value = 0; value < 5; value++) limited.Add(value);
            Assert.Throws<SvgException>(() => limited.Detach());
        }
        Assert.Equal(0, budget.Snapshot().BytesByCategory[(int)SvgMemoryCategory.Parser]);

        using BudgetedParserList<int> accepted = new(new RenderBudget(1_000), SvgMemoryCategory.Parser, new ParserWorkCounter(20), CancellationToken.None);
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
    public void Represents_sequential_group_opacity_scopes()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><g opacity='0.5'><rect width='1' height='1'/></g><g opacity='0.5'><rect width='1' height='1'/></g></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Collection(root.Commands,
            command => Assert.Equal(new SvgDrawCommand(SvgDrawCommandKind.BeginGroup, -1, 128), command),
            command => Assert.Equal(new SvgDrawCommand(SvgDrawCommandKind.Geometry, 0, 255), command),
            command => Assert.Equal(new SvgDrawCommand(SvgDrawCommandKind.EndGroup, -1, 255), command),
            command => Assert.Equal(new SvgDrawCommand(SvgDrawCommandKind.BeginGroup, -1, 128), command),
            command => Assert.Equal(new SvgDrawCommand(SvgDrawCommandKind.Geometry, 1, 255), command),
            command => Assert.Equal(new SvgDrawCommand(SvgDrawCommandKind.EndGroup, -1, 255), command));
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
        SvgRootAttributes root = new(new SvgInheritedPresentation(SvgPaint.Default, true, true, AxisTransform.Identity), SvgInheritedTextStyle.Default, null, 255, null, null, null);

        SvgException exception = Assert.Throws<SvgException>(() => SvgRenderedLeafParser.TryParse(reader, true, true, root, state, SvgRenderLimits.Default, budget, new Dictionary<string, SvgColor>(), null, workCounter, CancellationToken.None));

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
        using BudgetedParserList<SvgPoint> points = new(budget, SvgMemoryCategory.Geometry);

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

    [Fact]
    public void Clips_a_crossing_rectangle_to_its_nested_viewport_box()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><svg x='8' y='8' width='32' height='32'><rect width='64' height='64' fill='red'/></svg></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(new SvgRectangle(8, 8, 32, 32, new SvgColor(255, 0, 0, 255), true, null), Assert.Single(root.Rectangles));
    }

    [Fact]
    public void Clips_a_use_instance_to_its_nested_viewport_box()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><rect id='tile' width='64' height='64' fill='red'/></defs><svg x='8' y='8' width='32' height='32'><use href='#tile'/></svg></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(new SvgRectangle(8, 8, 32, 32, new SvgColor(255, 0, 0, 255), true, null), Assert.Single(root.Rectangles));
    }

    [Fact]
    public void Offsets_nested_viewport_content_by_the_viewport_origin()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><svg x='8' y='8' width='32' height='32'><rect width='4' height='4' fill='red'/></svg></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(new SvgRectangle(8, 8, 4, 4, new SvgColor(255, 0, 0, 255), true, null), Assert.Single(root.Rectangles));
    }

    [Fact]
    public void Clips_an_axis_aligned_line_to_its_nested_viewport_box()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><svg x='8' y='8' width='32' height='32'><line x1='0' y1='10' x2='64' y2='10' stroke='red' stroke-width='2'/></svg></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.AxisAlignedLine, geometry.Kind);
        Assert.Equal(new SvgAxisAlignedLine(8, 18, 40, 18, new SvgColor(255, 0, 0, 255), true, 2), geometry.AxisAlignedLine);
    }

    [Fact]
    public void Keeps_a_stroked_rectangle_fully_inside_its_nested_viewport()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><svg x='8' y='8' width='32' height='32'><rect x='10' y='10' width='4' height='4' fill='red' stroke='blue' stroke-width='2'/></svg></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(new SvgRectangle(18, 18, 4, 4, new SvgColor(255, 0, 0, 255), true, new SvgStroke(new SvgColor(0, 0, 255, 255), 2)), Assert.Single(root.Rectangles));
    }

    [Fact]
    public void Rejects_a_stroked_rectangle_crossing_a_nested_viewport_boundary()
    {
        SvgException exception = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><svg x='8' y='8' width='32' height='32'><rect width='64' height='64' fill='red' stroke='blue' stroke-width='2'/></svg></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, exception.ErrorCode);
    }

    [Fact]
    public void Rejects_text_inside_a_nested_viewport()
    {
        SvgException exception = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><svg x='8' y='8' width='32' height='32'><text font-family='Example'>A</text></svg></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, exception.ErrorCode);
    }

    [Fact]
    public void Expands_a_symbol_viewBox_through_use_with_explicit_dimensions()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><symbol id='tile' viewBox='0 0 16 16'><rect width='16' height='16' fill='red'/></symbol></defs><use href='#tile' x='16' y='16' width='32' height='32'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(new SvgRectangle(16, 16, 32, 32, new SvgColor(255, 0, 0, 255), true, null), Assert.Single(root.Rectangles));
    }

    [Fact]
    public void Defaults_dimless_symbol_use_to_the_full_parent_viewport()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg' width='64' height='64'><defs><symbol id='tile' viewBox='0 0 16 16'><rect width='16' height='16' fill='red'/></symbol></defs><use href='#tile' x='16' y='16'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(new SvgRectangle(16, 16, 64, 64, new SvgColor(255, 0, 0, 255), true, null), Assert.Single(root.Rectangles));
    }

    [Fact]
    public void Maps_symbol_content_without_a_viewBox_one_to_one()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><symbol id='tile'><rect width='16' height='16' fill='red'/></symbol></defs><use href='#tile' x='16' y='16' width='32' height='32'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(new SvgRectangle(16, 16, 16, 16, new SvgColor(255, 0, 0, 255), true, null), Assert.Single(root.Rectangles));
    }

    [Fact]
    public void Applies_instance_fill_override_inside_symbols()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><symbol id='tile' viewBox='0 0 16 16'><rect width='16' height='16'/></symbol></defs><use href='#tile' x='16' y='16' width='32' height='32' fill='blue'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(new SvgRectangle(16, 16, 32, 32, new SvgColor(0, 0, 255, 255), true, null), Assert.Single(root.Rectangles));
    }

    [Fact]
    public void Centers_meet_mapped_symbol_content_in_the_use_viewport()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><symbol id='tile' viewBox='0 0 16 8'><rect width='16' height='8' fill='red'/></symbol></defs><use href='#tile' width='32' height='32'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(new SvgRectangle(0, 8, 32, 16, new SvgColor(255, 0, 0, 255), true, null), Assert.Single(root.Rectangles));
    }

    [Fact]
    public void Composes_enclosing_group_transforms_around_symbol_viewports()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><symbol id='tile' viewBox='0 0 16 16'><rect width='16' height='16' fill='red'/></symbol></defs><g transform='translate(4)'><use href='#tile' x='16' y='16' width='32' height='32'/></g></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(new SvgRectangle(20, 16, 32, 32, new SvgColor(255, 0, 0, 255), true, null), Assert.Single(root.Rectangles));
    }
    [Fact]
    public void Clips_a_group_to_its_clip_path_box()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><clipPath id='window'><rect x='4' y='4' width='8' height='8'/></clipPath></defs><g clip-path='url(#window)'><rect width='16' height='16' fill='red'/></g></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(new SvgRectangle(4, 4, 8, 8, new SvgColor(255, 0, 0, 255), true, null), Assert.Single(root.Rectangles));
    }

    [Fact]
    public void Clips_a_leaf_to_its_clip_path_attribute()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><clipPath id='window'><rect x='4' y='4' width='8' height='8'/></clipPath></defs><rect width='16' height='16' fill='red' clip-path='url(#window)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(new SvgRectangle(4, 4, 8, 8, new SvgColor(255, 0, 0, 255), true, null), Assert.Single(root.Rectangles));
    }

    [Fact]
    public void Maps_nested_viewbox_meet_content_with_centering()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg' width='64' height='64'><svg x='0' y='0' width='48' height='24' viewBox='0 0 12 24'><rect x='0' y='0' width='12' height='24' fill='red'/></svg></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(new SvgRectangle(18, 0, 12, 24, new SvgColor(255, 0, 0, 255), true, null), Assert.Single(root.Rectangles));
    }

    [Fact]
    public void Maps_nested_viewbox_none_content_without_preserving_aspect()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg' width='64' height='64'><svg x='0' y='0' width='48' height='24' viewBox='0 0 12 24' preserveAspectRatio='none'><rect x='0' y='0' width='12' height='24' fill='red'/></svg></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(new SvgRectangle(0, 0, 48, 24, new SvgColor(255, 0, 0, 255), true, null), Assert.Single(root.Rectangles));
    }

    [Fact]
    public void Maps_nested_viewbox_slice_content_with_cropping()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg' width='64' height='64'><svg x='0' y='0' width='48' height='24' viewBox='0 0 12 24' preserveAspectRatio='xMidYMid slice'><rect x='0' y='0' width='12' height='24' fill='red'/></svg></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(new SvgRectangle(0, 0, 48, 24, new SvgColor(255, 0, 0, 255), true, null), Assert.Single(root.Rectangles));
    }

    [Fact]
    public void Admits_strokes_in_unscaled_viewbox_viewports()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg' width='64' height='64'><svg x='8' y='8' width='48' height='48' viewBox='8 8 48 48'><rect x='16' y='16' width='8' height='8' fill='red' stroke='blue' stroke-width='2'/></svg></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(new SvgRectangle(16, 16, 8, 8, new SvgColor(255, 0, 0, 255), true, new SvgStroke(new SvgColor(0, 0, 255, 255), 2)), Assert.Single(root.Rectangles));
    }
    [Fact]
    public void Resolves_percentage_nested_viewport_dimensions()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg' width='64' height='64'><svg x='50%' y='50%' width='50%' height='50%'><rect x='0' y='0' width='8' height='8' fill='red'/></svg></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(new SvgRectangle(32, 32, 8, 8, new SvgColor(255, 0, 0, 255), true, null), Assert.Single(root.Rectangles));
    }

    [Fact]
    public void Resolves_percentage_use_dimensions()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg' width='64' height='64'><defs><rect id='tile' width='8' height='8' fill='red'/></defs><use href='#tile' x='50%' y='50%' width='50%' height='50%'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(new SvgRectangle(32, 32, 8, 8, new SvgColor(255, 0, 0, 255), true, null), Assert.Single(root.Rectangles));
    }

    [Fact]
    public void Rejects_percentage_dimensions_with_unknown_parent_dimensions()
    {
        SvgException viewport = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><svg width='50%' height='50%'><rect width='8' height='8' fill='red'/></svg></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None));
        SvgException use = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><rect id='tile' width='8' height='8' fill='red'/></defs><use href='#tile' width='50%'/></svg></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, viewport.ErrorCode);
        Assert.Equal(SvgErrorCode.UnsupportedFeature, use.ErrorCode);
    }

    [Fact]
    public void Rejects_negative_percentage_dimensions()
    {
        SvgException exception = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg' width='64' height='64'><svg width='-50%' height='50%'><rect width='8' height='8' fill='red'/></svg></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.MalformedInput, exception.ErrorCode);
    }

    [Fact]
    public void Rejects_malformed_percentage_dimensions()
    {
        SvgException exception = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg' width='64' height='64'><svg width='lots%' height='50%'><rect width='8' height='8' fill='red'/></svg></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.MalformedInput, exception.ErrorCode);
    }

    [Fact]
    public void Rejects_strokes_in_scaled_viewports()
    {
        SvgException exception = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg' width='64' height='64'><svg x='8' y='8' width='48' height='48' viewBox='0 0 24 24'><rect x='0' y='0' width='8' height='8' fill='red' stroke='blue' stroke-width='2'/></svg></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, exception.ErrorCode);
    }

    [Fact]
    public void Rejects_lines_in_scaled_viewports()
    {
        SvgException exception = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg' width='64' height='64'><svg x='8' y='8' width='48' height='48' viewBox='0 0 24 24'><line x1='0' y1='0' x2='8' y2='0' stroke='red' stroke-width='2'/></svg></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, exception.ErrorCode);
    }

    [Fact]
    public void Rejects_pattern_fills_in_scaled_viewports()
    {
        SvgException exception = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg' width='64' height='64'><defs><pattern id='tiles' width='8' height='8' patternUnits='userSpaceOnUse'><rect width='8' height='8' fill='red'/></pattern></defs><svg x='8' y='8' width='48' height='48' viewBox='0 0 24 24'><rect x='0' y='0' width='8' height='8' fill='url(#tiles)'/></svg></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, exception.ErrorCode);
    }

    [Fact]
    public void Rejects_use_instances_in_scaled_viewports()
    {
        SvgException exception = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg' width='64' height='64'><defs><rect id='tile' width='8' height='8' fill='red'/></defs><svg x='8' y='8' width='48' height='48' viewBox='0 0 24 24'><use href='#tile'/></svg></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, exception.ErrorCode);
    }

    [Fact]
    public void Rejects_clip_paths_in_scaled_viewports()
    {
        SvgException exception = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg' width='64' height='64'><defs><clipPath id='window'><rect x='8' y='8' width='8' height='8'/></clipPath></defs><svg x='8' y='8' width='48' height='48' viewBox='0 0 24 24'><rect x='0' y='0' width='8' height='8' fill='red' clip-path='url(#window)'/></svg></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, exception.ErrorCode);
    }

    [Fact]
    public void Rejects_custom_mask_regions_in_scaled_viewports()
    {
        SvgException exception = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg' width='64' height='64'><defs><mask id='window' maskUnits='userSpaceOnUse' x='1' y='1' width='2' height='2'><rect x='0' y='0' width='8' height='8' fill='#808080'/></mask></defs><svg x='8' y='8' width='48' height='48' viewBox='0 0 24 24'><rect x='0' y='0' width='8' height='8' fill='red' mask='url(#window)'/></svg></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, exception.ErrorCode);
    }

    [Fact]
    public void Clips_a_rectangle_to_its_object_bounding_box_clip()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><clipPath id='window' clipPathUnits='objectBoundingBox'><rect x='0.25' y='0.25' width='0.5' height='0.5'/></clipPath></defs><rect x='8' y='8' width='48' height='48' fill='red' clip-path='url(#window)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(new SvgRectangle(20, 20, 24, 24, new SvgColor(255, 0, 0, 255), true, null), Assert.Single(root.Rectangles));
    }

    [Fact]
    public void Rejects_object_bounding_box_clips_on_paths()
    {
        SvgException exception = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><clipPath id='window' clipPathUnits='objectBoundingBox'><rect x='0' y='0' width='1' height='1'/></clipPath></defs><path d='M 0,0 H 4 V 4 H 0 Z' fill='red' clip-path='url(#window)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, exception.ErrorCode);
    }

    [Fact]
    public void Rejects_object_bounding_box_clips_on_use_instances()
    {
        SvgException exception = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><rect id='tile' width='4' height='4' fill='red'/><clipPath id='window' clipPathUnits='objectBoundingBox'><rect x='0' y='0' width='1' height='1'/></clipPath></defs><use href='#tile' clip-path='url(#window)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, exception.ErrorCode);
    }

    [Fact]
    public void Rejects_unknown_clip_path_unit_values()
    {
        SvgException exception = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><clipPath id='window' clipPathUnits='userSpace'><rect x='0' y='0' width='1' height='1'/></clipPath></defs><rect width='8' height='8' fill='red' clip-path='url(#window)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, exception.ErrorCode);
    }

    [Fact]
    public void Rejects_transformed_object_bounding_box_clip_content()
    {
        SvgException exception = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><clipPath id='window' clipPathUnits='objectBoundingBox'><rect x='0' y='0' width='1' height='1' transform='translate(1)'/></clipPath></defs><rect width='8' height='8' fill='red' clip-path='url(#window)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, exception.ErrorCode);
    }

    [Fact]
    public void Intersects_nested_clip_path_boxes()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><clipPath id='a'><rect x='2' y='2' width='12' height='12'/></clipPath><clipPath id='b'><rect x='4' y='4' width='8' height='8'/></clipPath></defs><g clip-path='url(#a)'><g clip-path='url(#b)'><rect width='16' height='16' fill='red'/></g></g></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(new SvgRectangle(4, 4, 8, 8, new SvgColor(255, 0, 0, 255), true, null), Assert.Single(root.Rectangles));
    }

    [Fact]
    public void Rejects_unknown_clip_path_references()
    {
        SvgException exception = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><g clip-path='url(#missing)'><rect width='8' height='8' fill='red'/></g></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, exception.ErrorCode);
    }

    [Fact]
    public void Rejects_multi_rectangle_clip_paths()
    {
        SvgException exception = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><clipPath id='window'><rect x='4' y='4' width='4' height='4'/><rect x='8' y='8' width='4' height='4'/></clipPath></defs><g clip-path='url(#window)'><rect width='8' height='8' fill='red'/></g></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, exception.ErrorCode);
    }

    [Fact]
    public void Rejects_text_inside_clipped_groups()
    {
        SvgException exception = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><clipPath id='window'><rect x='4' y='4' width='8' height='8'/></clipPath></defs><g clip-path='url(#window)'><text font-family='Example'>A</text></g></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, exception.ErrorCode);
    }
    [Fact]
    public void Lowers_a_two_stop_gradient_fill_to_gradient_geometry()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='g' x1='0' y1='0' x2='1' y2='1'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><rect x='8' y='8' width='48' height='48' fill='url(#g)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.GradientFill, geometry.Kind);
        Assert.Equal(new SvgLinearGradient(new SvgColor(255, 0, 0, 255), new SvgColor(0, 0, 255, 255), 0, 0, 1, 1, 0, 1, false, false, SvgGradientSpread.Pad), geometry.Gradient.Gradient);
        Assert.Equal(new SvgRectangle(8, 8, 48, 48, new SvgColor(0, 0, 0, 0), true, null), geometry.Gradient.Fill);
    }

    [Fact]
    public void Lowers_offset_two_stop_gradient_fill_with_retained_offsets()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='g' x1='0' y1='0' x2='1' y2='0'><stop offset='0.25' stop-color='#ff0000'/><stop offset='0.75' stop-color='#0000ff'/></linearGradient></defs><rect x='8' y='8' width='48' height='48' fill='url(#g)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.GradientFill, geometry.Kind);
        Assert.Equal(new SvgLinearGradient(new SvgColor(255, 0, 0, 255), new SvgColor(0, 0, 255, 255), 0, 0, 1, 0, 0.25, 0.75, false, false, SvgGradientSpread.Pad), geometry.Gradient.Gradient);
    }

    [Fact]
    public void Rejects_equal_offset_linear_gradient_stops()
    {
        SvgException exception = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='g'><stop offset='0.5' stop-color='#ff0000'/><stop offset='0.5' stop-color='#0000ff'/></linearGradient></defs><rect width='8' height='8' fill='url(#g)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, exception.ErrorCode);
    }

    [Fact]
    public void Rejects_reversed_offset_linear_gradient_stops()
    {
        SvgException exception = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='g'><stop offset='0.75' stop-color='#ff0000'/><stop offset='0.25' stop-color='#0000ff'/></linearGradient></defs><rect width='8' height='8' fill='url(#g)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, exception.ErrorCode);
    }

    [Fact]
    public void Clamps_outside_offset_linear_gradient_stops_into_range()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='g' x1='0' y1='0' x2='1' y2='0'><stop offset='-0.5' stop-color='#ff0000'/><stop offset='1.5' stop-color='#0000ff'/></linearGradient></defs><rect x='8' y='8' width='48' height='48' fill='url(#g)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.GradientFill, geometry.Kind);
        Assert.Equal(new SvgLinearGradient(new SvgColor(255, 0, 0, 255), new SvgColor(0, 0, 255, 255), 0, 0, 1, 0, 0, 1, false, false, SvgGradientSpread.Pad), geometry.Gradient.Gradient);
    }

    [Fact]
    public void Rejects_clamped_equal_offset_linear_gradient_stops()
    {
        SvgException above = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='g'><stop offset='2' stop-color='#ff0000'/><stop offset='3' stop-color='#0000ff'/></linearGradient></defs><rect width='8' height='8' fill='url(#g)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None));
        SvgException below = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='g'><stop offset='-2' stop-color='#ff0000'/><stop offset='-1' stop-color='#0000ff'/></linearGradient></defs><rect width='8' height='8' fill='url(#g)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, above.ErrorCode);
        Assert.Equal(SvgErrorCode.UnsupportedFeature, below.ErrorCode);
    }


    [Fact]
    public void Lowers_user_space_gradient_with_its_declared_vector()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='g' gradientUnits='userSpaceOnUse' x1='8' y1='0' x2='56' y2='0'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><rect x='8' y='8' width='48' height='48' fill='url(#g)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.GradientFill, geometry.Kind);
        Assert.Equal(new SvgLinearGradient(new SvgColor(255, 0, 0, 255), new SvgColor(0, 0, 255, 255), 8, 0, 56, 0, 0, 1, false, true, SvgGradientSpread.Pad), geometry.Gradient.Gradient);
    }

    [Fact]
    public void Maps_user_space_gradient_vectors_through_enclosing_transforms()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='g' gradientUnits='userSpaceOnUse' x1='0' y1='0' x2='8' y2='0'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><g transform='translate(8, 8)'><rect x='0' y='0' width='8' height='8' fill='url(#g)'/></g></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.GradientFill, geometry.Kind);
        Assert.Equal(new SvgLinearGradient(new SvgColor(255, 0, 0, 255), new SvgColor(0, 0, 255, 255), 8, 8, 16, 8, 0, 1, false, true, SvgGradientSpread.Pad), geometry.Gradient.Gradient);
    }

    [Fact]
    public void Rejects_percentage_user_space_gradient_coordinates()
    {
        SvgException exception = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='g' gradientUnits='userSpaceOnUse' x1='0%' y1='0%' x2='100%' y2='0%'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><rect width='8' height='8' fill='url(#g)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, exception.ErrorCode);
    }

    [Fact]
    public void Rejects_user_space_gradients_inside_transformed_viewports()
    {
        SvgException exception = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg' width='64' height='64'><defs><linearGradient id='g' gradientUnits='userSpaceOnUse' x1='0' y1='0' x2='8' y2='0'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><svg x='8' y='8' width='16' height='16'><rect x='0' y='0' width='8' height='8' fill='url(#g)'/></svg></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, exception.ErrorCode);
    }

    [Fact]
    public void Lowers_gradient_transform_vectors_with_composed_coordinates()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='g' x1='0' y1='0' x2='1' y2='0' gradientTransform='scale(2)'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><rect x='8' y='8' width='48' height='48' fill='url(#g)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.GradientFill, geometry.Kind);
        Assert.Equal(new SvgLinearGradient(new SvgColor(255, 0, 0, 255), new SvgColor(0, 0, 255, 255), 0, 0, 2, 0, 0, 1, false, false, SvgGradientSpread.Pad), geometry.Gradient.Gradient);
    }

    [Fact]
    public void Rejects_fractional_gradient_transforms()
    {
        SvgException exception = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='g' gradientTransform='translate(0.5, 0)'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><rect width='8' height='8' fill='url(#g)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, exception.ErrorCode);
    }

    [Fact]
    public void Rejects_rotated_gradient_transforms()
    {
        SvgException exception = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='g' gradientTransform='rotate(45)'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><rect width='8' height='8' fill='url(#g)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, exception.ErrorCode);
    }
    [Fact]
    public void Lowers_repeat_spread_gradients_with_retained_spread()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='g' x1='0' y1='0' x2='0.5' y2='0' spreadMethod='repeat'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><rect x='8' y='8' width='48' height='48' fill='url(#g)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(new SvgLinearGradient(new SvgColor(255, 0, 0, 255), new SvgColor(0, 0, 255, 255), 0, 0, 0.5, 0, 0, 1, false, false, SvgGradientSpread.Repeat), geometry.Gradient.Gradient);
    }

    [Fact]
    public void Lowers_reflect_spread_gradients_with_retained_spread()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='g' x1='0' y1='0' x2='0.5' y2='0' spreadMethod='reflect'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><rect x='8' y='8' width='48' height='48' fill='url(#g)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(new SvgLinearGradient(new SvgColor(255, 0, 0, 255), new SvgColor(0, 0, 255, 255), 0, 0, 0.5, 0, 0, 1, false, false, SvgGradientSpread.Reflect), geometry.Gradient.Gradient);
    }

    [Fact]
    public void Inherits_spread_methods_through_gradient_href()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='b' spreadMethod='reflect'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient><linearGradient id='c' href='#b'/></defs><rect x='8' y='8' width='48' height='48' fill='url(#c)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGradientSpread.Reflect, geometry.Gradient.Gradient.Spread);
    }

    [Fact]
    public void Rejects_unknown_spread_methods()
    {
        SvgException exception = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='g' spreadMethod='tile'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><rect width='8' height='8' fill='url(#g)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, exception.ErrorCode);
    }

    [Fact]
    public void Inherits_stops_and_vector_through_gradient_href()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='b' x1='0' y1='0' x2='1' y2='0'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient><linearGradient id='c' href='#b'/></defs><rect x='8' y='8' width='48' height='48' fill='url(#c)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.GradientFill, geometry.Kind);
        Assert.Equal(new SvgLinearGradient(new SvgColor(255, 0, 0, 255), new SvgColor(0, 0, 255, 255), 0, 0, 1, 0, 0, 1, false, false, SvgGradientSpread.Pad), geometry.Gradient.Gradient);
    }

    [Fact]
    public void Overrides_vector_through_gradient_href()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='b'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient><linearGradient id='c' href='#b' x1='0' y1='0' x2='0.5' y2='0'/></defs><rect x='8' y='8' width='48' height='48' fill='url(#c)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(new SvgLinearGradient(new SvgColor(255, 0, 0, 255), new SvgColor(0, 0, 255, 255), 0, 0, 0.5, 0, 0, 1, false, false, SvgGradientSpread.Pad), geometry.Gradient.Gradient);
    }

    [Fact]
    public void Overrides_stops_through_gradient_href()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='b' x1='0' y1='0' x2='1' y2='1'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient><linearGradient id='c' href='#b'><stop offset='0' stop-color='#00ff00'/><stop offset='1' stop-color='#ffff00'/></linearGradient></defs><rect x='8' y='8' width='48' height='48' fill='url(#c)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(new SvgLinearGradient(new SvgColor(0, 255, 0, 255), new SvgColor(255, 255, 0, 255), 0, 0, 1, 1, 0, 1, false, false, SvgGradientSpread.Pad), geometry.Gradient.Gradient);
    }

    [Fact]
    public void Inherits_transparency_through_gradient_href()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='b'/><linearGradient id='c' href='#b'/></defs><rect x='8' y='8' width='48' height='48' fill='url(#c)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.False(geometry.Gradient.Fill.HasFill);
    }

    [Fact]
    public void Rejects_forward_gradient_references()
    {
        SvgException exception = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='c' href='#b'/><linearGradient id='b'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><rect width='8' height='8' fill='url(#c)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, exception.ErrorCode);
    }

    [Fact]
    public void Rejects_missing_gradient_references()
    {
        SvgException exception = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='c' href='#nope'/></defs><rect width='8' height='8' fill='url(#c)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, exception.ErrorCode);
    }

    [Fact]
    public void Rejects_wrong_kind_gradient_references()
    {
        SvgException exception = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><rect id='r' width='8' height='8' fill='red'/><linearGradient id='c' href='#r'/></defs><rect width='8' height='8' fill='url(#c)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, exception.ErrorCode);
    }

    [Fact]
    public void Rejects_partial_vector_overrides_with_href()
    {
        SvgException exception = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='b'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient><linearGradient id='c' href='#b' x1='0'/></defs><rect width='8' height='8' fill='url(#c)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, exception.ErrorCode);
    }

    [Fact]
    public void Rejects_unit_overrides_without_vector_with_href()
    {
        SvgException exception = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='b'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient><linearGradient id='c' href='#b' gradientUnits='userSpaceOnUse'/></defs><rect width='8' height='8' fill='url(#c)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, exception.ErrorCode);
    }

    [Fact]
    public void Resolves_transitive_gradient_href_chains()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='a' x1='0' y1='0' x2='1' y2='0'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient><linearGradient id='b' href='#a'/><linearGradient id='c' href='#b' x1='0' y1='0' x2='0.5' y2='0'/></defs><rect x='8' y='8' width='48' height='48' fill='url(#c)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(new SvgLinearGradient(new SvgColor(255, 0, 0, 255), new SvgColor(0, 0, 255, 255), 0, 0, 0.5, 0, 0, 1, false, false, SvgGradientSpread.Pad), geometry.Gradient.Gradient);
    }

    [Fact]
    public void Composes_child_transforms_over_inherited_vectors()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='a' x1='0' y1='0' x2='1' y2='0'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient><linearGradient id='b' href='#a' gradientTransform='translate(1, 0)'/></defs><rect x='8' y='8' width='48' height='48' fill='url(#b)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(new SvgLinearGradient(new SvgColor(255, 0, 0, 255), new SvgColor(0, 0, 255, 255), 1, 0, 2, 0, 0, 1, false, false, SvgGradientSpread.Pad), geometry.Gradient.Gradient);
    }

    [Fact]
    public void Lowers_solid_fill_with_gradient_stroke_to_bands()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='g' x1='0' y1='0' x2='1' y2='0'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><rect x='16' y='16' width='32' height='32' fill='#00ff00' stroke='url(#g)' stroke-width='8'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.GradientStroke, geometry.Kind);
        Assert.Equal(new SvgRectangle(16, 16, 32, 32, new SvgColor(0, 255, 0, 255), true, null), geometry.GradientStroke.Fill);
        Assert.Equal(new SvgRectangle(12, 12, 40, 8, new SvgColor(0, 0, 0, 0), true, null), geometry.GradientStroke.Top);
        Assert.Equal(new SvgRectangle(12, 44, 40, 8, new SvgColor(0, 0, 0, 0), true, null), geometry.GradientStroke.Bottom);
        Assert.Equal(new SvgRectangle(12, 20, 8, 24, new SvgColor(0, 0, 0, 0), true, null), geometry.GradientStroke.Left);
        Assert.Equal(new SvgRectangle(44, 20, 8, 24, new SvgColor(0, 0, 0, 0), true, null), geometry.GradientStroke.Right);
        Assert.Equal(new SvgLinearGradient(new SvgColor(255, 0, 0, 255), new SvgColor(0, 0, 255, 255), 0, 0, 1, 0, 0, 1, false, false, SvgGradientSpread.Pad), geometry.GradientStroke.Gradient);
        Assert.True(geometry.GradientStroke.HasStroke);
    }

    [Fact]
    public void Lowers_unpainted_fill_with_gradient_stroke_to_bands_only()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='g'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><rect x='16' y='16' width='32' height='32' fill='none' stroke='url(#g)' stroke-width='8'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.GradientStroke, geometry.Kind);
        Assert.False(geometry.GradientStroke.Fill.HasFill);
        Assert.True(geometry.GradientStroke.HasStroke);
    }

    [Fact]
    public void Rejects_gradient_fills_with_gradient_strokes()
    {
        SvgException exception = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='g'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient><linearGradient id='h'><stop offset='0' stop-color='#00ff00'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><rect width='8' height='8' fill='url(#g)' stroke='url(#h)' stroke-width='2'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, exception.ErrorCode);
    }

    [Fact]
    public void Rejects_pattern_strokes()
    {
        SvgException exception = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><pattern id='tiles' width='8' height='8' patternUnits='userSpaceOnUse'><rect width='8' height='8' fill='red'/></pattern></defs><rect width='8' height='8' fill='red' stroke='url(#tiles)' stroke-width='2'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, exception.ErrorCode);
    }

    [Fact]
    public void Applies_a_gray_mask_to_gradient_stroke_geometry()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='g'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient><mask id='m'><rect x='0' y='0' width='64' height='64' fill='#808080'/></mask></defs><rect x='16' y='16' width='32' height='32' fill='#00ff00' stroke='url(#g)' stroke-width='8' mask='url(#m)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.GradientStroke, geometry.Kind);
        Assert.Equal(128, geometry.GradientStroke.Opacity);
        Assert.Equal(new SvgColor(0, 255, 0, 128), geometry.GradientStroke.Fill.Color);
        Assert.Equal(new SvgRectangle(12, 12, 40, 8, new SvgColor(0, 0, 0, 0), true, null), geometry.GradientStroke.Top);
    }

    [Fact]
    public void Admits_an_empty_linear_gradient_as_transparent()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='g'/></defs><rect x='8' y='8' width='48' height='48' fill='url(#g)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.GradientFill, geometry.Kind);
        Assert.False(geometry.Gradient.Fill.HasFill);
    }

    [Fact]
    public void Rejects_degenerate_linear_gradient_vectors()
    {
        SvgException exception = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='g' x1='1' y1='1' x2='1' y2='1'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><rect width='8' height='8' fill='url(#g)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, exception.ErrorCode);
    }

    [Fact]
    public void Rejects_three_stop_linear_gradients()
    {
        SvgException exception = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='g'><stop offset='0' stop-color='#ff0000'/><stop offset='0.5' stop-color='#00ff00'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><rect width='8' height='8' fill='url(#g)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, exception.ErrorCode);
    }

    [Fact]
    public void Rejects_translucent_gradient_stops()
    {
        SvgException exception = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='g'><stop offset='0' stop-color='#ff0000' stop-opacity='0.5'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><rect width='8' height='8' fill='url(#g)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, exception.ErrorCode);
    }
    [Fact]
    public void Stamps_a_pattern_tile_across_its_fill_region()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><pattern id='tiles' width='16' height='16' patternUnits='userSpaceOnUse'><rect width='8' height='8' fill='red'/></pattern></defs><rect x='8' y='8' width='48' height='48' fill='url(#tiles)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.PatternFill, geometry.Kind);
        Assert.Equal(new SvgRectangle(0, 0, 8, 8, new SvgColor(255, 0, 0, 255), true, null), geometry.Pattern.Content);
        Assert.Equal(new SvgRectangle(8, 8, 48, 48, new SvgColor(0, 0, 0, 0), true, null), geometry.Pattern.Fill);
    }

    [Fact]
    public void Rejects_pattern_fills_on_non_rectangle_leaves()
    {
        SvgException exception = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><pattern id='tiles' width='16' height='16' patternUnits='userSpaceOnUse'><rect width='8' height='8' fill='red'/></pattern></defs><line x1='0' y1='0' x2='4' y2='0' stroke='red' stroke-width='2' fill='url(#tiles)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, exception.ErrorCode);
    }

    [Fact]
    public void Rejects_multi_rectangle_pattern_tiles()
    {
        SvgException exception = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><pattern id='tiles' width='16' height='16' patternUnits='userSpaceOnUse'><rect width='8' height='8' fill='red'/><rect x='8' y='8' width='8' height='8' fill='blue'/></pattern></defs><rect width='8' height='8' fill='url(#tiles)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, exception.ErrorCode);
    }

    [Fact]
    public void Rejects_object_bounding_box_pattern_units()
    {
        SvgException exception = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><pattern id='tiles' width='16' height='16'><rect width='8' height='8' fill='red'/></pattern></defs><rect width='8' height='8' fill='url(#tiles)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, exception.ErrorCode);
    }
    [Fact]
    public void Lowers_a_solid_embedded_image_to_a_rectangle()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><image href='data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGP4z8DwHwAFAAH/iZk9HQAAAABJRU5ErkJggg==' x='8' y='8' width='48' height='48'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(new SvgRectangle(8, 8, 48, 48, new SvgColor(255, 0, 0, 255), true, null), Assert.Single(root.Rectangles));
    }

    [Fact]
    public void Defaults_dimless_embedded_images_to_the_full_parent_viewport()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg' width='64' height='64'><image href='data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGP4z8DwHwAFAAH/iZk9HQAAAABJRU5ErkJggg==' x='8' y='8'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(new SvgRectangle(8, 8, 64, 64, new SvgColor(255, 0, 0, 255), true, null), Assert.Single(root.Rectangles));
    }

    [Fact]
    public void Rejects_non_data_embedded_image_references()
    {
        SvgException exception = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><image href='http://example.com/a.png' x='8' y='8' width='48' height='48'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, exception.ErrorCode);
    }

    [Fact]
    public void Rejects_malformed_embedded_image_data()
    {
        SvgException exception = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><image href='data:image/png;base64,!!!' x='8' y='8' width='48' height='48'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.MalformedInput, exception.ErrorCode);
    }
    [Fact]
    public void Applies_a_white_mask_without_changing_content()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><mask id='fade'><rect x='8' y='8' width='48' height='48' fill='#ffffff'/></mask></defs><rect x='8' y='8' width='48' height='48' fill='#ff0000' mask='url(#fade)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(new SvgRectangle(8, 8, 48, 48, new SvgColor(255, 0, 0, 255), true, null), Assert.Single(root.Rectangles));
    }

    [Fact]
    public void Applies_a_gray_mask_as_translucency()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><mask id='fade'><rect x='8' y='8' width='48' height='48' fill='#808080'/></mask></defs><rect x='8' y='8' width='48' height='48' fill='#ff0000' mask='url(#fade)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(new SvgRectangle(8, 8, 48, 48, new SvgColor(255, 0, 0, 128), true, null), Assert.Single(root.Rectangles));
    }

    [Fact]
    public void Applies_an_alpha_type_mask_from_channel_alpha()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><mask id='fade' mask-type='alpha'><rect x='8' y='8' width='48' height='48' fill='#00ff00'/></mask></defs><rect x='8' y='8' width='48' height='48' fill='#ff0000' mask='url(#fade)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(new SvgRectangle(8, 8, 48, 48, new SvgColor(255, 0, 0, 255), true, null), Assert.Single(root.Rectangles));
    }

    [Fact]
    public void Registers_an_empty_mask_as_transparent()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><mask id='fade'/></defs><rect x='8' y='8' width='48' height='48' fill='#ff0000' mask='url(#fade)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(new SvgRectangle(8, 8, 48, 48, new SvgColor(255, 0, 0, 0), true, null), Assert.Single(root.Rectangles));
    }


    [Fact]
    public void Clips_masked_content_to_explicit_regions()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><mask id='window' maskUnits='userSpaceOnUse' x='16' y='16' width='32' height='32'><rect x='0' y='0' width='64' height='64' fill='#808080'/></mask></defs><rect x='8' y='8' width='48' height='48' fill='#ff0000' mask='url(#window)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(new SvgRectangle(16, 16, 32, 32, new SvgColor(255, 0, 0, 128), true, null), Assert.Single(root.Rectangles));
    }

    [Fact]
    public void Rejects_object_bounding_box_mask_regions()
    {
        SvgException exception = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><mask id='window' x='0' y='0' width='1' height='1'><rect x='0' y='0' width='8' height='8' fill='#808080'/></mask></defs><rect width='8' height='8' fill='red' mask='url(#window)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, exception.ErrorCode);
    }

    [Fact]
    public void Rejects_partial_user_space_mask_regions()
    {
        SvgException exception = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><mask id='window' maskUnits='userSpaceOnUse' x='1' y='1'><rect x='0' y='0' width='8' height='8' fill='#808080'/></mask></defs><rect width='8' height='8' fill='red' mask='url(#window)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, exception.ErrorCode);
    }

    [Fact]
    public void Rejects_unknown_mask_unit_values()
    {
        SvgException exception = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><mask id='window' maskUnits='picas'><rect x='0' y='0' width='8' height='8' fill='#808080'/></mask></defs><rect width='8' height='8' fill='red' mask='url(#window)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, exception.ErrorCode);
    }

    [Fact]
    public void Rejects_non_user_space_mask_content_units()
    {
        SvgException exception = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><mask id='window' maskContentUnits='objectBoundingBox'><rect x='0' y='0' width='8' height='8' fill='#808080'/></mask></defs><rect width='8' height='8' fill='red' mask='url(#window)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, exception.ErrorCode);
    }

    [Fact]
    public void Rejects_custom_mask_regions_on_use_instances()
    {
        SvgException exception = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><rect id='tile' width='8' height='8' fill='red'/><mask id='window' maskUnits='userSpaceOnUse' x='1' y='1' width='2' height='2'><rect x='0' y='0' width='8' height='8' fill='#808080'/></mask></defs><use href='#tile' mask='url(#window)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, exception.ErrorCode);
    }

    [Fact]
    public void Rejects_custom_mask_regions_on_masked_groups()
    {
        SvgException exception = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><mask id='window' maskUnits='userSpaceOnUse' x='1' y='1' width='2' height='2'><rect x='0' y='0' width='8' height='8' fill='#808080'/></mask></defs><g mask='url(#window)'><rect width='8' height='8' fill='red'/></g></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, exception.ErrorCode);
    }

    [Fact]
    public void Rejects_chromatic_luminance_mask_content()
    {
        SvgException exception = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><mask id='fade'><rect x='8' y='8' width='48' height='48' fill='#ff0000'/></mask></defs><rect width='8' height='8' fill='red' mask='url(#fade)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, exception.ErrorCode);
    }

    [Fact]
    public void Passes_white_masked_groups_through_without_isolation()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><mask id='fade'><rect x='8' y='8' width='48' height='48' fill='#ffffff'/></mask></defs><g mask='url(#fade)'><rect width='8' height='8' fill='red'/></g></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Collection(root.Commands,
            command => Assert.Equal(new SvgDrawCommand(SvgDrawCommandKind.Geometry, 0, 255), command));
    }

    [Fact]
    public void Represents_gray_masked_groups_as_an_isolated_scene_command()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><mask id='fade'><rect x='8' y='8' width='48' height='48' fill='#808080'/></mask></defs><g mask='url(#fade)'><rect width='8' height='8' fill='red'/></g></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Collection(root.Commands,
            command => Assert.Equal(new SvgDrawCommand(SvgDrawCommandKind.BeginGroup, -1, 128), command),
            command => Assert.Equal(new SvgDrawCommand(SvgDrawCommandKind.Geometry, 0, 255), command),
            command => Assert.Equal(new SvgDrawCommand(SvgDrawCommandKind.EndGroup, -1, 255), command));
    }

    [Fact]
    public void Combines_group_opacity_with_mask_values()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><mask id='fade'><rect x='8' y='8' width='48' height='48' fill='#808080'/></mask></defs><g opacity='0.5' mask='url(#fade)'><rect width='8' height='8' fill='red'/></g></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Collection(root.Commands,
            command => Assert.Equal(new SvgDrawCommand(SvgDrawCommandKind.BeginGroup, -1, 64), command),
            command => Assert.Equal(new SvgDrawCommand(SvgDrawCommandKind.Geometry, 0, 255), command),
            command => Assert.Equal(new SvgDrawCommand(SvgDrawCommandKind.EndGroup, -1, 255), command));
    }

    [Fact]
    public void Rejects_opacity_scopes_inside_masked_groups()
    {
        SvgException exception = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><mask id='fade'><rect x='8' y='8' width='48' height='48' fill='#808080'/></mask></defs><g mask='url(#fade)'><g opacity='0.5'><rect width='8' height='8' fill='red'/></g></g></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, exception.ErrorCode);
    }

    [Fact]
    public void Rejects_missing_mask_references_on_groups()
    {
        SvgException exception = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><g mask='url(#missing)'><rect width='8' height='8' fill='red'/></g></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, exception.ErrorCode);
    }

    [Fact]
    public void Rejects_unknown_mask_references()
    {
        SvgException exception = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><rect width='8' height='8' fill='red' mask='url(#missing)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, exception.ErrorCode);
    }
}
