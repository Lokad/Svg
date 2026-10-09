using System.Globalization;
using System.Security.Cryptography;
using System.Xml.Linq;
using System.Text.Json;
using Lokad.Svg.VisualDiff;

namespace Lokad.Svg.VisualTests;

public sealed class SvgRemainingScopeDocumentTests
{
    public static IEnumerable<object[]> Cases()
    {
        foreach (string family in new[] { "remaining-scope-documents", "remaining-gap-exports", "finite-support-exports", "compound-clip-documents" })
        {
            string directory = Path.Combine(AppContext.BaseDirectory, "Research", family);
            using JsonDocument manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(directory, "manifest.json")));
            foreach (JsonElement item in manifest.RootElement.GetProperty("cases").EnumerateArray())
                yield return new object[] { family, Required(item, "id") };
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task Retained_documents_preserve_frozen_outputs_and_admission_gates_through_six_apis(string family, string identity)
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "Research", family);
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(directory, "manifest.json")));
        JsonElement root = manifest.RootElement;
        Assert.Equal(family switch { "remaining-scope-documents" => 11, "remaining-gap-exports" => 3, "finite-support-exports" => 4, "compound-clip-documents" => 2, _ => throw new InvalidDataException("Unknown document family.") }, root.GetProperty("cases").GetArrayLength());
        Assert.Equal(3, root.GetProperty("freshProcessCaptures").GetInt32());
        Assert.Equal("identical", Required(root, "decodedPixelStability"));
        Assert.Equal("healthy", Required(root, "captureStartupHealth"));
        Assert.True(root.GetProperty("captureProfilesCleaned").GetBoolean());
        JsonElement item = root.GetProperty("cases").EnumerateArray().Single(c => Required(c, "id") == identity);
        string document = Required(item, "document");
        Assert.Equal(document == "inv-109" ? 9 : 1, root.GetProperty("cases").EnumerateArray().Count(c => Required(c, "document") == document));
        Assert.Equal("document-output", Required(item, "role"));
        Assert.NotEmpty(Required(item, "selection"));
        using JsonDocument sourceRecords = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(directory, "source-records.json")));
        JsonElement source = sourceRecords.RootElement.GetProperty("records").EnumerateArray().Single(c => Required(c, "identity") == document);
        bool publicDomain = document is "inv-110" or "inv-112" or "inv-115" or "inv-117";
        Assert.Equal(publicDomain ? "Public domain" : "CC0-1.0", Required(item, "license"));
        Assert.Equal(publicDomain ? "Public domain" : "CC0", Required(source.GetProperty("extmetadata").GetProperty("LicenseShortName"), "value"));
        Assert.Equal(Required(source, "sourceUrl"), Required(item, "sourceUrl"));
        byte[] input = File.ReadAllBytes(Path.Combine(directory, Required(item, "input")));
        byte[] original = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "candidates", document + ".svg"));
        Assert.Equal(original, input);
        Assert.Equal(source.GetProperty("size").GetInt32(), input.Length);
        Assert.Equal(Required(source, "sha1"), Convert.ToHexStringLower(SHA1.HashData(input)));
        Assert.Equal(Required(source, "sha256"), Convert.ToHexStringLower(SHA256.HashData(input)));
        Assert.Equal(Required(item, "sourceSha256"), Required(source, "sha256"));
        int width = item.GetProperty("width").GetInt32(), height = item.GetProperty("height").GetInt32();
        if (item.TryGetProperty("captureInput", out JsonElement captureInput))
        {
            byte[] captured = File.ReadAllBytes(Path.Combine(directory, captureInput.GetString() ?? throw new InvalidDataException()));
            Assert.Equal(Required(item, "captureInputSha256"), Convert.ToHexStringLower(SHA256.HashData(captured)));
            if (Required(item, "materialization") == "root-dimensions-only")
            {
                using MemoryStream originalStream = new(input, false);
                using MemoryStream capturedStream = new(captured, false);
                XDocument originalXml = XDocument.Load(originalStream, LoadOptions.PreserveWhitespace);
                XDocument capturedXml = XDocument.Load(capturedStream, LoadOptions.PreserveWhitespace);
                XElement originalRoot = Assert.IsType<XElement>(originalXml.Root);
                XElement capturedRoot = Assert.IsType<XElement>(capturedXml.Root);
                Assert.Equal(width.ToString(CultureInfo.InvariantCulture), capturedRoot.Attribute("width")?.Value);
                Assert.Equal(height.ToString(CultureInfo.InvariantCulture), capturedRoot.Attribute("height")?.Value);
                Assert.Equal(originalXml.Declaration?.ToString(), capturedXml.Declaration?.ToString());
                originalRoot.Attribute("width")?.Remove(); originalRoot.Attribute("height")?.Remove();
                capturedRoot.Attribute("width")?.Remove(); capturedRoot.Attribute("height")?.Remove();
                Assert.True(XNode.DeepEquals(originalXml, capturedXml), "Browser sizing must preserve every other XML node and attribute.");
            }
            else
            {
                string dimensions = "width=\"" + width + "\" height=\"" + height + "\"";
                string expected = Required(item, "materialization") switch
                {
                    "replace-root-dimensions" => System.Text.Encoding.UTF8.GetString(input).Replace(Required(item, "nativeRootDimensions"), dimensions, StringComparison.Ordinal),
                    "insert-root-dimensions" => System.Text.Encoding.UTF8.GetString(input).Replace("<svg ", "<svg " + dimensions + " ", StringComparison.Ordinal),
                    _ => throw new InvalidDataException("Unknown browser viewport materialization.")
                };
                Assert.Equal(expected, System.Text.Encoding.UTF8.GetString(captured));
            }
        }
        byte[] referenceBytes = File.ReadAllBytes(Path.Combine(directory, Required(item, "reference")));
        Assert.Equal(Required(item, "referenceSha256"), Convert.ToHexStringLower(SHA256.HashData(referenceBytes)));
        RgbaImage reference = PngRgbaDecoder.Decode(referenceBytes, checked(width * height));
        Assert.Equal(width, reference.Width);
        Assert.Equal(height, reference.Height);
        SvgColor background = Required(item, "background") switch
        {
            "transparent" => SvgColor.Transparent,
            "white" => new(255, 255, 255, 255),
            "black" => new(0, 0, 0, 255),
            _ => throw new InvalidDataException("Unknown background.")
        };
        SvgViewport? viewport = item.TryGetProperty("viewport", out JsonElement frame)
            ? new(frame.GetProperty("width").GetInt32(), frame.GetProperty("height").GetInt32()) : null;
        SvgRenderOptions options = new(viewport, item.GetProperty("scale").GetDouble(), background, SvgRenderLimits.Default);
        if (item.TryGetProperty("expectedError", out JsonElement error))
        {
            SvgErrorCode expectedError = Enum.Parse<SvgErrorCode>(error.GetString() ?? throw new InvalidDataException());
            Check(Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(input, options, CancellationToken.None)));
            byte[] target = new byte[checked(width * height * 4)];
            Check(Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(input, target, options, CancellationToken.None)));
            using MemoryStream rejected = new();
            Check(Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(input, rejected, options, CancellationToken.None)));
            Check(await Assert.ThrowsAsync<SvgException>(async () => await SvgConverter.ConvertToPngAsync(input, options, CancellationToken.None)));
            Check(await Assert.ThrowsAsync<SvgException>(async () => await SvgConverter.ConvertToPngAsync(input, target, options, CancellationToken.None)));
            Check(await Assert.ThrowsAsync<SvgException>(async () => await SvgConverter.ConvertToPngAsync(input, rejected, options, CancellationToken.None)));
            Assert.Equal(0, rejected.Length);
            return;

            void Check(SvgException exception)
            {
                Assert.Equal(expectedError, exception.ErrorCode);
                Assert.Equal(Required(item, "expectedMessage"), exception.Message);
            }
        }
        byte[] png = SvgConverter.ConvertToPng(input, options, CancellationToken.None).ToArray();
        PixelComparison comparison = PixelComparer.Compare(reference, PngRgbaDecoder.Decode(png, checked(width * height)));
        if (item.TryGetProperty("knownDifference", out JsonElement difference)) Assert.Equal(difference.Deserialize<PixelComparison>(), comparison);
        else Assert.True(comparison.IsExact, identity + ": " + comparison);
        byte[] destination = new byte[png.Length];
        Assert.Equal(png.Length, SvgConverter.ConvertToPng(input, destination, options, CancellationToken.None));
        Assert.Equal(png, destination);
        using MemoryStream stream = new();
        Assert.Equal(png.Length, SvgConverter.ConvertToPng(input, stream, options, CancellationToken.None));
        Assert.Equal(png, stream.ToArray());
        Assert.Equal(png, (await SvgConverter.ConvertToPngAsync(input, options, CancellationToken.None)).ToArray());
        Assert.Equal(png.Length, await SvgConverter.ConvertToPngAsync(input, destination, options, CancellationToken.None));
        Assert.Equal(png, destination);
        using MemoryStream asyncStream = new();
        Assert.Equal(png.Length, await SvgConverter.ConvertToPngAsync(input, asyncStream, options, CancellationToken.None));
        Assert.Equal(png, asyncStream.ToArray());
    }

    private static string Required(JsonElement element, string name) => element.GetProperty(name).GetString() ?? throw new InvalidDataException(name + " is missing.");
}
