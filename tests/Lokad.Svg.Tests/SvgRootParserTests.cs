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

        SvgResolvedPresentation presentation = SvgContainerPresentationResolver.Resolve(style, SvgPaint.Default, null, true, true, namedColors);

        Assert.Equal(new SvgColor(255, 0, 0, 255), presentation.Paint.CurrentColor);
        Assert.True(presentation.Paint.FillUsesCurrentColor);
        Assert.Equal(new SvgPaintOpacity(128, 0.5f), presentation.Paint.FillOpacity);
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
        Assert.Equal(12.5, root.Width?.Value);
        Assert.Equal(7, root.Height?.Value);
        Assert.False(root.Width?.IsPercentage);
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
    public void Expands_a_group_use_with_offsets()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><g id='tile'><rect width='2' height='2' fill='red'/></g><use href='#tile' x='5' y='6'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(2, root.Rectangles.Length);
        Assert.Equal(new SvgRectangle(0, 0, 2, 2, new SvgColor(255, 0, 0, 255), true, null), root.Rectangles[0]);
        Assert.Equal(new SvgRectangle(5, 6, 2, 2, new SvgColor(255, 0, 0, 255), true, null), root.Rectangles[1]);
        Assert.False(root.HasUnsupportedChild);
    }

    [Fact]
    public void Expands_nested_group_uses_transitively()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><g id='inner'><rect id='r' width='2' height='2' fill='red'/><use href='#r' x='2'/></g><use href='#inner' y='4'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(4, root.Rectangles.Length);
        Assert.Equal(new SvgRectangle(0, 0, 2, 2, new SvgColor(255, 0, 0, 255), true, null), root.Rectangles[0]);
        Assert.Equal(new SvgRectangle(2, 0, 2, 2, new SvgColor(255, 0, 0, 255), true, null), root.Rectangles[1]);
        Assert.Equal(new SvgRectangle(0, 4, 2, 2, new SvgColor(255, 0, 0, 255), true, null), root.Rectangles[2]);
        Assert.Equal(new SvgRectangle(2, 4, 2, 2, new SvgColor(255, 0, 0, 255), true, null), root.Rectangles[3]);
        Assert.False(root.HasUnsupportedChild);
    }

    [Fact]
    public void Resolves_a_forward_group_reference()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><use href='#later' x='4'/><g id='later'><rect width='2' height='2' fill='red'/></g></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(2, root.Rectangles.Length);
        Assert.Equal(new SvgRectangle(4, 0, 2, 2, new SvgColor(255, 0, 0, 255), true, null), root.Rectangles[0]);
        Assert.Equal(new SvgRectangle(0, 0, 2, 2, new SvgColor(255, 0, 0, 255), true, null), root.Rectangles[1]);
        Assert.False(root.HasUnsupportedChild);
    }

    [Fact]
    public void Rejects_a_self_referential_group_use_as_cyclic()
    {
        SvgException rejected = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><g id='c'><use href='#c'/></g><use href='#c'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, rejected.ErrorCode);
        Assert.Contains("Cyclic group use references", rejected.Message);
    }

    [Fact]
    public void Rejects_mutually_referential_group_uses_as_cyclic()
    {
        SvgException rejected = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><g id='a'><use href='#b'/></g><g id='b'><use href='#a'/></g><use href='#a'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, rejected.ErrorCode);
        Assert.Contains("Cyclic group use references", rejected.Message);
    }

    [Fact]
    public void Rejects_group_use_with_group_opacity()
    {
        SvgException rejected = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><g id='o' opacity='0.5'><rect width='2' height='2' fill='red'/></g><use href='#o'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, rejected.ErrorCode);
        Assert.Contains("group opacity", rejected.Message);
    }

    [Fact]
    public void Expands_translated_ancestor_group_for_outside_use()
    {
        // Translated ancestors un-bake through the axis-aligned inverse, so the
        // group instance expands exactly like direct rendering (no misrender, no gate).
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><g transform='translate(5,0)'><g id='inner'><rect width='2' height='2' fill='red'/></g></g><use href='#inner'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(2, root.Geometries.Length);
        Assert.False(root.HasUnsupportedChild);
    }

    [Fact]
    public void Expands_transformed_group_for_outside_use()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><g id='a' transform='rotate(30)'><polygon points='0,0 4,0 0,4' fill='red'/></g><use href='#a' x='8'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(2, root.Geometries.Length);
        Assert.False(root.HasUnsupportedChild);
    }

    [Fact]
    public void Rejects_duplicate_leaf_and_group_ids()
    {
        SvgException rejected = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><rect id='x' width='2' height='2' fill='red'/></defs><g id='x'><rect width='1' height='1' fill='red'/></g></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.MalformedInput, rejected.ErrorCode);
    }

    [Fact]
    public void Does_not_paint_group_nested_definitions_without_use()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><g><defs><rect id='tile' width='2' height='2' fill='red'/></defs></g></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Empty(root.Geometries);
        Assert.False(root.HasUnsupportedChild);
    }

    [Fact]
    public void Retains_group_wrapped_definition_content_without_painting_it()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><g><defs><g><rect width='5' height='5' fill='red'/></g></defs></g></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Empty(root.Geometries);
        Assert.False(root.HasUnsupportedChild);
    }

    [Fact]
    public void Parses_group_wrapped_definition_paths_without_painting_them()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><g><defs><path d='M0,0 H5 V5 H0 Z' fill='red'/></defs></g></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Empty(root.Geometries);
        Assert.False(root.HasUnsupportedChild);
    }

    [Fact]
    public void Parses_percentage_root_dimensions()
    {
        SvgRoot root = SvgRootParser.Parse(System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='100%' height='50%'/>"), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(100, root.Width?.Value);
        Assert.True(root.Width?.IsPercentage);
        Assert.Equal(50, root.Height?.Value);
        Assert.True(root.Height?.IsPercentage);
        Assert.False(root.HasUnsupportedChild);
    }

    [Fact]
    public void Rejects_nonpositive_percentage_root_dimensions()
    {
        SvgException zero = Assert.Throws<SvgException>(() => SvgRootParser.Parse(System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='0%'/>"), SvgRenderLimits.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.MalformedInput, zero.ErrorCode);
    }

    [Fact]
    public void Prefers_inline_style_width_over_root_attribute()
    {
        SvgRoot root = SvgRootParser.Parse(System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='10' style='width: 20px'/>"), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(20, root.Width?.Value);
        Assert.False(root.Width?.IsPercentage);
        Assert.False(root.HasUnsupportedChild);
    }

    [Fact]
    public void Resolves_auto_inline_style_width_as_viewport_percentage()
    {
        using SvgRoot root = SvgRootParser.Parse(System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='12' style='width: auto'/>"), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(100, root.Width?.Value);
        Assert.True(root.Width?.IsPercentage);
        Assert.False(root.HasUnsupportedChild);
    }

    [Fact]
    public void Ignores_empty_foreign_namespace_metadata_element()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg' xmlns:sodipodi='http://sodipodi.sourceforge.net/DTD/sodipodi-0.dtd'><sodipodi:namedview id='namedview'/><rect width='5' height='5' fill='red'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Single(root.Geometries);
        Assert.False(root.HasUnsupportedChild);
    }

    [Fact]
    public void Flags_nonempty_foreign_namespace_wrapper()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg' xmlns:foo='urn:example'><foo:wrapper><rect width='5' height='5' fill='red'/></foo:wrapper></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Empty(root.Geometries);
        Assert.True(root.HasUnsupportedChild);
    }

    [Fact]
    public void Skips_editor_namedview_with_foreign_page()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg' xmlns:sodipodi='http://sodipodi.sourceforge.net/DTD/sodipodi-0.dtd' xmlns:inkscape='http://www.inkscape.org/namespaces/inkscape'><sodipodi:namedview pagecolor='#ffffff'><inkscape:page x='0' y='0'/></sodipodi:namedview><rect width='5' height='5' fill='red'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Single(root.Geometries);
        Assert.False(root.HasUnsupportedChild);
    }

    [Fact]
    public void Flags_namedview_hiding_svg_content()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg' xmlns:sodipodi='http://sodipodi.sourceforge.net/DTD/sodipodi-0.dtd'><sodipodi:namedview><rect width='5' height='5' fill='red'/></sodipodi:namedview></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Empty(root.Geometries);
        Assert.True(root.HasUnsupportedChild);
    }

    [Fact]
    public void Deep_namedview_exceeds_nesting_limit()
    {
        SvgRenderLimits limits = new(16_385, 1024, 16, 16, 1024, 1024, 1024, 1024, 3, 256);

        SvgException rejected = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg' xmlns:sodipodi='http://sodipodi.sourceforge.net/DTD/sodipodi-0.dtd'><sodipodi:namedview><sodipodi:namedview><sodipodi:namedview/></sodipodi:namedview></sodipodi:namedview></svg>"u8.ToArray(), limits, new RenderBudget(16_385), CancellationToken.None));

        Assert.Equal(SvgErrorCode.LimitExceeded, rejected.ErrorCode);
    }

    [Fact]
    public void Namedview_subtree_exhausts_node_budget()
    {
        SvgRenderLimits limits = new(16_385, 1024, 16, 16, 1024, 3, 1024, 1024, 256, 256);

        SvgException rejected = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg' xmlns:sodipodi='http://sodipodi.sourceforge.net/DTD/sodipodi-0.dtd'><sodipodi:namedview><sodipodi:guide/><sodipodi:guide/></sodipodi:namedview></svg>"u8.ToArray(), limits, new RenderBudget(16_385), CancellationToken.None));

        Assert.Equal(SvgErrorCode.LimitExceeded, rejected.ErrorCode);
    }
    [Fact]
    public void Group_wrapped_definitions_convert_without_painting_their_content()
    {
        byte[] svg = "<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><g><defs><g><rect width='5' height='5' fill='red'/></g></defs></g></svg>"u8.ToArray();

        byte[] empty = "<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'/>"u8.ToArray();
        Assert.Equal(SvgConverter.ConvertToPng(empty, SvgRenderOptions.Default, CancellationToken.None).ToArray(), SvgConverter.ConvertToPng(svg, SvgRenderOptions.Default, CancellationToken.None).ToArray());
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
    public void Switch_skips_language_tagged_child_for_fallback()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><switch><rect systemLanguage='en' x='1' y='2' width='3' height='4' fill='red'/><rect x='5' y='6' width='7' height='8' fill='blue'/></switch></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Single(root.Rectangles);
        Assert.Equal(new SvgRectangle(5, 6, 7, 8, new SvgColor(0, 0, 255, 255), true, null), root.Rectangles[0]);
        Assert.False(root.HasUnsupportedChild);
    }

    [Fact]
    public void Switch_with_only_tagged_children_renders_nothing()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><switch><rect systemLanguage='en' width='2' height='2' fill='red'/></switch></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Empty(root.Geometries);
        Assert.False(root.HasUnsupportedChild);
    }

    [Fact]
    public void Switch_fallback_renders_byte_identical_to_direct_content()
    {
        byte[] direct = "<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><rect x='1' y='1' width='2' height='2' fill='red'/></svg>"u8.ToArray();
        byte[] switched = "<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><switch><rect systemLanguage='en' x='9' y='9' width='2' height='2' fill='blue'/><rect x='1' y='1' width='2' height='2' fill='red'/></switch></svg>"u8.ToArray();

        ReadOnlyMemory<byte> directPng = SvgConverter.ConvertToPng(direct, SvgRenderOptions.Default, CancellationToken.None);
        ReadOnlyMemory<byte> switchedPng = SvgConverter.ConvertToPng(switched, SvgRenderOptions.Default, CancellationToken.None);

        Assert.True(directPng.Span.SequenceEqual(switchedPng.Span));
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

    [Theory]
    [InlineData("<defs><clipPath id='c'><rect x='1' y='1' width='3' height='3'/></clipPath></defs><rect width='5' height='5' fill='red' clip-path='url(#c)'/>", "<defs><clipPath id='c'><rect x='1' y='1' width='3' height='3'/></clipPath></defs><rect width='5' height='5' fill='red' clip-path='url(\"#c\")'/>")]
    [InlineData("<defs><mask id='m'><rect x='0' y='0' width='5' height='5' fill='#ffffff'/></mask></defs><rect width='5' height='5' fill='red' mask='url(#m)'/>", "<defs><mask id='m'><rect x='0' y='0' width='5' height='5' fill='#ffffff'/></mask></defs><rect width='5' height='5' fill='red' mask='url(\"#m\")'/>")]
    [InlineData("<defs><pattern id='p' width='2' height='2' patternUnits='userSpaceOnUse'><rect width='1' height='1' fill='red'/></pattern></defs><rect width='5' height='5' fill='url(#p)'/>", "<defs><pattern id='p' width='2' height='2' patternUnits='userSpaceOnUse'><rect width='1' height='1' fill='red'/></pattern></defs><rect width='5' height='5' fill='url(\"#p\")'/>")]
    [InlineData("<defs><linearGradient id='g'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><rect width='5' height='5' fill='url(#g)'/>", "<defs><linearGradient id='g'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><rect width='5' height='5' fill='url(\"#g\")'/>")]
    public void Quoted_local_references_render_byte_identical_to_unquoted_ones(string plainBody, string quotedBody)
    {
        byte[] plain = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'>" + plainBody + "</svg>");
        byte[] quoted = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'>" + quotedBody + "</svg>");

        ReadOnlyMemory<byte> plainPng = SvgConverter.ConvertToPng(plain, SvgRenderOptions.Default, CancellationToken.None);
        ReadOnlyMemory<byte> quotedPng = SvgConverter.ConvertToPng(quoted, SvgRenderOptions.Default, CancellationToken.None);

        Assert.True(plainPng.Span.SequenceEqual(quotedPng.Span));
    }

    [Fact]
    public void Renders_palette_images_byte_identical_to_direct_fills()
    {
        byte[] image = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='2' height='2'><image href='data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAMAAABFaP0WAAAAA1BMVEX/AAAZ4gk3AAAAC0lEQVR4nGNgAAEAAAYAAf6MZ8gAAAAASUVORK5CYII=' x='0' y='0' width='2' height='2'/></svg>");
        byte[] direct = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='2' height='2'><rect width='2' height='2' fill='red'/></svg>");

        ReadOnlyMemory<byte> imagePng = SvgConverter.ConvertToPng(image, SvgRenderOptions.Default, CancellationToken.None);
        ReadOnlyMemory<byte> directPng = SvgConverter.ConvertToPng(direct, SvgRenderOptions.Default, CancellationToken.None);

        Assert.True(imagePng.Span.SequenceEqual(directPng.Span));
    }

    [Fact]
    public void Square_dash_with_path_length_scales_runs()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><line x1='0' y1='4' x2='16' y2='4' stroke='red' stroke-width='2' stroke-dasharray='8 8' pathLength='32' stroke-linecap='square'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(2, root.Geometries.Length);
        Assert.Equal(new SvgRectangle(-1, 3, 6, 2, new SvgColor(255, 0, 0, 255), true, null), root.Geometries[0].Rectangle);
        Assert.Equal(new SvgRectangle(7, 3, 6, 2, new SvgColor(255, 0, 0, 255), true, null), root.Geometries[1].Rectangle);
    }

    [Fact]
    public void Retains_translucent_square_capped_dashed_lines()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><line x1='0' y1='4' x2='16' y2='4' stroke='red' opacity='0.5' stroke-width='2' stroke-dasharray='8 4' stroke-linecap='square'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.False(root.HasUnsupportedChild);
        Assert.NotEmpty(root.Geometries);
        Assert.Contains(root.Commands, command => command.Kind == SvgDrawCommandKind.BeginGroup && command.SourceOpacity.Value == 0.5f);
    }

    [Fact]
    public void Retains_translucent_square_capped_gradient_dashes()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='s' gradientUnits='userSpaceOnUse' x1='0' y1='4' x2='16' y2='4'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><line x1='0' y1='4' x2='16' y2='4' stroke='url(#s)' opacity='0.5' stroke-width='2' stroke-dasharray='8 4' stroke-linecap='square'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.False(root.HasUnsupportedChild);
        Assert.NotEmpty(root.Geometries);
        Assert.Contains(root.Commands, command => command.Kind == SvgDrawCommandKind.BeginGroup && command.SourceOpacity.Value == 0.5f);
    }

    [Fact]
    public void Renders_translucent_stop_square_capped_gradient_dashes()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='s' gradientUnits='userSpaceOnUse' x1='0' y1='4' x2='16' y2='4'><stop offset='0' stop-color='#ff0000' stop-opacity='0.5'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><line x1='0' y1='4' x2='16' y2='4' stroke='url(#s)' stroke-width='2' stroke-dasharray='8 4' stroke-linecap='square'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.False(root.HasUnsupportedChild);
        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(128, geometry.GradientPolygon.Gradient.Start.Alpha);
        AssertStrokeContourBounds(geometry, [new(-1, 3, 10, 2, default, true, null), new(11, 3, 6, 2, default, true, null)]);
    }
    [Fact]
    public void Retains_translucent_square_capped_pattern_dashes()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><pattern id='p' width='8' height='8' patternUnits='userSpaceOnUse'><rect width='8' height='8' fill='red'/></pattern></defs><line x1='0' y1='4' x2='16' y2='4' stroke='url(#p)' opacity='0.5' stroke-width='2' stroke-dasharray='8 4' stroke-linecap='square'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.False(root.HasUnsupportedChild);
        Assert.NotEmpty(root.Geometries);
        Assert.Contains(root.Commands, command => command.Kind == SvgDrawCommandKind.BeginGroup && command.SourceOpacity.Value == 0.5f);
    }

    [Fact]
    public void Retains_translucent_square_capped_radial_dashes()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><radialGradient id='s' gradientUnits='userSpaceOnUse' cx='8' cy='4' r='8'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><line x1='0' y1='4' x2='16' y2='4' stroke='url(#s)' opacity='0.5' stroke-width='2' stroke-dasharray='8 4' stroke-linecap='square'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.False(root.HasUnsupportedChild);
        Assert.NotEmpty(root.Geometries);
        Assert.Contains(root.Commands, command => command.Kind == SvgDrawCommandKind.BeginGroup && command.SourceOpacity.Value == 0.5f);
    }
    [Fact]
    public void Renders_zero_length_square_capped_dashes()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><line x1='0' y1='4' x2='16' y2='4' stroke='red' stroke-width='2' stroke-dasharray='0 4' stroke-linecap='square'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.False(root.HasUnsupportedChild);
        SvgGeometry geometry = Assert.Single(root.Geometries);
        AssertStrokeContourBounds(geometry, [new(-1, 3, 2, 2, default, true, null), new(3, 3, 2, 2, default, true, null), new(7, 3, 2, 2, default, true, null), new(11, 3, 2, 2, default, true, null)]);
    }

    [Fact]
    public void Admits_zero_length_butt_dashes_without_runs()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><line x1='0' y1='4' x2='16' y2='4' stroke='red' stroke-width='2' stroke-dasharray='0 4'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Empty(root.Geometries);
    }

    [Fact]
    public void Square_dash_offset_keeps_butt_phase()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><line x1='0' y1='4' x2='16' y2='4' stroke='red' stroke-width='2' stroke-dasharray='4 4' stroke-dashoffset='2' stroke-linecap='square'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(3, root.Geometries.Length);
        Assert.Equal(new SvgRectangle(-1, 3, 4, 2, new SvgColor(255, 0, 0, 255), true, null), root.Geometries[0].Rectangle);
        Assert.Equal(new SvgRectangle(5, 3, 6, 2, new SvgColor(255, 0, 0, 255), true, null), root.Geometries[1].Rectangle);
        Assert.Equal(new SvgRectangle(13, 3, 4, 2, new SvgColor(255, 0, 0, 255), true, null), root.Geometries[2].Rectangle);
    }

    [Fact]
    public void Square_capped_vertical_line_dash_extends_runs()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><line x1='4' y1='0' x2='4' y2='16' stroke='red' stroke-width='2' stroke-dasharray='4 4' stroke-linecap='square'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(2, root.Geometries.Length);
        Assert.Equal(new SvgRectangle(3, -1, 2, 6, new SvgColor(255, 0, 0, 255), true, null), root.Geometries[0].Rectangle);
        Assert.Equal(new SvgRectangle(3, 7, 2, 6, new SvgColor(255, 0, 0, 255), true, null), root.Geometries[1].Rectangle);
    }

    [Fact]
    public void Square_capped_line_dash_extends_each_run()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><line x1='0' y1='4' x2='16' y2='4' stroke='red' stroke-width='2' stroke-dasharray='4 4' stroke-linecap='square'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(2, root.Geometries.Length);
        Assert.Equal(new SvgRectangle(-1, 3, 6, 2, new SvgColor(255, 0, 0, 255), true, null), root.Geometries[0].Rectangle);
        Assert.Equal(new SvgRectangle(7, 3, 6, 2, new SvgColor(255, 0, 0, 255), true, null), root.Geometries[1].Rectangle);
    }

    [Fact]
    public void Square_capped_gradient_line_dash_paints_runs()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='s' gradientUnits='userSpaceOnUse' x1='0' y1='4' x2='16' y2='4'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><line x1='0' y1='4' x2='16' y2='4' stroke='url(#s)' stroke-width='2' stroke-dasharray='4 4' stroke-linecap='square'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.GradientPolygon, geometry.Kind);
        AssertStrokeContourBounds(geometry, [new(-1, 3, 6, 2, default, true, null), new(7, 3, 6, 2, default, true, null)]);
    }

    [Fact]
    public void Square_capped_pattern_line_dash_paints_runs()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><pattern id='p' width='8' height='8' patternUnits='userSpaceOnUse'><rect width='8' height='8' fill='red'/></pattern></defs><line x1='0' y1='4' x2='16' y2='4' stroke='url(#p)' stroke-width='2' stroke-dasharray='4 4' stroke-linecap='square'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.PatternPolygon, geometry.Kind);
        AssertStrokeContourBounds(geometry, [new(-1, 3, 6, 2, default, true, null), new(7, 3, 6, 2, default, true, null)]);
    }

    [Fact]
    public void Square_capped_radial_line_dash_paints_runs()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><radialGradient id='s' gradientUnits='userSpaceOnUse' cx='8' cy='4' r='8'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><line x1='0' y1='4' x2='16' y2='4' stroke='url(#s)' stroke-width='2' stroke-dasharray='4 4' stroke-linecap='square'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.RadialPolygon, geometry.Kind);
        AssertStrokeContourBounds(geometry, [new(-1, 3, 6, 2, default, true, null), new(7, 3, 6, 2, default, true, null)]);
    }

    [Fact]
    public void Fills_circles_with_linear_gradients()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='g' gradientUnits='userSpaceOnUse' x1='0' y1='0' x2='8' y2='0'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><circle cx='4' cy='4' r='4' fill='url(#g)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.GradientPolygon, geometry.Kind);
    }

    [Fact]
    public void Fills_circles_with_radial_gradients()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><radialGradient id='s' gradientUnits='userSpaceOnUse' cx='4' cy='4' r='4'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><circle cx='4' cy='4' r='4' fill='url(#s)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.RadialPolygon, geometry.Kind);
    }

    [Fact]
    public void Fills_circles_with_patterns()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><pattern id='p' width='8' height='8' patternUnits='userSpaceOnUse'><rect width='8' height='8' fill='red'/></pattern></defs><circle cx='4' cy='4' r='4' fill='url(#p)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.PatternPolygon, geometry.Kind);
    }

    [Fact]
    public void Renders_pattern_fills_on_circles_in_scaled_viewports()
    {
        byte[] original = "<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><defs><pattern id='p' width='8' height='8' patternUnits='userSpaceOnUse'><rect width='8' height='8' fill='red'/></pattern></defs><svg x='0' y='0' width='16' height='16' viewBox='0 0 8 8'><circle cx='4' cy='4' r='4' fill='url(#p)'/></svg></svg>"u8.ToArray();
        using SvgRoot root = SvgRootParser.Parse(original, SvgRenderLimits.Default, CancellationToken.None);
        Assert.Equal(SvgGeometryKind.PatternPolygon, Assert.Single(root.Geometries).Kind);
        byte[] solid = System.Text.Encoding.UTF8.GetBytes(System.Text.Encoding.UTF8.GetString(original).Replace("fill='url(#p)'", "fill='red'", StringComparison.Ordinal));
        Assert.Equal(SvgConverter.ConvertToPng(solid, SvgRenderOptions.Default, CancellationToken.None).ToArray(),
            SvgConverter.ConvertToPng(original, SvgRenderOptions.Default, CancellationToken.None).ToArray());
    }

    [Fact]
    public void Renders_reflected_pattern_fills_on_circles()
    {
        byte[] original = "<svg xmlns='http://www.w3.org/2000/svg'><defs><pattern id='p' width='8' height='8' patternUnits='userSpaceOnUse'><rect width='8' height='8' fill='red'/></pattern></defs><circle cx='4' cy='4' r='4' fill='url(#p)' transform='scale(-1 1)'/></svg>"u8.ToArray();
        using SvgRoot root = SvgRootParser.Parse(original, SvgRenderLimits.Default, CancellationToken.None);
        Assert.Equal(SvgGeometryKind.PatternPolygon, Assert.Single(root.Geometries).Kind);
        byte[] solid = System.Text.Encoding.UTF8.GetBytes(System.Text.Encoding.UTF8.GetString(original).Replace("fill='url(#p)'", "fill='red'", StringComparison.Ordinal));
        Assert.Equal(SvgConverter.ConvertToPng(solid, SvgRenderOptions.Default, CancellationToken.None).ToArray(),
            SvgConverter.ConvertToPng(original, SvgRenderOptions.Default, CancellationToken.None).ToArray());
    }

    [Fact]
    public void Lowers_gradient_strokes_on_circles()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns=\"http://www.w3.org/2000/svg\"><defs><linearGradient id=\"g\"><stop offset=\"0\" stop-color=\"#ff0000\"/><stop offset=\"1\" stop-color=\"#0000ff\"/></linearGradient></defs><circle cx=\"4\" cy=\"4\" r=\"4\" fill=\"red\" stroke=\"url(#g)\" stroke-width=\"2\"/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(2, root.Geometries.Length);
        Assert.Equal(SvgGeometryKind.PolygonFill, root.Geometries[0].Kind);
        Assert.Equal(SvgGeometryKind.GradientPolygon, root.Geometries[1].Kind);
        Assert.True(root.Geometries[1].GradientPolygon.HasFill);
    }

    [Fact]
    public void Lowers_radial_strokes_on_circles()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns=\"http://www.w3.org/2000/svg\"><defs><radialGradient id=\"g\" cx=\"0.5\" cy=\"0.5\" r=\"0.5\"><stop offset=\"0\" stop-color=\"#ff0000\"/><stop offset=\"1\" stop-color=\"#0000ff\"/></radialGradient></defs><circle cx=\"4\" cy=\"4\" r=\"4\" fill=\"red\" stroke=\"url(#g)\" stroke-width=\"2\"/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(2, root.Geometries.Length);
        Assert.Equal(SvgGeometryKind.PolygonFill, root.Geometries[0].Kind);
        Assert.Equal(SvgGeometryKind.RadialPolygon, root.Geometries[1].Kind);
        Assert.True(root.Geometries[1].RadialPolygon.HasFill);
    }

    [Fact]
    public void Lowers_pattern_strokes_on_circles()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns=\"http://www.w3.org/2000/svg\"><defs><pattern id=\"p\" width=\"8\" height=\"8\" patternUnits=\"userSpaceOnUse\"><rect width=\"8\" height=\"8\" fill=\"red\"/></pattern></defs><circle cx=\"4\" cy=\"4\" r=\"4\" fill=\"red\" stroke=\"url(#p)\" stroke-width=\"2\"/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(2, root.Geometries.Length);
        Assert.Equal(SvgGeometryKind.PolygonFill, root.Geometries[0].Kind);
        Assert.Equal(SvgGeometryKind.PatternPolygon, root.Geometries[1].Kind);
        Assert.True(root.Geometries[1].PatternPolygon.HasFill);
    }

    [Fact]
    public void Lowers_dashed_circle_strokes_as_rings()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns=\"http://www.w3.org/2000/svg\"><circle cx=\"8\" cy=\"8\" r=\"6\" fill=\"red\" stroke=\"blue\" stroke-width=\"2\" stroke-dasharray=\"4 2\"/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.True(root.Geometries.Length >= 1);
        Assert.All(root.Geometries, geometry => Assert.Equal(SvgGeometryKind.PolygonFill, geometry.Kind));
    }

    [Fact]
    public void Dashed_circle_offsets_shift_run_bands()
    {
        byte[] plain = System.Text.Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"16\"><circle cx=\"8\" cy=\"8\" r=\"6\" fill=\"red\" stroke=\"blue\" stroke-width=\"2\" stroke-dasharray=\"4 2\"/></svg>");
        byte[] shifted = System.Text.Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"16\"><circle cx=\"8\" cy=\"8\" r=\"6\" fill=\"red\" stroke=\"blue\" stroke-width=\"2\" stroke-dasharray=\"4 2\" stroke-dashoffset=\"2\"/></svg>");

        ReadOnlyMemory<byte> plainPng = SvgConverter.ConvertToPng(plain, SvgRenderOptions.Default, CancellationToken.None);
        ReadOnlyMemory<byte> shiftedPng = SvgConverter.ConvertToPng(shifted, SvgRenderOptions.Default, CancellationToken.None);

        Assert.False(shiftedPng.Span.SequenceEqual(plainPng.Span));
    }

    [Fact]
    public void PathLength_scales_dashed_circle_runs()
    {
        byte[] plain = System.Text.Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"16\"><circle cx=\"8\" cy=\"8\" r=\"6\" fill=\"red\" stroke=\"blue\" stroke-width=\"2\" stroke-dasharray=\"4 2\"/></svg>");
        byte[] scaled = System.Text.Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"16\"><circle cx=\"8\" cy=\"8\" r=\"6\" fill=\"red\" stroke=\"blue\" stroke-width=\"2\" stroke-dasharray=\"4 2\" pathLength=\"10\"/></svg>");

        ReadOnlyMemory<byte> plainPng = SvgConverter.ConvertToPng(plain, SvgRenderOptions.Default, CancellationToken.None);
        ReadOnlyMemory<byte> scaledPng = SvgConverter.ConvertToPng(scaled, SvgRenderOptions.Default, CancellationToken.None);

        Assert.False(scaledPng.Span.SequenceEqual(plainPng.Span));
    }

    [Fact]
    public void OutputScale_dashed_circle_matches_enlarged_direct_rendering()
    {
        byte[] small = System.Text.Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"64\" height=\"64\"><circle cx=\"32\" cy=\"32\" r=\"16\" fill=\"none\" stroke=\"red\" stroke-width=\"8\" stroke-dasharray=\"8 8\"/></svg>");
        byte[] large = System.Text.Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"128\" height=\"128\"><circle cx=\"64\" cy=\"64\" r=\"32\" fill=\"none\" stroke=\"red\" stroke-width=\"16\" stroke-dasharray=\"16 16\"/></svg>");
        SvgRenderOptions scaled = new(null, 2, SvgColor.Transparent, SvgRenderLimits.Default);
        SvgRenderOptions direct = new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default);
        ReadOnlyMemory<byte> scaledPng = SvgConverter.ConvertToPng(small, scaled, CancellationToken.None);
        ReadOnlyMemory<byte> directPng = SvgConverter.ConvertToPng(large, direct, CancellationToken.None);

        Assert.True(scaledPng.Span.SequenceEqual(directPng.Span));
    }

    [Fact]
    public void OutputScale_dashed_circle_offset_matches_enlarged_direct_rendering()
    {
        byte[] small = System.Text.Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"64\" height=\"64\"><circle cx=\"32\" cy=\"32\" r=\"16\" fill=\"none\" stroke=\"red\" stroke-width=\"8\" stroke-dasharray=\"8 8\" stroke-dashoffset=\"4\"/></svg>");
        byte[] large = System.Text.Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"128\" height=\"128\"><circle cx=\"64\" cy=\"64\" r=\"32\" fill=\"none\" stroke=\"red\" stroke-width=\"16\" stroke-dasharray=\"16 16\" stroke-dashoffset=\"8\"/></svg>");
        SvgRenderOptions scaled = new(null, 2, SvgColor.Transparent, SvgRenderLimits.Default);
        SvgRenderOptions direct = new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default);
        ReadOnlyMemory<byte> scaledPng = SvgConverter.ConvertToPng(small, scaled, CancellationToken.None);
        ReadOnlyMemory<byte> directPng = SvgConverter.ConvertToPng(large, direct, CancellationToken.None);

        Assert.True(scaledPng.Span.SequenceEqual(directPng.Span));
    }

    [Fact]
    public void OutputScale_dashed_ellipse_matches_enlarged_direct_rendering()
    {
        byte[] small = System.Text.Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"64\" height=\"64\"><ellipse cx=\"32\" cy=\"32\" rx=\"20\" ry=\"12\" fill=\"none\" stroke=\"red\" stroke-width=\"8\" stroke-dasharray=\"8 8\"/></svg>");
        byte[] large = System.Text.Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"128\" height=\"128\"><ellipse cx=\"64\" cy=\"64\" rx=\"40\" ry=\"24\" fill=\"none\" stroke=\"red\" stroke-width=\"16\" stroke-dasharray=\"16 16\"/></svg>");
        SvgRenderOptions scaled = new(null, 2, SvgColor.Transparent, SvgRenderLimits.Default);
        SvgRenderOptions direct = new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default);
        ReadOnlyMemory<byte> scaledPng = SvgConverter.ConvertToPng(small, scaled, CancellationToken.None);
        ReadOnlyMemory<byte> directPng = SvgConverter.ConvertToPng(large, direct, CancellationToken.None);

        Assert.True(scaledPng.Span.SequenceEqual(directPng.Span));
    }

    [Fact]
    public void OutputScale_dashed_ellipse_offset_matches_enlarged_direct_rendering()
    {
        byte[] small = System.Text.Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"64\" height=\"64\"><ellipse cx=\"32\" cy=\"32\" rx=\"20\" ry=\"12\" fill=\"none\" stroke=\"red\" stroke-width=\"8\" stroke-dasharray=\"8 8\" stroke-dashoffset=\"4\"/></svg>");
        byte[] large = System.Text.Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"128\" height=\"128\"><ellipse cx=\"64\" cy=\"64\" rx=\"40\" ry=\"24\" fill=\"none\" stroke=\"red\" stroke-width=\"16\" stroke-dasharray=\"16 16\" stroke-dashoffset=\"8\"/></svg>");
        SvgRenderOptions scaled = new(null, 2, SvgColor.Transparent, SvgRenderLimits.Default);
        SvgRenderOptions direct = new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default);
        ReadOnlyMemory<byte> scaledPng = SvgConverter.ConvertToPng(small, scaled, CancellationToken.None);
        ReadOnlyMemory<byte> directPng = SvgConverter.ConvertToPng(large, direct, CancellationToken.None);

        Assert.True(scaledPng.Span.SequenceEqual(directPng.Span));
    }

    [Fact]
    public void Renders_round_caps_on_dashed_circle_strokes()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns=\"http://www.w3.org/2000/svg\"><circle cx=\"8\" cy=\"8\" r=\"6\" fill=\"red\" stroke=\"blue\" stroke-width=\"2\" stroke-dasharray=\"4 2\" stroke-linecap=\"round\"/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.False(root.HasUnsupportedChild);
        Assert.NotEmpty(root.Geometries);
    }

    [Fact]
    public void Renders_square_caps_on_dashed_circle_strokes()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns=\"http://www.w3.org/2000/svg\"><circle cx=\"8\" cy=\"8\" r=\"6\" fill=\"red\" stroke=\"blue\" stroke-width=\"2\" stroke-dasharray=\"4 2\" stroke-linecap=\"square\"/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.False(root.HasUnsupportedChild);
        Assert.NotEmpty(root.Geometries);
    }

    [Fact]
    public void Lowers_dashed_ellipse_strokes_as_rings()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns=\"http://www.w3.org/2000/svg\"><ellipse cx=\"8\" cy=\"8\" rx=\"6\" ry=\"4\" fill=\"red\" stroke=\"blue\" stroke-width=\"2\" stroke-dasharray=\"4 2\"/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.True(root.Geometries.Length >= 1);
        Assert.All(root.Geometries, geometry => Assert.Equal(SvgGeometryKind.PolygonFill, geometry.Kind));
    }

    [Fact]
    public void Dashed_ellipse_offsets_shift_run_bands()
    {
        byte[] plain = System.Text.Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"16\"><ellipse cx=\"8\" cy=\"8\" rx=\"6\" ry=\"4\" fill=\"red\" stroke=\"blue\" stroke-width=\"2\" stroke-dasharray=\"4 2\"/></svg>");
        byte[] shifted = System.Text.Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"16\"><ellipse cx=\"8\" cy=\"8\" rx=\"6\" ry=\"4\" fill=\"red\" stroke=\"blue\" stroke-width=\"2\" stroke-dasharray=\"4 2\" stroke-dashoffset=\"2\"/></svg>");

        ReadOnlyMemory<byte> plainPng = SvgConverter.ConvertToPng(plain, SvgRenderOptions.Default, CancellationToken.None);
        ReadOnlyMemory<byte> shiftedPng = SvgConverter.ConvertToPng(shifted, SvgRenderOptions.Default, CancellationToken.None);

        Assert.False(shiftedPng.Span.SequenceEqual(plainPng.Span));
    }

    [Fact]
    public void PathLength_scales_dashed_ellipse_runs()
    {
        byte[] plain = System.Text.Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"16\"><ellipse cx=\"8\" cy=\"8\" rx=\"6\" ry=\"4\" fill=\"red\" stroke=\"blue\" stroke-width=\"2\" stroke-dasharray=\"4 2\"/></svg>");
        byte[] scaled = System.Text.Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"16\"><ellipse cx=\"8\" cy=\"8\" rx=\"6\" ry=\"4\" fill=\"red\" stroke=\"blue\" stroke-width=\"2\" stroke-dasharray=\"4 2\" pathLength=\"10\"/></svg>");

        ReadOnlyMemory<byte> plainPng = SvgConverter.ConvertToPng(plain, SvgRenderOptions.Default, CancellationToken.None);
        ReadOnlyMemory<byte> scaledPng = SvgConverter.ConvertToPng(scaled, SvgRenderOptions.Default, CancellationToken.None);

        Assert.False(scaledPng.Span.SequenceEqual(plainPng.Span));
    }

    [Fact]
    public void Renders_round_caps_on_dashed_ellipse_strokes()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns=\"http://www.w3.org/2000/svg\"><ellipse cx=\"8\" cy=\"8\" rx=\"6\" ry=\"4\" fill=\"red\" stroke=\"blue\" stroke-width=\"2\" stroke-dasharray=\"4 2\" stroke-linecap=\"round\"/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.False(root.HasUnsupportedChild);
        Assert.NotEmpty(root.Geometries);
    }

    [Fact]
    public void Renders_square_caps_on_dashed_ellipse_strokes()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns=\"http://www.w3.org/2000/svg\"><ellipse cx=\"8\" cy=\"8\" rx=\"6\" ry=\"4\" fill=\"red\" stroke=\"blue\" stroke-width=\"2\" stroke-dasharray=\"4 2\" stroke-linecap=\"square\"/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.False(root.HasUnsupportedChild);
        Assert.NotEmpty(root.Geometries);
    }

    [Fact]
    public void Retains_translucent_paint_strokes_on_circles()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns=\"http://www.w3.org/2000/svg\"><defs><linearGradient id=\"g\"><stop offset=\"0\" stop-color=\"#ff0000\"/><stop offset=\"1\" stop-color=\"#0000ff\"/></linearGradient></defs><circle cx=\"4\" cy=\"4\" r=\"4\" fill=\"red\" stroke=\"url(#g)\" stroke-width=\"2\" opacity=\"0.5\"/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.False(root.HasUnsupportedChild);
        Assert.NotEmpty(root.Geometries);
        Assert.Contains(root.Commands, command => command.Kind == SvgDrawCommandKind.BeginGroup && command.SourceOpacity.Value == 0.5f);
    }

    [Fact]
    public void Rejects_undefined_paint_strokes_on_circles()
    {
        SvgException exception = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns=\"http://www.w3.org/2000/svg\"><circle cx=\"4\" cy=\"4\" r=\"4\" fill=\"red\" stroke=\"url(#missing)\" stroke-width=\"2\"/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, exception.ErrorCode);
    }

    [Fact]
    public void Lowers_linear_strokes_on_ellipses()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns=\"http://www.w3.org/2000/svg\"><defs><linearGradient id=\"g\"><stop offset=\"0\" stop-color=\"#ff0000\"/><stop offset=\"1\" stop-color=\"#0000ff\"/></linearGradient></defs><ellipse cx=\"32\" cy=\"32\" rx=\"20\" ry=\"12\" fill=\"red\" stroke=\"url(#g)\" stroke-width=\"8\"/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(2, root.Geometries.Length);
        Assert.Equal(SvgGeometryKind.PolygonFill, root.Geometries[0].Kind);
        Assert.Equal(SvgGeometryKind.GradientPolygon, root.Geometries[1].Kind);
        Assert.True(root.Geometries[1].GradientPolygon.HasFill);
    }

    [Fact]
    public void Lowers_radial_strokes_on_ellipses()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns=\"http://www.w3.org/2000/svg\"><defs><radialGradient id=\"g\" cx=\"0.5\" cy=\"0.5\" r=\"0.5\"><stop offset=\"0\" stop-color=\"#ff0000\"/><stop offset=\"1\" stop-color=\"#0000ff\"/></radialGradient></defs><ellipse cx=\"32\" cy=\"32\" rx=\"20\" ry=\"12\" fill=\"red\" stroke=\"url(#g)\" stroke-width=\"8\"/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(2, root.Geometries.Length);
        Assert.Equal(SvgGeometryKind.PolygonFill, root.Geometries[0].Kind);
        Assert.Equal(SvgGeometryKind.RadialPolygon, root.Geometries[1].Kind);
        Assert.True(root.Geometries[1].RadialPolygon.HasFill);
    }

    [Fact]
    public void Renders_pattern_ellipse_strokes_through_the_distinct_shader_material_route()
    {
        byte[] painted = System.Text.Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"64\" height=\"64\"><defs><pattern id=\"p\" width=\"8\" height=\"8\" patternUnits=\"userSpaceOnUse\"><rect width=\"8\" height=\"8\" fill=\"#0000ff\"/></pattern></defs><ellipse cx=\"32\" cy=\"32\" rx=\"20\" ry=\"12\" fill=\"red\" stroke=\"url(#p)\" stroke-width=\"8\"/></svg>");
        byte[] solid = System.Text.Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"64\" height=\"64\"><ellipse cx=\"32\" cy=\"32\" rx=\"20\" ry=\"12\" fill=\"red\" stroke=\"blue\" stroke-width=\"8\"/></svg>");

        ReadOnlyMemory<byte> paintedPng = SvgConverter.ConvertToPng(painted, SvgRenderOptions.Default, CancellationToken.None);
        ReadOnlyMemory<byte> solidPng = SvgConverter.ConvertToPng(solid, SvgRenderOptions.Default, CancellationToken.None);

        // Independent recipe-3 captures differ on 27 decoded pixels, including
        // 18 alpha lanes. Uniform tile samples do not erase shader operation identity.
        Assert.False(paintedPng.Span.SequenceEqual(solidPng.Span));
    }

    [Fact]
    public void Retains_translucent_paint_strokes_on_ellipses()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns=\"http://www.w3.org/2000/svg\"><defs><linearGradient id=\"g\"><stop offset=\"0\" stop-color=\"#ff0000\"/><stop offset=\"1\" stop-color=\"#0000ff\"/></linearGradient></defs><ellipse cx=\"32\" cy=\"32\" rx=\"20\" ry=\"12\" fill=\"red\" stroke=\"url(#g)\" stroke-width=\"8\" opacity=\"0.5\"/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.False(root.HasUnsupportedChild);
        Assert.NotEmpty(root.Geometries);
        Assert.Contains(root.Commands, command => command.Kind == SvgDrawCommandKind.BeginGroup && command.SourceOpacity.Value == 0.5f);
    }

    [Fact]
    public void Rejects_undefined_paint_strokes_on_ellipses()
    {
        SvgException exception = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns=\"http://www.w3.org/2000/svg\"><ellipse cx=\"32\" cy=\"32\" rx=\"20\" ry=\"12\" fill=\"red\" stroke=\"url(#missing)\" stroke-width=\"2\"/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, exception.ErrorCode);
    }

    [Theory]
    [InlineData("linearGradient")]
    [InlineData("radialGradient")]
    public void Shader_filled_circles_keep_their_independent_solid_stroke(string paint)
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='12' height='12'><defs><" + paint + " id='g'><stop stop-color='red'/><stop offset='1' stop-color='blue'/></" + paint + "></defs><circle cx='6' cy='6' r='4' fill='url(#g)' stroke='#00ff00' stroke-width='2'/></svg>");
        RenderBudget budget = new(1_000_000);
        using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None))
        using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, SvgRenderOptions.Default, budget, CancellationToken.None))
        {
            Assert.Equal(new byte[] { 0, 255, 0, 255 }, surface.Pixels.AsSpan((6 * 12 + 2) * 4, 4).ToArray());
            ReadOnlySpan<byte> center = surface.Pixels.AsSpan((6 * 12 + 6) * 4, 4);
            Assert.InRange(center[0], (byte)1, (byte)254);
            Assert.Equal(0, center[1]);
            Assert.InRange(center[2], (byte)1, (byte)254);
            Assert.Equal(255, center[3]);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Rejects_undefined_gradients_on_circles()
    {
        SvgException exception = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><circle cx='4' cy='4' r='4' fill='url(#missing)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, exception.ErrorCode);
    }

    [Fact]
    public void Fills_rounded_rectangles_with_linear_gradients()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='g' gradientUnits='userSpaceOnUse' x1='0' y1='0' x2='8' y2='0'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><rect x='0' y='0' width='8' height='8' rx='2' fill='url(#g)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.GradientPolygon, geometry.Kind);
    }

    [Fact]
    public void Fills_rounded_rectangles_with_radial_gradients()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><radialGradient id='s' gradientUnits='userSpaceOnUse' cx='4' cy='4' r='4'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><rect x='0' y='0' width='8' height='8' rx='2' fill='url(#s)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.RadialPolygon, geometry.Kind);
    }

    [Fact]
    public void Fills_rounded_rectangles_with_bounding_box_gradients()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='g' x1='0' y1='0' x2='1' y2='0'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><rect x='0' y='0' width='8' height='8' rx='2' fill='url(#g)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.GradientPolygon, geometry.Kind);
        Assert.Equal(new SvgRectangle(0, 0, 8, 8, default, true, null), geometry.GradientPolygon.Box);
    }

    [Fact]
    public void Fills_rounded_rectangles_with_patterns()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><pattern id='p' width='8' height='8' patternUnits='userSpaceOnUse'><rect width='8' height='8' fill='red'/></pattern></defs><rect x='0' y='0' width='8' height='8' rx='2' fill='url(#p)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.PatternPolygon, geometry.Kind);
    }

    [Fact]
    public void Renders_pattern_fills_on_rounded_rectangles_in_scaled_viewports()
    {
        byte[] original = "<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><defs><pattern id='p' width='8' height='8' patternUnits='userSpaceOnUse'><rect width='8' height='8' fill='red'/></pattern></defs><svg x='0' y='0' width='16' height='16' viewBox='0 0 8 8'><rect x='0' y='0' width='8' height='8' rx='2' fill='url(#p)'/></svg></svg>"u8.ToArray();
        using SvgRoot root = SvgRootParser.Parse(original, SvgRenderLimits.Default, CancellationToken.None);
        Assert.Equal(SvgGeometryKind.PatternPolygon, Assert.Single(root.Geometries).Kind);
        byte[] solid = System.Text.Encoding.UTF8.GetBytes(System.Text.Encoding.UTF8.GetString(original).Replace("fill='url(#p)'", "fill='red'", StringComparison.Ordinal));
        Assert.Equal(SvgConverter.ConvertToPng(solid, SvgRenderOptions.Default, CancellationToken.None).ToArray(),
            SvgConverter.ConvertToPng(original, SvgRenderOptions.Default, CancellationToken.None).ToArray());
    }

    [Fact]
    public void Renders_reflected_pattern_fills_on_rounded_rectangles()
    {
        byte[] original = "<svg xmlns='http://www.w3.org/2000/svg'><defs><pattern id='p' width='8' height='8' patternUnits='userSpaceOnUse'><rect width='8' height='8' fill='red'/></pattern></defs><rect x='0' y='0' width='8' height='8' rx='2' fill='url(#p)' transform='scale(-1 1)'/></svg>"u8.ToArray();
        using SvgRoot root = SvgRootParser.Parse(original, SvgRenderLimits.Default, CancellationToken.None);
        Assert.Equal(SvgGeometryKind.PatternPolygon, Assert.Single(root.Geometries).Kind);
        byte[] solid = System.Text.Encoding.UTF8.GetBytes(System.Text.Encoding.UTF8.GetString(original).Replace("fill='url(#p)'", "fill='red'", StringComparison.Ordinal));
        Assert.Equal(SvgConverter.ConvertToPng(solid, SvgRenderOptions.Default, CancellationToken.None).ToArray(),
            SvgConverter.ConvertToPng(original, SvgRenderOptions.Default, CancellationToken.None).ToArray());
    }

    [Fact]
    public void Lowers_gradient_strokes_on_rounded_rectangles()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns=\"http://www.w3.org/2000/svg\"><defs><linearGradient id=\"g\"><stop offset=\"0\" stop-color=\"#ff0000\"/><stop offset=\"1\" stop-color=\"#0000ff\"/></linearGradient></defs><rect x=\"0\" y=\"0\" width=\"8\" height=\"8\" rx=\"2\" fill=\"red\" stroke=\"url(#g)\" stroke-width=\"2\"/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(2, root.Geometries.Length);
        Assert.Equal(SvgGeometryKind.PolygonFill, root.Geometries[0].Kind);
        Assert.Equal(SvgGeometryKind.GradientPolygon, root.Geometries[1].Kind);
        Assert.True(root.Geometries[1].GradientPolygon.HasFill);
    }

    [Fact]
    public void Lowers_pattern_strokes_on_rounded_rectangles()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns=\"http://www.w3.org/2000/svg\"><defs><pattern id=\"p\" width=\"8\" height=\"8\" patternUnits=\"userSpaceOnUse\"><rect width=\"8\" height=\"8\" fill=\"red\"/></pattern></defs><rect x=\"0\" y=\"0\" width=\"8\" height=\"8\" rx=\"2\" fill=\"red\" stroke=\"url(#p)\" stroke-width=\"2\"/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(2, root.Geometries.Length);
        Assert.Equal(SvgGeometryKind.PolygonFill, root.Geometries[0].Kind);
        Assert.Equal(SvgGeometryKind.PatternPolygon, root.Geometries[1].Kind);
        Assert.True(root.Geometries[1].PatternPolygon.HasFill);
    }

    [Fact]
    public void Lowers_radial_strokes_on_rounded_rectangles()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns=\"http://www.w3.org/2000/svg\"><defs><radialGradient id=\"g\" cx=\"0.5\" cy=\"0.5\" r=\"0.5\"><stop offset=\"0\" stop-color=\"#ff0000\"/><stop offset=\"1\" stop-color=\"#0000ff\"/></radialGradient></defs><rect x=\"0\" y=\"0\" width=\"8\" height=\"8\" rx=\"2\" fill=\"red\" stroke=\"url(#g)\" stroke-width=\"2\"/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(2, root.Geometries.Length);
        Assert.Equal(SvgGeometryKind.PolygonFill, root.Geometries[0].Kind);
        Assert.Equal(SvgGeometryKind.RadialPolygon, root.Geometries[1].Kind);
        Assert.True(root.Geometries[1].RadialPolygon.HasFill);
    }

    [Fact]
    public void Retains_translucent_paint_strokes_on_rounded_rectangles()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns=\"http://www.w3.org/2000/svg\"><defs><linearGradient id=\"g\"><stop offset=\"0\" stop-color=\"#ff0000\"/><stop offset=\"1\" stop-color=\"#0000ff\"/></linearGradient></defs><rect x=\"0\" y=\"0\" width=\"8\" height=\"8\" rx=\"2\" fill=\"red\" stroke=\"url(#g)\" stroke-width=\"2\" opacity=\"0.5\"/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.False(root.HasUnsupportedChild);
        Assert.NotEmpty(root.Geometries);
        Assert.Contains(root.Commands, command => command.Kind == SvgDrawCommandKind.BeginGroup && command.SourceOpacity.Value == 0.5f);
    }

    [Fact]
    public void Rejects_undefined_paint_strokes_on_rounded_rectangles()
    {
        SvgException exception = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns=\"http://www.w3.org/2000/svg\"><rect x=\"0\" y=\"0\" width=\"8\" height=\"8\" rx=\"2\" fill=\"red\" stroke=\"url(#missing)\" stroke-width=\"2\"/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, exception.ErrorCode);
    }

    [Fact]
    public void Round_and_square_caps_render_identical_to_butt_on_solid_rectangles()
    {
        byte[] butt = System.Text.Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"8\" height=\"8\"><rect x=\"1\" y=\"1\" width=\"6\" height=\"6\" fill=\"red\" stroke=\"blue\" stroke-width=\"2\"/></svg>");
        byte[] round = System.Text.Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"8\" height=\"8\"><rect x=\"1\" y=\"1\" width=\"6\" height=\"6\" fill=\"red\" stroke=\"blue\" stroke-width=\"2\" stroke-linecap=\"round\"/></svg>");
        byte[] square = System.Text.Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"8\" height=\"8\"><rect x=\"1\" y=\"1\" width=\"6\" height=\"6\" fill=\"red\" stroke=\"blue\" stroke-width=\"2\" stroke-linecap=\"square\"/></svg>");

        ReadOnlyMemory<byte> buttPng = SvgConverter.ConvertToPng(butt, SvgRenderOptions.Default, CancellationToken.None);
        ReadOnlyMemory<byte> roundPng = SvgConverter.ConvertToPng(round, SvgRenderOptions.Default, CancellationToken.None);
        ReadOnlyMemory<byte> squarePng = SvgConverter.ConvertToPng(square, SvgRenderOptions.Default, CancellationToken.None);

        Assert.True(roundPng.Span.SequenceEqual(buttPng.Span));
        Assert.True(squarePng.Span.SequenceEqual(buttPng.Span));
    }

    [Fact]
    public void Round_caps_render_identical_to_butt_on_solid_rounded_rectangles()
    {
        byte[] butt = System.Text.Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"8\" height=\"8\"><rect x=\"0\" y=\"0\" width=\"8\" height=\"8\" rx=\"2\" fill=\"red\" stroke=\"blue\" stroke-width=\"2\"/></svg>");
        byte[] round = System.Text.Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"8\" height=\"8\"><rect x=\"0\" y=\"0\" width=\"8\" height=\"8\" rx=\"2\" fill=\"red\" stroke=\"blue\" stroke-width=\"2\" stroke-linecap=\"round\"/></svg>");

        ReadOnlyMemory<byte> buttPng = SvgConverter.ConvertToPng(butt, SvgRenderOptions.Default, CancellationToken.None);
        ReadOnlyMemory<byte> roundPng = SvgConverter.ConvertToPng(round, SvgRenderOptions.Default, CancellationToken.None);

        Assert.True(roundPng.Span.SequenceEqual(buttPng.Span));
    }

    [Fact]
    public void Renders_round_caps_on_dashed_rounded_rectangles()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns=\"http://www.w3.org/2000/svg\"><rect x=\"0\" y=\"0\" width=\"8\" height=\"8\" rx=\"2\" fill=\"red\" stroke=\"blue\" stroke-width=\"2\" stroke-dasharray=\"2 2\" stroke-linecap=\"round\"/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.False(root.HasUnsupportedChild);
        Assert.NotEmpty(root.Geometries);
    }

    [Fact]
    public void Strokes_solid_strokes_on_rounded_rectangles()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><rect x='0' y='0' width='8' height='8' rx='2' fill='red' stroke='blue' stroke-width='2'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(2, root.Geometries.Length);
        Assert.Equal(SvgGeometryKind.PolygonFill, root.Geometries[0].Kind);
        Assert.Equal(SvgGeometryKind.PolygonFill, root.Geometries[1].Kind);
        Assert.Equal(new SvgColor(255, 0, 0, 255), root.Geometries[0].Polygon.Color);
        Assert.Equal(new SvgColor(0, 0, 255, 255), root.Geometries[1].Polygon.Color);
    }

    [Fact]
    public void Round_joins_render_on_rounded_rectangles()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns=\"http://www.w3.org/2000/svg\"><rect x=\"0\" y=\"0\" width=\"8\" height=\"8\" rx=\"2\" fill=\"red\" stroke=\"blue\" stroke-width=\"2\" stroke-linejoin=\"round\"/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(2, root.Geometries.Length);
        Assert.Equal(SvgGeometryKind.PolygonFill, root.Geometries[0].Kind);
        Assert.Equal(SvgGeometryKind.PolygonFill, root.Geometries[1].Kind);
    }

    [Fact]
    public void Bevel_joins_render_on_rounded_rectangles()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns=\"http://www.w3.org/2000/svg\"><rect x=\"0\" y=\"0\" width=\"8\" height=\"8\" rx=\"2\" fill=\"red\" stroke=\"blue\" stroke-width=\"2\" stroke-linejoin=\"bevel\"/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(2, root.Geometries.Length);
        Assert.Equal(SvgGeometryKind.PolygonFill, root.Geometries[0].Kind);
        Assert.Equal(SvgGeometryKind.PolygonFill, root.Geometries[1].Kind);
    }

    [Fact]
    public void Smooth_rounded_rectangle_corners_preserve_the_same_pixels_for_bevel_and_miter_joins()
    {
        byte[] miter = System.Text.Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"8\" height=\"8\"><rect x=\"0\" y=\"0\" width=\"8\" height=\"8\" rx=\"2\" fill=\"red\" stroke=\"blue\" stroke-width=\"2\"/></svg>");
        byte[] bevel = System.Text.Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"8\" height=\"8\"><rect x=\"0\" y=\"0\" width=\"8\" height=\"8\" rx=\"2\" fill=\"red\" stroke=\"blue\" stroke-width=\"2\" stroke-linejoin=\"bevel\"/></svg>");

        ReadOnlyMemory<byte> miterPng = SvgConverter.ConvertToPng(miter, SvgRenderOptions.Default, CancellationToken.None);
        ReadOnlyMemory<byte> bevelPng = SvgConverter.ConvertToPng(bevel, SvgRenderOptions.Default, CancellationToken.None);

        Assert.True(bevelPng.Span.SequenceEqual(miterPng.Span));
    }

    [Fact]
    public void Renders_bevel_joins_on_sharp_rectangles()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns=\"http://www.w3.org/2000/svg\"><rect x=\"0\" y=\"0\" width=\"8\" height=\"8\" fill=\"red\" stroke=\"blue\" stroke-width=\"2\" stroke-linejoin=\"bevel\"/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.False(root.HasUnsupportedChild);
        Assert.NotEmpty(root.Geometries);
    }

    [Fact]
    public void Renders_round_joins_on_sharp_rectangles()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns=\"http://www.w3.org/2000/svg\"><rect x=\"0\" y=\"0\" width=\"8\" height=\"8\" fill=\"red\" stroke=\"blue\" stroke-width=\"2\" stroke-linejoin=\"round\"/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.False(root.HasUnsupportedChild);
        Assert.NotEmpty(root.Geometries);
    }

    [Fact]
    public void Admits_square_caps_on_solid_rounded_rectangles()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns=\"http://www.w3.org/2000/svg\"><rect x=\"0\" y=\"0\" width=\"8\" height=\"8\" rx=\"2\" fill=\"red\" stroke=\"blue\" stroke-width=\"2\" stroke-linecap=\"square\"/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(2, root.Geometries.Length);
        Assert.Equal(SvgGeometryKind.PolygonFill, root.Geometries[0].Kind);
        Assert.Equal(SvgGeometryKind.PolygonFill, root.Geometries[1].Kind);
    }

    [Fact]
    public void Renders_dashed_strokes_on_rounded_rectangles()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><rect x='0' y='0' width='8' height='8' rx='2' fill='red' stroke='blue' stroke-width='2' stroke-dasharray='2 2'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.False(root.HasUnsupportedChild);
        Assert.NotEmpty(root.Geometries);
    }

    [Fact]
    public void Retains_translucent_strokes_on_filled_rounded_rectangles()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><rect x='0' y='0' width='8' height='8' rx='2' fill='red' stroke='blue' stroke-width='2' opacity='0.5'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.False(root.HasUnsupportedChild);
        Assert.NotEmpty(root.Geometries);
        Assert.Contains(root.Commands, command => command.Kind == SvgDrawCommandKind.BeginGroup && command.SourceOpacity.Value == 0.5f);
    }

    [Fact]
    public void Rejects_undefined_gradients_on_rounded_rectangles()
    {
        SvgException exception = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><rect x='0' y='0' width='8' height='8' rx='2' fill='url(#missing)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, exception.ErrorCode);
    }

    [Fact]
    public void Accepts_noop_stroke_styles_on_stroked_leaves()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><rect x='1' y='1' width='4' height='4' fill='red' stroke='blue' stroke-width='2' stroke-linecap='butt' stroke-linejoin='miter' stroke-dasharray='none'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Single(root.Rectangles);
        Assert.Equal(new SvgColor(255, 0, 0, 255), root.Rectangles[0].Color);
        Assert.False(root.HasUnsupportedChild);
    }

    [Fact]
    public void Accepts_noop_stroke_styles_through_groups_and_stylesheets()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><style>g { stroke-linecap: butt; }</style><g stroke-linejoin='miter'><rect width='1' height='1' fill='red'/></g></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Single(root.Rectangles);
        Assert.False(root.HasUnsupportedChild);
    }

    [Theory]
    [InlineData("<rect width='1' height='1' fill='red' stroke='blue' stroke-width='2' stroke-linecap='round'/>")]
    [InlineData("<rect width='1' height='1' fill='red' stroke='blue' stroke-width='2' stroke-linejoin='round'/>")]
    [InlineData("<rect width='1' height='1' fill='red' stroke='blue' stroke-width='2' stroke-dasharray='2,2'/>")]

    public void Preserves_effectful_stroke_style_admission(string leaf)
    {
        byte[] svg = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg'>" + leaf + "</svg>");

        using SvgRoot admitted = SvgRootParser.Parse(svg, SvgRenderLimits.Default, CancellationToken.None);
        Assert.False(admitted.HasUnsupportedChild);
        Assert.NotEmpty(admitted.Geometries);
    }

    [Fact]
    public void Renders_stroke_styles_byte_identical_to_plain_strokes()
    {
        byte[] plain = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='6' height='6'><rect x='1' y='1' width='4' height='4' fill='red' stroke='blue' stroke-width='2'/></svg>");
        byte[] styled = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='6' height='6'><rect x='1' y='1' width='4' height='4' fill='red' stroke='blue' stroke-width='2' stroke-linecap='butt' stroke-linejoin='miter' stroke-dasharray='none'/></svg>");

        ReadOnlyMemory<byte> plainPng = SvgConverter.ConvertToPng(plain, SvgRenderOptions.Default, CancellationToken.None);
        ReadOnlyMemory<byte> styledPng = SvgConverter.ConvertToPng(styled, SvgRenderOptions.Default, CancellationToken.None);

        Assert.True(plainPng.Span.SequenceEqual(styledPng.Span));
    }

    [Fact]
    public void Accepts_noop_dashoffset_on_stroked_leaves()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><rect width='5' height='5' fill='red' stroke='blue' stroke-width='2' stroke-dasharray='none' stroke-dashoffset='5'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Single(root.Rectangles);
        Assert.False(root.HasUnsupportedChild);
    }

    [Fact]
    public void Accepts_clip_rule_on_clip_definitions()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><clipPath id='c' clip-rule='evenodd'><rect x='1' y='1' width='3' height='3'/></clipPath></defs><rect width='5' height='5' fill='red' clip-path='url(#c)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Single(root.Rectangles);
        Assert.False(root.HasUnsupportedChild);
    }

    [Theory]
    [InlineData("stroke-dashoffset", "foo")]
    [InlineData("stroke-dashoffset", "5%")]
    [InlineData("clip-rule", "bogus")]
    public void Rejects_effectful_dashoffset_and_clip_rule(string name, string value)
    {
        byte[] svg = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg'><defs><clipPath id='c'><rect x='1' y='1' width='3' height='3'/></clipPath></defs><rect width='5' height='5' fill='red' clip-path='url(#c)' " + name + "='" + value + "'/></svg>");

        SvgException exception = Assert.Throws<SvgException>(() => SvgRootParser.Parse(svg, SvgRenderLimits.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, exception.ErrorCode);
    }

    [Fact]
    public void Admits_default_rendering_intents_on_shapes_and_groups()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><g font-variant='normal'><rect width='1' height='1' fill='red' color-interpolation='sRGB'/></g></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Single(root.Rectangles);
        Assert.Equal(new SvgColor(255, 0, 0, 255), root.Rectangles[0].Color);
        Assert.False(root.HasUnsupportedChild);
    }

    [Theory]
    [InlineData("<text font-family='Example' font-variant='small-caps'>x</text>")]
    [InlineData("<rect width='1' height='1' fill='red' color-interpolation='linearRGB'/>")]
    [InlineData("<text font-family='Example' style='font-variant: small-caps'>x</text>")]
    [InlineData("<rect width='1' height='1' fill='red' style='color-interpolation: linearRGB'/>")]
    public void Rejects_non_default_rendering_intents(string leaf)
    {
        byte[] svg = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg'>" + leaf + "</svg>");

        SvgException exception = Assert.Throws<SvgException>(() => SvgRootParser.Parse(svg, SvgRenderLimits.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, exception.ErrorCode);
    }

    [Fact]
    public void Accepts_hidden_overflow_on_root_and_nested_viewports()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5' overflow='hidden' style='overflow: hidden'><svg x='1' y='1' width='3' height='3' overflow='hidden'><rect width='5' height='5' fill='red'/></svg></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Single(root.Rectangles);
        Assert.False(root.HasUnsupportedChild);
    }

    [Theory]
    [InlineData("<svg x='1' y='1' width='3' height='3' overflow='visible'><rect width='5' height='5' fill='red'/></svg>")]
    [InlineData("<svg width='5' height='5' overflow='scroll'><rect width='5' height='5' fill='red'/></svg>")]
    public void Rejects_non_hidden_viewport_overflow(string viewport)
    {
        byte[] svg = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'>" + viewport + "</svg>");

        SvgException exception = Assert.Throws<SvgException>(() => SvgRootParser.Parse(svg, SvgRenderLimits.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, exception.ErrorCode);
    }

    [Fact]
    public void Ignores_link_relationship_attributes()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><a href='https://example.com' target='_blank' rel='noopener' hreflang='en' download='logo' type='image/svg+xml' title='Example'><rect width='1' height='1' fill='red'/></a></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Single(root.Rectangles);
        Assert.Equal(new SvgColor(255, 0, 0, 255), root.Rectangles[0].Color);
        Assert.False(root.HasUnsupportedChild);
    }

    [Fact]
    public void Clips_embedded_images_to_their_clip_path_box()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><clipPath id='c'><rect x='2' y='2' width='4' height='4'/></clipPath></defs><image href='data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGP4z8DwHwAFAAH/iZk9HQAAAABJRU5ErkJggg==' x='0' y='0' width='8' height='8' clip-path='url(#c)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgImageGeometry placedImage = Assert.Single(root.Geometries).Image;
        Assert.Equal((0d, 0d, 8d, 8d), (placedImage.Box.X, placedImage.Box.Y, placedImage.Box.Width, placedImage.Box.Height));
        Assert.Equal(new SvgConstantShaderPaint(255, 0, 0, 255), placedImage.UniformSample);
        SvgPathFillAssertions.MatchesIntegerBands(root, [new(2, 2, 4, 4, new(255, 0, 0, 255), true, null)]);
    }

    [Fact]
    public void Clips_forward_clip_path_definitions_on_embedded_images()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><image href='data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGP4z8DwHwAFAAH/iZk9HQAAAABJRU5ErkJggg==' x='0' y='0' width='8' height='8' clip-path='url(#later)'/><defs><clipPath id='later'><rect x='2' y='2' width='4' height='4'/></clipPath></defs></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgImageGeometry placedImage = Assert.Single(root.Geometries).Image;
        Assert.Equal((0d, 0d, 8d, 8d), (placedImage.Box.X, placedImage.Box.Y, placedImage.Box.Width, placedImage.Box.Height));
        Assert.Equal(new SvgConstantShaderPaint(255, 0, 0, 255), placedImage.UniformSample);
        SvgPathFillAssertions.MatchesIntegerBands(root, [new(2, 2, 4, 4, new(255, 0, 0, 255), true, null)]);
    }

    [Fact]
    public void Clips_forward_bounding_box_clips_on_embedded_images()
    {
        AssertSolidRectanglePixels("<svg xmlns='http://www.w3.org/2000/svg'><image href='data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGP4z8DwHwAFAAH/iZk9HQAAAABJRU5ErkJggg==' x='0' y='0' width='8' height='8' clip-path='url(#later)'/><defs><clipPath id='later' clipPathUnits='objectBoundingBox'><rect x='0' y='0' width='1' height='1'/></clipPath></defs></svg>"u8.ToArray(), new(0, 0, 8, 8, new(255, 0, 0, 255), true, null), 300, 150);
    }

    [Fact]
    public void Applies_masks_to_embedded_images_without_changing_solid_content()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><mask id='m'><rect x='0' y='0' width='8' height='8' fill='#ffffff'/></mask></defs><image href='data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGP4z8DwHwAFAAH/iZk9HQAAAABJRU5ErkJggg==' x='0' y='0' width='8' height='8' mask='url(#m)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgImageGeometry placedImage = Assert.Single(root.Geometries).Image;
        Assert.Equal((0d, 0d, 8d, 8d), (placedImage.Box.X, placedImage.Box.Y, placedImage.Box.Width, placedImage.Box.Height));
        Assert.Equal(new SvgConstantShaderPaint(255, 0, 0, 255), placedImage.UniformSample);
        SvgPathFillAssertions.MatchesIntegerBands(root, [new(0, 0, 8, 8, new(255, 0, 0, 255), true, null)]);
    }

    [Fact]
    public void Applies_element_opacity_to_embedded_images()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><image href='data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGP4z8DwHwAFAAH/iZk9HQAAAABJRU5ErkJggg==' x='0' y='0' width='8' height='8' opacity='0.5'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(SvgGeometryKind.EmbeddedImage, Assert.Single(root.Geometries).Kind);
        using SvgConverter.RasterizedSurface output = SvgConverter.Rasterize(root,
            new(new SvgViewport(8, 8), 1, SvgColor.Transparent, SvgRenderLimits.Default), new RenderBudget(1_000_000), CancellationToken.None);
        for (int offset = 0; offset < output.Pixels.Length; offset += 4)
            Assert.Equal(new byte[] { 255, 0, 0, 128 }, output.Pixels.AsSpan(offset, 4).ToArray());
    }

    [Fact]
    public void Transforms_embedded_image_placement_through_the_admitted_subset()
    {
        using SvgRoot translated = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><image href='data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGP4z8DwHwAFAAH/iZk9HQAAAABJRU5ErkJggg==' x='0' y='0' width='4' height='4' transform='translate(4,0)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);
        using SvgRoot reflected = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><image href='data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGP4z8DwHwAFAAH/iZk9HQAAAABJRU5ErkJggg==' x='0' y='0' width='4' height='4' transform='translate(8,0) scale(-1,1)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgImageGeometry translatedImage = Assert.Single(translated.Geometries).Image;
        Assert.Equal((4d, 0d, 4d, 4d), (translatedImage.Box.X, translatedImage.Box.Y, translatedImage.Box.Width, translatedImage.Box.Height));
        Assert.Equal(new SvgConstantShaderPaint(255, 0, 0, 255), translatedImage.UniformSample);
        SvgImageGeometry reflectedImage = Assert.Single(reflected.Geometries).Image;
        Assert.Equal((4d, 0d, 4d, 4d), (reflectedImage.Box.X, reflectedImage.Box.Y, reflectedImage.Box.Width, reflectedImage.Box.Height));
        Assert.Equal(new SvgConstantShaderPaint(255, 0, 0, 255), reflectedImage.UniformSample);
        SvgPathFillAssertions.MatchesIntegerBands(translated, [new(4, 0, 4, 4, new(255, 0, 0, 255), true, null)]);
        SvgPathFillAssertions.MatchesIntegerBands(reflected, [new(4, 0, 4, 4, new(255, 0, 0, 255), true, null)]);
    }


    [Fact]
    public void Renders_explicit_none_stretch_despite_aspect_mismatch()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg' width='4' height='2'><image href='data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGP4z8DwHwAFAAH/iZk9HQAAAABJRU5ErkJggg==' x='0' y='0' width='4' height='2' preserveAspectRatio='none'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgImageGeometry placed = Assert.Single(root.Geometries).Image;
        Assert.Equal((0d, 0d, 4d, 2d), (placed.Box.X, placed.Box.Y, placed.Box.Width, placed.Box.Height));
        Assert.Equal(new SvgConstantShaderPaint(255, 0, 0, 255), placed.UniformSample);
        SvgPathFillAssertions.MatchesIntegerBands(root, [new(0, 0, 4, 2, new(255, 0, 0, 255), true, null)]);
        Assert.False(root.HasUnsupportedChild);
    }

    [Fact]
    public void Maps_default_meet_placement_for_mismatched_aspects()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg' width='4' height='2'><image href='data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGP4z8DwHwAFAAH/iZk9HQAAAABJRU5ErkJggg==' x='0' y='0' width='4' height='2'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgImageGeometry placedImage = Assert.Single(root.Geometries).Image;
        Assert.Equal((1d, 0d, 2d, 2d), (placedImage.Box.X, placedImage.Box.Y, placedImage.Box.Width, placedImage.Box.Height));
        Assert.Equal(new SvgConstantShaderPaint(255, 0, 0, 255), placedImage.UniformSample);
        SvgPathFillAssertions.MatchesIntegerBands(root, [new(1, 0, 2, 2, new(255, 0, 0, 255), true, null)]);
    }

    [Fact]
    public void Maps_explicit_meet_placement_for_mismatched_aspects()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg' width='4' height='2'><image href='data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGP4z8DwHwAFAAH/iZk9HQAAAABJRU5ErkJggg==' x='0' y='0' width='4' height='2' preserveAspectRatio='xMidYMid meet'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgImageGeometry placedImage = Assert.Single(root.Geometries).Image;
        Assert.Equal((1d, 0d, 2d, 2d), (placedImage.Box.X, placedImage.Box.Y, placedImage.Box.Width, placedImage.Box.Height));
        Assert.Equal(new SvgConstantShaderPaint(255, 0, 0, 255), placedImage.UniformSample);
        SvgPathFillAssertions.MatchesIntegerBands(root, [new(1, 0, 2, 2, new(255, 0, 0, 255), true, null)]);
    }

    [Theory]
    [InlineData("<image href='data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAIAAAABCAYAAAD0In+KAAAADklEQVR4nGP4z8DwH4QBEfcD/ePF9e8AAAAASUVORK5CYII=' x='0' y='0' width='4' height='4' preserveAspectRatio='xMinYMin meet'/>", 0, 0, 4, 2)]
    [InlineData("<image href='data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAIAAAABCAYAAAD0In+KAAAADklEQVR4nGP4z8DwH4QBEfcD/ePF9e8AAAAASUVORK5CYII=' x='0' y='0' width='4' height='4' preserveAspectRatio='xMaxYMax meet'/>", 0, 2, 4, 2)]
    [InlineData("<image href='data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAACCAYAAACZgbYnAAAAEElEQVR4nGP4z8DwnwFEAAAT9gP9uKbxygAAAABJRU5ErkJggg==' x='0' y='0' width='4' height='4'/>", 1, 0, 2, 4)]
    [InlineData("<image href='data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAMAAAABCAYAAAAb4BS0AAAADklEQVR4nGP4z8DwH4YBJuYF+2EsXwgAAAAASUVORK5CYII=' x='0' y='0' width='6' height='6'/>", 0, 2, 6, 2)]
    [InlineData("<image href='data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAIAAAABCAYAAAD0In+KAAAADklEQVR4nGP4z8DwH4QBEfcD/ePF9e8AAAAASUVORK5CYII=' x='0' y='0' width='4' height='4' preserveAspectRatio='xMidYMid slice'/>", 0, 0, 4, 4)]
    public void Maps_meet_and_slice_alignments_to_integral_placement(string image, double x, double y, double width, double height)
    {
        byte[] svg = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'>" + image + "</svg>");

        using SvgRoot root = SvgRootParser.Parse(svg, SvgRenderLimits.Default, CancellationToken.None);

        SvgImageGeometry placedImage = Assert.Single(root.Geometries).Image;
        Assert.Equal((x, y, width, height), (placedImage.Box.X, placedImage.Box.Y, placedImage.Box.Width, placedImage.Box.Height));
        Assert.Equal(new SvgConstantShaderPaint(255, 0, 0, 255), placedImage.UniformSample);
        SvgPathFillAssertions.MatchesIntegerBands(root, [new(x, y, width, height, new(255, 0, 0, 255), true, null)]);
    }

    [Theory]
    [InlineData("<image href='data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAMAAAABCAYAAAAb4BS0AAAADklEQVR4nGP4z8DwH4YBJuYF+2EsXwgAAAAASUVORK5CYII=' x='0' y='0' width='5' height='5'/>")]
    [InlineData("<image href='data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAIAAAABCAYAAAD0In+KAAAADklEQVR4nGP4z8DwH4QBEfcD/ePF9e8AAAAASUVORK5CYII=' x='0' y='0' width='3' height='3'/>")]
    public void Retains_fractional_aspect_mappings(string image)
    {
        byte[] svg = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'>" + image + "</svg>");

        using SvgRoot root = SvgRootParser.Parse(svg, SvgRenderLimits.Default, CancellationToken.None);
        SvgRectangle rectangle = Assert.Single(root.Geometries).Image.Box;
        Assert.Equal(SvgGeometryKind.EmbeddedImage, Assert.Single(root.Geometries).Kind);
        Assert.True(rectangle.Height > 0 && rectangle.Height < rectangle.Width);
        Assert.Equal(rectangle.Width, rectangle.Y * 2 + rectangle.Height, 12);
    }

    [Theory]
    [InlineData("style='preserveAspectRatio: meet'")]
    [InlineData("preserveAspectRatio='bogus'")]
    [InlineData("preserveAspectRatio='defer xMidYMid meet'")]
    public void Rejects_invalid_image_aspect_values(string aspect)
    {
        byte[] svg = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><image href='data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAIAAAABCAYAAAD0In+KAAAADklEQVR4nGP4z8DwH4QBEfcD/ePF9e8AAAAASUVORK5CYII=' x='0' y='0' width='4' height='4' " + aspect + "/></svg>");

        SvgException exception = Assert.Throws<SvgException>(() => SvgRootParser.Parse(svg, SvgRenderLimits.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, exception.ErrorCode);
    }

    [Fact]
    public void Prefers_presentation_attributes_over_inline_aspect()
    {
        byte[] attribute = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><image href='data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAIAAAABCAYAAAD0In+KAAAADklEQVR4nGP4z8DwH4QBEfcD/ePF9e8AAAAASUVORK5CYII=' x='0' y='0' width='4' height='4' preserveAspectRatio='none' style='preserveAspectRatio: xMinYMin meet'/></svg>");
        byte[] baseline = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><image href='data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAIAAAABCAYAAAD0In+KAAAADklEQVR4nGP4z8DwH4QBEfcD/ePF9e8AAAAASUVORK5CYII=' x='0' y='0' width='4' height='4' preserveAspectRatio='none'/></svg>");

        ReadOnlyMemory<byte> attributePng = SvgConverter.ConvertToPng(attribute, SvgRenderOptions.Default, CancellationToken.None);
        ReadOnlyMemory<byte> baselinePng = SvgConverter.ConvertToPng(baseline, SvgRenderOptions.Default, CancellationToken.None);

        Assert.True(attributePng.Span.SequenceEqual(baselinePng.Span));
    }

    [Fact]
    public void Ignores_inline_aspect_without_attributes()
    {
        byte[] inline = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><image href='data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAIAAAABCAYAAAD0In+KAAAADklEQVR4nGP4z8DwH4QBEfcD/ePF9e8AAAAASUVORK5CYII=' x='0' y='0' width='4' height='4' style='preserveAspectRatio: xMinYMin meet'/></svg>");
        byte[] baseline = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><image href='data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAIAAAABCAYAAAD0In+KAAAADklEQVR4nGP4z8DwH4QBEfcD/ePF9e8AAAAASUVORK5CYII=' x='0' y='0' width='4' height='4'/></svg>");

        ReadOnlyMemory<byte> inlinePng = SvgConverter.ConvertToPng(inline, SvgRenderOptions.Default, CancellationToken.None);
        ReadOnlyMemory<byte> baselinePng = SvgConverter.ConvertToPng(baseline, SvgRenderOptions.Default, CancellationToken.None);

        Assert.True(inlinePng.Span.SequenceEqual(baselinePng.Span));
    }

    [Fact]
    public void Rejects_link_nested_definitions()
    {
        SvgException exception = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><a><defs><rect id='tile' width='2' height='2' fill='red'/></defs></a><use href='#tile'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, exception.ErrorCode);
    }

    [Fact]
    public void Ignores_accessibility_and_data_attributes_on_leaves()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg' role='img' aria-label='Mark'><rect width='1' height='1' fill='red' data-x='1' tabindex='0'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Single(root.Rectangles);
        Assert.Equal(new SvgColor(255, 0, 0, 255), root.Rectangles[0].Color);
        Assert.False(root.HasUnsupportedChild);
    }

    [Fact]
    public void Ignores_editor_namespace_attributes()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg' xmlns:inkscape='http://www.inkscape.org/namespaces/inkscape'><g inkscape:label='Layer'><rect width='1' height='1' fill='red'/></g></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Single(root.Rectangles);
        Assert.Equal(new SvgColor(255, 0, 0, 255), root.Rectangles[0].Color);
        Assert.False(root.HasUnsupportedChild);
    }

    [Fact]
    public void Ignores_legacy_root_version_tags()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg' version='1.1' baseProfile='tiny' width='12.5' height='7'><rect width='1' height='1' fill='red' focusable='false'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(12.5, root.Width?.Value);
        Assert.Equal(7, root.Height?.Value);
        Assert.Single(root.Rectangles);
        Assert.False(root.HasUnsupportedChild);
    }

    [Fact]
    public void Ignores_pointer_events_and_cursor()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><style>rect { pointer-events: none; }</style><rect width='1' height='1' fill='red' style='cursor: pointer' pointer-events='all'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Single(root.Rectangles);
        Assert.Equal(new SvgColor(255, 0, 0, 255), root.Rectangles[0].Color);
        Assert.False(root.HasUnsupportedChild);
    }

    [Fact]
    public void Rejects_event_handler_attributes()
    {
        SvgException exception = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><rect width='1' height='1' fill='red' onclick='x'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None));

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
    public void Expands_transformed_group_nested_definitions_without_ancestor_transforms()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><g transform='translate(4,0)'><defs><rect id='r' x='0' y='0' width='4' height='8' fill='red'/></defs></g><use href='#r' x='0' y='0'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(new SvgRectangle(0, 0, 4, 8, new SvgColor(255, 0, 0, 255), true, null), Assert.Single(root.Rectangles));
    }

    [Fact]
    public void Applies_element_local_transforms_to_nested_definitions()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><rect id='r' x='0' y='0' width='2' height='8' fill='red' transform='translate(2,0)'/></defs><use href='#r' x='0' y='0'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(new SvgRectangle(2, 0, 2, 8, new SvgColor(255, 0, 0, 255), true, null), Assert.Single(root.Rectangles));
    }

    [Fact]
    public void Lowers_pattern_tile_content_without_ancestor_transforms()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><g transform='translate(4,0)'><defs><pattern id='p' width='4' height='4' patternUnits='userSpaceOnUse'><rect x='0' y='0' width='2' height='2' fill='red'/></pattern></defs></g><rect x='0' y='0' width='8' height='8' fill='url(#p)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.PatternFill, geometry.Kind);
        Assert.Equal(new SvgRectangle(0, 0, 2, 2, new SvgColor(255, 0, 0, 255), true, null), NativePatternRectangle(Assert.IsType<SvgVectorContent>(geometry.Pattern.VectorContent)));
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
        SvgAuthoredPath source = geometry.AuthoredPath ?? throw new InvalidOperationException("Missing native line centerline.");
        Assert.Equal(new SvgDevicePoint(1, 3), source.SceneMap.Apply(source.Commands[0].First.X, source.Commands[0].First.Y));
        Assert.Equal(new SvgDevicePoint(3, 3), source.SceneMap.Apply(source.Commands[1].First.X, source.Commands[1].First.Y));
        Assert.Equal(2, source.StrokeStyle?.Width);
        SvgRecordedPaintAssertions.ElementOpacity(root, new(128, 0.5f));
    }

    [Fact]
    public void Expands_axis_aligned_line_symbols_through_typed_use_geometry()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><symbol id='line'><line x2='2' stroke='red' stroke-width='2'/></symbol></defs><use href='#line' x='1' y='1'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        SvgAuthoredPath source = geometry.AuthoredPath ?? throw new InvalidOperationException("Missing native line centerline.");
        Assert.Equal(new SvgDevicePoint(1, 1), source.SceneMap.Apply(source.Commands[0].First.X, source.Commands[0].First.Y));
        Assert.Equal(new SvgDevicePoint(3, 1), source.SceneMap.Apply(source.Commands[1].First.X, source.Commands[1].First.Y));
        Assert.Equal(2, source.StrokeStyle?.Width);
    }

    [Fact]
    public void Applies_integer_transforms_to_axis_aligned_line_use_instances()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><line id='line' x1='1' y1='1' x2='2' y2='1' stroke='red' stroke-width='2'/></defs><use href='#line' x='3' y='2' transform='scale(2) translate(1, 0)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        SvgAuthoredPath source = geometry.AuthoredPath ?? throw new InvalidOperationException("Missing native line centerline.");
        Assert.Equal(new SvgDevicePoint(10, 6), source.SceneMap.Apply(source.Commands[0].First.X, source.Commands[0].First.Y));
        Assert.Equal(new SvgDevicePoint(12, 6), source.SceneMap.Apply(source.Commands[1].First.X, source.Commands[1].First.Y));
        Assert.Equal(2, source.StrokeStyle?.Width);
        Assert.Equal(2, source.StrokeStyle?.WidthMap.A);
        Assert.Equal(2, source.StrokeStyle?.WidthMap.D);
    }

    [Fact]
    public void Fractional_line_use_offsets_match_direct_native_placement()
    {
        byte[] instance = "<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><line id='line' x2='2' y1='3' y2='3' stroke='red' stroke-width='2'/></defs><use href='#line' x='0.25'/></svg>"u8.ToArray();
        byte[] direct = "<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><line x2='2' y1='3' y2='3' stroke='red' stroke-width='2' transform='translate(.25)'/></svg>"u8.ToArray();
        Assert.Equal(SvgConverter.ConvertToPng(direct, SvgRenderOptions.Default, CancellationToken.None).ToArray(), SvgConverter.ConvertToPng(instance, SvgRenderOptions.Default, CancellationToken.None).ToArray());
    }

    [Fact]
    public void Fill_inherit_uses_the_enclosing_group_paint()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><g fill='red'><rect width='1' height='1' fill='inherit'/></g></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(new SvgColor(255, 0, 0, 255), root.Rectangles[0].Color);
    }

    [Fact]
    public void Fill_initial_ignores_the_enclosing_group_paint()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><g fill='red'><rect width='1' height='1' fill='initial'/></g></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(new SvgColor(0, 0, 0, 255), root.Rectangles[0].Color);
    }

    [Fact]
    public void Fill_opacity_unset_inherits_the_group_value()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><g fill-opacity='0.5'><rect width='1' height='1' fill='red' fill-opacity='unset'/></g></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(new SvgColor(255, 0, 0, 128), root.Rectangles[0].Color);
    }

    [Fact]
    public void Stroke_initial_means_no_stroke()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><rect width='4' height='4' fill='red' stroke='blue' style='stroke: initial'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Single(root.Rectangles);
        Assert.Equal(new SvgColor(255, 0, 0, 255), root.Rectangles[0].Color);
        Assert.Null(root.Rectangles[0].Stroke);
    }

    [Fact]
    public void Display_initial_shows_hidden_group_content()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><g display='none'><rect width='1' height='1' fill='red' display='initial'/></g></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Single(root.Rectangles);
        Assert.False(root.HasUnsupportedChild);
    }

    [Fact]
    public void Inherited_fill_through_stylesheet_rules()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><style>#r { fill: inherit; }</style><g fill='red'><rect id='r' width='1' height='1'/></g></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(new SvgColor(255, 0, 0, 255), root.Rectangles[0].Color);
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

        Assert.Equal((byte)255, root.Rectangles[0].Color.Alpha);
        SvgRecordedPaintAssertions.ElementOpacity(root, new(128, 0.5f));
        Assert.True(root.Rectangles[0].HasFill);
        Assert.False(root.Rectangles[1].HasFill);
    }

    [Fact]
    public void Applies_stylesheet_opacity_and_display_to_rectangle_use_instances()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><style>#half { opacity: 0.5; } .hidden { display: none; }</style><defs><rect id='tile' width='1' height='1' fill='red'/></defs><use id='half' href='#tile'/><use class='hidden' href='#tile'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal((byte)255, root.Rectangles[0].Color.Alpha);
        SvgRecordedPaintAssertions.ElementOpacity(root, new(128, 0.5f));
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
    public void Authored_rectangle_fill_wins_over_use_fill()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><rect id='tile' width='1' height='1' fill='blue'/></defs><use href='#tile' fill='red'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);
        Assert.Equal(new SvgColor(0, 0, 255, 255), Assert.Single(root.Rectangles).Color);
    }

    [Fact]
    public void Use_fill_does_not_replace_an_authored_line_stroke()
    {
        byte[] input = "<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><line id='line' x1='2' y1='4' x2='6' y2='4' stroke='blue' stroke-width='2'/></defs><use href='#line' fill='red'/></svg>"u8.ToArray();
        byte[] direct = "<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><line x1='2' y1='4' x2='6' y2='4' stroke='blue' stroke-width='2'/></svg>"u8.ToArray();
        Assert.Equal(SvgConverter.ConvertToPng(direct, SvgRenderOptions.Default, CancellationToken.None).ToArray(), SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None).ToArray());
    }

    [Fact]
    public void Lowers_gradient_fills_on_use_instances()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='g' x1='0' y1='0' x2='1' y2='0'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient><rect id='r' x='0' y='0' width='4' height='8'/></defs><use href='#r' x='4' y='0' fill='url(#g)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.GradientFill, geometry.Kind);
        Assert.Equal(new SvgRectangle(4, 0, 4, 8, default, true, null), geometry.Gradient.Fill);
        AssertEqualGradient(new SvgLinearGradient(new SvgColor(255, 0, 0, 255), new SvgColor(0, 0, 255, 255), 0, 0, 1, 0, 0, 1, false, false, SvgGradientSpread.Pad, new SvgGradientStop[] { new(0, new SvgColor(255, 0, 0, 255)), new(1, new SvgColor(0, 0, 255, 255)) }, 1, 1, false), geometry.Gradient.Gradient);
    }

    [Fact]
    public void Lowers_user_space_gradient_fills_on_use_instances_with_placement_offsets()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='g' gradientUnits='userSpaceOnUse' x1='0' y1='0' x2='8' y2='0'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient><rect id='r' x='0' y='0' width='4' height='8'/></defs><use href='#r' x='4' y='0' fill='url(#g)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.GradientFill, geometry.Kind);
        Assert.Equal(new SvgRectangle(4, 0, 4, 8, default, true, null), geometry.Gradient.Fill);
        Assert.Equal(4, geometry.Gradient.Gradient.X1);
        Assert.Equal(12, geometry.Gradient.Gradient.X2);
    }

    [Fact]
    public void Lowers_pattern_fills_on_use_instances()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><pattern id='p' width='4' height='4' patternUnits='userSpaceOnUse'><rect width='2' height='2' fill='red'/></pattern><rect id='r' x='0' y='0' width='4' height='8'/></defs><use href='#r' x='4' y='0' fill='url(#p)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.PatternFill, geometry.Kind);
        Assert.Equal(new SvgRectangle(4, 0, 4, 8, default, true, null), geometry.Pattern.Fill);
        Assert.Equal(new SvgRectangle(0, 0, 2, 2, new SvgColor(255, 0, 0, 255), true, null), NativePatternRectangle(Assert.IsType<SvgVectorContent>(geometry.Pattern.VectorContent)));
    }

    [Fact]
    public void Lowers_gradient_fills_on_forward_use_instances()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><use href='#r' x='4' y='0' fill='url(#g)'/><defs><linearGradient id='g' x1='0' y1='0' x2='1' y2='0'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient><rect id='r' x='0' y='0' width='4' height='8'/></defs></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.GradientFill, geometry.Kind);
        Assert.Equal(new SvgRectangle(4, 0, 4, 8, default, true, null), geometry.Gradient.Fill);
    }

    [Fact]
    public void Lowers_gradient_fills_on_symbol_instances()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='g' x1='0' y1='0' x2='1' y2='0'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient><symbol id='s' viewBox='0 0 4 4'><rect x='0' y='0' width='4' height='4'/></symbol></defs><use href='#s' x='0' y='0' width='8' height='8' fill='url(#g)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.GradientFill, geometry.Kind);
        Assert.Equal(new SvgRectangle(0, 0, 8, 8, default, true, null), geometry.Gradient.Fill);
        SvgDrawCommand clip = Assert.Single(root.Commands, command => command.Kind == SvgDrawCommandKind.BeginGroup);
        Assert.Equal(SvgLayerRegionKind.AffineViewport, clip.RegionKind);
        Assert.Equal(new SvgClipBox(0, 0, 8, 8), clip.MaskRegion);
        Assert.True(clip.MaskContentIndex >= 0);
        Assert.Equal(1, clip.SourceOpacity.Value);
    }

    [Fact]
    public void Renders_pattern_fills_on_reflected_use_instances()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><pattern id='p' width='4' height='4' patternUnits='userSpaceOnUse'><rect width='2' height='2' fill='red'/></pattern><rect id='r' x='0' y='0' width='4' height='8'/></defs><use href='#r' x='0' y='0' transform='translate(8,0) scale(-1,1)' fill='url(#p)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.False(root.HasUnsupportedChild);
        Assert.NotEmpty(root.Geometries);
    }

    [Fact]
    public void Rejects_unknown_paint_server_fills_on_use_instances()
    {
        SvgException exception = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><rect id='r' x='0' y='0' width='4' height='8'/></defs><use href='#r' fill='url(#missing)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None));

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
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><style>/* palette */ rect/* type */ { /* property */ fill: /* value */red; }</style><rect width='1'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(new SvgColor(255, 0, 0, 255), root.Rectangles[0].Color);
    }

    [Fact]
    public void Cascades_rect_id_and_class_stylesheet_rules_by_specificity_and_source_order()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><style>rect { fill: blue; fill-opacity: 0.5; } .accent { fill: red; } .accent { fill: teal; } #selected { fill: lime; } #selected { opacity: 0.5; }</style><rect width='1'/><rect width='1' class='accent'/><rect width='1' id='selected' class='accent'/><rect width='1' class='accent' style='fill: yellow'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Collection(root.Rectangles,
            rectangle => Assert.Equal(new SvgColor(0, 0, 255, 128), rectangle.Color),
            rectangle => Assert.Equal(new SvgColor(0, 128, 128, 128), rectangle.Color),
            rectangle => Assert.Equal(new SvgColor(0, 255, 0, 128), rectangle.Color),
            rectangle => Assert.Equal(new SvgColor(255, 255, 0, 128), rectangle.Color));
        SvgRecordedPaintAssertions.ElementOpacity(root, new(128, 0.5f));
    }
    [Fact]
    public void Later_stylesheets_win_at_equal_specificity()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><style>rect { fill: red; }</style><style>rect { fill: blue; }</style><rect width='1' height='1'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(new SvgRectangle(0, 0, 1, 1, new SvgColor(0, 0, 255, 255), true, null), Assert.Single(root.Rectangles));
    }

    [Fact]
    public void Stylesheet_specificity_beats_later_source_order()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><style>#r { fill: red; }</style><style>rect { fill: blue; }</style><rect id='r' width='1' height='1'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(new SvgRectangle(0, 0, 1, 1, new SvgColor(255, 0, 0, 255), true, null), Assert.Single(root.Rectangles));
    }

    [Fact]
    public void Applies_stylesheets_after_styleable_content()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><rect width='1' height='1'/><style>rect { fill: red; }</style></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);
        Assert.Equal(new SvgRectangle(0, 0, 1, 1, new SvgColor(255, 0, 0, 255), true, null), Assert.Single(root.Rectangles));
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
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><style>#path { fill: red; } #polygon { fill: blue; }</style><path id='path' d='M 0 0 H 1 V 1 H 0 Z'/><polygon id='polygon' points='1,0 2,0 2,1 1,1'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgPathFillAssertions.MatchesIntegerBands(root, [
            new(0, 0, 1, 1, new(255, 0, 0, 255), true, null),
            new(1, 0, 1, 1, new(0, 0, 255, 255), true, null),
        ]);
    }

    [Fact]
    public void Applies_class_stylesheet_rules_to_supported_paths_and_point_lists()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><style>.path { fill: red; } .polygon { fill: blue; } .polyline { fill: lime; }</style><path class='path' d='M 0 0 H 1 V 1 H 0 Z'/><polygon class='polygon' points='1,0 2,0 2,1 1,1'/><polyline class='polyline' points='2,0 3,0 3,1 2,1'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgPathFillAssertions.MatchesIntegerBands(root, [
            new(0, 0, 1, 1, new(255, 0, 0, 255), true, null),
            new(1, 0, 1, 1, new(0, 0, 255, 255), true, null),
            new(2, 0, 1, 1, new(0, 255, 0, 255), true, null),
        ]);
    }

    [Fact]
    public void Applies_stylesheet_opacity_to_supported_paths()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><style>#path { fill: red; opacity: 0.5; }</style><path id='path' d='M 0 0 H 1 V 1 H 0 Z'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(new SvgColor(255, 0, 0, 255), Assert.Single(root.Geometries).Polygon.Color);
        SvgRecordedPaintAssertions.ElementOpacity(root, new(128, 0.5f));
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
    public void Admits_general_fractional_rectangle_coordinates()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><rect x='0.1' width='1' height='1' fill='red'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal((double)0.1f, root.Rectangles[0].X);
    }

    [Fact]
    public void Admits_fractional_translate_offsets()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><rect x='1' y='2' width='2' height='2' fill='red' transform='translate(0.5, -0.25)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(1.5, root.Rectangles[0].X);
        Assert.Equal(1.75, root.Rectangles[0].Y);
    }

    [Fact]
    public void Zero_translate_counts_as_identity_for_rectangle_strokes()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><rect x='2' y='2' width='8' height='8' fill='red' stroke='black' stroke-width='2' transform='translate(0, 0)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.NotNull(root.Rectangles[0].Stroke);
    }

    [Fact]
    public void Rejects_percentage_translate_coordinates()
    {
        SvgException exception = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><rect width='1' height='1' transform='translate(1%)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, exception.ErrorCode);
    }

    [Fact]
    public void Rejects_zero_scale_values()
    {
        SvgException exception = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><rect width='1' height='1' transform='scale(0)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None));

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
    public void Rejects_undefined_group_clip_references()
    {
        SvgException exception = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><g clip-path='url(#clip)'><rect width='1' height='1'/></g></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, exception.ErrorCode);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("45")]
    public void Admits_plain_rotated_rectangles(string angle)
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg'><rect width='1' height='1' fill='#ff0000' transform='rotate(" + angle + ")'/></svg>");
        using SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, CancellationToken.None);
        Assert.False(root.HasUnsupportedChild);
        Assert.Equal(SvgGeometryKind.PolygonFill, Assert.Single(root.Geometries).Kind);
        RenderBudget budget = new(1_000_000);
        using SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, new(new SvgViewport(8, 8), 1, SvgColor.Transparent, SvgRenderLimits.Default), budget, CancellationToken.None);
        Assert.Equal(255, surface.Pixels[0]);
        Assert.True(surface.Pixels[3] > 0);
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
    public void Admits_odd_width_rectangle_strokes_on_the_analytic_subset()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><rect width='4' height='4' fill='none' stroke='red'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);
        using SvgRoot translated = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><rect width='4' height='4' fill='blue' stroke='red' stroke-width='1' transform='translate(1)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgStroke stroke = Assert.NotNull(root.Rectangles[0].Stroke);
        Assert.Equal(1, stroke.Width);
        Assert.Equal(1, translated.Rectangles[0].X);
        Assert.Equal(new SvgStroke(new SvgColor(255, 0, 0, 255), 1), translated.Rectangles[0].Stroke);
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
    public void Lowers_translucent_lines_to_bands()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><line x1='1' y1='3' x2='5' y2='3' stroke='red' stroke-width='2' opacity='0.5'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(new SvgRectangle(1, 2, 4, 2, new SvgColor(255, 0, 0, 255), true, null), Assert.Single(root.Rectangles));
        SvgRecordedPaintAssertions.ElementOpacity(root, new(128, 0.5f));
    }

    [Fact]
    public void Lowers_translucent_stroke_colors_on_lines()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><line x1='1' y1='3' x2='5' y2='3' stroke='#ff000080' stroke-width='2'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(new SvgRectangle(1, 2, 4, 2, new SvgColor(255, 0, 0, 128), true, null), Assert.Single(root.Rectangles));
    }

    [Fact]
    public void Lowers_userspace_gradient_strokes_on_lines()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='g' gradientUnits='userSpaceOnUse' x1='0' y1='0' x2='8' y2='0'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><line x1='0' y1='4' x2='8' y2='4' stroke='url(#g)' stroke-width='2'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.GradientPolygon, geometry.Kind);
        AssertStrokeContourBounds(geometry, [new(0, 3, 8, 2, default, true, null)]);
        AssertEqualGradient(new SvgLinearGradient(new SvgColor(255, 0, 0, 255), new SvgColor(0, 0, 255, 255), 0, 0, 8, 0, 0, 1, false, true, SvgGradientSpread.Pad, new SvgGradientStop[] { new(0, new SvgColor(255, 0, 0, 255)), new(1, new SvgColor(0, 0, 255, 255)) }, 1, 1, false), geometry.GradientPolygon.Gradient);
    }

    [Theory]
    [InlineData("<defs><linearGradient id='g'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><line x1='0' y1='4' x2='8' y2='4' stroke='url(#g)' stroke-width='2'/>")]
    [InlineData("<defs><linearGradient id='g' x1='0' y1='0' x2='0' y2='1'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><line x1='4' y1='0' x2='4' y2='8' stroke='url(#g)' stroke-width='2'/>")]
    public void Bounding_box_gradients_do_not_paint_zero_area_line_boxes(string body)
    {
        using SvgRoot root = SvgRootParser.Parse(System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg'>" + body + "</svg>"), SvgRenderLimits.Default, CancellationToken.None);

        Assert.False(root.HasUnsupportedChild);
        Assert.False(Assert.Single(root.Geometries).GradientPolygon.HasFill);
        RenderBudget rasterBudget = new(1_000_000);
        using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root,
            new(new SvgViewport(8, 8), 1, SvgColor.Transparent, SvgRenderLimits.Default), rasterBudget, CancellationToken.None))
            Assert.All(surface.Pixels, component => Assert.Equal((byte)0, component));
        Assert.Equal(0, rasterBudget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Admits_nonuniform_userspace_gradient_transforms()
    {
        byte[] png = Lokad.Svg.SvgConverter.ConvertToPng("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='16'><defs><linearGradient id='g' gradientUnits='userSpaceOnUse' x1='0' y1='0' x2='16' y2='16'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><g transform='scale(2, 1)'><rect x='0' y='0' width='16' height='16' fill='url(#g)'/></g></svg>"u8.ToArray(), Lokad.Svg.SvgRenderOptions.Default, CancellationToken.None).ToArray();

        Assert.Equal(32, System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16, 4)));
        Assert.Equal(16, System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20, 4)));
    }

    [Fact]
    public void Records_reference_scales_on_nonuniform_userspace_vectors()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='16'><defs><linearGradient id='g' gradientUnits='userSpaceOnUse' x1='0' y1='0' x2='16' y2='16'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><g transform='scale(2, 1)'><rect x='0' y='0' width='16' height='16' fill='url(#g)'/></g></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.GradientFill, geometry.Kind);
        Assert.Equal(2, geometry.Gradient.Gradient.ReferenceScaleX);
        Assert.Equal(1, geometry.Gradient.Gradient.ReferenceScaleY);
        Assert.Equal(0, geometry.Gradient.Gradient.X1);
        Assert.Equal(32, geometry.Gradient.Gradient.X2);
        Assert.Equal(16, geometry.Gradient.Gradient.Y2);
    }

    [Fact]
    public void Admits_integer_mapped_pattern_tiles_under_viewBox_scaling()
    {
        byte[] svg = "<svg xmlns='http://www.w3.org/2000/svg' width='64' height='64' viewBox='0 0 32 32'><defs><pattern id='p' width='8' height='8' patternUnits='userSpaceOnUse'><rect width='4' height='8' fill='#ff0000'/></pattern></defs><rect x='0' y='0' width='32' height='32' fill='url(#p)'/></svg>"u8.ToArray();
        byte[] png = Lokad.Svg.SvgConverter.ConvertToPng(svg, Lokad.Svg.SvgRenderOptions.Default, CancellationToken.None).ToArray();

        Assert.Equal(64, System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16, 4)));
        Assert.Equal(64, System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20, 4)));
    }

    [Fact]
    public void Renders_fractional_pattern_phase_under_viewBox_scaling()
    {
        byte[] svg = "<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8' viewBox='0 0 16 16'><defs><pattern id='p' x='1' width='8' height='8' patternUnits='userSpaceOnUse'><rect width='4' height='4' fill='#ff0000'/></pattern></defs><rect x='0' y='0' width='16' height='16' fill='url(#p)'/></svg>"u8.ToArray();

        ReadOnlyMemory<byte> png = SvgConverter.ConvertToPng(svg, SvgRenderOptions.Default, CancellationToken.None);
        Assert.False(png.IsEmpty);
    }

    [Fact]
    public void Renders_pattern_content_strokes_under_viewBox_scaling()
    {
        byte[] svg = "<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8' viewBox='0 0 4 4'><defs><pattern id='p' width='8' height='8' patternUnits='userSpaceOnUse'><rect x='2' y='2' width='4' height='4' fill='red' stroke='blue' stroke-width='2'/></pattern></defs><rect x='0' y='0' width='4' height='4' fill='url(#p)'/></svg>"u8.ToArray();

        ReadOnlyMemory<byte> png = Lokad.Svg.SvgConverter.ConvertToPng(svg, Lokad.Svg.SvgRenderOptions.Default, CancellationToken.None);

        Assert.False(png.IsEmpty);
    }

    [Fact]
    public void Scaled_pattern_output_scale_matches_doubled_rendering()
    {
        byte[] small = "<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8' ><defs><pattern id='p' width='4' height='4' patternUnits='userSpaceOnUse'><rect width='2' height='2' fill='red'/></pattern></defs><rect x='0' y='0' width='8' height='8' fill='url(#p)'/></svg>"u8.ToArray();
        byte[] big = "<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><defs><pattern id='p' width='8' height='8' patternUnits='userSpaceOnUse'><rect width='4' height='4' fill='red'/></pattern></defs><rect x='0' y='0' width='16' height='16' fill='url(#p)'/></svg>"u8.ToArray();

        Lokad.Svg.SvgRenderOptions scaled = new(null, 2, SvgColor.Transparent, SvgRenderLimits.Default);
        byte[] doubled = Lokad.Svg.SvgConverter.ConvertToPng(small, scaled, CancellationToken.None).ToArray();
        byte[] reference = Lokad.Svg.SvgConverter.ConvertToPng(big, Lokad.Svg.SvgRenderOptions.Default, CancellationToken.None).ToArray();

        Assert.Equal(reference, doubled);
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
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><path d='M -2147483648,0 H 2147483647 V 1 H -2147483648 Z'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(-2_147_483_648d, geometry.Polygon.Points.ToArray().Min(point => point.X));
        Assert.Equal(2_147_483_648d, geometry.Polygon.Points.ToArray().Max(point => point.X));
        Assert.NotNull(geometry.AuthoredPath);
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
    public void Reflects_rectangles_through_negative_integer_scale()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><rect x='1' y='2' width='3' height='4' transform='translate(8,0) scale(-1,1)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(new SvgRectangle(4, 2, 3, 4, new SvgColor(0, 0, 0, 255), true, null), root.Rectangles[0]);
    }

    [Fact]
    public void Reflects_rectangles_through_negative_axis_aligned_matrix()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><rect x='1' y='2' width='3' height='4' transform='matrix(-1,0,0,1,8,0)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(new SvgRectangle(4, 2, 3, 4, new SvgColor(0, 0, 0, 255), true, null), root.Rectangles[0]);
    }

    [Theory]
    [InlineData("scale(0)")]
    [InlineData("scale(0,1)")]
    [InlineData("scale(1,0)")]
    [InlineData("matrix(0,0,0,1,0,0)")]
    public void Rejects_zero_geometry_transforms(string transform)
    {
        SvgException exception = Assert.Throws<SvgException>(() => SvgRootParser.Parse(System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg'><rect width='8' height='8' transform='" + transform + "'/></svg>"), SvgRenderLimits.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, exception.ErrorCode);
    }

    [Fact]
    public void Mirrors_bounding_box_gradient_vectors_under_reflection()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='g' x1='0' y1='0' x2='1' y2='0'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><rect width='8' height='8' fill='url(#g)' transform='translate(8,0) scale(-1,1)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.GradientFill, geometry.Kind);
        AssertEqualGradient(new SvgLinearGradient(new SvgColor(255, 0, 0, 255), new SvgColor(0, 0, 255, 255), 1, 0, 0, 0, 0, 1, false, false, SvgGradientSpread.Pad, new SvgGradientStop[] { new(0, new SvgColor(255, 0, 0, 255)), new(1, new SvgColor(0, 0, 255, 255)) }, 1, 1, false), geometry.Gradient.Gradient);
    }

    [Fact]
    public void Clips_symbol_content_to_the_use_viewport_before_the_instance_transform()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><symbol id='s' viewBox='0 0 4 4'><rect width='4' height='4' fill='red'/></symbol></defs><use href='#s' x='0' y='0' width='4' height='4' transform='translate(8,0) scale(-1,1)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(new SvgRectangle(4, 0, 4, 4, new SvgColor(255, 0, 0, 255), true, null), Assert.Single(root.Rectangles));
    }

    [Fact]
    public void Offscreen_reflected_pattern_rectangles_paint_nothing()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><pattern id='p' width='2' height='2' patternUnits='userSpaceOnUse'><rect width='1' height='1' fill='red'/></pattern></defs><rect width='4' height='4' fill='url(#p)' transform='scale(-1,1)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.False(root.HasUnsupportedChild);
        RenderBudget rasterBudget = new(1_000_000);
        using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root,
            new(new SvgViewport(8, 8), 1, SvgColor.Transparent, SvgRenderLimits.Default), rasterBudget, CancellationToken.None))
            Assert.All(surface.Pixels, component => Assert.Equal((byte)0, component));
        Assert.Equal(0, rasterBudget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Renders_reflected_paths_identical_to_hand_mirrored_baselines()
    {
        byte[] reflected = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><path d='M1,1 H4 V6 H1 Z' fill='red' transform='translate(8,0) scale(-1,1)'/></svg>");
        byte[] baseline = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><path d='M7,1 H4 V6 H7 Z' fill='red'/></svg>");

        ReadOnlyMemory<byte> reflectedPng = SvgConverter.ConvertToPng(reflected, SvgRenderOptions.Default, CancellationToken.None);
        ReadOnlyMemory<byte> baselinePng = SvgConverter.ConvertToPng(baseline, SvgRenderOptions.Default, CancellationToken.None);

        Assert.True(reflectedPng.Span.SequenceEqual(baselinePng.Span));
    }

    [Fact]
    public void Rejects_reflected_bands_beyond_the_coordinate_range()
    {
        SvgException exception = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><path d='M -2147483648,0 H 0 V 1 H -2147483648 Z' transform='scale(-1,1)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, exception.ErrorCode);
    }

    [Fact]
    public void Rejects_extreme_line_stroke_scales_under_reflection()
    {
        SvgException exception = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><line id='l' x1='0' y1='0' x2='0' y2='2' stroke='red' stroke-width='2'/></defs><use href='#l' transform='matrix(-2147483648,0,0,1,0,0)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, exception.ErrorCode);
    }

    [Fact]
    public void Retains_gradient_path_contours_and_the_original_paint_box()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='g'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><path d='M1,1 H7 V3 H3 V7 H1 Z' fill='url(#g)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.GradientPolygon, geometry.Kind);
        Assert.Equal(new SvgRectangle(1, 1, 6, 6, default, true, null), geometry.GradientPolygon.Box);
        Assert.Equal(6, geometry.GradientPolygon.Points.Length);
        Assert.NotNull(geometry.AuthoredPath);
    }

    [Fact]
    public void Retains_gradient_filled_polygon_contours()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='g'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><polygon points='1,1 7,1 7,3 3,3 3,7 1,7' fill='url(#g)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.GradientPolygon, geometry.Kind);
        SvgRectangle box = geometry.GradientPolygon.Box;
        Assert.Equal((1d, 1d, 6d, 6d), (box.X, box.Y, box.Width, box.Height));
        Assert.Equal(7, Assert.IsType<SvgAuthoredPath>(geometry.AuthoredPath).Commands.Length);
    }

    [Fact]
    public void Lowers_gradient_filled_polygon_instances_through_placement()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='g'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><defs><polygon id='p' points='0,0 8,0 8,4 4,4 4,8 0,8' fill='url(#g)'/></defs><use href='#p' x='8'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.GradientPolygon, geometry.Kind);
        SvgRectangle box = geometry.GradientPolygon.Box;
        Assert.Equal((8d, 0d, 8d, 8d), (box.X, box.Y, box.Width, box.Height));
        SvgAuthoredPath source = Assert.IsType<SvgAuthoredPath>(geometry.AuthoredPath);
        Assert.Equal(SvgAffineTransform.FromTranslation(8, 0), source.SceneMap);
        Assert.Equal(7, source.Commands.Length);
    }

    [Fact]
    public void Lowers_symbol_gradient_instances_through_viewBox_mapping()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='g'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><defs><symbol id='s' viewBox='0 0 4 4'><polygon points='0,0 4,0 4,2 2,2 2,4 0,4' fill='url(#g)'/></symbol></defs><use href='#s' width='8' height='8' opacity='0.5'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgRecordedPaintAssertions.ElementOpacity(root, new(128, 0.5f));
        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.GradientPolygon, geometry.Kind);
        SvgRectangle box = geometry.GradientPolygon.Box;
        Assert.Equal((0d, 0d, 8d, 8d), (box.X, box.Y, box.Width, box.Height));
        Assert.Equal(255, geometry.GradientPolygon.Opacity);
        Assert.Equal(new SvgClipBox(0, 0, 8, 8), geometry.GradientPolygon.Clip);
        Assert.Equal(SvgAffineTransform.FromScale(2, 2), Assert.IsType<SvgAuthoredPath>(geometry.AuthoredPath).SceneMap);
    }

    [Fact]
    public void Lowers_symbol_gradient_stroke_instances_through_viewBox_mapping()
    {
        byte[] input = "<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='g' gradientUnits='userSpaceOnUse' x1='0' y1='0' x2='4' y2='0'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><defs><symbol id='s' viewBox='0 0 4 4'><rect x='0' y='0' width='4' height='4' fill='#ff0000' stroke='url(#g)' stroke-width='2'/></symbol></defs><use href='#s' width='8' height='8' opacity='0.5'/></svg>"u8.ToArray();
        byte[] expected = "<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='g' gradientUnits='userSpaceOnUse' x1='0' y1='0' x2='4' y2='0'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><g opacity='0.5'><svg width='8' height='8' viewBox='0 0 4 4'><rect width='4' height='4' fill='#ff0000' stroke='url(#g)' stroke-width='2'/></svg></g></svg>"u8.ToArray();
        Assert.Equal(SvgConverter.ConvertToPng(expected, SvgRenderOptions.Default, CancellationToken.None).ToArray(), SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None).ToArray());
        using SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, CancellationToken.None);
        SvgDrawCommand opacityScope = Assert.Single(root.Commands, command => command.Kind == SvgDrawCommandKind.BeginGroup && command.SourceOpacity.Value != 1);
        SvgRecordedPaintAssertions.OpacityScope(opacityScope, new(128, 0.5f));
    }
    [Fact]
    public void Lowers_gradient_stroke_rect_instances_through_placement()
    {
        byte[] input = "<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='g' gradientUnits='userSpaceOnUse' x1='0' y1='0' x2='8' y2='0'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><defs><rect id='r' width='8' height='8' fill='#ff0000' stroke='url(#g)' stroke-width='2'/></defs><use href='#r' x='8'/></svg>"u8.ToArray();
        byte[] expected = "<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='g' gradientUnits='userSpaceOnUse' x1='0' y1='0' x2='8' y2='0'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><g transform='translate(8)'><rect width='8' height='8' fill='#ff0000' stroke='url(#g)' stroke-width='2'/></g></svg>"u8.ToArray();
        Assert.Equal(SvgConverter.ConvertToPng(expected, SvgRenderOptions.Default, CancellationToken.None).ToArray(), SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None).ToArray());
    }

    [Fact]
    public void Renders_reflected_gradient_paths_identical_to_hand_mirrored_baselines()
    {
        byte[] reflected = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><linearGradient id='g'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><path d='M1,1 H7 V3 H3 V7 H1 Z' fill='url(#g)' transform='translate(8,0) scale(-1,1)'/></svg>");
        byte[] baseline = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><linearGradient id='g' x1='1' y1='0' x2='0' y2='0'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><path d='M7,1 H1 V3 H5 V7 H7 Z' fill='url(#g)'/></svg>");

        ReadOnlyMemory<byte> reflectedPng = SvgConverter.ConvertToPng(reflected, SvgRenderOptions.Default, CancellationToken.None);
        ReadOnlyMemory<byte> baselinePng = SvgConverter.ConvertToPng(baseline, SvgRenderOptions.Default, CancellationToken.None);

        Assert.True(reflectedPng.Span.SequenceEqual(baselinePng.Span));
    }

    [Fact]
    public void Renders_degenerate_pad_gradient_paths_as_end_stop_solids()
    {
        byte[] gradient = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><linearGradient id='g' x1='1' y1='1' x2='1' y2='1'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><path d='M1,1 H7 V3 H3 V7 H1 Z' fill='url(#g)'/></svg>");
        byte[] baseline = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><path d='M1,1 H7 V3 H3 V7 H1 Z' fill='blue'/></svg>");

        ReadOnlyMemory<byte> gradientPng = SvgConverter.ConvertToPng(gradient, SvgRenderOptions.Default, CancellationToken.None);
        ReadOnlyMemory<byte> baselinePng = SvgConverter.ConvertToPng(baseline, SvgRenderOptions.Default, CancellationToken.None);

        Assert.True(gradientPng.Span.SequenceEqual(baselinePng.Span));
    }
    [Fact]
    public void Retains_single_stop_rectangle_shader_alpha()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='g'><stop offset='0.3' stop-color='#ff0000' stop-opacity='.27'/></linearGradient></defs><rect width='8' height='8' fill='url(#g)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGradientGeometry gradient = Assert.Single(root.Geometries).Gradient;
        Assert.Equal(8, gradient.Fill.Width);
        Assert.Equal(8, gradient.Fill.Height);
        SvgGradientStop stop = Assert.Single(gradient.Gradient.Stops);
        Assert.Equal(.27f, stop.Alpha);
        Assert.Equal(new SvgColor(255, 0, 0, 69), stop.Color);
    }

    [Fact]
    public void Retains_single_stop_gradient_paths_as_solid_contours()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='g' spreadMethod='repeat'><stop offset='0.3' stop-color='#ff0000'/></linearGradient></defs><path d='M1,1 H7 V3 H3 V7 H1 Z' fill='url(#g)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.PolygonFill, geometry.Kind);
        Assert.Equal(new SvgColor(255, 0, 0, 255), geometry.Polygon.Color);
        Assert.NotNull(geometry.AuthoredPath);
    }

    [Fact]
    public void Renders_single_stop_gradients_identical_to_solid_fills()
    {
        byte[] gradient = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><linearGradient id='g'><stop offset='0.3' stop-color='#ff0000'/></linearGradient></defs><rect width='8' height='8' fill='url(#g)'/></svg>");
        byte[] baseline = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><rect width='8' height='8' fill='red'/></svg>");

        ReadOnlyMemory<byte> gradientPng = SvgConverter.ConvertToPng(gradient, SvgRenderOptions.Default, CancellationToken.None);
        ReadOnlyMemory<byte> baselinePng = SvgConverter.ConvertToPng(baseline, SvgRenderOptions.Default, CancellationToken.None);

        Assert.True(gradientPng.Span.SequenceEqual(baselinePng.Span));
    }
    [Fact]
    public void Renders_empty_transforms_identical_to_untransformed_content()
    {
        byte[] plain = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><rect x='1' y='1' width='6' height='6' fill='red'/></svg>");
        byte[] empty = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><rect x='1' y='1' width='6' height='6' fill='red' transform=''/></svg>");
        byte[] blank = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><rect x='1' y='1' width='6' height='6' fill='red' transform='   '/></svg>");

        ReadOnlyMemory<byte> plainPng = SvgConverter.ConvertToPng(plain, SvgRenderOptions.Default, CancellationToken.None);
        ReadOnlyMemory<byte> emptyPng = SvgConverter.ConvertToPng(empty, SvgRenderOptions.Default, CancellationToken.None);
        ReadOnlyMemory<byte> blankPng = SvgConverter.ConvertToPng(blank, SvgRenderOptions.Default, CancellationToken.None);

        Assert.True(emptyPng.Span.SequenceEqual(plainPng.Span));
        Assert.True(blankPng.Span.SequenceEqual(plainPng.Span));
    }

    [Fact]
    public void Renders_empty_gradient_transforms_identical_to_untransformed_vectors()
    {
        byte[] plain = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><linearGradient id='g'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><rect width='8' height='8' fill='url(#g)'/></svg>");
        byte[] empty = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><linearGradient id='g' gradientTransform=''><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><rect width='8' height='8' fill='url(#g)'/></svg>");

        ReadOnlyMemory<byte> plainPng = SvgConverter.ConvertToPng(plain, SvgRenderOptions.Default, CancellationToken.None);
        ReadOnlyMemory<byte> emptyPng = SvgConverter.ConvertToPng(empty, SvgRenderOptions.Default, CancellationToken.None);

        Assert.True(emptyPng.Span.SequenceEqual(plainPng.Span));
    }

    [Fact]
    public void Renders_empty_pattern_transforms_identical_to_unshifted_tiles()
    {
        byte[] plain = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><pattern id='p' width='4' height='4' patternUnits='userSpaceOnUse'><rect width='4' height='4' fill='red'/></pattern></defs><rect width='8' height='8' fill='url(#p)'/></svg>");
        byte[] empty = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><pattern id='p' width='4' height='4' patternUnits='userSpaceOnUse' patternTransform=''><rect width='4' height='4' fill='red'/></pattern></defs><rect width='8' height='8' fill='url(#p)'/></svg>");

        ReadOnlyMemory<byte> plainPng = SvgConverter.ConvertToPng(plain, SvgRenderOptions.Default, CancellationToken.None);
        ReadOnlyMemory<byte> emptyPng = SvgConverter.ConvertToPng(empty, SvgRenderOptions.Default, CancellationToken.None);

        Assert.True(emptyPng.Span.SequenceEqual(plainPng.Span));
    }

    [Fact]
    public void Renders_single_stop_use_instances_as_solid_bands()
    {
        byte[] instanced = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='8'><defs><linearGradient id='g'><stop offset='0.3' stop-color='#ff0000'/></linearGradient></defs><defs><polygon id='p' points='0,0 8,0 8,4 4,4 4,8 0,8' fill='url(#g)'/></defs><use href='#p' x='8'/></svg>");
        byte[] baseline = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='8'><polygon points='8,0 16,0 16,4 12,4 12,8 8,8' fill='red'/></svg>");

        ReadOnlyMemory<byte> instancedPng = SvgConverter.ConvertToPng(instanced, SvgRenderOptions.Default, CancellationToken.None);
        ReadOnlyMemory<byte> baselinePng = SvgConverter.ConvertToPng(baseline, SvgRenderOptions.Default, CancellationToken.None);

        Assert.True(instancedPng.Span.SequenceEqual(baselinePng.Span));
    }

    [Fact]
    public void Lowers_single_stop_gradient_strokes_as_constant_bands()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='g'><stop offset='0.3' stop-color='#ff0000'/></linearGradient></defs><rect x='1' y='1' width='6' height='6' fill='red' stroke='url(#g)' stroke-width='2'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.GradientStroke, geometry.Kind);
        Assert.True(geometry.GradientStroke.HasStroke);
        Assert.Equal(new SvgRectangle(1, 1, 6, 6, new SvgColor(255, 0, 0, 255), true, null), geometry.GradientStroke.Fill);
    }

    [Fact]
    public void Lowers_single_stop_gradient_strokes_on_lines_as_native_contours()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='g' gradientUnits='userSpaceOnUse' x1='0' y1='0' x2='8' y2='0'><stop offset='0.3' stop-color='#ff0000'/></linearGradient></defs><line x1='0' y1='4' x2='8' y2='4' stroke='url(#g)' stroke-width='2'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.PolygonFill, geometry.Kind);
        AssertStrokeContourBounds(geometry, [new(0, 3, 8, 2, default, true, null)]);
        Assert.Equal(new SvgColor(255, 0, 0, 255), geometry.Polygon.Color);
    }

    [Fact]
    public void Lowers_dashed_gradient_strokes_on_lines_as_native_contours()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='g' gradientUnits='userSpaceOnUse' x1='0' y1='0' x2='16' y2='0'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><line x1='0' y1='4' x2='16' y2='4' stroke='url(#g)' stroke-width='2' stroke-dasharray='4 2'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.GradientPolygon, geometry.Kind);
        AssertStrokeContourBounds(geometry, [new(0, 3, 4, 2, default, true, null), new(6, 3, 4, 2, default, true, null), new(12, 3, 4, 2, default, true, null)]);
    }

    [Fact]
    public void Lowers_dashed_radial_strokes_on_lines_as_native_contours()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><radialGradient id='g' gradientUnits='userSpaceOnUse' cx='8' cy='4' r='8'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><line x1='0' y1='4' x2='16' y2='4' stroke='url(#g)' stroke-width='2' stroke-dasharray='4 2'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.RadialPolygon, geometry.Kind);
        AssertStrokeContourBounds(geometry, [new(0, 3, 4, 2, default, true, null), new(6, 3, 4, 2, default, true, null), new(12, 3, 4, 2, default, true, null)]);
    }

    [Fact]
    public void Lowers_dashed_pattern_strokes_on_lines_as_native_contours()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><pattern id='p' width='8' height='8' patternUnits='userSpaceOnUse'><rect width='8' height='8' fill='red'/></pattern></defs><line x1='0' y1='4' x2='16' y2='4' stroke='url(#p)' stroke-width='2' stroke-dasharray='4 2'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.PatternPolygon, geometry.Kind);
        AssertStrokeContourBounds(geometry, [new(0, 3, 4, 2, default, true, null), new(6, 3, 4, 2, default, true, null), new(12, 3, 4, 2, default, true, null)]);
    }

    [Fact]
    public void Path_length_scales_dashed_line_runs()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><line x1='0' y1='4' x2='16' y2='4' stroke='red' stroke-width='2' stroke-dasharray='4 2' pathLength='8'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(2, root.Geometries.Length);
        Assert.All(root.Geometries, geometry => Assert.Equal(SvgGeometryKind.OrthogonalBand, geometry.Kind));
        Assert.Equal(new SvgRectangle(0, 3, 8, 2, new SvgColor(255, 0, 0, 255), true, null), root.Geometries[0].Rectangle);
        Assert.Equal(new SvgRectangle(12, 3, 4, 2, new SvgColor(255, 0, 0, 255), true, null), root.Geometries[1].Rectangle);
    }

    [Fact]
    public void Zero_path_length_pins_a_solid_line()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><line x1='0' y1='4' x2='16' y2='4' stroke='red' stroke-width='2' stroke-dasharray='4 2' pathLength='0'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.OrthogonalBand, geometry.Kind);
        Assert.Equal(new SvgRectangle(0, 3, 16, 2, new SvgColor(255, 0, 0, 255), true, null), geometry.Rectangle);
    }

    [Fact]
    public void Negative_path_length_on_lines_is_malformed()
    {
        SvgException rejected = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><line x1='0' y1='4' x2='16' y2='4' stroke='red' stroke-width='2' stroke-dasharray='4 2' pathLength='-1'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.MalformedInput, rejected.ErrorCode);
    }

    [Fact]
    public void Path_length_scales_dashed_gradient_line_runs()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='g' gradientUnits='userSpaceOnUse' x1='0' y1='0' x2='16' y2='0'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><line x1='0' y1='4' x2='16' y2='4' stroke='url(#g)' stroke-width='2' stroke-dasharray='4 2' pathLength='8'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.GradientPolygon, geometry.Kind);
        AssertStrokeContourBounds(geometry, [new(0, 3, 8, 2, default, true, null), new(12, 3, 4, 2, default, true, null)]);
    }

    [Fact]
    public void Dash_offset_shifts_gradient_line_runs()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='g' gradientUnits='userSpaceOnUse' x1='0' y1='0' x2='16' y2='0'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><line x1='0' y1='4' x2='16' y2='4' stroke='url(#g)' stroke-width='2' stroke-dasharray='4 2' stroke-dashoffset='2'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.GradientPolygon, geometry.Kind);
        AssertStrokeContourBounds(geometry, [new(0, 3, 2, 2, default, true, null), new(4, 3, 4, 2, default, true, null), new(10, 3, 4, 2, default, true, null)]);
    }

    [Fact]
    public void Square_caps_extend_dashed_gradient_line_runs()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='g' gradientUnits='userSpaceOnUse' x1='0' y1='0' x2='16' y2='0'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><line x1='0' y1='4' x2='16' y2='4' stroke='url(#g)' stroke-width='2' stroke-dasharray='4 2' stroke-linecap='square'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.GradientPolygon, geometry.Kind);
        AssertStrokeContourBounds(geometry, [new(-1, 3, 6, 2, default, true, null), new(5, 3, 6, 2, default, true, null), new(11, 3, 6, 2, default, true, null)]);
    }
    [Theory]
    [InlineData("<path d='M1,1 H7 V3 H3 V7 H1 Z' fill='url(#h)'/>")]
    public void Rejects_unsupported_gradient_path_forms(string body)
    {
        SvgException exception = Assert.Throws<SvgException>(() => SvgRootParser.Parse(System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='g'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs>" + body + "</svg>"), SvgRenderLimits.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, exception.ErrorCode);
    }

    [Fact]
    public void Inherits_gradient_fill_on_an_orthogonal_path()
    {
        using SvgRoot root = SvgRootParser.Parse(System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='g'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><g fill='url(#g)'><path d='M1,1 H7 V3 H3 V7 H1 Z'/></g></svg>"), SvgRenderLimits.Default, CancellationToken.None);

        Assert.NotEmpty(root.Geometries);
        Assert.All(root.Geometries, geometry => Assert.Equal(SvgGeometryKind.GradientPolygon, geometry.Kind));
    }

    [Fact]
    public void Retains_pattern_path_contours()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><pattern id='p' width='4' height='4' patternUnits='userSpaceOnUse'><rect width='2' height='2' fill='red'/></pattern></defs><path d='M1,1 H7 V3 H3 V7 H1 Z' fill='url(#p)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.PatternPolygon, geometry.Kind);
        Assert.Equal(new SvgRectangle(0, 0, 2, 2, new SvgColor(255, 0, 0, 255), true, null), NativePatternRectangle(Assert.IsType<SvgVectorContent>(geometry.PatternPolygon.VectorContent)));
        Assert.Equal(6, geometry.PatternPolygon.Points.Length);
        Assert.NotNull(geometry.AuthoredPath);
    }

    [Fact]
    public void Retains_pattern_filled_polygon_contours()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><pattern id='p' width='4' height='4' patternUnits='userSpaceOnUse'><rect width='2' height='2' fill='red'/></pattern></defs><polygon points='1,1 7,1 7,3 3,3 3,7 1,7' fill='url(#p)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.PatternPolygon, geometry.Kind);
        Assert.Equal(7, Assert.IsType<SvgAuthoredPath>(geometry.AuthoredPath).Commands.Length);
        Assert.Equal(new SvgRectangle(0, 0, 2, 2, new(255, 0, 0, 255), true, null),
            NativePatternRectangle(Assert.IsType<SvgVectorContent>(geometry.PatternPolygon.VectorContent)));
    }

    [Fact]
    public void Lowers_pattern_filled_polygon_instances_through_placement()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><pattern id='p' width='8' height='8' patternUnits='userSpaceOnUse'><rect width='4' height='4' fill='#ff0000'/></pattern></defs><defs><polygon id='q' points='0,0 8,0 8,4 4,4 4,8 0,8' fill='url(#p)'/></defs><use href='#q' x='8' opacity='0.5'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgRecordedPaintAssertions.ElementOpacity(root, new(128, 0.5f));
        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.PatternPolygon, geometry.Kind);
        Assert.Equal(255, geometry.PatternPolygon.Opacity);
        SvgAuthoredPath source = Assert.IsType<SvgAuthoredPath>(geometry.AuthoredPath);
        Assert.Equal(SvgAffineTransform.FromTranslation(8, 0), source.SceneMap);
        Assert.Equal(7, source.Commands.Length);
    }

    [Fact]
    public void Routes_gradient_and_pattern_paths_to_retained_contours()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='g'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient><pattern id='p' width='4' height='4' patternUnits='userSpaceOnUse'><rect width='2' height='2' fill='red'/></pattern></defs><path d='M0,0 H4 V4 H0 Z' fill='url(#g)'/><path d='M4,4 H8 V8 H4 Z' fill='url(#p)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(SvgGeometryKind.GradientPolygon, root.Geometries[0].Kind);
        Assert.Equal(SvgGeometryKind.PatternPolygon, root.Geometries[1].Kind);
    }

    [Theory]
    [InlineData("<path d='M1,1 H7 V7 H1 Z' fill='url(#p)' transform='translate(8,0) scale(-1,1)'/>")]
    public void Replays_reflected_pattern_paths(string body)
    {
        using SvgRoot scene = SvgRootParser.Parse(System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg'><defs><pattern id='p' width='4' height='4' patternUnits='userSpaceOnUse'><rect width='2' height='2' fill='red'/></pattern></defs>" + body + "</svg>"), SvgRenderLimits.Default, CancellationToken.None);

        Assert.NotNull(Assert.Single(scene.Geometries).PatternPolygon.VectorContent);
    }
    [Fact]
    public void Retains_gradient_fill_opacity_separately_from_element_opacity()
    {
        byte[] faded = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><linearGradient id='g'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><rect width='8' height='8' fill='url(#g)' fill-opacity='0.5'/></svg>");
        byte[] baseline = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><linearGradient id='g'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><rect width='8' height='8' fill='url(#g)' opacity='0.5'/></svg>");

        ReadOnlyMemory<byte> fadedPng = SvgConverter.ConvertToPng(faded, SvgRenderOptions.Default, CancellationToken.None);
        ReadOnlyMemory<byte> baselinePng = SvgConverter.ConvertToPng(baseline, SvgRenderOptions.Default, CancellationToken.None);

        // Fresh browser captures differ for these unchanged inputs.
        Assert.False(fadedPng.Span.SequenceEqual(baselinePng.Span));
        using SvgRoot first = SvgRootParser.Parse(faded, SvgRenderLimits.Default, CancellationToken.None);
        using SvgRoot second = SvgRootParser.Parse(baseline, SvgRenderLimits.Default, CancellationToken.None);
        SvgRecordedPaintAssertions.ShaderAlpha(first, 128 / 255f);
        Assert.DoesNotContain(first.Commands, command => command.Kind == SvgDrawCommandKind.BeginGroup);
        SvgRecordedPaintAssertions.ShaderAlpha(second, 1);
        SvgRecordedPaintAssertions.ElementOpacity(second, new(128, 0.5f));
    }

    [Fact]
    public void Preserves_shader_byte_and_element_float_opacity_when_combined()
    {
        byte[] layered = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><linearGradient id='g'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><rect width='8' height='8' fill='url(#g)' opacity='0.5' fill-opacity='0.5'/></svg>");
        byte[] baseline = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><linearGradient id='g'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><rect width='8' height='8' fill='url(#g)' opacity='0.25'/></svg>");

        ReadOnlyMemory<byte> layeredPng = SvgConverter.ConvertToPng(layered, SvgRenderOptions.Default, CancellationToken.None);
        ReadOnlyMemory<byte> baselinePng = SvgConverter.ConvertToPng(baseline, SvgRenderOptions.Default, CancellationToken.None);

        // Fresh browser captures differ for these unchanged inputs.
        Assert.False(layeredPng.Span.SequenceEqual(baselinePng.Span));
        using SvgRoot first = SvgRootParser.Parse(layered, SvgRenderLimits.Default, CancellationToken.None);
        using SvgRoot second = SvgRootParser.Parse(baseline, SvgRenderLimits.Default, CancellationToken.None);
        SvgRecordedPaintAssertions.ShaderAlpha(first, 128 / 255f);
        SvgRecordedPaintAssertions.ElementOpacity(first, new(128, 0.5f));
        SvgRecordedPaintAssertions.ShaderAlpha(second, 1);
        SvgRecordedPaintAssertions.ElementOpacity(second, new(64, 0.25f));
    }

    [Fact]
    public void Retains_gradient_path_fill_opacity_separately_from_element_opacity()
    {
        byte[] faded = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><linearGradient id='g'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><path d='M1,1 H7 V3 H3 V7 H1 Z' fill='url(#g)' fill-opacity='0.5'/></svg>");
        byte[] baseline = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><linearGradient id='g'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><path d='M1,1 H7 V3 H3 V7 H1 Z' fill='url(#g)' opacity='0.5'/></svg>");

        ReadOnlyMemory<byte> fadedPng = SvgConverter.ConvertToPng(faded, SvgRenderOptions.Default, CancellationToken.None);
        ReadOnlyMemory<byte> baselinePng = SvgConverter.ConvertToPng(baseline, SvgRenderOptions.Default, CancellationToken.None);

        // Fresh browser captures differ for these unchanged inputs.
        Assert.False(fadedPng.Span.SequenceEqual(baselinePng.Span));
        using SvgRoot first = SvgRootParser.Parse(faded, SvgRenderLimits.Default, CancellationToken.None);
        using SvgRoot second = SvgRootParser.Parse(baseline, SvgRenderLimits.Default, CancellationToken.None);
        SvgRecordedPaintAssertions.ShaderAlpha(first, 128 / 255f);
        Assert.DoesNotContain(first.Commands, command => command.Kind == SvgDrawCommandKind.BeginGroup);
        SvgRecordedPaintAssertions.ShaderAlpha(second, 1);
        SvgRecordedPaintAssertions.ElementOpacity(second, new(128, 0.5f));
    }

    [Fact]
    public void Renders_pattern_path_fill_opacity_identical_to_opacity()
    {
        byte[] faded = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><pattern id='p' width='4' height='4' patternUnits='userSpaceOnUse'><rect width='2' height='2' fill='red'/></pattern></defs><path d='M1,1 H7 V3 H3 V7 H1 Z' fill='url(#p)' fill-opacity='0.5'/></svg>");
        byte[] baseline = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><pattern id='p' width='4' height='4' patternUnits='userSpaceOnUse'><rect width='2' height='2' fill='red'/></pattern></defs><path d='M1,1 H7 V3 H3 V7 H1 Z' fill='url(#p)' opacity='0.5'/></svg>");

        ReadOnlyMemory<byte> fadedPng = SvgConverter.ConvertToPng(faded, SvgRenderOptions.Default, CancellationToken.None);
        ReadOnlyMemory<byte> baselinePng = SvgConverter.ConvertToPng(baseline, SvgRenderOptions.Default, CancellationToken.None);

        Assert.True(fadedPng.Span.SequenceEqual(baselinePng.Span));
    }

    [Fact]
    public void Renders_pattern_rect_fill_opacity_identical_to_opacity()
    {
        byte[] faded = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><pattern id='p' width='4' height='4' patternUnits='userSpaceOnUse'><rect width='2' height='2' fill='red'/></pattern></defs><rect width='8' height='8' fill='url(#p)' fill-opacity='0.5'/></svg>");
        byte[] baseline = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><pattern id='p' width='4' height='4' patternUnits='userSpaceOnUse'><rect width='2' height='2' fill='red'/></pattern></defs><rect width='8' height='8' fill='url(#p)' opacity='0.5'/></svg>");

        ReadOnlyMemory<byte> fadedPng = SvgConverter.ConvertToPng(faded, SvgRenderOptions.Default, CancellationToken.None);
        ReadOnlyMemory<byte> baselinePng = SvgConverter.ConvertToPng(baseline, SvgRenderOptions.Default, CancellationToken.None);

        Assert.True(fadedPng.Span.SequenceEqual(baselinePng.Span));
    }

    [Fact]
    public void Keeps_solid_stroke_opacity_outside_gradient_fill_opacity()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='g'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><rect x='1' y='1' width='6' height='6' fill='url(#g)' fill-opacity='0.5' stroke='green' stroke-width='2'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.GradientFill, geometry.Kind);
        Assert.Equal(128, geometry.Gradient.Opacity);
        Assert.Equal(new SvgColor(0, 128, 0, 255), geometry.Gradient.Stroke.GetValueOrDefault().Color);
    }

    [Fact]
    public void Keeps_solid_stroke_opacity_outside_pattern_fill_opacity()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><pattern id='p' width='4' height='4' patternUnits='userSpaceOnUse'><rect width='2' height='2' fill='red'/></pattern></defs><rect x='1' y='1' width='6' height='6' fill='url(#p)' fill-opacity='0.5' stroke='green' stroke-width='2'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.PatternFill, geometry.Kind);
        Assert.Equal(128, geometry.Pattern.Opacity);
        Assert.Equal(new SvgColor(0, 128, 0, 255), geometry.Pattern.Stroke.GetValueOrDefault().Color);
    }

    [Fact]
    public void Clips_use_instances_to_their_clip_path_box()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><rect id='r' x='0' y='0' width='8' height='8' fill='red'/><clipPath id='c'><rect x='2' y='2' width='4' height='4'/></clipPath></defs><use href='#r' clip-path='url(#c)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(new SvgRectangle(0, 0, 8, 8, new SvgColor(255, 0, 0, 255), true, null), Assert.Single(root.Rectangles));
        SvgPathFillAssertions.MatchesIntegerBands(root, [new SvgRectangle(2, 2, 4, 4, new SvgColor(255, 0, 0, 255), true, null)]);
    }

    [Fact]
    public void Preserves_the_path_contour_and_its_clip_path_box()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><clipPath id='c'><rect x='2' y='0' width='4' height='8'/></clipPath></defs><path d='M0,0 H8 V8 H0 Z' fill='red' clip-path='url(#c)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgPolygonFill polygon = Assert.Single(root.Geometries).Polygon;
        Assert.Equal(new SvgColor(255, 0, 0, 255), polygon.Color);
        Assert.Equal(new SvgDevicePoint[] { new(0, 0), new(8, 0), new(8, 8), new(0, 8) }, polygon.Points);
        SvgClipBox clip = Assert.IsType<SvgClipBox>(polygon.Clip);
        Assert.Equal(new SvgClipBox(2, 0, 4, 8), clip);
        Assert.Equal(SvgClipAntialiasing.All, clip.Antialiasing);
    }

    [Fact]
    public void Preserves_the_path_contour_and_direct_child_clip_path_box()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><clipPath id='c'><rect x='2' y='0' width='4' height='8'/></clipPath><path d='M0,0 H8 V8 H0 Z' fill='red' clip-path='url(#c)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgPolygonFill polygon = Assert.Single(root.Geometries).Polygon;
        Assert.Equal(new SvgDevicePoint[] { new(0, 0), new(8, 0), new(8, 8), new(0, 8) }, polygon.Points);
        SvgClipBox clip = Assert.IsType<SvgClipBox>(polygon.Clip);
        Assert.Equal(new SvgClipBox(2, 0, 4, 8), clip);
        Assert.Equal(SvgClipAntialiasing.All, clip.Antialiasing);
        Assert.False(root.HasUnsupportedChild);
    }

    [Fact]
    public void Direct_child_clip_path_matches_defs_wrapped_clip()
    {
        ReadOnlyMemory<byte> direct = SvgConverter.ConvertToPng(System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><clipPath id='c'><rect x='2' y='0' width='4' height='8'/></clipPath><path d='M0,0 H8 V8 H0 Z' fill='red' clip-path='url(#c)'/></svg>"), SvgRenderOptions.Default, CancellationToken.None);
        ReadOnlyMemory<byte> wrapped = SvgConverter.ConvertToPng(System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><clipPath id='c'><rect x='2' y='0' width='4' height='8'/></clipPath></defs><path d='M0,0 H8 V8 H0 Z' fill='red' clip-path='url(#c)'/></svg>"), SvgRenderOptions.Default, CancellationToken.None);

        Assert.True(direct.Span.SequenceEqual(wrapped.Span));
    }

    [Fact]
    public void Authored_rectangular_path_accepts_a_multi_contour_clip()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><clipPath id='c'><path d='M0,0 H4 V4 H0 Z M5,5 H8 V8 H5 Z'/></clipPath><path d='M0,0 H8 V8 H0 Z' fill='red' clip-path='url(#c)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);
        SvgPathFillAssertions.MatchesIntegerBands(root, [
            new(0, 0, 4, 4, new(255, 0, 0, 255), true, null),
            new(5, 5, 3, 3, new(255, 0, 0, 255), true, null),
        ]);
    }

    [Fact]
    public void Clips_lines_to_their_clip_path_box()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><clipPath id='c'><rect x='2' y='0' width='4' height='8'/></clipPath></defs><line x1='0' y1='4' x2='8' y2='4' stroke='red' stroke-width='2' clip-path='url(#c)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(new SvgRectangle(2, 3, 4, 2, new SvgColor(255, 0, 0, 255), true, null), Assert.Single(root.Rectangles));
    }

    [Fact]
    public void Clips_symbol_instances_to_their_clip_path_box()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><symbol id='s' viewBox='0 0 8 8'><rect width='8' height='8' fill='red'/></symbol><clipPath id='c'><rect x='2' y='0' width='4' height='8'/></clipPath></defs><use href='#s' x='0' y='0' width='8' height='8' clip-path='url(#c)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(new SvgRectangle(0, 0, 8, 8, new SvgColor(255, 0, 0, 255), true, null), Assert.Single(root.Rectangles));
        SvgPathFillAssertions.MatchesIntegerBands(root, [new SvgRectangle(2, 0, 4, 8, new SvgColor(255, 0, 0, 255), true, null)]);
    }

    [Fact]
    public void Retains_gradient_path_bounds_and_the_separate_clip_box()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='g'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient><clipPath id='c'><rect x='2' y='0' width='4' height='8'/></clipPath></defs><path d='M0,0 H8 V8 H0 Z' fill='url(#g)' clip-path='url(#c)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.GradientPolygon, geometry.Kind);
        Assert.Equal(new SvgRectangle(0, 0, 8, 8, default, true, null), geometry.GradientPolygon.Box);
        Assert.Equal(new SvgClipBox(2, 0, 4, 8), geometry.GradientPolygon.Clip);
    }

    [Fact]
    public void Clips_leaves_to_transformed_clip_path_content()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><clipPath id='c'><rect x='1' y='1' width='4' height='4' transform='translate(1,1)'/></clipPath></defs><rect x='0' y='0' width='8' height='8' fill='red' clip-path='url(#c)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(new SvgRectangle(2, 2, 4, 4, new SvgColor(255, 0, 0, 255), true, null), Assert.Single(root.Rectangles));
    }

    [Fact]
    public void Crops_partially_covering_mask_content()
    {
        RenderBudget budget = new(1_000_000);
        using (SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><mask id='m'><rect x='0' y='0' width='2' height='2' fill='#ffffff'/></mask></defs><rect x='0' y='0' width='8' height='8' fill='red' mask='url(#m)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, budget, CancellationToken.None))
        using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, new(new SvgViewport(8, 8), 1, SvgColor.Transparent, SvgRenderLimits.Default), budget, CancellationToken.None))
        {
            Assert.Equal(new byte[] { 255, 0, 0, 255 }, surface.Pixels.AsSpan((0 * 8 + 0) * 4, 4).ToArray());
            Assert.Equal(new byte[] { 0, 0, 0, 0 }, surface.Pixels.AsSpan((4 * 8 + 4) * 4, 4).ToArray());
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Crops_transformed_mask_content()
    {
        RenderBudget budget = new(1_000_000);
        using (SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><mask id='m'><rect x='0' y='0' width='8' height='8' fill='#ffffff' transform='translate(2,0) scale(2,1)'/></mask></defs><rect x='0' y='0' width='8' height='8' fill='red' mask='url(#m)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, budget, CancellationToken.None))
        using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, new(new SvgViewport(8, 8), 1, SvgColor.Transparent, SvgRenderLimits.Default), budget, CancellationToken.None))
        {
            Assert.Equal(new byte[] { 255, 0, 0, 255 }, surface.Pixels.AsSpan((3 * 8 + 3) * 4, 4).ToArray());
            Assert.Equal(new byte[] { 0, 0, 0, 0 }, surface.Pixels.AsSpan((3 * 8 + 0) * 4, 4).ToArray());
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Crops_partially_covering_mask_content_on_bands()
    {
        RenderBudget budget = new(1_000_000);
        using (SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><mask id='m'><rect x='0' y='0' width='2' height='2' fill='#ffffff'/></mask></defs><path d='M0,0 H8 V8 H0 Z' fill='red' mask='url(#m)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, budget, CancellationToken.None))
        using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, new(new SvgViewport(8, 8), 1, SvgColor.Transparent, SvgRenderLimits.Default), budget, CancellationToken.None))
        {
            Assert.Equal(new byte[] { 255, 0, 0, 255 }, surface.Pixels.AsSpan((0 * 8 + 0) * 4, 4).ToArray());
            Assert.Equal(new byte[] { 0, 0, 0, 0 }, surface.Pixels.AsSpan((4 * 8 + 4) * 4, 4).ToArray());
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Partially_covering_use_masks_preserve_forward_binding_and_coverage()
    {
        byte[] backward = "<svg xmlns='http://www.w3.org/2000/svg'><defs><rect id='r' x='0' y='0' width='8' height='8' fill='red'/><mask id='m'><rect x='0' y='0' width='2' height='2' fill='#ffffff'/></mask></defs><use href='#r' mask='url(#m)'/></svg>"u8.ToArray();
        byte[] forward = "<svg xmlns='http://www.w3.org/2000/svg'><use href='#r' mask='url(#m)'/><defs><rect id='r' x='0' y='0' width='8' height='8' fill='red'/><mask id='m'><rect x='0' y='0' width='2' height='2' fill='#ffffff'/></mask></defs></svg>"u8.ToArray();
        RenderBudget budget = new(1_000_000);
        byte[] expected;
        SvgRenderOptions options = new(new SvgViewport(8, 8), 1, SvgColor.Transparent, SvgRenderLimits.Default);
        using (SvgRoot root = SvgRootParser.Parse(backward, SvgRenderLimits.Default, budget, CancellationToken.None))
        using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, options, budget, CancellationToken.None))
        {
            expected = surface.Pixels.ToArray();
            Assert.Equal(new byte[] { 255, 0, 0, 255 }, expected.AsSpan(0, 4).ToArray());
            Assert.Equal(new byte[] { 0, 0, 0, 0 }, expected.AsSpan((4 * 8 + 4) * 4, 4).ToArray());
        }
        using (SvgRoot root = SvgRootParser.Parse(forward, SvgRenderLimits.Default, budget, CancellationToken.None))
        using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, options, budget, CancellationToken.None))
            Assert.Equal(expected, surface.Pixels);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Crops_mask_content_inside_its_custom_region()
    {
        RenderBudget budget = new(1_000_000);
        using (SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><mask id='m' maskUnits='userSpaceOnUse' x='0' y='0' width='8' height='8'><rect x='0' y='0' width='2' height='2' fill='#ffffff'/></mask></defs><rect x='0' y='0' width='8' height='8' fill='red' mask='url(#m)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, budget, CancellationToken.None))
        using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, new(new SvgViewport(8, 8), 1, SvgColor.Transparent, SvgRenderLimits.Default), budget, CancellationToken.None))
        {
            Assert.Equal(new byte[] { 255, 0, 0, 255 }, surface.Pixels.AsSpan((0 * 8 + 0) * 4, 4).ToArray());
            Assert.Equal(new byte[] { 0, 0, 0, 0 }, surface.Pixels.AsSpan((4 * 8 + 4) * 4, 4).ToArray());
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Renders_nested_viewport_opacity_identical_to_group_opacity()
    {
        byte[] viewport = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><svg x='0' y='0' width='8' height='8' opacity='0.5'><rect x='0' y='0' width='8' height='8' fill='red'/><rect x='2' y='2' width='4' height='4' fill='blue'/></svg></svg>");
        byte[] group = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><g opacity='0.5'><rect x='0' y='0' width='8' height='8' fill='red'/><rect x='2' y='2' width='4' height='4' fill='blue'/></g></svg>");

        ReadOnlyMemory<byte> viewportPng = SvgConverter.ConvertToPng(viewport, SvgRenderOptions.Default, CancellationToken.None);
        ReadOnlyMemory<byte> groupPng = SvgConverter.ConvertToPng(group, SvgRenderOptions.Default, CancellationToken.None);

        Assert.True(viewportPng.Span.SequenceEqual(groupPng.Span));
    }

    [Fact]
    public void Original_nested_viewport_and_group_opacity_compose_separate_layers()
    {
        AssertSolidRectanglePixels("<svg xmlns='http://www.w3.org/2000/svg' opacity='0.5'><svg x='0' y='0' width='8' height='8' opacity='0.5'><rect width='8' height='8' fill='red'/></svg></svg>"u8.ToArray(), new(0, 0, 8, 8, new(255, 0, 0, 64), true, null), 300, 150);
        AssertSolidRectanglePixels("<svg xmlns='http://www.w3.org/2000/svg'><svg x='0' y='0' width='8' height='8' opacity='0.5'><g opacity='0.5'><rect width='8' height='8' fill='red'/></g></svg></svg>"u8.ToArray(), new(0, 0, 8, 8, new(255, 0, 0, 64), true, null), 300, 150);
    }

    [Fact]
    public void Nested_percentage_viewport_resolves_against_default_intrinsic_size()
    {
        // Omitted nested dimensions default to 100 percent of the resolved root output size.
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg' width='100%' height='100%'><svg><rect x='10' y='10' width='20' height='20' fill='red'/></svg></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(new SvgRectangle(10, 10, 20, 20, new SvgColor(255, 0, 0, 255), true, null), Assert.Single(root.Rectangles));
    }

    [Fact]
    public void Nested_percentage_viewport_resolves_against_explicit_viewport()
    {
        // The explicit viewport sizes percentage roots, so omitted nested dimensions match the absolute twin.
        SvgRenderOptions options = new(new SvgViewport(64, 32), 1, new SvgColor(0, 0, 0, 0), SvgRenderLimits.Default);
        byte[] percent = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='100%' height='100%'><svg><rect x='4' y='4' width='8' height='8' fill='red'/></svg></svg>");
        byte[] absolute = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='64' height='32'><svg x='0' y='0' width='64' height='32'><rect x='4' y='4' width='8' height='8' fill='red'/></svg></svg>");

        ReadOnlyMemory<byte> percentPng = SvgConverter.ConvertToPng(percent, options, CancellationToken.None);
        ReadOnlyMemory<byte> absolutePng = SvgConverter.ConvertToPng(absolute, options, CancellationToken.None);

        Assert.True(percentPng.Span.SequenceEqual(absolutePng.Span));
    }

    [Fact]
    public void Nested_percentage_bases_use_viewBox_user_units()
    {
        // Percentage bases resolve in parent user units, keeping scene content in root user space for the single output map.
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 10 10' width='100%' height='100%'><svg x='50%' width='5' height='5'><rect width='5' height='5' fill='red'/></svg></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(new SvgRectangle(5, 0, 5, 5, new SvgColor(255, 0, 0, 255), true, null), Assert.Single(root.Rectangles));
    }

    [Fact]
    public void Omitted_nested_dimensions_use_the_root_viewBox_extent()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 10 10'><svg><rect width='5' height='5' fill='red'/></svg></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(new SvgRectangle(0, 0, 5, 5, new SvgColor(255, 0, 0, 255), true, null), Assert.Single(root.Rectangles));
    }

    [Fact]
    public void Admits_empty_nested_viewports_with_opacity()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><svg x='0' y='0' width='8' height='8' opacity='0.5'/><rect x='0' y='0' width='8' height='8' fill='red'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(new SvgRectangle(0, 0, 8, 8, new SvgColor(255, 0, 0, 255), true, null), Assert.Single(root.Rectangles));
    }

    [Fact]
    public void Lowers_intersecting_contours_with_nonzero_winding()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><path d='M1,1 H6 V6 H1 Z M3,0 H5 V8 H3 Z' fill='red'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgPathFillAssertions.MatchesIntegerBands(root, [
            new SvgRectangle(3, 0, 2, 1, new SvgColor(255, 0, 0, 255), true, null),
            new SvgRectangle(1, 1, 2, 5, new SvgColor(255, 0, 0, 255), true, null),
            new SvgRectangle(3, 1, 2, 5, new SvgColor(255, 0, 0, 255), true, null),
            new SvgRectangle(5, 1, 1, 5, new SvgColor(255, 0, 0, 255), true, null),
            new SvgRectangle(3, 6, 2, 2, new SvgColor(255, 0, 0, 255), true, null),
        ]);
    }

    [Fact]
    public void Lowers_intersecting_contours_with_evenodd_pairing()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><path d='M1,1 H6 V6 H1 Z M3,0 H5 V8 H3 Z' fill='red' fill-rule='evenodd'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgPathFillAssertions.MatchesIntegerBands(root, [
            new SvgRectangle(3, 0, 2, 1, new SvgColor(255, 0, 0, 255), true, null),
            new SvgRectangle(1, 1, 2, 5, new SvgColor(255, 0, 0, 255), true, null),
            new SvgRectangle(5, 1, 1, 5, new SvgColor(255, 0, 0, 255), true, null),
            new SvgRectangle(3, 6, 2, 2, new SvgColor(255, 0, 0, 255), true, null),
        ]);
    }

    [Fact]
    public void Lowers_corner_touching_contours_independently()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><path d='M0,0 H4 V4 H0 Z M4,4 H8 V8 H4 Z' fill='red'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgPathFillAssertions.MatchesIntegerBands(root, [
            new SvgRectangle(0, 0, 4, 4, new SvgColor(255, 0, 0, 255), true, null),
            new SvgRectangle(4, 4, 4, 4, new SvgColor(255, 0, 0, 255), true, null),
        ]);
    }

    [Fact]
    public void Lowers_self_crossing_contours()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><path d='M0,4 H8 V5 H5 V8 H3 V0 H2 V5 H0 Z' fill='red'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgPathFillAssertions.MatchesIntegerBands(root, [
            new SvgRectangle(2, 0, 1, 4, new SvgColor(255, 0, 0, 255), true, null),
            new SvgRectangle(0, 4, 2, 1, new SvgColor(255, 0, 0, 255), true, null),
            new SvgRectangle(3, 4, 5, 1, new SvgColor(255, 0, 0, 255), true, null),
            new SvgRectangle(3, 5, 2, 3, new SvgColor(255, 0, 0, 255), true, null),
        ]);
    }

    [Fact]
    public void Lowers_abutting_rect_contours_to_adjacent_bands()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><path d='M0,0 H4 V4 H0 Z M4,0 H8 V4 H4 Z' fill='red'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgPathFillAssertions.MatchesIntegerBands(root, [
            new SvgRectangle(0, 0, 4, 4, new SvgColor(255, 0, 0, 255), true, null),
            new SvgRectangle(4, 0, 4, 4, new SvgColor(255, 0, 0, 255), true, null),
        ]);
    }

    [Fact]
    public void Lowers_abutting_rect_contours_with_evenodd_pairing()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><path d='M0,0 H4 V4 H0 Z M4,0 H8 V4 H4 Z' fill='red' fill-rule='evenodd'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgPathFillAssertions.MatchesIntegerBands(root, [
            new SvgRectangle(0, 0, 4, 4, new SvgColor(255, 0, 0, 255), true, null),
            new SvgRectangle(4, 0, 4, 4, new SvgColor(255, 0, 0, 255), true, null),
        ]);
    }

    [Fact]
    public void Lowers_opposite_orientation_abut_to_adjacent_bands()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><path d='M0,0 H4 V4 H0 Z M8,0 H4 V4 H8 Z' fill='red'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgPathFillAssertions.MatchesIntegerBands(root, [
            new SvgRectangle(0, 0, 4, 4, new SvgColor(255, 0, 0, 255), true, null),
            new SvgRectangle(4, 0, 4, 4, new SvgColor(255, 0, 0, 255), true, null),
        ]);
    }

    [Fact]
    public void Lowers_retraced_edge_contour_to_a_single_band()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><path d='M0,0 H4 V4 H0 V0 H4 Z' fill='red'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgPathFillAssertions.MatchesIntegerBands(root, [
            new SvgRectangle(0, 0, 4, 4, new SvgColor(255, 0, 0, 255), true, null),
        ]);
    }

    [Fact]
    public void Lowers_vertical_spike_contour_by_cancellation()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><path d='M0,0 H2 V4 V0 H4 V4 H0 Z' fill='red' fill-rule='evenodd'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgPathFillAssertions.MatchesIntegerBands(root, [
            new SvgRectangle(0, 0, 2, 4, new SvgColor(255, 0, 0, 255), true, null),
            new SvgRectangle(2, 0, 2, 4, new SvgColor(255, 0, 0, 255), true, null),
        ]);
    }

    [Fact]
    public void Lowers_vertical_spike_contour_with_nonzero_sweep()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><path d='M0,0 H2 V4 V0 H4 V4 H0 Z' fill='red'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgPathFillAssertions.MatchesIntegerBands(root, [
            new SvgRectangle(0, 0, 2, 4, new SvgColor(255, 0, 0, 255), true, null),
            new SvgRectangle(2, 0, 2, 4, new SvgColor(255, 0, 0, 255), true, null),
        ]);
    }

    [Fact]
    public void Lowers_nested_touching_hole_with_evenodd_pairing()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><path d='M0,0 H10 V10 H0 Z M6,4 H10 V6 H6 Z' fill='red' fill-rule='evenodd'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgPathFillAssertions.MatchesIntegerBands(root, [
            new SvgRectangle(0, 0, 10, 4, new SvgColor(255, 0, 0, 255), true, null),
            new SvgRectangle(0, 4, 6, 2, new SvgColor(255, 0, 0, 255), true, null),
            new SvgRectangle(0, 6, 10, 4, new SvgColor(255, 0, 0, 255), true, null),
        ]);
    }

    [Fact]
    public void Lowers_stacked_identical_contours_to_empty_with_evenodd()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><path d='M0,0 H4 V4 H0 Z M0,0 H4 V4 H0 Z' fill='red' fill-rule='evenodd'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgPathFillAssertions.MatchesIntegerBands(root, []);
    }

    [Fact]
    public void Lowers_triple_covered_bands_with_nonzero_sweep()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><path d='M0,0 H2 V4 H0 Z M0,0 H2 V4 H0 Z M0,0 H2 V4 H0 Z' fill='red'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgPathFillAssertions.MatchesIntegerBands(root, [
            new SvgRectangle(0, 0, 2, 4, new SvgColor(255, 0, 0, 255), true, null),
        ]);
    }

    [Fact]
    public void Lowers_closed_axis_aligned_rectangle_paths()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><path d='M 1,2 H 4 V 6 H 1 Z' fill='red'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgPathFillAssertions.MatchesIntegerBands(root, [new SvgRectangle(1, 2, 3, 4, new SvgColor(255, 0, 0, 255), true, null)]);
    }

    [Fact]
    public void Lowers_paths_with_adjacent_commands_without_materializing_tokens()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><path d='M0,0H3V1H1V3H0Z' fill='red'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgPathFillAssertions.MatchesIntegerBands(root, [
            new SvgRectangle(0, 0, 3, 1, new SvgColor(255, 0, 0, 255), true, null),
            new SvgRectangle(0, 1, 1, 2, new SvgColor(255, 0, 0, 255), true, null),
        ]);
    }

    [Fact]
    public void Lowers_closed_axis_aligned_line_rectangle_paths()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><path d='M 1,2 L 4,2 L 4,6 L 1,6 z' fill='red'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgPathFillAssertions.MatchesIntegerBands(root, [new SvgRectangle(1, 2, 3, 4, new SvgColor(255, 0, 0, 255), true, null)]);
    }

    [Fact]
    public void Lowers_closed_axis_aligned_line_paths_to_non_overlapping_rectangle_bands()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><path d='M 0,0 H 3 V 1 H 1 V 3 H 0 Z' fill='red'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgPathFillAssertions.MatchesIntegerBands(root, [
            new SvgRectangle(0, 0, 3, 1, new SvgColor(255, 0, 0, 255), true, null),
            new SvgRectangle(0, 1, 1, 2, new SvgColor(255, 0, 0, 255), true, null),
        ]);
    }

    [Fact]
    public void Retains_orthogonal_path_commands_as_one_native_fill()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><path d='M 0,0 H 2 V 1 H 0 Z' fill='red'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.PolygonFill, geometry.Kind);
        Assert.NotNull(geometry.AuthoredPath);
        SvgPathFillAssertions.MatchesIntegerBands(root, [new(0, 0, 2, 1, new(255, 0, 0, 255), true, null)]);
    }

    [Fact]
    public void Retains_closed_axis_aligned_polygon_coverage()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><polygon points='0,0 3,0 3,1 1,1 1,3 0,3' fill='red'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.PolygonFill, geometry.Kind);
        SvgAuthoredPath source = Assert.IsType<SvgAuthoredPath>(geometry.AuthoredPath);
        Assert.Equal(7, source.Commands.Length);
        Assert.Equal(SvgAuthoredPathVerb.Close, source.Commands[^1].Verb);
        SvgPathFillAssertions.MatchesIntegerBands(root,
            [new(0, 0, 3, 1, new(255, 0, 0, 255), true, null), new(0, 1, 1, 2, new(255, 0, 0, 255), true, null)]);
    }

    [Fact]
    public void Retains_open_polyline_source_with_closed_fill_coverage()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><polyline points='0,0 3,0 3,1 1,1 1,3 0,3' fill='red'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.PolygonFill, geometry.Kind);
        SvgAuthoredPath source = Assert.IsType<SvgAuthoredPath>(geometry.AuthoredPath);
        Assert.Equal(7, source.Commands.Length);
        Assert.Equal(SvgAuthoredPathVerb.End, source.Commands[^1].Verb);
        SvgPathFillAssertions.MatchesIntegerBands(root,
            [new(0, 0, 3, 1, new(255, 0, 0, 255), true, null), new(0, 1, 1, 2, new(255, 0, 0, 255), true, null)]);
    }

    [Fact]
    public void Admits_self_intersecting_diagonal_polygons()
    {
        // Single-contour nonzero crossings lower like simple contours.
        SvgRoot root = SvgRootParser.Parse(System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg'><polygon points='0,0 2,2 2,0 0,2'/></svg>"), SvgRenderLimits.Default, CancellationToken.None);

        Assert.NotEmpty(root.Geometries);
        Assert.False(root.HasUnsupportedChild);
    }

    [Fact]
    public void Lowers_self_intersecting_orthogonal_contours()
    {
        using SvgRoot polygon = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><polygon points='0,0 3,0 3,3 1,3 1,1 4,1 4,4 0,4'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);
        using SvgRoot path = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><path d='M 0,0 H 3 V 3 H 1 V 1 H 4 V 4 H 0 Z'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgRectangle[] expected =
        [
            new SvgRectangle(0, 0, 3, 1, new SvgColor(0, 0, 0, 255), true, null),
            new SvgRectangle(0, 1, 1, 2, new SvgColor(0, 0, 0, 255), true, null),
            new SvgRectangle(1, 1, 2, 2, new SvgColor(0, 0, 0, 255), true, null),
            new SvgRectangle(3, 1, 1, 2, new SvgColor(0, 0, 0, 255), true, null),
            new SvgRectangle(0, 3, 4, 1, new SvgColor(0, 0, 0, 255), true, null),
        ];
        SvgPathFillAssertions.MatchesIntegerBands(polygon, expected);
        SvgPathFillAssertions.MatchesIntegerBands(path, expected);
    }

    [Fact]
    public void Lowers_evenodd_counter_path_to_bands_with_hole()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><path d='M8,8 H56 V56 H8 Z M20,20 V44 H44 V20 Z' fill-rule='evenodd' fill='red'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgPathFillAssertions.MatchesIntegerBands(root, [
            new SvgRectangle(8, 8, 48, 12, new SvgColor(255, 0, 0, 255), true, null),
            new SvgRectangle(8, 20, 12, 24, new SvgColor(255, 0, 0, 255), true, null),
            new SvgRectangle(44, 20, 12, 24, new SvgColor(255, 0, 0, 255), true, null),
            new SvgRectangle(8, 44, 48, 12, new SvgColor(255, 0, 0, 255), true, null),
        ]);
    }

    [Fact]
    public void Lowers_same_winding_nonzero_counter_path_to_solid_bands()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><path d='M8,8 H56 V56 H8 Z M20,20 H44 V44 H20 Z' fill-rule='nonzero' fill='red'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgPathFillAssertions.MatchesIntegerBands(root, [
            new SvgRectangle(8, 8, 48, 12, new SvgColor(255, 0, 0, 255), true, null),
            new SvgRectangle(8, 20, 12, 24, new SvgColor(255, 0, 0, 255), true, null),
            new SvgRectangle(20, 20, 24, 24, new SvgColor(255, 0, 0, 255), true, null),
            new SvgRectangle(44, 20, 12, 24, new SvgColor(255, 0, 0, 255), true, null),
            new SvgRectangle(8, 44, 48, 12, new SvgColor(255, 0, 0, 255), true, null),
        ]);
    }

    [Fact]
    public void Lowers_overlapping_contours_with_nonzero_winding()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><path d='M0,0 H4 V4 H0 Z M2,2 H6 V6 H2 Z' fill='red'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgPathFillAssertions.MatchesIntegerBands(root, [
            new SvgRectangle(0, 0, 4, 2, new SvgColor(255, 0, 0, 255), true, null),
            new SvgRectangle(0, 2, 2, 2, new SvgColor(255, 0, 0, 255), true, null),
            new SvgRectangle(2, 2, 2, 2, new SvgColor(255, 0, 0, 255), true, null),
            new SvgRectangle(4, 2, 2, 2, new SvgColor(255, 0, 0, 255), true, null),
            new SvgRectangle(2, 4, 4, 2, new SvgColor(255, 0, 0, 255), true, null),
        ]);
    }

    [Fact]
    public void Admits_trailing_unclosed_path_subpath()
    {
        using SvgRoot closed = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><path d='M0,0 H2 V2 H0 Z M4,4 H6 V6 H4 Z'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);
        using SvgRoot open = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><path d='M0,0 H2 V2 H0 Z M4,4 H6 V6 H4'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgPathFillAssertions.Equivalent(closed, open);
    }

    [Fact]
    public void Admits_nonfinal_unclosed_path_subpath()
    {
        using SvgRoot closed = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><path d='M0,0 H2 V2 H0 Z M4,4 H6 V6 H4 Z'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);
        using SvgRoot open = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><path d='M0,0 H2 V2 H0 M4,4 H6 V6 H4 Z'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgPathFillAssertions.Equivalent(closed, open);
    }

    [Fact]
    public void Admits_explicit_return_to_start_path_subpath()
    {
        using SvgRoot closed = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><path d='M0,0 H2 V2 H0 Z M4,4 H6 V6 H4 Z'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);
        using SvgRoot returned = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><path d='M0,0 H2 V2 H0 V0 M4,4 H6 V6 H4 Z'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgPathFillAssertions.Equivalent(closed, returned);
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
        using SvgRoot relative = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><path d='m 1,2 h 3 v 4 h -3 z' fill='red'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);
        using SvgRoot absolute = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><path d='M 1,2 H 4 V 6 H 1 Z' fill='red'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgPathFillAssertions.Equivalent(absolute, relative);
        SvgPathFillAssertions.MatchesIntegerBands(relative, [
            new SvgRectangle(1, 2, 3, 4, new SvgColor(255, 0, 0, 255), true, null),
        ]);
    }

    [Fact]
    public void Lowers_implicit_lineto_path_to_same_bands_as_explicit()
    {
        using SvgRoot implicitPath = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><path d='M0,0 3,0 3,1 1,1 1,3 0,3Z' fill='red'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgPathFillAssertions.MatchesIntegerBands(implicitPath, [
            new SvgRectangle(0, 0, 3, 1, new SvgColor(255, 0, 0, 255), true, null),
            new SvgRectangle(0, 1, 1, 2, new SvgColor(255, 0, 0, 255), true, null),
        ]);
    }

    [Fact]
    public void Lowers_compact_repeated_commands_to_same_bands()
    {
        using SvgRoot compact = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><path d='M0,0H2 3V1H0Z' fill='red'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgPathFillAssertions.MatchesIntegerBands(compact, [
            new SvgRectangle(0, 0, 3, 1, new SvgColor(255, 0, 0, 255), true, null),
        ]);
    }

    [Fact]
    public void Lowers_relative_counter_subpath_to_the_same_hole()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><path d='M8,8H56V56H8Z m12,12 v24 h24 v-24 Z' fill-rule='evenodd' fill='red'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgPathFillAssertions.MatchesIntegerBands(root, [
            new SvgRectangle(8, 8, 48, 12, new SvgColor(255, 0, 0, 255), true, null),
            new SvgRectangle(8, 20, 12, 24, new SvgColor(255, 0, 0, 255), true, null),
            new SvgRectangle(44, 20, 12, 24, new SvgColor(255, 0, 0, 255), true, null),
            new SvgRectangle(8, 44, 48, 12, new SvgColor(255, 0, 0, 255), true, null),
        ]);
    }

    [Fact]
    public void Relative_touch_union_retains_directed_contours()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><path d='m0,0 l2,0 l0,2 l-2,0 z m2,1 l2,-1 l0,2 z'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Null(geometry.Polygon.Signs);
        Assert.Equal(2, geometry.Polygon.ContourEnds.Length);
        Assert.NotNull(geometry.AuthoredPath);
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
        SvgRenderLimits limits = new(1_000_000, 1_024, 16, 16, 1_024, 16, 16, 10_000);
        RenderBudget measured = new(limits.MaximumAccountedBytes);
        using (SvgRootParser.Parse(svg, limits, measured, CancellationToken.None)) { }
        Assert.Equal(0, measured.Snapshot().CurrentBytes);
        RenderBudget insufficient = new(measured.Snapshot().PeakBytes - 1);
        SvgException exception = Assert.Throws<SvgException>(() => SvgRootParser.Parse(svg, limits, insufficient, CancellationToken.None));
        Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode);
        Assert.Equal(0, insufficient.Snapshot().CurrentBytes);
        using SvgRoot root = SvgRootParser.Parse(svg, limits, new RenderBudget(measured.Snapshot().PeakBytes), CancellationToken.None);
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
    public void Parses_px_and_percentage_dimensions_but_keeps_other_relative_unit_suffixes_explicitly_unsupported()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg' width='12.5px' height='7px'/>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);
        SvgRoot percent = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg' width='100%' height='7'/>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);
        SvgException relativeUnit = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg' width='1vmin' height='7'/>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None));

        Assert.Equal(12.5, root.Width?.Value);
        Assert.Equal(100, percent.Width?.Value);
        Assert.True(percent.Width?.IsPercentage);
        Assert.Equal(7, percent.Height?.Value);
        Assert.False(percent.Height?.IsPercentage);
        Assert.Equal(SvgErrorCode.UnsupportedFeature, relativeUnit.ErrorCode);
    }

    [Fact]
    public void Inline_styles_override_presentation_attributes_and_inherit_through_groups()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg' fill='red' style='color: lime'><g fill='blue' style='fill: currentColor; fill-opacity: 0.5'><rect width='1' height='1'/></g><rect width='1' height='1' fill='red' style='fill: blue; opacity: 0.5'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Collection(root.Rectangles,
            rectangle => Assert.Equal(new SvgColor(0, 255, 0, 128), rectangle.Color),
            rectangle => Assert.Equal(new SvgColor(0, 0, 255, 255), rectangle.Color));
        SvgRecordedPaintAssertions.ElementOpacity(root, new(128, 0.5f));
    }

    [Fact]
    public void Rejects_inline_styles_that_are_not_in_the_supported_rectangle_subset()
    {
        SvgException unknownProperty = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><rect width='1' height='1' style='stroke-dashoffset: foo'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None));
        Assert.Equal(SvgErrorCode.UnsupportedFeature, unknownProperty.ErrorCode);
    }

    [Fact]
    public void Represents_group_opacity_as_an_isolated_scene_command()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><g opacity='0.5'><rect width='1' height='1'/></g></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Collection(root.Commands,
            command => SvgRecordedPaintAssertions.OpacityScope(command, new(128, 0.5f)),
            command => Assert.Equal(new SvgDrawCommand(SvgDrawCommandKind.Geometry, 0, 255, null, -1, 255), command),
            command => Assert.Equal(new SvgDrawCommand(SvgDrawCommandKind.EndGroup, -1, 255, null, -1, 255), command));
        Assert.Equal(SvgGeometryKind.Rectangle, Assert.Single(root.Geometries).Kind);
    }

    [Fact]
    public void Represents_sequential_group_opacity_scopes()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><g opacity='0.5'><rect width='1' height='1'/></g><g opacity='0.5'><rect width='1' height='1'/></g></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Collection(root.Commands,
            command => SvgRecordedPaintAssertions.OpacityScope(command, new(128, 0.5f)),
            command => Assert.Equal(new SvgDrawCommand(SvgDrawCommandKind.Geometry, 0, 255, null, -1, 255), command),
            command => Assert.Equal(new SvgDrawCommand(SvgDrawCommandKind.EndGroup, -1, 255, null, -1, 255), command),
            command => SvgRecordedPaintAssertions.OpacityScope(command, new(128, 0.5f)),
            command => Assert.Equal(new SvgDrawCommand(SvgDrawCommandKind.Geometry, 1, 255, null, -1, 255), command),
            command => Assert.Equal(new SvgDrawCommand(SvgDrawCommandKind.EndGroup, -1, 255, null, -1, 255), command));
    }

    [Fact]
    public void Represents_root_opacity_as_an_isolated_scene_command()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg' opacity='0.5'><rect width='1' height='1'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Collection(root.Commands,
            command => SvgRecordedPaintAssertions.OpacityScope(command, new(128, 0.5f)),
            command => Assert.Equal(new SvgDrawCommand(SvgDrawCommandKind.Geometry, 0, 255, null, -1, 255), command),
            command => Assert.Equal(new SvgDrawCommand(SvgDrawCommandKind.EndGroup, -1, 255, null, -1, 255), command));
    }

    [Fact]
    public void Original_nested_group_opacity_composes_separate_layers()
    {
        AssertSolidRectanglePixels("<svg xmlns='http://www.w3.org/2000/svg'><g opacity='0.5'><g opacity='0.5'><rect width='1' height='1'/></g></g></svg>"u8.ToArray(), new(0, 0, 1, 1, new(0, 0, 0, 64), true, null), 300, 150);
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
        using SvgRoot root = SvgRootParser.Parse(svg, limits, new RenderBudget(workspace + SvgSceneBuilder.RecordedPaintOwnerBytes), CancellationToken.None);

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
        state.StyleRules.Add(new CssRule(".selected", new(0, 1, 0), default, false));
        SvgRootAttributes root = new(new SvgInheritedPresentation(SvgPaint.Default, true, true, SvgAffineTransform.Identity), SvgInheritedTextStyle.Default, SvgInheritedStrokeStyle.Default, null, null, null, null);

        SvgException exception = Assert.Throws<SvgException>(() => SvgRenderedLeafParser.TryParse(reader, true, root, state, SvgRenderLimits.Default, budget, new Dictionary<string, SvgColor>(), null, workCounter, CancellationToken.None));

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
        CssRule rule = new(".selected", new(0, 1, 0), default, false);
        RenderBudget budget = new(10_000);
        ParserWorkCounter work = new(10);
        using BudgetedParserList<CssRule> rules = new(budget, SvgMemoryCategory.Parser, work, CancellationToken.None);
        using SvgCssAncestorContext ancestors = new(rules, budget, work, CancellationToken.None);

        SvgCssElement element = SvgCssElement.Read(reader);
        SvgException exception = Assert.Throws<SvgException>(() => rule.Matches(element, ancestors, work, CancellationToken.None));

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

        Assert.NotNull(Assert.Single(root.Geometries).AuthoredPath);
        Assert.Equal(0, budget.Snapshot().BytesByCategory[(int)SvgMemoryCategory.Geometry]);
    }

    [Fact]
    public void Twenty_circles_report_exact_budget_peak_before_denial()
    {
        StringBuilder discs = new("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'>");
        for (int index = 0; index < 20; index++)
        {
            discs.Append("<circle cx='4' cy='4' r='3' fill='red'/>");
        }
        discs.Append("</svg>");
        byte[] svg = System.Text.Encoding.UTF8.GetBytes(discs.ToString());
        RenderBudget budget = new(100_000_000);
        using SvgRoot root = SvgRootParser.Parse(svg, SvgRenderLimits.Default, budget, CancellationToken.None);
        long peak = budget.Snapshot().PeakBytes;
        RenderBudget denied = new(peak - 1);
        SvgException exception = Assert.Throws<SvgException>(() =>
        {
            using SvgRoot deniedRoot = SvgRootParser.Parse(svg, SvgRenderLimits.Default, denied, CancellationToken.None);
        });

        Assert.NotEmpty(root.Geometries);
        Assert.True(peak > 0);
        Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode);
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
    public void Offsets_a_polygon_use_instance_by_its_nested_viewport_origin()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><polygon id='p' points='0,0 4,0 0,4' fill='red'/></defs><svg x='8' y='8' width='32' height='32'><use href='#p'/></svg></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.PolygonFill, geometry.Kind);
        Assert.Equal(new SvgDevicePoint(8, 8), geometry.Polygon.Points[0]);
        Assert.Equal(new SvgDevicePoint(12, 8), geometry.Polygon.Points[1]);
        Assert.Equal(new SvgDevicePoint(8, 12), geometry.Polygon.Points[2]);
    }

    [Fact]
    public void Offsets_nested_viewport_content_by_the_viewport_origin()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><svg x='8' y='8' width='32' height='32'><rect width='4' height='4' fill='red'/></svg></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(new SvgRectangle(8, 8, 4, 4, new SvgColor(255, 0, 0, 255), true, null), Assert.Single(root.Rectangles));
    }

    [Fact]
    public void Retains_the_original_line_band_and_its_nested_viewport_clip()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><svg x='8' y='8' width='32' height='32'><line x1='0' y1='10' x2='64' y2='10' stroke='red' stroke-width='2'/></svg></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.OrthogonalBand, geometry.Kind);
        Assert.Equal(new SvgRectangle(8, 17, 64, 2, new SvgColor(255, 0, 0, 255), true, null), geometry.Rectangle with { Clip = null });
        SvgClipBox clip = Assert.IsType<SvgClipBox>(geometry.Rectangle.Clip);
        Assert.Equal((8d, 8d, 32d, 32d), (clip.X, clip.Y, clip.Width, clip.Height));
    }

    [Fact]
    public void Keeps_a_stroked_rectangle_fully_inside_its_nested_viewport()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><svg x='8' y='8' width='32' height='32'><rect x='10' y='10' width='4' height='4' fill='red' stroke='blue' stroke-width='2'/></svg></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(new SvgRectangle(18, 18, 4, 4, new SvgColor(255, 0, 0, 255), true, new SvgStroke(new SvgColor(0, 0, 255, 255), 2)), Assert.Single(root.Rectangles));
    }
    [Fact]
    public void Keeps_a_stroked_rect_use_fully_inside_its_nested_viewport()
    {
        RenderBudget budget = new(1_000_000);
        using (SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><rect id='r' x='10' y='10' width='4' height='4' fill='red' stroke='blue' stroke-width='2'/></defs><svg x='8' y='8' width='32' height='32'><use href='#r'/></svg></svg>"u8.ToArray(), SvgRenderLimits.Default, budget, CancellationToken.None))
        using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, new(new SvgViewport(32, 32), 1, SvgColor.Transparent, SvgRenderLimits.Default), budget, CancellationToken.None))
        {
            for (int y = 0; y < 32; y++)
                for (int x = 0; x < 32; x++)
                {
                    byte[] expected = x >= 19 && x < 21 && y >= 19 && y < 21
                        ? [255, 0, 0, 255]
                        : x >= 17 && x < 23 && y >= 17 && y < 23 ? [0, 0, 255, 255] : [0, 0, 0, 0];
                    Assert.Equal(expected, surface.Pixels.AsSpan((y * 32 + x) * 4, 4).ToArray());
                }
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Clips_a_stroked_rect_use_crossing_a_nested_viewport_boundary()
    {
        RenderBudget budget = new(1_000_000);
        using (SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><rect id='r' x='0' y='0' width='8' height='8' fill='red' stroke='blue' stroke-width='2'/></defs><svg x='8' y='8' width='4' height='4'><use href='#r'/></svg></svg>"u8.ToArray(), SvgRenderLimits.Default, budget, CancellationToken.None))
        using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, new(new SvgViewport(16, 16), 1, SvgColor.Transparent, SvgRenderLimits.Default), budget, CancellationToken.None))
        {
            for (int y = 0; y < 16; y++)
                for (int x = 0; x < 16; x++)
                {
                    byte[] expected = x >= 8 && x < 12 && y >= 8 && y < 12
                        ? x == 8 || y == 8 ? [0, 0, 255, 255] : [255, 0, 0, 255]
                        : [0, 0, 0, 0];
                    Assert.Equal(expected, surface.Pixels.AsSpan((y * 16 + x) * 4, 4).ToArray());
                }
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Original_stroke_crossing_viewport_preserves_source_and_physical_clip()
    {
        RenderBudget budget = new(8_000_000);
        using (SvgRoot scene = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><svg x='8' y='8' width='32' height='32'><rect width='64' height='64' fill='red' stroke='blue' stroke-width='2'/></svg></svg>"u8.ToArray(), SvgRenderLimits.Default, budget, CancellationToken.None))
        using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(scene, SvgRenderOptions.Default, budget, CancellationToken.None))
        {
            Assert.Equal(300, surface.Width);
            Assert.Equal(150, surface.Height);
            for (int y = 0; y < surface.Height; y++)
            for (int x = 0; x < surface.Width; x++)
            {
                SvgColor expected = x >= 8 && x < 40 && y >= 8 && y < 40
                    ? x == 8 || y == 8 ? new(0, 0, 255, 255) : new(255, 0, 0, 255) : SvgColor.Transparent;
                int offset = (y * surface.Width + x) * 4;
                Assert.Equal(expected, new SvgColor(surface.Pixels[offset], surface.Pixels[offset + 1], surface.Pixels[offset + 2], surface.Pixels[offset + 3]));
            }
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
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
    public void Applies_a_fill_override_to_an_unstyled_polygon()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><polygon id='p' points='0,0 8,0 0,8'/></defs><use href='#p' fill='blue'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.PolygonFill, geometry.Kind);
        Assert.Equal(new SvgColor(0, 0, 255, 255), geometry.Polygon.Color);
    }

    [Fact]
    public void Applies_fill_override_to_retained_polygon_source()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><polygon id='p' points='0,0 4,0 4,4 0,4'/></defs><use href='#p' fill='blue'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.PolygonFill, geometry.Kind);
        Assert.Equal(new SvgColor(0, 0, 255, 255), geometry.Polygon.Color);
        Assert.NotNull(geometry.AuthoredPath);
        SvgPathFillAssertions.MatchesIntegerBands(root, [new(0, 0, 4, 4, new(0, 0, 255, 255), true, null)]);
    }

    [Fact]
    public void Use_fill_preserves_the_authored_fill_and_stroke_of_a_polygon_pair()
    {
        byte[] input = "<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><polygon id='p' points='0,0 8,0 0,8' fill='red' stroke='blue' stroke-width='2'/></defs><use href='#p' fill='green'/></svg>"u8.ToArray();
        byte[] direct = "<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><polygon points='0,0 8,0 0,8' fill='red' stroke='blue' stroke-width='2'/></svg>"u8.ToArray();
        Assert.Equal(SvgConverter.ConvertToPng(direct, SvgRenderOptions.Default, CancellationToken.None).ToArray(), SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None).ToArray());
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
    public void Applies_forward_masks_to_groups()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><g mask='url(#later)'><rect x='0' y='0' width='8' height='8' fill='red'/></g><defs><mask id='later'><rect x='0' y='0' width='8' height='8' fill='#ffffff'/></mask></defs></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(new SvgRectangle(0, 0, 8, 8, new SvgColor(255, 0, 0, 255), true, null), Assert.Single(root.Rectangles));
        SvgLeafMaskAssertions.UniformGroupScope(root, 255, null);
    }

    [Fact]
    public void Isolates_gray_forward_masks_on_groups()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><g mask='url(#later)'><rect x='0' y='0' width='8' height='8' fill='red'/></g><defs><mask id='later'><rect x='0' y='0' width='8' height='8' fill='#808080'/></mask></defs></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(new SvgRectangle(0, 0, 8, 8, new SvgColor(255, 0, 0, 255), true, null), Assert.Single(root.Rectangles));
        SvgDrawCommand begin = Assert.Single(root.Commands, command => command.Kind == SvgDrawCommandKind.BeginGroup);
        Assert.Equal(255, begin.Opacity);
        Assert.Equal(128, begin.MaskAlpha);
        Assert.Contains(root.Commands, command => command.Kind == SvgDrawCommandKind.EndGroup);
    }

    [Fact]
    public void Rejects_never_defined_masks_on_groups()
    {
        SvgException exception = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><g mask='url(#missing)'><rect x='0' y='0' width='8' height='8' fill='red'/></g></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, exception.ErrorCode);
    }

    [Fact]
    public void Forward_group_masks_compose_after_nested_opacity()
    {
        RenderBudget budget = new(1_000_000);
        using (SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><g mask='url(#later)'><g opacity='0.5'><rect x='0' y='0' width='8' height='8' fill='red'/></g></g><defs><mask id='later'><rect x='0' y='0' width='8' height='8' fill='#808080'/></mask></defs></svg>"u8.ToArray(), SvgRenderLimits.Default, budget, CancellationToken.None))
        using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, new(new SvgViewport(8, 8), 1, SvgColor.Transparent, SvgRenderLimits.Default), budget, CancellationToken.None))
        {
            Assert.Equal(5, root.Commands.Length);
            Assert.Equal(255, root.Commands[0].Opacity);
            Assert.Equal(128, root.Commands[0].MaskAlpha);
            Assert.Equal(128, root.Commands[1].Opacity);
            Assert.Equal(255, root.Commands[1].MaskAlpha);
            Assert.NotEqual(root.Commands[0].RecordingScope, root.Commands[1].RecordingScope);
            for (int pixel = 0; pixel < 64; pixel++)
                Assert.Equal(new byte[] { 255, 0, 0, 64 }, surface.Pixels.AsSpan(pixel * 4, 4).ToArray());
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Original_forward_mask_composes_nested_group_opacity()
    {
        AssertSolidRectanglePixels("<svg xmlns='http://www.w3.org/2000/svg'><g opacity='0.5' mask='url(#later)'><g opacity='0.5'><rect x='0' y='0' width='8' height='8' fill='red'/></g></g><defs><mask id='later'><rect x='0' y='0' width='8' height='8' fill='#808080'/></mask></defs></svg>"u8.ToArray(), new(0, 0, 8, 8, new(255, 0, 0, 32), true, null), 300, 150);
    }

    [Fact]
    public void Crops_forward_masks_on_groups_with_uncovered_children()
    {
        RenderBudget budget = new(1_000_000);
        using (SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><g mask='url(#later)'><rect x='0' y='0' width='8' height='8' fill='red'/></g><defs><mask id='later'><rect x='0' y='0' width='2' height='2' fill='#ffffff'/></mask></defs></svg>"u8.ToArray(), SvgRenderLimits.Default, budget, CancellationToken.None))
        using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, new(new SvgViewport(8, 8), 1, SvgColor.Transparent, SvgRenderLimits.Default), budget, CancellationToken.None))
        {
            Assert.Equal(new byte[] { 255, 0, 0, 255 }, surface.Pixels.AsSpan((0 * 8 + 0) * 4, 4).ToArray());
            Assert.Equal(new byte[] { 0, 0, 0, 0 }, surface.Pixels.AsSpan((4 * 8 + 4) * 4, 4).ToArray());
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Rejects_text_in_forward_masked_groups()
    {
        SvgException exception = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><g mask='url(#later)'><text x='0' y='8'>Hi</text></g><defs><mask id='later'><rect x='0' y='0' width='8' height='8' fill='#ffffff'/></mask></defs></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, exception.ErrorCode);
    }

    [Fact]
    public void Retains_opacity_and_forward_masks_as_separate_layer_operations()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><g opacity='0.5' mask='url(#later)'><rect x='0' y='0' width='8' height='8' fill='red'/></g><defs><mask id='later'><rect x='0' y='0' width='8' height='8' fill='#808080'/></mask></defs></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(new SvgRectangle(0, 0, 8, 8, new SvgColor(255, 0, 0, 255), true, null), Assert.Single(root.Rectangles));
        SvgDrawCommand begin = Assert.Single(root.Commands, command => command.Kind == SvgDrawCommandKind.BeginGroup);
        Assert.Equal(128, begin.Opacity);
        Assert.Equal(128, begin.MaskAlpha);
    }

    [Fact]
    public void Applies_forward_custom_region_masks_on_groups()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><g mask='url(#later)'><rect x='0' y='0' width='8' height='8' fill='red'/></g><defs><mask id='later' maskUnits='userSpaceOnUse' x='2' y='2' width='4' height='4'><rect x='0' y='0' width='8' height='8' fill='#ffffff'/></mask></defs></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgDrawCommand begin = Assert.Single(root.Commands, command => command.Kind == SvgDrawCommandKind.BeginGroup);
        Assert.Equal(255, begin.Opacity);
        Assert.Equal(new SvgClipBox(2, 2, 4, 4), begin.MaskRegion);
        Assert.Contains(root.Commands, command => command.Kind == SvgDrawCommandKind.EndGroup);
    }

    [Fact]
    public void Applies_forward_clips_with_forward_masks_on_groups()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><g clip-path='url(#c)' mask='url(#m)'><rect x='0' y='0' width='8' height='8' fill='red'/></g><defs><clipPath id='c'><rect x='2' y='2' width='4' height='4'/></clipPath><mask id='m'><rect x='0' y='0' width='8' height='8' fill='#808080'/></mask></defs></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(new SvgRectangle(0, 0, 8, 8, new SvgColor(255, 0, 0, 255), true, null), Assert.Single(root.Rectangles));
        Assert.Collection(root.Commands.Where(command => command.Kind == SvgDrawCommandKind.BeginGroup),
            clip =>
            {
                Assert.Equal(SvgLayerRegionKind.AffineViewport, clip.RegionKind);
                Assert.Equal(new SvgClipBox(2, 2, 4, 4), clip.MaskRegion);
                Assert.True(clip.MaskContentIndex >= 0);
                Assert.Equal(255, clip.Opacity);
                Assert.Equal(255, clip.MaskAlpha);
            },
            mask =>
            {
                Assert.Equal(SvgLayerRegionKind.Mask, mask.RegionKind);
                Assert.Equal(255, mask.Opacity);
                Assert.Equal(128, mask.MaskAlpha);
            });
        Assert.Contains(root.Commands, command => command.Kind == SvgDrawCommandKind.EndGroup);
    }

    [Fact]
    public void Clips_a_group_to_its_clip_path_box()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><clipPath id='window'><rect x='4' y='4' width='8' height='8'/></clipPath></defs><g clip-path='url(#window)'><rect width='16' height='16' fill='red'/></g></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(new SvgRectangle(4, 4, 8, 8, new SvgColor(255, 0, 0, 255), true, null), Assert.Single(root.Rectangles));
    }

    [Fact]
    public void Clips_forward_clip_paths_on_groups()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><g clip-path='url(#later)'><rect width='16' height='16' fill='red'/></g><rect x='0' y='0' width='2' height='2' fill='blue'/><defs><clipPath id='later'><rect x='4' y='4' width='8' height='8'/></clipPath></defs></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(2, root.Geometries.Length);
        Assert.Equal(new SvgRectangle(0, 0, 16, 16, new SvgColor(255, 0, 0, 255), true, null) { Clip = new(4, 4, 8, 8) }, root.Geometries[0].Rectangle);
        Assert.Equal(new SvgRectangle(0, 0, 2, 2, new SvgColor(0, 0, 255, 255), true, null), root.Geometries[1].Rectangle);
    }

    [Fact]
    public void Clips_forward_clip_paths_on_nested_groups()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><g clip-path='url(#later)'><g><rect width='16' height='16' fill='red'/></g></g><defs><clipPath id='later'><rect x='4' y='4' width='8' height='8'/></clipPath></defs></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(new SvgRectangle(4, 4, 8, 8, new SvgColor(255, 0, 0, 255), true, null), Assert.Single(root.Rectangles));
    }

    [Fact]
    public void Rejects_never_defined_clip_paths_on_groups()
    {
        SvgException exception = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><g clip-path='url(#missing)'><rect width='16' height='16' fill='red'/></g></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, exception.ErrorCode);
    }
    [Fact]
    public void Clips_forward_bounding_box_clips_on_groups()
    {
        AssertSolidRectanglePixels("<svg xmlns='http://www.w3.org/2000/svg'><g clip-path='url(#later)'><rect width='16' height='16' fill='red'/></g><defs><clipPath id='later' clipPathUnits='objectBoundingBox'><rect x='0' y='0' width='1' height='1'/></clipPath></defs></svg>"u8.ToArray(), new(0, 0, 16, 16, new(255, 0, 0, 255), true, null), 300, 150);
    }

    [Fact]
    public void Clips_forward_clips_on_masked_groups()
    {
        AssertSolidRectanglePixels("<svg xmlns='http://www.w3.org/2000/svg'><defs><mask id='fade'><rect x='0' y='0' width='16' height='16' fill='#ffffff'/></mask></defs><g mask='url(#fade)' clip-path='url(#later)'><rect width='16' height='16' fill='red'/></g><defs><clipPath id='later'><rect x='4' y='4' width='8' height='8'/></clipPath></defs></svg>"u8.ToArray(), new(4, 4, 8, 8, new(255, 0, 0, 255), true, null), 300, 150);
    }

    [Fact]
    public void Original_forward_group_clip_maps_scaled_viewport()
    {
        AssertSolidRectanglePixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><svg x='0' y='0' width='16' height='16' viewBox='0 0 8 8'><g clip-path='url(#later)'><rect width='8' height='8' fill='red'/></g></svg><defs><clipPath id='later'><rect x='2' y='2' width='4' height='4'/></clipPath></defs></svg>"u8.ToArray(), new(4, 4, 8, 8, new(255, 0, 0, 255), true, null), 16, 16);
    }

    [Fact]
    public void Original_enclosing_group_clip_stays_outside_child_viewport_map()
    {
        AssertSolidRectanglePixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><g clip-path='url(#later)'><svg x='0' y='0' width='16' height='16' viewBox='0 0 8 8'><rect width='8' height='8' fill='red'/></svg></g><defs><clipPath id='later'><rect x='2' y='2' width='4' height='4'/></clipPath></defs></svg>"u8.ToArray(), new(2, 2, 4, 4, new(255, 0, 0, 255), true, null), 16, 16);
    }

    [Fact]
    public void Clips_forward_clip_paths_on_groups_with_use_instances()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><g clip-path='url(#c)'><use href='#p'/></g><rect x='0' y='0' width='2' height='2' fill='blue'/><defs><polygon id='p' points='0,0 8,0 8,4 4,4 4,8 0,8' fill='red'/><clipPath id='c'><rect x='2' y='2' width='4' height='4'/></clipPath></defs></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(2, root.Geometries.Length);
        Assert.Equal(SvgGeometryKind.PolygonFill, root.Geometries[0].Kind);
        Assert.Equal(new SvgClipBox(2, 2, 4, 4), root.Geometries[0].Polygon.Clip);
        Assert.Equal(new SvgRectangle(0, 0, 2, 2, new(0, 0, 255, 255), true, null), root.Geometries[1].Rectangle);
        SvgPathFillAssertions.MatchesIntegerBands(root,
            [new(2, 2, 4, 2, new(255, 0, 0, 255), true, null), new(2, 4, 2, 2, new(255, 0, 0, 255), true, null),
             new(0, 0, 2, 2, new(0, 0, 255, 255), true, null)]);
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
    public void Root_fallback_dimensions_resolve_nested_percentages()
    {
        using SvgRoot viewport = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><svg width='50%' height='50%'><rect width='8' height='8' fill='red'/></svg></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);
        using SvgRoot use = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><rect id='tile' width='8' height='8' fill='red'/></defs><use href='#tile' width='50%'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);
        SvgRectangle expected = new(0, 0, 8, 8, new SvgColor(255, 0, 0, 255), true, null);
        Assert.Equal(expected, Assert.Single(viewport.Rectangles));
        Assert.Equal(expected, Assert.Single(use.Rectangles));
        Assert.Equal(SvgErrorCode.MalformedInput, Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><rect id='tile' width='8' height='8' fill='red'/></defs><use href='#tile' width='50%'/></svg></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None)).ErrorCode);
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
    public void Renders_strokes_in_scaled_viewports()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg' width='64' height='64'><svg x='8' y='8' width='48' height='48' viewBox='0 0 24 24'><rect x='0' y='0' width='8' height='8' fill='red' stroke='blue' stroke-width='2'/></svg></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.False(root.HasUnsupportedChild);
        Assert.NotEmpty(root.Geometries);
    }

    [Fact]
    public void Scaled_viewport_lines_retain_the_source_stroke_and_viewport_map()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg' width='64' height='64'><svg x='8' y='8' width='48' height='48' viewBox='0 0 24 24'><line x1='0' y1='0' x2='8' y2='0' stroke='red' stroke-width='2'/></svg></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);
        SvgGeometry geometry = Assert.Single(root.Geometries);
        SvgAuthoredPath source = Assert.IsType<SvgAuthoredPath>(geometry.AuthoredPath);
        Assert.Equal(2, source.StrokeStyle?.Width);
        Assert.Equal(new SvgAffineTransform(2, 0, 0, 2, 8, 8), source.SceneMap);
    }

    [Fact]
    public void Maps_pattern_fills_through_scaled_viewports()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg' width='64' height='64'><defs><pattern id='tiles' width='8' height='8' patternUnits='userSpaceOnUse'><rect width='8' height='8' fill='red'/></pattern></defs><svg x='8' y='8' width='48' height='48' viewBox='0 0 24 24'><rect x='0' y='0' width='8' height='8' fill='url(#tiles)'/></svg></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.PatternFill, geometry.Kind);
        Assert.Equal((8d, 8d, 16d, 16d), (geometry.Pattern.Fill.X, geometry.Pattern.Fill.Y, geometry.Pattern.Fill.Width, geometry.Pattern.Fill.Height));
        Assert.Equal(new SvgAffineTransform(2, 0, 0, 2, 8, 8), geometry.Pattern.ShaderPlacement);
    }

    [Fact]
    public void Maps_use_instances_through_scaled_viewports()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg' width='64' height='64'><defs><rect id='tile' width='8' height='8' fill='red'/></defs><svg x='8' y='8' width='48' height='48' viewBox='0 0 24 24'><use href='#tile'/></svg></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgRectangle rectangle = Assert.Single(root.Rectangles);
        Assert.Equal((8d, 8d, 16d, 16d), (rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height));
        Assert.Equal(new SvgColor(255, 0, 0, 255), rectangle.Color);
    }

    [Fact]
    public void Original_scaled_leaf_clip_preserves_empty_intersection()
    {
        AssertSolidRectanglePixels("<svg xmlns='http://www.w3.org/2000/svg' width='64' height='64'><defs><clipPath id='window'><rect x='8' y='8' width='8' height='8'/></clipPath></defs><svg x='8' y='8' width='48' height='48' viewBox='0 0 24 24'><rect x='0' y='0' width='8' height='8' fill='red' clip-path='url(#window)'/></svg></svg>"u8.ToArray(), new(0, 0, 0, 0, new(255, 0, 0, 255), true, null), 64, 64);
    }

    [Fact]
    public void Original_scaled_viewport_mask_maps_region_and_coverage()
    {
        AssertSolidRectanglePixels("<svg xmlns='http://www.w3.org/2000/svg' width='64' height='64'><defs><mask id='window' maskUnits='userSpaceOnUse' x='1' y='1' width='2' height='2'><rect x='0' y='0' width='8' height='8' fill='#808080'/></mask></defs><svg x='8' y='8' width='48' height='48' viewBox='0 0 24 24'><rect x='0' y='0' width='8' height='8' fill='red' mask='url(#window)'/></svg></svg>"u8.ToArray(), new(10, 10, 4, 4, new(255, 0, 0, 128), true, null), 64, 64);
    }

    [Fact]
    public void Clips_a_rounded_rectangle_to_its_user_space_clip()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns=\"http://www.w3.org/2000/svg\"><defs><clipPath id=\"c\"><rect x=\"8\" y=\"8\" width=\"48\" height=\"48\"/></clipPath></defs><rect x=\"0\" y=\"0\" width=\"64\" height=\"64\" rx=\"12\" fill=\"red\" clip-path=\"url(#c)\"/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.PolygonFill, geometry.Kind);
        Assert.True(geometry.Polygon.Clip.HasValue);
        Assert.Equal(new SvgClipBox(8, 8, 48, 48), geometry.Polygon.Clip.Value);
    }

    [Fact]
    public void Clips_forward_user_space_clips_on_rounded_rectangles()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns=\"http://www.w3.org/2000/svg\"><rect x=\"0\" y=\"0\" width=\"64\" height=\"64\" rx=\"12\" fill=\"red\" clip-path=\"url(#later)\"/><defs><clipPath id=\"later\"><rect x=\"8\" y=\"8\" width=\"48\" height=\"48\"/></clipPath></defs></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.PolygonFill, geometry.Kind);
        Assert.True(geometry.Polygon.Clip.HasValue);
        Assert.Equal(new SvgClipBox(8, 8, 48, 48), geometry.Polygon.Clip.Value);
    }

    [Fact]
    public void Renders_forward_clips_identical_to_backward_clips_on_rounded_rectangles()
    {
        byte[] forward = System.Text.Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"64\" height=\"64\"><rect x=\"0\" y=\"0\" width=\"64\" height=\"64\" rx=\"12\" fill=\"red\" clip-path=\"url(#later)\"/><defs><clipPath id=\"later\"><rect x=\"8\" y=\"8\" width=\"48\" height=\"48\"/></clipPath></defs></svg>");
        byte[] backward = System.Text.Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"64\" height=\"64\"><defs><clipPath id=\"later\"><rect x=\"8\" y=\"8\" width=\"48\" height=\"48\"/></clipPath></defs><rect x=\"0\" y=\"0\" width=\"64\" height=\"64\" rx=\"12\" fill=\"red\" clip-path=\"url(#later)\"/></svg>");

        ReadOnlyMemory<byte> forwardPng = SvgConverter.ConvertToPng(forward, SvgRenderOptions.Default, CancellationToken.None);
        ReadOnlyMemory<byte> backwardPng = SvgConverter.ConvertToPng(backward, SvgRenderOptions.Default, CancellationToken.None);

        Assert.True(forwardPng.Span.SequenceEqual(backwardPng.Span));
    }

    [Fact]
    public void Clips_a_rounded_rectangle_to_its_object_bounding_box_clip()
    {
        AssertSolidRectanglePixels("<svg xmlns=\"http://www.w3.org/2000/svg\"><defs><clipPath id=\"c\" clipPathUnits=\"objectBoundingBox\"><rect x=\"0.25\" y=\"0.25\" width=\"0.5\" height=\"0.5\"/></clipPath></defs><rect x=\"0\" y=\"0\" width=\"64\" height=\"64\" rx=\"12\" fill=\"red\" clip-path=\"url(#c)\"/></svg>"u8.ToArray(), new(16, 16, 32, 32, new(255, 0, 0, 255), true, null), 300, 150);
    }

    [Fact]
    public void Clips_a_rectangle_to_its_object_bounding_box_clip()
    {
        AssertSolidRectanglePixels("<svg xmlns='http://www.w3.org/2000/svg'><defs><clipPath id='window' clipPathUnits='objectBoundingBox'><rect x='0.25' y='0.25' width='0.5' height='0.5'/></clipPath></defs><rect x='8' y='8' width='48' height='48' fill='red' clip-path='url(#window)'/></svg>"u8.ToArray(), new(20, 20, 24, 24, new(255, 0, 0, 255), true, null), 300, 150);
    }

    [Fact]
    public void Preserves_full_fraction_clips_on_orthogonal_paths()
    {
        AssertSolidRectanglePixels("<svg xmlns='http://www.w3.org/2000/svg'><defs><clipPath id='window' clipPathUnits='objectBoundingBox'><rect x='0' y='0' width='1' height='1'/></clipPath></defs><path d='M 0,0 H 4 V 4 H 0 Z' fill='red' clip-path='url(#window)'/></svg>"u8.ToArray(), new(0, 0, 4, 4, new(255, 0, 0, 255), true, null), 300, 150);
    }
    [Fact]
    public void Clips_a_polygon_to_its_object_bounding_box_clip()
    {
        AssertEquivalentPixels("<svg xmlns='http://www.w3.org/2000/svg'><defs><clipPath id='window' clipPathUnits='objectBoundingBox'><rect x='0.25' y='0.25' width='0.5' height='0.5'/></clipPath></defs><polygon points='0,0 8,0 8,4 4,4 4,8 0,8' fill='red' clip-path='url(#window)'/></svg>"u8.ToArray(),
            "<svg xmlns='http://www.w3.org/2000/svg'><defs><clipPath id='c'><rect x='2' y='2' width='4' height='4'/></clipPath></defs><polygon points='0,0 8,0 8,4 4,4 4,8 0,8' fill='red' clip-path='url(#c)'/></svg>"u8.ToArray());
    }

    [Fact]
    public void Clips_a_diagonal_path_to_its_object_bounding_box_clip()
    {
        AssertEquivalentPixels("<svg xmlns='http://www.w3.org/2000/svg'><defs><clipPath id='window' clipPathUnits='objectBoundingBox'><rect x='0.25' y='0.25' width='0.5' height='0.5'/></clipPath></defs><path d='M0,0 L8,0 L0,8 Z' fill='red' clip-path='url(#window)'/></svg>"u8.ToArray(),
            "<svg xmlns='http://www.w3.org/2000/svg'><defs><clipPath id='c'><rect x='2' y='2' width='4' height='4'/></clipPath></defs><path d='M0,0 L8,0 L0,8 Z' fill='red' clip-path='url(#c)'/></svg>"u8.ToArray());
    }

    [Fact]
    public void Passes_full_fraction_clips_on_curved_paths_without_clipping()
    {
        AssertEquivalentPixels("<svg xmlns='http://www.w3.org/2000/svg'><defs><clipPath id='window' clipPathUnits='objectBoundingBox'><rect x='0' y='0' width='1' height='1'/></clipPath></defs><path d='M0,0 C8,0 8,8 0,8 Z' fill='red' clip-path='url(#window)'/></svg>"u8.ToArray(),
            "<svg xmlns='http://www.w3.org/2000/svg'><defs><clipPath id='c'><rect x='0' y='0' width='6' height='8'/></clipPath></defs><path d='M0,0 C8,0 8,8 0,8 Z' fill='red' clip-path='url(#c)'/></svg>"u8.ToArray());
    }

    [Fact]
    public void Clips_a_curved_path_to_its_object_bounding_box_clip()
    {
        AssertEquivalentPixels("<svg xmlns='http://www.w3.org/2000/svg'><defs><clipPath id='window' clipPathUnits='objectBoundingBox'><rect x='0.25' y='0.25' width='0.5' height='0.5'/></clipPath></defs><path d='M0,0 C8,0 8,8 0,8 Z' fill='red' clip-path='url(#window)'/></svg>"u8.ToArray(),
            "<svg xmlns='http://www.w3.org/2000/svg'><defs><clipPath id='c'><rect x='1.5' y='2' width='3' height='4'/></clipPath></defs><path d='M0,0 C8,0 8,8 0,8 Z' fill='red' clip-path='url(#c)'/></svg>"u8.ToArray());
    }

    [Fact]
    public void Clips_a_fractional_polygon_to_its_object_bounding_box_clip()
    {
        AssertEquivalentPixels("<svg xmlns='http://www.w3.org/2000/svg'><defs><clipPath id='window' clipPathUnits='objectBoundingBox'><rect x='0.25' y='0.25' width='0.5' height='0.5'/></clipPath></defs><polygon points='0.5,0 8,0 0,8' fill='red' clip-path='url(#window)'/></svg>"u8.ToArray(),
            "<svg xmlns='http://www.w3.org/2000/svg'><defs><clipPath id='c'><rect x='2' y='2' width='4' height='4'/></clipPath></defs><polygon points='0.5,0 8,0 0,8' fill='red' clip-path='url(#c)'/></svg>"u8.ToArray());
    }

    [Fact]
    public void Clips_a_fractional_curve_to_its_object_bounding_box_clip()
    {
        AssertEquivalentPixels("<svg xmlns='http://www.w3.org/2000/svg'><defs><clipPath id='window' clipPathUnits='objectBoundingBox'><rect x='0' y='0' width='1' height='1'/></clipPath></defs><path d='M0.5,8 Q4,-3.5 8,8' fill='red' clip-path='url(#window)'/></svg>"u8.ToArray(),
            "<svg xmlns='http://www.w3.org/2000/svg'><defs><clipPath id='c'><rect x='0.5' y='2.25' width='7.5' height='5.75'/></clipPath></defs><path d='M0.5,8 Q4,-3.5 8,8' fill='red' clip-path='url(#c)'/></svg>"u8.ToArray());
    }

    [Fact]
    public void Clips_use_instances_to_their_object_bounding_box()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><rect id='tile' width='4' height='4' fill='red'/><clipPath id='window' clipPathUnits='objectBoundingBox'><rect x='0' y='0' width='1' height='1'/></clipPath></defs><use href='#tile' clip-path='url(#window)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(new SvgRectangle(0, 0, 4, 4, new SvgColor(255, 0, 0, 255), true, null), Assert.Single(root.Rectangles));
    }

    [Fact]
    public void Clips_polygon_use_instances_to_a_fractional_object_bounding_box()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><polygon id='p' points='0,0 8,0 0,8' fill='red'/></defs><defs><clipPath id='window' clipPathUnits='objectBoundingBox'><rect x='0.25' y='0.25' width='0.5' height='0.5'/></clipPath></defs><use href='#p' x='8' clip-path='url(#window)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.PolygonFill, geometry.Kind);
        Assert.NotNull(geometry.AuthoredPath);
        Assert.Contains(root.Commands.ToArray(), command => command.Kind == SvgDrawCommandKind.BeginGroup && command.MaskContentIndex >= 0);
        using SvgRoot expected = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><clipPath id='c'><rect x='10' y='2' width='4' height='4'/></clipPath></defs><polygon points='8,0 16,0 8,8' fill='red' clip-path='url(#c)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);
        SvgPathFillAssertions.Equivalent(expected, root);
    }

    [Fact]
    public void Clips_forward_use_instances_to_their_object_bounding_box()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><clipPath id='window' clipPathUnits='objectBoundingBox'><rect x='0.25' y='0.25' width='0.5' height='0.5'/></clipPath></defs><use href='#p' x='8' clip-path='url(#window)'/><defs><polygon id='p' points='0,0 8,0 0,8' fill='red'/></defs></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.PolygonFill, geometry.Kind);
        Assert.NotNull(geometry.AuthoredPath);
        Assert.Contains(root.Commands.ToArray(), command => command.Kind == SvgDrawCommandKind.BeginGroup && command.MaskContentIndex >= 0);
        using SvgRoot expected = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><clipPath id='c'><rect x='10' y='2' width='4' height='4'/></clipPath></defs><polygon points='8,0 16,0 8,8' fill='red' clip-path='url(#c)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);
        SvgPathFillAssertions.Equivalent(expected, root);
    }

    [Fact]
    public void Clips_forward_symbol_use_instances_to_their_object_bounding_box()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><clipPath id='window' clipPathUnits='objectBoundingBox'><rect x='0.25' y='0.25' width='0.5' height='0.5'/></clipPath></defs><use href='#s' width='8' height='8' clip-path='url(#window)'/><defs><symbol id='s' viewBox='0 0 8 8'><polygon points='0,0 8,0 0,8' fill='red'/></symbol></defs></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.PolygonFill, geometry.Kind);
        Assert.NotNull(geometry.AuthoredPath);
        Assert.Contains(root.Commands.ToArray(), command => command.Kind == SvgDrawCommandKind.BeginGroup && command.MaskContentIndex >= 0);
        using SvgRoot expected = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><clipPath id='c'><rect x='2' y='2' width='4' height='4'/></clipPath></defs><polygon points='0,0 8,0 0,8' fill='red' clip-path='url(#c)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);
        SvgPathFillAssertions.Equivalent(expected, root);
    }

    [Fact]
    public void Clips_forward_clip_path_use_instances_to_their_user_space_box()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><rect id='r' x='0' y='0' width='8' height='8' fill='red'/></defs><use href='#r' clip-path='url(#c)'/><defs><clipPath id='c'><rect x='2' y='2' width='4' height='4'/></clipPath></defs></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(new SvgRectangle(0, 0, 8, 8, new SvgColor(255, 0, 0, 255), true, null), Assert.Single(root.Rectangles));
        SvgPathFillAssertions.MatchesIntegerBands(root, [new SvgRectangle(2, 2, 4, 4, new SvgColor(255, 0, 0, 255), true, null)]);
    }

    [Fact]
    public void Clips_forward_object_bounding_box_clip_definitions_on_use_instances()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><rect id='tile' width='4' height='4' fill='red'/></defs><use href='#tile' clip-path='url(#window)'/><defs><clipPath id='window' clipPathUnits='objectBoundingBox'><rect x='0' y='0' width='1' height='1'/></clipPath></defs></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(new SvgRectangle(0, 0, 4, 4, new SvgColor(255, 0, 0, 255), true, null), Assert.Single(root.Rectangles));
    }

    [Fact]
    public void Clips_forward_clip_path_symbol_instances_to_their_user_space_box()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><symbol id='s' viewBox='0 0 8 8'><polygon points='0,0 8,0 0,8' fill='red'/></symbol></defs><use href='#s' width='8' height='8' clip-path='url(#c)'/><defs><clipPath id='c'><rect x='2' y='2' width='4' height='4'/></clipPath></defs></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.PolygonFill, geometry.Kind);
        Assert.NotNull(geometry.AuthoredPath);
        Assert.Contains(root.Commands.ToArray(), command => command.Kind == SvgDrawCommandKind.BeginGroup && command.MaskContentIndex >= 0);
        using SvgRoot expected = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><clipPath id='c'><rect x='2' y='2' width='4' height='4'/></clipPath></defs><polygon points='0,0 8,0 0,8' fill='red' clip-path='url(#c)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);
        SvgPathFillAssertions.Equivalent(expected, root);
    }

    [Fact]
    public void Rejects_never_defined_clip_paths_on_use_instances()
    {
        SvgException exception = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><rect id='r' x='0' y='0' width='8' height='8' fill='red'/></defs><use href='#r' clip-path='url(#missing)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, exception.ErrorCode);
    }

    [Fact]
    public void Clips_forward_clip_path_definitions_on_direct_rects()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><rect x='0' y='0' width='8' height='8' fill='red' clip-path='url(#later)'/><defs><clipPath id='later'><rect x='2' y='2' width='4' height='4'/></clipPath></defs></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(new SvgRectangle(2, 2, 4, 4, new SvgColor(255, 0, 0, 255), true, null), Assert.Single(root.Rectangles));
    }

    [Fact]
    public void Clips_forward_clip_path_definitions_on_direct_paths()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><path d='M 0,0 H 8 V 8 H 0 Z' fill='red' clip-path='url(#later)'/><defs><clipPath id='later'><rect x='2' y='2' width='4' height='4'/></clipPath></defs></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(new SvgClipBox(2, 2, 4, 4), Assert.Single(root.Geometries).Polygon.Clip);
        SvgPathFillAssertions.MatchesIntegerBands(root, [new(2, 2, 4, 4, new(255, 0, 0, 255), true, null)]);
    }

    [Fact]
    public void Rejects_never_defined_clip_paths_on_direct_leaves()
    {
        SvgException exception = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><rect x='0' y='0' width='8' height='8' fill='red' clip-path='url(#missing)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, exception.ErrorCode);
    }

    [Fact]
    public void Clips_forward_bounding_box_clips_on_direct_rects()
    {
        AssertSolidRectanglePixels("<svg xmlns='http://www.w3.org/2000/svg'><rect x='0' y='0' width='8' height='8' fill='red' clip-path='url(#later)'/><defs><clipPath id='later' clipPathUnits='objectBoundingBox'><rect x='0.25' y='0.25' width='0.5' height='0.5'/></clipPath></defs></svg>"u8.ToArray(), new(2, 2, 4, 4, new(255, 0, 0, 255), true, null), 300, 150);
    }

    [Fact]
    public void Clips_forward_bounding_box_clips_on_direct_paths()
    {
        AssertSolidRectanglePixels("<svg xmlns='http://www.w3.org/2000/svg'><path d='M 0,0 H 8 V 8 H 0 Z' fill='red' clip-path='url(#later)'/><defs><clipPath id='later' clipPathUnits='objectBoundingBox'><rect x='0.25' y='0.25' width='0.5' height='0.5'/></clipPath></defs></svg>"u8.ToArray(), new(2, 2, 4, 4, new(255, 0, 0, 255), true, null), 300, 150);
    }
    [Fact]
    public void Zero_height_line_object_box_clip_is_nonpainting()
    {
        AssertSolidRectanglePixels("<svg xmlns='http://www.w3.org/2000/svg'><line x1='0' y1='0' x2='8' y2='0' stroke='red' stroke-width='2' clip-path='url(#later)'/><defs><clipPath id='later' clipPathUnits='objectBoundingBox'><rect x='0' y='0' width='1' height='1'/></clipPath></defs></svg>"u8.ToArray(), new(0, 0, 0, 0, new(255, 0, 0, 255), true, null), 300, 150);
    }

    [Fact]
    public void Clips_forward_bounding_box_clips_on_direct_circles()
    {
        AssertEquivalentPixels("<svg xmlns='http://www.w3.org/2000/svg'><circle cx='4' cy='4' r='4' fill='red' clip-path='url(#later)'/><defs><clipPath id='later' clipPathUnits='objectBoundingBox'><rect x='0' y='0' width='1' height='1'/></clipPath></defs></svg>"u8.ToArray(),
            "<svg xmlns='http://www.w3.org/2000/svg'><circle cx='4' cy='4' r='4' fill='red'/></svg>"u8.ToArray());
    }
    [Fact]
    public void Applies_backward_masks_with_forward_clip_paths_on_direct_leaves()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><mask id='fade'><rect x='0' y='0' width='8' height='8' fill='#ffffff'/></mask></defs><rect x='0' y='0' width='8' height='8' fill='red' mask='url(#fade)' clip-path='url(#later)'/><defs><clipPath id='later'><rect x='2' y='2' width='4' height='4'/></clipPath></defs></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(new SvgRectangle(2, 2, 4, 4, new SvgColor(255, 0, 0, 255), true, null), Assert.Single(root.Rectangles));
        SvgLeafMaskAssertions.UniformScope(root, 255, null);
    }

    [Fact]
    public void Applies_forward_masks_to_use_instances()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><rect id='r' x='0' y='0' width='8' height='8' fill='red'/></defs><use href='#r' mask='url(#m)'/><defs><mask id='m'><rect x='0' y='0' width='8' height='8' fill='#808080'/></mask></defs></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(new SvgRectangle(0, 0, 8, 8, new SvgColor(255, 0, 0, 255), true, null), Assert.Single(root.Rectangles));
        SvgLeafMaskAssertions.UniformScope(root, 128, null);
    }

    [Fact]
    public void Applies_forward_masks_to_symbol_instances()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><symbol id='s' viewBox='0 0 8 8'><polygon points='0,0 8,0 0,8' fill='red'/></symbol></defs><use href='#s' width='8' height='8' mask='url(#m)'/><defs><mask id='m'><rect x='0' y='0' width='8' height='8' fill='#808080'/></mask></defs></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.PolygonFill, geometry.Kind);
        Assert.Equal(new SvgColor(255, 0, 0, 255), geometry.Polygon.Color);
        SvgLeafMaskAssertions.UniformScope(root, 128, null);
    }

    [Fact]
    public void Applies_forward_masks_to_forward_use_instances()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><use href='#p' mask='url(#m)'/><defs><polygon id='p' points='0,0 8,0 0,8' fill='red'/></defs><defs><mask id='m'><rect x='0' y='0' width='8' height='8' fill='#808080'/></mask></defs></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.PolygonFill, geometry.Kind);
        Assert.Equal(new SvgColor(255, 0, 0, 255), geometry.Polygon.Color);
        SvgLeafMaskAssertions.UniformScope(root, 128, null);
    }

    [Fact]
    public void Rejects_never_defined_masks_on_use_instances()
    {
        SvgException exception = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><rect id='r' x='0' y='0' width='8' height='8' fill='red'/></defs><use href='#r' mask='url(#missing)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, exception.ErrorCode);
    }

    [Fact]
    public void Applies_forward_masks_to_direct_rects()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><rect x='0' y='0' width='8' height='8' fill='red' mask='url(#later)'/><defs><mask id='later'><rect x='0' y='0' width='8' height='8' fill='#808080'/></mask></defs></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(new SvgRectangle(0, 0, 8, 8, new SvgColor(255, 0, 0, 255), true, null), Assert.Single(root.Rectangles));
        SvgLeafMaskAssertions.UniformScope(root, 128, null);
    }

    [Fact]
    public void Applies_forward_masks_to_direct_paths()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><path d='M 0,0 H 8 V 8 H 0 Z' fill='red' mask='url(#later)'/><defs><mask id='later'><rect x='0' y='0' width='8' height='8' fill='#808080'/></mask></defs></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.NotEmpty(root.Geometries);
        foreach (SvgGeometry geometry in root.Geometries) Assert.Equal(255, geometry.Polygon.Color.Alpha);
        SvgLeafMaskAssertions.UniformScope(root, 128, null);
    }

    [Fact]
    public void Rejects_never_defined_masks_on_direct_leaves()
    {
        SvgException exception = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><rect x='0' y='0' width='8' height='8' fill='red' mask='url(#missing)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, exception.ErrorCode);
    }
    [Fact]
    public void Applies_forward_custom_region_masks_to_direct_rects()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><rect x='0' y='0' width='8' height='8' fill='red' mask='url(#later)'/><defs><mask id='later' maskUnits='userSpaceOnUse' x='2' y='2' width='4' height='4'><rect x='0' y='0' width='8' height='8' fill='#ffffff'/></mask></defs></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(new SvgRectangle(0, 0, 8, 8, new SvgColor(255, 0, 0, 255), true, null), Assert.Single(root.Rectangles));
        SvgLeafMaskAssertions.UniformScope(root, 255, new(2, 2, 4, 4));
    }

    [Fact]
    public void Applies_forward_fraction_region_masks_to_direct_rects()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><rect x='0' y='0' width='8' height='8' fill='red' mask='url(#later)'/><defs><mask id='later' x='0.25' y='0.25' width='0.5' height='0.5'><rect x='0' y='0' width='8' height='8' fill='#ffffff'/></mask></defs></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(new SvgRectangle(0, 0, 8, 8, new SvgColor(255, 0, 0, 255), true, null), Assert.Single(root.Rectangles));
        SvgLeafMaskAssertions.UniformScope(root, 255, new(2, 2, 4, 4));
    }

    [Fact]
    public void Original_forward_viewport_mask_maps_its_region()
    {
        AssertSolidRectanglePixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='8'><svg x='4' y='0' width='8' height='8'><rect x='0' y='0' width='8' height='8' fill='red' mask='url(#later)'/></svg><defs><mask id='later' maskUnits='userSpaceOnUse' x='2' y='2' width='4' height='4'><rect x='0' y='0' width='8' height='8' fill='#ffffff'/></mask></defs></svg>"u8.ToArray(), new(6, 2, 4, 4, new(255, 0, 0, 255), true, null), 16, 8);
    }

    [Fact]
    public void Applies_forward_masks_with_backward_clips_on_direct_rects()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><clipPath id='window'><rect x='2' y='2' width='4' height='4'/></clipPath></defs><rect x='0' y='0' width='8' height='8' fill='red' clip-path='url(#window)' mask='url(#later)'/><defs><mask id='later'><rect x='0' y='0' width='8' height='8' fill='#808080'/></mask></defs></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(new SvgRectangle(2, 2, 4, 4, new SvgColor(255, 0, 0, 255), true, null), Assert.Single(root.Rectangles));
        SvgLeafMaskAssertions.UniformScope(root, 128, null);
    }

    [Fact]
    public void Applies_forward_masks_with_forward_clips_on_direct_rects()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><rect x='0' y='0' width='8' height='8' fill='red' clip-path='url(#window)' mask='url(#later)'/><defs><clipPath id='window'><rect x='2' y='2' width='4' height='4'/></clipPath><mask id='later'><rect x='0' y='0' width='8' height='8' fill='#808080'/></mask></defs></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(new SvgRectangle(2, 2, 4, 4, new SvgColor(255, 0, 0, 255), true, null), Assert.Single(root.Rectangles));
        SvgLeafMaskAssertions.UniformScope(root, 128, null);
    }

    [Fact]
    public void Clips_symbol_use_instances_to_their_object_bounding_box()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><symbol id='s' viewBox='0 0 8 8'><polygon points='0,0 8,0 0,8' fill='red'/></symbol></defs><defs><clipPath id='window' clipPathUnits='objectBoundingBox'><rect x='0.25' y='0.25' width='0.5' height='0.5'/></clipPath></defs><use href='#s' width='8' height='8' clip-path='url(#window)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.PolygonFill, geometry.Kind);
        Assert.NotNull(geometry.AuthoredPath);
        Assert.Contains(root.Commands.ToArray(), command => command.Kind == SvgDrawCommandKind.BeginGroup && command.MaskContentIndex >= 0);
        using SvgRoot expected = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><clipPath id='c'><rect x='2' y='2' width='4' height='4'/></clipPath></defs><polygon points='0,0 8,0 0,8' fill='red' clip-path='url(#c)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);
        SvgPathFillAssertions.Equivalent(expected, root);
    }

    [Fact]
    public void Clips_plain_symbol_use_instances_to_their_object_bounding_box()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><symbol id='s'><polygon points='0,0 8,0 0,8' fill='red'/></symbol></defs><defs><clipPath id='window' clipPathUnits='objectBoundingBox'><rect x='0.25' y='0.25' width='0.5' height='0.5'/></clipPath></defs><use href='#s' clip-path='url(#window)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.PolygonFill, geometry.Kind);
        Assert.NotNull(geometry.AuthoredPath);
        Assert.Contains(root.Commands.ToArray(), command => command.Kind == SvgDrawCommandKind.BeginGroup && command.MaskContentIndex >= 0);
        using SvgRoot expected = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><clipPath id='c'><rect x='2' y='2' width='4' height='4'/></clipPath></defs><polygon points='0,0 8,0 0,8' fill='red' clip-path='url(#c)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);
        SvgPathFillAssertions.Equivalent(expected, root);
    }

    [Fact]
    public void Root_fallback_viewport_supplies_sizeless_symbol_instance_clips()
    {
        byte[] svg = "<svg xmlns='http://www.w3.org/2000/svg'><defs><symbol id='s' viewBox='0 0 8 8'><polygon points='0,0 8,0 0,8' fill='red'/></symbol></defs><defs><clipPath id='window' clipPathUnits='objectBoundingBox'><rect x='0.25' y='0.25' width='0.5' height='0.5'/></clipPath></defs><use href='#s' clip-path='url(#window)'/></svg>"u8.ToArray();
        string source = System.Text.Encoding.UTF8.GetString(svg);
        byte[] explicitViewport = System.Text.Encoding.UTF8.GetBytes(source.Insert(source.IndexOf('>'), " width='300' height='150'"));
        Assert.Equal(SvgConverter.ConvertToPng(explicitViewport, SvgRenderOptions.Default, CancellationToken.None).ToArray(),
            SvgConverter.ConvertToPng(svg, SvgRenderOptions.Default, CancellationToken.None).ToArray());
    }

    [Fact]
    public void Rejects_unknown_clip_path_unit_values()
    {
        SvgException exception = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><clipPath id='window' clipPathUnits='userSpace'><rect x='0' y='0' width='1' height='1'/></clipPath></defs><rect width='8' height='8' fill='red' clip-path='url(#window)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, exception.ErrorCode);
    }

    [Fact]
    public void Transformed_object_box_clip_outside_target_is_nonpainting()
    {
        AssertSolidRectanglePixels("<svg xmlns='http://www.w3.org/2000/svg'><defs><clipPath id='window' clipPathUnits='objectBoundingBox'><rect x='0' y='0' width='1' height='1' transform='translate(1)'/></clipPath></defs><rect width='8' height='8' fill='red' clip-path='url(#window)'/></svg>"u8.ToArray(), new(0, 0, 0, 0, new(255, 0, 0, 255), true, null), 300, 150);
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
    public void Renders_multi_rectangle_clip_paths()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><clipPath id='window'><rect x='4' y='4' width='4' height='4'/><rect x='8' y='8' width='4' height='4'/></clipPath></defs><g clip-path='url(#window)'><rect width='8' height='8' fill='red'/></g></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);
        Assert.False(root.HasUnsupportedChild);
        using SvgConverter.RasterizedSurface raster = SvgConverter.Rasterize(root,
            new(new SvgViewport(8, 8), 1, SvgColor.Transparent, SvgRenderLimits.Default), new(1_000_000), CancellationToken.None);
        Assert.Equal(8, raster.Width);
        Assert.Equal(8, raster.Height);
        for (int y = 0; y < 8; y++)
            for (int x = 0; x < 8; x++)
            {
                byte red = x >= 4 && y >= 4 ? (byte)255 : (byte)0;
                int offset = (y * 8 + x) * 4;
                Assert.Equal(red, raster.Pixels[offset]);
                Assert.Equal(0, raster.Pixels[offset + 1]);
                Assert.Equal(0, raster.Pixels[offset + 2]);
                Assert.Equal(red, raster.Pixels[offset + 3]);
            }
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
        AssertEqualGradient(new SvgLinearGradient(new SvgColor(255, 0, 0, 255), new SvgColor(0, 0, 255, 255), 0, 0, 1, 1, 0, 1, false, false, SvgGradientSpread.Pad, new SvgGradientStop[] { new(0, new SvgColor(255, 0, 0, 255)), new(1, new SvgColor(0, 0, 255, 255)) }, 1, 1, false), geometry.Gradient.Gradient);
        Assert.Equal(new SvgRectangle(8, 8, 48, 48, new SvgColor(0, 0, 0, 0), true, null), geometry.Gradient.Fill);
    }

    [Fact]
    public void Lowers_offset_two_stop_gradient_fill_with_retained_offsets()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='g' x1='0' y1='0' x2='1' y2='0'><stop offset='0.25' stop-color='#ff0000'/><stop offset='0.75' stop-color='#0000ff'/></linearGradient></defs><rect x='8' y='8' width='48' height='48' fill='url(#g)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.GradientFill, geometry.Kind);
        AssertEqualGradient(new SvgLinearGradient(new SvgColor(255, 0, 0, 255), new SvgColor(0, 0, 255, 255), 0, 0, 1, 0, 0.25, 0.75, false, false, SvgGradientSpread.Pad, new SvgGradientStop[] { new(0.25, new SvgColor(255, 0, 0, 255)), new(0.75, new SvgColor(0, 0, 255, 255)) }, 1, 1, false), geometry.Gradient.Gradient);
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
        AssertEqualGradient(new SvgLinearGradient(new SvgColor(255, 0, 0, 255), new SvgColor(0, 0, 255, 255), 0, 0, 1, 0, 0, 1, false, false, SvgGradientSpread.Pad, new SvgGradientStop[] { new(0, new SvgColor(255, 0, 0, 255)), new(1, new SvgColor(0, 0, 255, 255)) }, 1, 1, false), geometry.Gradient.Gradient);
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
        AssertEqualGradient(new SvgLinearGradient(new SvgColor(255, 0, 0, 255), new SvgColor(0, 0, 255, 255), 8, 0, 56, 0, 0, 1, false, true, SvgGradientSpread.Pad, new SvgGradientStop[] { new(0, new SvgColor(255, 0, 0, 255)), new(1, new SvgColor(0, 0, 255, 255)) }, 1, 1, false), geometry.Gradient.Gradient);
    }

    [Fact]
    public void Maps_user_space_gradient_vectors_through_enclosing_transforms()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='g' gradientUnits='userSpaceOnUse' x1='0' y1='0' x2='8' y2='0'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><g transform='translate(8, 8)'><rect x='0' y='0' width='8' height='8' fill='url(#g)'/></g></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.GradientFill, geometry.Kind);
        AssertEqualGradient(new SvgLinearGradient(new SvgColor(255, 0, 0, 255), new SvgColor(0, 0, 255, 255), 8, 8, 16, 8, 0, 1, false, true, SvgGradientSpread.Pad, new SvgGradientStop[] { new(0, new SvgColor(255, 0, 0, 255)), new(1, new SvgColor(0, 0, 255, 255)) }, 1, 1, false), geometry.Gradient.Gradient);
    }

    [Fact]
    public void Rejects_percentage_user_space_gradient_coordinates()
    {
        SvgException exception = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='g' gradientUnits='userSpaceOnUse' x1='0%' y1='0%' x2='100%' y2='0%'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><rect width='8' height='8' fill='url(#g)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, exception.ErrorCode);
    }

    [Fact]
    public void Admits_user_space_gradients_inside_offset_viewports()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg' width='64' height='64'><defs><linearGradient id='g' gradientUnits='userSpaceOnUse' x1='0' y1='0' x2='8' y2='0'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><svg x='8' y='8' width='16' height='16'><rect x='0' y='0' width='8' height='8' fill='url(#g)'/></svg></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.GradientFill, geometry.Kind);
        Assert.Equal(8, geometry.Gradient.Gradient.X1);
        Assert.Equal(8, geometry.Gradient.Gradient.Y1);
        Assert.Equal(16, geometry.Gradient.Gradient.X2);
        Assert.Equal(8, geometry.Gradient.Gradient.Y2);
        Assert.Equal(1, geometry.Gradient.Gradient.ReferenceScaleX);
        Assert.Equal(1, geometry.Gradient.Gradient.ReferenceScaleY);
    }

    [Fact]
    public void Lowers_gradient_transform_vectors_with_composed_coordinates()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='g' x1='0' y1='0' x2='1' y2='0' gradientTransform='scale(2)'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><rect x='8' y='8' width='48' height='48' fill='url(#g)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.GradientFill, geometry.Kind);
        AssertEqualGradient(new SvgLinearGradient(new SvgColor(255, 0, 0, 255), new SvgColor(0, 0, 255, 255), 0, 0, 2, 0, 0, 1, false, false, SvgGradientSpread.Pad, new SvgGradientStop[] { new(0, new SvgColor(255, 0, 0, 255)), new(1, new SvgColor(0, 0, 255, 255)) }, 1, 1, false), geometry.Gradient.Gradient);
    }

    [Theory]
    [InlineData("translate(0.5, 0)", "0.5", "0", "1.5", "0")]
    [InlineData("scale(0.5)", "0", "0", "0.5", "0")]
    [InlineData("translate(0.5, 0) scale(2)", "0.5", "0", "2.5", "0")]
    public void Gradient_fractional_transforms_match_explicit_vectors(string transform, string x1, string y1, string x2, string y2)
    {
        byte[] transformed = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><defs><linearGradient id='g' gradientTransform='" + transform + "'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><rect width='5' height='5' fill='url(#g)'/></svg>");
        byte[] baseline = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><defs><linearGradient id='g' x1='" + x1 + "' y1='" + y1 + "' x2='" + x2 + "' y2='" + y2 + "'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><rect width='5' height='5' fill='url(#g)'/></svg>");

        ReadOnlyMemory<byte> transformedPng = SvgConverter.ConvertToPng(transformed, SvgRenderOptions.Default, CancellationToken.None);
        ReadOnlyMemory<byte> baselinePng = SvgConverter.ConvertToPng(baseline, SvgRenderOptions.Default, CancellationToken.None);

        Assert.True(transformedPng.Span.SequenceEqual(baselinePng.Span));
    }

    [Theory]
    [InlineData("scale(0)")]
    public void Rejects_unsupported_gradient_transform_forms(string transform)
    {
        SvgException exception = Assert.Throws<SvgException>(() => SvgRootParser.Parse(System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='g' gradientTransform='" + transform + "'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><rect width='8' height='8' fill='url(#g)'/></svg>"), SvgRenderLimits.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, exception.ErrorCode);
    }

    [Theory]
    [InlineData("skewX(30)", "matrix(1 0 .5773502691896257 1 0 0)")]
    [InlineData("skewY(15)", "matrix(1 .2679491924311227 0 1 0 0)")]
    public void Original_gradient_skews_match_their_affine_matrix_and_release_storage(string transform, string matrix)
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='g' gradientTransform='" + transform + "'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><rect width='8' height='8' fill='url(#g)'/></svg>");
        byte[] equivalent = System.Text.Encoding.UTF8.GetBytes(System.Text.Encoding.UTF8.GetString(input).Replace(transform, matrix, StringComparison.Ordinal));
        RenderBudget budget = new(1_000_000);
        using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None))
            Assert.True(Assert.Single(root.Geometries).Gradient.Gradient.RequiresSourceMap);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        SvgRenderOptions options = new(new SvgViewport(8, 8), 1, SvgColor.Transparent, SvgRenderLimits.Default);
        Assert.Equal(SvgConverter.ConvertToPng(equivalent, options, CancellationToken.None).ToArray(), SvgConverter.ConvertToPng(input, options, CancellationToken.None).ToArray());
    }

    [Fact]
    public void Admits_general_linear_gradient_maps_on_source_bound_rectangles()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='g' gradientTransform='matrix(1, 1, 0, 1, 0, 0)'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><rect width='8' height='8' fill='url(#g)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.GradientFill, geometry.Kind);
        Assert.True(geometry.Gradient.Gradient.RequiresSourceMap);
        SvgLinearPaintMap paint = Assert.IsType<SvgLinearPaintMap>(geometry.Gradient.Gradient.SourcePaint);
        Assert.True(paint.Data.IsMapped);
        Assert.Equal(new SvgGradientTransform(8, 8, 0, 8, 0, 0), paint.Data.Local);
        Assert.Equal((0d, 0d, 1d, 0d), (paint.Data.X1, paint.Data.Y1, paint.Data.X2, paint.Data.Y2));
    }

    [Fact]
    public void Admits_rotated_linear_gradient_vectors()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='g' x1='0' y1='0' x2='1' y2='0' gradientTransform='rotate(45)'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><rect width='8' height='8' fill='url(#g)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.GradientFill, geometry.Kind);
        Assert.Equal(0, geometry.Gradient.Gradient.X1);
        Assert.Equal(0, geometry.Gradient.Gradient.Y1);
        Assert.Equal(Math.Cos(Math.PI / 4), geometry.Gradient.Gradient.X2);
        Assert.Equal(Math.Sin(Math.PI / 4), geometry.Gradient.Gradient.Y2);
    }
    [Fact]
    public void Lowers_repeat_spread_gradients_with_retained_spread()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='g' x1='0' y1='0' x2='0.5' y2='0' spreadMethod='repeat'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><rect x='8' y='8' width='48' height='48' fill='url(#g)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        AssertEqualGradient(new SvgLinearGradient(new SvgColor(255, 0, 0, 255), new SvgColor(0, 0, 255, 255), 0, 0, 0.5, 0, 0, 1, false, false, SvgGradientSpread.Repeat, new SvgGradientStop[] { new(0, new SvgColor(255, 0, 0, 255)), new(1, new SvgColor(0, 0, 255, 255)) }, 1, 1, false), geometry.Gradient.Gradient);
    }

    [Fact]
    public void Lowers_reflect_spread_gradients_with_retained_spread()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='g' x1='0' y1='0' x2='0.5' y2='0' spreadMethod='reflect'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><rect x='8' y='8' width='48' height='48' fill='url(#g)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        AssertEqualGradient(new SvgLinearGradient(new SvgColor(255, 0, 0, 255), new SvgColor(0, 0, 255, 255), 0, 0, 0.5, 0, 0, 1, false, false, SvgGradientSpread.Reflect, new SvgGradientStop[] { new(0, new SvgColor(255, 0, 0, 255)), new(1, new SvgColor(0, 0, 255, 255)) }, 1, 1, false), geometry.Gradient.Gradient);
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
        AssertEqualGradient(new SvgLinearGradient(new SvgColor(255, 0, 0, 255), new SvgColor(0, 0, 255, 255), 0, 0, 1, 0, 0, 1, false, false, SvgGradientSpread.Pad, new SvgGradientStop[] { new(0, new SvgColor(255, 0, 0, 255)), new(1, new SvgColor(0, 0, 255, 255)) }, 1, 1, false), geometry.Gradient.Gradient);
    }

    [Fact]
    public void Overrides_vector_through_gradient_href()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='b'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient><linearGradient id='c' href='#b' x1='0' y1='0' x2='0.5' y2='0'/></defs><rect x='8' y='8' width='48' height='48' fill='url(#c)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        AssertEqualGradient(new SvgLinearGradient(new SvgColor(255, 0, 0, 255), new SvgColor(0, 0, 255, 255), 0, 0, 0.5, 0, 0, 1, false, false, SvgGradientSpread.Pad, new SvgGradientStop[] { new(0, new SvgColor(255, 0, 0, 255)), new(1, new SvgColor(0, 0, 255, 255)) }, 1, 1, false), geometry.Gradient.Gradient);
    }

    [Fact]
    public void Overrides_stops_through_gradient_href()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='b' x1='0' y1='0' x2='1' y2='1'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient><linearGradient id='c' href='#b'><stop offset='0' stop-color='#00ff00'/><stop offset='1' stop-color='#ffff00'/></linearGradient></defs><rect x='8' y='8' width='48' height='48' fill='url(#c)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        AssertEqualGradient(new SvgLinearGradient(new SvgColor(0, 255, 0, 255), new SvgColor(255, 255, 0, 255), 0, 0, 1, 1, 0, 1, false, false, SvgGradientSpread.Pad, new SvgGradientStop[] { new(0, new SvgColor(0, 255, 0, 255)), new(1, new SvgColor(255, 255, 0, 255)) }, 1, 1, false), geometry.Gradient.Gradient);
    }

    [Fact]
    public void Inherits_transparency_through_gradient_href()
    {
        byte[] input = "<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='b'/><linearGradient id='c' href='#b'/></defs><rect x='8' y='8' width='48' height='48' fill='url(#c)'/></svg>"u8.ToArray();
        using SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, CancellationToken.None);
        SvgRectangle bounds = SvgMaskSyntax.BoundingBox(Assert.Single(root.Geometries));
        Assert.Equal((8d, 8d, 48d, 48d), (bounds.X, bounds.Y, bounds.Width, bounds.Height));
        Assert.Equal(SvgConverter.ConvertToPng("<svg xmlns='http://www.w3.org/2000/svg'/>"u8.ToArray(), SvgRenderOptions.Default, CancellationToken.None).ToArray(),
            SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None).ToArray());
    }

    [Fact]
    public void Resolves_forward_gradient_references()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='c' href='#b'/><linearGradient id='b'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><rect width='8' height='8' fill='url(#c)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.GradientFill, geometry.Kind);
        AssertEqualGradient(new SvgLinearGradient(new SvgColor(255, 0, 0, 255), new SvgColor(0, 0, 255, 255), 0, 0, 1, 0, 0, 1, false, false, SvgGradientSpread.Pad, new SvgGradientStop[] { new(0, new SvgColor(255, 0, 0, 255)), new(1, new SvgColor(0, 0, 255, 255)) }, 1, 1, false), geometry.Gradient.Gradient);
    }

    [Fact]
    public void Resolves_forward_gradient_reference_chains()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='a' href='#b'/><linearGradient id='b' href='#c'/><linearGradient id='c'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><rect width='8' height='8' fill='url(#a)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(new SvgColor(255, 0, 0, 255), geometry.Gradient.Gradient.Start);
        Assert.Equal(new SvgColor(0, 0, 255, 255), geometry.Gradient.Gradient.End);
    }

    [Fact]
    public void Gradient_href_cycles_stay_unsupported()
    {
        SvgException exception = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='a' href='#b'/><linearGradient id='b' href='#a'/></defs><rect width='8' height='8' fill='red'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None));

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
    public void Gradient_href_partial_vector_override_matches_explicit_vector()
    {
        byte[] child = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><defs><linearGradient id='b'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient><linearGradient id='c' href='#b' x1='0.25'/></defs><rect width='5' height='5' fill='url(#c)'/></svg>");
        byte[] baseline = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><defs><linearGradient id='e' x1='0.25' y1='0' x2='1' y2='0'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><rect width='5' height='5' fill='url(#e)'/></svg>");

        ReadOnlyMemory<byte> childPng = SvgConverter.ConvertToPng(child, SvgRenderOptions.Default, CancellationToken.None);
        ReadOnlyMemory<byte> baselinePng = SvgConverter.ConvertToPng(baseline, SvgRenderOptions.Default, CancellationToken.None);

        Assert.True(childPng.Span.SequenceEqual(baselinePng.Span));
    }

    [Fact]
    public void Gradient_href_partial_vector_override_matches_explicit_vector_in_user_space()
    {
        byte[] child = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><defs><linearGradient id='b' gradientUnits='userSpaceOnUse' x1='1' y1='1' x2='4' y2='1'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient><linearGradient id='c' href='#b' x2='3' y2='2'/></defs><rect width='5' height='5' fill='url(#c)'/></svg>");
        byte[] baseline = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><defs><linearGradient id='e' gradientUnits='userSpaceOnUse' x1='1' y1='1' x2='3' y2='2'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><rect width='5' height='5' fill='url(#e)'/></svg>");

        ReadOnlyMemory<byte> childPng = SvgConverter.ConvertToPng(child, SvgRenderOptions.Default, CancellationToken.None);
        ReadOnlyMemory<byte> baselinePng = SvgConverter.ConvertToPng(baseline, SvgRenderOptions.Default, CancellationToken.None);

        Assert.True(childPng.Span.SequenceEqual(baselinePng.Span));
    }

    [Fact]
    public void Rejects_partial_vector_overrides_with_unit_overrides()
    {
        SvgException exception = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='b'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient><linearGradient id='c' href='#b' gradientUnits='userSpaceOnUse' x1='1'/></defs><rect width='5' height='5' fill='url(#c)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None));

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
        AssertEqualGradient(new SvgLinearGradient(new SvgColor(255, 0, 0, 255), new SvgColor(0, 0, 255, 255), 0, 0, 0.5, 0, 0, 1, false, false, SvgGradientSpread.Pad, new SvgGradientStop[] { new(0, new SvgColor(255, 0, 0, 255)), new(1, new SvgColor(0, 0, 255, 255)) }, 1, 1, false), geometry.Gradient.Gradient);
    }

    [Fact]
    public void Composes_child_transforms_over_inherited_vectors()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='a' x1='0' y1='0' x2='1' y2='0'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient><linearGradient id='b' href='#a' gradientTransform='translate(1, 0)'/></defs><rect x='8' y='8' width='48' height='48' fill='url(#b)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        AssertEqualGradient(new SvgLinearGradient(new SvgColor(255, 0, 0, 255), new SvgColor(0, 0, 255, 255), 1, 0, 2, 0, 0, 1, false, false, SvgGradientSpread.Pad, new SvgGradientStop[] { new(0, new SvgColor(255, 0, 0, 255)), new(1, new SvgColor(0, 0, 255, 255)) }, 1, 1, false), geometry.Gradient.Gradient);
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
        AssertEqualGradient(new SvgLinearGradient(new SvgColor(255, 0, 0, 255), new SvgColor(0, 0, 255, 255), 0, 0, 1, 0, 0, 1, false, false, SvgGradientSpread.Pad, new SvgGradientStop[] { new(0, new SvgColor(255, 0, 0, 255)), new(1, new SvgColor(0, 0, 255, 255)) }, 1, 1, false), geometry.GradientStroke.Gradient);
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
    public void Lowers_gradient_fills_with_gradient_strokes_as_fill_plus_bands()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='g'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient><linearGradient id='h'><stop offset='0' stop-color='#00ff00'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><rect width='8' height='8' fill='url(#g)' stroke='url(#h)' stroke-width='2'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(2, root.Geometries.Length);
        Assert.Equal(SvgGeometryKind.GradientFill, root.Geometries[0].Kind);
        Assert.True(root.Geometries[0].Gradient.Fill.HasFill);
        Assert.Equal(SvgGeometryKind.GradientStroke, root.Geometries[1].Kind);
        Assert.False(root.Geometries[1].GradientStroke.Fill.HasFill);
        Assert.True(root.Geometries[1].GradientStroke.HasStroke);
    }

    [Fact]
    public void Lowers_radial_fills_with_radial_strokes_as_fill_plus_bands()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><radialGradient id='f' cx='0.5' cy='0.5' r='0.5'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient><radialGradient id='s' cx='0.5' cy='0.5' r='0.5'><stop offset='0' stop-color='#00ff00'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><rect width='8' height='8' fill='url(#f)' stroke='url(#s)' stroke-width='2'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(5, root.Geometries.Length);
        Assert.All(root.Geometries, band => Assert.Equal(SvgGeometryKind.RadialGradient, band.Kind));
        Assert.True(root.Geometries[0].Radial.Fill.HasFill);
    }

    [Fact]
    public void Lowers_pattern_strokes_as_tile_bands()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><pattern id='tiles' width='8' height='8' patternUnits='userSpaceOnUse'><rect width='8' height='8' fill='red'/></pattern></defs><rect width='8' height='8' fill='red' stroke='url(#tiles)' stroke-width='2'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(5, root.Geometries.Length);
        Assert.Equal(SvgGeometryKind.Rectangle, root.Geometries[0].Kind);
        Assert.Equal(new SvgColor(255, 0, 0, 255), root.Geometries[0].Rectangle.Color);
        Assert.All(root.Geometries.Skip(1), band => Assert.Equal(SvgGeometryKind.PatternFill, band.Kind));
        Assert.All(root.Geometries.Skip(1), band => Assert.True(band.Pattern.Fill.HasFill));
    }


    [Fact]
    public void Lowers_pattern_strokes_on_lines_as_native_contours()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><pattern id='tiles' width='8' height='8' patternUnits='userSpaceOnUse'><rect width='8' height='8' fill='red'/></pattern></defs><line x1='0' y1='4' x2='8' y2='4' stroke='url(#tiles)' stroke-width='2'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.PatternPolygon, geometry.Kind);
        AssertStrokeContourBounds(geometry, [new(0, 3, 8, 2, default, true, null)]);
    }

    [Fact]
    public void Extends_square_cap_lines_by_half_width()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><line x1='8' y1='4' x2='16' y2='4' stroke='red' stroke-width='4' stroke-linecap='square'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.AxisAlignedLine, geometry.Kind);
        Assert.Equal(new SvgAxisAlignedLine(6, 4, 18, 4, new SvgColor(255, 0, 0, 255), true, 4), geometry.AxisAlignedLine);
    }

    [Fact]
    public void Lowers_round_capped_lines_as_band_with_tip_discs()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><line x1='8' y1='4' x2='16' y2='4' stroke='red' stroke-width='4' stroke-linecap='round'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(3, root.Geometries.Length);
        Assert.Equal(SvgGeometryKind.AxisAlignedLine, root.Geometries[0].Kind);
        Assert.Equal(new SvgAxisAlignedLine(8, 4, 16, 4, new SvgColor(255, 0, 0, 255), true, 4), root.Geometries[0].AxisAlignedLine);
        Assert.Equal(SvgGeometryKind.PolygonFill, root.Geometries[1].Kind);
        Assert.Equal(SvgGeometryKind.PolygonFill, root.Geometries[2].Kind);
        Assert.Equal(SvgGeometryKind.PolygonFill, root.Geometries[1].Kind);
        Assert.Equal(SvgGeometryKind.PolygonFill, root.Geometries[2].Kind);
    }

    [Fact]
    public void Lowers_round_capped_gradient_lines_as_native_contours()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns=\"http://www.w3.org/2000/svg\"><defs><linearGradient id=\"g\" gradientUnits=\"userSpaceOnUse\" x1=\"0\" y1=\"0\" x2=\"16\" y2=\"0\"><stop offset=\"0\" stop-color=\"#ff0000\"/><stop offset=\"1\" stop-color=\"#0000ff\"/></linearGradient></defs><line x1=\"0\" y1=\"4\" x2=\"16\" y2=\"4\" stroke=\"url(#g)\" stroke-width=\"2\" stroke-linecap=\"round\"/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.GradientPolygon, geometry.Kind);
        AssertStrokeContourBounds(geometry, [new(-1, 3, 18, 2, default, true, null)]);
        Assert.True(geometry.GradientPolygon.HasFill);
    }

    [Fact]
    public void Lowers_round_capped_radial_lines_as_native_contours()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns=\"http://www.w3.org/2000/svg\"><defs><radialGradient id=\"g\" gradientUnits=\"userSpaceOnUse\" cx=\"8\" cy=\"4\" r=\"8\"><stop offset=\"0\" stop-color=\"#ff0000\"/><stop offset=\"1\" stop-color=\"#0000ff\"/></radialGradient></defs><line x1=\"0\" y1=\"4\" x2=\"16\" y2=\"4\" stroke=\"url(#g)\" stroke-width=\"2\" stroke-linecap=\"round\"/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.RadialPolygon, geometry.Kind);
        AssertStrokeContourBounds(geometry, [new(-1, 3, 18, 2, default, true, null)]);
        Assert.True(geometry.RadialPolygon.HasFill);
    }

    [Fact]
    public void Lowers_round_capped_pattern_lines_as_native_contours()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns=\"http://www.w3.org/2000/svg\"><defs><pattern id=\"p\" width=\"8\" height=\"8\" patternUnits=\"userSpaceOnUse\"><rect width=\"8\" height=\"8\" fill=\"red\"/></pattern></defs><line x1=\"0\" y1=\"4\" x2=\"16\" y2=\"4\" stroke=\"url(#p)\" stroke-width=\"2\" stroke-linecap=\"round\"/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.PatternPolygon, geometry.Kind);
        AssertStrokeContourBounds(geometry, [new(-1, 3, 18, 2, default, true, null)]);
        Assert.True(geometry.PatternPolygon.HasFill);
    }

    [Fact]
    public void Retains_translucent_round_capped_paint_lines()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns=\"http://www.w3.org/2000/svg\"><defs><linearGradient id=\"g\" gradientUnits=\"userSpaceOnUse\" x1=\"0\" y1=\"0\" x2=\"16\" y2=\"0\"><stop offset=\"0\" stop-color=\"#ff0000\"/><stop offset=\"1\" stop-color=\"#0000ff\"/></linearGradient></defs><line x1=\"0\" y1=\"4\" x2=\"16\" y2=\"4\" stroke=\"url(#g)\" stroke-width=\"2\" stroke-linecap=\"round\" opacity=\"0.5\"/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.False(root.HasUnsupportedChild);
        Assert.NotEmpty(root.Geometries);
        Assert.Contains(root.Commands, command => command.Kind == SvgDrawCommandKind.BeginGroup && command.SourceOpacity.Value == 0.5f);
    }

    [Fact]
    public void Renders_pattern_round_capped_lines_identical_to_solid()
    {
        byte[] painted = System.Text.Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"8\"><defs><pattern id=\"p\" width=\"8\" height=\"8\" patternUnits=\"userSpaceOnUse\"><rect width=\"8\" height=\"8\" fill=\"red\"/></pattern></defs><line x1=\"0\" y1=\"4\" x2=\"16\" y2=\"4\" stroke=\"url(#p)\" stroke-width=\"2\" stroke-linecap=\"round\"/></svg>");
        byte[] solid = System.Text.Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"8\"><line x1=\"0\" y1=\"4\" x2=\"16\" y2=\"4\" stroke=\"red\" stroke-width=\"2\" stroke-linecap=\"round\"/></svg>");

        ReadOnlyMemory<byte> paintedPng = SvgConverter.ConvertToPng(painted, SvgRenderOptions.Default, CancellationToken.None);
        ReadOnlyMemory<byte> solidPng = SvgConverter.ConvertToPng(solid, SvgRenderOptions.Default, CancellationToken.None);

        Assert.True(paintedPng.Span.SequenceEqual(solidPng.Span));
    }

    [Fact]
    public void Lowers_dashed_lines_as_run_bands()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><line x1='0' y1='4' x2='16' y2='4' stroke='red' stroke-width='2' stroke-dasharray='4 4'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(2, root.Geometries.Length);
        Assert.Equal(SvgGeometryKind.OrthogonalBand, root.Geometries[0].Kind);
        Assert.Equal(new SvgRectangle(0, 3, 4, 2, new SvgColor(255, 0, 0, 255), true, null), root.Geometries[0].Rectangle);
        Assert.Equal(new SvgRectangle(8, 3, 4, 2, new SvgColor(255, 0, 0, 255), true, null), root.Geometries[1].Rectangle);
    }

    [Fact]
    public void Retains_translucent_round_capped_lines()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><line x1='0' y1='4' x2='16' y2='4' stroke='red' opacity='0.5' stroke-width='2' stroke-linecap='round'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.False(root.HasUnsupportedChild);
        Assert.NotEmpty(root.Geometries);
        Assert.Contains(root.Commands, command => command.Kind == SvgDrawCommandKind.BeginGroup && command.SourceOpacity.Value == 0.5f);
    }

    [Fact]
    public void Renders_round_caps_with_dashed_lines()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><line x1='0' y1='4' x2='16' y2='4' stroke='red' stroke-width='2' stroke-dasharray='4 4' stroke-linecap='round'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.False(root.HasUnsupportedChild);
        SvgGeometry geometry = Assert.Single(root.Geometries);
        AssertStrokeContourBounds(geometry, [new(-1, 3, 6, 2, default, true, null), new(7, 3, 6, 2, default, true, null)]);
    }

    [Fact]
    public void Maps_user_space_vectors_on_round_capped_gradient_lines()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns=\"http://www.w3.org/2000/svg\"><defs><linearGradient id=\"s\" gradientUnits=\"userSpaceOnUse\" x1=\"0\" y1=\"4\" x2=\"16\" y2=\"4\"><stop offset=\"0\" stop-color=\"#ff0000\"/><stop offset=\"1\" stop-color=\"#0000ff\"/></linearGradient></defs><line x1=\"0\" y1=\"4\" x2=\"16\" y2=\"4\" stroke=\"url(#s)\" stroke-width=\"2\" stroke-linecap=\"round\"/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgLinearGradient gradient = Assert.Single(root.Geometries).GradientPolygon.Gradient;
        Assert.Equal(0, gradient.X1);
        Assert.Equal(4, gradient.Y1);
        Assert.Equal(16, gradient.X2);
        Assert.Equal(4, gradient.Y2);
    }
    [Fact]
    public void Dashed_line_offsets_shift_run_bands()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><line x1='0' y1='4' x2='16' y2='4' stroke='red' stroke-width='2' stroke-dasharray='4 4' stroke-dashoffset='2'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(3, root.Geometries.Length);
        Assert.Equal(new SvgRectangle(0, 3, 2, 2, new SvgColor(255, 0, 0, 255), true, null), root.Geometries[0].Rectangle);
        Assert.Equal(new SvgRectangle(6, 3, 4, 2, new SvgColor(255, 0, 0, 255), true, null), root.Geometries[1].Rectangle);
        Assert.Equal(new SvgRectangle(14, 3, 2, 2, new SvgColor(255, 0, 0, 255), true, null), root.Geometries[2].Rectangle);
    }

    [Theory]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='g' gradientUnits='userSpaceOnUse' x1='0' y1='0' x2='16' y2='0'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><svg xmlns='http://www.w3.org/2000/svg'><line x1='0' y1='4' x2='16' y2='4' stroke='url(#g)' stroke-width='2' stroke-dasharray='4 4'/></svg>")]
    public void Rejects_malformed_nested_dashed_line_documents(string svg)
    {
        SvgException exception = Assert.Throws<SvgException>(() => SvgRootParser.Parse(System.Text.Encoding.UTF8.GetBytes(svg), SvgRenderLimits.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.MalformedInput, exception.ErrorCode);
        using SvgRoot corrected = SvgRootParser.Parse(System.Text.Encoding.UTF8.GetBytes(svg + "</svg>"), SvgRenderLimits.Default, CancellationToken.None);
        Assert.NotEmpty(corrected.Geometries);
    }

    [Fact]
    public void Lowers_gradient_fills_with_pattern_strokes_as_fill_plus_bands()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='g'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><defs><pattern id='p' width='8' height='8' patternUnits='userSpaceOnUse'><rect width='8' height='8' fill='red'/></pattern></defs><rect width='8' height='8' fill='url(#g)' stroke='url(#p)' stroke-width='2'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(5, root.Geometries.Length);
        Assert.Equal(SvgGeometryKind.GradientFill, root.Geometries[0].Kind);
        Assert.True(root.Geometries[0].Gradient.Fill.HasFill);
        Assert.All(root.Geometries.Skip(1), band => Assert.Equal(SvgGeometryKind.PatternFill, band.Kind));
        Assert.All(root.Geometries.Skip(1), band => Assert.True(band.Pattern.Fill.HasFill));
    }

    [Fact]
    public void Lowers_radial_fills_with_pattern_strokes_as_fill_plus_bands()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><radialGradient id='f' cx='0.5' cy='0.5' r='0.5'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><defs><pattern id='p' width='8' height='8' patternUnits='userSpaceOnUse'><rect width='8' height='8' fill='red'/></pattern></defs><rect width='8' height='8' fill='url(#f)' stroke='url(#p)' stroke-width='2'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(5, root.Geometries.Length);
        Assert.Equal(SvgGeometryKind.RadialGradient, root.Geometries[0].Kind);
        Assert.True(root.Geometries[0].Radial.Fill.HasFill);
        Assert.All(root.Geometries.Skip(1), band => Assert.Equal(SvgGeometryKind.PatternFill, band.Kind));
    }

    [Fact]
    public void Lowers_pattern_fills_with_pattern_strokes_as_fill_plus_bands()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><pattern id='f' width='8' height='8' patternUnits='userSpaceOnUse'><rect width='8' height='8' fill='red'/></pattern><pattern id='s' width='8' height='8' patternUnits='userSpaceOnUse'><rect width='8' height='8' fill='blue'/></pattern></defs><rect width='8' height='8' fill='url(#f)' stroke='url(#s)' stroke-width='2'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(5, root.Geometries.Length);
        Assert.All(root.Geometries, band => Assert.Equal(SvgGeometryKind.PatternFill, band.Kind));
        Assert.True(root.Geometries[0].Pattern.Fill.HasFill);
    }

    [Fact]
    public void Lowers_pattern_fills_with_gradient_strokes_as_fill_plus_bands()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><pattern id='p' width='8' height='8' patternUnits='userSpaceOnUse'><rect width='8' height='8' fill='red'/></pattern></defs><defs><linearGradient id='g'><stop offset='0' stop-color='#00ff00'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><rect width='8' height='8' fill='url(#p)' stroke='url(#g)' stroke-width='2'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(2, root.Geometries.Length);
        Assert.Equal(SvgGeometryKind.PatternFill, root.Geometries[0].Kind);
        Assert.True(root.Geometries[0].Pattern.Fill.HasFill);
        Assert.Equal(SvgGeometryKind.GradientStroke, root.Geometries[1].Kind);
        Assert.False(root.Geometries[1].GradientStroke.Fill.HasFill);
        Assert.True(root.Geometries[1].GradientStroke.HasStroke);
    }

    [Fact]
    public void Lowers_pattern_fills_with_radial_strokes_as_fill_plus_bands()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><pattern id='p' width='8' height='8' patternUnits='userSpaceOnUse'><rect width='8' height='8' fill='red'/></pattern></defs><defs><radialGradient id='s' cx='0.5' cy='0.5' r='0.5'><stop offset='0' stop-color='#00ff00'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><rect width='8' height='8' fill='url(#p)' stroke='url(#s)' stroke-width='2'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(5, root.Geometries.Length);
        Assert.Equal(SvgGeometryKind.PatternFill, root.Geometries[0].Kind);
        Assert.True(root.Geometries[0].Pattern.Fill.HasFill);
        Assert.All(root.Geometries.Skip(1), band => Assert.Equal(SvgGeometryKind.RadialGradient, band.Kind));
    }

    [Fact]
    public void Instances_gradient_fill_with_pattern_stroke_definitions()
    {
        byte[] input = "<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='g'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient><pattern id='p' width='8' height='8' patternUnits='userSpaceOnUse'><rect width='8' height='8' fill='red'/></pattern><rect id='r' width='8' height='8' fill='url(#g)' stroke='url(#p)' stroke-width='2'/></defs><use href='#r'/></svg>"u8.ToArray();
        byte[] expected = "<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='g'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient><pattern id='p' width='8' height='8' patternUnits='userSpaceOnUse'><rect width='8' height='8' fill='red'/></pattern></defs><rect width='8' height='8' fill='url(#g)' stroke='url(#p)' stroke-width='2'/></svg>"u8.ToArray();
        Assert.Equal(SvgConverter.ConvertToPng(expected, SvgRenderOptions.Default, CancellationToken.None).ToArray(), SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None).ToArray());
    }

    [Fact]
    public void White_masks_leave_rounded_strokes_unchanged()
    {
        byte[] masked = System.Text.Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"64\" height=\"64\"><defs><mask id=\"m\"><rect x=\"0\" y=\"0\" width=\"64\" height=\"64\" fill=\"white\"/></mask></defs><rect x=\"8\" y=\"8\" width=\"48\" height=\"48\" rx=\"12\" fill=\"red\" stroke=\"blue\" stroke-width=\"8\" mask=\"url(#m)\"/></svg>");
        byte[] baseline = System.Text.Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"64\" height=\"64\"><rect x=\"8\" y=\"8\" width=\"48\" height=\"48\" rx=\"12\" fill=\"red\" stroke=\"blue\" stroke-width=\"8\"/></svg>");

        ReadOnlyMemory<byte> maskedPng = SvgConverter.ConvertToPng(masked, SvgRenderOptions.Default, CancellationToken.None);
        ReadOnlyMemory<byte> baselinePng = SvgConverter.ConvertToPng(baseline, SvgRenderOptions.Default, CancellationToken.None);

        Assert.True(maskedPng.Span.SequenceEqual(baselinePng.Span));
    }

    [Fact]
    public void White_masks_leave_rounded_rectangles_unchanged()
    {
        byte[] masked = System.Text.Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"64\" height=\"64\"><defs><mask id=\"m\"><rect x=\"0\" y=\"0\" width=\"64\" height=\"64\" fill=\"white\"/></mask></defs><rect x=\"8\" y=\"8\" width=\"48\" height=\"48\" rx=\"12\" fill=\"red\" mask=\"url(#m)\"/></svg>");
        byte[] baseline = System.Text.Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"64\" height=\"64\"><rect x=\"8\" y=\"8\" width=\"48\" height=\"48\" rx=\"12\" fill=\"red\"/></svg>");

        ReadOnlyMemory<byte> maskedPng = SvgConverter.ConvertToPng(masked, SvgRenderOptions.Default, CancellationToken.None);
        ReadOnlyMemory<byte> baselinePng = SvgConverter.ConvertToPng(baseline, SvgRenderOptions.Default, CancellationToken.None);

        Assert.True(maskedPng.Span.SequenceEqual(baselinePng.Span));
    }

    [Fact]
    public void Applies_a_gray_mask_to_gradient_stroke_geometry()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='g'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient><mask id='m'><rect x='0' y='0' width='64' height='64' fill='#808080'/></mask></defs><rect x='16' y='16' width='32' height='32' fill='#00ff00' stroke='url(#g)' stroke-width='8' mask='url(#m)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.GradientStroke, geometry.Kind);
        Assert.Equal(255, geometry.GradientStroke.Opacity);
        Assert.Equal(new SvgColor(0, 255, 0, 255), geometry.GradientStroke.Fill.Color);
        Assert.Equal(new SvgRectangle(12, 12, 40, 8, new SvgColor(0, 0, 0, 0), true, null), geometry.GradientStroke.Top);
        SvgLeafMaskAssertions.UniformScope(root, 128, null);
    }

    [Fact]
    public void Admits_an_empty_linear_gradient_as_transparent()
    {
        byte[] input = "<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='g'/></defs><rect x='8' y='8' width='48' height='48' fill='url(#g)'/></svg>"u8.ToArray();
        using SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, CancellationToken.None);
        SvgRectangle bounds = SvgMaskSyntax.BoundingBox(Assert.Single(root.Geometries));
        Assert.Equal((8d, 8d, 48d, 48d), (bounds.X, bounds.Y, bounds.Width, bounds.Height));
        Assert.Equal(SvgConverter.ConvertToPng("<svg xmlns='http://www.w3.org/2000/svg'/>"u8.ToArray(), SvgRenderOptions.Default, CancellationToken.None).ToArray(),
            SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None).ToArray());
    }

    [Fact]
    public void Degenerate_user_space_gradients_render_the_end_stop_color()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='g' gradientUnits='userSpaceOnUse' x1='1' y1='1' x2='1' y2='1'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><rect width='8' height='8' fill='url(#g)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.GradientFill, geometry.Kind);
        Assert.Equal(new SvgColor(0, 0, 255, 255), geometry.Gradient.Gradient.End);
        Assert.True(geometry.Gradient.Fill.HasFill);
        Assert.False(root.HasUnsupportedChild);
    }

    [Fact]
    public void Degenerate_bounding_box_gradients_render_the_end_stop_color()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='g' x1='0.5' y1='0.5' x2='0.5' y2='0.5'><stop offset='0.25' stop-color='#ff0000'/><stop offset='0.75' stop-color='#0000ff'/></linearGradient></defs><rect width='8' height='8' fill='url(#g)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.GradientFill, geometry.Kind);
        Assert.Equal(new SvgColor(0, 0, 255, 255), geometry.Gradient.Gradient.End);
        Assert.True(geometry.Gradient.Fill.HasFill);
        Assert.False(root.HasUnsupportedChild);
    }

    [Fact]
    public void Degenerate_gradient_fills_render_byte_identical_to_solid_fills()
    {
        byte[] degenerate = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><defs><linearGradient id='g' x1='1' y1='1' x2='1' y2='1'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><rect width='5' height='5' fill='url(#g)'/></svg>");
        byte[] solid = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><rect width='5' height='5' fill='blue'/></svg>");

        ReadOnlyMemory<byte> degeneratePng = SvgConverter.ConvertToPng(degenerate, SvgRenderOptions.Default, CancellationToken.None);
        ReadOnlyMemory<byte> solidPng = SvgConverter.ConvertToPng(solid, SvgRenderOptions.Default, CancellationToken.None);

        Assert.True(degeneratePng.Span.SequenceEqual(solidPng.Span));
    }

    [Fact]
    public void Rejects_non_pad_degenerate_gradient_fills()
    {
        SvgException exception = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='g' x1='1' y1='1' x2='1' y2='1' spreadMethod='repeat'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><rect width='8' height='8' fill='url(#g)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, exception.ErrorCode);
    }

    [Fact]
    public void Retains_degenerate_gradient_strokes_as_constant_shader_rings()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='g' x1='1' y1='1' x2='1' y2='1'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><rect width='5' height='5' fill='red' stroke='url(#g)' stroke-width='2'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.GradientStroke, geometry.Kind);
        Assert.Equal(new SvgColor(255, 0, 0, 255), geometry.GradientStroke.Fill.Color);
        Assert.Equal(new SvgColor(0, 0, 255, 255), geometry.GradientStroke.Gradient.End);
        Assert.Equal(2, geometry.GradientStroke.Top.Height);
    }

    [Fact]
    public void Lowers_multi_stop_linear_gradients_with_retained_clamped_stops()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='g'><stop offset='0' stop-color='#ff0000'/><stop offset='0.5' stop-color='#00ff00'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><rect width='8' height='8' fill='url(#g)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        AssertEqualGradient(new SvgLinearGradient(new SvgColor(255, 0, 0, 255), new SvgColor(0, 0, 255, 255), 0, 0, 1, 0, 0, 1, false, false, SvgGradientSpread.Pad, new SvgGradientStop[] { new(0, new SvgColor(255, 0, 0, 255)), new(0.5, new SvgColor(0, 255, 0, 255)), new(1, new SvgColor(0, 0, 255, 255)) }, 1, 1, false), geometry.Gradient.Gradient);
    }

    [Fact]
    public void Admits_linear_gradients_with_more_than_eight_stops()
    {
        using SvgRoot root = SvgRootParser.Parse(System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='g'><stop offset='0' stop-color='#ff0000'/><stop offset='0.1' stop-color='#ff0000'/><stop offset='0.2' stop-color='#ff0000'/><stop offset='0.3' stop-color='#ff0000'/><stop offset='0.4' stop-color='#ff0000'/><stop offset='0.5' stop-color='#ff0000'/><stop offset='0.6' stop-color='#ff0000'/><stop offset='0.7' stop-color='#ff0000'/><stop offset='0.8' stop-color='#ff0000'/></linearGradient></defs><rect width='8' height='8' fill='url(#g)'/></svg>"), SvgRenderLimits.Default, CancellationToken.None);

        SvgGradientStop[] stops = Assert.Single(root.Geometries).Gradient.Gradient.Stops;
        Assert.Equal(9, stops.Length);
        Assert.All(stops, stop => Assert.Equal(new SvgColor(255, 0, 0, 255), stop.Color));
    }

    [Fact]
    public void Admits_duplicate_gradient_stop_offsets_as_hard_edges()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='g'><stop offset='0' stop-color='#ff0000'/><stop offset='0.5' stop-color='#ff0000'/><stop offset='0.5' stop-color='#0000ff'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><rect width='8' height='8' fill='url(#g)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        AssertEqualGradient(new SvgLinearGradient(new SvgColor(255, 0, 0, 255), new SvgColor(0, 0, 255, 255), 0, 0, 1, 0, 0, 1, false, false, SvgGradientSpread.Pad, new SvgGradientStop[] { new(0, new SvgColor(255, 0, 0, 255)), new(0.5, new SvgColor(255, 0, 0, 255)), new(0.5, new SvgColor(0, 0, 255, 255)), new(1, new SvgColor(0, 0, 255, 255)) }, 1, 1, false), geometry.Gradient.Gradient);
    }

    [Fact]
    public void Rises_falling_gradient_stop_offsets_to_the_previous_stop()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='g'><stop offset='0' stop-color='#ff0000'/><stop offset='0.8' stop-color='#ff0000'/><stop offset='0.3' stop-color='#0000ff'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><rect width='8' height='8' fill='url(#g)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        AssertEqualGradient(new SvgLinearGradient(new SvgColor(255, 0, 0, 255), new SvgColor(0, 0, 255, 255), 0, 0, 1, 0, 0, 1, false, false, SvgGradientSpread.Pad, new SvgGradientStop[] { new(0, new SvgColor(255, 0, 0, 255)), new((double)0.8f, new SvgColor(255, 0, 0, 255)), new((double)0.8f, new SvgColor(0, 0, 255, 255)), new(1, new SvgColor(0, 0, 255, 255)) }, 1, 1, false), geometry.Gradient.Gradient);
    }

    [Fact]
    public void Rejects_gradient_stops_with_zero_clamped_offset_range()
    {
        SvgException duplicate = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='g'><stop offset='0.5' stop-color='#ff0000'/><stop offset='0.5' stop-color='#0000ff'/></linearGradient></defs><rect width='8' height='8' fill='url(#g)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, duplicate.ErrorCode);
        SvgException fallen = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='g'><stop offset='0.8' stop-color='#ff0000'/><stop offset='0.2' stop-color='#0000ff'/></linearGradient></defs><rect width='8' height='8' fill='url(#g)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, fallen.ErrorCode);
    }

    [Fact]
    public void Lowers_concentric_radial_gradient_with_clamped_stops()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><radialGradient id='g' cx='0.5' cy='0.5' r='0.5'><stop offset='0' stop-color='#ff0000'/><stop offset='0.5' stop-color='#00ff00'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><rect width='8' height='8' fill='url(#g)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.RadialGradient, geometry.Kind);
        AssertEqualRadial(new SvgRadialGradient(new SvgColor(255, 0, 0, 255), new SvgColor(0, 0, 255, 255), 0.5, 0.5, 0.5, 0.5, 0.5, 0, 0, 1, false, false, SvgGradientSpread.Pad, new SvgGradientStop[] { new(0, new SvgColor(255, 0, 0, 255)), new(0.5, new SvgColor(0, 255, 0, 255)), new(1, new SvgColor(0, 0, 255, 255)) }, 1, 1, false, SvgGradientTransform.Identity, false), geometry.Radial.Gradient);
    }

    [Fact]
    public void Radial_gradient_defaults_to_centered_half_radius()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><radialGradient id='g'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><rect width='8' height='8' fill='url(#g)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.RadialGradient, geometry.Kind);
        Assert.Equal(0.5, geometry.Radial.Gradient.CenterX);
        Assert.Equal(0.5, geometry.Radial.Gradient.CenterY);
        Assert.Equal(0.5, geometry.Radial.Gradient.Radius);
    }

    [Fact]
    public void Inherits_radial_stops_and_circle_through_href()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><radialGradient id='p' cx='0.25' cy='0.25' r='0.75'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient><radialGradient id='g' href='#p'/></defs><rect width='8' height='8' fill='url(#g)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.RadialGradient, geometry.Kind);
        AssertEqualRadial(new SvgRadialGradient(new SvgColor(255, 0, 0, 255), new SvgColor(0, 0, 255, 255), 0.25, 0.25, 0.75, 0.25, 0.25, 0, 0, 1, false, false, SvgGradientSpread.Pad, new SvgGradientStop[] { new(0, new SvgColor(255, 0, 0, 255)), new(1, new SvgColor(0, 0, 255, 255)) }, 1, 1, false, SvgGradientTransform.Identity, false), geometry.Radial.Gradient);
    }

    [Fact]
    public void Lowers_userspace_radial_gradient_with_user_circle()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><radialGradient id='g' gradientUnits='userSpaceOnUse' cx='4' cy='4' r='4'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><rect width='8' height='8' fill='url(#g)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.RadialGradient, geometry.Kind);
        AssertEqualRadial(new SvgRadialGradient(new SvgColor(255, 0, 0, 255), new SvgColor(0, 0, 255, 255), 4, 4, 4, 4, 4, 0, 0, 1, false, true, SvgGradientSpread.Pad, new SvgGradientStop[] { new(0, new SvgColor(255, 0, 0, 255)), new(1, new SvgColor(0, 0, 255, 255)) }, 1, 1, false, SvgGradientTransform.Identity, false), geometry.Radial.Gradient);
    }
    [Fact]
    public void Maps_userspace_radial_circle_through_uniform_transform()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><radialGradient id='g' gradientUnits='userSpaceOnUse' cx='4' cy='4' r='4'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><rect width='8' height='8' fill='url(#g)' transform='scale(2)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.RadialGradient, geometry.Kind);
        Assert.Equal(8, geometry.Radial.Gradient.CenterX);
        Assert.Equal(8, geometry.Radial.Gradient.CenterY);
        Assert.Equal(8, geometry.Radial.Gradient.Radius);
        Assert.Equal(8, geometry.Radial.Gradient.FocalX);
        Assert.Equal(8, geometry.Radial.Gradient.FocalY);
    }
    [Fact]
    public void Inherits_userspace_units_through_radial_href()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><radialGradient id='p' gradientUnits='userSpaceOnUse' cx='4' cy='4' r='4'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient><radialGradient id='g' href='#p'/></defs><rect width='8' height='8' fill='url(#g)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.RadialGradient, geometry.Kind);
        Assert.True(geometry.Radial.Gradient.IsUserSpace);
        Assert.Equal(4, geometry.Radial.Gradient.CenterX);
        Assert.Equal(4, geometry.Radial.Gradient.Radius);
    }

    [Fact]
    public void Rejects_percentage_userspace_radial_coordinates()
    {
        SvgException rejected = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><radialGradient id='g' gradientUnits='userSpaceOnUse' cx='50%' cy='4' r='4'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><rect width='8' height='8' fill='url(#g)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, rejected.ErrorCode);
    }
    [Fact]
    public void Admits_nonuniform_userspace_radial_fills_through_recorded_scales()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><radialGradient id='g' gradientUnits='userSpaceOnUse' cx='4' cy='4' r='4'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><g transform='scale(2,1)'><rect width='8' height='8' fill='url(#g)'/></g></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.RadialGradient, geometry.Kind);
        Assert.Equal(8, geometry.Radial.Gradient.CenterX);
        Assert.Equal(4, geometry.Radial.Gradient.CenterY);
        Assert.Equal(8, geometry.Radial.Gradient.Radius);
        Assert.Equal(2, geometry.Radial.Gradient.ReferenceScaleX);
        Assert.Equal(1, geometry.Radial.Gradient.ReferenceScaleY);
    }

    [Fact]
    public void Admits_nonuniform_userspace_radial_polygon_fills_through_recorded_scales()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><radialGradient id='g' gradientUnits='userSpaceOnUse' cx='4' cy='4' r='4'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><g transform='scale(2,1)'><polygon points='0,0 8,0 0,8' fill='url(#g)'/></g></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.RadialPolygon, geometry.Kind);
        Assert.True(geometry.RadialPolygon.HasFill);
        Assert.Equal(2, geometry.RadialPolygon.Gradient.ReferenceScaleX);
        Assert.Equal(1, geometry.RadialPolygon.Gradient.ReferenceScaleY);
    }

    [Fact]
    public void Validates_trailing_XML_after_nonuniform_radial_strokes()
    {
        // Keep the original missing </g>: admitting the stroke must still validate the trailing XML.
        SvgException rejected = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><radialGradient id='g' gradientUnits='userSpaceOnUse' cx='4' cy='4' r='4'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><g transform='scale(2,1)'><polygon points='0,0 8,0 0,8' fill='none' stroke='url(#g)' stroke-width='2'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.MalformedInput, rejected.ErrorCode);
    }

    [Fact]
    public void Admits_userspace_gradients_inside_scaled_viewports()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg' width='64' height='64'><defs><linearGradient id='g' gradientUnits='userSpaceOnUse' x1='0' y1='0' x2='8' y2='0'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><svg x='0' y='0' width='16' height='8' viewBox='0 0 8 8' preserveAspectRatio='none'><rect width='8' height='8' fill='url(#g)'/></svg></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.GradientFill, geometry.Kind);
        Assert.Equal(0, geometry.Gradient.Gradient.X1);
        Assert.Equal(16, geometry.Gradient.Gradient.X2);
        Assert.Equal(2, geometry.Gradient.Gradient.ReferenceScaleX);
        Assert.Equal(1, geometry.Gradient.Gradient.ReferenceScaleY);
    }

    [Fact]
    public void Admits_userspace_radial_inside_offset_viewports()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><radialGradient id='g' gradientUnits='userSpaceOnUse' cx='4' cy='4' r='4'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><svg x='8' y='0' width='16' height='16'><rect width='8' height='8' fill='url(#g)'/></svg></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.RadialGradient, geometry.Kind);
        Assert.Equal(12, geometry.Radial.Gradient.CenterX);
        Assert.Equal(4, geometry.Radial.Gradient.CenterY);
        Assert.Equal(4, geometry.Radial.Gradient.Radius);
        Assert.Equal(1, geometry.Radial.Gradient.ReferenceScaleX);
        Assert.Equal(1, geometry.Radial.Gradient.ReferenceScaleY);
    }

    [Fact]
    public void Lowers_repeat_spread_radial_gradients_with_retained_spread()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><radialGradient id='g' spreadMethod='repeat' cx='0.5' cy='0.5' r='0.5'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><rect width='8' height='8' fill='url(#g)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.RadialGradient, geometry.Kind);
        Assert.Equal(SvgGradientSpread.Repeat, geometry.Radial.Gradient.Spread);
    }

    [Fact]
    public void Lowers_reflect_spread_radial_gradients_with_retained_spread()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><radialGradient id='g' spreadMethod='reflect' cx='0.5' cy='0.5' r='0.5'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><rect width='8' height='8' fill='url(#g)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.RadialGradient, geometry.Kind);
        Assert.Equal(SvgGradientSpread.Reflect, geometry.Radial.Gradient.Spread);
    }

    [Fact]
    public void Inherits_radial_spread_through_href()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><radialGradient id='p' spreadMethod='reflect' cx='0.5' cy='0.5' r='0.5'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient><radialGradient id='g' href='#p'/></defs><rect width='8' height='8' fill='url(#g)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.RadialGradient, geometry.Kind);
        Assert.Equal(SvgGradientSpread.Reflect, geometry.Radial.Gradient.Spread);
    }

    [Fact]
    public void Overrides_radial_spread_through_href()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><radialGradient id='p' cx='0.5' cy='0.5' r='0.5'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient><radialGradient id='g' href='#p' spreadMethod='repeat'/></defs><rect width='8' height='8' fill='url(#g)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.RadialGradient, geometry.Kind);
        Assert.Equal(SvgGradientSpread.Repeat, geometry.Radial.Gradient.Spread);
    }

    [Fact]
    public void Admits_xlink_href_on_radial_gradient_definitions()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg' xmlns:xlink='http://www.w3.org/1999/xlink'><defs><radialGradient id='p' cx='0.5' cy='0.5' r='0.5'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient><radialGradient id='g' xlink:href='#p' spreadMethod='repeat'/></defs><rect width='8' height='8' fill='url(#g)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.RadialGradient, geometry.Kind);
        Assert.Equal(SvgGradientSpread.Repeat, geometry.Radial.Gradient.Spread);
    }

    [Fact]
    public void Admits_cross_type_radial_href_from_linear_parent()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg' xmlns:xlink='http://www.w3.org/1999/xlink'><defs><linearGradient id='a'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient><radialGradient id='b' xlink:href='#a' cx='0.5' cy='0.5' r='0.5'/></defs><rect width='8' height='8' fill='url(#b)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.RadialGradient, geometry.Kind);
        Assert.Equal(new SvgColor(255, 0, 0, 255), geometry.Radial.Gradient.Start);
        Assert.Equal(new SvgColor(0, 0, 255, 255), geometry.Radial.Gradient.End);
        Assert.Equal(2, geometry.Radial.Gradient.Stops.Length);
        Assert.Equal(0.5, geometry.Radial.Gradient.CenterX);
        Assert.Equal(0.5, geometry.Radial.Gradient.CenterY);
        Assert.Equal(0.5, geometry.Radial.Gradient.Radius);
    }

        [Fact]
    public void Admits_cross_type_linear_href_from_radial_parent()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns=\"http://www.w3.org/2000/svg\" xmlns:xlink=\"http://www.w3.org/1999/xlink\"><defs><radialGradient id=\"a\" cx=\"0.5\" cy=\"0.5\" r=\"0.5\"><stop offset=\"0\" stop-color=\"#ff0000\"/><stop offset=\"1\" stop-color=\"#0000ff\"/></radialGradient><linearGradient id=\"b\" xlink:href=\"#a\"/></defs><rect width=\"8\" height=\"8\" fill=\"url(#b)\"/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.GradientFill, geometry.Kind);
        Assert.Equal(new SvgColor(255, 0, 0, 255), geometry.Gradient.Gradient.Start);
        Assert.Equal(new SvgColor(0, 0, 255, 255), geometry.Gradient.Gradient.End);
        Assert.Equal(2, geometry.Gradient.Gradient.Stops.Length);
        Assert.Equal(0, geometry.Gradient.Gradient.X1);
        Assert.Equal(0, geometry.Gradient.Gradient.Y1);
        Assert.Equal(1, geometry.Gradient.Gradient.X2);
        Assert.Equal(0, geometry.Gradient.Gradient.Y2);
    }

[Fact]
    public void Lowers_inside_radial_focal()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><radialGradient id='g' cx='0.5' cy='0.5' r='0.5' fx='0.25' fy='0.5'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><rect width='8' height='8' fill='url(#g)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.RadialGradient, geometry.Kind);
        Assert.Equal(0.25, geometry.Radial.Gradient.FocalX);
        Assert.Equal(0.5, geometry.Radial.Gradient.FocalY);
        Assert.Equal(0.5, geometry.Radial.Gradient.CenterX);
        Assert.Equal(0.5, geometry.Radial.Gradient.Radius);
    }

    [Fact]
    public void Radial_focal_defaults_to_center()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><radialGradient id='g' cx='0.25' cy='0.75' r='0.25'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><rect width='8' height='8' fill='url(#g)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.RadialGradient, geometry.Kind);
        Assert.Equal(geometry.Radial.Gradient.CenterX, geometry.Radial.Gradient.FocalX);
        Assert.Equal(geometry.Radial.Gradient.CenterY, geometry.Radial.Gradient.FocalY);
    }

    [Fact]
    public void Inherits_radial_focal_through_href()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><radialGradient id='p' cx='0.5' cy='0.5' r='0.5' fx='0.25' fy='0.5'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient><radialGradient id='g' href='#p'/></defs><rect width='8' height='8' fill='url(#g)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.RadialGradient, geometry.Kind);
        Assert.Equal(0.25, geometry.Radial.Gradient.FocalX);
        Assert.Equal(0.5, geometry.Radial.Gradient.FocalY);
    }

    [Fact]
    public void Maps_userspace_radial_focal_through_uniform_transform()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><radialGradient id='g' gradientUnits='userSpaceOnUse' cx='4' cy='4' r='4' fx='2' fy='4'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><rect width='8' height='8' fill='url(#g)' transform='scale(2)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.RadialGradient, geometry.Kind);
        Assert.Equal(8, geometry.Radial.Gradient.CenterX);
        Assert.Equal(4, geometry.Radial.Gradient.FocalX);
        Assert.Equal(8, geometry.Radial.Gradient.FocalY);
        Assert.Equal(8, geometry.Radial.Gradient.Radius);
    }

    [Fact]
    public void Rejects_outside_radial_foci()
    {
        SvgException rejected = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><radialGradient id='g' cx='0.5' cy='0.5' r='0.5' fx='-0.5' fy='0.5'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><rect width='8' height='8' fill='url(#g)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, rejected.ErrorCode);
    }

    [Fact]
    public void Rejects_on_circle_radial_foci()
    {
        SvgException rejected = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><radialGradient id='g' cx='0.5' cy='0.5' r='0.5' fx='0.0' fy='0.5'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><rect width='8' height='8' fill='url(#g)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, rejected.ErrorCode);
    }

    [Fact]
    public void Rejects_percentage_userspace_radial_focal()
    {
        SvgException rejected = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><radialGradient id='g' gradientUnits='userSpaceOnUse' cx='4' cy='4' r='4' fx='50%'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><rect width='8' height='8' fill='url(#g)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, rejected.ErrorCode);
    }

    [Fact]
    public void Rejects_partial_radial_focal_overrides()
    {
        SvgException rejected = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><radialGradient id='p' cx='0.5' cy='0.5' r='0.5'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient><radialGradient id='g' href='#p' fx='0.25'/></defs><rect width='8' height='8' fill='url(#g)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, rejected.ErrorCode);
    }
    [Fact]
    public void Lowers_contained_focal_radius()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><radialGradient id='g' cx='0.5' cy='0.5' r='0.5' fr='0.1'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><rect width='8' height='8' fill='url(#g)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.RadialGradient, geometry.Kind);
        Assert.Equal(0.1, geometry.Radial.Gradient.FocalRadius);
        Assert.Equal(0.5, geometry.Radial.Gradient.FocalX);
    }

    [Fact]
    public void Lowers_offset_contained_focal_circle()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><radialGradient id='g' cx='0.5' cy='0.5' r='0.5' fx='0.25' fy='0.5' fr='0.1'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><rect width='8' height='8' fill='url(#g)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.RadialGradient, geometry.Kind);
        Assert.Equal(0.25, geometry.Radial.Gradient.FocalX);
        Assert.Equal(0.1, geometry.Radial.Gradient.FocalRadius);
    }

    [Fact]
    public void Inherits_focal_radius_through_href()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><radialGradient id='p' cx='0.5' cy='0.5' r='0.5' fr='0.1'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient><radialGradient id='g' href='#p'/></defs><rect width='8' height='8' fill='url(#g)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.RadialGradient, geometry.Kind);
        Assert.Equal(0.1, geometry.Radial.Gradient.FocalRadius);
    }

    [Fact]
    public void Maps_userspace_focal_radius_through_uniform_transform()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><radialGradient id='g' gradientUnits='userSpaceOnUse' cx='4' cy='4' r='4' fr='1'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><rect width='8' height='8' fill='url(#g)' transform='scale(2)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.RadialGradient, geometry.Kind);
        Assert.Equal(8, geometry.Radial.Gradient.Radius);
        Assert.Equal(2, geometry.Radial.Gradient.FocalRadius);
    }

    [Fact]
    public void Rejects_negative_focal_radius()
    {
        SvgException rejected = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><radialGradient id='g' cx='0.5' cy='0.5' r='0.5' fr='-0.1'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><rect width='8' height='8' fill='url(#g)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.MalformedInput, rejected.ErrorCode);
    }

    [Fact]
    public void Rejects_escaping_focal_circle()
    {
        SvgException rejected = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><radialGradient id='g' cx='0.5' cy='0.5' r='0.5' fx='0.25' fy='0.5' fr='0.3'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><rect width='8' height='8' fill='url(#g)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, rejected.ErrorCode);
    }

    [Fact]
    public void Rejects_focal_radius_covering_circle()
    {
        SvgException rejected = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><radialGradient id='g' cx='0.5' cy='0.5' r='0.5' fr='0.5'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><rect width='8' height='8' fill='url(#g)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, rejected.ErrorCode);
    }

    [Fact]
    public void Rejects_partial_focal_radius_overrides()
    {
        SvgException rejected = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><radialGradient id='p' cx='0.5' cy='0.5' r='0.5'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient><radialGradient id='g' href='#p' fr='0.1'/></defs><rect width='8' height='8' fill='url(#g)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, rejected.ErrorCode);
    }

    [Fact]
    public void Rejects_percentage_userspace_focal_radius()
    {
        SvgException rejected = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><radialGradient id='g' gradientUnits='userSpaceOnUse' cx='4' cy='4' r='4' fr='50%'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><rect width='8' height='8' fill='url(#g)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, rejected.ErrorCode);
    }
    [Fact]
    public void Lowers_translated_radial_gradients()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><radialGradient id='g' gradientTransform='translate(0.25)' cx='0.25' cy='0.5' r='0.5' fx='0.25' fy='0.5'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><rect width='8' height='8' fill='url(#g)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.RadialGradient, geometry.Kind);
        Assert.Equal(0.5, geometry.Radial.Gradient.CenterX);
        Assert.Equal(0.5, geometry.Radial.Gradient.CenterY);
        Assert.Equal(0.5, geometry.Radial.Gradient.Radius);
        Assert.Equal(0.5, geometry.Radial.Gradient.FocalX);
        Assert.Equal(0.5, geometry.Radial.Gradient.FocalY);
    }

    [Fact]
    public void Lowers_uniformly_scaled_radial_gradients()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><radialGradient id='g' gradientTransform='scale(2)' cx='0.25' cy='0.25' r='0.25'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><rect width='8' height='8' fill='url(#g)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.RadialGradient, geometry.Kind);
        Assert.Equal(0.5, geometry.Radial.Gradient.CenterX);
        Assert.Equal(0.5, geometry.Radial.Gradient.CenterY);
        Assert.Equal(0.5, geometry.Radial.Gradient.Radius);
        Assert.Equal(0.5, geometry.Radial.Gradient.FocalX);
        Assert.Equal(0.5, geometry.Radial.Gradient.FocalY);
    }

    [Fact]
    public void Lowers_uniform_matrix_radial_gradients()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><radialGradient id='g' gradientTransform='matrix(2, 0, 0, 2, 0.5, 0)' cx='0.25' cy='0.25' r='0.25'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><rect width='8' height='8' fill='url(#g)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.RadialGradient, geometry.Kind);
        Assert.Equal(1, geometry.Radial.Gradient.CenterX);
        Assert.Equal(0.5, geometry.Radial.Gradient.CenterY);
        Assert.Equal(0.5, geometry.Radial.Gradient.Radius);
    }

    [Fact]
    public void Applies_radial_transform_to_inherited_circles()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><radialGradient id='p' cx='0.5' cy='0.5' r='0.5'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient><radialGradient id='g' href='#p' gradientTransform='translate(-0.25)'/></defs><rect width='8' height='8' fill='url(#g)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.RadialGradient, geometry.Kind);
        Assert.Equal(0.25, geometry.Radial.Gradient.CenterX);
        Assert.Equal(0.5, geometry.Radial.Gradient.CenterY);
        Assert.Equal(0.5, geometry.Radial.Gradient.Radius);
        Assert.Equal(0.25, geometry.Radial.Gradient.FocalX);
    }

    [Fact]
    public void Admits_nonuniform_radial_gradient_transforms_through_recorded_scales()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><radialGradient id='g' gradientUnits='userSpaceOnUse' gradientTransform='scale(2,1)' cx='4' cy='4' r='4'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><rect width='8' height='8' fill='url(#g)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.RadialGradient, geometry.Kind);
        Assert.Equal(8, geometry.Radial.Gradient.CenterX);
        Assert.Equal(4, geometry.Radial.Gradient.CenterY);
        Assert.Equal(8, geometry.Radial.Gradient.Radius);
        Assert.Equal(2, geometry.Radial.Gradient.ReferenceScaleX);
        Assert.Equal(1, geometry.Radial.Gradient.ReferenceScaleY);
    }

    [Fact]
    public void Validates_offset_focal_before_radial_transform_application()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><radialGradient id='g' gradientUnits='userSpaceOnUse' gradientTransform='scale(1,2)' cx='4' cy='4' r='4' fx='4' fy='7.9'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><rect width='8' height='8' fill='url(#g)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.RadialGradient, geometry.Kind);
        Assert.Equal(4, geometry.Radial.Gradient.CenterX);
        Assert.Equal(8, geometry.Radial.Gradient.CenterY);
        Assert.Equal(4, geometry.Radial.Gradient.Radius);
    }

    [Fact]
    public void Rejects_on_circle_focal_with_nonuniform_radial_transform()
    {
        SvgException rejected = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><radialGradient id='g' gradientUnits='userSpaceOnUse' gradientTransform='scale(2,1)' cx='4' cy='4' r='4' fx='4' fy='8'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><rect width='8' height='8' fill='url(#g)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, rejected.ErrorCode);
    }

    [Fact]
    public void Admits_nonuniform_scale_radial_transforms()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><radialGradient id='g' gradientTransform='scale(2, 1)' cx='0.5' cy='0.5' r='0.5'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><rect width='8' height='8' fill='url(#g)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.RadialGradient, geometry.Kind);
        Assert.Equal(0.5, geometry.Radial.Gradient.CenterX);
        Assert.Equal(0.5, geometry.Radial.Gradient.Radius);
        Assert.True(geometry.Radial.Gradient.HasGeneralTransform);
    }

    [Fact]
    public void Admits_nonuniform_matrix_radial_transforms()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><radialGradient id='g' gradientTransform='matrix(2, 0, 0, 1, 0, 0)' cx='0.5' cy='0.5' r='0.5'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><rect width='8' height='8' fill='url(#g)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.RadialGradient, geometry.Kind);
        Assert.True(geometry.Radial.Gradient.HasGeneralTransform);
        (double x, double y) = geometry.Radial.Gradient.Inverse.Apply(1, 0);
        Assert.Equal(0.5, x);
        Assert.Equal(0, y);
    }
    [Fact]
    public void Admits_mirrored_radial_circles()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><radialGradient id='g' gradientUnits='userSpaceOnUse' cx='44' cy='32' r='22' fx='52' fy='32' gradientTransform='translate(64 0) scale(-1 1)'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><rect width='8' height='8' fill='url(#g)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.RadialGradient, geometry.Kind);
        Assert.Equal(20, geometry.Radial.Gradient.CenterX);
        Assert.Equal(32, geometry.Radial.Gradient.CenterY);
        Assert.Equal(22, geometry.Radial.Gradient.Radius);
        Assert.Equal(12, geometry.Radial.Gradient.FocalX);
        Assert.Equal(32, geometry.Radial.Gradient.FocalY);
    }

    [Fact]
    public void Admits_rotated_radial_circles()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><radialGradient id='g' gradientUnits='userSpaceOnUse' cx='4' cy='4' r='4' gradientTransform='rotate(45)'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><rect width='8' height='8' fill='url(#g)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.RadialGradient, geometry.Kind);
        double cosine = Math.Cos(Math.PI / 4);
        double sine = Math.Sin(Math.PI / 4);
        Assert.Equal(4 * cosine - 4 * sine, geometry.Radial.Gradient.CenterX);
        Assert.Equal(4 * sine + 4 * cosine, geometry.Radial.Gradient.CenterY);
        Assert.Equal(4 * Math.Sqrt(cosine * cosine + sine * sine), geometry.Radial.Gradient.Radius);
        Assert.Equal(geometry.Radial.Gradient.CenterX, geometry.Radial.Gradient.FocalX);
        Assert.Equal(geometry.Radial.Gradient.CenterY, geometry.Radial.Gradient.FocalY);
    }

    [Fact]
    public void Admits_rotated_scaled_radial_inverse_map()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><radialGradient id='g' gradientUnits='userSpaceOnUse' cx='4' cy='4' r='4' gradientTransform='rotate(45) scale(2 1)'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><rect width='8' height='8' fill='url(#g)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.RadialGradient, geometry.Kind);
        Assert.Equal(4, geometry.Radial.Gradient.CenterX);
        Assert.Equal(4, geometry.Radial.Gradient.CenterY);
        Assert.Equal(4, geometry.Radial.Gradient.Radius);
        Assert.True(geometry.Radial.Gradient.HasGeneralTransform);
        double cosine = Math.Cos(Math.PI / 4);
        double sine = Math.Sin(Math.PI / 4);
        (double x, double y) = geometry.Radial.Gradient.Inverse.Apply(2 * cosine, 2 * sine);
        Assert.Equal(1, x, 9);
        Assert.Equal(0, y, 9);
    }

    [Fact]
    public void Rejects_singular_radial_gradient_transforms()
    {
        SvgException rejected = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><radialGradient id='g' gradientUnits='userSpaceOnUse' cx='4' cy='4' r='4' gradientTransform='matrix(1 2 2 4 0 0)'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><rect width='8' height='8' fill='url(#g)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, rejected.ErrorCode);
        Assert.Contains("Singular radial gradient transforms are not implemented.", rejected.Message);
    }

    [Fact]
    public void Href_radial_vectors_keep_the_effective_general_transform()
    {
        const string parent = "<radialGradient id='p' gradientUnits='userSpaceOnUse' cx='4' cy='4' r='4'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient>";
        const string transform = "matrix(2 1 1 2 0 0)";
        byte[] alias = Document(parent + "<radialGradient id='g' href='#p' gradientTransform='" + transform + "'/>");
        byte[] standalone = Document(parent.Replace("id='p'", "id='g' gradientTransform='" + transform + "'", StringComparison.Ordinal));
        Assert.Equal(SvgConverter.ConvertToPng(standalone, SvgRenderOptions.Default, CancellationToken.None).ToArray(), SvgConverter.ConvertToPng(alias, SvgRenderOptions.Default, CancellationToken.None).ToArray());

        static byte[] Document(string gradients) => System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg'><defs>" + gradients + "</defs><rect width='8' height='8' fill='url(#g)'/></svg>");
    }
    [Fact]
    public void Rejects_partial_radial_vector_overrides()
    {
        SvgException rejected = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><radialGradient id='p' cx='0.5' cy='0.5' r='0.5'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient><radialGradient id='g' href='#p' cx='0.25'/></defs><rect width='8' height='8' fill='url(#g)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, rejected.ErrorCode);
    }
    [Fact]
    public void Rejects_negative_radial_radius()
    {
        SvgException rejected = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><radialGradient id='g' cx='0.5' cy='0.5' r='-0.5'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><rect width='8' height='8' fill='url(#g)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.MalformedInput, rejected.ErrorCode);
    }
    [Fact]
    public void Lowers_radial_rect_strokes_as_fill_bands()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><radialGradient id='g' cx='0.5' cy='0.5' r='0.5'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><rect width='8' height='8' fill='red' stroke='url(#g)' stroke-width='2'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(5, root.Geometries.Length);
        Assert.Equal(SvgGeometryKind.Rectangle, root.Geometries[0].Kind);
        Assert.All(root.Geometries.Skip(1), band => Assert.Equal(SvgGeometryKind.RadialGradient, band.Kind));
        Assert.Equal(new SvgRectangle(-1, -1, 10, 2, default, true, null), root.Geometries[1].Radial.Fill);
        Assert.Equal(new SvgRectangle(0, 0, 8, 8, default, true, null), root.Geometries[1].Radial.Box);
    }

    [Fact]
    public void Lowers_single_stop_radial_strokes_as_constant_bands()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><radialGradient id='g' cx='0.5' cy='0.5' r='0.5'><stop offset='0.3' stop-color='#ff0000'/></radialGradient></defs><rect width='8' height='8' fill='red' stroke='url(#g)' stroke-width='2'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(5, root.Geometries.Length);
        Assert.Equal(SvgGeometryKind.Rectangle, root.Geometries[0].Kind);
        Assert.All(root.Geometries.Skip(1), band => Assert.Equal(SvgGeometryKind.RadialGradient, band.Kind));
    }

    [Fact]
    public void Retains_degenerate_radial_strokes_as_constant_shader_bands()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><radialGradient id='g' cx='0.5' cy='0.5' r='0'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><rect width='8' height='8' fill='red' stroke='url(#g)' stroke-width='2'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(5, root.Geometries.Length);
        Assert.Equal(SvgGeometryKind.Rectangle, root.Geometries[0].Kind);
        Assert.Equal(new SvgColor(255, 0, 0, 255), root.Geometries[0].Rectangle.Color);
        Assert.All(root.Geometries.Skip(1), geometry =>
        {
            Assert.Equal(SvgGeometryKind.RadialGradient, geometry.Kind);
            Assert.Equal(0, geometry.Radial.Gradient.Radius);
            Assert.Equal(new SvgColor(0, 0, 255, 255), geometry.Radial.Gradient.End);
            Assert.True(geometry.Radial.Fill.HasFill);
        });
    }
    [Fact]
    public void Lowers_user_space_radial_line_strokes_as_native_contours()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><radialGradient id='g' gradientUnits='userSpaceOnUse' cx='4' cy='4' r='4'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><line x1='0' y1='4' x2='8' y2='4' stroke='url(#g)' stroke-width='2'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.RadialPolygon, geometry.Kind);
        AssertStrokeContourBounds(geometry, [new(0, 3, 8, 2, default, true, null)]);
        Assert.Equal(4, geometry.RadialPolygon.Gradient.CenterX);
    }

    [Fact]
    public void Bounding_box_radial_gradients_do_not_paint_zero_area_line_boxes()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><radialGradient id='g' cx='0.5' cy='0.5' r='0.5'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><line x1='0' y1='4' x2='8' y2='4' stroke='url(#g)' stroke-width='2'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.False(root.HasUnsupportedChild);
        Assert.False(Assert.Single(root.Geometries).RadialPolygon.HasFill);
        RenderBudget rasterBudget = new(1_000_000);
        using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root,
            new(new SvgViewport(8, 8), 1, SvgColor.Transparent, SvgRenderLimits.Default), rasterBudget, CancellationToken.None))
            Assert.All(surface.Pixels, component => Assert.Equal((byte)0, component));
        Assert.Equal(0, rasterBudget.Snapshot().CurrentBytes);
    }
    [Fact]
    public void Lowers_radial_fills_on_use_instances()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><radialGradient id='g' cx='0.5' cy='0.5' r='0.5'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient><rect id='r' x='0' y='0' width='4' height='8'/></defs><use href='#r' x='4' y='0' fill='url(#g)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.RadialGradient, geometry.Kind);
        Assert.Equal(new SvgRectangle(4, 0, 4, 8, default, true, null), geometry.Radial.Fill);
        AssertEqualRadial(new SvgRadialGradient(new SvgColor(255, 0, 0, 255), new SvgColor(0, 0, 255, 255), 0.5, 0.5, 0.5, 0.5, 0.5, 0, 0, 1, false, false, SvgGradientSpread.Pad, new SvgGradientStop[] { new(0, new SvgColor(255, 0, 0, 255)), new(1, new SvgColor(0, 0, 255, 255)) }, 1, 1, false, SvgGradientTransform.Identity, false), geometry.Radial.Gradient);
    }

    [Fact]
    public void Lowers_user_space_radial_fills_on_use_instances_with_placement_offsets()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><radialGradient id='g' gradientUnits='userSpaceOnUse' cx='0' cy='0' r='8'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient><rect id='r' x='0' y='0' width='4' height='8'/></defs><use href='#r' x='4' y='0' fill='url(#g)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.RadialGradient, geometry.Kind);
        Assert.Equal(new SvgRectangle(4, 0, 4, 8, default, true, null), geometry.Radial.Fill);
        Assert.Equal(4, geometry.Radial.Gradient.CenterX);
        Assert.Equal(0, geometry.Radial.Gradient.CenterY);
        Assert.Equal(8, geometry.Radial.Gradient.Radius);
    }

    [Fact]
    public void Lowers_symbol_paintserver_radial_on_use_instances()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><radialGradient id='g' cx='0.5' cy='0.5' r='0.5'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient><symbol id='s' viewBox='0 0 4 4'><rect x='0' y='0' width='4' height='4'/></symbol></defs><use href='#s' x='0' y='0' width='8' height='8' fill='url(#g)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.RadialGradient, geometry.Kind);
    }
    [Fact]
    public void Lowers_masked_radial_gradients()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><radialGradient id='g' cx='0.5' cy='0.5' r='0.5'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient><mask id='m'><rect x='0' y='0' width='16' height='16' fill='#ffffff'/></mask></defs><rect width='8' height='8' fill='url(#g)' mask='url(#m)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.RadialGradient, geometry.Kind);
        Assert.True(geometry.Radial.Fill.HasFill);
    }
    [Fact]
    public void Rejects_duplicate_linear_radial_gradient_ids()
    {
        SvgException rejected = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='g'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient><radialGradient id='g' cx='0.5' cy='0.5' r='0.5'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><rect width='8' height='8' fill='url(#g)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.MalformedInput, rejected.ErrorCode);
    }

    [Fact]
    public void Admits_translucent_gradient_stops_with_calibrated_alpha()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='g'><stop offset='0' stop-color='#ff0000' stop-opacity='0.5'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><rect width='8' height='8' fill='url(#g)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.GradientFill, geometry.Kind);
        Assert.Equal(2, geometry.Gradient.Gradient.Stops.Length);
        Assert.Equal(new SvgColor(255, 0, 0, 128), geometry.Gradient.Gradient.Stops[0].Color);
        Assert.Equal(new SvgColor(0, 0, 255, 255), geometry.Gradient.Gradient.Stops[1].Color);
    }

    [Fact]
    public void Admits_middle_translucent_stops_with_untouched_neighbors()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='g'><stop offset='0' stop-color='#ff0000'/><stop offset='0.5' stop-color='#00ff00' stop-opacity='0.5'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><rect width='8' height='8' fill='url(#g)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.GradientFill, geometry.Kind);
        Assert.Equal(3, geometry.Gradient.Gradient.Stops.Length);
        Assert.Equal(new SvgColor(255, 0, 0, 255), geometry.Gradient.Gradient.Stops[0].Color);
        Assert.Equal(new SvgColor(0, 255, 0, 128), geometry.Gradient.Gradient.Stops[1].Color);
        Assert.Equal(new SvgColor(0, 0, 255, 255), geometry.Gradient.Gradient.Stops[2].Color);
    }
    [Fact]
    public void Stamps_a_pattern_tile_across_its_fill_region()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><pattern id='tiles' width='16' height='16' patternUnits='userSpaceOnUse'><rect width='8' height='8' fill='red'/></pattern></defs><rect x='8' y='8' width='48' height='48' fill='url(#tiles)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.PatternFill, geometry.Kind);
        Assert.Equal(new SvgRectangle(0, 0, 8, 8, new SvgColor(255, 0, 0, 255), true, null), NativePatternRectangle(Assert.IsType<SvgVectorContent>(geometry.Pattern.VectorContent)));
        Assert.Equal(new SvgRectangle(8, 8, 48, 48, new SvgColor(0, 0, 0, 0), true, null), geometry.Pattern.Fill);
    }

    [Fact]
    public void Line_fill_servers_do_not_create_fill_geometry()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><pattern id='tiles' width='16' height='16' patternUnits='userSpaceOnUse'><rect width='8' height='8' fill='red'/></pattern></defs><line x1='0' y1='0' x2='4' y2='0' stroke='red' stroke-width='2' fill='url(#tiles)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);
        Assert.False(root.HasUnsupportedChild);
        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.AxisAlignedLine, geometry.Kind);
        Assert.Equal(new SvgColor(255, 0, 0, 255), geometry.AxisAlignedLine.Color);
    }

    [Fact]
    public void Admits_multi_rectangle_pattern_tiles()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><pattern id='tiles' width='16' height='16' patternUnits='userSpaceOnUse'><rect width='8' height='8' fill='red'/><rect x='8' y='8' width='8' height='8' fill='blue'/></pattern></defs><rect width='8' height='8' fill='url(#tiles)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.False(root.HasUnsupportedChild);
        Assert.Single(root.Geometries);
        SvgVectorContent content = Assert.IsType<SvgVectorContent>(root.Geometries[0].Pattern.VectorContent);
        Assert.Equal(2, content.Scene.Geometries.Length);
    }

    [Fact]
    public void Retains_default_object_bounding_box_pattern_units()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><pattern id='tiles' width='16' height='16'><rect width='8' height='8' fill='red'/></pattern></defs><rect width='8' height='8' fill='url(#tiles)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        SvgPatternCoordinates coordinates = Assert.IsType<SvgPatternCoordinates>(geometry.Pattern.Coordinates);
        Assert.True(coordinates.ObjectBoxTiles);
        Assert.False(coordinates.ObjectBoxContent);
        Assert.NotNull(coordinates.ObjectBox);
        Assert.Equal(128, coordinates.Resolve(0, 0, 16, 16, geometry.Pattern.ShaderPlacement, null).Width);
    }

    [Fact]
    public void Shifts_pattern_content_through_translate_transforms()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><pattern id='tiles' width='16' height='16' patternUnits='userSpaceOnUse' patternTransform='translate(2,3)'><rect width='8' height='8' fill='red'/></pattern></defs><rect x='8' y='8' width='48' height='48' fill='url(#tiles)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.PatternFill, geometry.Kind);
        Assert.Equal(new SvgRectangle(0, 0, 8, 8, new SvgColor(255, 0, 0, 255), true, null), NativePatternRectangle(Assert.IsType<SvgVectorContent>(geometry.Pattern.VectorContent)));
        Assert.Equal(SvgAffineTransform.FromTranslation(2, 3), geometry.Pattern.ShaderPlacement);
        Assert.Equal(0, geometry.Pattern.OriginX);
        Assert.Equal(0, geometry.Pattern.OriginY);
    }

    [Fact]
    public void Clips_pattern_tile_content_through_clip_paths()
    {
        using SvgRoot userSpace = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><clipPath id='c'><rect x='0' y='0' width='1' height='1'/></clipPath><pattern id='tiles' width='4' height='4' patternUnits='userSpaceOnUse'><rect width='2' height='2' fill='red' clip-path='url(#c)'/></pattern></defs><rect width='8' height='8' fill='url(#tiles)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);
        using SvgRoot boundingBox = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><clipPath id='c' clipPathUnits='objectBoundingBox'><rect x='0' y='0' width='0.5' height='0.5'/></clipPath><pattern id='tiles' width='4' height='4' patternUnits='userSpaceOnUse'><rect width='2' height='2' fill='red' clip-path='url(#c)'/></pattern></defs><rect width='8' height='8' fill='url(#tiles)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);
        // Clipped leaves use retained tile scenes. Protect their visible
        // coverage independently from the old single-rectangle storage shortcut.
        foreach (SvgRoot scene in new[] { userSpace, boundingBox })
        {
            RenderBudget budget = new(1_000_000);
            using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(scene,
                new(new SvgViewport(8, 8), 1, SvgColor.Transparent, SvgRenderLimits.Default), budget, CancellationToken.None))
                for (int y = 0; y < 8; y++) for (int x = 0; x < 8; x++)
                {
                    int offset = (y * 8 + x) * 4;
                    SvgColor expected = x % 4 == 0 && y % 4 == 0 ? new(255, 0, 0, 255) : SvgColor.Transparent;
                    Assert.Equal(expected, new SvgColor(surface.Pixels[offset], surface.Pixels[offset + 1], surface.Pixels[offset + 2], surface.Pixels[offset + 3]));
                }
            Assert.Equal(0, budget.Snapshot().CurrentBytes);
        }
    }

    [Fact]
    public void Admits_opacity_scale_and_stroke_on_pattern_tile_content()
    {
        using SvgRoot faded = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><pattern id='tiles' width='4' height='4' patternUnits='userSpaceOnUse'><rect width='2' height='2' fill='red' opacity='0.5'/></pattern></defs><rect width='8' height='8' fill='url(#tiles)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry fadedGeometry = Assert.Single(faded.Geometries);
        Assert.NotNull(fadedGeometry.Pattern.VectorContent);
        RenderBudget fadedBudget = new(1_000_000);
        using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(faded,
            new(new SvgViewport(8, 8), 1, SvgColor.Transparent, SvgRenderLimits.Default), fadedBudget, CancellationToken.None))
            for (int y = 0; y < 8; y++)
                for (int x = 0; x < 8; x++)
                    Assert.Equal(x % 4 < 2 && y % 4 < 2 ? new byte[] { 255, 0, 0, 128 } : new byte[4],
                        surface.Pixels.AsSpan((y * 8 + x) * 4, 4).ToArray());
        Assert.Equal(0, fadedBudget.Snapshot().CurrentBytes);
        using SvgRoot scaled = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><pattern id='tiles' width='4' height='4' patternUnits='userSpaceOnUse'><rect width='1' height='1' fill='red' transform='scale(2)'/></pattern></defs><rect width='8' height='8' fill='url(#tiles)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry scaledGeometry = Assert.Single(scaled.Geometries);
        Assert.Equal(new SvgRectangle(0, 0, 2, 2, new SvgColor(255, 0, 0, 255), true, null), NativePatternRectangle(Assert.IsType<SvgVectorContent>(scaledGeometry.Pattern.VectorContent)));

        using SvgRoot stroked = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><pattern id='tiles' width='4' height='4' patternUnits='userSpaceOnUse'><rect x='1' y='1' width='2' height='2' fill='red' stroke='blue' stroke-width='2'/></pattern></defs><rect width='8' height='8' fill='url(#tiles)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        RenderBudget rasterBudget = new(1_000_000);
        using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(stroked,
            new(new SvgViewport(8, 8), 1, SvgColor.Transparent, SvgRenderLimits.Default), rasterBudget, CancellationToken.None))
            for (int at = 0; at < surface.Pixels.Length; at += 4)
                Assert.Equal(new byte[] { 0, 0, 255, 255 }, surface.Pixels.AsSpan(at, 4).ToArray());
        Assert.Equal(0, rasterBudget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Original_pattern_tile_mask_renders_and_releases_sources()
    {
        byte[] input = "<svg xmlns='http://www.w3.org/2000/svg'><defs><mask id='m'><rect width='8' height='8' fill='#808080'/></mask><pattern id='tiles' width='4' height='4' patternUnits='userSpaceOnUse'><rect width='2' height='2' fill='red' mask='url(#m)'/></pattern></defs><rect width='8' height='8' fill='url(#tiles)'/></svg>"u8.ToArray();
        RenderBudget budget = new(8_000_000);
        using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None))
        using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, SvgRenderOptions.Default, budget, CancellationToken.None))
        {
            Assert.Equal(300, surface.Width); Assert.Equal(150, surface.Height);
            for (int y = 0; y < surface.Height; y++) for (int x = 0; x < surface.Width; x++)
            {
                SvgColor expected = x < 8 && y < 8 && x % 4 < 2 && y % 4 < 2 ? new(255, 0, 0, 128) : SvgColor.Transparent;
                int offset = (y * surface.Width + x) * 4;
                Assert.Equal(expected, new SvgColor(surface.Pixels[offset], surface.Pixels[offset + 1], surface.Pixels[offset + 2], surface.Pixels[offset + 3]));
            }
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Replays_shifted_pattern_content_on_its_native_grid()
    {
        using SvgRoot scene = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><pattern id='tiles' width='4' height='4' patternUnits='userSpaceOnUse' patternTransform='translate(3,0)'><rect width='2' height='2' fill='red'/></pattern></defs><rect width='8' height='8' fill='url(#tiles)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);
        Assert.NotNull(Assert.Single(scene.Geometries).Pattern.VectorContent);
    }

    [Fact]
    public void Clips_pattern_content_strokes_to_the_native_tile()
    {
        using SvgRoot scene = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><pattern id='tiles' width='4' height='4' patternUnits='userSpaceOnUse'><rect width='2' height='2' fill='red' stroke='blue' stroke-width='2'/></pattern></defs><rect width='8' height='8' fill='url(#tiles)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);
        Assert.NotNull(Assert.Single(scene.Geometries).Pattern.VectorContent);
    }

    [Fact]
    public void Retains_fractional_pattern_translation_on_vector_tile_paint()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><pattern id='tiles' width='16' height='16' patternUnits='userSpaceOnUse' patternTransform='translate(0.5)'><rect width='8' height='8' fill='red'/></pattern></defs><rect width='8' height='8' fill='url(#tiles)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);
        SvgPatternGeometry pattern = Assert.Single(root.Geometries).Pattern;
        Assert.NotNull(pattern.VectorContent);
        Assert.Equal(0.5, pattern.ShaderPlacement.E);
        Assert.Equal(0, pattern.ShaderPlacement.F);
    }

    [Theory]
    [InlineData("matrix(1 1 1 1 0 0)", SvgErrorCode.UnsupportedFeature)]
    [InlineData("translate()", SvgErrorCode.MalformedInput)]
    [InlineData("translate(1,2,3)", SvgErrorCode.MalformedInput)]
    public void Rejects_invalid_or_singular_pattern_transforms(string transform, SvgErrorCode code)
    {
        SvgException exception = Assert.Throws<SvgException>(() => SvgRootParser.Parse(System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg'><defs><pattern id='tiles' width='16' height='16' patternUnits='userSpaceOnUse' patternTransform='" + transform + "'><rect width='8' height='8' fill='red'/></pattern></defs><rect width='8' height='8' fill='url(#tiles)'/></svg>"), SvgRenderLimits.Default, CancellationToken.None));

        Assert.Equal(code, exception.ErrorCode);
    }

    [Theory]
    [InlineData("scale(2)", 2)]
    [InlineData("matrix(1,0,0,1,0,0)", 1)]
    public void Positive_axis_pattern_maps_preserve_the_content_edge_and_transparent_gap(string transform, int scale)
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'><defs><pattern id='tiles' width='16' height='16' patternUnits='userSpaceOnUse' patternTransform='" + transform + "'><rect width='8' height='8' fill='red'/></pattern></defs><rect width='32' height='32' fill='url(#tiles)'/></svg>");
        RenderBudget budget = new(1_000_000);
        using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None))
        using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, SvgRenderOptions.Default, budget, CancellationToken.None))
        {
            SvgPatternGeometry pattern = Assert.Single(root.Geometries).Pattern;
            Assert.Equal(scale, pattern.ShaderPlacement.A);
            Assert.Equal(new byte[] { 255, 0, 0, 255 }, surface.Pixels.AsSpan((8 * scale - 1) * 4, 4).ToArray());
            Assert.Equal(new byte[] { 0, 0, 0, 0 }, surface.Pixels.AsSpan(8 * scale * 4, 4).ToArray());
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Retains_a_uniform_embedded_image_without_allocating_a_pixel_array()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><image href='data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGP4z8DwHwAFAAH/iZk9HQAAAABJRU5ErkJggg==' x='8' y='8' width='48' height='48'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgImageGeometry placedImage = Assert.Single(root.Geometries).Image;
        Assert.Equal((8d, 8d, 48d, 48d), (placedImage.Box.X, placedImage.Box.Y, placedImage.Box.Width, placedImage.Box.Height));
        Assert.Equal(new SvgConstantShaderPaint(255, 0, 0, 255), placedImage.UniformSample);
        SvgPathFillAssertions.MatchesIntegerBands(root, [new(8, 8, 48, 48, new(255, 0, 0, 255), true, null)]);
    }

    [Fact]
    public void Defaults_dimless_embedded_images_to_the_decoded_intrinsic_size()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg' width='64' height='64'><image href='data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGP4z8DwHwAFAAH/iZk9HQAAAABJRU5ErkJggg==' x='8' y='8'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgImageGeometry placedImage = Assert.Single(root.Geometries).Image;
        Assert.Equal((8d, 8d, 1d, 1d), (placedImage.Box.X, placedImage.Box.Y, placedImage.Box.Width, placedImage.Box.Height));
        Assert.Equal(new SvgConstantShaderPaint(255, 0, 0, 255), placedImage.UniformSample);
        SvgPathFillAssertions.MatchesIntegerBands(root, [new(8, 8, 1, 1, new(255, 0, 0, 255), true, null)]);
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
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><mask id='fade'><rect x='8' y='8' width='48' height='48' fill='#808080'/></mask></defs><rect x='8' y='8' width='48' height='48' fill='#ff0000' mask='url(#fade)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(new SvgRectangle(8, 8, 48, 48, new SvgColor(255, 0, 0, 255), true, null), Assert.Single(root.Rectangles));
        SvgLeafMaskAssertions.UniformScope(root, 128, null);
    }
    [Fact]
    public void Applies_a_gray_mask_to_a_diagonal_polygon()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><mask id='fade' maskUnits='userSpaceOnUse' x='16' y='16' width='32' height='32'><rect x='8' y='8' width='48' height='48' fill='#808080'/></mask></defs><polygon points='8,8 56,8 8,56' fill='#ff0000' mask='url(#fade)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.PolygonFill, geometry.Kind);
        Assert.Equal(new SvgColor(255, 0, 0, 255), geometry.Polygon.Color);
        Assert.Null(geometry.Polygon.Clip);
        SvgLeafMaskAssertions.UniformScope(root, 128, new(16, 16, 32, 32));
    }

    [Fact]
    public void Lowers_diagonal_gradient_polygons_as_gradient_fills()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='g'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><polygon points='0,0 8,0 0,8' fill='url(#g)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.GradientPolygon, geometry.Kind);
        Assert.True(geometry.GradientPolygon.HasFill);
        Assert.Equal(new SvgRectangle(0, 0, 8, 8, default, true, null), geometry.GradientPolygon.Box);
    }

    [Fact]
    public void Lowers_diagonal_radial_polygons_as_radial_fills()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><radialGradient id='g' cx='0.5' cy='0.5' r='0.5'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><polygon points='0,0 8,0 0,8' fill='url(#g)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.RadialPolygon, geometry.Kind);
        Assert.True(geometry.RadialPolygon.HasFill);
        Assert.Equal(new SvgRectangle(0, 0, 8, 8, default, true, null), geometry.RadialPolygon.Box);
    }

    [Fact]
    public void Lowers_userspace_gradient_strokes_on_polygons_as_outlines()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='g' gradientUnits='userSpaceOnUse' x1='0' y1='0' x2='8' y2='0'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><polygon points='0,0 8,0 0,8' fill='none' stroke='url(#g)' stroke-width='2'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(2, root.Geometries.Length);
        Assert.Equal(SvgGeometryKind.PolygonFill, root.Geometries[0].Kind);
        Assert.False(root.Geometries[0].Polygon.HasFill);
        Assert.Equal(SvgGeometryKind.GradientPolygon, root.Geometries[1].Kind);
        Assert.True(root.Geometries[1].GradientPolygon.HasFill);
    }

    [Fact]
    public void Lowers_radial_strokes_on_polygons_as_outlines()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><radialGradient id='g' gradientUnits='userSpaceOnUse' cx='4' cy='4' r='4'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><polygon points='0,0 8,0 0,8' fill='none' stroke='url(#g)' stroke-width='2'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(2, root.Geometries.Length);
        Assert.Equal(SvgGeometryKind.PolygonFill, root.Geometries[0].Kind);
        Assert.Equal(SvgGeometryKind.RadialPolygon, root.Geometries[1].Kind);
        Assert.True(root.Geometries[1].RadialPolygon.HasFill);
    }

    [Fact]
    public void Lowers_bounding_box_radial_strokes_on_polygons_as_outlines()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><radialGradient id='g' cx='0.5' cy='0.5' r='0.5'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><polygon points='0,0 8,0 0,8' fill='none' stroke='url(#g)' stroke-width='2'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(2, root.Geometries.Length);
        Assert.Equal(SvgGeometryKind.RadialPolygon, root.Geometries[1].Kind);
        Assert.True(root.Geometries[1].RadialPolygon.HasFill);
        Assert.Equal(new SvgRectangle(0, 0, 8, 8, default, true, null), root.Geometries[1].RadialPolygon.Box);
    }
    [Fact]
    public void Lowers_userspace_gradient_strokes_on_curved_paths_as_outlines()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='g' gradientUnits='userSpaceOnUse' x1='0' y1='0' x2='8' y2='0'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><path d='M0,8 Q4,-4 8,8' fill='none' stroke='url(#g)' stroke-width='2'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.NotEmpty(root.Geometries);
        Assert.All(root.Geometries, outline => Assert.Equal(SvgGeometryKind.GradientPolygon, outline.Kind));
        Assert.All(root.Geometries, outline => Assert.True(outline.GradientPolygon.HasFill));
    }

    [Fact]
    public void Lowers_bounding_box_gradient_strokes_on_polygons_as_outlines()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='g' x1='0' y1='0' x2='1' y2='0'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><polygon points='0,0 8,0 0,8' fill='none' stroke='url(#g)' stroke-width='2'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(2, root.Geometries.Length);
        Assert.Equal(SvgGeometryKind.GradientPolygon, root.Geometries[1].Kind);
        Assert.True(root.Geometries[1].GradientPolygon.HasFill);
        Assert.Equal(new SvgRectangle(0, 0, 8, 8, default, true, null), root.Geometries[1].GradientPolygon.Box);
    }

    [Fact]
    public void Lowers_bounding_box_gradient_strokes_on_paths_as_outlines()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='g' x1='0' y1='0' x2='1' y2='0'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><path d='M0,0 L8,0 L0,8 Z' fill='none' stroke='url(#g)' stroke-width='2'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry outline = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.GradientPolygon, outline.Kind);
        Assert.True(outline.GradientPolygon.HasFill);
        Assert.Equal(new SvgRectangle(0, 0, 8, 8, default, true, null), outline.GradientPolygon.Box);
    }

    [Fact]
    public void Lowers_bounding_box_radial_strokes_on_paths_as_outlines()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><radialGradient id='g' cx='0.5' cy='0.5' r='0.5'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><path d='M0,0 L8,0 L0,8 Z' fill='none' stroke='url(#g)' stroke-width='2'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry outline = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.RadialPolygon, outline.Kind);
        Assert.True(outline.RadialPolygon.HasFill);
        Assert.Equal(new SvgRectangle(0, 0, 8, 8, default, true, null), outline.RadialPolygon.Box);
    }

    [Fact]
    public void Lowers_pattern_strokes_on_polygons_as_outlines()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><pattern id='p' width='8' height='8' patternUnits='userSpaceOnUse'><rect width='8' height='8' fill='red'/></pattern></defs><polygon points='0,0 8,0 0,8' fill='none' stroke='url(#p)' stroke-width='2'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(2, root.Geometries.Length);
        Assert.Equal(SvgGeometryKind.PolygonFill, root.Geometries[0].Kind);
        Assert.Equal(SvgGeometryKind.PatternPolygon, root.Geometries[1].Kind);
        Assert.True(root.Geometries[1].PatternPolygon.HasFill);
    }

    [Fact]
    public void Lowers_pattern_strokes_on_paths_as_outlines()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><pattern id='p' width='8' height='8' patternUnits='userSpaceOnUse'><rect width='8' height='8' fill='red'/></pattern></defs><path d='M0,0 L8,0 L0,8 Z' fill='none' stroke='url(#p)' stroke-width='2'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry outline = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.PatternPolygon, outline.Kind);
        Assert.True(outline.PatternPolygon.HasFill);
    }

    [Fact]
    public void Lowers_pattern_strokes_on_curved_paths_as_outlines()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><pattern id='p' width='8' height='8' patternUnits='userSpaceOnUse'><rect width='8' height='8' fill='red'/></pattern></defs><path d='M0,8 Q4,-4 8,8' fill='none' stroke='url(#p)' stroke-width='2'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.NotEmpty(root.Geometries);
        Assert.All(root.Geometries, outline => Assert.Equal(SvgGeometryKind.PatternPolygon, outline.Kind));
        Assert.All(root.Geometries, outline => Assert.True(outline.PatternPolygon.HasFill));
    }

    [Fact]
    public void Empty_pattern_tiles_leave_outlines_unpainted()
    {
        byte[] input = "<svg xmlns='http://www.w3.org/2000/svg'><defs><pattern id='e' width='8' height='8' patternUnits='userSpaceOnUse'></pattern></defs><polygon points='0,0 8,0 0,8' fill='none' stroke='url(#e)' stroke-width='2'/></svg>"u8.ToArray();
        using SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, CancellationToken.None);
        SvgRectangle bounds = SvgMaskSyntax.BoundingBox(Assert.Single(root.Geometries));
        Assert.Equal((0d, 0d, 8d, 8d), (bounds.X, bounds.Y, bounds.Width, bounds.Height));
        Assert.Equal(SvgConverter.ConvertToPng("<svg xmlns='http://www.w3.org/2000/svg'/>"u8.ToArray(), SvgRenderOptions.Default, CancellationToken.None).ToArray(),
            SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None).ToArray());
    }
    [Fact]
    public void Lowers_bounding_box_gradient_strokes_on_curved_paths_as_outlines()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='g' x1='0' y1='0' x2='1' y2='0'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><path d='M0,8 Q4,-4 8,8' fill='none' stroke='url(#g)' stroke-width='2'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry outline = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.GradientPolygon, outline.Kind);
        Assert.True(outline.GradientPolygon.HasFill);
        Assert.Equal(new SvgRectangle(0, 2, 8, 6, default, true, null), outline.GradientPolygon.Box);
    }

    [Fact]
    public void Lowers_bounding_box_radial_strokes_on_curved_paths_as_outlines()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><radialGradient id='g' cx='0.5' cy='0.5' r='0.5'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><path d='M0,8 Q4,-4 8,8' fill='none' stroke='url(#g)' stroke-width='2'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry outline = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.RadialPolygon, outline.Kind);
        Assert.True(outline.RadialPolygon.HasFill);
        Assert.Equal(new SvgRectangle(0, 2, 8, 6, default, true, null), outline.RadialPolygon.Box);
    }

    [Fact]
    public void Lowers_bounding_box_gradient_strokes_on_fractional_paths_as_outlines()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='g' x1='0' y1='0' x2='1' y2='0'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><path d='M0.5,0 L8,0 L0,8 Z' fill='none' stroke='url(#g)' stroke-width='2'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry outline = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.GradientPolygon, outline.Kind);
        Assert.True(outline.GradientPolygon.HasFill);
        Assert.Equal(new SvgRectangle(0, 0, 8, 8, default, true, null), outline.GradientPolygon.Box);
    }

    [Fact]
    public void Lowers_gradient_strokes_on_fractional_polygons_as_outlines()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='g' x1='0' y1='0' x2='1' y2='0'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><polygon points='0.5,0 8,0 0,8' fill='none' stroke='url(#g)' stroke-width='2'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(2, root.Geometries.Length);
        Assert.Equal(SvgGeometryKind.PolygonFill, root.Geometries[0].Kind);
        Assert.Equal(SvgGeometryKind.GradientPolygon, root.Geometries[1].Kind);
        Assert.True(root.Geometries[1].GradientPolygon.HasFill);
        Assert.Equal(FillBounds(root.Geometries[0]), root.Geometries[1].GradientPolygon.Box);
    }

    [Fact]
    public void Lowers_radial_strokes_on_fractional_polygons_as_outlines()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><radialGradient id='g' gradientUnits='userSpaceOnUse' cx='4' cy='4' r='4'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><polygon points='0.5,0 8,0 0,8' fill='none' stroke='url(#g)' stroke-width='2'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(2, root.Geometries.Length);
        Assert.Equal(SvgGeometryKind.RadialPolygon, root.Geometries[1].Kind);
        Assert.True(root.Geometries[1].RadialPolygon.HasFill);
    }

    [Fact]
    public void Lowers_pattern_strokes_on_fractional_polygons_as_outlines()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><pattern id='p' width='8' height='8' patternUnits='userSpaceOnUse'><rect width='8' height='8' fill='red'/></pattern></defs><polygon points='0.5,0 8,0 0,8' fill='none' stroke='url(#p)' stroke-width='2'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(2, root.Geometries.Length);
        Assert.Equal(SvgGeometryKind.PatternPolygon, root.Geometries[1].Kind);
        Assert.True(root.Geometries[1].PatternPolygon.HasFill);
    }

    private static SvgRectangle FillBounds(SvgGeometry fill)
    {
        Assert.Equal(SvgGeometryKind.PolygonFill, fill.Kind);
        double left = double.PositiveInfinity;
        double top = double.PositiveInfinity;
        double right = double.NegativeInfinity;
        double bottom = double.NegativeInfinity;
        foreach (SvgDevicePoint vertex in fill.Polygon.Points)
        {
            left = Math.Min(left, vertex.X);
            top = Math.Min(top, vertex.Y);
            right = Math.Max(right, vertex.X);
            bottom = Math.Max(bottom, vertex.Y);
        }
        return new SvgRectangle(left, top, right - left, bottom - top, default, true, null);
    }
    [Fact]
    public void Applies_a_white_mask_to_a_circle_without_changing_content()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><mask id='fade'><rect x='0' y='0' width='32' height='32' fill='#ffffff'/></mask></defs><circle cx='16' cy='16' r='8' fill='#ff0000' mask='url(#fade)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.PolygonFill, geometry.Kind);
        Assert.Equal(new SvgColor(255, 0, 0, 255), geometry.Polygon.Color);
    }

    [Fact]
    public void Applies_a_gray_mask_to_a_stroked_polygon_pair()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><defs><mask id='fade'><rect x='0' y='0' width='16' height='16' fill='#808080'/></mask></defs><polygon points='4,4 12,4 4,12' fill='#ff0000' stroke='#0000ff' stroke-width='2' mask='url(#fade)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(2, root.Geometries.Length);
        foreach (SvgGeometry geometry in root.Geometries)
        {
            Assert.Equal(SvgGeometryKind.PolygonFill, geometry.Kind);
            Assert.Equal(255, geometry.Polygon.Color.Alpha);
        }
        SvgLeafMaskAssertions.UniformScope(root, 128, null);
    }

    [Fact]
    public void Applies_a_gray_mask_to_a_gradient_outline_pair()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><defs><linearGradient id='g' gradientUnits='userSpaceOnUse' x1='0' y1='0' x2='16' y2='0'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient><mask id='fade'><rect x='0' y='0' width='16' height='16' fill='#808080'/></mask></defs><polygon points='4,4 12,4 4,12' fill='#ff0000' stroke='url(#g)' stroke-width='2' mask='url(#fade)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(2, root.Geometries.Length);
        Assert.Equal(SvgGeometryKind.PolygonFill, root.Geometries[0].Kind);
        Assert.Equal(255, root.Geometries[0].Polygon.Color.Alpha);
        Assert.Equal(SvgGeometryKind.GradientPolygon, root.Geometries[1].Kind);
        Assert.Equal(255, root.Geometries[1].GradientPolygon.Opacity);
        SvgLeafMaskAssertions.UniformScope(root, 128, null);
    }

    [Fact]
    public void Applies_a_gray_mask_to_a_radial_outline_pair()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><defs><radialGradient id='g' gradientUnits='userSpaceOnUse' cx='8' cy='8' r='8'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient><mask id='fade'><rect x='0' y='0' width='16' height='16' fill='#808080'/></mask></defs><polygon points='4,4 12,4 4,12' fill='#ff0000' stroke='url(#g)' stroke-width='2' mask='url(#fade)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(2, root.Geometries.Length);
        Assert.Equal(SvgGeometryKind.RadialPolygon, root.Geometries[1].Kind);
        Assert.Equal(255, root.Geometries[1].RadialPolygon.Opacity);
        SvgLeafMaskAssertions.UniformScope(root, 128, null);
    }

    [Fact]
    public void Applies_a_gray_mask_to_a_pattern_outline_pair()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><defs><pattern id='p' width='8' height='8' patternUnits='userSpaceOnUse'><rect width='8' height='8' fill='red'/></pattern><mask id='fade'><rect x='0' y='0' width='16' height='16' fill='#808080'/></mask></defs><polygon points='4,4 12,4 4,12' fill='#ff0000' stroke='url(#p)' stroke-width='2' mask='url(#fade)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(2, root.Geometries.Length);
        Assert.Equal(SvgGeometryKind.PatternPolygon, root.Geometries[1].Kind);
        Assert.Equal(255, root.Geometries[1].PatternPolygon.Opacity);
        SvgLeafMaskAssertions.UniformScope(root, 128, null);
    }

    [Fact]
    public void Applies_a_gray_mask_to_a_polygon_use_instance()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><mask id='fade'><rect x='0' y='0' width='8' height='8' fill='#808080'/></mask></defs><defs><polygon id='p' points='0,0 8,0 0,8' fill='#ff0000'/></defs><use href='#p' mask='url(#fade)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.PolygonFill, geometry.Kind);
        Assert.Equal(new SvgColor(255, 0, 0, 255), geometry.Polygon.Color);
        SvgLeafMaskAssertions.UniformScope(root, 128, null);
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
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><mask id='fade'/></defs><rect x='8' y='8' width='48' height='48' fill='#ff0000' mask='url(#fade)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(new SvgRectangle(8, 8, 48, 48, new SvgColor(255, 0, 0, 255), true, null), Assert.Single(root.Rectangles));
        SvgLeafMaskAssertions.UniformScope(root, 0, null);
    }


    [Fact]
    public void Clips_masked_content_to_explicit_regions()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><mask id='window' maskUnits='userSpaceOnUse' x='16' y='16' width='32' height='32'><rect x='0' y='0' width='64' height='64' fill='#808080'/></mask></defs><rect x='8' y='8' width='48' height='48' fill='#ff0000' mask='url(#window)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(new SvgRectangle(8, 8, 48, 48, new SvgColor(255, 0, 0, 255), true, null), Assert.Single(root.Rectangles));
        SvgLeafMaskAssertions.UniformScope(root, 128, new(16, 16, 32, 32));
    }

    [Fact]
    public void Clips_masked_content_to_bounding_box_regions()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><mask id='window' x='0' y='0' width='1' height='1'><rect x='0' y='0' width='8' height='8' fill='#808080'/></mask></defs><rect width='8' height='8' fill='red' mask='url(#window)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(new SvgRectangle(0, 0, 8, 8, new SvgColor(255, 0, 0, 255), true, null), Assert.Single(root.Rectangles));
        SvgLeafMaskAssertions.UniformScope(root, 128, new(0, 0, 8, 8));
    }

    [Fact]
    public void Bounding_box_region_partials_fall_back_to_specification_defaults()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><mask id='window' x='0.25' width='0.5'><rect x='-8' y='-8' width='24' height='24' fill='#808080'/></mask></defs><rect width='8' height='8' fill='red' mask='url(#window)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(new SvgRectangle(0, 0, 8, 8, new SvgColor(255, 0, 0, 255), true, null), Assert.Single(root.Rectangles));
        SvgLeafMaskAssertions.UniformScope(root, 128, new(2, -0.8, 4, 9.6));
    }

    [Fact]
    public void Negative_bounding_box_region_dimensions_are_malformed()
    {
        SvgException exception = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><mask id='window' x='0' y='0' width='-1' height='1'><rect x='0' y='0' width='8' height='8' fill='#808080'/></mask></defs><rect width='8' height='8' fill='red' mask='url(#window)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.MalformedInput, exception.ErrorCode);
    }

    [Fact]
    public void Applies_bounding_box_regions_on_masked_groups()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><mask id='window' x='0' y='0' width='1' height='1'><rect x='0' y='0' width='8' height='8' fill='#808080'/></mask></defs><g mask='url(#window)'><rect width='8' height='8' fill='red'/></g></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgDrawCommand begin = Assert.Single(root.Commands, command => command.Kind == SvgDrawCommandKind.BeginGroup);
        Assert.Equal(255, begin.Opacity);
        Assert.Equal(128, begin.MaskAlpha);
        Assert.Equal(new SvgClipBox(0, 0, 8, 8), begin.MaskRegion);
        Assert.Contains(root.Commands, command => command.Kind == SvgDrawCommandKind.EndGroup);
    }

    [Fact]
    public void Clips_masked_use_content_to_bounding_box_regions()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><rect id='r' x='8' y='8' width='48' height='48' fill='#ff0000'/></defs><defs><mask id='window' x='0.25' y='0.25' width='0.5' height='0.5'><rect x='0' y='0' width='64' height='64' fill='#808080'/></mask></defs><use href='#r' mask='url(#window)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(new SvgRectangle(8, 8, 48, 48, new SvgColor(255, 0, 0, 255), true, null), Assert.Single(root.Rectangles));
        SvgLeafMaskAssertions.UniformScope(root, 128, new(20, 20, 24, 24));
    }

    [Fact]
    public void Original_partial_user_space_region_uses_default_extents()
    {
        AssertSolidRectanglePixels("<svg xmlns='http://www.w3.org/2000/svg'><defs><mask id='window' maskUnits='userSpaceOnUse' x='1' y='1'><rect x='0' y='0' width='8' height='8' fill='#808080'/></mask></defs><rect width='8' height='8' fill='red' mask='url(#window)'/></svg>"u8.ToArray(), new(1, 1, 7, 7, new(255, 0, 0, 128), true, null), 300, 150);
    }

    [Fact]
    public void Rejects_unknown_mask_unit_values()
    {
        SvgException exception = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><mask id='window' maskUnits='picas'><rect x='0' y='0' width='8' height='8' fill='#808080'/></mask></defs><rect width='8' height='8' fill='red' mask='url(#window)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, exception.ErrorCode);
    }

    [Fact]
    public void Original_object_box_mask_content_preserves_gray_coverage_and_cleanup()
    {
        byte[] input = "<svg xmlns='http://www.w3.org/2000/svg'><defs><mask id='window' maskContentUnits='objectBoundingBox'><rect x='0' y='0' width='8' height='8' fill='#808080'/></mask></defs><rect width='8' height='8' fill='red' mask='url(#window)'/></svg>"u8.ToArray();
        RenderBudget sceneBudget = new(1_000_000), rasterBudget = new(1_000_000);
        using (SvgRoot scene = SvgRootParser.Parse(input, SvgRenderLimits.Default, sceneBudget, CancellationToken.None))
        using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(scene, new(new SvgViewport(8, 8), 1, SvgColor.Transparent, SvgRenderLimits.Default), rasterBudget, CancellationToken.None))
            for (int pixel = 0; pixel < 64; pixel++)
            {
                Assert.Equal(255, surface.Pixels[pixel * 4]); Assert.Equal(0, surface.Pixels[pixel * 4 + 1]);
                Assert.Equal(0, surface.Pixels[pixel * 4 + 2]); Assert.Equal(128, surface.Pixels[pixel * 4 + 3]);
            }
        Assert.Equal(0, sceneBudget.Snapshot().CurrentBytes); Assert.Equal(0, rasterBudget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Clips_masked_use_content_to_explicit_regions()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><rect id='tile' width='8' height='8' fill='red'/><mask id='window' maskUnits='userSpaceOnUse' x='1' y='1' width='2' height='2'><rect x='0' y='0' width='8' height='8' fill='#808080'/></mask></defs><use href='#tile' mask='url(#window)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(new SvgRectangle(0, 0, 8, 8, new SvgColor(255, 0, 0, 255), true, null), Assert.Single(root.Rectangles));
        SvgLeafMaskAssertions.UniformScope(root, 128, new(1, 1, 2, 2));
    }

    [Fact]
    public void Clips_masked_symbol_content_to_explicit_regions()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><symbol id='s' viewBox='0 0 8 8'><polygon points='0,0 8,0 0,8' fill='red'/></symbol></defs><use href='#s' width='8' height='8' mask='url(#m)'/><defs><mask id='m' maskUnits='userSpaceOnUse' x='2' y='2' width='4' height='4'><rect x='0' y='0' width='8' height='8' fill='#808080'/></mask></defs></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.PolygonFill, geometry.Kind);
        Assert.Equal(new SvgColor(255, 0, 0, 255), geometry.Polygon.Color);
        Assert.True(geometry.Polygon.Clip.HasValue);
        Assert.Equal(new SvgClipBox(0, 0, 8, 8), geometry.Polygon.Clip.Value);
        SvgLeafMaskAssertions.UniformScope(root, 128, new(2, 2, 4, 4));
    }

    [Fact]
    public void Applies_forward_custom_region_masks_to_use_instances()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><rect id='tile' width='8' height='8' fill='red'/></defs><use href='#tile' mask='url(#m)'/><defs><mask id='m' maskUnits='userSpaceOnUse' x='1' y='1' width='2' height='2'><rect x='0' y='0' width='8' height='8' fill='#808080'/></mask></defs></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(new SvgRectangle(0, 0, 8, 8, new SvgColor(255, 0, 0, 255), true, null), Assert.Single(root.Rectangles));
        SvgLeafMaskAssertions.UniformScope(root, 128, new(1, 1, 2, 2));
    }

    [Fact]
    public void Original_viewport_use_mask_maps_its_region()
    {
        AssertSolidRectanglePixels("<svg xmlns='http://www.w3.org/2000/svg' width='64' height='64'><defs><rect id='r' x='0' y='0' width='8' height='8' fill='red'/><mask id='m' maskUnits='userSpaceOnUse' x='0' y='0' width='8' height='8'><rect x='0' y='0' width='16' height='16' fill='#808080'/></mask></defs><svg x='8' y='8' width='48' height='48'><use href='#r' mask='url(#m)'/></svg></svg>"u8.ToArray(), new(8, 8, 8, 8, new(255, 0, 0, 128), true, null), 64, 64);
    }

    [Fact]
    public void Applies_custom_mask_regions_on_masked_groups()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><mask id='window' maskUnits='userSpaceOnUse' x='1' y='1' width='2' height='2'><rect x='0' y='0' width='8' height='8' fill='#808080'/></mask></defs><g mask='url(#window)'><rect width='8' height='8' fill='red'/></g></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgDrawCommand begin = Assert.Single(root.Commands, command => command.Kind == SvgDrawCommandKind.BeginGroup);
        Assert.Equal(255, begin.Opacity);
        Assert.Equal(128, begin.MaskAlpha);
        Assert.Equal(new SvgClipBox(1, 1, 2, 2), begin.MaskRegion);
        Assert.Contains(root.Commands, command => command.Kind == SvgDrawCommandKind.EndGroup);
    }

    [Fact]
    public void Original_chromatic_mask_preserves_nonoverlapping_empty_output_and_releases_sources()
    {
        RenderBudget budget = new(8_000_000);
        using (SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><mask id='fade'><rect x='8' y='8' width='48' height='48' fill='#ff0000'/></mask></defs><rect width='8' height='8' fill='red' mask='url(#fade)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, budget, CancellationToken.None))
        using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, SvgRenderOptions.Default, budget, CancellationToken.None))
            Assert.All(surface.Pixels, value => Assert.Equal(0, value));
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Retains_default_white_mask_regions_around_groups()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><mask id='fade'><rect x='0' y='0' width='48' height='48' fill='#ffffff'/></mask></defs><g mask='url(#fade)'><rect width='8' height='8' fill='red'/></g></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgLeafMaskAssertions.UniformGroupScope(root, 255, null);
    }
    [Fact]
    public void Retains_default_white_mask_regions_around_polygon_groups()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><mask id='fade'><rect x='0' y='0' width='8' height='8' fill='#ffffff'/></mask></defs><g mask='url(#fade)'><polygon points='0,0 8,0 0,8' fill='red'/></g></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgLeafMaskAssertions.UniformGroupScope(root, 255, null);
        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.PolygonFill, geometry.Kind);
    }

    [Fact]
    public void Isolates_gray_masked_groups_before_applying_the_mask_value()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><mask id='fade'><rect x='0' y='0' width='48' height='48' fill='#808080'/></mask></defs><g mask='url(#fade)'><rect width='8' height='8' fill='red'/></g></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgLeafMaskAssertions.UniformGroupScope(root, 128, null);
        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.Rectangle, geometry.Kind);
        Assert.Equal(new SvgColor(255, 0, 0, 255), geometry.Rectangle.Color);
    }
    [Fact]
    public void Isolates_gray_masked_polygon_groups_without_changing_the_child_paint()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><mask id='fade'><rect x='0' y='0' width='8' height='8' fill='#808080'/></mask></defs><g mask='url(#fade)'><polygon points='0,0 8,0 0,8' fill='red'/></g></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgLeafMaskAssertions.UniformGroupScope(root, 128, null);
        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.PolygonFill, geometry.Kind);
        Assert.Equal(new SvgColor(255, 0, 0, 255), geometry.Polygon.Color);
    }

    [Fact]
    public void Retains_group_opacity_and_mask_values_as_separate_layer_operations()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><mask id='fade'><rect x='0' y='0' width='48' height='48' fill='#808080'/></mask></defs><g opacity='0.5' mask='url(#fade)'><rect width='8' height='8' fill='red'/></g></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Collection(root.Commands,
            command =>
            {
                Assert.Equal(SvgDrawCommandKind.BeginGroup, command.Kind);
                Assert.True(command.RecordingScope.Value >= 0);
                Assert.Equal(new SvgPaintOpacity(128, 0.5f), command.SourceOpacity);
                Assert.Equal(128, command.MaskAlpha);
                Assert.Equal(new SvgClipBox(-0.8, -0.8, 9.6, 9.6), command.MaskRegion);
                Assert.Equal(-1, command.MaskContentIndex);
            },
            command => Assert.Equal(new SvgDrawCommand(SvgDrawCommandKind.Geometry, 0, 255, null, -1, 255), command),
            command => Assert.Equal(new SvgDrawCommand(SvgDrawCommandKind.EndGroup, -1, 255, null, -1, 255), command));
    }

    [Fact]
    public void Crops_masked_groups_with_partial_mask_content()
    {
        RenderBudget budget = new(1_000_000);
        using (SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><mask id='m'><rect x='0' y='0' width='4' height='8' fill='#808080'/></mask></defs><g mask='url(#m)'><rect x='0' y='0' width='4' height='4' fill='red'/><rect x='4' y='4' width='4' height='4' fill='blue'/></g></svg>"u8.ToArray(), SvgRenderLimits.Default, budget, CancellationToken.None))
        using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, new(new SvgViewport(8, 8), 1, SvgColor.Transparent, SvgRenderLimits.Default), budget, CancellationToken.None))
        {
            Assert.Equal(new byte[] { 255, 0, 0, 128 }, surface.Pixels.AsSpan((1 * 8 + 1) * 4, 4).ToArray());
            Assert.Equal(new byte[] { 0, 0, 0, 0 }, surface.Pixels.AsSpan((5 * 8 + 5) * 4, 4).ToArray());
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Admits_masked_groups_with_covering_mask_content()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><mask id='m'><rect x='0' y='0' width='8' height='8' fill='#808080'/></mask></defs><g mask='url(#m)'><rect x='0' y='0' width='4' height='4' fill='red'/><rect x='4' y='4' width='4' height='4' fill='blue'/></g></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(2, root.Geometries.Length);
    }
    [Fact]
    public void Isolates_backward_uniform_masks_around_expanded_grouped_use_content()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><mask id='m'><rect x='0' y='0' width='8' height='8' fill='#808080'/></mask></defs><g mask='url(#m)'><use href='#p'/></g><defs><g id='p' fill='red'><rect width='8' height='4'/><rect y='4' width='4' height='4'/></g></defs></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgLeafMaskAssertions.UniformGroupScope(root, 128, null);
        Assert.Equal(2, root.Geometries.Length);
        Assert.All(root.Geometries, geometry => Assert.Equal(SvgGeometryKind.Rectangle, geometry.Kind));
        Assert.All(root.Geometries, geometry => Assert.Equal(new SvgColor(255, 0, 0, 255), geometry.Rectangle.Color));
    }

    [Fact]
    public void Crops_masked_groups_with_forward_uses_outside_mask_content()
    {
        RenderBudget budget = new(1_000_000);
        using (SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><mask id='m'><rect x='0' y='0' width='4' height='4' fill='#808080'/></mask></defs><g mask='url(#m)'><use href='#r'/></g><defs><rect id='r' x='0' y='0' width='8' height='8'/></defs></svg>"u8.ToArray(), SvgRenderLimits.Default, budget, CancellationToken.None))
        using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, new(new SvgViewport(8, 8), 1, SvgColor.Transparent, SvgRenderLimits.Default), budget, CancellationToken.None))
        {
            Assert.Equal(new byte[] { 0, 0, 0, 128 }, surface.Pixels.AsSpan((1 * 8 + 1) * 4, 4).ToArray());
            Assert.Equal(new byte[] { 0, 0, 0, 0 }, surface.Pixels.AsSpan((5 * 8 + 5) * 4, 4).ToArray());
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Crops_masked_groups_with_strokes_outside_mask_content()
    {
        RenderBudget budget = new(1_000_000);
        using (SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><mask id='m'><rect x='0' y='0' width='4' height='4' fill='#808080'/></mask></defs><g mask='url(#m)'><rect x='0' y='0' width='4' height='4' fill='red' stroke='blue' stroke-width='2'/></g></svg>"u8.ToArray(), SvgRenderLimits.Default, budget, CancellationToken.None))
        using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, new(new SvgViewport(8, 8), 1, SvgColor.Transparent, SvgRenderLimits.Default), budget, CancellationToken.None))
        {
            Assert.Equal(new byte[] { 0, 0, 255, 128 }, surface.Pixels.AsSpan((0 * 8 + 0) * 4, 4).ToArray());
            Assert.Equal(new byte[] { 0, 0, 0, 0 }, surface.Pixels.AsSpan((5 * 8 + 5) * 4, 4).ToArray());
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Empty_mask_intersection_preserves_nested_opacity_cleanup()
    {
        RenderBudget budget = new(1_000_000);
        using (SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><mask id='fade'><rect x='8' y='8' width='48' height='48' fill='#808080'/></mask></defs><g mask='url(#fade)'><g opacity='0.5'><rect width='8' height='8' fill='red'/></g></g></svg>"u8.ToArray(), SvgRenderLimits.Default, budget, CancellationToken.None))
        using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, new(new SvgViewport(8, 8), 1, SvgColor.Transparent, SvgRenderLimits.Default), budget, CancellationToken.None))
        {
            Assert.Equal(new byte[] { 0, 0, 0, 0 }, surface.Pixels.AsSpan((0 * 8 + 0) * 4, 4).ToArray());
            Assert.Equal(new byte[] { 0, 0, 0, 0 }, surface.Pixels.AsSpan((5 * 8 + 5) * 4, 4).ToArray());
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Rejects_missing_mask_references_on_groups()
    {
        SvgException exception = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><g mask='url(#missing)'><rect width='8' height='8' fill='red'/></g></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, exception.ErrorCode);
    }

    [Fact]
    public void Admits_sibling_after_empty_masked_group()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><mask id='m'><rect x='0' y='0' width='4' height='4' fill='#808080'/></mask></defs><g mask='url(#m)'/><rect x='0' y='0' width='8' height='8' fill='red'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Single(root.Geometries);
    }

    [Fact]
    public void Rejects_text_inside_masked_groups()
    {
        SvgException exception = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><mask id='m'><rect x='0' y='0' width='8' height='8' fill='#808080'/></mask></defs><g mask='url(#m)'><text font-family='Example'>A</text></g></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, exception.ErrorCode);
    }

    [Fact]
    public void Rejects_unknown_mask_references()
    {
        SvgException exception = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><rect width='8' height='8' fill='red' mask='url(#missing)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, exception.ErrorCode);
    }

    private static void AssertStrokeContourBounds(SvgGeometry geometry, SvgRectangle[] expected)
    {
        (SvgDevicePoint[] points, int[] ends) = geometry.Kind switch
        {
            SvgGeometryKind.PolygonFill => (geometry.Polygon.Points, geometry.Polygon.ContourEnds),
            SvgGeometryKind.GradientPolygon => (geometry.GradientPolygon.Points, geometry.GradientPolygon.ContourEnds),
            SvgGeometryKind.RadialPolygon => (geometry.RadialPolygon.Points, geometry.RadialPolygon.ContourEnds),
            SvgGeometryKind.PatternPolygon => (geometry.PatternPolygon.Points, geometry.PatternPolygon.ContourEnds),
            _ => throw new InvalidOperationException("Expected one shader stroke outline.")
        };
        Assert.Equal(expected.Length, ends.Length);
        int start = 0;
        for (int contour = 0; contour < ends.Length; contour++)
        {
            double left = double.PositiveInfinity, top = double.PositiveInfinity;
            double right = double.NegativeInfinity, bottom = double.NegativeInfinity;
            for (int point = start; point < ends[contour]; point++)
            {
                left = Math.Min(left, points[point].X); top = Math.Min(top, points[point].Y);
                right = Math.Max(right, points[point].X); bottom = Math.Max(bottom, points[point].Y);
            }
            Assert.Equal(expected[contour], new(left, top, right - left, bottom - top, default, true, null));
            start = ends[contour];
        }
        Assert.Equal(points.Length, start);
    }

    private static void AssertEqualGradient(SvgLinearGradient expected, SvgLinearGradient actual)
    {
        Assert.Equal(expected.Start, actual.Start);
        Assert.Equal(expected.End, actual.End);
        Assert.Equal(expected.X1, actual.X1);
        Assert.Equal(expected.Y1, actual.Y1);
        Assert.Equal(expected.X2, actual.X2);
        Assert.Equal(expected.Y2, actual.Y2);
        Assert.Equal(expected.Offset1, actual.Offset1);
        Assert.Equal(expected.Offset2, actual.Offset2);
        Assert.Equal(expected.IsEmpty, actual.IsEmpty);
        Assert.Equal(expected.IsUserSpace, actual.IsUserSpace);
        Assert.Equal(expected.Spread, actual.Spread);
        Assert.Equal(expected.ReferenceScaleX, actual.ReferenceScaleX);
        Assert.Equal(expected.ReferenceScaleY, actual.ReferenceScaleY);
        Assert.Equal(expected.Stops, actual.Stops);
    }

    private static void AssertEqualRadial(SvgRadialGradient expected, SvgRadialGradient actual)
    {
        Assert.Equal(expected.Start, actual.Start);
        Assert.Equal(expected.End, actual.End);
        Assert.Equal(expected.CenterX, actual.CenterX);
        Assert.Equal(expected.CenterY, actual.CenterY);
        Assert.Equal(expected.Radius, actual.Radius);
        Assert.Equal(expected.FocalX, actual.FocalX);
        Assert.Equal(expected.FocalY, actual.FocalY);
        Assert.Equal(expected.FocalRadius, actual.FocalRadius);
        Assert.Equal(expected.Offset1, actual.Offset1);
        Assert.Equal(expected.Offset2, actual.Offset2);
        Assert.Equal(expected.IsEmpty, actual.IsEmpty);
        Assert.Equal(expected.IsUserSpace, actual.IsUserSpace);
        Assert.Equal(expected.Spread, actual.Spread);
        Assert.Equal(expected.Stops, actual.Stops);
        Assert.Equal(expected.ReferenceScaleX, actual.ReferenceScaleX);
        Assert.Equal(expected.ReferenceScaleY, actual.ReferenceScaleY);
        Assert.Equal(expected.Inverse, actual.Inverse);
        Assert.Equal(expected.HasGeneralTransform, actual.HasGeneralTransform);
    }

    [Fact]
    public void Admits_zero_exporter_root_positions()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg' x='0px' y='0px' width='12' height='7'><rect width='1' height='1' fill='red'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(12, root.Width?.Value);
        Assert.Equal(7, root.Height?.Value);
        Assert.Single(root.Rectangles);
        Assert.False(root.HasUnsupportedChild);
    }

    [Fact]
    public void Ignores_outermost_root_positions_when_rendering()
    {
        // SVG 1.1 gives x and y no effect on the outermost svg element.
        byte[] plain = "<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><rect x='1' y='1' width='2' height='2' fill='red'/></svg>"u8.ToArray();
        byte[] offset = "<svg xmlns='http://www.w3.org/2000/svg' x='8' y='4' width='5' height='5'><rect x='1' y='1' width='2' height='2' fill='red'/></svg>"u8.ToArray();

        ReadOnlyMemory<byte> plainPng = SvgConverter.ConvertToPng(plain, SvgRenderOptions.Default, CancellationToken.None);
        ReadOnlyMemory<byte> offsetPng = SvgConverter.ConvertToPng(offset, SvgRenderOptions.Default, CancellationToken.None);

        Assert.True(plainPng.Span.SequenceEqual(offsetPng.Span));
    }

    [Fact]
    public void Rejects_percentage_root_positions()
    {
        SvgException exception = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg' x='50%'/>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, exception.ErrorCode);
    }
    [Fact]
    public void Admits_preserve_root_xml_space()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg' xml:space='preserve' width='12' height='7'><rect width='1' height='1' fill='red'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(12, root.Width?.Value);
        Assert.Equal(7, root.Height?.Value);
        Assert.Single(root.Rectangles);
        Assert.False(root.HasUnsupportedChild);
    }

    [Fact]
    public void Ignores_root_xml_space_when_rendering_without_text()
    {
        byte[] plain = "<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><rect x='1' y='1' width='2' height='2' fill='red'/></svg>"u8.ToArray();
        byte[] spaced = "<svg xmlns='http://www.w3.org/2000/svg' xml:space='preserve' width='5' height='5'><rect x='1' y='1' width='2' height='2' fill='red'/></svg>"u8.ToArray();

        ReadOnlyMemory<byte> plainPng = SvgConverter.ConvertToPng(plain, SvgRenderOptions.Default, CancellationToken.None);
        ReadOnlyMemory<byte> spacedPng = SvgConverter.ConvertToPng(spaced, SvgRenderOptions.Default, CancellationToken.None);

        Assert.True(plainPng.Span.SequenceEqual(spacedPng.Span));
    }

    [Fact]
    public void Rejects_unknown_root_xml_space()
    {
        SvgException exception = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg' xml:space='bogus'/>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.MalformedInput, exception.ErrorCode);
    }
    [Fact]
    public void Admits_illustrator_root_enable_background()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg' enable-background='new 0 0 595.28 841.89' width='12' height='7'><rect width='1' height='1' fill='red'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(12, root.Width?.Value);
        Assert.Equal(7, root.Height?.Value);
        Assert.Single(root.Rectangles);
        Assert.False(root.HasUnsupportedChild);
    }

    [Fact]
    public void Ignores_root_enable_background_when_rendering()
    {
        byte[] plain = "<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><rect x='1' y='1' width='2' height='2' fill='red'/></svg>"u8.ToArray();
        byte[] transparent = "<svg xmlns='http://www.w3.org/2000/svg' enable-background='new' width='5' height='5'><rect x='1' y='1' width='2' height='2' fill='red'/></svg>"u8.ToArray();

        ReadOnlyMemory<byte> plainPng = SvgConverter.ConvertToPng(plain, SvgRenderOptions.Default, CancellationToken.None);
        ReadOnlyMemory<byte> backdropPng = SvgConverter.ConvertToPng(transparent, SvgRenderOptions.Default, CancellationToken.None);

        Assert.True(plainPng.Span.SequenceEqual(backdropPng.Span));
    }

    [Fact]
    public void Rejects_filter_style_root_enable_background()
    {
        SvgException exception = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg' enable-background='blur(5)'/>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, exception.ErrorCode);
    }
    [Fact]
    public void Admits_empty_defs_with_id()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs id='defs1'/><rect width='1' height='1' fill='red'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Single(root.Rectangles);
        Assert.False(root.HasUnsupportedChild);
    }

    [Fact]
    public void Ignores_empty_id_defs_when_rendering()
    {
        byte[] plain = "<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><rect x='1' y='1' width='2' height='2' fill='red'/></svg>"u8.ToArray();
        byte[] defined = "<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><defs id='defs1'/><rect x='1' y='1' width='2' height='2' fill='red'/></svg>"u8.ToArray();

        ReadOnlyMemory<byte> plainPng = SvgConverter.ConvertToPng(plain, SvgRenderOptions.Default, CancellationToken.None);
        ReadOnlyMemory<byte> definedPng = SvgConverter.ConvertToPng(defined, SvgRenderOptions.Default, CancellationToken.None);

        Assert.True(plainPng.Span.SequenceEqual(definedPng.Span));
    }

    [Fact]
    public void Rejects_defs_with_non_id_attributes()
    {
        SvgException exception = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs transform='scale(2)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, exception.ErrorCode);
    }

    [Fact]
    public void Rejects_overlong_defs_ids()
    {
        string id = new('a', SvgSyntaxLimits.MaximumIdentifierCharacters + 1);
        SvgException exception = Assert.Throws<SvgException>(() => SvgRootParser.Parse(System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg'><defs id='" + id + "'/></svg>"), SvgRenderLimits.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode);
    }
    [Fact]
    public void Ignores_clip_rule_on_regular_paths_when_rendering()
    {
        byte[] plain = "<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><rect x='1' y='1' width='2' height='2' fill='red'/></svg>"u8.ToArray();
        byte[] ruled = "<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><rect x='1' y='1' width='2' height='2' fill='red' clip-rule='evenodd'/></svg>"u8.ToArray();

        ReadOnlyMemory<byte> plainPng = SvgConverter.ConvertToPng(plain, SvgRenderOptions.Default, CancellationToken.None);
        ReadOnlyMemory<byte> ruledPng = SvgConverter.ConvertToPng(ruled, SvgRenderOptions.Default, CancellationToken.None);

        Assert.True(plainPng.Span.SequenceEqual(ruledPng.Span));
    }

    [Fact]
    public void Ignores_clip_rule_on_groups_when_rendering()
    {
        byte[] plain = "<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><g><rect x='1' y='1' width='2' height='2' fill='red'/></g></svg>"u8.ToArray();
        byte[] ruled = "<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><g clip-rule='evenodd'><rect x='1' y='1' width='2' height='2' fill='red'/></g></svg>"u8.ToArray();

        ReadOnlyMemory<byte> plainPng = SvgConverter.ConvertToPng(plain, SvgRenderOptions.Default, CancellationToken.None);
        ReadOnlyMemory<byte> ruledPng = SvgConverter.ConvertToPng(ruled, SvgRenderOptions.Default, CancellationToken.None);

        Assert.True(plainPng.Span.SequenceEqual(ruledPng.Span));
    }
    [Fact]
    public void Ignores_clip_rule_on_root_when_rendering()
    {
        byte[] plain = "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"5\" height=\"5\"><rect x=\"1\" y=\"1\" width=\"2\" height=\"2\" fill=\"red\"/></svg>"u8.ToArray();
        byte[] ruled = "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"5\" height=\"5\" clip-rule=\"evenodd\"><rect x=\"1\" y=\"1\" width=\"2\" height=\"2\" fill=\"red\"/></svg>"u8.ToArray();

        ReadOnlyMemory<byte> plainPng = SvgConverter.ConvertToPng(plain, SvgRenderOptions.Default, CancellationToken.None);
        ReadOnlyMemory<byte> ruledPng = SvgConverter.ConvertToPng(ruled, SvgRenderOptions.Default, CancellationToken.None);

        Assert.True(plainPng.Span.SequenceEqual(ruledPng.Span));
    }
    [Fact]
    public void Rejects_invalid_root_clip_rule()
    {
        SvgException exception = Assert.Throws<SvgException>(() => SvgRootParser.Parse(System.Text.Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"5\" height=\"5\" clip-rule=\"bogus\"><rect x=\"1\" y=\"1\" width=\"2\" height=\"2\" fill=\"red\"/></svg>"), SvgRenderLimits.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, exception.ErrorCode);
    }
    [Fact]
    public void Admits_root_class_without_stylesheet()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg' class='external-icon' width='12' height='7'><rect width='1' height='1' fill='red'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Single(root.Rectangles);
        Assert.False(root.HasUnsupportedChild);
    }

    [Fact]
    public void Ignores_root_class_when_rendering()
    {
        byte[] plain = "<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><rect x='1' y='1' width='2' height='2' fill='red'/></svg>"u8.ToArray();
        byte[] classified = "<svg xmlns='http://www.w3.org/2000/svg' class='external-icon' width='5' height='5'><rect x='1' y='1' width='2' height='2' fill='red'/></svg>"u8.ToArray();

        ReadOnlyMemory<byte> plainPng = SvgConverter.ConvertToPng(plain, SvgRenderOptions.Default, CancellationToken.None);
        ReadOnlyMemory<byte> classifiedPng = SvgConverter.ConvertToPng(classified, SvgRenderOptions.Default, CancellationToken.None);

        Assert.True(plainPng.Span.SequenceEqual(classifiedPng.Span));
    }

    [Fact]
    public void Root_class_stylesheet_supplies_inherited_fill()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg' class='external-icon'><style>.external-icon{fill:blue}</style><rect width='1' height='1'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(new SvgColor(0, 0, 255, 255), Assert.Single(root.Rectangles).Color);
        Assert.False(root.HasUnsupportedChild);
    }

    [Fact]
    public void Admits_unmatched_class_stylesheet_with_root_class()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg' class='external-icon'><style>.other{fill:blue}</style><rect width='1' height='1' fill='red'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Single(root.Rectangles);
        Assert.False(root.HasUnsupportedChild);
    }

    [Fact]
    public void Rejects_overlong_root_class()
    {
        string classValue = new('a', SvgSyntaxLimits.MaximumIdentifierCharacters + 1);
        SvgException exception = Assert.Throws<SvgException>(() => SvgRootParser.Parse(System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' class='" + classValue + "'/>"), SvgRenderLimits.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode);
    }
    private static void AssertEquivalentPixels(byte[] input, byte[] expected)
    {
        Assert.Equal(Render(expected), Render(input));

        static byte[] Render(byte[] source)
        {
            RenderBudget budget = new(8_000_000);
            byte[] png;
            using (SvgRoot root = SvgRootParser.Parse(source, SvgRenderLimits.Default, budget, CancellationToken.None))
                png = SvgConverter.ConvertToPng(root, SvgRenderOptions.Default, budget, CancellationToken.None).ToArray();
            Assert.Equal(0, budget.Snapshot().CurrentBytes);
            return png;
        }
    }

    private static void AssertSolidRectanglePixels(byte[] input, SvgRectangle visible, int width, int height)
    {
        RenderBudget budget = new(8_000_000);
        using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None))
        using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, SvgRenderOptions.Default, budget, CancellationToken.None))
        {
            Assert.Equal(width, surface.Width);
            Assert.Equal(height, surface.Height);
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                SvgColor expected = x >= visible.X && x < visible.X + visible.Width && y >= visible.Y && y < visible.Y + visible.Height ? visible.Color : SvgColor.Transparent;
                int offset = (y * width + x) * 4;
                Assert.Equal(expected, new SvgColor(surface.Pixels[offset], surface.Pixels[offset + 1], surface.Pixels[offset + 2], surface.Pixels[offset + 3]));
            }
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    private static SvgRectangle NativePatternRectangle(SvgVectorContent content)
    {
        SvgGeometry geometry = Assert.Single(content.Scene.Geometries);
        Assert.Equal(SvgGeometryKind.Rectangle, geometry.Kind);
        return geometry.Rectangle;
    }

}

