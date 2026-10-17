using System.Security.Cryptography;
using System.Text.Json;
using Lokad.Svg.Fonts;
using Lokad.Svg.VisualDiff;

namespace Lokad.Svg.VisualTests;

public sealed class SvgBoundedRasterTests
{
    private static string DirectoryPath => Path.Combine(AppContext.BaseDirectory, "Research", "bounded-raster-sizing");
    private static string FontDirectory => Path.Combine(AppContext.BaseDirectory, "Research", "supplied-font-labels");

    [Theory]
    [InlineData("layout-600", "layout.svg", 600, 300)]
    [InlineData("layout-800", "layout.svg", 800, 400)]
    [InlineData("thin-overflow", "thin-overflow.svg", 8, 1)]
    public async Task Fitted_output_matches_frozen_native_pixels_and_prepared_conversion(string id, string source, int width, int height)
    {
        byte[] input = File.ReadAllBytes(Path.Combine(DirectoryPath, source));
        SvgFontPackCatalog catalog = SvgFontPackCatalog.Parse(File.ReadAllBytes(Path.Combine(FontDirectory, "font-pack.json")), "public-noto-sans");
        FontProvider provider = new(File.ReadAllBytes(Path.Combine(FontDirectory, "fonts", "NotoSans-Regular.ttf")));
        SvgRasterBounds bounds = new(width, height, (long)width * height);
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync(input, catalog, provider, CancellationToken.None);
        SvgRenderOptions options = new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, snapshot, bounds);
        byte[] png = SvgConverter.ConvertToPng(input, options, CancellationToken.None).ToArray();
        RgbaImage actual = PngRgbaDecoder.Decode(png, checked(width * height));
        Assert.Equal(width, actual.Width); Assert.Equal(height, actual.Height);
        using SvgPreparedDocument prepared = await SvgPreparedDocument.CreateAsync(input, catalog, provider,
            new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, null, bounds), CancellationToken.None);
        Assert.Equal(png, prepared.ConvertToPng(CancellationToken.None).ToArray());
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(DirectoryPath, "manifest.json")));
        JsonElement item = manifest.RootElement.GetProperty("cases").EnumerateArray().Single(item => item.GetProperty("id").GetString() == id);
        Assert.Equal(item.GetProperty("inputSha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(input)));
        if (OperatingSystem.IsWindows() || OperatingSystem.IsLinux())
        {
            JsonElement platform = item.GetProperty("platforms").GetProperty(OperatingSystem.IsWindows() ? "windows" : "linux");
            byte[] reference = File.ReadAllBytes(Path.Combine(DirectoryPath, platform.GetProperty("reference").GetString() ?? throw new InvalidDataException()));
            Assert.True(PixelComparer.Compare(PngRgbaDecoder.Decode(reference, checked(width * height)), actual).IsExact);
            Assert.Equal(3, platform.GetProperty("captures").GetArrayLength());
            foreach (JsonElement capture in platform.GetProperty("captures").EnumerateArray())
            {
                Assert.Equal(capture.GetProperty("pngSha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(reference)));
                Assert.Equal("healthy", capture.GetProperty("startupHealth").GetString());
                Assert.True(capture.GetProperty("profileCleaned").GetBoolean());
            }
        }
    }

    /// <summary>Returns borrowed immutable OFL test bytes without I/O; observes cancellation and permits concurrent requests.</summary>
    private sealed class FontProvider(byte[] bytes) : ISvgFontProgramSourceProvider
    {
        public ValueTask<ISvgFontProgramSource?> GetSourceAsync(SvgFontPackFile file, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            return ValueTask.FromResult<ISvgFontProgramSource?>(new SvgMemoryFontProgramSource("public-noto-sans", bytes));
        }
    }
}
