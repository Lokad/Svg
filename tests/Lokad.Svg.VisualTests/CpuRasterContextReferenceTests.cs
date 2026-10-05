using System.Security.Cryptography;
using System.Text.Json;
using Lokad.Svg.VisualDiff;

namespace Lokad.Svg.VisualTests;

public sealed class CpuRasterContextReferenceTests
{
    [Fact]
    public void Every_declared_context_control_retains_healthy_triples_and_its_complete_candidate_identity()
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "tests", "References");
        using JsonDocument declaration = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(directory, "cpu-raster-context-declaration.json")));
        JsonElement cases = declaration.RootElement.GetProperty("Cases");
        Assert.Equal(16, cases.GetArrayLength());
        Assert.Equal(15, cases.EnumerateArray().Count(value => value.GetProperty("DeclaredBeforeImplementation").GetBoolean()));
        foreach (JsonElement value in cases.EnumerateArray())
        {
            string id = value.GetProperty("Id").GetString() ?? throw new InvalidDataException("Missing context control ID.");
            using JsonDocument metadata = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(directory, "original-" + id + ".json")));
            JsonElement record = metadata.RootElement;
            byte[] input = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "original", id + ".svg"));
            Assert.Equal(value.GetProperty("InputSha256").GetString(), Hash(input));
            Assert.Equal(record.GetProperty("inputSha256").GetString(), Hash(input));
            string filename = record.GetProperty("referencePng").GetString() ?? throw new InvalidDataException("Missing reference PNG.");
            byte[] reference = File.ReadAllBytes(Path.Combine(directory, filename));
            Assert.Equal(record.GetProperty("referenceSha256").GetString(), Hash(reference));
            Assert.Equal(3, record.GetProperty("captureSha256").GetArrayLength());
            Assert.All(record.GetProperty("captureSha256").EnumerateArray(), capture => Assert.Equal(Hash(reference), capture.GetString()));
            Assert.All(record.GetProperty("captureStartupHealth").EnumerateArray(), health => Assert.Equal("healthy", health.GetString()));
            Assert.All(record.GetProperty("captureProfilesCleaned").EnumerateArray(), cleaned => Assert.True(cleaned.GetBoolean()));
            int width = value.GetProperty("Width").GetInt32();
            int height = value.GetProperty("Height").GetInt32();
            RgbaImage image = PngRgbaDecoder.Decode(reference, width * height);
            Assert.Equal((width, height), (image.Width, image.Height));
            SvgRenderOptions options = new(new SvgViewport(width, height), 1, SvgColor.Transparent, SvgRenderLimits.Default);
            if (record.GetProperty("candidateOutcome").GetString() == "renders")
                Assert.Equal(record.GetProperty("candidateSha256").GetString(), Hash(SvgConverter.ConvertToPng(input, options, CancellationToken.None).Span));
            else
            {
                SvgException exception = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(input, options, CancellationToken.None));
                Assert.Equal(record.GetProperty("candidateOutcome").GetString(), exception.ErrorCode.ToString());
            }
        }

        static string Hash(ReadOnlySpan<byte> bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    }

    [Fact]
    public void Translucent_border_band_is_painted_once_in_every_owned_interior()
    {
        const string id = "cpu-raster-border-band";
        byte[] input = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "original", id + ".svg"));
        RgbaImage reference = PngRgbaDecoder.Decode(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "tests", "References", "original-" + id + ".png")), 640 * 320);
        RgbaImage rendered = PngRgbaDecoder.Decode(SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None), 640 * 320);
        for (int y = 12; y < 308; y++)
            for (int x = 240; x < 274; x++)
            {
                int alpha = (y * 640 + x) * 4 + 3;
                Assert.Equal((byte)140, reference.Pixels[alpha]);
                Assert.Equal(reference.Pixels[alpha], rendered.Pixels[alpha]);
            }
    }
}
