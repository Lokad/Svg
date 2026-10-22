using Lokad.Svg.Fonts;

namespace Lokad.Svg.Tests;

public sealed partial class OpenTypeFontTests
{
    public static IEnumerable<object[]> LeafSerializations()
    {
        foreach (string leaf in new[] { "rect", "path", "circle", "ellipse", "use" })
        foreach (string scope in new[] { "direct", "definition", "symbol", "clip", "mask", "pattern" })
        foreach (string children in new[] { "", "<!--comment-->", "<title>label</title>", "<desc>description</desc>" })
            yield return [leaf, scope, children];
        foreach (string children in new[] { "", "<!--comment-->", "<title>label</title>", "<desc>description</desc>" })
            yield return ["stop", "gradient", children];
    }

    [Theory]
    [MemberData(nameof(LeafSerializations))]
    public async Task Balanced_leaf_serializations_keep_source_order_and_prepared_output(string leaf, string scope, string children)
    {
        string geometry = leaf switch
        {
            "rect" => "<rect x='8' y='8' width='16' height='16'/>",
            "path" => "<path d='M8 8H24V24H8Z'/>",
            "circle" => "<circle cx='16' cy='16' r='8'/>",
            "ellipse" => "<ellipse cx='16' cy='16' rx='8' ry='4'/>",
            "use" => "<use href='#s'/>",
            _ => "<stop stop-color='red'/>"
        };
        byte[] input = Svg(geometry[..^2] + ">" + children + "</" + leaf + ">"), reference = Svg(geometry);
        byte[] expected = SvgConverter.ConvertToPng(reference, SvgRenderOptions.Default, CancellationToken.None).ToArray();
        Assert.Equal(expected, SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None).ToArray());
        (SvgFontPackCatalog catalog, CountingFontProvider provider, CountingFontSource _) = CreateExamplePack(CreateTriangleFont());
        using SvgPreparedDocument prepared = await SvgPreparedDocument.CreateAsync(input, catalog, provider, SvgRenderOptions.Default, CancellationToken.None);
        Assert.Equal(expected, prepared.ConvertToPng(CancellationToken.None).ToArray());
        Assert.Equal(0, provider.RequestCount);

        byte[] Svg(string content)
        {
            string definitions = "<defs><path id='s' d='M8 8L24 8L16 24Z'/></defs>";
            string body = scope switch
            {
                "direct" => content,
                "definition" => "<defs><g id='t'>" + content + "</g></defs><use href='#t'/>",
                "symbol" => "<defs><symbol id='t'>" + content + "</symbol></defs><use href='#t'/>",
                "clip" => "<defs><clipPath id='c'>" + content + "</clipPath></defs><rect width='32' height='32' fill='red' clip-path='url(#c)'/>",
                "mask" => "<defs><mask id='m'>" + content + "</mask></defs><rect width='32' height='32' fill='red' mask='url(#m)'/>",
                "pattern" => "<defs><pattern id='p' width='32' height='32' patternUnits='userSpaceOnUse'>" + content + "</pattern></defs><rect width='32' height='32' fill='url(#p)'/>",
                _ => "<defs><linearGradient id='p'>" + content + "</linearGradient></defs><rect width='32' height='32' fill='url(#p)'/>"
            };
            return System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32' fill='white'>" + definitions + body + "<rect x='28' y='28' width='4' height='4' fill='blue'/></svg>");
        }
    }

    [Theory]
    [InlineData("<clipPath id='r'><rect width='8' height='8'><rect/></rect></clipPath>")]
    [InlineData("<mask id='r'><rect width='8' height='8'><animate/></rect></mask>")]
    [InlineData("<pattern id='r' width='8' height='8'><path d='M0 0H8V8Z'><unknown/></path></pattern>")]
    [InlineData("<linearGradient id='r'><stop><rect/></stop></linearGradient>")]
    public void Resource_leaves_still_reject_drawing_animation_and_unknown_children(string resource)
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs>" + resource + "</defs></svg>");
        Assert.Equal(SvgErrorCode.UnsupportedFeature, Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None)).ErrorCode);
    }

    [Fact]
    public async Task Balanced_text_instances_still_discover_their_supplied_font()
    {
        const string root = "<svg xmlns='http://www.w3.org/2000/svg' width='16' height='8'><defs><text id='label' font-family='Example' font-size='100' y='4'>AAA</text></defs>";
        byte[] input = System.Text.Encoding.UTF8.GetBytes(root + "<use href='#label'><title>label</title></use></svg>");
        byte[] reference = System.Text.Encoding.UTF8.GetBytes(root + "<use href='#label'/></svg>");
        (SvgFontPackCatalog catalog, CountingFontProvider provider, CountingFontSource _) = CreateExamplePack(CreateTriangleFont());
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync(reference, catalog, provider, CancellationToken.None);
        byte[] expected = SvgConverter.ConvertToPng(reference, new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, snapshot), CancellationToken.None).ToArray();
        using SvgPreparedDocument prepared = await SvgPreparedDocument.CreateAsync(input, catalog, provider, SvgRenderOptions.Default, CancellationToken.None);
        Assert.Equal(expected, prepared.ConvertToPng(CancellationToken.None).ToArray());
    }
}
