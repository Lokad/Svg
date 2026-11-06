using Lokad.Svg.Parsing;

namespace Lokad.Svg.Tests;

[Trait("Layer", "Mechanism")]
public sealed class SvgRootEffectAdmissionTests
{
    [Theory]
    [InlineData("filter='url(#effect)'", "")]
    [InlineData("clip-path='url(#clip)'", "")]
    [InlineData("style='filter:url(#effect)'", "")]
    [InlineData("", "<style>svg{filter:url(#effect)}</style>")]
    public void Root_effects_reject_at_their_attribute_or_computed_style_boundary(string attributes, string stylesheet)
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16' "
            + attributes + ">" + stylesheet + "<rect width='8' height='8'/></svg>");
        SvgException error = Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, CancellationToken.None));
        Assert.Equal(SvgErrorCode.UnsupportedFeature, error.ErrorCode);
    }

    [Theory]
    [InlineData("filter:NoNe", "")]
    [InlineData("filter:none", "svg{filter:url(#missing)}")]
    [InlineData("filter:url(#missing)", "svg{filter:none!important}")]
    public void Winning_no_effect_css_preserves_the_root_source_without_resolving_overridden_filters(string inline, string stylesheet)
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16' style='"
            + inline + "'><style>" + stylesheet + "</style><rect width='8' height='8'/></svg>");
        using SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, CancellationToken.None);
        Assert.Equal(new SvgRectangle(0, 0, 8, 8, new SvgColor(0, 0, 0, 255), true, null), Assert.Single(root.Geometries.CharacterizeRectangles()));
    }
}
