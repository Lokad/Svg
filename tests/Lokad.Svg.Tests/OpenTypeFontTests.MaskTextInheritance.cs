using System.Security.Cryptography;
using Lokad.Svg.Fonts;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed partial class OpenTypeFontTests
{
    public static IEnumerable<object[]> MaskTextStyleCases()
    {
        foreach (string location in new[] { "root", "ancestor", "mask", "group" })
        foreach (bool normalized in new[] { false, true })
        foreach (string type in new[] { "alpha", "luminance" })
        foreach (string map in new[] { "translate(0)", "rotate(10 16 16)" })
        foreach (string consumer in new[] { "direct", "symbol", "pattern" })
            yield return [location, normalized, type, map, consumer];
    }

    [Theory]
    [MemberData(nameof(MaskTextStyleCases))]
    public async Task Grouped_masks_inherit_the_resource_text_style_instead_of_the_masked_target(string location, bool normalized, string type, string map, string consumer)
    {
        using SvgFontSnapshot fonts = await CreateMaskStyleFont();
        SvgRenderOptions options = new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, fonts);
        byte[] input = Document(false), control = Document(true), unchanged = input.ToArray();
        RenderBudget budget = new(4_000_000);
        using (RenderBudget.BudgetReservation borrowed = budget.Reserve(73, SvgMemoryCategory.Scene))
        {
            byte[] expected;
            using (SvgRoot root = SvgRootParser.Parse(control, SvgRenderLimits.Default, budget, fonts, CancellationToken.None))
            using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, options, budget, CancellationToken.None))
            {
                expected = surface.Pixels.ToArray();
                Assert.Contains(expected, value => value != 0);
            }
            using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, fonts, CancellationToken.None))
            using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, options, budget, CancellationToken.None))
                Assert.Equal(expected, surface.Pixels);
            Assert.Equal(73, budget.Snapshot().CurrentBytes);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        Assert.Equal(SvgConverter.ConvertToPng(control, options, CancellationToken.None).ToArray(), SvgConverter.ConvertToPng(input, options, CancellationToken.None).ToArray());
        (var catalog, var provider) = CreateMaskStylePack();
        using SvgPreparedDocument prepared = await SvgPreparedDocument.CreateAsync(input, catalog, provider, SvgRenderOptions.Default, CancellationToken.None);
        Assert.Equal(1, provider.RequestCount);
        Assert.Equal(SvgConverter.ConvertToPng(control, options, CancellationToken.None).ToArray(), prepared.ConvertToPng(CancellationToken.None).ToArray());
        Assert.Equal(unchanged, input);

        byte[] Document(bool explicitText)
        {
            string size = normalized ? "5" : "200";
            string letter = normalized ? ".0078125" : ".25", word = normalized ? ".015625" : ".5";
            string style = "font-family:Example;font-size:" + size + "px;font-weight:700;font-style:italic;white-space:pre;letter-spacing:" + letter + "px;word-spacing:" + word + "px";
            string own = explicitText ? " style='" + style + "' text-anchor='middle'" : "";
            string coordinate = normalized ? ".5" : "16", baseline = normalized ? ".5" : "14";
            string text = "<text x='" + coordinate + "' y='" + baseline + "' fill='white'" + own + ">A  A</text>";
            string groupStyle = location == "group" ? " font-family='Example' font-size='" + size + "' font-weight='700' font-style='italic' letter-spacing='" + letter + "' word-spacing='" + word + "' text-anchor='middle' xml:space='preserve'" : "";
            string maskStyle = location == "mask" ? " style='" + style + "'" : "";
            string mask = "<mask id='m' maskUnits='userSpaceOnUse' maskContentUnits='" + (normalized ? "objectBoundingBox" : "userSpaceOnUse") + "' mask-type='" + type + "' x='0' y='0' width='32' height='32'" + maskStyle + "><g" + groupStyle + ">" + text + "</g></mask>";
            string ancestor = "<g display='none'" + (location == "ancestor" ? " style='" + style + "'" : "") + "><defs>" + mask + "</defs></g>";
            string shape = "<rect width='32' height='32' fill='red' mask='url(#m)'/>";
            string body = consumer switch
            {
                "symbol" => "<use href='#s' mask='url(#m)'/><defs><symbol id='s'><rect width='32' height='32' fill='red'/></symbol></defs>",
                "pattern" => "<defs><pattern id='p' patternUnits='userSpaceOnUse' width='32' height='32'>" + shape + "</pattern></defs><rect width='32' height='32' fill='url(#p)'/>",
                _ => shape,
            };
            body = "<g font-family='Unavailable' font-size='16' font-weight='400' font-style='normal' transform='" + map + "'>" + body + "</g>";
            string rootStyle = location == "root" ? " style='" + style + "'" : "";
            return System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32' text-anchor='middle'" + rootStyle + ">" + (consumer == "symbol" ? body + ancestor : ancestor + body) + "</svg>");
        }
    }
    [Theory]
    [InlineData("root")]
    [InlineData("ancestor")]
    [InlineData("mask")]
    [InlineData("group")]
    public async Task Mask_text_child_declarations_override_the_complete_inherited_style(string location)
    {
        const string inherited = "font-family:Unavailable;font-size:10px;font-weight:400;font-style:normal;white-space:normal;letter-spacing:10px;word-spacing:10px";
        const string child = "<text x='2' y='14' fill='white' font-family='Example' font-size='200' font-weight='700' font-style='italic' xml:space='preserve' letter-spacing='.25' word-spacing='.5' text-anchor='start'>A  A</text>";
        using SvgFontSnapshot fonts = await CreateMaskStyleFont();
        SvgRenderOptions options = new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, fonts);
        byte[] expected = SvgConverter.ConvertToPng(Document(false), options, CancellationToken.None).ToArray();
        Assert.Equal(expected, SvgConverter.ConvertToPng(Document(true), options, CancellationToken.None).ToArray());
        (var catalog, var provider) = CreateMaskStylePack();
        using SvgPreparedDocument prepared = await SvgPreparedDocument.CreateAsync(Document(true), catalog, provider, SvgRenderOptions.Default, CancellationToken.None);
        Assert.Equal(1, provider.RequestCount);
        Assert.Equal(expected, prepared.ConvertToPng(CancellationToken.None).ToArray());

        byte[] Document(bool inheritedStyle)
        {
            string style = inheritedStyle ? " style='" + inherited + "'" : "";
            string group = inheritedStyle && location == "group" ? " font-family='Unavailable' font-size='10' font-weight='400' font-style='normal' letter-spacing='10' word-spacing='10' text-anchor='end'" : "";
            string mask = "<mask id='m' maskUnits='userSpaceOnUse' x='0' y='0' width='32' height='32'" + (location == "mask" ? style : "") + "><g" + group + ">" + child + "</g></mask>";
            return System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'" + (location == "root" ? style : "") + "><g" + (location == "ancestor" ? style : "") + "><defs>" + mask + "</defs></g><rect width='32' height='32' fill='red' mask='url(#m)'/></svg>");
        }
    }

    private static ValueTask<SvgFontSnapshot> CreateMaskStyleFont()
    {
        (var catalog, var provider) = CreateMaskStylePack();
        return SvgFontSnapshot.CreateAsync(catalog, new[] { new SvgFontFaceRequest("Example", 700, true) }, provider, CancellationToken.None);
    }

    private static (SvgFontPackCatalog Catalog, CountingFontProvider Provider) CreateMaskStylePack()
    {
        byte[] font = CreateTriangleAAndSpaceFont();
        OpenTypeFontMetrics metrics = OpenTypeFont.ParseMetrics(font, CancellationToken.None);
        for (int index = 0; index < metrics.NumberOfHMetrics; index++) WriteUInt16(font, metrics.Hmtx.Offset + index * 4, 40);
        string hash = Convert.ToHexString(SHA256.HashData(font));
        string manifest = "{\"packId\":\"mask-style\",\"files\":[{\"relativePath\":\"face.ttf\",\"byteSize\":" + font.Length + ",\"sha256\":\"" + hash + "\"}],\"families\":[{\"requestedFamily\":\"Example\",\"resolvedFamily\":\"Example\",\"relativeFontFile\":\"face.ttf\",\"weight\":700,\"italic\":true}]}";
        SvgFontPackCatalog catalog = SvgFontPackCatalog.Parse(System.Text.Encoding.UTF8.GetBytes(manifest), "mask-style");
        return (catalog, new CountingFontProvider(new CountingFontSource(font)));
    }

}
