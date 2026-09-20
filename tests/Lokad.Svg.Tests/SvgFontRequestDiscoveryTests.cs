using Lokad.Svg.Fonts;

namespace Lokad.Svg.Tests;

public sealed class SvgFontRequestDiscoveryTests
{
    [Fact]
    public void Discovers_inherited_and_overridden_text_font_requests()
    {
        SvgFontFaceRequest[] requests = SvgFontRequestDiscovery.Discover("""
            <svg xmlns="http://www.w3.org/2000/svg" font-family="Root">
              <text font-family="'Example'" font-weight="bold"><tspan>one</tspan><tspan font-style="italic">two</tspan></text>
            </svg>
            """u8.ToArray(), CancellationToken.None);

        Assert.Equal(2, requests.Length);
        Assert.Equal("Example", requests[0].Family);
        Assert.Equal(700, requests[0].Weight);
        Assert.False(requests[0].Italic);
        Assert.True(requests[1].Italic);
    }

    [Fact]
    public void Rejects_font_family_lists_instead_of_silently_selecting_one()
    {
        SvgException exception = Assert.Throws<SvgException>(() => SvgFontRequestDiscovery.Discover("<svg><text font-family='First, Second'>x</text></svg>"u8.ToArray(), CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, exception.ErrorCode);
    }

    [Fact]
    public void Inline_text_style_overrides_presentation_attributes()
    {
        SvgFontFaceRequest request = Assert.Single(SvgFontRequestDiscovery.Discover("<svg><text font-family='Attribute' font-weight='normal' style='font-family: Inline; font-weight: 700; font-style: italic'>x</text></svg>"u8.ToArray(), CancellationToken.None));

        Assert.Equal("Inline", request.Family);
        Assert.Equal(700, request.Weight);
        Assert.True(request.Italic);
    }
}
