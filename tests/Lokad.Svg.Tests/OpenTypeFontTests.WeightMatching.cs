using System.Security.Cryptography;
using System.Text.Json;
using Lokad.Svg.Fonts;

namespace Lokad.Svg.Tests;

public sealed partial class OpenTypeFontTests
{
    [Theory]
    [InlineData(300, 100, 400, 100)]
    [InlineData(700, 600, 900, 900)]
    [InlineData(400, 300, 500, 500)]
    public async Task CSS_weight_fallback_selects_the_same_face_for_snapshots_and_prepared_output(int requested, int lower, int upper, int expectedWeight)
    {
        byte[] first = CreateTriangleFont(), second = CreateTriangleFont();
        WriteUInt16(second, FindTableOffset(second, 0x686D7478) + 4, 450);
        var programs = new Dictionary<string, byte[]> { ["lower.ttf"] = first, ["upper.ttf"] = second };
        string expectedFile = expectedWeight == lower ? "lower.ttf" : "upper.ttf";
        byte[] manifest = JsonSerializer.SerializeToUtf8Bytes(new
        {
            packId = "weights",
            files = programs.Select(pair => new { relativePath = pair.Key, byteSize = pair.Value.Length, sha256 = Convert.ToHexString(SHA256.HashData(pair.Value)) }),
            families = new[]
            {
                new { requestedFamily = "Example", resolvedFamily = "Example", relativeFontFile = "lower.ttf", weight = lower },
                new { requestedFamily = "Example", resolvedFamily = "Example", relativeFontFile = "upper.ttf", weight = upper },
                new { requestedFamily = "Expected", resolvedFamily = "Example", relativeFontFile = expectedFile, weight = expectedWeight }
            }
        });
        SvgFontPackCatalog catalog = SvgFontPackCatalog.Parse(manifest, "weights");
        StackFontProvider provider = new(programs);
        using SvgFontSnapshot requestSnapshot = await SvgFontSnapshot.CreateAsync(catalog,
            [SvgFontFaceRequest.FromCss("Example", requested, false)], provider, CancellationToken.None);
        Assert.Equal(expectedWeight, Assert.Single(requestSnapshot.Fonts).Resolution.Resolution.Face.Weight);
        Assert.Equal(new[] { expectedFile }, provider.Requests);

        byte[] svg = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='240' height='48'><text font-family='Example' font-weight='"
            + requested + "' font-size='100' y='30'>AA</text></svg>");
        provider.Requests.Clear();
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync(svg, catalog, provider, CancellationToken.None);
        Assert.Equal(expectedWeight, Assert.Single(snapshot.Fonts).Resolution.Resolution.Face.Weight);
        Assert.Equal(new[] { expectedFile }, provider.Requests);
        byte[] actual = SvgConverter.ConvertToPng(svg, new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, snapshot), CancellationToken.None).ToArray();

        provider.Requests.Clear();
        using SvgPreparedDocument prepared = await SvgPreparedDocument.CreateAsync(svg, catalog, provider, SvgRenderOptions.Default, CancellationToken.None);
        Assert.Equal(new[] { expectedFile }, provider.Requests);
        Assert.Equal(actual, prepared.ConvertToPng(CancellationToken.None).ToArray());
        byte[] control = System.Text.Encoding.UTF8.GetBytes(System.Text.Encoding.UTF8.GetString(svg).Replace("font-family='Example'", "font-family='Expected'", StringComparison.Ordinal));
        using SvgPreparedDocument expected = await SvgPreparedDocument.CreateAsync(control, catalog, provider, SvgRenderOptions.Default, CancellationToken.None);
        Assert.Equal(expected.ConvertToPng(CancellationToken.None).ToArray(), actual);
    }
}
