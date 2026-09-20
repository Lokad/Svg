using Lokad.Svg.Fonts;

namespace Lokad.Svg.Tests;

public sealed class SvgFontRequestDiscoveryTests
{
    [Fact]
    public void Discovers_the_renderable_text_font_request_without_span_overrides()
    {
        SvgFontFaceRequest[] requests = SvgFontRequestDiscovery.Discover("""
            <svg xmlns="http://www.w3.org/2000/svg" font-family="Root">
              <text font-family="'Example'" font-weight="bold"><tspan>one</tspan><tspan font-style="italic">two</tspan></text>
            </svg>
            """u8.ToArray(), CancellationToken.None);

        SvgFontFaceRequest request = Assert.Single(requests);
        Assert.Equal("Example", request.Family);
        Assert.Equal(700, request.Weight);
        Assert.False(request.Italic);
    }

    [Fact]
    public void Rejects_font_family_lists_instead_of_silently_selecting_one()
    {
        SvgException exception = Assert.Throws<SvgException>(() => SvgFontRequestDiscovery.Discover("<svg xmlns='http://www.w3.org/2000/svg'><text font-family='First, Second'>x</text></svg>"u8.ToArray(), CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, exception.ErrorCode);
    }

    [Fact]
    public void Inline_text_style_overrides_presentation_attributes()
    {
        SvgFontFaceRequest request = Assert.Single(SvgFontRequestDiscovery.Discover("<svg xmlns='http://www.w3.org/2000/svg'><text font-family='Attribute' font-weight='normal' style='font-family: Inline; font-weight: 700; font-style: italic'>x</text></svg>"u8.ToArray(), CancellationToken.None));

        Assert.Equal("Inline", request.Family);
        Assert.Equal(700, request.Weight);
        Assert.True(request.Italic);
    }

    [Fact]
    public void Uses_the_last_inline_font_declaration_for_each_property()
    {
        SvgFontFaceRequest request = Assert.Single(SvgFontRequestDiscovery.Discover("<svg xmlns='http://www.w3.org/2000/svg'><text style='font-family: First; font-family: Last; font-weight: 400; font-weight: 700; font-style: normal; font-style: italic'>x</text></svg>"u8.ToArray(), CancellationToken.None));

        Assert.Equal("Last", request.Family);
        Assert.Equal(700, request.Weight);
        Assert.True(request.Italic);
    }

    [Theory]
    [InlineData("font-family:", SvgErrorCode.UnsupportedFeature)]
    [InlineData("font-family: Example !important", SvgErrorCode.UnsupportedFeature)]
    [InlineData("font-family:: Example", SvgErrorCode.MalformedInput)]
    public void Rejects_the_same_invalid_inline_font_declarations_as_rendering(string declaration, SvgErrorCode expectedErrorCode)
    {
        SvgException exception = Assert.Throws<SvgException>(() => SvgFontRequestDiscovery.Discover(System.Text.Encoding.UTF8.GetBytes($"<svg xmlns='http://www.w3.org/2000/svg'><text style='{declaration}'>x</text></svg>"), CancellationToken.None));

        Assert.Equal(expectedErrorCode, exception.ErrorCode);
    }

    [Fact]
    public void Deduplicates_case_insensitive_equivalent_font_requests()
    {
        SvgFontFaceRequest request = Assert.Single(SvgFontRequestDiscovery.Discover("<svg xmlns='http://www.w3.org/2000/svg'><text font-family='Example'>one</text><text font-family='example'>two</text></svg>"u8.ToArray(), CancellationToken.None));

        Assert.Equal("Example", request.Family);
        Assert.Equal(400, request.Weight);
        Assert.False(request.Italic);
    }

    [Fact]
    public void Rejects_a_non_svg_root_instead_of_discovering_foreign_text()
    {
        SvgException exception = Assert.Throws<SvgException>(() => SvgFontRequestDiscovery.Discover("<not-svg><text font-family='Example'>x</text></not-svg>"u8.ToArray(), CancellationToken.None));

        Assert.Equal(SvgErrorCode.MalformedInput, exception.ErrorCode);
    }

    [Fact]
    public void Applies_caller_selected_structural_limits_during_discovery()
    {
        SvgRenderLimits limits = new(1_000_000, 1_024, 128, 1_024, 1_024, 1, 16, 1_000);

        SvgException exception = Assert.Throws<SvgException>(() => SvgFontRequestDiscovery.Discover("<svg xmlns='http://www.w3.org/2000/svg'><text font-family='Example'>x</text></svg>"u8.ToArray(), limits, CancellationToken.None));

        Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode);
    }

    [Fact]
    public void Ignores_font_requests_from_inert_definition_content()
    {
        SvgFontFaceRequest[] requests = SvgFontRequestDiscovery.Discover("<svg xmlns='http://www.w3.org/2000/svg'><defs><text font-family='Unused'>x</text></defs><rect width='1' height='1'/></svg>"u8.ToArray(), CancellationToken.None);

        Assert.Empty(requests);
    }

    [Fact]
    public void Does_not_discover_fonts_from_unrenderable_span_children()
    {
        SvgFontFaceRequest[] requests = SvgFontRequestDiscovery.Discover("<svg xmlns='http://www.w3.org/2000/svg'><tspan font-family='Unused'>x</tspan></svg>"u8.ToArray(), CancellationToken.None);

        Assert.Empty(requests);
    }

    [Theory]
    [InlineData("display='none'")]
    [InlineData("visibility='hidden'")]
    [InlineData("style='display: none'")]
    public void Ignores_font_requests_from_hidden_text_ancestors(string presentation)
    {
        SvgFontFaceRequest[] requests = SvgFontRequestDiscovery.Discover(System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' " + presentation + "><text font-family='Unused'>x</text></svg>"), CancellationToken.None);

        Assert.Empty(requests);
    }
}
