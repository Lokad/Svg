using Lokad.Svg.Fonts;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed partial class OpenTypeFontTests
{
    public static IEnumerable<object[]> InapplicableTextLengths()
    {
        foreach (string text in new[] { "A", "AA", "A A" })
        foreach (string adjust in new[] { "spacing", "spacingAndGlyphs" })
        foreach (string anchor in new[] { "start", "middle", "end" })
        foreach (string scope in new[] { "direct", "span", "positions", "path" }) yield return [text, "0", adjust, anchor, scope];
        foreach (string length in new[] { "8", "24" })
        foreach (string anchor in new[] { "start", "middle", "end" })
        foreach (string scope in new[] { "direct", "span", "positions", "path" }) yield return ["A", length, "spacing", anchor, scope];
    }

    [Theory]
    [MemberData(nameof(InapplicableTextLengths))]
    public async Task Inapplicable_text_length_keeps_natural_layout_across_anchors_and_output_scales(string text, string length, string adjust, string anchor, string scope)
    {
        byte[] input = Svg(" textLength='" + length + "' lengthAdjust='" + adjust + "'"), reference = Svg("");
        (SvgFontPackCatalog catalog, CountingFontProvider provider, CountingFontSource _) = CreateExamplePack(CreateTriangleAAndSpaceFont());
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

        byte[] Svg(string fitting)
        {
            string content = scope == "span" ? "<tspan>" + text + "</tspan>" : scope == "path" ? "<textPath href='#p' startOffset='96'>" + text + "</textPath>" : text;
            string position = scope == "path" ? "" : scope == "positions" ? "x='96 110 124' y='24'" : "x='96' y='24'";
            return System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='192' height='32'><defs><path id='p' d='M0 24H192'/></defs><text font-family='Example' font-size='100' text-anchor='" + anchor + "' " + position + fitting + ">" + content + "</text></svg>");
        }
    }

    [Theory]
    [InlineData("A")]
    [InlineData("AA")]
    [InlineData("AAA")]
    public async Task One_shaped_glyph_uses_natural_metrics_even_when_several_scalars_form_a_ligature(string text)
    {
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync([CreateResolution(CreateLigatureFont(false))], CancellationToken.None);
        SvgTextStyle style = new(new("Example", 400, false), 37.25, 16, 24, 0, 0, 0, SvgTextAnchor.Middle, "label") { TextLength = 24 };
        RenderBudget budget = new(1_000_000);
        SvgGlyphDeviceMetrics metrics = new(1.5, SvgGlyphMetricTarget.Windows, false);
        using (SvgTextRun natural = SvgTextRunBuilder.Create(style with { TextLength = null }, text, [], [], metrics, snapshot, budget, CancellationToken.None))
        using (SvgTextRun actual = SvgTextRunBuilder.Create(style, text, [], [], metrics, snapshot, budget, CancellationToken.None))
        {
            Assert.Single(actual.Glyphs.ToArray());
            Assert.Equal(natural.Glyphs.ToArray(), actual.Glyphs.ToArray());
            Assert.Equal(natural.DeviceMetrics, actual.DeviceMetrics);
            Assert.Equal(1, actual.GlyphStretch);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }
}
