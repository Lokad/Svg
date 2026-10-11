using Lokad.Svg.Fonts;

namespace Lokad.Svg.Tests;

public sealed partial class OpenTypeFontTests
{
    [Theory]
    [InlineData("<text font-family='Example' font-size='100' y='3' fill-rule='RULE'>A</text>")]
    [InlineData("<text font-family='Example' font-size='100' y='3' style='fill-rule:RULE'>A</text>")]
    [InlineData("<g fill-rule='RULE'><text font-family='Example' font-size='100' y='3' fill-rule='inherit'>A</text></g>")]
    [InlineData("<text font-family='Example' font-size='100' y='3' fill-rule='nonzero'>A</text><style>text{fill-rule:RULE}</style>")]
    [InlineData("<text font-family='Example' font-size='100' y='3' fill-rule='RULE'><tspan>A</tspan></text>")]
    public async Task Valid_text_fill_rules_preserve_glyph_rendering_and_prepared_discovery(string content)
    {
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync([CreateResolution(CreateTriangleFont())], CancellationToken.None);
        SvgRenderOptions options = new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, snapshot);
        ReadOnlyMemory<byte> expected = SvgConverter.ConvertToPng("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><text font-family='Example' font-size='100' y='3'>A</text></svg>"u8.ToArray(), options, CancellationToken.None);

        foreach (string rule in new[] { "nonzero", "evenodd" })
        {
            byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'>" + content.Replace("RULE", rule, StringComparison.Ordinal) + "</svg>");
            Assert.Equal("Example", Assert.Single(SvgFontRequestDiscovery.Discover(input, CancellationToken.None)).Family);
            Assert.Equal(expected.ToArray(), SvgConverter.ConvertToPng(input, options, CancellationToken.None).ToArray());
            using SvgPreparedDocument document = await CreateSceneDocumentAsync(input);
            Assert.Equal(expected.ToArray(), document.ConvertToPng(CancellationToken.None).ToArray());
        }
    }
}
