using System.Security.Cryptography;
using System.Text.Json;
using Lokad.Svg.Fonts;
using Lokad.Svg.VisualDiff;

namespace Lokad.Svg.VisualTests;

public sealed class SvgPatternRecordingReferenceTests
{
    [Theory]
    [InlineData("unequal-opacity")]
    [InlineData("plain-cancel")]
    [InlineData("translated-clip")]
    [InlineData("nested-clip-thin")]
    [InlineData("nested-clip-fill")]
    [InlineData("outer-mask-control")]
    [InlineData("direct-fill-clip")]
    [InlineData("direct-thin-mask")]
    [InlineData("use-leaf-thin-clip")]
    [InlineData("use-group-fill-mask")]
    [InlineData("symbol-thin-opacity")]
    [InlineData("filter-fill-clip")]
    [InlineData("viewport-thin-opacity")]
    [InlineData("text-fill-clip")]
    [InlineData("text-thin-mask")]
    [InlineData("text-instance-fill-clip")]
    [InlineData("text-instance-thin-mask")]
    [InlineData("affine-translate")]
    [InlineData("affine-skew")]
    [InlineData("affine-rotate")]
    [InlineData("affine-reflect")]
    public async Task Pattern_recording_matches_each_authored_and_externally_scaled_browser_image(string id)
    {
        string folder = Path.Combine(ResearchCorpus.Root, "pattern-recording");
        using JsonDocument evidence = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(folder, "capture.json")));
        JsonElement program = evidence.RootElement.GetProperty("fontProgram");
        SvgFontPackCatalog catalog = SvgFontPackCatalog.Parse(File.ReadAllBytes(Path.Combine(folder, Text(program, "catalog"))), Text(program, "packId"));
        foreach (string kind in new[] { "candidate", "control" })
        {
            JsonElement item = evidence.RootElement.GetProperty("cases").EnumerateArray().Single(row => Text(row, "id") == id + "-" + kind);
            byte[] authored = File.ReadAllBytes(Path.Combine(folder, Text(item, "input")));
            Assert.Equal(Text(item, "sourceSha256"), Convert.ToHexStringLower(SHA256.HashData(authored)));
            bool font = item.GetProperty("font").GetBoolean();
            byte[] fontBytes = font ? File.ReadAllBytes(Path.Combine(folder, Text(program, "path"))) : [];
            string browser = System.Text.Encoding.UTF8.GetString(authored);
            if (font)
            {
                Assert.Equal(Text(program, "sha256"), Convert.ToHexStringLower(SHA256.HashData(fontBytes)));
                browser = browser.Replace("font-family='serif'", "font-family='ReviewFont'", StringComparison.Ordinal);
                browser = browser.Insert(browser.IndexOf('>') + 1,
                    "<style>@font-face{font-family:ReviewFont;src:url(data:font/ttf;base64," + Convert.ToBase64String(fontBytes) + ")}</style>");
            }
            Assert.Equal(Text(item, "browserSourceSha256"), Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(browser))));
            Provider provider = new(font, fontBytes);
            JsonElement host = OperatingSystem.IsLinux() && item.TryGetProperty("linux64", out JsonElement linux) ? linux : item;
            foreach (bool external in new[] { false, true })
            {
                JsonElement expected = external ? host.GetProperty("externalScale") : host;
                byte[] input = authored;
                if (external)
                {
                    string sized = "width='" + item.GetProperty("width").GetInt32() + "' height='" + item.GetProperty("height").GetInt32() + "' viewBox='0 0 32 32'>";
                    string original = System.Text.Encoding.UTF8.GetString(authored).Replace(sized, "width='32' height='32'>", StringComparison.Ordinal);
                    input = System.Text.Encoding.UTF8.GetBytes(original);
                    Assert.Equal(Text(expected, "sourceSha256"), Convert.ToHexStringLower(SHA256.HashData(input)));
                }
                double scale = external ? expected.GetProperty("outputScale").GetDouble() : 1;
                byte[] reference = File.ReadAllBytes(Path.Combine(folder, Text(expected, "reference")));
                Assert.Equal(Text(expected, "referenceSha256"), Convert.ToHexStringLower(SHA256.HashData(reference)));
                using SvgFontSnapshot? fonts = font ? await SvgFontSnapshot.CreateAsync(input, catalog, provider, CancellationToken.None) : null;
                SvgRenderOptions options = new(null, scale, SvgColor.Transparent, SvgRenderLimits.Default, fonts);
                byte[] raw = SvgConverter.ConvertToPng(input, options, CancellationToken.None).ToArray();
                Assert.True(PixelComparer.Compare(PngRgbaDecoder.Decode(reference, 1_048_576), PngRgbaDecoder.Decode(raw, 1_048_576)).IsExact,
                    id + " " + kind + (external ? " external scale" : " authored viewport"));
                using SvgPreparedDocument prepared = await SvgPreparedDocument.CreateAsync(input, catalog, provider,
                    new SvgRenderOptions(null, scale, SvgColor.Transparent, SvgRenderLimits.Default), CancellationToken.None);
                Assert.Equal(raw, prepared.ConvertToPng(CancellationToken.None).ToArray());
            }
        }
    }

    private static string Text(JsonElement value, string name)
        => value.GetProperty(name).GetString() ?? throw new InvalidDataException("Missing " + name + ".");

    private sealed class Provider(bool allowFonts, byte[] bytes) : ISvgFontProgramSourceProvider
    {
        public ValueTask<ISvgFontProgramSource?> GetSourceAsync(SvgFontPackFile file, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!allowFonts) throw new InvalidOperationException("Font-free pattern sources must not request a font program.");
            return ValueTask.FromResult<ISvgFontProgramSource?>(new SvgMemoryFontProgramSource(file.RelativePath, bytes));
        }
    }
}
