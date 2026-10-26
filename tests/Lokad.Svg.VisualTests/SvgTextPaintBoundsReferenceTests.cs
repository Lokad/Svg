using System.Security.Cryptography;
using System.Text.Json;
using Lokad.Svg.Fonts;
using Lokad.Svg.VisualDiff;

namespace Lokad.Svg.VisualTests;

public sealed class SvgTextPaintBoundsReferenceTests
{
    public static IEnumerable<object[]> Cases()
    {
        using JsonDocument data = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(Folder(), "image-capture.json")));
        foreach (JsonElement row in data.RootElement.GetProperty("cases").EnumerateArray())
            yield return [row.GetProperty("id").GetString() ?? throw new InvalidDataException("Missing case id.")];
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public Task Text_paint_boxes_match_native_fill_stroke_and_user_space_controls(string id)
        => AssertNativeImage("image-capture.json", id, true);

    public static IEnumerable<object[]> SharedLayoutCases()
    {
        using JsonDocument data = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(Folder(), "shared-layout-images.json")));
        foreach (JsonElement row in data.RootElement.GetProperty("cases").EnumerateArray())
            yield return [row.GetProperty("id").GetString() ?? throw new InvalidDataException("Missing shared case id.")];
    }

    [Theory]
    [MemberData(nameof(SharedLayoutCases))]
    [Trait("Layer", "Rendering")]
    public Task Shared_layout_matches_each_native_source_image(string id)
        => AssertNativeImage("shared-layout-images.json", id, false);

    private static async Task AssertNativeImage(string captureFile, string id, bool replayPrepared)
    {
        using JsonDocument data = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(Folder(), captureFile)));
        JsonElement row = data.RootElement.GetProperty("cases").EnumerateArray().Single(item => item.GetProperty("id").GetString() == id);
        JsonElement image = row.GetProperty("references").GetProperty(OperatingSystem.IsWindows() ? "windows" : "linux");
        byte[] input = File.ReadAllBytes(Path.Combine(Folder(), row.GetProperty("input").GetString() ?? throw new InvalidDataException()));
        byte[] reference = File.ReadAllBytes(Path.Combine(Folder(), image.GetProperty("path").GetString() ?? throw new InvalidDataException()));
        Assert.Equal(row.GetProperty("sourceSha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(input)));
        Assert.Equal(image.GetProperty("sha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(reference)));
        SvgFontPackCatalog catalog = SvgFontPackCatalog.Parse(File.ReadAllBytes(Path.Combine(Folder(), "font-pack.json")), "r99-fonts");
        FontPrograms provider = new();
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync(input, catalog, provider, CancellationToken.None);
        SvgRenderOptions options = new(null, row.GetProperty("outputScale").GetDouble(), SvgColor.Transparent, SvgRenderLimits.Default, snapshot);
        byte[] actual = SvgConverter.ConvertToPng(input, options, CancellationToken.None).ToArray();
        Assert.True(PixelComparer.Compare(PngRgbaDecoder.Decode(reference, 1_048_576), PngRgbaDecoder.Decode(actual, 1_048_576)).IsExact, id);
        if (!replayPrepared) return;
        using SvgPreparedDocument document = await SvgPreparedDocument.CreateAsync(input, catalog, provider,
            new(null, options.OutputScale, SvgColor.Transparent, SvgRenderLimits.Default), CancellationToken.None);
        Assert.Equal(actual, document.ConvertToPng(CancellationToken.None).ToArray());
    }

    private static string Folder() => Path.Combine(AppContext.BaseDirectory, "Research", "text-paint-bounds");

    private sealed class FontPrograms : ISvgFontProgramSourceProvider
    {
        public ValueTask<ISvgFontProgramSource?> GetSourceAsync(SvgFontPackFile file, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string name = file.RelativePath == "sans.ttf" ? "supplied-font-labels" : "supplied-font-documents";
            string font = file.RelativePath == "sans.ttf" ? "NotoSans-Regular.ttf" : "NotoSansMono-Bold.ttf";
            return ValueTask.FromResult<ISvgFontProgramSource?>(new SvgMemoryFontProgramSource(file.RelativePath,
                File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Research", name, "fonts", font))));
        }
    }
}
