namespace Lokad.Svg.Tests;

public sealed class SvgPaintFallbackTests
{
    private const string Definitions = "<defs><linearGradient id='g'><stop stop-color='#e04020'/><stop offset='1' stop-color='#2040e0'/></linearGradient>"
        + "<linearGradient id='empty'/><rect id='wrong' width='8' height='8'/></defs>";

    [Theory]
    [Trait("Layer", "Rendering")]
    [InlineData("url(#g) red", "url(#g)")]
    [InlineData("url(#missing) #32a060", "#32a060")]
    [InlineData("url(#missing) none", "none")]
    [InlineData("url(#wrong) #2080e0", "#2080e0")]
    [InlineData("url(#empty) red", "none")]
    [InlineData("url(#missing) currentColor", "#8020a0")]
    public void Fill_fallback_depends_on_resource_validity(string paint, string expected)
    {
        SvgRasterTestHelper.AssertPixelsEqual(Document("<rect width='32' height='16' fill=\"" + expected + "\"/>"),
            Document("<rect width='32' height='16' fill=\"" + paint + "\"/>"), SvgRenderOptions.Default);
    }

    [Theory]
    [Trait("Layer", "Rendering")]
    [InlineData("url(#g) #0ee")]
    [InlineData(" URL( \"#g\" ) #0ee ")]
    public void Stroke_fallback_keeps_the_available_gradient(string paint)
    {
        const string shape = "<path d='M2 8H30' fill='none' stroke='url(#g)' stroke-width='4' stroke-linecap='round'/>";
        SvgRasterTestHelper.AssertPixelsEqual(Document(shape),
            Document(shape.Replace("url(#g)", paint, StringComparison.Ordinal)), SvgRenderOptions.Default);
    }

    [Fact]
    [Trait("Layer", "Rendering")]
    public void Group_fallback_inherits_the_solid_color()
    {
        const string shape = "<rect width='32' height='16'/>";
        SvgRasterTestHelper.AssertPixelsEqual(Document("<g fill='#32a060'>" + shape + "</g>"),
            Document("<g fill='url(#missing) #32a060'>" + shape + "</g>"), SvgRenderOptions.Default);
    }

    [Theory]
    [Trait("Layer", "Rendering")]
    [InlineData("<g id='group' fill='PAINT'><rect width='16' height='16'/></g><use href='#group' x='16'/>")]
    [InlineData("<defs><symbol id='symbol' viewBox='0 0 32 16' fill='PAINT'><rect width='32' height='16'/></symbol></defs><use href='#symbol' width='32' height='16'/>")]
    [InlineData("<defs><symbol id='symbol' viewBox='0 0 32 16'><g fill='PAINT'><rect width='32' height='16'/></g></symbol></defs><use href='#symbol' width='32' height='16'/>")]
    [InlineData("<defs><rect id='shape' width='32' height='16'/></defs><use href='#shape' fill='PAINT'/>")]
    public void Retained_and_instanced_inheritance_resolves_missing_fallback(string shape)
        => SvgRasterTestHelper.AssertPixelsEqual(Document(shape.Replace("PAINT", "#32a060", StringComparison.Ordinal)),
            Document(shape.Replace("PAINT", "url(#missing) #32a060", StringComparison.Ordinal)), SvgRenderOptions.Default);

    [Theory]
    [Trait("Layer", "Mechanism")]
    [InlineData("<rect width='32' height='16' fill='url(#g) madeup'/>")]
    [InlineData("<rect width='32' height='16' fill='url(#missing) madeup'/>")]
    [InlineData("<rect width='32' height='16' fill='url(https://example.org/g.svg#g) red'/>")]
    [InlineData("<rect width='32' height='16' fill='url(#missing)'/>")]
    [InlineData("<path d='M0 8H32' marker-end='url(#g) red'/>")]
    [InlineData("<defs><linearGradient id='invalid' gradientUnits='invalid'/></defs><rect width='32' height='16' fill='url(#invalid) red'/>")]
    public void Fallback_does_not_admit_invalid_syntax_effect_tails_or_unsupported_servers(string shape)
        => Assert.Equal(SvgErrorCode.UnsupportedFeature, Assert.Throws<SvgException>(() =>
            SvgConverter.ConvertToPng(Document(shape), SvgRenderOptions.Default, CancellationToken.None)).ErrorCode);

    private static byte[] Document(string shape)
        => System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='16' color='#8020a0'>"
            + shape + Definitions + "</svg>");
}
