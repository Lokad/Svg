using Lokad.Svg.Fonts;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;
using System.Text;

namespace Lokad.Svg.Tests;

public sealed class SvgFontRequestDiscoveryTests
{
    [Trait("Layer", "Mechanism")]
    [Theory]
    [InlineData("marker-length-angle-units")]
    [InlineData("marker-overflow-cascade")]
    public void Admitted_marker_controls_do_not_suppress_a_later_required_face(string identity)
    {
        string control = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "effects-marker-sheet", identity + ".svg"));
        byte[] input = System.Text.Encoding.UTF8.GetBytes(control.Replace("</svg>", "<text font-family='Example' x='4' y='100'>A</text></svg>", StringComparison.Ordinal));
        Assert.Equal("Example", Assert.Single(SvgFontRequestDiscovery.Discover(input, CancellationToken.None)).Family);
    }

    [Trait("Layer", "Mechanism")]
    [Theory]
    [InlineData("<text>A</text>", "serif")]
    [InlineData("<g font-family='Example'><text>A</text></g>", "Example")]
    [InlineData("<text> </text>", "")]
    [InlineData("<text fill='none'>A</text>", "")]
    public void Initial_family_is_a_caller_request_only_for_painted_text(string content, string expected)
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg'>" + content + "</svg>");
        SvgFontFaceRequest[] requests = SvgFontRequestDiscovery.Discover(input, CancellationToken.None);
        Assert.Equal(expected.Length == 0 ? [] : new[] { expected }, requests.Select(static request => request.Family).ToArray());
    }

    [Trait("Layer", "Mechanism")]
    [Theory]
    [InlineData("12pt")]
    [InlineData("1pc")]
    [InlineData("0.5in")]
    [InlineData("1cm")]
    [InlineData("1mm")]
    public void Absolute_font_sizes_are_admitted_in_container_inheritance(string size)
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' font-size='" + size + "'><text>A</text></svg>");
        Assert.Equal("serif", Assert.Single(SvgFontRequestDiscovery.Discover(input, CancellationToken.None)).Family);
    }

    [Trait("Layer", "Contract")]
    [Fact]
    public void Repeated_discovery_borrows_the_committed_request_array_without_another_charge()
    {
        byte[] input = "<svg xmlns='http://www.w3.org/2000/svg'><text font-family='Example, serif'>A</text></svg>"u8.ToArray();
        using HostMemoryBudget host = new(2_000_000);
        RenderBudget budget = new(host);
        using (SvgDocumentPreparation preparation = SvgDocumentPreparation.Create(input, SvgRenderLimits.Default, budget, CancellationToken.None))
        {
            SvgFontFaceRequest[] first = preparation.DiscoverFontRequests();
            long retained = host.Reserved;
            Assert.Single(first);
            Assert.Same(first, preparation.DiscoverFontRequests());
            Assert.Equal(retained, host.Reserved);
        }
        Assert.Equal(0, host.Reserved);
    }

    [Trait("Layer", "Contract")]
    [Fact]
    public void Does_not_reserve_a_direct_text_chunk_without_eligible_text()
    {
        byte[] input = "<svg xmlns='http://www.w3.org/2000/svg'/>"u8.ToArray();
        SvgRenderLimits limits = new(1_024, 16, 16, 1_024, 16, 16, 16, 1);
        long workspace = SvgRootParser.GetXmlReaderWorkspaceBytes(input.Length, limits.MaximumAttributesPerElement);
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(workspace));

        SvgFontFaceRequest[] requests = SvgFontRequestDiscovery.Discover(input, limits, budget, CancellationToken.None);

        Assert.Empty(requests);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Trait("Layer", "Contract")]
    [Fact]
    public void Denied_reader_workspace_rejects_discovery_without_retention()
    {
        byte[] input = "<svg xmlns='http://www.w3.org/2000/svg'/>"u8.ToArray();
        SvgRenderLimits limits = new(1_024, 16, 16, 1_024, 16, 16, 16, 1);
        long workspace = SvgRootParser.GetXmlReaderWorkspaceBytes(input.Length, limits.MaximumAttributesPerElement);
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(workspace - 1));

        SvgException exception = Assert.Throws<SvgException>(() => SvgFontRequestDiscovery.Discover(input, limits, budget, CancellationToken.None));

        Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Trait("Layer", "Mechanism")]
    [Fact]
    public void Discovers_text_through_ignored_accessibility_attributes()
    {
        SvgFontFaceRequest request = Assert.Single(SvgFontRequestDiscovery.Discover("<svg xmlns='http://www.w3.org/2000/svg' role='img' aria-label='Mark'><g data-layer='1' tabindex='0'><text font-family='Example'>x</text></g></svg>"u8.ToArray(), CancellationToken.None));

        Assert.Equal("Example", request.Family);
    }

    [Trait("Layer", "Mechanism")]
    [Fact]
    public void Discovers_text_with_noop_stroke_styles()
    {
        SvgFontFaceRequest request = Assert.Single(SvgFontRequestDiscovery.Discover("<svg xmlns='http://www.w3.org/2000/svg'><text font-family='Example' stroke-linecap='butt'>x</text></svg>"u8.ToArray(), CancellationToken.None));

        Assert.Equal("Example", request.Family);
    }

    [Trait("Layer", "Mechanism")]
    [Fact]
    public void Discovers_text_with_default_font_variant()
    {
        SvgFontFaceRequest request = Assert.Single(SvgFontRequestDiscovery.Discover("<svg xmlns='http://www.w3.org/2000/svg'><style>#t { font-variant: normal; }</style><text id='t' font-family='Example' font-variant='normal'>x</text></svg>"u8.ToArray(), CancellationToken.None));

        Assert.Equal("Example", request.Family);
    }

    [Trait("Layer", "Mechanism")]
    [Fact]
    public void Rejects_small_caps_font_variants_before_acquisition()
    {
        SvgException exception = Assert.Throws<SvgException>(() => SvgFontRequestDiscovery.Discover("<svg xmlns='http://www.w3.org/2000/svg'><text font-family='Example' font-variant='small-caps'>x</text></svg>"u8.ToArray(), CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, exception.ErrorCode);
    }

    [Trait("Layer", "Mechanism")]
    [Fact]
    public void Discovers_text_with_ui_behavior_attributes()
    {
        SvgFontFaceRequest request = Assert.Single(SvgFontRequestDiscovery.Discover("<svg xmlns='http://www.w3.org/2000/svg'><text font-family='Example' pointer-events='none' cursor='pointer'>x</text></svg>"u8.ToArray(), CancellationToken.None));

        Assert.Equal("Example", request.Family);
    }

    [Trait("Layer", "Mechanism")]
    [Fact]
    public void Discovers_text_around_hidden_overflow_viewports()
    {
        SvgFontFaceRequest request = Assert.Single(SvgFontRequestDiscovery.Discover("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><svg x='1' y='1' width='3' height='3' overflow='hidden'><rect width='5' height='5' fill='red'/></svg><text font-family='Example'>x</text></svg>"u8.ToArray(), CancellationToken.None));

        Assert.Equal("Example", request.Family);
    }

    [Trait("Layer", "Mechanism")]
    [Fact]
    public void Discovers_text_on_versioned_roots()
    {
        SvgFontFaceRequest request = Assert.Single(SvgFontRequestDiscovery.Discover("<svg xmlns='http://www.w3.org/2000/svg' version='1.1' baseProfile='tiny'><text font-family='Example'>x</text></svg>"u8.ToArray(), CancellationToken.None));

        Assert.Equal("Example", request.Family);
    }

    [Trait("Layer", "Mechanism")]
    [Fact]
    public void Skips_font_requests_for_whitespace_only_text_and_discovers_normalized_attribute_free_span_content()
    {
        SvgFontFaceRequest[] empty = SvgFontRequestDiscovery.Discover("<svg xmlns='http://www.w3.org/2000/svg'><text font-family='Unused'> \t\r\n </text></svg>"u8.ToArray(), CancellationToken.None);
        SvgFontFaceRequest request = Assert.Single(SvgFontRequestDiscovery.Discover("<svg xmlns='http://www.w3.org/2000/svg'><text font-family='Example'> \t<tspan>A</tspan>\r\n </text></svg>"u8.ToArray(), CancellationToken.None));
        SvgFontFaceRequest preserved = Assert.Single(SvgFontRequestDiscovery.Discover("<svg xmlns='http://www.w3.org/2000/svg'><text font-family='Preserved' xml:space='preserve'> </text></svg>"u8.ToArray(), CancellationToken.None));

        Assert.Empty(empty);
        Assert.Equal("Example", request.Family);
        Assert.Equal("Preserved", preserved.Family);
    }

    [Trait("Layer", "Mechanism")]
    [Fact]
    public void Discovers_whitespace_preserved_by_a_nested_xml_space_span()
    {
        SvgFontFaceRequest request = Assert.Single(SvgFontRequestDiscovery.Discover("<svg xmlns='http://www.w3.org/2000/svg'><text font-family='Preserved'><tspan xml:space='preserve'> </tspan></text></svg>"u8.ToArray(), CancellationToken.None));

        Assert.Equal("Preserved", request.Family);
    }

    [Trait("Layer", "Mechanism")]
    [Fact]
    public void Skips_whitespace_trimmed_by_a_nested_default_xml_space_span()
    {
        SvgFontFaceRequest[] requests = SvgFontRequestDiscovery.Discover("<svg xmlns='http://www.w3.org/2000/svg'><text font-family='Unused' xml:space='preserve'><tspan xml:space='default'> </tspan></text></svg>"u8.ToArray(), CancellationToken.None);

        Assert.Empty(requests);
    }

    [Trait("Layer", "Mechanism")]
    [Fact]
    public void Discovers_whitespace_preserved_by_bounded_css_whitespace_declarations()
    {
        SvgFontFaceRequest inline = Assert.Single(SvgFontRequestDiscovery.Discover("<svg xmlns='http://www.w3.org/2000/svg'><text font-family='Inline' style='white-space: pre'> </text></svg>"u8.ToArray(), CancellationToken.None));
        SvgFontFaceRequest inherited = Assert.Single(SvgFontRequestDiscovery.Discover("<svg xmlns='http://www.w3.org/2000/svg'><style>g { white-space: pre; }</style><g><text font-family='Inherited'> </text></g></svg>"u8.ToArray(), CancellationToken.None));
        SvgFontFaceRequest textStylesheet = Assert.Single(SvgFontRequestDiscovery.Discover("<svg xmlns='http://www.w3.org/2000/svg'><style>#label { white-space: pre; }</style><text id='label' font-family='TextStylesheet'> </text></svg>"u8.ToArray(), CancellationToken.None));
        SvgFontFaceRequest textClassStylesheet = Assert.Single(SvgFontRequestDiscovery.Discover("<svg xmlns='http://www.w3.org/2000/svg'><style>.label { white-space: pre; }</style><text class='label' font-family='TextClassStylesheet'> </text></svg>"u8.ToArray(), CancellationToken.None));
        SvgFontFaceRequest span = Assert.Single(SvgFontRequestDiscovery.Discover("<svg xmlns='http://www.w3.org/2000/svg'><text font-family='Span'><tspan style='white-space: pre'> </tspan></text></svg>"u8.ToArray(), CancellationToken.None));
        SvgFontFaceRequest spanStylesheet = Assert.Single(SvgFontRequestDiscovery.Discover("<svg xmlns='http://www.w3.org/2000/svg'><style>#space { white-space: pre; }</style><text font-family='SpanStylesheet'><tspan id='space'> </tspan></text></svg>"u8.ToArray(), CancellationToken.None));
        SvgFontFaceRequest[] cssOverride = SvgFontRequestDiscovery.Discover("<svg xmlns='http://www.w3.org/2000/svg'><text font-family='Unused' xml:space='preserve' style='white-space: normal'> </text></svg>"u8.ToArray(), CancellationToken.None);

        Assert.Equal("Inline", inline.Family);
        Assert.Equal("Inherited", inherited.Family);
        Assert.Equal("TextStylesheet", textStylesheet.Family);
        Assert.Equal("TextClassStylesheet", textClassStylesheet.Family);
        Assert.Equal("Span", span.Family);
        Assert.Equal("SpanStylesheet", spanStylesheet.Family);
        Assert.Empty(cssOverride);
    }

    [Trait("Layer", "Mechanism")]
    [Fact]
    public void Restores_the_active_direct_text_candidate_after_nested_attribute_free_spans()
    {
        SvgFontFaceRequest[] requests = SvgFontRequestDiscovery.Discover("<svg xmlns='http://www.w3.org/2000/svg'><text font-family='First'><tspan><tspan>one</tspan></tspan></text><text font-family='Second'>two</text></svg>"u8.ToArray(), CancellationToken.None);

        Assert.Collection(requests,
            first => Assert.Equal("First", first.Family),
            second => Assert.Equal("Second", second.Family));
    }

    [Trait("Layer", "Mechanism")]
    [Fact]
    public void Discovers_both_styles_for_the_original_italic_span()
    {
        SvgFontFaceRequest[] requests = SvgFontRequestDiscovery.Discover("""
            <svg xmlns="http://www.w3.org/2000/svg" font-family="Root">
              <text font-family="'Example'" font-weight="bold"><tspan>one</tspan><tspan font-style="italic">two</tspan></text>
            </svg>
            """u8.ToArray(), CancellationToken.None);

        Assert.Equal(new[] { ("Example", 700, false), ("Example", 700, true) }, requests.Select(request => (request.Family, request.Weight, request.Italic)));
    }

    [Trait("Layer", "Mechanism")]
    [Fact]
    public void Discovers_family_lists_without_silently_selecting_one()
    {
        SvgFontFaceRequest request = Assert.Single(SvgFontRequestDiscovery.Discover("<svg xmlns='http://www.w3.org/2000/svg'><text font-family='First, Second'>x</text></svg>"u8.ToArray(), CancellationToken.None));
        Assert.Equal(new[] { "First", "Second" }, request.Families.Select(family => family.Name));
    }

    [Trait("Layer", "Mechanism")]
    [Fact]
    public void Inline_text_style_overrides_presentation_attributes()
    {
        SvgFontFaceRequest request = Assert.Single(SvgFontRequestDiscovery.Discover("<svg xmlns='http://www.w3.org/2000/svg'><text font-family='Attribute' font-weight='normal' style='font-family: Inline; font-weight: 700; font-style: italic'>x</text></svg>"u8.ToArray(), CancellationToken.None));

        Assert.Equal("Inline", request.Family);
        Assert.Equal(700, request.Weight);
        Assert.True(request.Italic);
    }

    [Trait("Layer", "Mechanism")]
    [Fact]
    public void Uses_the_last_inline_font_declaration_for_each_property()
    {
        SvgFontFaceRequest request = Assert.Single(SvgFontRequestDiscovery.Discover("<svg xmlns='http://www.w3.org/2000/svg'><text style='font-family: First; font-family: Last; font-weight: 400; font-weight: 700; font-style: normal; font-style: italic'>x</text></svg>"u8.ToArray(), CancellationToken.None));

        Assert.Equal("Last", request.Family);
        Assert.Equal(700, request.Weight);
        Assert.True(request.Italic);
    }

    [Trait("Layer", "Mechanism")]
    [Theory]
    [InlineData("font-family:", SvgErrorCode.UnsupportedFeature)]
    [InlineData("font-family:: Example", SvgErrorCode.MalformedInput)]
    public void Rejects_the_same_invalid_inline_font_declarations_as_rendering(string declaration, SvgErrorCode expectedErrorCode)
    {
        SvgException exception = Assert.Throws<SvgException>(() => SvgFontRequestDiscovery.Discover(System.Text.Encoding.UTF8.GetBytes($"<svg xmlns='http://www.w3.org/2000/svg'><text style='{declaration}'>x</text></svg>"), CancellationToken.None));

        Assert.Equal(expectedErrorCode, exception.ErrorCode);
    }

    [Trait("Layer", "Mechanism")]
    [Fact]
    public void Deduplicates_case_insensitive_equivalent_font_requests()
    {
        SvgFontFaceRequest request = Assert.Single(SvgFontRequestDiscovery.Discover("<svg xmlns='http://www.w3.org/2000/svg'><text font-family='Example'>one</text><text font-family='example'>two</text></svg>"u8.ToArray(), CancellationToken.None));

        Assert.Equal("Example", request.Family);
        Assert.Equal(400, request.Weight);
        Assert.False(request.Italic);
    }

    [Trait("Layer", "Mechanism")]
    [Fact]
    public void Rejects_a_non_svg_root_instead_of_discovering_foreign_text()
    {
        SvgException exception = Assert.Throws<SvgException>(() => SvgFontRequestDiscovery.Discover("<not-svg><text font-family='Example'>x</text></not-svg>"u8.ToArray(), CancellationToken.None));

        Assert.Equal(SvgErrorCode.MalformedInput, exception.ErrorCode);
    }

    [Trait("Layer", "Mechanism")]
    [Fact]
    public void Applies_caller_selected_structural_limits_during_discovery()
    {
        SvgRenderLimits limits = new(1_024, 128, 1_024, 1_024, 1, 16);

        SvgException exception = Assert.Throws<SvgException>(() => SvgFontRequestDiscovery.Discover("<svg xmlns='http://www.w3.org/2000/svg'><text font-family='Example'>x</text></svg>"u8.ToArray(), limits, CancellationToken.None));

        Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode);
    }

    [Trait("Layer", "Mechanism")]
    [Fact]
    public void Ignores_font_requests_from_inert_definition_content()
    {
        SvgFontFaceRequest[] requests = SvgFontRequestDiscovery.Discover("<svg xmlns='http://www.w3.org/2000/svg'><defs><text font-family='Unused'>x</text></defs><rect width='1' height='1'/></svg>"u8.ToArray(), CancellationToken.None);

        Assert.Empty(requests);
    }

    [Trait("Layer", "Mechanism")]
    [Theory]
    [InlineData("title")]
    [InlineData("desc")]
    [InlineData("metadata")]
    public void Ignores_font_requests_from_other_inert_document_content(string container)
    {
        SvgFontFaceRequest[] requests = SvgFontRequestDiscovery.Discover(System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg'><" + container + "><text font-family='Unused'>x</text></" + container + "></svg>"), CancellationToken.None);

        Assert.Empty(requests);
    }

    [Trait("Layer", "Mechanism")]
    [Fact]
    public void Retains_the_outer_inert_boundary_after_a_nested_inert_element()
    {
        SvgFontFaceRequest[] requests = SvgFontRequestDiscovery.Discover("<svg xmlns='http://www.w3.org/2000/svg'><metadata><desc>ignored</desc><text font-family='Unused'>x</text></metadata></svg>"u8.ToArray(), CancellationToken.None);

        Assert.Empty(requests);
    }

    [Trait("Layer", "Mechanism")]
    [Theory]
    [InlineData("symbol")]
    [InlineData("unsupported-container")]
    public void Does_not_discover_fonts_from_text_outside_renderable_container_paths(string container)
    {
        SvgFontFaceRequest[] requests = SvgFontRequestDiscovery.Discover(System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg'><" + container + "><text font-family='Unused'>x</text></" + container + "></svg>"), CancellationToken.None);

        Assert.Empty(requests);
    }

    [Trait("Layer", "Mechanism")]
    [Fact]
    public void Finds_nested_text_using_the_root_fallback_viewport()
    {
        SvgFontFaceRequest[] requests = SvgFontRequestDiscovery.Discover("<svg xmlns='http://www.w3.org/2000/svg'><svg><text font-family='Unused'>x</text></svg></svg>"u8.ToArray(), CancellationToken.None);
        Assert.Equal("Unused", Assert.Single(requests).Family);
    }

    [Trait("Layer", "Mechanism")]
    [Fact]
    public void Does_not_discover_fonts_from_unrenderable_span_children()
    {
        SvgFontFaceRequest[] requests = SvgFontRequestDiscovery.Discover("<svg xmlns='http://www.w3.org/2000/svg'><tspan font-family='Unused'>x</tspan></svg>"u8.ToArray(), CancellationToken.None);

        Assert.Empty(requests);
    }

    [Trait("Layer", "Mechanism")]
    [Theory]
    [InlineData("rotate(5)")]
    [InlineData("scale(2)")]
    public void Discovers_the_declared_face_from_text_with_an_inherited_affine_transform(string transform)
    {
        SvgFontFaceRequest[] requests = SvgFontRequestDiscovery.Discover(System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg'><g transform='" + transform + "'><text font-family='Unused'>x</text></g></svg>"), CancellationToken.None);

        Assert.Equal("Unused", Assert.Single(requests).Family);
    }

    [Trait("Layer", "Mechanism")]
    [Fact]
    public void Discovers_fonts_from_text_with_an_inherited_identity_transform()
    {
        SvgFontFaceRequest request = Assert.Single(SvgFontRequestDiscovery.Discover("<svg xmlns='http://www.w3.org/2000/svg'><g transform='translate(0)'><text font-family='Example'>x</text></g></svg>"u8.ToArray(), CancellationToken.None));

        Assert.Equal("Example", request.Family);
    }

    [Trait("Layer", "Mechanism")]
    [Fact]
    public void Rejects_an_invalid_inherited_font_size_with_the_renderer_font_inheritance_rules()
    {
        SvgException exception = Assert.Throws<SvgException>(() => SvgFontRequestDiscovery.Discover("<svg xmlns='http://www.w3.org/2000/svg' font-size='0'><text font-family='Example'>x</text></svg>"u8.ToArray(), CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, exception.ErrorCode);
    }

    [Trait("Layer", "Mechanism")]
    [Theory]
    [InlineData("display='none'")]
    [InlineData("visibility='hidden'")]
    [InlineData("style='display: none'")]
    public void Ignores_font_requests_from_hidden_text_ancestors(string presentation)
    {
        SvgFontFaceRequest[] requests = SvgFontRequestDiscovery.Discover(System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' " + presentation + "><text font-family='Unused'>x</text></svg>"), CancellationToken.None);

        Assert.Empty(requests);
    }

    [Trait("Layer", "Mechanism")]
    [Fact]
    public void Applies_the_renderer_stylesheet_cascade_when_discovering_hidden_text()
    {
        SvgFontFaceRequest[] requests = SvgFontRequestDiscovery.Discover("<svg xmlns='http://www.w3.org/2000/svg'><style>.hidden { display: none; }</style><g class='hidden'><text font-family='Unused'>x</text></g></svg>"u8.ToArray(), CancellationToken.None);

        Assert.Empty(requests);
    }
    [Trait("Layer", "Contract")]
    [Fact]
    public void Discovery_budgets_ancestor_control_storage()
    {
        StringBuilder svg = new("<svg xmlns='http://www.w3.org/2000/svg'>");
        for (int level = 0; level < 20; level++)
        {
            svg.Append("<g>");
        }
        svg.Append("<rect width='1' height='1'/>");
        for (int level = 0; level < 20; level++)
        {
            svg.Append("</g>");
        }
        svg.Append("</svg>");
        SvgRenderLimits limits = SvgRenderLimits.Default;
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(600));

        SvgException exception = Assert.Throws<SvgException>(() => SvgFontRequestDiscovery.Discover(System.Text.Encoding.UTF8.GetBytes(svg.ToString()), limits, budget, CancellationToken.None));

        Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode);
        RenderBudgetSnapshot snapshot = budget.Snapshot();
        Assert.Equal(0, snapshot.CurrentBytes);
        Assert.True(snapshot.PeakBytes > 0 || snapshot.DeniedReservationCount > 0);
    }

    [Trait("Layer", "Contract")]
    [Fact]
    public void Discovery_budgets_distinct_request_keys()
    {
        StringBuilder svg = new("<svg xmlns='http://www.w3.org/2000/svg'>");
        for (int index = 0; index < 10; index++)
        {
            svg.Append("<text font-family='Family");
            svg.Append(index);
            svg.Append("'>x</text>");
        }
        svg.Append("</svg>");
        SvgRenderLimits limits = SvgRenderLimits.Default;
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(9000));

        SvgException exception = Assert.Throws<SvgException>(() => SvgFontRequestDiscovery.Discover(System.Text.Encoding.UTF8.GetBytes(svg.ToString()), limits, budget, CancellationToken.None));

        Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Trait("Layer", "Contract")]
    [Fact]
    public void Discovery_releases_its_preparation_ledger_on_success()
    {
        SvgRenderLimits limits = SvgRenderLimits.Default;
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(268_435_456L));

        SvgFontFaceRequest[] requests = SvgFontRequestDiscovery.Discover("<svg xmlns='http://www.w3.org/2000/svg'><text font-family='Example'>x</text></svg>"u8.ToArray(), limits, budget, CancellationToken.None);

        SvgFontFaceRequest request = Assert.Single(requests);
        Assert.Equal("Example", request.Family);
        RenderBudgetSnapshot snapshot = budget.Snapshot();
        Assert.Equal(0, snapshot.CurrentBytes);
        Assert.True(snapshot.PeakBytes > 0);
        Assert.Equal(0, snapshot.DeniedReservationCount);
    }

    [Trait("Layer", "Contract")]
    [Fact]
    public void Discovery_observes_cancellation_and_releases_its_ledger()
    {
        SvgRenderLimits limits = SvgRenderLimits.Default;
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(268_435_456L));
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() => SvgFontRequestDiscovery.Discover("<svg xmlns='http://www.w3.org/2000/svg'><text font-family='Example'>x</text></svg>"u8.ToArray(), limits, budget, cancellation.Token));

        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }
}
