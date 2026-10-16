namespace Lokad.Svg.Tests;

using Lokad.Svg.Fonts;

public sealed partial class OpenTypeFontTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Local_group_text_replays_instance_font_size_through_nested_use(bool nested)
    {
        const string label = "<g id='label'><text x='3' y='25'>A</text></g>";
        string uses = nested ? "<g id='alias'><use href='#label' font-size='26'/></g><use href='#alias' x='32'/>"
            : "<use href='#label' x='32' font-size='26'/>";
        string expectedBody = "<text x='3' y='25'>A</text>" + (nested ? "<text x='3' y='25' font-size='26'>A</text>" : string.Empty)
            + "<text x='35' y='25' font-size='26'>A</text>";
        const string prefix = "<svg xmlns='http://www.w3.org/2000/svg' width='64' height='48' font-family='Example' font-size='20'>";
        byte[] input = System.Text.Encoding.UTF8.GetBytes(prefix + label + uses + "</svg>"), unchanged = input.ToArray();
        byte[] expected = System.Text.Encoding.UTF8.GetBytes(prefix + expectedBody + "</svg>");
        (SvgFontPackCatalog catalog, CountingFontProvider provider, _) = CreateExamplePack(CreateTriangleFont());
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync(input, catalog, provider, CancellationToken.None);
        SvgRenderOptions options = new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, snapshot);
        Assert.Equal(SvgConverter.ConvertToPng(expected, options, CancellationToken.None).ToArray(), SvgConverter.ConvertToPng(input, options, CancellationToken.None).ToArray());
        Assert.Equal(unchanged, input);
    }

    [Theory]
    [InlineData("A<tspan dx='3'>A</tspan> A")]
    [InlineData("<![CDATA[A]]><tspan style='fill:blue' rotate='10'>A</tspan>")]
    [InlineData("<textPath href='#p'>A<tspan stroke='red' stroke-width='.2'>A</tspan></textPath> A")]
    public async Task Retained_group_text_reuses_ordinary_layout_and_source_css(string content)
    {
        string document = "<svg xmlns='http://www.w3.org/2000/svg' width='64' height='48'><style>.outer > .source text > tspan { fill: green !important; }</style><defs><path id='p' d='M0 32 L60 32'/></defs><g class='outer'><g id='label' class='source' font-family='Example' font-size='20'><text x='3' y='24'>" + content + "</text></g><text font-family='Example' font-size='20' x='32' y='44'>A</text></g></svg>";
        byte[] input = System.Text.Encoding.UTF8.GetBytes(document), unchanged = input.ToArray();
        byte[] direct = System.Text.Encoding.UTF8.GetBytes(document.Replace(" id='label'", string.Empty, StringComparison.Ordinal));
        (SvgFontPackCatalog catalog, CountingFontProvider provider, _) = CreateExamplePack(CreateTriangleAAndSpaceFont());
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync(input, catalog, provider, CancellationToken.None);
        SvgRenderOptions options = new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, snapshot);
        Assert.Equal(SvgConverter.ConvertToPng(direct, options, CancellationToken.None).ToArray(), SvgConverter.ConvertToPng(input, options, CancellationToken.None).ToArray());
        Assert.Equal(unchanged, input);
    }
}
