using System.Security.Cryptography;
using System.Text.Json;
using Lokad.Svg.Fonts;
using Lokad.Svg.VisualDiff;
using TextEncoding = System.Text.Encoding;

namespace Lokad.Svg.VisualTests;

public sealed class SvgSingularSourceReferenceTests
{
    private static readonly PaintReferenceBank Bank = new("singular-source-maps");

    public static IEnumerable<object[]> Cases() => Bank.CaseIds.Select(id => new object[] { id });

    [Theory]
    [MemberData(nameof(Cases))]
    [Trait("Layer", "Rendering")]
    public async Task Singular_source_maps_preserve_the_owning_effect_scope(string id)
    {
        JsonElement row = Bank.GetCase(id);
        byte[] input = TextEncoding.UTF8.GetBytes(Required(row, "source"));
        Assert.Equal(Required(row, "sourceSha256"), Convert.ToHexStringLower(SHA256.HashData(input)));
        using SvgFontSnapshot? fonts = row.TryGetProperty("fontContext", out JsonElement fontContext) && fontContext.GetBoolean()
            ? await SvgFontSnapshot.CreateAsync(input, NotoPaintPrograms.Catalog, new NotoPaintPrograms(), CancellationToken.None) : null;
        SvgRenderOptions options = new(null, row.GetProperty("outputScale").GetDouble(), SvgColor.Transparent, SvgRenderLimits.Default, fonts);
        using SvgPngResult png = SvgConverter.ConvertToOwnedPng(input, options, CancellationToken.None);
        JsonElement reference = row.TryGetProperty("nativeByHost", out JsonElement profiles)
            ? profiles.GetProperty(OperatingSystem.IsWindows() ? "windows" : "linux") : row.GetProperty("native");
        byte[] native = File.ReadAllBytes(Path.Combine(ResearchCorpus.Root, Bank.Name, Required(reference, "path")));
        Assert.Equal(Required(reference, "pngSha256"), Convert.ToHexStringLower(SHA256.HashData(native)));
        int pixels = checked(reference.GetProperty("width").GetInt32() * reference.GetProperty("height").GetInt32());
        Assert.True(PixelComparer.Compare(PngRgbaDecoder.Decode(native, pixels), PngRgbaDecoder.Decode(png.Memory, pixels)).IsExact, id);
    }

    private static string Required(JsonElement row, string property)
        => row.GetProperty(property).GetString() ?? throw new InvalidDataException("Missing " + property + ".");
}
