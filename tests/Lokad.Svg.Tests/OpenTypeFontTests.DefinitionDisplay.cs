namespace Lokad.Svg.Tests;

public sealed partial class OpenTypeFontTests
{
    [Fact]
    public async Task A_local_replacement_pattern_keeps_its_font_requirement_when_its_template_is_hidden()
    {
        (var catalog, var provider, _) = CreateExamplePack(CreateTriangleFont());
        foreach (bool forward in new[] { false, true })
        foreach (string display in new[] { "display='none'", "style='display:none'", "class='hidden'" })
        {
            string Input(string family)
            {
                string text = "<text x='0' y='7' font-size='12' font-family='" + family + "'>A</text>";
                string source = "<pattern id='base' patternUnits='userSpaceOnUse' " + display + ">" + text + "</pattern>";
                string replacement = "<pattern id='p' href='#base' width='8' height='8'>" + text + "</pattern>";
                return "<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><style>.hidden{display:none}</style><defs>" + (forward ? replacement + source : source + replacement) + "</defs><rect width='8' height='8' fill='url(#p)'/></svg>";
            }
            byte[] input = System.Text.Encoding.UTF8.GetBytes(Input("Example"));
            Assert.Equal(SvgErrorCode.UnsupportedFeature, Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None)).ErrorCode);
            using SvgPreparedDocument prepared = await SvgPreparedDocument.CreateAsync(input, catalog, provider, SvgRenderOptions.Default, CancellationToken.None);
            byte[] reference = prepared.ConvertToPng(CancellationToken.None).ToArray();
            Assert.NotEqual(Render(Document("")), reference);
            using SvgPreparedDocument fallback = await SvgPreparedDocument.CreateAsync(System.Text.Encoding.UTF8.GetBytes(Input("Unavailable")), catalog, provider, SvgRenderOptions.Default, CancellationToken.None);
            Assert.Equal(reference, fallback.ConvertToPng(CancellationToken.None).ToArray());
        }
        Assert.True(provider.RequestCount > 0);
    }

    [Theory]
    [MemberData(nameof(SvgDefinitionDisplayTests.ResourceCases), true, MemberType = typeof(SvgDefinitionDisplayTests))]
    public async Task Definition_display_suppresses_resource_fonts_before_preparation_and_marker_metrics(string kind, string scenario, string selector, bool forward, string actual, string expected, bool disabled)
    {
        _ = (kind, scenario, selector, forward);
        (var catalog, var provider, _) = CreateExamplePack(CreateTriangleFont());
        using SvgPreparedDocument control = await SvgPreparedDocument.CreateAsync(System.Text.Encoding.UTF8.GetBytes(expected), catalog, provider, SvgRenderOptions.Default, CancellationToken.None);
        byte[] reference = control.ConvertToPng(CancellationToken.None).ToArray();
        byte[] input = System.Text.Encoding.UTF8.GetBytes(actual);
        using SvgPreparedDocument prepared = await SvgPreparedDocument.CreateAsync(input, catalog, provider, SvgRenderOptions.Default, CancellationToken.None);
        Assert.Equal(reference, prepared.ConvertToPng(CancellationToken.None).ToArray());
        if (disabled)
        {
            Assert.Equal(0, provider.RequestCount);
            Assert.Equal(reference, SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None).ToArray());
        }
        else Assert.True(provider.RequestCount > 0);
    }
}
