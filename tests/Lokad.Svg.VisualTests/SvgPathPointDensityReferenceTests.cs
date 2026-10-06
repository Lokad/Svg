using System.Security.Cryptography;
using System.Text.Json;
using Lokad.Svg.VisualDiff;

namespace Lokad.Svg.VisualTests;

public sealed class SvgPathPointDensityReferenceTests
{
    [Fact]
    public void Independent_density_controls_preserve_capture_identity_and_disposition()
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "tests", "References");
        using JsonDocument declaration = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(directory, "path-point-density-declaration.json")));
        JsonElement root = declaration.RootElement;
        Assert.True(root.GetProperty("DeclaredBeforeProductionEdit").GetBoolean());
        Assert.True(root.GetProperty("DeclaredBeforeGenericCountPrototype").GetBoolean());
        Assert.Equal(24, root.GetProperty("Cases").GetArrayLength());
        int exact = 0;
        int known = 0;
        int rejected = 0;
        foreach (JsonElement control in root.GetProperty("Cases").EnumerateArray())
        {
            string id = control.GetProperty("Id").GetString() ?? throw new InvalidDataException("Missing control ID.");
            string source = control.GetProperty("Source").GetString() ?? throw new InvalidDataException("Missing control source.");
            byte[] input = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "original", source));
            ReadOnlySpan<byte> prefix = "<!-- SPDX-License-Identifier: CC0-1.0 -->\n"u8;
            Assert.True(input.AsSpan().StartsWith(prefix));
            Assert.Equal(control.GetProperty("InputSha256").GetString(), Hash(input));
            using JsonDocument metadata = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(directory, "original-" + id + ".json")));
            JsonElement record = metadata.RootElement;
            Assert.Equal(control.GetProperty("InputSha256").GetString(), record.GetProperty("inputSha256").GetString());
            Assert.Equal(Hash(input), record.GetProperty("materializedSha256").GetString());
            Assert.True(record.GetProperty("declaredBeforeProductionEdit").GetBoolean());
            Assert.True(record.GetProperty("declaredBeforeGenericCountPrototype").GetBoolean());
            byte[] reference = File.ReadAllBytes(Path.Combine(directory, "original-" + id + ".png"));
            Assert.Equal(record.GetProperty("referenceSha256").GetString(), Hash(reference));
            Assert.Equal(3, record.GetProperty("freshProcessCaptures").GetInt32());
            Assert.Equal(3, record.GetProperty("captureSha256").GetArrayLength());
            Assert.All(record.GetProperty("captureSha256").EnumerateArray(), capture => Assert.Equal(Hash(reference), capture.GetString()));
            Assert.All(record.GetProperty("captureStartupHealth").EnumerateArray(), health => Assert.Equal("healthy", health.GetString()));
            Assert.All(record.GetProperty("captureProfilesCleaned").EnumerateArray(), cleaned => Assert.True(cleaned.GetBoolean()));
            SvgColor background = control.GetProperty("Background").GetString() switch { "transparent" => SvgColor.Transparent, "white" => new(255, 255, 255, 255), "black" => new(0, 0, 0, 255), _ => throw new InvalidDataException("Invalid control background.") };
            int width = control.GetProperty("Width").GetInt32();
            int height = control.GetProperty("Height").GetInt32();
            SvgRenderOptions options = new(new SvgViewport(width, height), 1, background, SvgRenderLimits.Default);
            RgbaImage oracle = PngRgbaDecoder.Decode(reference, width * height);
            Assert.Equal(width, oracle.Width);
            Assert.Equal(height, oracle.Height);
            if (record.GetProperty("candidateOutcome").GetString() != "renders")
            {
                SvgException exception = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(input, options, CancellationToken.None));
                Assert.Equal(SvgErrorCode.UnsupportedFeature, exception.ErrorCode);
                Assert.Equal(JsonValueKind.Null, record.GetProperty("comparison").ValueKind);
                rejected++;
                continue;
            }
            ReadOnlyMemory<byte> output = SvgConverter.ConvertToPng(input, options, CancellationToken.None);
            Assert.Equal(record.GetProperty("candidateSha256").GetString(), Hash(output.Span));
            PixelComparison comparison = PixelComparer.Compare(oracle, PngRgbaDecoder.Decode(output, width * height));
            JsonElement expected = record.GetProperty("comparison");
            Assert.Equal(expected.GetProperty("exact").GetBoolean(), comparison.IsExact);
            Assert.Equal(expected.GetProperty("changedPixelCount").GetInt64(), comparison.ChangedPixelCount);
            Assert.Equal(expected.GetProperty("maximumChannelError").GetByte(), comparison.MaximumChannelError);
            Assert.Equal(expected.GetProperty("maximumAlphaError").GetByte(), comparison.MaximumAlphaError);
            if (comparison.IsExact) exact++;
            else known++;
        }
        Assert.Equal(18, exact);
        Assert.Equal(6, known);
        Assert.Equal(0, rejected);

        static string Hash(ReadOnlySpan<byte> bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    }
}
