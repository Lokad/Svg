using Lokad.Svg.Fonts;

namespace Lokad.Svg.Tests;

public sealed partial class OpenTypeFontTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Supplied_glyph_use_clips_keep_fractional_placement_and_forward_resolution(bool symbol, bool forward)
    {
        const string prefix = "<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32' font-family='Example' font-size='16'>";
        const string clip = "<clipPath id='c'><rect x='4' y='4' width='20' height='20'/></clipPath>";
        const string glyphs = "<text x='4' y='20'>AA</text>";
        string definition = symbol ? "<symbol id='s'>" + glyphs + "</symbol>" : glyphs.Replace("<text ", "<text id='s' ", StringComparison.Ordinal);
        string definitions = "<defs>" + clip + definition + "</defs>";
        const string placed = "<use href='#s' x='3.5' y='2.5' fill='red' clip-path='url(#c)'/>";
        const string grouped = "<g transform='translate(3.5 2.5)'><use href='#s' fill='red' clip-path='url(#c)'/></g>";
        using SvgFontSnapshot fonts = await SvgFontSnapshot.CreateAsync([CreateResolution(CreateTriangleFont())], CancellationToken.None);
        SvgRenderOptions options = new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, fonts);
        byte[] actual = System.Text.Encoding.UTF8.GetBytes(prefix + (forward ? placed + definitions : definitions + placed) + "</svg>");
        byte[] expected = System.Text.Encoding.UTF8.GetBytes(prefix + (forward ? grouped + definitions : definitions + grouped) + "</svg>");
        Assert.Equal(SvgConverter.ConvertToPng(expected, options, CancellationToken.None).ToArray(), SvgConverter.ConvertToPng(actual, options, CancellationToken.None).ToArray());
    }
}
