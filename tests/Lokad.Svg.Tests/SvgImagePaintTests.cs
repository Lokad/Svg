using Lokad.Svg.Encoding;
using Lokad.Svg.Fonts;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed partial class OpenTypeFontTests
{
    public static IEnumerable<object[]> ImagePaintCases()
    {
        foreach (bool varied in new[] { false, true })
        foreach (bool affine in new[] { false, true })
        foreach (string site in new[] { "root", "group", "inline", "stylesheet", "pattern" })
        foreach (string paint in new[] { "fill:none", "stroke:red" })
            yield return [varied, affine, site, paint];
    }

    [Theory]
    [MemberData(nameof(ImagePaintCases))]
    public async Task Images_ignore_shape_paint_through_raw_and_prepared_outputs(bool varied, bool affine, string site, string paint)
    {
        byte[] control = ImagePaintDocument(varied, affine, site, "", "");
        byte[] expected = SvgConverter.ConvertToPng(control, SvgRenderOptions.Default, CancellationToken.None).ToArray();
        await AssertImagePaintOutputs(ImagePaintDocument(varied, affine, site, paint, ""), expected);
    }

    public static IEnumerable<object[]> ImagePaintEffectCases()
    {
        foreach (bool varied in new[] { false, true })
        foreach (string effect in new[] { "display='none'", "visibility='hidden'", "opacity='.5'", "opacity='0'",
            "clip-path='url(#c)' mask='url(#m)' filter='url(#f)' opacity='.625'" })
            yield return [varied, effect];
    }

    [Theory]
    [MemberData(nameof(ImagePaintEffectCases))]
    public async Task Image_presentation_keeps_visibility_opacity_and_composed_effects(bool varied, string effect)
    {
        byte[] control = ImagePaintDocument(varied, true, "inline", "", effect);
        byte[] expected = SvgConverter.ConvertToPng(control, SvgRenderOptions.Default, CancellationToken.None).ToArray();
        byte[] input = ImagePaintDocument(varied, true, "inline", "fill:none;fill-opacity:0;stroke:red;stroke-width:0", effect);
        await AssertImagePaintOutputs(input, expected);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Image_presentation_does_not_bind_irrelevant_paint_servers(bool varied)
    {
        byte[] control = ImagePaintDocument(varied, true, "inline", "", "");
        byte[] expected = SvgConverter.ConvertToPng(control, SvgRenderOptions.Default, CancellationToken.None).ToArray();
        byte[] input = ImagePaintDocument(varied, true, "inline", "fill:url(#unused);stroke:url(#unused)", "");
        await AssertImagePaintOutputs(input, expected);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Image_presentation_preserves_scene_owners_on_denial_cancellation_and_retry(bool varied, bool affine)
    {
        byte[] input = ImagePaintDocument(varied, affine, "group", "fill:none;stroke:red", "");
        byte[] original = input.ToArray();
        RenderBudget sceneBudget = new RenderBudget(new HostMemoryBudget(1_000_000));
        using (SvgRoot scene = SvgRootParser.Parse(input, SvgRenderLimits.Default, sceneBudget, CancellationToken.None))
        {
            long retained = sceneBudget.Snapshot().CurrentBytes;
            RenderBudget conversionBudget = new RenderBudget(new HostMemoryBudget(1_000_000));
            byte[] expected = SvgConverter.ConvertToPng(scene, SvgRenderOptions.Default, conversionBudget, CancellationToken.None).ToArray();
            Assert.Equal(0, conversionBudget.Snapshot().CurrentBytes);
            RenderBudget denied = new RenderBudget(new HostMemoryBudget(1));
            Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() =>
                SvgConverter.ConvertToPng(scene, SvgRenderOptions.Default, denied, CancellationToken.None)).ErrorCode);
            Assert.Equal(0, denied.Snapshot().CurrentBytes);
            using CancellationTokenSource cancellation = new();
            cancellation.Cancel();
            Assert.Equal(cancellation.Token, Assert.ThrowsAny<OperationCanceledException>(() =>
                SvgConverter.ConvertToPng(scene, SvgRenderOptions.Default, conversionBudget, cancellation.Token)).CancellationToken);
            Assert.Equal(0, conversionBudget.Snapshot().CurrentBytes);
            Assert.Equal(expected, SvgConverter.ConvertToPng(scene, SvgRenderOptions.Default, conversionBudget, CancellationToken.None).ToArray());
            Assert.Equal(0, conversionBudget.Snapshot().CurrentBytes);
            Assert.Equal(retained, sceneBudget.Snapshot().CurrentBytes);
        }
        Assert.Equal(0, sceneBudget.Snapshot().CurrentBytes);
        Assert.Equal(original, input);
    }

    [Theory]
    [InlineData("fill")]
    [InlineData("stroke")]
    public void Image_presentation_keeps_direct_paint_attribute_admission(string attribute)
    {
        string document = System.Text.Encoding.UTF8.GetString(ImagePaintDocument(false, false, "inline", "", ""));
        byte[] input = System.Text.Encoding.UTF8.GetBytes(document.Replace("<image ", "<image " + attribute + "='none' ", StringComparison.Ordinal));
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(1_000_000));
        Assert.Equal(SvgErrorCode.UnsupportedFeature, Assert.Throws<SvgException>(() =>
        {
            using SvgRoot scene = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None);
        }).ErrorCode);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    private static byte[] ImagePaintDocument(bool varied, bool affine, string site, string paint, string effect)
    {
        byte[] pixels = varied ? [255, 0, 0, 255, 0, 0, 255, 128] : [255, 0, 0, 255];
        string png = Convert.ToBase64String(PngEncoder.Encode(varied ? 2 : 1, 1, pixels, CancellationToken.None));
        string image = "<image x='2.25' y='2.5' width='8.5' height='7.25' preserveAspectRatio='none' href='data:image/png;base64," + png + "'"
            + (affine ? " transform='matrix(1 .125 .25 1 0 0)'" : "") + (site == "inline" ? " style='" + paint + "'" : "") + "/>";
        string body = site switch
        {
            "group" => "<g style='" + paint + "'>" + image + "</g>",
            "stylesheet" => "<style>image {" + paint + "}</style>" + image,
            "pattern" => "<defs><pattern id='tile' patternUnits='userSpaceOnUse' width='16' height='16'><g style='" + paint + "'>"
                + image + "</g></pattern></defs><rect width='16' height='16' fill='url(#tile)'/>",
            "root" or "inline" => image,
            _ => throw new ArgumentOutOfRangeException(nameof(site))
        };
        string definitions = effect.Contains("url(", StringComparison.Ordinal)
            ? "<defs><clipPath id='c'><rect x='3' y='1' width='6' height='10'/></clipPath>"
                + "<mask id='m' maskUnits='userSpaceOnUse' x='0' y='0' width='16' height='16'><rect width='16' height='16' fill='white'/></mask>"
                + "<filter id='f' filterUnits='userSpaceOnUse' x='0' y='0' width='16' height='16'><feGaussianBlur stdDeviation='.5'/></filter></defs>" : "";
        return System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'"
            + (site == "root" ? " style='" + paint + "'" : "") + ">" + definitions + "<g " + effect + ">" + body + "</g></svg>");
    }

    private enum ImagePaintOutput { Owned, OwnedAsync, Memory, MemoryAsync, Stream, StreamAsync }

    private static async Task AssertImagePaintOutputs(byte[] input, byte[] expected)
    {
        (SvgFontPackCatalog catalog, CountingFontProvider provider, CountingFontSource source) = CreateExamplePack(CreateTriangleFont());
        Assert.Empty(SvgFontRequestDiscovery.Discover(input, CancellationToken.None));
        foreach (bool prepared in new[] { false, true })
        foreach (ImagePaintOutput form in Enum.GetValues<ImagePaintOutput>())
        {
            using SvgPreparedDocument? document = prepared
                ? await SvgPreparedDocument.CreateAsync(input, catalog, provider, SvgRenderOptions.Default, CancellationToken.None) : null;
            byte[] target = new byte[expected.Length];
            using MemoryStream stream = new();
            ReadOnlyMemory<byte> result;
            int written;
            switch (form)
            {
                case ImagePaintOutput.Owned:
                    result = document is null ? SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None) : document.ConvertToPng(CancellationToken.None);
                    break;
                case ImagePaintOutput.OwnedAsync:
                    result = document is null ? await SvgConverter.ConvertToPngAsync(input, SvgRenderOptions.Default, CancellationToken.None) : await document.ConvertToPngAsync(CancellationToken.None);
                    break;
                case ImagePaintOutput.Memory:
                    written = document is null ? SvgConverter.ConvertToPng(input, target, SvgRenderOptions.Default, CancellationToken.None) : document.ConvertToPng(target, CancellationToken.None);
                    Assert.Equal(expected.Length, written); result = target.AsMemory(0, written);
                    break;
                case ImagePaintOutput.MemoryAsync:
                    written = document is null ? await SvgConverter.ConvertToPngAsync(input, target, SvgRenderOptions.Default, CancellationToken.None) : await document.ConvertToPngAsync(target, CancellationToken.None);
                    Assert.Equal(expected.Length, written); result = target.AsMemory(0, written);
                    break;
                case ImagePaintOutput.Stream:
                    written = document is null ? SvgConverter.ConvertToPng(input, stream, SvgRenderOptions.Default, CancellationToken.None) : document.ConvertToPng(stream, CancellationToken.None);
                    Assert.Equal(expected.Length, written); result = stream.ToArray();
                    break;
                case ImagePaintOutput.StreamAsync:
                    written = document is null ? await SvgConverter.ConvertToPngAsync(input, stream, SvgRenderOptions.Default, CancellationToken.None) : await document.ConvertToPngAsync(stream, CancellationToken.None);
                    Assert.Equal(expected.Length, written); result = stream.ToArray();
                    break;
                default: throw new ArgumentOutOfRangeException(nameof(form));
            }
            Assert.Equal(expected, result.ToArray());
        }
        Assert.Equal(0, provider.RequestCount);
        Assert.Equal(0, source.RequestCount);
    }
}
