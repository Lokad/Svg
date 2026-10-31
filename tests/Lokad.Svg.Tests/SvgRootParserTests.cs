using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

using static Lokad.Svg.Tests.SvgPatternContentAssertions;

namespace Lokad.Svg.Tests;

[Trait("Layer", "Mechanism")]
public sealed class SvgRootParserTests
{
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
        SvgRenderLimits limits = new(1024, 16, 16, 1024, 16, 16, 16, 2);

        SvgException exception = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg' width='1' height='1'/>"u8.ToArray(), limits, CancellationToken.None));

        Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode);
    }

    [Fact]
    public void Rejects_child_attributes_above_the_configured_limit()
    {
        SvgRenderLimits limits = new(1024, 16, 16, 1024, 16, 16, 16, 2);

        SvgException exception = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><rect width='1' height='1' fill='red'/></svg>"u8.ToArray(), limits, CancellationToken.None));

        Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode);
    }

    [Fact]
    public void Parses_decimal_root_dimensions()
    {
        using SvgRoot root = SvgRootParser.Parse(System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='12.5' height='7'/>"), SvgRenderLimits.Default, CancellationToken.None);
        Assert.Equal(12.5, root.Width?.Value);
        Assert.Equal(7, root.Height?.Value);
        Assert.False(root.Width?.IsPercentage);
        Assert.False(root.HasUnsupportedChild);
    }

    [Fact]
    public void Records_visible_child_elements_for_the_scene_stage()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><rect width='1' height='1' fill='#ff0000'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);
        Assert.Single(root.Rectangles);
    }

    [Fact]
    public void Expands_a_prior_rectangle_definition_through_use()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><rect id='tile' x='1' y='2' width='3' height='4' fill='red'/></defs><use href='#tile' x='5' y='6'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Single(root.Rectangles);
        Assert.Equal(new SvgRectangle(6, 8, 3, 4, new SvgColor(255, 0, 0, 255), true, null), root.Rectangles[0]);
    }

    [Fact]
    public void Expands_a_group_nested_rectangle_definition_through_use()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><g><defs><rect id='tile' x='1' y='2' width='3' height='4' fill='red'/></defs></g><use href='#tile' x='5' y='6'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Single(root.Rectangles);
        Assert.Equal(new SvgRectangle(6, 8, 3, 4, new SvgColor(255, 0, 0, 255), true, null), root.Rectangles[0]);
        Assert.False(root.HasUnsupportedChild);
    }

    [Fact]
    public void Expands_a_group_nested_definition_for_a_local_use_instance()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><g><defs><rect id='tile' width='2' height='2' fill='red'/></defs><use href='#tile'/></g></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Single(root.Rectangles);
        Assert.Equal(new SvgRectangle(0, 0, 2, 2, new SvgColor(255, 0, 0, 255), true, null), root.Rectangles[0]);
        Assert.False(root.HasUnsupportedChild);
    }

    [Fact]
    public void Expands_a_group_use_with_offsets()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><g id='tile'><rect width='2' height='2' fill='red'/></g><use href='#tile' x='5' y='6'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(2, root.Rectangles.Length);
        Assert.Equal(new SvgRectangle(0, 0, 2, 2, new SvgColor(255, 0, 0, 255), true, null), root.Rectangles[0]);
        Assert.Equal(new SvgRectangle(5, 6, 2, 2, new SvgColor(255, 0, 0, 255), true, null), root.Rectangles[1]);
        Assert.False(root.HasUnsupportedChild);
    }

    [Fact]
    public void Expands_nested_group_uses_transitively()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><g id='inner'><rect id='r' width='2' height='2' fill='red'/><use href='#r' x='2'/></g><use href='#inner' y='4'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

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
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><use href='#later' x='4'/><g id='later'><rect width='2' height='2' fill='red'/></g></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

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
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><g transform='translate(5,0)'><g id='inner'><rect width='2' height='2' fill='red'/></g></g><use href='#inner'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(2, root.Geometries.Length);
        Assert.False(root.HasUnsupportedChild);
    }

    [Fact]
    public void Expands_transformed_group_for_outside_use()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><g id='a' transform='rotate(30)'><polygon points='0,0 4,0 0,4' fill='red'/></g><use href='#a' x='8'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

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
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><g><defs><rect id='tile' width='2' height='2' fill='red'/></defs></g></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Empty(root.Geometries);
        Assert.False(root.HasUnsupportedChild);
    }

    [Fact]
    public void Retains_group_wrapped_definition_content_without_painting_it()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><g><defs><g><rect width='5' height='5' fill='red'/></g></defs></g></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Empty(root.Geometries);
        Assert.False(root.HasUnsupportedChild);
    }

    [Fact]
    public void Parses_group_wrapped_definition_paths_without_painting_them()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><g><defs><path d='M0,0 H5 V5 H0 Z' fill='red'/></defs></g></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Empty(root.Geometries);
        Assert.False(root.HasUnsupportedChild);
    }

    [Fact]
    public void Parses_percentage_root_dimensions()
    {
        using SvgRoot root = SvgRootParser.Parse(System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='100%' height='50%'/>"), SvgRenderLimits.Default, CancellationToken.None);

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
        using SvgRoot root = SvgRootParser.Parse(System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='10' style='width: 20px'/>"), SvgRenderLimits.Default, CancellationToken.None);

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
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg' xmlns:sodipodi='http://sodipodi.sourceforge.net/DTD/sodipodi-0.dtd'><sodipodi:namedview id='namedview'/><rect width='5' height='5' fill='red'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Single(root.Geometries);
        Assert.False(root.HasUnsupportedChild);
    }

    [Fact]
    public void Flags_nonempty_foreign_namespace_wrapper()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg' xmlns:foo='urn:example'><foo:wrapper><rect width='5' height='5' fill='red'/></foo:wrapper></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Empty(root.Geometries);
        Assert.True(root.HasUnsupportedChild);
    }

    [Fact]
    public void Skips_editor_namedview_with_foreign_page()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg' xmlns:sodipodi='http://sodipodi.sourceforge.net/DTD/sodipodi-0.dtd' xmlns:inkscape='http://www.inkscape.org/namespaces/inkscape'><sodipodi:namedview pagecolor='#ffffff'><inkscape:page x='0' y='0'/></sodipodi:namedview><rect width='5' height='5' fill='red'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Single(root.Geometries);
        Assert.False(root.HasUnsupportedChild);
    }

    [Fact]
    public void Flags_namedview_hiding_svg_content()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg' xmlns:sodipodi='http://sodipodi.sourceforge.net/DTD/sodipodi-0.dtd'><sodipodi:namedview><rect width='5' height='5' fill='red'/></sodipodi:namedview></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Empty(root.Geometries);
        Assert.True(root.HasUnsupportedChild);
    }

    [Fact]
    public void Deep_namedview_exceeds_nesting_limit()
    {
        SvgRenderLimits limits = new(1024, 16, 16, 1024, 1024, 1024, 3, 256);

        SvgException rejected = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg' xmlns:sodipodi='http://sodipodi.sourceforge.net/DTD/sodipodi-0.dtd'><sodipodi:namedview><sodipodi:namedview><sodipodi:namedview/></sodipodi:namedview></sodipodi:namedview></svg>"u8.ToArray(), limits, new RenderBudget(new HostMemoryBudget(16_385)), CancellationToken.None));

        Assert.Equal(SvgErrorCode.LimitExceeded, rejected.ErrorCode);
    }

    [Fact]
    public void Namedview_subtree_exhausts_node_budget()
    {
        SvgRenderLimits limits = new(1024, 16, 16, 1024, 3, 1024, 256, 256);

        SvgException rejected = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg' xmlns:sodipodi='http://sodipodi.sourceforge.net/DTD/sodipodi-0.dtd'><sodipodi:namedview><sodipodi:guide/><sodipodi:guide/></sodipodi:namedview></svg>"u8.ToArray(), limits, new RenderBudget(new HostMemoryBudget(16_385)), CancellationToken.None));

        Assert.Equal(SvgErrorCode.LimitExceeded, rejected.ErrorCode);
    }

    [Fact]
    public void Leaves_direct_definition_content_inert()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><rect width='5' height='5' fill='red'/></defs></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Empty(root.Geometries);
        Assert.False(root.HasUnsupportedChild);
    }

    [Fact]
    public void Renders_only_the_first_switch_child()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><switch><rect x='1' y='2' width='3' height='4' fill='red'/><rect x='5' y='6' width='7' height='8' fill='blue'/></switch></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Single(root.Rectangles);
        Assert.Equal(new SvgRectangle(1, 2, 3, 4, new SvgColor(255, 0, 0, 255), true, null), root.Rectangles[0]);
        Assert.False(root.HasUnsupportedChild);
    }

    [Fact]
    public void Switch_skips_language_tagged_child_for_fallback()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><switch><rect systemLanguage='en' x='1' y='2' width='3' height='4' fill='red'/><rect x='5' y='6' width='7' height='8' fill='blue'/></switch></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Single(root.Rectangles);
        Assert.Equal(new SvgRectangle(5, 6, 7, 8, new SvgColor(0, 0, 255, 255), true, null), root.Rectangles[0]);
        Assert.False(root.HasUnsupportedChild);
    }

    [Fact]
    public void Switch_with_only_tagged_children_renders_nothing()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><switch><rect systemLanguage='en' width='2' height='2' fill='red'/></switch></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Empty(root.Geometries);
        Assert.False(root.HasUnsupportedChild);
    }

    [Fact]
    public void Switch_applies_group_transforms_to_its_first_child()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><switch transform='translate(1, 2)'><rect width='2' height='2' fill='red'/><rect width='2' height='2' fill='blue'/></switch></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Single(root.Rectangles);
        Assert.Equal(new SvgRectangle(1, 2, 2, 2, new SvgColor(255, 0, 0, 255), true, null), root.Rectangles[0]);
        Assert.False(root.HasUnsupportedChild);
    }

    [Fact]
    public void Empty_switch_renders_nothing()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><switch/><rect width='1' height='1' fill='red'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

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
    public void Square_dash_with_path_length_scales_runs()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><line x1='0' y1='4' x2='16' y2='4' stroke='red' stroke-width='2' stroke-dasharray='8 8' pathLength='32' stroke-linecap='square'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

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
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><line x1='0' y1='4' x2='16' y2='4' stroke='red' stroke-width='2' stroke-dasharray='0 4'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Empty(root.Geometries);
    }

    [Fact]
    public void Square_dash_offset_keeps_butt_phase()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><line x1='0' y1='4' x2='16' y2='4' stroke='red' stroke-width='2' stroke-dasharray='4 4' stroke-dashoffset='2' stroke-linecap='square'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(3, root.Geometries.Length);
        Assert.Equal(new SvgRectangle(-1, 3, 4, 2, new SvgColor(255, 0, 0, 255), true, null), root.Geometries[0].Rectangle);
        Assert.Equal(new SvgRectangle(5, 3, 6, 2, new SvgColor(255, 0, 0, 255), true, null), root.Geometries[1].Rectangle);
        Assert.Equal(new SvgRectangle(13, 3, 4, 2, new SvgColor(255, 0, 0, 255), true, null), root.Geometries[2].Rectangle);
    }

    [Fact]
    public void Square_capped_vertical_line_dash_extends_runs()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><line x1='4' y1='0' x2='4' y2='16' stroke='red' stroke-width='2' stroke-dasharray='4 4' stroke-linecap='square'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(2, root.Geometries.Length);
        Assert.Equal(new SvgRectangle(3, -1, 2, 6, new SvgColor(255, 0, 0, 255), true, null), root.Geometries[0].Rectangle);
        Assert.Equal(new SvgRectangle(3, 7, 2, 6, new SvgColor(255, 0, 0, 255), true, null), root.Geometries[1].Rectangle);
    }

    [Fact]
    public void Square_capped_line_dash_extends_each_run()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><line x1='0' y1='4' x2='16' y2='4' stroke='red' stroke-width='2' stroke-dasharray='4 4' stroke-linecap='square'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

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
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='g' gradientUnits='userSpaceOnUse' x1='0' y1='0' x2='8' y2='0'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><circle cx='4' cy='4' r='4' fill='url(#g)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.GradientPolygon, geometry.Kind);
    }

    [Fact]
    public void Fills_circles_with_radial_gradients()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><radialGradient id='s' gradientUnits='userSpaceOnUse' cx='4' cy='4' r='4'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><circle cx='4' cy='4' r='4' fill='url(#s)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.RadialPolygon, geometry.Kind);
    }

    [Fact]
    public void Fills_circles_with_patterns()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><pattern id='p' width='8' height='8' patternUnits='userSpaceOnUse'><rect width='8' height='8' fill='red'/></pattern></defs><circle cx='4' cy='4' r='4' fill='url(#p)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.PatternPolygon, geometry.Kind);
    }

    [Fact]
    public void Lowers_gradient_strokes_on_circles()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns=\"http://www.w3.org/2000/svg\"><defs><linearGradient id=\"g\"><stop offset=\"0\" stop-color=\"#ff0000\"/><stop offset=\"1\" stop-color=\"#0000ff\"/></linearGradient></defs><circle cx=\"4\" cy=\"4\" r=\"4\" fill=\"red\" stroke=\"url(#g)\" stroke-width=\"2\"/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(2, root.Geometries.Length);
        Assert.Equal(SvgGeometryKind.PolygonFill, root.Geometries[0].Kind);
        Assert.Equal(SvgGeometryKind.GradientPolygon, root.Geometries[1].Kind);
        Assert.True(root.Geometries[1].GradientPolygon.HasFill);
    }

    [Fact]
    public void Lowers_radial_strokes_on_circles()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns=\"http://www.w3.org/2000/svg\"><defs><radialGradient id=\"g\" cx=\"0.5\" cy=\"0.5\" r=\"0.5\"><stop offset=\"0\" stop-color=\"#ff0000\"/><stop offset=\"1\" stop-color=\"#0000ff\"/></radialGradient></defs><circle cx=\"4\" cy=\"4\" r=\"4\" fill=\"red\" stroke=\"url(#g)\" stroke-width=\"2\"/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(2, root.Geometries.Length);
        Assert.Equal(SvgGeometryKind.PolygonFill, root.Geometries[0].Kind);
        Assert.Equal(SvgGeometryKind.RadialPolygon, root.Geometries[1].Kind);
        Assert.True(root.Geometries[1].RadialPolygon.HasFill);
    }

    [Fact]
    public void Lowers_pattern_strokes_on_circles()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns=\"http://www.w3.org/2000/svg\"><defs><pattern id=\"p\" width=\"8\" height=\"8\" patternUnits=\"userSpaceOnUse\"><rect width=\"8\" height=\"8\" fill=\"red\"/></pattern></defs><circle cx=\"4\" cy=\"4\" r=\"4\" fill=\"red\" stroke=\"url(#p)\" stroke-width=\"2\"/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(2, root.Geometries.Length);
        Assert.Equal(SvgGeometryKind.PolygonFill, root.Geometries[0].Kind);
        Assert.Equal(SvgGeometryKind.PatternPolygon, root.Geometries[1].Kind);
        Assert.True(root.Geometries[1].PatternPolygon.HasFill);
    }

    [Fact]
    public void Lowers_dashed_circle_strokes_as_rings()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns=\"http://www.w3.org/2000/svg\"><circle cx=\"8\" cy=\"8\" r=\"6\" fill=\"red\" stroke=\"blue\" stroke-width=\"2\" stroke-dasharray=\"4 2\"/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.True(root.Geometries.Length >= 1);
        Assert.All(root.Geometries, geometry => Assert.Equal(SvgGeometryKind.PolygonFill, geometry.Kind));
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
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns=\"http://www.w3.org/2000/svg\"><ellipse cx=\"8\" cy=\"8\" rx=\"6\" ry=\"4\" fill=\"red\" stroke=\"blue\" stroke-width=\"2\" stroke-dasharray=\"4 2\"/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.True(root.Geometries.Length >= 1);
        Assert.All(root.Geometries, geometry => Assert.Equal(SvgGeometryKind.PolygonFill, geometry.Kind));
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
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns=\"http://www.w3.org/2000/svg\"><defs><linearGradient id=\"g\"><stop offset=\"0\" stop-color=\"#ff0000\"/><stop offset=\"1\" stop-color=\"#0000ff\"/></linearGradient></defs><ellipse cx=\"32\" cy=\"32\" rx=\"20\" ry=\"12\" fill=\"red\" stroke=\"url(#g)\" stroke-width=\"8\"/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(2, root.Geometries.Length);
        Assert.Equal(SvgGeometryKind.PolygonFill, root.Geometries[0].Kind);
        Assert.Equal(SvgGeometryKind.GradientPolygon, root.Geometries[1].Kind);
        Assert.True(root.Geometries[1].GradientPolygon.HasFill);
    }

    [Fact]
    public void Lowers_radial_strokes_on_ellipses()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns=\"http://www.w3.org/2000/svg\"><defs><radialGradient id=\"g\" cx=\"0.5\" cy=\"0.5\" r=\"0.5\"><stop offset=\"0\" stop-color=\"#ff0000\"/><stop offset=\"1\" stop-color=\"#0000ff\"/></radialGradient></defs><ellipse cx=\"32\" cy=\"32\" rx=\"20\" ry=\"12\" fill=\"red\" stroke=\"url(#g)\" stroke-width=\"8\"/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(2, root.Geometries.Length);
        Assert.Equal(SvgGeometryKind.PolygonFill, root.Geometries[0].Kind);
        Assert.Equal(SvgGeometryKind.RadialPolygon, root.Geometries[1].Kind);
        Assert.True(root.Geometries[1].RadialPolygon.HasFill);
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

    [Fact]
    public void Rejects_undefined_gradients_on_circles()
    {
        SvgException exception = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><circle cx='4' cy='4' r='4' fill='url(#missing)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, exception.ErrorCode);
    }

    [Fact]
    public void Fills_rounded_rectangles_with_linear_gradients()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='g' gradientUnits='userSpaceOnUse' x1='0' y1='0' x2='8' y2='0'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><rect x='0' y='0' width='8' height='8' rx='2' fill='url(#g)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.GradientPolygon, geometry.Kind);
    }

    [Fact]
    public void Fills_rounded_rectangles_with_radial_gradients()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><radialGradient id='s' gradientUnits='userSpaceOnUse' cx='4' cy='4' r='4'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><rect x='0' y='0' width='8' height='8' rx='2' fill='url(#s)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.RadialPolygon, geometry.Kind);
    }

    [Fact]
    public void Fills_rounded_rectangles_with_bounding_box_gradients()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='g' x1='0' y1='0' x2='1' y2='0'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><rect x='0' y='0' width='8' height='8' rx='2' fill='url(#g)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.GradientPolygon, geometry.Kind);
        Assert.Equal(new SvgRectangle(0, 0, 8, 8, default, true, null), geometry.GradientPolygon.Box);
    }

    [Fact]
    public void Fills_rounded_rectangles_with_patterns()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><pattern id='p' width='8' height='8' patternUnits='userSpaceOnUse'><rect width='8' height='8' fill='red'/></pattern></defs><rect x='0' y='0' width='8' height='8' rx='2' fill='url(#p)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.PatternPolygon, geometry.Kind);
    }

    [Fact]
    public void Lowers_gradient_strokes_on_rounded_rectangles()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns=\"http://www.w3.org/2000/svg\"><defs><linearGradient id=\"g\"><stop offset=\"0\" stop-color=\"#ff0000\"/><stop offset=\"1\" stop-color=\"#0000ff\"/></linearGradient></defs><rect x=\"0\" y=\"0\" width=\"8\" height=\"8\" rx=\"2\" fill=\"red\" stroke=\"url(#g)\" stroke-width=\"2\"/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(2, root.Geometries.Length);
        Assert.Equal(SvgGeometryKind.PolygonFill, root.Geometries[0].Kind);
        Assert.Equal(SvgGeometryKind.GradientPolygon, root.Geometries[1].Kind);
        Assert.True(root.Geometries[1].GradientPolygon.HasFill);
    }

    [Fact]
    public void Lowers_pattern_strokes_on_rounded_rectangles()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns=\"http://www.w3.org/2000/svg\"><defs><pattern id=\"p\" width=\"8\" height=\"8\" patternUnits=\"userSpaceOnUse\"><rect width=\"8\" height=\"8\" fill=\"red\"/></pattern></defs><rect x=\"0\" y=\"0\" width=\"8\" height=\"8\" rx=\"2\" fill=\"red\" stroke=\"url(#p)\" stroke-width=\"2\"/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(2, root.Geometries.Length);
        Assert.Equal(SvgGeometryKind.PolygonFill, root.Geometries[0].Kind);
        Assert.Equal(SvgGeometryKind.PatternPolygon, root.Geometries[1].Kind);
        Assert.True(root.Geometries[1].PatternPolygon.HasFill);
    }

    [Fact]
    public void Lowers_radial_strokes_on_rounded_rectangles()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns=\"http://www.w3.org/2000/svg\"><defs><radialGradient id=\"g\" cx=\"0.5\" cy=\"0.5\" r=\"0.5\"><stop offset=\"0\" stop-color=\"#ff0000\"/><stop offset=\"1\" stop-color=\"#0000ff\"/></radialGradient></defs><rect x=\"0\" y=\"0\" width=\"8\" height=\"8\" rx=\"2\" fill=\"red\" stroke=\"url(#g)\" stroke-width=\"2\"/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

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
    public void Renders_round_caps_on_dashed_rounded_rectangles()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns=\"http://www.w3.org/2000/svg\"><rect x=\"0\" y=\"0\" width=\"8\" height=\"8\" rx=\"2\" fill=\"red\" stroke=\"blue\" stroke-width=\"2\" stroke-dasharray=\"2 2\" stroke-linecap=\"round\"/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.False(root.HasUnsupportedChild);
        Assert.NotEmpty(root.Geometries);
    }

    [Fact]
    public void Strokes_solid_strokes_on_rounded_rectangles()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><rect x='0' y='0' width='8' height='8' rx='2' fill='red' stroke='blue' stroke-width='2'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(2, root.Geometries.Length);
        Assert.Equal(SvgGeometryKind.PolygonFill, root.Geometries[0].Kind);
        Assert.Equal(SvgGeometryKind.PolygonFill, root.Geometries[1].Kind);
        Assert.Equal(new SvgColor(255, 0, 0, 255), root.Geometries[0].Polygon.Color);
        Assert.Equal(new SvgColor(0, 0, 255, 255), root.Geometries[1].Polygon.Color);
    }

    [Fact]
    public void Round_joins_render_on_rounded_rectangles()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns=\"http://www.w3.org/2000/svg\"><rect x=\"0\" y=\"0\" width=\"8\" height=\"8\" rx=\"2\" fill=\"red\" stroke=\"blue\" stroke-width=\"2\" stroke-linejoin=\"round\"/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(2, root.Geometries.Length);
        Assert.Equal(SvgGeometryKind.PolygonFill, root.Geometries[0].Kind);
        Assert.Equal(SvgGeometryKind.PolygonFill, root.Geometries[1].Kind);
    }

    [Fact]
    public void Bevel_joins_render_on_rounded_rectangles()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns=\"http://www.w3.org/2000/svg\"><rect x=\"0\" y=\"0\" width=\"8\" height=\"8\" rx=\"2\" fill=\"red\" stroke=\"blue\" stroke-width=\"2\" stroke-linejoin=\"bevel\"/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(2, root.Geometries.Length);
        Assert.Equal(SvgGeometryKind.PolygonFill, root.Geometries[0].Kind);
        Assert.Equal(SvgGeometryKind.PolygonFill, root.Geometries[1].Kind);
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
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns=\"http://www.w3.org/2000/svg\"><rect x=\"0\" y=\"0\" width=\"8\" height=\"8\" rx=\"2\" fill=\"red\" stroke=\"blue\" stroke-width=\"2\" stroke-linecap=\"square\"/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

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
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><rect x='1' y='1' width='4' height='4' fill='red' stroke='blue' stroke-width='2' stroke-linecap='butt' stroke-linejoin='miter' stroke-dasharray='none'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Single(root.Rectangles);
        Assert.Equal(new SvgColor(255, 0, 0, 255), root.Rectangles[0].Color);
        Assert.False(root.HasUnsupportedChild);
    }

    [Fact]
    public void Accepts_noop_stroke_styles_through_groups_and_stylesheets()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><style>g { stroke-linecap: butt; }</style><g stroke-linejoin='miter'><rect width='1' height='1' fill='red'/></g></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

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
    public void Accepts_noop_dashoffset_on_stroked_leaves()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><rect width='5' height='5' fill='red' stroke='blue' stroke-width='2' stroke-dasharray='none' stroke-dashoffset='5'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Single(root.Rectangles);
        Assert.False(root.HasUnsupportedChild);
    }

    [Fact]
    public void Accepts_clip_rule_on_clip_definitions()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><clipPath id='c' clip-rule='evenodd'><rect x='1' y='1' width='3' height='3'/></clipPath></defs><rect width='5' height='5' fill='red' clip-path='url(#c)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

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
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><g font-variant='normal'><rect width='1' height='1' fill='red' color-interpolation='sRGB'/></g></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

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
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5' overflow='hidden' style='overflow: hidden'><svg x='1' y='1' width='3' height='3' overflow='hidden'><rect width='5' height='5' fill='red'/></svg></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

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
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><a href='https://example.com' target='_blank' rel='noopener' hreflang='en' download='logo' type='image/svg+xml' title='Example'><rect width='1' height='1' fill='red'/></a></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Single(root.Rectangles);
        Assert.Equal(new SvgColor(255, 0, 0, 255), root.Rectangles[0].Color);
        Assert.False(root.HasUnsupportedChild);
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
    public void Rejects_link_nested_definitions()
    {
        SvgException exception = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><a><defs><rect id='tile' width='2' height='2' fill='red'/></defs></a><use href='#tile'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, exception.ErrorCode);
    }

    [Fact]
    public void Ignores_accessibility_and_data_attributes_on_leaves()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg' role='img' aria-label='Mark'><rect width='1' height='1' fill='red' data-x='1' tabindex='0'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Single(root.Rectangles);
        Assert.Equal(new SvgColor(255, 0, 0, 255), root.Rectangles[0].Color);
        Assert.False(root.HasUnsupportedChild);
    }

    [Fact]
    public void Ignores_editor_namespace_attributes()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg' xmlns:inkscape='http://www.inkscape.org/namespaces/inkscape'><g inkscape:label='Layer'><rect width='1' height='1' fill='red'/></g></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Single(root.Rectangles);
        Assert.Equal(new SvgColor(255, 0, 0, 255), root.Rectangles[0].Color);
        Assert.False(root.HasUnsupportedChild);
    }

    [Fact]
    public void Ignores_legacy_root_version_tags()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg' version='1.1' baseProfile='tiny' width='12.5' height='7'><rect width='1' height='1' fill='red' focusable='false'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(12.5, root.Width?.Value);
        Assert.Equal(7, root.Height?.Value);
        Assert.Single(root.Rectangles);
        Assert.False(root.HasUnsupportedChild);
    }

    [Fact]
    public void Ignores_pointer_events_and_cursor()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><style>rect { pointer-events: none; }</style><rect width='1' height='1' fill='red' style='cursor: pointer' pointer-events='all'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

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
    public void Expands_transformed_group_nested_definitions_without_ancestor_transforms()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><g transform='translate(4,0)'><defs><rect id='r' x='0' y='0' width='4' height='8' fill='red'/></defs></g><use href='#r' x='0' y='0'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(new SvgRectangle(0, 0, 4, 8, new SvgColor(255, 0, 0, 255), true, null), Assert.Single(root.Rectangles));
    }

    [Fact]
    public void Applies_element_local_transforms_to_nested_definitions()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><rect id='r' x='0' y='0' width='2' height='8' fill='red' transform='translate(2,0)'/></defs><use href='#r' x='0' y='0'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

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
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><rect id='tile' width='2' height='2' fill='red'/></defs><use href='#tile' x='1.5'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Single(root.Rectangles);
        Assert.Equal(new SvgRectangle(1.5, 0, 2, 2, new SvgColor(255, 0, 0, 255), true, null), root.Rectangles[0]);
    }

    [Fact]
    public void Expands_axis_aligned_line_definitions_through_typed_use_geometry()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><use href='#line' x='1' y='2' opacity='0.5'/><defs><line id='line' x1='0' y1='1' x2='2' y2='1' stroke='red' stroke-width='2'/></defs></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

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
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><symbol id='line'><line x2='2' stroke='red' stroke-width='2'/></symbol></defs><use href='#line' x='1' y='1'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        SvgAuthoredPath source = geometry.AuthoredPath ?? throw new InvalidOperationException("Missing native line centerline.");
        Assert.Equal(new SvgDevicePoint(1, 1), source.SceneMap.Apply(source.Commands[0].First.X, source.Commands[0].First.Y));
        Assert.Equal(new SvgDevicePoint(3, 1), source.SceneMap.Apply(source.Commands[1].First.X, source.Commands[1].First.Y));
        Assert.Equal(2, source.StrokeStyle?.Width);
    }

    [Fact]
    public void Applies_integer_transforms_to_axis_aligned_line_use_instances()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><line id='line' x1='1' y1='1' x2='2' y2='1' stroke='red' stroke-width='2'/></defs><use href='#line' x='3' y='2' transform='scale(2) translate(1, 0)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        SvgAuthoredPath source = geometry.AuthoredPath ?? throw new InvalidOperationException("Missing native line centerline.");
        Assert.Equal(new SvgDevicePoint(10, 6), source.SceneMap.Apply(source.Commands[0].First.X, source.Commands[0].First.Y));
        Assert.Equal(new SvgDevicePoint(12, 6), source.SceneMap.Apply(source.Commands[1].First.X, source.Commands[1].First.Y));
        Assert.Equal(2, source.StrokeStyle?.Width);
        Assert.Equal(2, source.StrokeStyle?.WidthMap.A);
        Assert.Equal(2, source.StrokeStyle?.WidthMap.D);
    }

    [Fact]
    public void Fill_inherit_uses_the_enclosing_group_paint()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><g fill='red'><rect width='1' height='1' fill='inherit'/></g></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(new SvgColor(255, 0, 0, 255), root.Rectangles[0].Color);
    }

    [Fact]
    public void Fill_initial_ignores_the_enclosing_group_paint()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><g fill='red'><rect width='1' height='1' fill='initial'/></g></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(new SvgColor(0, 0, 0, 255), root.Rectangles[0].Color);
    }

    [Fact]
    public void Fill_opacity_unset_inherits_the_group_value()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><g fill-opacity='0.5'><rect width='1' height='1' fill='red' fill-opacity='unset'/></g></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(new SvgColor(255, 0, 0, 128), root.Rectangles[0].Color);
    }

    [Fact]
    public void Stroke_initial_means_no_stroke()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><rect width='4' height='4' fill='red' stroke='blue' style='stroke: initial'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Single(root.Rectangles);
        Assert.Equal(new SvgColor(255, 0, 0, 255), root.Rectangles[0].Color);
        Assert.Null(root.Rectangles[0].Stroke);
    }

    [Fact]
    public void Display_initial_shows_hidden_group_content()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><g display='none'><rect width='1' height='1' fill='red' display='initial'/></g></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Single(root.Rectangles);
        Assert.False(root.HasUnsupportedChild);
    }

    [Fact]
    public void Inherited_fill_through_stylesheet_rules()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><style>#r { fill: inherit; }</style><g fill='red'><rect id='r' width='1' height='1'/></g></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(new SvgColor(255, 0, 0, 255), root.Rectangles[0].Color);
    }

    [Fact]
    public void Applies_stylesheet_rules_to_rectangle_definitions()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><style>rect { fill: red; }</style><defs><rect id='tile' width='1' height='1'/></defs><use href='#tile'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(new SvgColor(255, 0, 0, 255), root.Rectangles[0].Color);
    }

    [Fact]
    public void Important_stylesheet_fill_beats_higher_specificity_normal_fill()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><style>rect { fill: blue !important; } #r { fill: red; }</style><rect id='r' width='1' height='1'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(new SvgColor(0, 0, 255, 255), root.Rectangles[0].Color);
    }

    [Fact]
    public void Important_stylesheet_fill_beats_normal_inline_fill()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><style>#r { fill: blue !important; }</style><rect id='r' width='1' height='1' style='fill: red'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(new SvgColor(0, 0, 255, 255), root.Rectangles[0].Color);
    }

    [Fact]
    public void Important_inline_fill_beats_important_stylesheet_fill()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><style>#r { fill: blue !important; }</style><rect id='r' width='1' height='1' style='fill: red !important'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(new SvgColor(255, 0, 0, 255), root.Rectangles[0].Color);
    }

    [Fact]
    public void Important_inline_fill_beats_later_normal_inline_fill()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><rect width='1' height='1' style='fill: red !important; fill: blue'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(new SvgColor(255, 0, 0, 255), root.Rectangles[0].Color);
    }

    [Fact]
    public void Resolves_a_forward_rectangle_definition_through_use_in_document_order()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><use href='#tile' x='2'/><defs><rect id='tile' width='1' height='1' fill='red'/></defs><rect x='3' width='1' height='1' fill='blue'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Collection(root.Rectangles,
            rectangle => Assert.Equal(new SvgRectangle(2, 0, 1, 1, new SvgColor(255, 0, 0, 255), true, null), rectangle),
            rectangle => Assert.Equal(new SvgRectangle(3, 0, 1, 1, new SvgColor(0, 0, 255, 255), true, null), rectangle));
    }

    [Fact]
    public void Applies_integer_transforms_to_rectangle_use_instances()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><rect id='tile' x='1' y='1' width='2' height='1' fill='red'/></defs><use href='#tile' x='3' y='2' transform='scale(2) translate(1, 0)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(new SvgRectangle(10, 6, 4, 2, new SvgColor(255, 0, 0, 255), true, null), root.Rectangles[0]);
    }

    [Fact]
    public void Expands_a_rectangle_symbol_through_use()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><symbol id='tile'><rect width='1' height='1' fill='red'/></symbol></defs><use href='#tile' x='2'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(new SvgRectangle(2, 0, 1, 1, new SvgColor(255, 0, 0, 255), true, null), root.Rectangles[0]);
    }

    [Fact]
    public void Applies_opacity_and_display_to_rectangle_use_instances()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><rect id='tile' width='1' height='1' fill='red'/></defs><use href='#tile' opacity='0.5'/><use href='#tile' display='none'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal((byte)255, root.Rectangles[0].Color.Alpha);
        SvgRecordedPaintAssertions.ElementOpacity(root, new(128, 0.5f));
        Assert.True(root.Rectangles[0].HasFill);
        Assert.False(root.Rectangles[1].HasFill);
    }

    [Fact]
    public void Applies_stylesheet_opacity_and_display_to_rectangle_use_instances()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><style>#half { opacity: 0.5; } .hidden { display: none; }</style><defs><rect id='tile' width='1' height='1' fill='red'/></defs><use id='half' href='#tile'/><use class='hidden' href='#tile'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal((byte)255, root.Rectangles[0].Color.Alpha);
        SvgRecordedPaintAssertions.ElementOpacity(root, new(128, 0.5f));
        Assert.True(root.Rectangles[0].HasFill);
        Assert.False(root.Rectangles[1].HasFill);
    }

    [Fact]
    public void Applies_fill_style_to_inherited_rectangle_use_instances()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><rect id='tile' width='1' height='1'/></defs><use href='#tile' style='fill: red'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(new SvgRectangle(0, 0, 1, 1, new SvgColor(255, 0, 0, 255), true, null), Assert.Single(root.Rectangles));
    }

    [Fact]
    public void Applies_stylesheet_fill_to_inherited_rectangle_use_instances()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><style>#instance { fill: red; }</style><defs><rect id='tile' width='1' height='1'/></defs><use id='instance' href='#tile'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(new SvgColor(255, 0, 0, 255), Assert.Single(root.Rectangles).Color);
    }

    [Fact]
    public void Applies_fill_style_to_forward_inherited_rectangle_use_instances()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><use href='#tile' fill='red'/><defs><rect id='tile' width='1' height='1'/></defs></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(new SvgColor(255, 0, 0, 255), Assert.Single(root.Rectangles).Color);
    }

    [Fact]
    public void Authored_rectangle_fill_wins_over_use_fill()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><rect id='tile' width='1' height='1' fill='blue'/></defs><use href='#tile' fill='red'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);
        Assert.Equal(new SvgColor(0, 0, 255, 255), Assert.Single(root.Rectangles).Color);
    }

    [Fact]
    public void Lowers_gradient_fills_on_use_instances()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='g' x1='0' y1='0' x2='1' y2='0'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient><rect id='r' x='0' y='0' width='4' height='8'/></defs><use href='#r' x='4' y='0' fill='url(#g)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.GradientFill, geometry.Kind);
        Assert.Equal(new SvgRectangle(4, 0, 4, 8, default, true, null), geometry.Gradient.Fill);
        AssertEqualGradient(new SvgLinearGradient(new SvgColor(255, 0, 0, 255), new SvgColor(0, 0, 255, 255), 0, 0, 1, 0, 0, 1, false, false, SvgGradientSpread.Pad, new SvgGradientStop[] { new(0, new SvgColor(255, 0, 0, 255)), new(1, new SvgColor(0, 0, 255, 255)) }, 1, 1, false), geometry.Gradient.Gradient);
    }

    [Fact]
    public void Lowers_user_space_gradient_fills_on_use_instances_with_placement_offsets()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='g' gradientUnits='userSpaceOnUse' x1='0' y1='0' x2='8' y2='0'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient><rect id='r' x='0' y='0' width='4' height='8'/></defs><use href='#r' x='4' y='0' fill='url(#g)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.GradientFill, geometry.Kind);
        Assert.Equal(new SvgRectangle(4, 0, 4, 8, default, true, null), geometry.Gradient.Fill);
        SvgLinearPaintMapData paint = AssertUserSpacePaint(geometry.Gradient.Gradient, SvgAffineTransform.FromTranslation(4, 0));
        Assert.Equal((0, 0, 8, 0), (paint.X1, paint.Y1, paint.X2, paint.Y2));
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
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><use href='#r' x='4' y='0' fill='url(#g)'/><defs><linearGradient id='g' x1='0' y1='0' x2='1' y2='0'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient><rect id='r' x='0' y='0' width='4' height='8'/></defs></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

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
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><rect x='0.25' y='0.5' width='5.5' height='3.25'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(new SvgRectangle(0.25, 0.5, 5.5, 3.25, new SvgColor(0, 0, 0, 255), true, null), root.Rectangles[0]);
    }

    [Fact]
    public void Multiplies_fill_opacity_with_color_alpha()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><rect width='1' height='1' fill='rgba(255, 0, 0, 0.5)' fill-opacity='0.5'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

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
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><rect width='1' height='1' fill='#ff000001' fill-opacity='0.5' opacity='0.5'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(new SvgColor(255, 0, 0, 1), root.Rectangles[0].Color);
    }

    [Fact]
    public void Resolves_inherited_fill_opacity_and_current_color_at_each_element()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><g fill-opacity='0.5'><rect width='1' fill='red'/></g><g fill='red' fill-opacity='0.5'><rect width='1' fill-opacity='1'/></g><g color='red' fill='currentColor'><rect width='1' color='blue'/></g></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Collection(root.Rectangles,
            rectangle => Assert.Equal(new SvgColor(255, 0, 0, 128), rectangle.Color),
            rectangle => Assert.Equal(new SvgColor(255, 0, 0, 255), rectangle.Color),
            rectangle => Assert.Equal(new SvgColor(0, 0, 255, 255), rectangle.Color));
    }

    [Fact]
    public void Parses_basic_css_named_colors()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><rect width='1' fill='yellow'/><rect width='1' fill='Teal'/><rect width='1' fill='fuchsia'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Collection(root.Rectangles,
            rectangle => Assert.Equal(new SvgColor(255, 255, 0, 255), rectangle.Color),
            rectangle => Assert.Equal(new SvgColor(0, 128, 128, 255), rectangle.Color),
            rectangle => Assert.Equal(new SvgColor(255, 0, 255, 255), rectangle.Color));
    }

    [Fact]
    public void Parses_extended_css_named_colors()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><rect width='1' fill='orange'/><rect width='1' fill='RebeccaPurple'/><rect width='1' fill='darkslategray'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Collection(root.Rectangles,
            rectangle => Assert.Equal(new SvgColor(255, 165, 0, 255), rectangle.Color),
            rectangle => Assert.Equal(new SvgColor(102, 51, 153, 255), rectangle.Color),
            rectangle => Assert.Equal(new SvgColor(47, 79, 79, 255), rectangle.Color));
    }

    [Fact]
    public void Maps_css_gray_and_grey_aliases_to_the_same_color()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><rect width='1' fill='darkgray'/><rect width='1' fill='darkgrey'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Collection(root.Rectangles,
            rectangle => Assert.Equal(new SvgColor(169, 169, 169, 255), rectangle.Color),
            rectangle => Assert.Equal(new SvgColor(169, 169, 169, 255), rectangle.Color));
    }

    [Fact]
    public void Parses_space_separated_functional_css_colors()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><rect width='1' fill='rgb(100% 0% 0% / 50%)'/><rect width='1' fill='hsl(120 100% 50% / 0.5)'/><rect width='1' fill='rgba(1 2 3)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

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
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><style>rect { fill: blue; fill-opacity: 0.5; }</style><rect width='1'/><rect width='1' fill='red'/><rect width='1' style='fill: lime'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

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
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><style>rect { fill: blue; fill-opacity: 0.5; } .accent { fill: red; } .accent { fill: teal; } #selected { fill: lime; } #selected { opacity: 0.5; }</style><rect width='1'/><rect width='1' class='accent'/><rect width='1' id='selected' class='accent'/><rect width='1' class='accent' style='fill: yellow'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

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
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><style>rect { fill: red; }</style><style>rect { fill: blue; }</style><rect width='1' height='1'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(new SvgRectangle(0, 0, 1, 1, new SvgColor(0, 0, 255, 255), true, null), Assert.Single(root.Rectangles));
    }

    [Fact]
    public void Stylesheet_specificity_beats_later_source_order()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><style>#r { fill: red; }</style><style>rect { fill: blue; }</style><rect id='r' width='1' height='1'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

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
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><style>.accent { fill: red; }</style><rect width='1' class='before&#x9;accent&#xA;after'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(new SvgColor(255, 0, 0, 255), root.Rectangles[0].Color);
    }

    [Fact]
    public void Applies_descendant_and_child_group_stylesheet_rules_to_nested_rectangles()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><style>rect { fill: blue; } g rect { fill: red; } g > rect { fill: lime; }</style><rect width='1'/><g><rect width='1'/></g></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Collection(root.Rectangles,
            rectangle => Assert.Equal(new SvgColor(0, 0, 255, 255), rectangle.Color),
            rectangle => Assert.Equal(new SvgColor(0, 255, 0, 255), rectangle.Color));
    }

    [Fact]
    public void Applies_id_and_class_presence_and_equality_attribute_selectors()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><style>rect { fill: blue; } [ id ] { fill: red; } [ class = accent ] { fill: lime; }</style><rect width='1'/><rect width='1' id='other'/><rect width='1' class='accent'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Collection(root.Rectangles,
            rectangle => Assert.Equal(new SvgColor(0, 0, 255, 255), rectangle.Color),
            rectangle => Assert.Equal(new SvgColor(255, 0, 0, 255), rectangle.Color),
            rectangle => Assert.Equal(new SvgColor(0, 255, 0, 255), rectangle.Color));
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
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><style>#selected { stroke: red; } .hidden { display: none; }</style><line id='selected' x1='0' y1='1' x2='2' y2='1' stroke-width='2'/><line class='hidden' x1='0' y1='3' x2='2' y2='3' stroke='blue' stroke-width='2'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(new SvgColor(255, 0, 0, 255), root.Rectangles[0].Color);
        Assert.False(root.Rectangles[1].HasFill);
    }

    [Fact]
    public void Inherits_group_stylesheet_paint_and_visibility()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><style>g { fill: red; } #hidden { display: none; }</style><g><rect width='1'/></g><g id='hidden'><rect width='1'/></g></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(new SvgColor(255, 0, 0, 255), root.Rectangles[0].Color);
        Assert.True(root.Rectangles[0].HasFill);
        Assert.False(root.Rectangles[1].HasFill);
    }

    [Fact]
    public void Applies_compound_type_class_and_type_id_stylesheet_selectors()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><style>rect { fill: blue; } rect.accent { fill: red; } rect#selected { fill: lime; } g.hidden { display: none; }</style><rect width='1' class='accent'/><rect width='1' id='selected'/><g class='hidden'><rect width='1'/></g></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Collection(root.Rectangles,
            rectangle => Assert.Equal(new SvgColor(255, 0, 0, 255), rectangle.Color),
            rectangle => Assert.Equal(new SvgColor(0, 255, 0, 255), rectangle.Color),
            rectangle => Assert.False(rectangle.HasFill));
    }
    [Fact]
    public void Admits_general_fractional_rectangle_coordinates()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><rect x='0.1' width='1' height='1' fill='red'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal((double)0.1f, root.Rectangles[0].X);
    }

    [Fact]
    public void Admits_fractional_translate_offsets()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><rect x='1' y='2' width='2' height='2' fill='red' transform='translate(0.5, -0.25)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(1.5, root.Rectangles[0].X);
        Assert.Equal(1.75, root.Rectangles[0].Y);
    }

    [Fact]
    public void Zero_translate_counts_as_identity_for_rectangle_strokes()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><rect x='2' y='2' width='8' height='8' fill='red' stroke='black' stroke-width='2' transform='translate(0, 0)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

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
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><g><rect width='1' height='1' fill='red'/><g><rect width='1' height='1' fill='blue'/></g></g></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.False(root.HasUnsupportedChild);
        Assert.Collection(root.Rectangles, rectangle => Assert.Equal((byte)255, rectangle.Color.Red), rectangle => Assert.Equal((byte)255, rectangle.Color.Blue));
    }

    [Fact]
    public void Rejects_undefined_group_clip_references()
    {
        SvgException exception = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><g clip-path='url(#clip)'><rect width='1' height='1'/></g></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None));

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
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><rect x='1' y='1' width='4' height='4' fill='none' stroke='red' stroke-width='2'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(new SvgStroke(new SvgColor(255, 0, 0, 255), 2), root.Rectangles[0].Stroke);
        Assert.False(root.Rectangles[0].HasFill);
    }

    [Fact]
    public void Does_not_apply_fill_opacity_to_strokes()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><g fill-opacity='0.5'><rect x='1' y='1' width='4' height='4' fill='none' stroke='red' stroke-width='2'/></g></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(new SvgStroke(new SvgColor(255, 0, 0, 255), 2), root.Rectangles[0].Stroke);
    }

    [Theory]
    [InlineData("display='none'")]
    [InlineData("visibility='hidden'")]
    public void Hides_rectangle_strokes_when_the_rectangle_is_not_painted(string visibilityAttribute)
    {
        byte[] svg = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg'><rect x='1' y='1' width='4' height='4' fill='none' stroke='red' stroke-width='2' " + visibilityAttribute + "/></svg>");

        using SvgRoot root = SvgRootParser.Parse(svg, SvgRenderLimits.Default, CancellationToken.None);

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
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><rect width='4' height='4' fill='blue' stroke='red' stroke-width='2'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.True(root.Rectangles[0].HasFill);
        Assert.Equal(new SvgStroke(new SvgColor(255, 0, 0, 255), 2), root.Rectangles[0].Stroke);
    }

    [Fact]
    public void Resolves_current_color_for_bounded_rectangle_and_line_strokes()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><rect width='4' height='4' fill='none' color='blue' stroke='currentColor' stroke-width='2'/><line x1='0' y1='6' x2='4' y2='6' color='red' stroke='currentColor' stroke-width='2'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(new SvgStroke(new SvgColor(0, 0, 255, 255), 2), root.Rectangles[0].Stroke);
        Assert.Equal(new SvgRectangle(0, 5, 4, 2, new SvgColor(255, 0, 0, 255), true, null), root.Rectangles[1]);
    }

    [Fact]
    public void Lowers_bounded_axis_aligned_lines_to_stroke_bands()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><line x1='1' y1='3' x2='5' y2='3' stroke='red' stroke-width='2'/><line x1='4' y1='1' x2='4' y2='5' stroke='blue' stroke-width='2'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Collection(root.Rectangles,
            line => Assert.Equal(new SvgRectangle(1, 2, 4, 2, new SvgColor(255, 0, 0, 255), true, null), line),
            line => Assert.Equal(new SvgRectangle(3, 1, 2, 4, new SvgColor(0, 0, 255, 255), true, null), line));
    }

    [Fact]
    public void Retains_axis_aligned_lines_as_typed_scene_geometry()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><line x1='1' y1='3' x2='5' y2='3' stroke='red' stroke-width='2'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.AxisAlignedLine, geometry.Kind);
        Assert.Equal(new SvgAxisAlignedLine(1, 3, 5, 3, new SvgColor(255, 0, 0, 255), true, 2), geometry.AxisAlignedLine);
    }

    [Fact]
    public void Lowers_translucent_lines_to_bands()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><line x1='1' y1='3' x2='5' y2='3' stroke='red' stroke-width='2' opacity='0.5'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(new SvgRectangle(1, 2, 4, 2, new SvgColor(255, 0, 0, 255), true, null), Assert.Single(root.Rectangles));
        SvgRecordedPaintAssertions.ElementOpacity(root, new(128, 0.5f));
    }

    [Fact]
    public void Lowers_translucent_stroke_colors_on_lines()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><line x1='1' y1='3' x2='5' y2='3' stroke='#ff000080' stroke-width='2'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

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

    [Fact]
    public void Keeps_nonuniform_reference_mapping_separate_from_user_space_endpoints()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='16'><defs><linearGradient id='g' gradientUnits='userSpaceOnUse' x1='0' y1='0' x2='16' y2='16'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><g transform='scale(2, 1)'><rect x='0' y='0' width='16' height='16' fill='url(#g)'/></g></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.GradientFill, geometry.Kind);
        SvgLinearPaintMapData paint = AssertUserSpacePaint(geometry.Gradient.Gradient, SvgAffineTransform.FromScaleOffset(2, 1, 0, 0));
        Assert.Equal((0, 0, 16, 16), (paint.X1, paint.Y1, paint.X2, paint.Y2));
    }

    [Fact]
    public void Preserves_the_full_extent_of_extreme_axis_aligned_line_coordinates()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><line x1='-2147483648' y1='2' x2='2147483647' y2='2' stroke='red' stroke-width='2'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

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
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><g transform='translate(1, 2)'><g transform='translate(3)'><rect x='4' y='5' width='1' height='1' transform='translate(-1, 2)'/></g></g></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(7, root.Rectangles[0].X);
        Assert.Equal(9, root.Rectangles[0].Y);
    }

    [Fact]
    public void Composes_positive_integer_scale_and_translate_transforms()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><g transform='translate(1, 2)'><g transform='scale(2, 3)'><rect x='4' y='5' width='1' height='2'/></g></g></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(9, root.Rectangles[0].X);
        Assert.Equal(17, root.Rectangles[0].Y);
        Assert.Equal(2, root.Rectangles[0].Width);
        Assert.Equal(6, root.Rectangles[0].Height);
    }

    [Fact]
    public void Preserves_svg_order_for_integer_transform_lists()
    {
        using SvgRoot translatedThenScaled = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><rect x='4' y='5' width='1' height='1' transform='translate(1, 2) scale(2, 3)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);
        using SvgRoot scaledThenTranslated = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><rect x='4' y='5' width='1' height='1' transform='scale(2, 3) translate(1, 2)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(new SvgRectangle(9, 17, 2, 3, new SvgColor(0, 0, 0, 255), true, null), translatedThenScaled.Rectangles[0]);
        Assert.Equal(new SvgRectangle(10, 21, 2, 3, new SvgColor(0, 0, 0, 255), true, null), scaledThenTranslated.Rectangles[0]);
    }

    [Fact]
    public void Parses_axis_aligned_integer_matrix_transforms()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><rect x='2' y='3' width='1' height='1' transform='matrix(2 0 0 3 4 5)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(8, root.Rectangles[0].X);
        Assert.Equal(14, root.Rectangles[0].Y);
        Assert.Equal(2, root.Rectangles[0].Width);
        Assert.Equal(3, root.Rectangles[0].Height);
    }

    [Fact]
    public void Reflects_rectangles_through_negative_integer_scale()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><rect x='1' y='2' width='3' height='4' transform='translate(8,0) scale(-1,1)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(new SvgRectangle(4, 2, 3, 4, new SvgColor(0, 0, 0, 255), true, null), root.Rectangles[0]);
    }

    [Fact]
    public void Reflects_rectangles_through_negative_axis_aligned_matrix()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><rect x='1' y='2' width='3' height='4' transform='matrix(-1,0,0,1,8,0)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

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
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='g' x1='0' y1='0' x2='1' y2='0'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><rect width='8' height='8' fill='url(#g)' transform='translate(8,0) scale(-1,1)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.GradientFill, geometry.Kind);
        AssertEqualGradient(new SvgLinearGradient(new SvgColor(255, 0, 0, 255), new SvgColor(0, 0, 255, 255), 1, 0, 0, 0, 0, 1, false, false, SvgGradientSpread.Pad, new SvgGradientStop[] { new(0, new SvgColor(255, 0, 0, 255)), new(1, new SvgColor(0, 0, 255, 255)) }, 1, 1, false), geometry.Gradient.Gradient);
    }

    [Fact]
    public void Clips_symbol_content_to_the_use_viewport_before_the_instance_transform()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><symbol id='s' viewBox='0 0 4 4'><rect width='4' height='4' fill='red'/></symbol></defs><use href='#s' x='0' y='0' width='4' height='4' transform='translate(8,0) scale(-1,1)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(new SvgRectangle(4, 0, 4, 4, new SvgColor(255, 0, 0, 255), true, null), Assert.Single(root.Rectangles));
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
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='g'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><path d='M1,1 H7 V3 H3 V7 H1 Z' fill='url(#g)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

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
        SvgRectangle box = MappedPointListBox(geometry, new(1, 1, 6, 6, default, true, null), SvgAffineTransform.Identity);
        Assert.Equal((1d, 1d, 6d, 6d), (box.X, box.Y, box.Width, box.Height));
        Assert.Equal(7, Assert.IsType<SvgAuthoredPath>(geometry.AuthoredPath).Commands.Length);
    }

    [Fact]
    public void Lowers_gradient_filled_polygon_instances_through_placement()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='g'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><defs><polygon id='p' points='0,0 8,0 8,4 4,4 4,8 0,8' fill='url(#g)'/></defs><use href='#p' x='8'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.GradientPolygon, geometry.Kind);
        SvgRectangle box = MappedPointListBox(geometry, new(0, 0, 8, 8, default, true, null), SvgAffineTransform.FromTranslation(8, 0));
        Assert.Equal((8d, 0d, 8d, 8d), (box.X, box.Y, box.Width, box.Height));
        SvgAuthoredPath source = Assert.IsType<SvgAuthoredPath>(geometry.AuthoredPath);
        Assert.Equal(SvgAffineTransform.FromTranslation(8, 0), source.SceneMap);
        Assert.Equal(7, source.Commands.Length);
    }

    [Fact]
    public void Lowers_symbol_gradient_instances_through_viewBox_mapping()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='g'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><defs><symbol id='s' viewBox='0 0 4 4'><polygon points='0,0 4,0 4,2 2,2 2,4 0,4' fill='url(#g)'/></symbol></defs><use href='#s' width='8' height='8' opacity='0.5'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(5, root.Commands.Length);
        SvgRecordedPaintAssertions.OpacityScope(root.Commands[0], new(128, 0.5f));
        SvgLeafMaskAssertions.ViewportScope(root, 1, new(0, 0, 4, 4), SvgAffineTransform.FromScale(2, 2));
        Assert.Equal(new SvgDrawCommand(SvgDrawCommandKind.EndGroup, -1, 255, null, -1, 255), root.Commands[4]);
        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.GradientPolygon, geometry.Kind);
        SvgRectangle box = MappedPointListBox(geometry, new(0, 0, 4, 4, default, true, null), SvgAffineTransform.FromScale(2, 2));
        Assert.Equal((0d, 0d, 8d, 8d), (box.X, box.Y, box.Width, box.Height));
        Assert.Equal(255, geometry.GradientPolygon.Opacity);
        Assert.Null(geometry.GradientPolygon.Clip);
        Assert.Equal(SvgAffineTransform.FromScale(2, 2), Assert.IsType<SvgAuthoredPath>(geometry.AuthoredPath).SceneMap);
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
    public void Retains_single_stop_gradient_paths_with_their_shader()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='g' spreadMethod='repeat'><stop offset='0.3' stop-color='#ff0000'/></linearGradient></defs><path d='M1,1 H7 V3 H3 V7 H1 Z' fill='url(#g)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.GradientPolygon, geometry.Kind);
        Assert.Equal(new SvgColor(255, 0, 0, 255), Assert.Single(geometry.GradientPolygon.Gradient.Stops).Color);
        Assert.NotNull(geometry.AuthoredPath);
    }

    [Fact]
    public void Lowers_single_stop_gradient_strokes_as_constant_bands()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='g'><stop offset='0.3' stop-color='#ff0000'/></linearGradient></defs><rect x='1' y='1' width='6' height='6' fill='red' stroke='url(#g)' stroke-width='2'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

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
        Assert.Equal(SvgGeometryKind.GradientPolygon, geometry.Kind);
        AssertStrokeContourBounds(geometry, [new(0, 3, 8, 2, default, true, null)]);
        Assert.Equal(new SvgColor(255, 0, 0, 255), Assert.Single(geometry.GradientPolygon.Gradient.Stops).Color);
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
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><line x1='0' y1='4' x2='16' y2='4' stroke='red' stroke-width='2' stroke-dasharray='4 2' pathLength='8'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(2, root.Geometries.Length);
        Assert.All(root.Geometries, geometry => Assert.Equal(SvgGeometryKind.OrthogonalBand, geometry.Kind));
        Assert.Equal(new SvgRectangle(0, 3, 8, 2, new SvgColor(255, 0, 0, 255), true, null), root.Geometries[0].Rectangle);
        Assert.Equal(new SvgRectangle(12, 3, 4, 2, new SvgColor(255, 0, 0, 255), true, null), root.Geometries[1].Rectangle);
    }

    [Fact]
    public void Zero_path_length_pins_a_solid_line()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><line x1='0' y1='4' x2='16' y2='4' stroke='red' stroke-width='2' stroke-dasharray='4 2' pathLength='0'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

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
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='g'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient><pattern id='p' width='4' height='4' patternUnits='userSpaceOnUse'><rect width='2' height='2' fill='red'/></pattern></defs><path d='M0,0 H4 V4 H0 Z' fill='url(#g)'/><path d='M4,4 H8 V8 H4 Z' fill='url(#p)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

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
    public void Keeps_solid_stroke_opacity_outside_gradient_fill_opacity()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='g'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><rect x='1' y='1' width='6' height='6' fill='url(#g)' fill-opacity='0.5' stroke='green' stroke-width='2'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.GradientFill, geometry.Kind);
        Assert.Equal(128, geometry.Gradient.Opacity);
        Assert.Equal(new SvgColor(0, 128, 0, 255), geometry.Gradient.Stroke.GetValueOrDefault().Color);
    }

    [Fact]
    public void Keeps_solid_stroke_opacity_outside_pattern_fill_opacity()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><pattern id='p' width='4' height='4' patternUnits='userSpaceOnUse'><rect width='2' height='2' fill='red'/></pattern></defs><rect x='1' y='1' width='6' height='6' fill='url(#p)' fill-opacity='0.5' stroke='green' stroke-width='2'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.PatternFill, geometry.Kind);
        Assert.Equal(128, geometry.Pattern.Opacity);
        Assert.Equal(new SvgColor(0, 128, 0, 255), geometry.Pattern.Stroke.GetValueOrDefault().Color);
    }

    [Fact]
    public void Preserves_the_path_contour_and_its_clip_path_box()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><clipPath id='c'><rect x='2' y='0' width='4' height='8'/></clipPath></defs><path d='M0,0 H8 V8 H0 Z' fill='red' clip-path='url(#c)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

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
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><clipPath id='c'><rect x='2' y='0' width='4' height='8'/></clipPath><path d='M0,0 H8 V8 H0 Z' fill='red' clip-path='url(#c)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgPolygonFill polygon = Assert.Single(root.Geometries).Polygon;
        Assert.Equal(new SvgDevicePoint[] { new(0, 0), new(8, 0), new(8, 8), new(0, 8) }, polygon.Points);
        SvgClipBox clip = Assert.IsType<SvgClipBox>(polygon.Clip);
        Assert.Equal(new SvgClipBox(2, 0, 4, 8), clip);
        Assert.Equal(SvgClipAntialiasing.All, clip.Antialiasing);
        Assert.False(root.HasUnsupportedChild);
    }

    [Fact]
    public void Clips_lines_to_their_clip_path_box()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><clipPath id='c'><rect x='2' y='0' width='4' height='8'/></clipPath></defs><line x1='0' y1='4' x2='8' y2='4' stroke='red' stroke-width='2' clip-path='url(#c)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(new SvgRectangle(2, 3, 4, 2, new SvgColor(255, 0, 0, 255), true, null), Assert.Single(root.Rectangles));
    }

    [Fact]
    public void Retains_gradient_path_bounds_and_the_separate_clip_box()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='g'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient><clipPath id='c'><rect x='2' y='0' width='4' height='8'/></clipPath></defs><path d='M0,0 H8 V8 H0 Z' fill='url(#g)' clip-path='url(#c)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.GradientPolygon, geometry.Kind);
        Assert.Equal(new SvgRectangle(0, 0, 8, 8, default, true, null), geometry.GradientPolygon.Box);
        Assert.Equal(new SvgClipBox(2, 0, 4, 8), geometry.GradientPolygon.Clip);
    }

    [Fact]
    public void Clips_leaves_to_transformed_clip_path_content()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><clipPath id='c'><rect x='1' y='1' width='4' height='4' transform='translate(1,1)'/></clipPath></defs><rect x='0' y='0' width='8' height='8' fill='red' clip-path='url(#c)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(new SvgRectangle(2, 2, 4, 4, new SvgColor(255, 0, 0, 255), true, null), Assert.Single(root.Rectangles));
    }

    [Fact]
    public void Nested_percentage_viewport_resolves_against_default_intrinsic_size()
    {
        // Omitted nested dimensions default to 100 percent of the resolved root output size.
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg' width='100%' height='100%'><svg><rect x='10' y='10' width='20' height='20' fill='red'/></svg></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(new SvgRectangle(10, 10, 20, 20, new SvgColor(255, 0, 0, 255), true, null), Assert.Single(root.Rectangles));
    }

    [Fact]
    public void Nested_percentage_bases_use_viewBox_user_units()
    {
        // Percentage bases resolve in parent user units, keeping scene content in root user space for the single output map.
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 10 10' width='100%' height='100%'><svg x='50%' width='5' height='5'><rect width='5' height='5' fill='red'/></svg></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

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
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><svg x='0' y='0' width='8' height='8' opacity='0.5'/><rect x='0' y='0' width='8' height='8' fill='red'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(new SvgRectangle(0, 0, 8, 8, new SvgColor(255, 0, 0, 255), true, null), Assert.Single(root.Rectangles));
    }

    [Fact]
    public void Admits_self_intersecting_diagonal_polygons()
    {
        // Single-contour nonzero crossings lower like simple contours.
        using SvgRoot root = SvgRootParser.Parse(System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg'><polygon points='0,0 2,2 2,0 0,2'/></svg>"), SvgRenderLimits.Default, CancellationToken.None);

        Assert.NotEmpty(root.Geometries);
        Assert.False(root.HasUnsupportedChild);
    }

    [Fact]
    public void Enforces_the_path_command_limit_before_expanding_polygon_storage()
    {
        SvgRenderLimits limits = new(1024, 10, 100, 1024, 1, 1);
        SvgException exception = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><polygon points='0,0 3,0 3,1 1,1 1,3 0,3'/></svg>"u8.ToArray(), limits, CancellationToken.None));

        Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode);
    }

    [Fact]
    public void Enforces_the_path_command_limit_across_supported_path_elements()
    {
        SvgRenderLimits limits = new(1024, 10, 100, 1024, 16, 9);

        SvgException exception = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><path d='M 0,0 H 1 V 1 H 0 Z'/><path d='M 2,0 H 3 V 1 H 2 Z'/></svg>"u8.ToArray(), limits, CancellationToken.None));

        Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode);
    }

    [Fact]
    public void Relative_touch_union_retains_directed_contours()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><path d='m0,0 l2,0 l0,2 l-2,0 z m2,1 l2,-1 l0,2 z'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Null(geometry.Polygon.Signs);
        Assert.Equal(2, geometry.Polygon.ContourEnds.Length);
        Assert.NotNull(geometry.AuthoredPath);
    }

    [Fact]
    public void Parses_supported_root_view_box_mapping()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg' viewBox='1 2 3 4' preserveAspectRatio='none'/>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);
        using SvgRoot aligned = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 1 2' preserveAspectRatio='xMaxYMin meet'/>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);
        using SvgRoot commaSeparated = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg' viewBox='1,2,3,4'/>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(new SvgViewBox(1, 2, 3, 4, SvgViewBoxAlignment.None, SvgViewBoxAlignment.None, SvgViewBoxFit.None), root.ViewBox);
        Assert.Equal(new SvgViewBox(0, 0, 1, 2, SvgViewBoxAlignment.Max, SvgViewBoxAlignment.Min, SvgViewBoxFit.Meet), aligned.ViewBox);
        Assert.Equal(new SvgViewBox(1, 2, 3, 4, SvgViewBoxAlignment.Mid, SvgViewBoxAlignment.Mid, SvgViewBoxFit.Meet), commaSeparated.ViewBox);
    }

    [Fact]
    public void Enforces_the_node_limit_before_expanding_rectangle_storage()
    {
        SvgRenderLimits limits = new(1024, 10, 100, 1024, 1, 1);
        SvgException exception = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><rect width='1' height='1' fill='#ff0000'/><rect width='1' height='1' fill='#ff0000'/></svg>"u8.ToArray(), limits, CancellationToken.None));
        Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode);
    }

    [Fact]
    public void Enforces_the_node_limit_for_inert_definition_descendants()
    {
        SvgRenderLimits limits = new(1024, 10, 100, 1024, 1, 1);

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
        SvgRenderLimits limits = new(1024, 10, 100, 1024, 16, 16, 2);

        SvgException exception = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><g><rect/></g></svg>"u8.ToArray(), limits, CancellationToken.None));

        Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode);
    }

    [Fact]
    public void Enforces_the_structural_limit_while_expanding_stylesheet_selectors()
    {
        SvgRenderLimits limits = new(1024, 10, 100, 1024, 2, 16);

        SvgException exception = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><style>rect, #one, .two { fill: red; }</style></svg>"u8.ToArray(), limits, CancellationToken.None));

        Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode);
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
    public void Parser_collection_sorting_orders_retained_values()
    {
        using BudgetedParserList<int> values = new(new RenderBudget(new HostMemoryBudget(1_000)), SvgMemoryCategory.Geometry);
        values.Add(3);
        values.Add(1);
        values.Add(2);

        values.Sort(CancellationToken.None);
        Assert.Equal([1, 2, 3], values.AsSpan().ToArray());
    }

    [Fact]
    public void Scene_transfer_preserves_geometry()
    {
        SvgRectangle rectangle = new(0, 0, 1, 1, new SvgColor(0, 0, 0, 255), true, null);
        using SvgSceneBuilder accepted = new(new RenderBudget(new HostMemoryBudget(10_000)), CancellationToken.None);
        accepted.AddGeometries([SvgGeometry.FromRectangle(rectangle)]);
        using SvgSceneStorage storage = accepted.Build();
        Assert.Equal([rectangle], storage.Rectangles);
    }

    [Fact]
    public void Tolerates_inert_root_metadata_without_claiming_visible_support()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><title>sample</title><metadata><editor:note xmlns:editor='urn:editor'>ignored</editor:note></metadata><rect width='1' height='1' fill='#ff0000'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);
        Assert.False(root.HasUnsupportedChild);
        Assert.Single(root.Rectangles);
    }

    [Fact]
    public void Parses_px_and_percentage_dimensions_but_keeps_other_relative_unit_suffixes_explicitly_unsupported()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg' width='12.5px' height='7px'/>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);
        using SvgRoot percent = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg' width='100%' height='7'/>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);
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
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg' fill='red' style='color: lime'><g fill='blue' style='fill: currentColor; fill-opacity: 0.5'><rect width='1' height='1'/></g><rect width='1' height='1' fill='red' style='fill: blue; opacity: 0.5'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

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
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><g opacity='0.5'><rect width='1' height='1'/></g></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Collection(root.Commands,
            command => SvgRecordedPaintAssertions.OpacityScope(command, new(128, 0.5f)),
            command => Assert.Equal(new SvgDrawCommand(SvgDrawCommandKind.Geometry, 0, 255, null, -1, 255), command),
            command => Assert.Equal(new SvgDrawCommand(SvgDrawCommandKind.EndGroup, -1, 255, null, -1, 255), command));
        Assert.Equal(SvgGeometryKind.Rectangle, Assert.Single(root.Geometries).Kind);
    }

    [Fact]
    public void Represents_sequential_group_opacity_scopes()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><g opacity='0.5'><rect width='1' height='1'/></g><g opacity='0.5'><rect width='1' height='1'/></g></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

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
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg' opacity='0.5'><rect width='1' height='1'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Collection(root.Commands,
            command => SvgRecordedPaintAssertions.OpacityScope(command, new(128, 0.5f)),
            command => Assert.Equal(new SvgDrawCommand(SvgDrawCommandKind.Geometry, 0, 255, null, -1, 255), command),
            command => Assert.Equal(new SvgDrawCommand(SvgDrawCommandKind.EndGroup, -1, 255, null, -1, 255), command));
    }

    [Fact]
    public void Categorizes_prohibited_external_entity_processing_as_malformed_input()
    {
        SvgException exception = Assert.Throws<SvgException>(() => SvgRootParser.Parse(System.Text.Encoding.UTF8.GetBytes("<!DOCTYPE svg [<!ENTITY x SYSTEM 'file:///nope'>]><svg xmlns='http://www.w3.org/2000/svg'/>") , SvgRenderLimits.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.MalformedInput, exception.ErrorCode);
    }

    [Fact]
    public void Validates_class_lengths_before_stylesheet_matching()
    {
        string classValue = new('a', SvgSyntaxLimits.MaximumIdentifierCharacters + 1);
        using System.Xml.XmlReader reader = System.Xml.XmlReader.Create(new System.IO.StringReader("<svg xmlns='http://www.w3.org/2000/svg'><rect class='" + classValue + "'/></svg>"));
        Assert.True(reader.Read());
        Assert.True(reader.Read());
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(10_000));
        using SvgDocumentState state = new(budget, CancellationToken.None);
        state.StyleRules.Add(new CssRule(".selected", new(0, 1, 0), default, false));
        SvgRootAttributes root = new(new SvgInheritedPresentation(SvgPaint.Default, true, true, SvgAffineTransform.Identity), SvgInheritedTextStyle.Default, SvgInheritedStrokeStyle.Default, null, null, null, null);

        SvgException exception = Assert.Throws<SvgException>(() => SvgRenderedLeafParser.TryParse(reader, true, root, state, SvgRenderLimits.Default, budget, new Dictionary<string, SvgColor>(), null, CancellationToken.None));

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
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(10_000));
        using BudgetedParserList<CssRule> rules = new(budget, SvgMemoryCategory.Parser, CancellationToken.None);
        using SvgCssAncestorContext ancestors = new(rules, budget, CancellationToken.None);

        SvgCssElement element = SvgCssElement.Read(reader);
        SvgException exception = Assert.Throws<SvgException>(() => rule.Matches(element, ancestors, CancellationToken.None));

        Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode);
        Assert.Contains("identifiers and class values", exception.Message);
    }

    [Fact]
    public void Clips_a_crossing_rectangle_to_its_nested_viewport_box()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><svg x='8' y='8' width='32' height='32'><rect width='64' height='64' fill='red'/></svg></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(new SvgRectangle(8, 8, 32, 32, new SvgColor(255, 0, 0, 255), true, null), Assert.Single(root.Rectangles));
    }

    [Fact]
    public void Clips_a_use_instance_to_its_nested_viewport_box()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><rect id='tile' width='64' height='64' fill='red'/></defs><svg x='8' y='8' width='32' height='32'><use href='#tile'/></svg></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(new SvgRectangle(8, 8, 32, 32, new SvgColor(255, 0, 0, 255), true, null), Assert.Single(root.Rectangles));
    }
    [Fact]
    public void Offsets_a_polygon_use_instance_by_its_nested_viewport_origin()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><polygon id='p' points='0,0 4,0 0,4' fill='red'/></defs><svg x='8' y='8' width='32' height='32'><use href='#p'/></svg></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.PolygonFill, geometry.Kind);
        Assert.Equal(new SvgDevicePoint(8, 8), geometry.Polygon.Points[0]);
        Assert.Equal(new SvgDevicePoint(12, 8), geometry.Polygon.Points[1]);
        Assert.Equal(new SvgDevicePoint(8, 12), geometry.Polygon.Points[2]);
    }

    [Fact]
    public void Offsets_nested_viewport_content_by_the_viewport_origin()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><svg x='8' y='8' width='32' height='32'><rect width='4' height='4' fill='red'/></svg></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(new SvgRectangle(8, 8, 4, 4, new SvgColor(255, 0, 0, 255), true, null), Assert.Single(root.Rectangles));
    }

    [Fact]
    public void Retains_the_original_line_band_and_its_nested_viewport_clip()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><svg x='8' y='8' width='32' height='32'><line x1='0' y1='10' x2='64' y2='10' stroke='red' stroke-width='2'/></svg></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.OrthogonalBand, geometry.Kind);
        Assert.Equal(new SvgRectangle(8, 17, 64, 2, new SvgColor(255, 0, 0, 255), true, null), geometry.Rectangle with { Clip = null });
        SvgClipBox clip = Assert.IsType<SvgClipBox>(geometry.Rectangle.Clip);
        Assert.Equal((8d, 8d, 32d, 32d), (clip.X, clip.Y, clip.Width, clip.Height));
    }

    [Fact]
    public void Keeps_a_stroked_rectangle_fully_inside_its_nested_viewport()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><svg x='8' y='8' width='32' height='32'><rect x='10' y='10' width='4' height='4' fill='red' stroke='blue' stroke-width='2'/></svg></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(new SvgRectangle(18, 18, 4, 4, new SvgColor(255, 0, 0, 255), true, new SvgStroke(new SvgColor(0, 0, 255, 255), 2)), Assert.Single(root.Rectangles));
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
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><symbol id='tile' viewBox='0 0 16 16'><rect width='16' height='16' fill='red'/></symbol></defs><use href='#tile' x='16' y='16' width='32' height='32'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(new SvgRectangle(16, 16, 32, 32, new SvgColor(255, 0, 0, 255), true, null), Assert.Single(root.Rectangles));
    }

    [Fact]
    public void Defaults_dimless_symbol_use_to_the_full_parent_viewport()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg' width='64' height='64'><defs><symbol id='tile' viewBox='0 0 16 16'><rect width='16' height='16' fill='red'/></symbol></defs><use href='#tile' x='16' y='16'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(new SvgRectangle(16, 16, 64, 64, new SvgColor(255, 0, 0, 255), true, null), Assert.Single(root.Rectangles));
    }

    [Fact]
    public void Maps_symbol_content_without_a_viewBox_one_to_one()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><symbol id='tile'><rect width='16' height='16' fill='red'/></symbol></defs><use href='#tile' x='16' y='16' width='32' height='32'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(new SvgRectangle(16, 16, 16, 16, new SvgColor(255, 0, 0, 255), true, null), Assert.Single(root.Rectangles));
    }

    [Fact]
    public void Applies_instance_fill_override_inside_symbols()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><symbol id='tile' viewBox='0 0 16 16'><rect width='16' height='16'/></symbol></defs><use href='#tile' x='16' y='16' width='32' height='32' fill='blue'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(new SvgRectangle(16, 16, 32, 32, new SvgColor(0, 0, 255, 255), true, null), Assert.Single(root.Rectangles));
    }
    [Fact]
    public void Applies_a_fill_override_to_an_unstyled_polygon()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><polygon id='p' points='0,0 8,0 0,8'/></defs><use href='#p' fill='blue'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.PolygonFill, geometry.Kind);
        Assert.Equal(new SvgColor(0, 0, 255, 255), geometry.Polygon.Color);
    }

    [Fact]
    public void Centers_meet_mapped_symbol_content_in_the_use_viewport()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><symbol id='tile' viewBox='0 0 16 8'><rect width='16' height='8' fill='red'/></symbol></defs><use href='#tile' width='32' height='32'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(new SvgRectangle(0, 8, 32, 16, new SvgColor(255, 0, 0, 255), true, null), Assert.Single(root.Rectangles));
    }

    [Fact]
    public void Composes_enclosing_group_transforms_around_symbol_viewports()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><symbol id='tile' viewBox='0 0 16 16'><rect width='16' height='16' fill='red'/></symbol></defs><g transform='translate(4)'><use href='#tile' x='16' y='16' width='32' height='32'/></g></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(new SvgRectangle(20, 16, 32, 32, new SvgColor(255, 0, 0, 255), true, null), Assert.Single(root.Rectangles));
    }
    [Fact]
    public void Applies_forward_masks_to_groups()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><g mask='url(#later)'><rect x='0' y='0' width='8' height='8' fill='red'/></g><defs><mask id='later'><rect x='0' y='0' width='8' height='8' fill='#ffffff'/></mask></defs></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(new SvgRectangle(0, 0, 8, 8, new SvgColor(255, 0, 0, 255), true, null), Assert.Single(root.Rectangles));
        SvgLeafMaskAssertions.UniformGroupScope(root, 255, null);
    }

    [Fact]
    public void Isolates_gray_forward_masks_on_groups()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><g mask='url(#later)'><rect x='0' y='0' width='8' height='8' fill='red'/></g><defs><mask id='later'><rect x='0' y='0' width='8' height='8' fill='#808080'/></mask></defs></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

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
    public void Rejects_text_in_forward_masked_groups()
    {
        SvgException exception = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><g mask='url(#later)'><text x='0' y='8'>Hi</text></g><defs><mask id='later'><rect x='0' y='0' width='8' height='8' fill='#ffffff'/></mask></defs></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, exception.ErrorCode);
    }

    [Fact]
    public void Retains_opacity_and_forward_masks_as_separate_layer_operations()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><g opacity='0.5' mask='url(#later)'><rect x='0' y='0' width='8' height='8' fill='red'/></g><defs><mask id='later'><rect x='0' y='0' width='8' height='8' fill='#808080'/></mask></defs></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(new SvgRectangle(0, 0, 8, 8, new SvgColor(255, 0, 0, 255), true, null), Assert.Single(root.Rectangles));
        SvgDrawCommand begin = Assert.Single(root.Commands, command => command.Kind == SvgDrawCommandKind.BeginGroup);
        Assert.Equal(128, begin.Opacity);
        Assert.Equal(128, begin.MaskAlpha);
    }

    [Fact]
    public void Applies_forward_custom_region_masks_on_groups()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><g mask='url(#later)'><rect x='0' y='0' width='8' height='8' fill='red'/></g><defs><mask id='later' maskUnits='userSpaceOnUse' x='2' y='2' width='4' height='4'><rect x='0' y='0' width='8' height='8' fill='#ffffff'/></mask></defs></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

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
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><clipPath id='window'><rect x='4' y='4' width='8' height='8'/></clipPath></defs><g clip-path='url(#window)'><rect width='16' height='16' fill='red'/></g></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(new SvgRectangle(4, 4, 8, 8, new SvgColor(255, 0, 0, 255), true, null), Assert.Single(root.Rectangles));
    }

    [Fact]
    public void Clips_forward_clip_paths_on_groups()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><g clip-path='url(#later)'><rect width='16' height='16' fill='red'/></g><rect x='0' y='0' width='2' height='2' fill='blue'/><defs><clipPath id='later'><rect x='4' y='4' width='8' height='8'/></clipPath></defs></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(2, root.Geometries.Length);
        Assert.Equal(new SvgRectangle(0, 0, 16, 16, new SvgColor(255, 0, 0, 255), true, null) { Clip = new(4, 4, 8, 8) }, root.Geometries[0].Rectangle);
        Assert.Equal(new SvgRectangle(0, 0, 2, 2, new SvgColor(0, 0, 255, 255), true, null), root.Geometries[1].Rectangle);
    }

    [Fact]
    public void Clips_forward_clip_paths_on_nested_groups()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><g clip-path='url(#later)'><g><rect width='16' height='16' fill='red'/></g></g><defs><clipPath id='later'><rect x='4' y='4' width='8' height='8'/></clipPath></defs></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(new SvgRectangle(4, 4, 8, 8, new SvgColor(255, 0, 0, 255), true, null), Assert.Single(root.Rectangles));
    }

    [Fact]
    public void Rejects_never_defined_clip_paths_on_groups()
    {
        SvgException exception = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><g clip-path='url(#missing)'><rect width='16' height='16' fill='red'/></g></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, exception.ErrorCode);
    }

    [Fact]
    public void Clips_a_leaf_to_its_clip_path_attribute()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><clipPath id='window'><rect x='4' y='4' width='8' height='8'/></clipPath></defs><rect width='16' height='16' fill='red' clip-path='url(#window)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(new SvgRectangle(4, 4, 8, 8, new SvgColor(255, 0, 0, 255), true, null), Assert.Single(root.Rectangles));
    }

    [Fact]
    public void Maps_nested_viewbox_meet_content_with_centering()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg' width='64' height='64'><svg x='0' y='0' width='48' height='24' viewBox='0 0 12 24'><rect x='0' y='0' width='12' height='24' fill='red'/></svg></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(new SvgRectangle(18, 0, 12, 24, new SvgColor(255, 0, 0, 255), true, null), Assert.Single(root.Rectangles));
    }

    [Fact]
    public void Maps_nested_viewbox_none_content_without_preserving_aspect()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg' width='64' height='64'><svg x='0' y='0' width='48' height='24' viewBox='0 0 12 24' preserveAspectRatio='none'><rect x='0' y='0' width='12' height='24' fill='red'/></svg></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(new SvgRectangle(0, 0, 48, 24, new SvgColor(255, 0, 0, 255), true, null), Assert.Single(root.Rectangles));
    }

    [Fact]
    public void Maps_nested_viewbox_slice_content_with_cropping()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg' width='64' height='64'><svg x='0' y='0' width='48' height='24' viewBox='0 0 12 24' preserveAspectRatio='xMidYMid slice'><rect x='0' y='0' width='12' height='24' fill='red'/></svg></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(new SvgRectangle(0, 0, 48, 24, new SvgColor(255, 0, 0, 255), true, null), Assert.Single(root.Rectangles));
    }

    [Fact]
    public void Admits_strokes_in_unscaled_viewbox_viewports()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg' width='64' height='64'><svg x='8' y='8' width='48' height='48' viewBox='8 8 48 48'><rect x='16' y='16' width='8' height='8' fill='red' stroke='blue' stroke-width='2'/></svg></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(new SvgRectangle(16, 16, 8, 8, new SvgColor(255, 0, 0, 255), true, new SvgStroke(new SvgColor(0, 0, 255, 255), 2)), Assert.Single(root.Rectangles));
    }
    [Fact]
    public void Resolves_percentage_nested_viewport_dimensions()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg' width='64' height='64'><svg x='50%' y='50%' width='50%' height='50%'><rect x='0' y='0' width='8' height='8' fill='red'/></svg></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(new SvgRectangle(32, 32, 8, 8, new SvgColor(255, 0, 0, 255), true, null), Assert.Single(root.Rectangles));
    }

    [Fact]
    public void Resolves_percentage_use_dimensions()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg' width='64' height='64'><defs><rect id='tile' width='8' height='8' fill='red'/></defs><use href='#tile' x='50%' y='50%' width='50%' height='50%'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

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
    public void Clips_a_rounded_rectangle_to_its_user_space_clip()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns=\"http://www.w3.org/2000/svg\"><defs><clipPath id=\"c\"><rect x=\"8\" y=\"8\" width=\"48\" height=\"48\"/></clipPath></defs><rect x=\"0\" y=\"0\" width=\"64\" height=\"64\" rx=\"12\" fill=\"red\" clip-path=\"url(#c)\"/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.PolygonFill, geometry.Kind);
        Assert.True(geometry.Polygon.Clip.HasValue);
        Assert.Equal(new SvgClipBox(8, 8, 48, 48), geometry.Polygon.Clip.Value);
    }

    [Fact]
    public void Clips_forward_user_space_clips_on_rounded_rectangles()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns=\"http://www.w3.org/2000/svg\"><rect x=\"0\" y=\"0\" width=\"64\" height=\"64\" rx=\"12\" fill=\"red\" clip-path=\"url(#later)\"/><defs><clipPath id=\"later\"><rect x=\"8\" y=\"8\" width=\"48\" height=\"48\"/></clipPath></defs></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.PolygonFill, geometry.Kind);
        Assert.True(geometry.Polygon.Clip.HasValue);
        Assert.Equal(new SvgClipBox(8, 8, 48, 48), geometry.Polygon.Clip.Value);
    }

    [Fact]
    public void Clips_use_instances_to_their_object_bounding_box()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><rect id='tile' width='4' height='4' fill='red'/><clipPath id='window' clipPathUnits='objectBoundingBox'><rect x='0' y='0' width='1' height='1'/></clipPath></defs><use href='#tile' clip-path='url(#window)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(new SvgRectangle(0, 0, 4, 4, new SvgColor(255, 0, 0, 255), true, null), Assert.Single(root.Rectangles));
    }

    [Fact]
    public void Clips_forward_object_bounding_box_clip_definitions_on_use_instances()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><rect id='tile' width='4' height='4' fill='red'/></defs><use href='#tile' clip-path='url(#window)'/><defs><clipPath id='window' clipPathUnits='objectBoundingBox'><rect x='0' y='0' width='1' height='1'/></clipPath></defs></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(new SvgRectangle(0, 0, 4, 4, new SvgColor(255, 0, 0, 255), true, null), Assert.Single(root.Rectangles));
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
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><rect x='0' y='0' width='8' height='8' fill='red' clip-path='url(#later)'/><defs><clipPath id='later'><rect x='2' y='2' width='4' height='4'/></clipPath></defs></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(new SvgRectangle(2, 2, 4, 4, new SvgColor(255, 0, 0, 255), true, null), Assert.Single(root.Rectangles));
    }

    [Fact]
    public void Rejects_never_defined_clip_paths_on_direct_leaves()
    {
        SvgException exception = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><rect x='0' y='0' width='8' height='8' fill='red' clip-path='url(#missing)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, exception.ErrorCode);
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
        SvgLeafMaskAssertions.UniformScopeWithViewport(root, 128, null, new(0, 0, 8, 8), SvgAffineTransform.Identity);
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
        SvgLeafMaskAssertions.UniformScope(root, 255, new(2, 2, 4, 4), new(0, 0, 8, 8));
    }

    [Fact]
    public void Applies_forward_fraction_region_masks_to_direct_rects()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><rect x='0' y='0' width='8' height='8' fill='red' mask='url(#later)'/><defs><mask id='later' x='0.25' y='0.25' width='0.5' height='0.5'><rect x='0' y='0' width='8' height='8' fill='#ffffff'/></mask></defs></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(new SvgRectangle(0, 0, 8, 8, new SvgColor(255, 0, 0, 255), true, null), Assert.Single(root.Rectangles));
        SvgLeafMaskAssertions.UniformScope(root, 255, new(2, 2, 4, 4), new(0, 0, 8, 8));
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
    public void Rejects_unknown_clip_path_unit_values()
    {
        SvgException exception = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><clipPath id='window' clipPathUnits='userSpace'><rect x='0' y='0' width='1' height='1'/></clipPath></defs><rect width='8' height='8' fill='red' clip-path='url(#window)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, exception.ErrorCode);
    }

    [Fact]
    public void Intersects_nested_clip_path_boxes()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><clipPath id='a'><rect x='2' y='2' width='12' height='12'/></clipPath><clipPath id='b'><rect x='4' y='4' width='8' height='8'/></clipPath></defs><g clip-path='url(#a)'><g clip-path='url(#b)'><rect width='16' height='16' fill='red'/></g></g></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(new SvgRectangle(4, 4, 8, 8, new SvgColor(255, 0, 0, 255), true, null), Assert.Single(root.Rectangles));
    }

    [Fact]
    public void Rejects_unknown_clip_path_references()
    {
        SvgException exception = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><g clip-path='url(#missing)'><rect width='8' height='8' fill='red'/></g></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None));

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
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='g' x1='0' y1='0' x2='1' y2='1'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><rect x='8' y='8' width='48' height='48' fill='url(#g)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.GradientFill, geometry.Kind);
        AssertEqualGradient(new SvgLinearGradient(new SvgColor(255, 0, 0, 255), new SvgColor(0, 0, 255, 255), 0, 0, 1, 1, 0, 1, false, false, SvgGradientSpread.Pad, new SvgGradientStop[] { new(0, new SvgColor(255, 0, 0, 255)), new(1, new SvgColor(0, 0, 255, 255)) }, 1, 1, false), geometry.Gradient.Gradient);
        Assert.Equal(new SvgRectangle(8, 8, 48, 48, new SvgColor(0, 0, 0, 0), true, null), geometry.Gradient.Fill);
    }

    [Fact]
    public void Lowers_offset_two_stop_gradient_fill_with_retained_offsets()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='g' x1='0' y1='0' x2='1' y2='0'><stop offset='0.25' stop-color='#ff0000'/><stop offset='0.75' stop-color='#0000ff'/></linearGradient></defs><rect x='8' y='8' width='48' height='48' fill='url(#g)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.GradientFill, geometry.Kind);
        AssertEqualGradient(new SvgLinearGradient(new SvgColor(255, 0, 0, 255), new SvgColor(0, 0, 255, 255), 0, 0, 1, 0, 0.25, 0.75, false, false, SvgGradientSpread.Pad, new SvgGradientStop[] { new(0.25, new SvgColor(255, 0, 0, 255)), new(0.75, new SvgColor(0, 0, 255, 255)) }, 1, 1, false), geometry.Gradient.Gradient);
    }

    [Theory]
    [InlineData("0.5", "0.5", 0.5)]
    [InlineData("0.75", "0.25", 0.75)]
    [InlineData("0.8", "0.2", 0.8)]
    [InlineData("2", "3", 1)]
    [InlineData("-2", "-1", 0)]
    public void Coincident_clamped_stops_preserve_both_boundary_colors(string first, string second, double expectedOffset)
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='g'><stop offset='" + first
            + "' stop-color='#ff0000'/><stop offset='" + second + "' stop-color='#0000ff'/></linearGradient></defs><rect width='8' height='8' fill='url(#g)'/></svg>");
        using SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, CancellationToken.None);
        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.GradientFill, geometry.Kind);
        SvgGradientStop[] stops = geometry.Gradient.Gradient.Stops;
        Assert.Equal(2, stops.Length);
        Assert.All(stops, stop => Assert.Equal((double)(float)expectedOffset, stop.Offset));
        Assert.Equal(new SvgColor(255, 0, 0, 255), stops[0].Color);
        Assert.Equal(new SvgColor(0, 0, 255, 255), stops[1].Color);
    }


    [Fact]
    public void Clamps_outside_offset_linear_gradient_stops_into_range()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='g' x1='0' y1='0' x2='1' y2='0'><stop offset='-0.5' stop-color='#ff0000'/><stop offset='1.5' stop-color='#0000ff'/></linearGradient></defs><rect x='8' y='8' width='48' height='48' fill='url(#g)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.GradientFill, geometry.Kind);
        AssertEqualGradient(new SvgLinearGradient(new SvgColor(255, 0, 0, 255), new SvgColor(0, 0, 255, 255), 0, 0, 1, 0, 0, 1, false, false, SvgGradientSpread.Pad, new SvgGradientStop[] { new(0, new SvgColor(255, 0, 0, 255)), new(1, new SvgColor(0, 0, 255, 255)) }, 1, 1, false), geometry.Gradient.Gradient);
    }



    [Fact]
    public void Lowers_user_space_gradient_with_its_declared_vector()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='g' gradientUnits='userSpaceOnUse' x1='8' y1='0' x2='56' y2='0'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><rect x='8' y='8' width='48' height='48' fill='url(#g)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.GradientFill, geometry.Kind);
        AssertEqualGradient(new SvgLinearGradient(new SvgColor(255, 0, 0, 255), new SvgColor(0, 0, 255, 255), 8, 0, 56, 0, 0, 1, false, true, SvgGradientSpread.Pad, new SvgGradientStop[] { new(0, new SvgColor(255, 0, 0, 255)), new(1, new SvgColor(0, 0, 255, 255)) }, 1, 1, false), geometry.Gradient.Gradient);
    }

    [Fact]
    public void Maps_user_space_gradient_vectors_through_enclosing_transforms()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='g' gradientUnits='userSpaceOnUse' x1='0' y1='0' x2='8' y2='0'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><g transform='translate(8, 8)'><rect x='0' y='0' width='8' height='8' fill='url(#g)'/></g></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.GradientFill, geometry.Kind);
        SvgLinearPaintMapData paint = AssertUserSpacePaint(geometry.Gradient.Gradient, SvgAffineTransform.FromTranslation(8, 8));
        Assert.Equal((0, 0, 8, 0), (paint.X1, paint.Y1, paint.X2, paint.Y2));
        AssertEqualGradient(new SvgLinearGradient(new SvgColor(255, 0, 0, 255), new SvgColor(0, 0, 255, 255), 0, 0, 8, 0, 0, 1, false, true, SvgGradientSpread.Pad, new SvgGradientStop[] { new(0, new SvgColor(255, 0, 0, 255)), new(1, new SvgColor(0, 0, 255, 255)) }, 1, 1, false), geometry.Gradient.Gradient);
    }

    [Fact]
    public void Lowers_percentage_user_space_gradient_coordinates()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='g' gradientUnits='userSpaceOnUse' x1='0%' y1='0%' x2='100%' y2='0%'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><rect width='8' height='8' fill='url(#g)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgLinearGradient gradient = Assert.Single(root.Geometries).Gradient.Gradient;
        Assert.Equal((0, 0, 300, 0), (gradient.X1, gradient.Y1, gradient.X2, gradient.Y2));
    }

    [Fact]
    public void Admits_user_space_gradients_inside_offset_viewports()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg' width='64' height='64'><defs><linearGradient id='g' gradientUnits='userSpaceOnUse' x1='0' y1='0' x2='8' y2='0'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><svg x='8' y='8' width='16' height='16'><rect x='0' y='0' width='8' height='8' fill='url(#g)'/></svg></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.GradientFill, geometry.Kind);
        SvgLinearPaintMapData paint = AssertUserSpacePaint(geometry.Gradient.Gradient, SvgAffineTransform.FromTranslation(8, 8));
        Assert.Equal((0, 0, 8, 0), (paint.X1, paint.Y1, paint.X2, paint.Y2));
    }

    [Fact]
    public void Lowers_gradient_transform_vectors_with_composed_coordinates()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='g' x1='0' y1='0' x2='1' y2='0' gradientTransform='scale(2)'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><rect x='8' y='8' width='48' height='48' fill='url(#g)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.GradientFill, geometry.Kind);
        AssertEqualGradient(new SvgLinearGradient(new SvgColor(255, 0, 0, 255), new SvgColor(0, 0, 255, 255), 0, 0, 2, 0, 0, 1, false, false, SvgGradientSpread.Pad, new SvgGradientStop[] { new(0, new SvgColor(255, 0, 0, 255)), new(1, new SvgColor(0, 0, 255, 255)) }, 1, 1, false), geometry.Gradient.Gradient);
    }

    [Theory]
    [InlineData("scale(0)")]
    public void Rejects_unsupported_gradient_transform_forms(string transform)
    {
        SvgException exception = Assert.Throws<SvgException>(() => SvgRootParser.Parse(System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='g' gradientTransform='" + transform + "'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><rect width='8' height='8' fill='url(#g)'/></svg>"), SvgRenderLimits.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, exception.ErrorCode);
    }

    [Fact]
    public void Admits_general_linear_gradient_maps_on_source_bound_rectangles()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='g' gradientTransform='matrix(1, 1, 0, 1, 0, 0)'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><rect width='8' height='8' fill='url(#g)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

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
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='g' x1='0' y1='0' x2='1' y2='0' gradientTransform='rotate(45)'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><rect width='8' height='8' fill='url(#g)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

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
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='g' x1='0' y1='0' x2='0.5' y2='0' spreadMethod='repeat'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><rect x='8' y='8' width='48' height='48' fill='url(#g)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        AssertEqualGradient(new SvgLinearGradient(new SvgColor(255, 0, 0, 255), new SvgColor(0, 0, 255, 255), 0, 0, 0.5, 0, 0, 1, false, false, SvgGradientSpread.Repeat, new SvgGradientStop[] { new(0, new SvgColor(255, 0, 0, 255)), new(1, new SvgColor(0, 0, 255, 255)) }, 1, 1, false), geometry.Gradient.Gradient);
    }

    [Fact]
    public void Lowers_reflect_spread_gradients_with_retained_spread()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='g' x1='0' y1='0' x2='0.5' y2='0' spreadMethod='reflect'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><rect x='8' y='8' width='48' height='48' fill='url(#g)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        AssertEqualGradient(new SvgLinearGradient(new SvgColor(255, 0, 0, 255), new SvgColor(0, 0, 255, 255), 0, 0, 0.5, 0, 0, 1, false, false, SvgGradientSpread.Reflect, new SvgGradientStop[] { new(0, new SvgColor(255, 0, 0, 255)), new(1, new SvgColor(0, 0, 255, 255)) }, 1, 1, false), geometry.Gradient.Gradient);
    }

    [Fact]
    public void Inherits_spread_methods_through_gradient_href()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='b' spreadMethod='reflect'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient><linearGradient id='c' href='#b'/></defs><rect x='8' y='8' width='48' height='48' fill='url(#c)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

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
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='b' x1='0' y1='0' x2='1' y2='0'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient><linearGradient id='c' href='#b'/></defs><rect x='8' y='8' width='48' height='48' fill='url(#c)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.GradientFill, geometry.Kind);
        AssertEqualGradient(new SvgLinearGradient(new SvgColor(255, 0, 0, 255), new SvgColor(0, 0, 255, 255), 0, 0, 1, 0, 0, 1, false, false, SvgGradientSpread.Pad, new SvgGradientStop[] { new(0, new SvgColor(255, 0, 0, 255)), new(1, new SvgColor(0, 0, 255, 255)) }, 1, 1, false), geometry.Gradient.Gradient);
    }

    [Fact]
    public void Overrides_vector_through_gradient_href()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='b'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient><linearGradient id='c' href='#b' x1='0' y1='0' x2='0.5' y2='0'/></defs><rect x='8' y='8' width='48' height='48' fill='url(#c)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        AssertEqualGradient(new SvgLinearGradient(new SvgColor(255, 0, 0, 255), new SvgColor(0, 0, 255, 255), 0, 0, 0.5, 0, 0, 1, false, false, SvgGradientSpread.Pad, new SvgGradientStop[] { new(0, new SvgColor(255, 0, 0, 255)), new(1, new SvgColor(0, 0, 255, 255)) }, 1, 1, false), geometry.Gradient.Gradient);
    }

    [Fact]
    public void Overrides_stops_through_gradient_href()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='b' x1='0' y1='0' x2='1' y2='1'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient><linearGradient id='c' href='#b'><stop offset='0' stop-color='#00ff00'/><stop offset='1' stop-color='#ffff00'/></linearGradient></defs><rect x='8' y='8' width='48' height='48' fill='url(#c)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        AssertEqualGradient(new SvgLinearGradient(new SvgColor(0, 255, 0, 255), new SvgColor(255, 255, 0, 255), 0, 0, 1, 1, 0, 1, false, false, SvgGradientSpread.Pad, new SvgGradientStop[] { new(0, new SvgColor(0, 255, 0, 255)), new(1, new SvgColor(255, 255, 0, 255)) }, 1, 1, false), geometry.Gradient.Gradient);
    }

    [Fact]
    public void Resolves_forward_gradient_references()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='c' href='#b'/><linearGradient id='b'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><rect width='8' height='8' fill='url(#c)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.GradientFill, geometry.Kind);
        AssertEqualGradient(new SvgLinearGradient(new SvgColor(255, 0, 0, 255), new SvgColor(0, 0, 255, 255), 0, 0, 1, 0, 0, 1, false, false, SvgGradientSpread.Pad, new SvgGradientStop[] { new(0, new SvgColor(255, 0, 0, 255)), new(1, new SvgColor(0, 0, 255, 255)) }, 1, 1, false), geometry.Gradient.Gradient);
    }

    [Fact]
    public void Resolves_forward_gradient_reference_chains()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='a' href='#b'/><linearGradient id='b' href='#c'/><linearGradient id='c'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><rect width='8' height='8' fill='url(#a)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

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
    public void Lowers_partial_vector_overrides_with_unit_overrides()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='b'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient><linearGradient id='c' href='#b' gradientUnits='userSpaceOnUse' x1='1'/></defs><rect width='5' height='5' fill='url(#c)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgLinearGradient gradient = Assert.Single(root.Geometries).Gradient.Gradient;
        Assert.Equal((1, 0, 300, 0), (gradient.X1, gradient.Y1, gradient.X2, gradient.Y2));
    }

    [Fact]
    public void Lowers_unit_overrides_without_vector_with_href()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='b'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient><linearGradient id='c' href='#b' gradientUnits='userSpaceOnUse'/></defs><rect width='8' height='8' fill='url(#c)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgLinearGradient gradient = Assert.Single(root.Geometries).Gradient.Gradient;
        Assert.Equal((0, 0, 300, 0), (gradient.X1, gradient.Y1, gradient.X2, gradient.Y2));
    }

    [Fact]
    public void Resolves_transitive_gradient_href_chains()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='a' x1='0' y1='0' x2='1' y2='0'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient><linearGradient id='b' href='#a'/><linearGradient id='c' href='#b' x1='0' y1='0' x2='0.5' y2='0'/></defs><rect x='8' y='8' width='48' height='48' fill='url(#c)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        AssertEqualGradient(new SvgLinearGradient(new SvgColor(255, 0, 0, 255), new SvgColor(0, 0, 255, 255), 0, 0, 0.5, 0, 0, 1, false, false, SvgGradientSpread.Pad, new SvgGradientStop[] { new(0, new SvgColor(255, 0, 0, 255)), new(1, new SvgColor(0, 0, 255, 255)) }, 1, 1, false), geometry.Gradient.Gradient);
    }

    [Fact]
    public void Composes_child_transforms_over_inherited_vectors()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='a' x1='0' y1='0' x2='1' y2='0'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient><linearGradient id='b' href='#a' gradientTransform='translate(1, 0)'/></defs><rect x='8' y='8' width='48' height='48' fill='url(#b)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        AssertEqualGradient(new SvgLinearGradient(new SvgColor(255, 0, 0, 255), new SvgColor(0, 0, 255, 255), 1, 0, 2, 0, 0, 1, false, false, SvgGradientSpread.Pad, new SvgGradientStop[] { new(0, new SvgColor(255, 0, 0, 255)), new(1, new SvgColor(0, 0, 255, 255)) }, 1, 1, false), geometry.Gradient.Gradient);
    }

    [Fact]
    public void Lowers_solid_fill_with_gradient_stroke_to_bands()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='g' x1='0' y1='0' x2='1' y2='0'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><rect x='16' y='16' width='32' height='32' fill='#00ff00' stroke='url(#g)' stroke-width='8'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

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
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='g'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><rect x='16' y='16' width='32' height='32' fill='none' stroke='url(#g)' stroke-width='8'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.GradientStroke, geometry.Kind);
        Assert.False(geometry.GradientStroke.Fill.HasFill);
        Assert.True(geometry.GradientStroke.HasStroke);
    }

    [Fact]
    public void Lowers_gradient_fills_with_gradient_strokes_as_fill_plus_bands()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='g'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient><linearGradient id='h'><stop offset='0' stop-color='#00ff00'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><rect width='8' height='8' fill='url(#g)' stroke='url(#h)' stroke-width='2'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

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
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><radialGradient id='f' cx='0.5' cy='0.5' r='0.5'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient><radialGradient id='s' cx='0.5' cy='0.5' r='0.5'><stop offset='0' stop-color='#00ff00'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><rect width='8' height='8' fill='url(#f)' stroke='url(#s)' stroke-width='2'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(5, root.Geometries.Length);
        Assert.All(root.Geometries, band => Assert.Equal(SvgGeometryKind.RadialGradient, band.Kind));
        Assert.True(root.Geometries[0].Radial.Fill.HasFill);
    }

    [Fact]
    public void Lowers_pattern_strokes_as_tile_bands()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><pattern id='tiles' width='8' height='8' patternUnits='userSpaceOnUse'><rect width='8' height='8' fill='red'/></pattern></defs><rect width='8' height='8' fill='red' stroke='url(#tiles)' stroke-width='2'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

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
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><line x1='8' y1='4' x2='16' y2='4' stroke='red' stroke-width='4' stroke-linecap='square'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.AxisAlignedLine, geometry.Kind);
        Assert.Equal(new SvgAxisAlignedLine(6, 4, 18, 4, new SvgColor(255, 0, 0, 255), true, 4), geometry.AxisAlignedLine);
    }

    [Fact]
    public void Lowers_round_capped_lines_as_band_with_tip_discs()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><line x1='8' y1='4' x2='16' y2='4' stroke='red' stroke-width='4' stroke-linecap='round'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

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
    public void Lowers_dashed_lines_as_run_bands()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><line x1='0' y1='4' x2='16' y2='4' stroke='red' stroke-width='2' stroke-dasharray='4 4'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

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
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><line x1='0' y1='4' x2='16' y2='4' stroke='red' stroke-width='2' stroke-dasharray='4 4' stroke-dashoffset='2'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

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
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='g'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><defs><pattern id='p' width='8' height='8' patternUnits='userSpaceOnUse'><rect width='8' height='8' fill='red'/></pattern></defs><rect width='8' height='8' fill='url(#g)' stroke='url(#p)' stroke-width='2'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(5, root.Geometries.Length);
        Assert.Equal(SvgGeometryKind.GradientFill, root.Geometries[0].Kind);
        Assert.True(root.Geometries[0].Gradient.Fill.HasFill);
        Assert.All(root.Geometries.Skip(1), band => Assert.Equal(SvgGeometryKind.PatternFill, band.Kind));
        Assert.All(root.Geometries.Skip(1), band => Assert.True(band.Pattern.Fill.HasFill));
    }

    [Fact]
    public void Lowers_radial_fills_with_pattern_strokes_as_fill_plus_bands()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><radialGradient id='f' cx='0.5' cy='0.5' r='0.5'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><defs><pattern id='p' width='8' height='8' patternUnits='userSpaceOnUse'><rect width='8' height='8' fill='red'/></pattern></defs><rect width='8' height='8' fill='url(#f)' stroke='url(#p)' stroke-width='2'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(5, root.Geometries.Length);
        Assert.Equal(SvgGeometryKind.RadialGradient, root.Geometries[0].Kind);
        Assert.True(root.Geometries[0].Radial.Fill.HasFill);
        Assert.All(root.Geometries.Skip(1), band => Assert.Equal(SvgGeometryKind.PatternFill, band.Kind));
    }

    [Fact]
    public void Lowers_pattern_fills_with_pattern_strokes_as_fill_plus_bands()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><pattern id='f' width='8' height='8' patternUnits='userSpaceOnUse'><rect width='8' height='8' fill='red'/></pattern><pattern id='s' width='8' height='8' patternUnits='userSpaceOnUse'><rect width='8' height='8' fill='blue'/></pattern></defs><rect width='8' height='8' fill='url(#f)' stroke='url(#s)' stroke-width='2'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(5, root.Geometries.Length);
        Assert.All(root.Geometries, band => Assert.Equal(SvgGeometryKind.PatternFill, band.Kind));
        Assert.True(root.Geometries[0].Pattern.Fill.HasFill);
    }

    [Fact]
    public void Lowers_pattern_fills_with_gradient_strokes_as_fill_plus_bands()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><pattern id='p' width='8' height='8' patternUnits='userSpaceOnUse'><rect width='8' height='8' fill='red'/></pattern></defs><defs><linearGradient id='g'><stop offset='0' stop-color='#00ff00'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><rect width='8' height='8' fill='url(#p)' stroke='url(#g)' stroke-width='2'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

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
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><pattern id='p' width='8' height='8' patternUnits='userSpaceOnUse'><rect width='8' height='8' fill='red'/></pattern></defs><defs><radialGradient id='s' cx='0.5' cy='0.5' r='0.5'><stop offset='0' stop-color='#00ff00'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><rect width='8' height='8' fill='url(#p)' stroke='url(#s)' stroke-width='2'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(5, root.Geometries.Length);
        Assert.Equal(SvgGeometryKind.PatternFill, root.Geometries[0].Kind);
        Assert.True(root.Geometries[0].Pattern.Fill.HasFill);
        Assert.All(root.Geometries.Skip(1), band => Assert.Equal(SvgGeometryKind.RadialGradient, band.Kind));
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
    public void Degenerate_user_space_gradients_render_the_end_stop_color()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='g' gradientUnits='userSpaceOnUse' x1='1' y1='1' x2='1' y2='1'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><rect width='8' height='8' fill='url(#g)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.GradientFill, geometry.Kind);
        Assert.Equal(new SvgColor(0, 0, 255, 255), geometry.Gradient.Gradient.End);
        Assert.True(geometry.Gradient.Fill.HasFill);
        Assert.False(root.HasUnsupportedChild);
    }

    [Fact]
    public void Degenerate_bounding_box_gradients_render_the_end_stop_color()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='g' x1='0.5' y1='0.5' x2='0.5' y2='0.5'><stop offset='0.25' stop-color='#ff0000'/><stop offset='0.75' stop-color='#0000ff'/></linearGradient></defs><rect width='8' height='8' fill='url(#g)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.GradientFill, geometry.Kind);
        Assert.Equal(new SvgColor(0, 0, 255, 255), geometry.Gradient.Gradient.End);
        Assert.True(geometry.Gradient.Fill.HasFill);
        Assert.False(root.HasUnsupportedChild);
    }

    [Fact]
    public void Retains_non_pad_degenerate_gradient_fills_for_factory_replay()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='g' x1='1' y1='1' x2='1' y2='1' spreadMethod='repeat'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><rect width='8' height='8' fill='url(#g)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);
        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.GradientFill, geometry.Kind);
        Assert.Equal(SvgGradientSpread.Repeat, geometry.Gradient.Gradient.Spread);
        Assert.Equal(2, geometry.Gradient.Gradient.Stops.Length);
    }

    [Fact]
    public void Retains_degenerate_gradient_strokes_as_constant_shader_rings()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='g' x1='1' y1='1' x2='1' y2='1'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><rect width='5' height='5' fill='red' stroke='url(#g)' stroke-width='2'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.GradientStroke, geometry.Kind);
        Assert.Equal(new SvgColor(255, 0, 0, 255), geometry.GradientStroke.Fill.Color);
        Assert.Equal(new SvgColor(0, 0, 255, 255), geometry.GradientStroke.Gradient.End);
        Assert.Equal(2, geometry.GradientStroke.Top.Height);
    }

    [Fact]
    public void Lowers_multi_stop_linear_gradients_with_retained_clamped_stops()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='g'><stop offset='0' stop-color='#ff0000'/><stop offset='0.5' stop-color='#00ff00'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><rect width='8' height='8' fill='url(#g)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

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
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='g'><stop offset='0' stop-color='#ff0000'/><stop offset='0.5' stop-color='#ff0000'/><stop offset='0.5' stop-color='#0000ff'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><rect width='8' height='8' fill='url(#g)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        AssertEqualGradient(new SvgLinearGradient(new SvgColor(255, 0, 0, 255), new SvgColor(0, 0, 255, 255), 0, 0, 1, 0, 0, 1, false, false, SvgGradientSpread.Pad, new SvgGradientStop[] { new(0, new SvgColor(255, 0, 0, 255)), new(0.5, new SvgColor(255, 0, 0, 255)), new(0.5, new SvgColor(0, 0, 255, 255)), new(1, new SvgColor(0, 0, 255, 255)) }, 1, 1, false), geometry.Gradient.Gradient);
    }

    [Fact]
    public void Rises_falling_gradient_stop_offsets_to_the_previous_stop()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='g'><stop offset='0' stop-color='#ff0000'/><stop offset='0.8' stop-color='#ff0000'/><stop offset='0.3' stop-color='#0000ff'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><rect width='8' height='8' fill='url(#g)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        AssertEqualGradient(new SvgLinearGradient(new SvgColor(255, 0, 0, 255), new SvgColor(0, 0, 255, 255), 0, 0, 1, 0, 0, 1, false, false, SvgGradientSpread.Pad, new SvgGradientStop[] { new(0, new SvgColor(255, 0, 0, 255)), new((double)0.8f, new SvgColor(255, 0, 0, 255)), new((double)0.8f, new SvgColor(0, 0, 255, 255)), new(1, new SvgColor(0, 0, 255, 255)) }, 1, 1, false), geometry.Gradient.Gradient);
    }


    [Fact]
    public void Lowers_concentric_radial_gradient_with_clamped_stops()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><radialGradient id='g' cx='0.5' cy='0.5' r='0.5'><stop offset='0' stop-color='#ff0000'/><stop offset='0.5' stop-color='#00ff00'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><rect width='8' height='8' fill='url(#g)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.RadialGradient, geometry.Kind);
        AssertEqualRadial(new SvgRadialGradient(new SvgColor(255, 0, 0, 255), new SvgColor(0, 0, 255, 255), 0.5, 0.5, 0.5, 0.5, 0.5, 0, 0, 1, false, false, SvgGradientSpread.Pad, new SvgGradientStop[] { new(0, new SvgColor(255, 0, 0, 255)), new(0.5, new SvgColor(0, 255, 0, 255)), new(1, new SvgColor(0, 0, 255, 255)) }, 1, 1, false, SvgGradientTransform.Identity, false), geometry.Radial.Gradient);
    }

    [Fact]
    public void Radial_gradient_defaults_to_centered_half_radius()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><radialGradient id='g'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><rect width='8' height='8' fill='url(#g)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.RadialGradient, geometry.Kind);
        Assert.Equal(0.5, geometry.Radial.Gradient.CenterX);
        Assert.Equal(0.5, geometry.Radial.Gradient.CenterY);
        Assert.Equal(0.5, geometry.Radial.Gradient.Radius);
    }

    [Fact]
    public void Inherits_radial_stops_and_circle_through_href()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><radialGradient id='p' cx='0.25' cy='0.25' r='0.75'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient><radialGradient id='g' href='#p'/></defs><rect width='8' height='8' fill='url(#g)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.RadialGradient, geometry.Kind);
        AssertEqualRadial(new SvgRadialGradient(new SvgColor(255, 0, 0, 255), new SvgColor(0, 0, 255, 255), 0.25, 0.25, 0.75, 0.25, 0.25, 0, 0, 1, false, false, SvgGradientSpread.Pad, new SvgGradientStop[] { new(0, new SvgColor(255, 0, 0, 255)), new(1, new SvgColor(0, 0, 255, 255)) }, 1, 1, false, SvgGradientTransform.Identity, false), geometry.Radial.Gradient);
    }

    [Fact]
    public void Lowers_userspace_radial_gradient_with_user_circle()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><radialGradient id='g' gradientUnits='userSpaceOnUse' cx='4' cy='4' r='4'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><rect width='8' height='8' fill='url(#g)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.RadialGradient, geometry.Kind);
        AssertEqualRadial(new SvgRadialGradient(new SvgColor(255, 0, 0, 255), new SvgColor(0, 0, 255, 255), 4, 4, 4, 4, 4, 0, 0, 1, false, true, SvgGradientSpread.Pad, new SvgGradientStop[] { new(0, new SvgColor(255, 0, 0, 255)), new(1, new SvgColor(0, 0, 255, 255)) }, 1, 1, false, SvgGradientTransform.Identity, false), geometry.Radial.Gradient);
    }
    [Fact]
    public void Maps_userspace_radial_circle_through_uniform_transform()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><radialGradient id='g' gradientUnits='userSpaceOnUse' cx='4' cy='4' r='4'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><rect width='8' height='8' fill='url(#g)' transform='scale(2)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

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
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><radialGradient id='p' gradientUnits='userSpaceOnUse' cx='4' cy='4' r='4'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient><radialGradient id='g' href='#p'/></defs><rect width='8' height='8' fill='url(#g)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.RadialGradient, geometry.Kind);
        Assert.True(geometry.Radial.Gradient.IsUserSpace);
        Assert.Equal(4, geometry.Radial.Gradient.CenterX);
        Assert.Equal(4, geometry.Radial.Gradient.Radius);
    }

    [Fact]
    public void Lowers_percentage_userspace_radial_coordinates()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><radialGradient id='g' gradientUnits='userSpaceOnUse' cx='50%' cy='4' r='4'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><rect width='8' height='8' fill='url(#g)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgRadialGradient gradient = Assert.Single(root.Geometries).Radial.Gradient;
        Assert.Equal((150, 4, 4), (gradient.CenterX, gradient.CenterY, gradient.Radius));
    }
    [Fact]
    public void Admits_nonuniform_userspace_radial_fills_through_recorded_scales()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><radialGradient id='g' gradientUnits='userSpaceOnUse' cx='4' cy='4' r='4'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><g transform='scale(2,1)'><rect width='8' height='8' fill='url(#g)'/></g></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

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
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><radialGradient id='g' gradientUnits='userSpaceOnUse' cx='4' cy='4' r='4'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><g transform='scale(2,1)'><polygon points='0,0 8,0 0,8' fill='url(#g)'/></g></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.RadialPolygon, geometry.Kind);
        Assert.True(geometry.RadialPolygon.HasFill);
        SvgRadialGradient gradient = geometry.RadialPolygon.Gradient;
        SvgGradientPaintMatrices matrices = Assert.IsType<SvgGradientPaintMatrices>(gradient.NativePaintMatrices);
        Assert.Equal(new(1, 0, 0, 0, 1, 0), matrices.Local);
        Assert.Equal(SvgAffineTransform.FromScale(2, 1), matrices.Referencing);
        Assert.Equal(new(.5, 0, 0, 1, 0, 0), gradient.Inverse);
        Assert.Equal((4d, 4d, 4d), (gradient.CenterX, gradient.CenterY, gradient.Radius));
        Assert.Equal(matrices.Referencing, Assert.IsType<SvgAuthoredPath>(geometry.AuthoredPath).SceneMap);
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
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg' width='64' height='64'><defs><linearGradient id='g' gradientUnits='userSpaceOnUse' x1='0' y1='0' x2='8' y2='0'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><svg x='0' y='0' width='16' height='8' viewBox='0 0 8 8' preserveAspectRatio='none'><rect width='8' height='8' fill='url(#g)'/></svg></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.GradientFill, geometry.Kind);
        SvgLinearPaintMapData paint = AssertUserSpacePaint(geometry.Gradient.Gradient, SvgAffineTransform.FromScaleOffset(2, 1, 0, 0));
        Assert.Equal((0, 0, 8, 0), (paint.X1, paint.Y1, paint.X2, paint.Y2));
    }

    [Fact]
    public void Admits_userspace_radial_inside_offset_viewports()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><radialGradient id='g' gradientUnits='userSpaceOnUse' cx='4' cy='4' r='4'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><svg x='8' y='0' width='16' height='16'><rect width='8' height='8' fill='url(#g)'/></svg></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

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
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><radialGradient id='g' spreadMethod='repeat' cx='0.5' cy='0.5' r='0.5'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><rect width='8' height='8' fill='url(#g)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.RadialGradient, geometry.Kind);
        Assert.Equal(SvgGradientSpread.Repeat, geometry.Radial.Gradient.Spread);
    }

    [Fact]
    public void Lowers_reflect_spread_radial_gradients_with_retained_spread()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><radialGradient id='g' spreadMethod='reflect' cx='0.5' cy='0.5' r='0.5'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><rect width='8' height='8' fill='url(#g)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.RadialGradient, geometry.Kind);
        Assert.Equal(SvgGradientSpread.Reflect, geometry.Radial.Gradient.Spread);
    }

    [Fact]
    public void Inherits_radial_spread_through_href()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><radialGradient id='p' spreadMethod='reflect' cx='0.5' cy='0.5' r='0.5'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient><radialGradient id='g' href='#p'/></defs><rect width='8' height='8' fill='url(#g)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.RadialGradient, geometry.Kind);
        Assert.Equal(SvgGradientSpread.Reflect, geometry.Radial.Gradient.Spread);
    }

    [Fact]
    public void Overrides_radial_spread_through_href()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><radialGradient id='p' cx='0.5' cy='0.5' r='0.5'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient><radialGradient id='g' href='#p' spreadMethod='repeat'/></defs><rect width='8' height='8' fill='url(#g)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.RadialGradient, geometry.Kind);
        Assert.Equal(SvgGradientSpread.Repeat, geometry.Radial.Gradient.Spread);
    }

    [Fact]
    public void Admits_xlink_href_on_radial_gradient_definitions()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg' xmlns:xlink='http://www.w3.org/1999/xlink'><defs><radialGradient id='p' cx='0.5' cy='0.5' r='0.5'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient><radialGradient id='g' xlink:href='#p' spreadMethod='repeat'/></defs><rect width='8' height='8' fill='url(#g)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.RadialGradient, geometry.Kind);
        Assert.Equal(SvgGradientSpread.Repeat, geometry.Radial.Gradient.Spread);
    }

    [Fact]
    public void Admits_cross_type_radial_href_from_linear_parent()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg' xmlns:xlink='http://www.w3.org/1999/xlink'><defs><linearGradient id='a'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient><radialGradient id='b' xlink:href='#a' cx='0.5' cy='0.5' r='0.5'/></defs><rect width='8' height='8' fill='url(#b)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

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
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns=\"http://www.w3.org/2000/svg\" xmlns:xlink=\"http://www.w3.org/1999/xlink\"><defs><radialGradient id=\"a\" cx=\"0.5\" cy=\"0.5\" r=\"0.5\"><stop offset=\"0\" stop-color=\"#ff0000\"/><stop offset=\"1\" stop-color=\"#0000ff\"/></radialGradient><linearGradient id=\"b\" xlink:href=\"#a\"/></defs><rect width=\"8\" height=\"8\" fill=\"url(#b)\"/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

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
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><radialGradient id='g' cx='0.5' cy='0.5' r='0.5' fx='0.25' fy='0.5'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><rect width='8' height='8' fill='url(#g)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

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
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><radialGradient id='g' cx='0.25' cy='0.75' r='0.25'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><rect width='8' height='8' fill='url(#g)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.RadialGradient, geometry.Kind);
        Assert.Equal(geometry.Radial.Gradient.CenterX, geometry.Radial.Gradient.FocalX);
        Assert.Equal(geometry.Radial.Gradient.CenterY, geometry.Radial.Gradient.FocalY);
    }

    [Fact]
    public void Inherits_radial_focal_through_href()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><radialGradient id='p' cx='0.5' cy='0.5' r='0.5' fx='0.25' fy='0.5'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient><radialGradient id='g' href='#p'/></defs><rect width='8' height='8' fill='url(#g)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.RadialGradient, geometry.Kind);
        Assert.Equal(0.25, geometry.Radial.Gradient.FocalX);
        Assert.Equal(0.5, geometry.Radial.Gradient.FocalY);
    }

    [Fact]
    public void Maps_userspace_radial_focal_through_uniform_transform()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><radialGradient id='g' gradientUnits='userSpaceOnUse' cx='4' cy='4' r='4' fx='2' fy='4'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><rect width='8' height='8' fill='url(#g)' transform='scale(2)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

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
    public void Admits_point_foci_on_circle_and_masks_samples_outside_the_cone()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><radialGradient id='g' cx='0.5' cy='0.5' r='0.5' fx='0.0' fy='0.5'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><rect width='8' height='8' fill='url(#g)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgRadialGeometry radial = Assert.Single(root.Geometries).Radial;
        Assert.Equal(0, radial.Gradient.FocalX);
        Assert.True(radial.Gradient.HasGeneralTransform);
        SvgShaderCoordinates coordinates = SvgShaderCoordinates.CreateRadial(radial.Gradient, radial.Box, 1, 1, 0, 0);
        Assert.True(coordinates.TryRadial(0, 4, new(0, 0), out SvgRadialShaderSample inside));
        Assert.True(inside.IsVisible);
        Assert.True(coordinates.TryRadial(-1, 4, new(0, 0), out SvgRadialShaderSample outside));
        Assert.False(outside.IsVisible);
    }

    [Fact]
    public void Rejects_percentage_userspace_radial_focal()
    {
        SvgException rejected = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><radialGradient id='g' gradientUnits='userSpaceOnUse' cx='4' cy='4' r='4' fx='50%'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><rect width='8' height='8' fill='url(#g)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, rejected.ErrorCode);
    }

    [Fact]
    public void Lowers_partial_radial_focal_overrides()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><radialGradient id='p' cx='0.5' cy='0.5' r='0.5'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient><radialGradient id='g' href='#p' fx='0.25'/></defs><rect width='8' height='8' fill='url(#g)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(.25, Assert.Single(root.Geometries).Radial.Gradient.FocalX);
    }
    [Fact]
    public void Lowers_contained_focal_radius()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><radialGradient id='g' cx='0.5' cy='0.5' r='0.5' fr='0.1'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><rect width='8' height='8' fill='url(#g)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.RadialGradient, geometry.Kind);
        Assert.Equal(0.1, geometry.Radial.Gradient.FocalRadius);
        Assert.Equal(0.5, geometry.Radial.Gradient.FocalX);
    }

    [Fact]
    public void Lowers_offset_contained_focal_circle()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><radialGradient id='g' cx='0.5' cy='0.5' r='0.5' fx='0.25' fy='0.5' fr='0.1'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><rect width='8' height='8' fill='url(#g)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.RadialGradient, geometry.Kind);
        Assert.Equal(0.25, geometry.Radial.Gradient.FocalX);
        Assert.Equal(0.1, geometry.Radial.Gradient.FocalRadius);
    }

    [Fact]
    public void Inherits_focal_radius_through_href()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><radialGradient id='p' cx='0.5' cy='0.5' r='0.5' fr='0.1'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient><radialGradient id='g' href='#p'/></defs><rect width='8' height='8' fill='url(#g)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.RadialGradient, geometry.Kind);
        Assert.Equal(0.1, geometry.Radial.Gradient.FocalRadius);
    }

    [Fact]
    public void Retains_userspace_focal_radii_and_uniform_reference_map()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><radialGradient id='g' gradientUnits='userSpaceOnUse' cx='4' cy='4' r='4' fr='1'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><rect width='8' height='8' fill='url(#g)' transform='scale(2)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.RadialGradient, geometry.Kind);
        SvgRadialGradient gradient = geometry.Radial.Gradient;
        Assert.Equal(4, gradient.Radius);
        Assert.Equal(1, gradient.FocalRadius);
        Assert.True(gradient.HasGeneralTransform);
        Assert.Equal((5d, 4d), gradient.Inverse.Apply(10, 8));
        Assert.Equal(16, geometry.Radial.Fill.Width);
        Assert.Equal(16, geometry.Radial.Fill.Height);
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
    public void Lowers_partial_focal_radius_overrides()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><radialGradient id='p' cx='0.5' cy='0.5' r='0.5'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient><radialGradient id='g' href='#p' fr='0.1'/></defs><rect width='8' height='8' fill='url(#g)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(.1, Assert.Single(root.Geometries).Radial.Gradient.FocalRadius);
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
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><radialGradient id='g' gradientTransform='translate(0.25)' cx='0.25' cy='0.5' r='0.5' fx='0.25' fy='0.5'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><rect width='8' height='8' fill='url(#g)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

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
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><radialGradient id='g' gradientTransform='scale(2)' cx='0.25' cy='0.25' r='0.25'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><rect width='8' height='8' fill='url(#g)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

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
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><radialGradient id='g' gradientTransform='matrix(2, 0, 0, 2, 0.5, 0)' cx='0.25' cy='0.25' r='0.25'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><rect width='8' height='8' fill='url(#g)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.RadialGradient, geometry.Kind);
        Assert.Equal(1, geometry.Radial.Gradient.CenterX);
        Assert.Equal(0.5, geometry.Radial.Gradient.CenterY);
        Assert.Equal(0.5, geometry.Radial.Gradient.Radius);
    }

    [Fact]
    public void Applies_radial_transform_to_inherited_circles()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><radialGradient id='p' cx='0.5' cy='0.5' r='0.5'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient><radialGradient id='g' href='#p' gradientTransform='translate(-0.25)'/></defs><rect width='8' height='8' fill='url(#g)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

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
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><radialGradient id='g' gradientUnits='userSpaceOnUse' gradientTransform='scale(2,1)' cx='4' cy='4' r='4'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><rect width='8' height='8' fill='url(#g)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

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
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><radialGradient id='g' gradientUnits='userSpaceOnUse' gradientTransform='scale(1,2)' cx='4' cy='4' r='4' fx='4' fy='7.9'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><rect width='8' height='8' fill='url(#g)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.RadialGradient, geometry.Kind);
        Assert.Equal(4, geometry.Radial.Gradient.CenterX);
        Assert.Equal(4, geometry.Radial.Gradient.CenterY);
        Assert.Equal(4, geometry.Radial.Gradient.Radius);
        Assert.Equal(7.9, geometry.Radial.Gradient.FocalY);
        Assert.True(geometry.Radial.Gradient.HasGeneralTransform);
        Assert.Equal((4d, 4d), geometry.Radial.Gradient.Inverse.Apply(4, 8));
    }

    [Fact]
    public void Rejects_outside_focal_before_nonuniform_radial_transform()
    {
        SvgException rejected = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><radialGradient id='g' gradientUnits='userSpaceOnUse' gradientTransform='scale(2,1)' cx='4' cy='4' r='4' fx='4' fy='8.1'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><rect width='8' height='8' fill='url(#g)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, rejected.ErrorCode);
    }

    [Fact]
    public void Admits_nonuniform_scale_radial_transforms()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><radialGradient id='g' gradientTransform='scale(2, 1)' cx='0.5' cy='0.5' r='0.5'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><rect width='8' height='8' fill='url(#g)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.RadialGradient, geometry.Kind);
        Assert.Equal(0.5, geometry.Radial.Gradient.CenterX);
        Assert.Equal(0.5, geometry.Radial.Gradient.Radius);
        Assert.True(geometry.Radial.Gradient.HasGeneralTransform);
    }

    [Fact]
    public void Admits_nonuniform_matrix_radial_transforms()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><radialGradient id='g' gradientTransform='matrix(2, 0, 0, 1, 0, 0)' cx='0.5' cy='0.5' r='0.5'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><rect width='8' height='8' fill='url(#g)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

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
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><radialGradient id='g' gradientUnits='userSpaceOnUse' cx='44' cy='32' r='22' fx='52' fy='32' gradientTransform='translate(64 0) scale(-1 1)'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><rect width='8' height='8' fill='url(#g)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.RadialGradient, geometry.Kind);
        Assert.Equal(44, geometry.Radial.Gradient.CenterX);
        Assert.Equal(32, geometry.Radial.Gradient.CenterY);
        Assert.Equal(22, geometry.Radial.Gradient.Radius);
        Assert.Equal(52, geometry.Radial.Gradient.FocalX);
        Assert.Equal(32, geometry.Radial.Gradient.FocalY);
        Assert.True(geometry.Radial.Gradient.HasGeneralTransform);
        Assert.Equal((44d, 32d), geometry.Radial.Gradient.Inverse.Apply(20, 32));
        Assert.Equal((52d, 32d), geometry.Radial.Gradient.Inverse.Apply(12, 32));
    }

    [Fact]
    public void Admits_rotated_radial_circles()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><radialGradient id='g' gradientUnits='userSpaceOnUse' cx='4' cy='4' r='4' gradientTransform='rotate(45)'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><rect width='8' height='8' fill='url(#g)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

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
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><radialGradient id='g' gradientUnits='userSpaceOnUse' cx='4' cy='4' r='4' gradientTransform='rotate(45) scale(2 1)'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><rect width='8' height='8' fill='url(#g)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

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
    public void Lowers_partial_radial_vector_overrides()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><radialGradient id='p' cx='0.5' cy='0.5' r='0.5'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient><radialGradient id='g' href='#p' cx='0.25'/></defs><rect width='8' height='8' fill='url(#g)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgRadialGradient gradient = Assert.Single(root.Geometries).Radial.Gradient;
        Assert.Equal(.25, gradient.CenterX);
        Assert.Equal(.25, gradient.FocalX);
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
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><radialGradient id='g' cx='0.5' cy='0.5' r='0.5'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><rect width='8' height='8' fill='red' stroke='url(#g)' stroke-width='2'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(5, root.Geometries.Length);
        Assert.Equal(SvgGeometryKind.Rectangle, root.Geometries[0].Kind);
        Assert.All(root.Geometries.Skip(1), band => Assert.Equal(SvgGeometryKind.RadialGradient, band.Kind));
        Assert.Equal(new SvgRectangle(-1, -1, 10, 2, default, true, null), root.Geometries[1].Radial.Fill);
        Assert.Equal(new SvgRectangle(0, 0, 8, 8, default, true, null), root.Geometries[1].Radial.Box);
    }

    [Fact]
    public void Lowers_single_stop_radial_strokes_as_constant_bands()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><radialGradient id='g' cx='0.5' cy='0.5' r='0.5'><stop offset='0.3' stop-color='#ff0000'/></radialGradient></defs><rect width='8' height='8' fill='red' stroke='url(#g)' stroke-width='2'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(5, root.Geometries.Length);
        Assert.Equal(SvgGeometryKind.Rectangle, root.Geometries[0].Kind);
        Assert.All(root.Geometries.Skip(1), band => Assert.Equal(SvgGeometryKind.RadialGradient, band.Kind));
    }

    [Fact]
    public void Retains_degenerate_radial_strokes_as_constant_shader_bands()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><radialGradient id='g' cx='0.5' cy='0.5' r='0'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><rect width='8' height='8' fill='red' stroke='url(#g)' stroke-width='2'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

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
    public void Lowers_radial_fills_on_use_instances()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><radialGradient id='g' cx='0.5' cy='0.5' r='0.5'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient><rect id='r' x='0' y='0' width='4' height='8'/></defs><use href='#r' x='4' y='0' fill='url(#g)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.RadialGradient, geometry.Kind);
        Assert.Equal(new SvgRectangle(4, 0, 4, 8, default, true, null), geometry.Radial.Fill);
        AssertEqualRadial(new SvgRadialGradient(new SvgColor(255, 0, 0, 255), new SvgColor(0, 0, 255, 255), 0.5, 0.5, 0.5, 0.5, 0.5, 0, 0, 1, false, false, SvgGradientSpread.Pad, new SvgGradientStop[] { new(0, new SvgColor(255, 0, 0, 255)), new(1, new SvgColor(0, 0, 255, 255)) }, 1, 1, false, SvgGradientTransform.Identity, false), geometry.Radial.Gradient);
    }

    [Fact]
    public void Lowers_user_space_radial_fills_on_use_instances_with_placement_offsets()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><radialGradient id='g' gradientUnits='userSpaceOnUse' cx='0' cy='0' r='8'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient><rect id='r' x='0' y='0' width='4' height='8'/></defs><use href='#r' x='4' y='0' fill='url(#g)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

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
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><radialGradient id='g' cx='0.5' cy='0.5' r='0.5'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient><symbol id='s' viewBox='0 0 4 4'><rect x='0' y='0' width='4' height='4'/></symbol></defs><use href='#s' x='0' y='0' width='8' height='8' fill='url(#g)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.RadialGradient, geometry.Kind);
    }
    [Fact]
    public void Lowers_masked_radial_gradients()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><radialGradient id='g' cx='0.5' cy='0.5' r='0.5'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient><mask id='m'><rect x='0' y='0' width='16' height='16' fill='#ffffff'/></mask></defs><rect width='8' height='8' fill='url(#g)' mask='url(#m)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

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
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='g'><stop offset='0' stop-color='#ff0000' stop-opacity='0.5'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><rect width='8' height='8' fill='url(#g)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.GradientFill, geometry.Kind);
        Assert.Equal(2, geometry.Gradient.Gradient.Stops.Length);
        Assert.Equal(new SvgColor(255, 0, 0, 128), geometry.Gradient.Gradient.Stops[0].Color);
        Assert.Equal(new SvgColor(0, 0, 255, 255), geometry.Gradient.Gradient.Stops[1].Color);
    }

    [Fact]
    public void Admits_middle_translucent_stops_with_untouched_neighbors()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='g'><stop offset='0' stop-color='#ff0000'/><stop offset='0.5' stop-color='#00ff00' stop-opacity='0.5'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><rect width='8' height='8' fill='url(#g)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

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
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><mask id='fade'><rect x='8' y='8' width='48' height='48' fill='#ffffff'/></mask></defs><rect x='8' y='8' width='48' height='48' fill='#ff0000' mask='url(#fade)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

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
        SvgLeafMaskAssertions.UniformScope(root, 128, new(16, 16, 32, 32), new(8, 8, 48, 48));
    }

    [Fact]
    public void Lowers_diagonal_gradient_polygons_as_gradient_fills()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='g'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><polygon points='0,0 8,0 0,8' fill='url(#g)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.GradientPolygon, geometry.Kind);
        Assert.True(geometry.GradientPolygon.HasFill);
        Assert.Equal(new SvgRectangle(0, 0, 8, 8, default, true, null),
            MappedPointListBox(geometry, new(0, 0, 8, 8, default, true, null), SvgAffineTransform.Identity));
    }

    [Fact]
    public void Lowers_diagonal_radial_polygons_as_radial_fills()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><radialGradient id='g' cx='0.5' cy='0.5' r='0.5'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><polygon points='0,0 8,0 0,8' fill='url(#g)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.RadialPolygon, geometry.Kind);
        Assert.True(geometry.RadialPolygon.HasFill);
        SvgAuthoredPath path = Assert.IsType<SvgAuthoredPath>(geometry.AuthoredPath);
        SvgRectangle bounds = SvgPathBounds.Compute(path.Commands, CancellationToken.None);
        Assert.Equal((0d, 0d, 8d, 8d), (bounds.X, bounds.Y, bounds.Width, bounds.Height));
        SvgGradientPaintMatrices matrices = Assert.IsType<SvgGradientPaintMatrices>(geometry.RadialPolygon.Gradient.NativePaintMatrices);
        Assert.Equal(new(8, 0, 0, 0, 8, 0), matrices.Local);
        Assert.Equal(SvgAffineTransform.Identity, matrices.Referencing);
        Assert.Equal(matrices.Referencing, path.SceneMap);
    }

    [Fact]
    public void Lowers_userspace_gradient_strokes_on_polygons_as_outlines()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='g' gradientUnits='userSpaceOnUse' x1='0' y1='0' x2='8' y2='0'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><polygon points='0,0 8,0 0,8' fill='none' stroke='url(#g)' stroke-width='2'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(2, root.Geometries.Length);
        Assert.Equal(SvgGeometryKind.PolygonFill, root.Geometries[0].Kind);
        Assert.False(root.Geometries[0].Polygon.HasFill);
        Assert.Equal(SvgGeometryKind.GradientPolygon, root.Geometries[1].Kind);
        Assert.True(root.Geometries[1].GradientPolygon.HasFill);
    }

    [Fact]
    public void Lowers_radial_strokes_on_polygons_as_outlines()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><radialGradient id='g' gradientUnits='userSpaceOnUse' cx='4' cy='4' r='4'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><polygon points='0,0 8,0 0,8' fill='none' stroke='url(#g)' stroke-width='2'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(2, root.Geometries.Length);
        Assert.Equal(SvgGeometryKind.PolygonFill, root.Geometries[0].Kind);
        Assert.Equal(SvgGeometryKind.RadialPolygon, root.Geometries[1].Kind);
        Assert.True(root.Geometries[1].RadialPolygon.HasFill);
    }

    [Fact]
    public void Lowers_bounding_box_radial_strokes_on_polygons_as_outlines()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><radialGradient id='g' cx='0.5' cy='0.5' r='0.5'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><polygon points='0,0 8,0 0,8' fill='none' stroke='url(#g)' stroke-width='2'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(2, root.Geometries.Length);
        Assert.Equal(SvgGeometryKind.RadialPolygon, root.Geometries[1].Kind);
        Assert.True(root.Geometries[1].RadialPolygon.HasFill);
        Assert.Equal(new SvgRectangle(0, 0, 8, 8, default, true, null), root.Geometries[1].RadialPolygon.Box);
    }
    [Fact]
    public void Lowers_userspace_gradient_strokes_on_curved_paths_as_outlines()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='g' gradientUnits='userSpaceOnUse' x1='0' y1='0' x2='8' y2='0'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><path d='M0,8 Q4,-4 8,8' fill='none' stroke='url(#g)' stroke-width='2'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.NotEmpty(root.Geometries);
        Assert.All(root.Geometries, outline => Assert.Equal(SvgGeometryKind.GradientPolygon, outline.Kind));
        Assert.All(root.Geometries, outline => Assert.True(outline.GradientPolygon.HasFill));
    }

    [Fact]
    public void Lowers_bounding_box_gradient_strokes_on_polygons_as_outlines()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='g' x1='0' y1='0' x2='1' y2='0'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><polygon points='0,0 8,0 0,8' fill='none' stroke='url(#g)' stroke-width='2'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(2, root.Geometries.Length);
        Assert.Equal(SvgGeometryKind.GradientPolygon, root.Geometries[1].Kind);
        Assert.True(root.Geometries[1].GradientPolygon.HasFill);
        Assert.Equal(new SvgRectangle(0, 0, 8, 8, default, true, null), root.Geometries[1].GradientPolygon.Box);
    }

    [Fact]
    public void Lowers_bounding_box_gradient_strokes_on_paths_as_outlines()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='g' x1='0' y1='0' x2='1' y2='0'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><path d='M0,0 L8,0 L0,8 Z' fill='none' stroke='url(#g)' stroke-width='2'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry outline = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.GradientPolygon, outline.Kind);
        Assert.True(outline.GradientPolygon.HasFill);
        Assert.Equal(new SvgRectangle(0, 0, 8, 8, default, true, null), outline.GradientPolygon.Box);
    }

    [Fact]
    public void Lowers_bounding_box_radial_strokes_on_paths_as_outlines()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><radialGradient id='g' cx='0.5' cy='0.5' r='0.5'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><path d='M0,0 L8,0 L0,8 Z' fill='none' stroke='url(#g)' stroke-width='2'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry outline = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.RadialPolygon, outline.Kind);
        Assert.True(outline.RadialPolygon.HasFill);
        Assert.Equal(new SvgRectangle(0, 0, 8, 8, default, true, null), outline.RadialPolygon.Box);
    }

    [Fact]
    public void Lowers_pattern_strokes_on_polygons_as_outlines()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><pattern id='p' width='8' height='8' patternUnits='userSpaceOnUse'><rect width='8' height='8' fill='red'/></pattern></defs><polygon points='0,0 8,0 0,8' fill='none' stroke='url(#p)' stroke-width='2'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(2, root.Geometries.Length);
        Assert.Equal(SvgGeometryKind.PolygonFill, root.Geometries[0].Kind);
        Assert.Equal(SvgGeometryKind.PatternPolygon, root.Geometries[1].Kind);
        Assert.True(root.Geometries[1].PatternPolygon.HasFill);
    }

    [Fact]
    public void Lowers_pattern_strokes_on_paths_as_outlines()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><pattern id='p' width='8' height='8' patternUnits='userSpaceOnUse'><rect width='8' height='8' fill='red'/></pattern></defs><path d='M0,0 L8,0 L0,8 Z' fill='none' stroke='url(#p)' stroke-width='2'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry outline = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.PatternPolygon, outline.Kind);
        Assert.True(outline.PatternPolygon.HasFill);
    }

    [Fact]
    public void Lowers_pattern_strokes_on_curved_paths_as_outlines()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><pattern id='p' width='8' height='8' patternUnits='userSpaceOnUse'><rect width='8' height='8' fill='red'/></pattern></defs><path d='M0,8 Q4,-4 8,8' fill='none' stroke='url(#p)' stroke-width='2'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.NotEmpty(root.Geometries);
        Assert.All(root.Geometries, outline => Assert.Equal(SvgGeometryKind.PatternPolygon, outline.Kind));
        Assert.All(root.Geometries, outline => Assert.True(outline.PatternPolygon.HasFill));
    }
    [Fact]
    public void Lowers_bounding_box_gradient_strokes_on_curved_paths_as_outlines()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='g' x1='0' y1='0' x2='1' y2='0'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><path d='M0,8 Q4,-4 8,8' fill='none' stroke='url(#g)' stroke-width='2'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry outline = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.GradientPolygon, outline.Kind);
        Assert.True(outline.GradientPolygon.HasFill);
        Assert.Equal(new SvgRectangle(0, 2, 8, 6, default, true, null), outline.GradientPolygon.Box);
    }

    [Fact]
    public void Lowers_bounding_box_radial_strokes_on_curved_paths_as_outlines()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><radialGradient id='g' cx='0.5' cy='0.5' r='0.5'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><path d='M0,8 Q4,-4 8,8' fill='none' stroke='url(#g)' stroke-width='2'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry outline = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.RadialPolygon, outline.Kind);
        Assert.True(outline.RadialPolygon.HasFill);
        Assert.Equal(new SvgRectangle(0, 2, 8, 6, default, true, null), outline.RadialPolygon.Box);
    }

    [Fact]
    public void Lowers_bounding_box_gradient_strokes_on_fractional_paths_as_outlines()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='g' x1='0' y1='0' x2='1' y2='0'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><path d='M0.5,0 L8,0 L0,8 Z' fill='none' stroke='url(#g)' stroke-width='2'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry outline = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.GradientPolygon, outline.Kind);
        Assert.True(outline.GradientPolygon.HasFill);
        Assert.Equal(new SvgRectangle(0, 0, 8, 8, default, true, null), outline.GradientPolygon.Box);
    }

    [Fact]
    public void Lowers_gradient_strokes_on_fractional_polygons_as_outlines()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='g' x1='0' y1='0' x2='1' y2='0'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><polygon points='0.5,0 8,0 0,8' fill='none' stroke='url(#g)' stroke-width='2'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(2, root.Geometries.Length);
        Assert.Equal(SvgGeometryKind.PolygonFill, root.Geometries[0].Kind);
        Assert.Equal(SvgGeometryKind.GradientPolygon, root.Geometries[1].Kind);
        Assert.True(root.Geometries[1].GradientPolygon.HasFill);
        Assert.Equal(FillBounds(root.Geometries[0]), root.Geometries[1].GradientPolygon.Box);
    }

    [Fact]
    public void Lowers_radial_strokes_on_fractional_polygons_as_outlines()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><radialGradient id='g' gradientUnits='userSpaceOnUse' cx='4' cy='4' r='4'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><polygon points='0.5,0 8,0 0,8' fill='none' stroke='url(#g)' stroke-width='2'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(2, root.Geometries.Length);
        Assert.Equal(SvgGeometryKind.RadialPolygon, root.Geometries[1].Kind);
        Assert.True(root.Geometries[1].RadialPolygon.HasFill);
    }

    [Fact]
    public void Lowers_pattern_strokes_on_fractional_polygons_as_outlines()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><pattern id='p' width='8' height='8' patternUnits='userSpaceOnUse'><rect width='8' height='8' fill='red'/></pattern></defs><polygon points='0.5,0 8,0 0,8' fill='none' stroke='url(#p)' stroke-width='2'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

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
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><mask id='fade'><rect x='0' y='0' width='32' height='32' fill='#ffffff'/></mask></defs><circle cx='16' cy='16' r='8' fill='#ff0000' mask='url(#fade)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

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
        SvgLeafMaskAssertions.UniformScope(root, 128, null, new(0, 0, 16, 16));
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
        SvgLeafMaskAssertions.UniformScope(root, 128, null, new(0, 0, 16, 16));
    }

    [Fact]
    public void Applies_a_gray_mask_to_a_radial_outline_pair()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><defs><radialGradient id='g' gradientUnits='userSpaceOnUse' cx='8' cy='8' r='8'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient><mask id='fade'><rect x='0' y='0' width='16' height='16' fill='#808080'/></mask></defs><polygon points='4,4 12,4 4,12' fill='#ff0000' stroke='url(#g)' stroke-width='2' mask='url(#fade)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(2, root.Geometries.Length);
        Assert.Equal(SvgGeometryKind.RadialPolygon, root.Geometries[1].Kind);
        Assert.Equal(255, root.Geometries[1].RadialPolygon.Opacity);
        SvgLeafMaskAssertions.UniformScope(root, 128, null, new(0, 0, 16, 16));
    }

    [Fact]
    public void Applies_a_gray_mask_to_a_pattern_outline_pair()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><defs><pattern id='p' width='8' height='8' patternUnits='userSpaceOnUse'><rect width='8' height='8' fill='red'/></pattern><mask id='fade'><rect x='0' y='0' width='16' height='16' fill='#808080'/></mask></defs><polygon points='4,4 12,4 4,12' fill='#ff0000' stroke='url(#p)' stroke-width='2' mask='url(#fade)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(2, root.Geometries.Length);
        Assert.Equal(SvgGeometryKind.PatternPolygon, root.Geometries[1].Kind);
        Assert.Equal(255, root.Geometries[1].PatternPolygon.Opacity);
        SvgLeafMaskAssertions.UniformScope(root, 128, null, new(0, 0, 16, 16));
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
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><mask id='fade' mask-type='alpha'><rect x='8' y='8' width='48' height='48' fill='#00ff00'/></mask></defs><rect x='8' y='8' width='48' height='48' fill='#ff0000' mask='url(#fade)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

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
        SvgLeafMaskAssertions.UniformScope(root, 128, new(16, 16, 32, 32), new(8, 8, 48, 48));
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
        SvgLeafMaskAssertions.UniformScope(root, 128, new(2, -0.8, 4, 9.6), new(0, -1, 8, 10));
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
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><mask id='window' x='0' y='0' width='1' height='1'><rect x='0' y='0' width='8' height='8' fill='#808080'/></mask></defs><g mask='url(#window)'><rect width='8' height='8' fill='red'/></g></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

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
        SvgLeafMaskAssertions.UniformScope(root, 128, new(20, 20, 24, 24), new(8, 8, 48, 48));
    }

    [Fact]
    public void Rejects_unknown_mask_unit_values()
    {
        SvgException exception = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><mask id='window' maskUnits='picas'><rect x='0' y='0' width='8' height='8' fill='#808080'/></mask></defs><rect width='8' height='8' fill='red' mask='url(#window)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, exception.ErrorCode);
    }

    [Fact]
    public void Clips_masked_use_content_to_explicit_regions()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><rect id='tile' width='8' height='8' fill='red'/><mask id='window' maskUnits='userSpaceOnUse' x='1' y='1' width='2' height='2'><rect x='0' y='0' width='8' height='8' fill='#808080'/></mask></defs><use href='#tile' mask='url(#window)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(new SvgRectangle(0, 0, 8, 8, new SvgColor(255, 0, 0, 255), true, null), Assert.Single(root.Rectangles));
        SvgLeafMaskAssertions.UniformScope(root, 128, new(1, 1, 2, 2), new(0, 0, 8, 8));
    }

    [Fact]
    public void Clips_masked_symbol_content_to_explicit_regions()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><symbol id='s' viewBox='0 0 8 8'><polygon points='0,0 8,0 0,8' fill='red'/></symbol></defs><use href='#s' width='8' height='8' mask='url(#m)'/><defs><mask id='m' maskUnits='userSpaceOnUse' x='2' y='2' width='4' height='4'><rect x='0' y='0' width='8' height='8' fill='#808080'/></mask></defs></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.PolygonFill, geometry.Kind);
        Assert.Equal(new SvgColor(255, 0, 0, 255), geometry.Polygon.Color);
        Assert.Null(geometry.Polygon.Clip);
        SvgLeafMaskAssertions.UniformScopeWithViewport(root, 128, new(2, 2, 4, 4), new(0, 0, 8, 8), SvgAffineTransform.Identity, new(0, 0, 8, 8));
    }

    [Fact]
    public void Applies_forward_custom_region_masks_to_use_instances()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><rect id='tile' width='8' height='8' fill='red'/></defs><use href='#tile' mask='url(#m)'/><defs><mask id='m' maskUnits='userSpaceOnUse' x='1' y='1' width='2' height='2'><rect x='0' y='0' width='8' height='8' fill='#808080'/></mask></defs></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(new SvgRectangle(0, 0, 8, 8, new SvgColor(255, 0, 0, 255), true, null), Assert.Single(root.Rectangles));
        SvgLeafMaskAssertions.UniformScope(root, 128, new(1, 1, 2, 2), new(0, 0, 8, 8));
    }

    [Fact]
    public void Applies_custom_mask_regions_on_masked_groups()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><mask id='window' maskUnits='userSpaceOnUse' x='1' y='1' width='2' height='2'><rect x='0' y='0' width='8' height='8' fill='#808080'/></mask></defs><g mask='url(#window)'><rect width='8' height='8' fill='red'/></g></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgDrawCommand begin = Assert.Single(root.Commands, command => command.Kind == SvgDrawCommandKind.BeginGroup);
        Assert.Equal(255, begin.Opacity);
        Assert.Equal(128, begin.MaskAlpha);
        Assert.Equal(new SvgClipBox(1, 1, 2, 2), begin.MaskRegion);
        Assert.Contains(root.Commands, command => command.Kind == SvgDrawCommandKind.EndGroup);
    }

    [Fact]
    public void Retains_default_white_mask_regions_around_groups()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><mask id='fade'><rect x='0' y='0' width='48' height='48' fill='#ffffff'/></mask></defs><g mask='url(#fade)'><rect width='8' height='8' fill='red'/></g></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgLeafMaskAssertions.UniformGroupScope(root, 255, null);
    }
    [Fact]
    public void Retains_default_white_mask_regions_around_polygon_groups()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><mask id='fade'><rect x='0' y='0' width='8' height='8' fill='#ffffff'/></mask></defs><g mask='url(#fade)'><polygon points='0,0 8,0 0,8' fill='red'/></g></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgLeafMaskAssertions.UniformGroupScope(root, 255, null);
        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.PolygonFill, geometry.Kind);
    }

    [Fact]
    public void Isolates_gray_masked_groups_before_applying_the_mask_value()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><mask id='fade'><rect x='0' y='0' width='48' height='48' fill='#808080'/></mask></defs><g mask='url(#fade)'><rect width='8' height='8' fill='red'/></g></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgLeafMaskAssertions.UniformGroupScope(root, 128, null);
        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.Rectangle, geometry.Kind);
        Assert.Equal(new SvgColor(255, 0, 0, 255), geometry.Rectangle.Color);
    }
    [Fact]
    public void Isolates_gray_masked_polygon_groups_without_changing_the_child_paint()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><mask id='fade'><rect x='0' y='0' width='8' height='8' fill='#808080'/></mask></defs><g mask='url(#fade)'><polygon points='0,0 8,0 0,8' fill='red'/></g></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgLeafMaskAssertions.UniformGroupScope(root, 128, null);
        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.PolygonFill, geometry.Kind);
        Assert.Equal(new SvgColor(255, 0, 0, 255), geometry.Polygon.Color);
    }

    [Fact]
    public void Retains_group_opacity_and_mask_values_as_separate_layer_operations()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><mask id='fade'><rect x='0' y='0' width='48' height='48' fill='#808080'/></mask></defs><g opacity='0.5' mask='url(#fade)'><rect width='8' height='8' fill='red'/></g></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

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
    public void Admits_masked_groups_with_covering_mask_content()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><mask id='m'><rect x='0' y='0' width='8' height='8' fill='#808080'/></mask></defs><g mask='url(#m)'><rect x='0' y='0' width='4' height='4' fill='red'/><rect x='4' y='4' width='4' height='4' fill='blue'/></g></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

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
    public void Rejects_missing_mask_references_on_groups()
    {
        SvgException exception = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><g mask='url(#missing)'><rect width='8' height='8' fill='red'/></g></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, exception.ErrorCode);
    }

    [Fact]
    public void Admits_sibling_after_empty_masked_group()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><mask id='m'><rect x='0' y='0' width='4' height='4' fill='#808080'/></mask></defs><g mask='url(#m)'/><rect x='0' y='0' width='8' height='8' fill='red'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

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
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg' x='0px' y='0px' width='12' height='7'><rect width='1' height='1' fill='red'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(12, root.Width?.Value);
        Assert.Equal(7, root.Height?.Value);
        Assert.Single(root.Rectangles);
        Assert.False(root.HasUnsupportedChild);
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
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg' xml:space='preserve' width='12' height='7'><rect width='1' height='1' fill='red'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(12, root.Width?.Value);
        Assert.Equal(7, root.Height?.Value);
        Assert.Single(root.Rectangles);
        Assert.False(root.HasUnsupportedChild);
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
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg' enable-background='new 0 0 595.28 841.89' width='12' height='7'><rect width='1' height='1' fill='red'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(12, root.Width?.Value);
        Assert.Equal(7, root.Height?.Value);
        Assert.Single(root.Rectangles);
        Assert.False(root.HasUnsupportedChild);
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
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs id='defs1'/><rect width='1' height='1' fill='red'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Single(root.Rectangles);
        Assert.False(root.HasUnsupportedChild);
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
    public void Rejects_invalid_root_clip_rule()
    {
        SvgException exception = Assert.Throws<SvgException>(() => SvgRootParser.Parse(System.Text.Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"5\" height=\"5\" clip-rule=\"bogus\"><rect x=\"1\" y=\"1\" width=\"2\" height=\"2\" fill=\"red\"/></svg>"), SvgRenderLimits.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, exception.ErrorCode);
    }
    [Fact]
    public void Admits_root_class_without_stylesheet()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg' class='external-icon' width='12' height='7'><rect width='1' height='1' fill='red'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Single(root.Rectangles);
        Assert.False(root.HasUnsupportedChild);
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
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg' class='external-icon'><style>.other{fill:blue}</style><rect width='1' height='1' fill='red'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

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

    private static SvgLinearPaintMapData AssertUserSpacePaint(SvgLinearGradient gradient, SvgAffineTransform reference)
    {
        Assert.True(gradient.IsUserSpace);
        SvgLinearPaintMap paint = Assert.IsType<SvgLinearPaintMap>(gradient.SourcePaint);
        Assert.True(paint.Data.IsMapped);
        Assert.Equal(SvgGradientTransform.Identity, paint.Data.Local);
        Assert.Equal(reference, paint.Data.Referencing);
        return paint.Data;
    }

    private static SvgRectangle MappedPointListBox(SvgGeometry geometry, SvgRectangle expectedSource, SvgAffineTransform expectedMap)
    {
        SvgAuthoredPath path = Assert.IsType<SvgAuthoredPath>(geometry.AuthoredPath);
        SvgRectangle bounds = SvgPathBounds.Compute(path.Commands, CancellationToken.None);
        Assert.Equal((expectedSource.X, expectedSource.Y, expectedSource.Width, expectedSource.Height),
            (bounds.X, bounds.Y, bounds.Width, bounds.Height));
        Assert.Equal(expectedMap, path.SceneMap);
        SvgLinearPaintMap paint = Assert.IsType<SvgLinearPaintMap>(geometry.GradientPolygon.Gradient.SourcePaint);
        Assert.True(paint.Data.IsMapped);
        Assert.Equal(new(expectedSource.Width, 0, 0, expectedSource.Height, expectedSource.X, expectedSource.Y), paint.Data.Local);
        Assert.Equal(expectedMap, paint.Data.Referencing);
        return path.SceneMap.Apply(bounds with { HasFill = geometry.GradientPolygon.HasFill });
    }
}
