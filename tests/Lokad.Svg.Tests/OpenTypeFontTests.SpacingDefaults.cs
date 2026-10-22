using Lokad.Svg.Fonts;

namespace Lokad.Svg.Tests;

public sealed partial class OpenTypeFontTests
{
    [Theory]
    [InlineData("text", "attribute")]
    [InlineData("text", "inline")]
    [InlineData("text", "sheet")]
    [InlineData("span", "attribute")]
    [InlineData("span", "inline")]
    [InlineData("span", "sheet")]
    [InlineData("group", "attribute")]
    [InlineData("group", "inline")]
    [InlineData("group", "sheet")]
    [InlineData("use", "attribute")]
    [InlineData("use", "inline")]
    [InlineData("use", "sheet")]
    public async Task Normal_letter_spacing_resets_inherited_spacing_in_layout_and_discovery(string scope, string declaration)
    {
        byte[] input = Svg("normal"), reference = Svg("0");
        (SvgFontPackCatalog catalog, CountingFontProvider provider, CountingFontSource _) = CreateExamplePack(CreateTriangleFont());
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync(reference, catalog, provider, CancellationToken.None);
        foreach (double scale in new[] { 1d, 1.5d })
        {
            SvgRenderOptions options = new(null, scale, SvgColor.Transparent, SvgRenderLimits.Default, snapshot);
            byte[] expected = SvgConverter.ConvertToPng(reference, options, CancellationToken.None).ToArray();
            Assert.Equal(expected, SvgConverter.ConvertToPng(input, options, CancellationToken.None).ToArray());
            using SvgPreparedDocument prepared = await SvgPreparedDocument.CreateAsync(input, catalog, provider,
                new(null, scale, SvgColor.Transparent, SvgRenderLimits.Default), CancellationToken.None);
            Assert.Equal(expected, prepared.ConvertToPng(CancellationToken.None).ToArray());
        }

        byte[] Svg(string spacing)
        {
            string attribute = declaration == "attribute" ? "letter-spacing='" + spacing + "'"
                : declaration == "inline" ? "style='letter-spacing:" + spacing + "'" : "class='reset'";
            string rules = declaration == "sheet" ? "<style>.reset{letter-spacing:" + spacing + "}</style>" : "";
            string body = scope switch
            {
                "text" => "<text x='2' y='4' " + attribute + ">AAA</text>",
                "span" => "<text x='2' y='4'><tspan " + attribute + ">AAA</tspan></text>",
                "group" => "<g " + attribute + "><text x='2' y='4'>AAA</text></g>",
                _ => "<defs><text id='label' x='2' y='4'><tspan " + attribute + ">AAA</tspan></text></defs><use href='#label'/>"
            };
            return System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='24' height='8' font-family='Example' font-size='100' letter-spacing='3'>" + rules + body + "</svg>");
        }
    }
}
