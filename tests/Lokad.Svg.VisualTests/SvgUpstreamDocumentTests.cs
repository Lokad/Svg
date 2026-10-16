using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;
using Lokad.Svg.Fonts;
using Lokad.Svg.VisualDiff;

namespace Lokad.Svg.VisualTests;

/// <summary>Exercises the finite upstream document selection through the existing native-reference comparison.</summary>
public sealed class SvgUpstreamDocumentTests
{
    private static string DirectoryPath => Path.Combine(AppContext.BaseDirectory, "Research", "upstream-static-documents");

    public static IEnumerable<object[]> Documents()
    {
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(DirectoryPath, "manifest.json")));
        foreach (JsonElement item in manifest.RootElement.GetProperty("cases").EnumerateArray())
            yield return new object[] { Required(item, "id") };
    }

    [Theory]
    [MemberData(nameof(Documents))]
    public async Task Original_upstream_documents_match_frozen_platform_references(string identity)
    {
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(DirectoryPath, "manifest.json")));
        JsonElement root = manifest.RootElement;
        Assert.Equal(26, root.GetProperty("cases").GetArrayLength());
        Assert.Equal(3, root.GetProperty("freshProcessCaptures").GetInt32());
        Assert.Equal("healthy", Required(root, "captureStartupHealth"));
        Assert.Equal("identical", Required(root, "decodedPixelStability"));
        Assert.True(root.GetProperty("captureProfilesCleaned").GetBoolean());
        JsonElement item = root.GetProperty("cases").EnumerateArray().Single(c => Required(c, "id") == identity);
        Assert.Equal("document-output", Required(item, "role"));
        Assert.NotEmpty(Required(item, "selection"));
        Assert.NotEmpty(Required(item, "upstreamRevision"));
        Assert.NotEmpty(Required(item, "upstreamTest"));
        Assert.NotEmpty(Required(item, "sourceUrl"));
        Assert.NotEmpty(item.GetProperty("coveredGroups").EnumerateArray());
        Assert.Equal(Required(item, "suite") == "resvg" ? "MIT" : "BSD-3-Clause", Required(item, "license"));
        byte[] input = Read(item, "input", "sourceSha256");
        byte[] transport = Read(item, "captureInput", "captureInputSha256");
        using MemoryStream originalStream = new(input, false), transportStream = new(transport, false);
        XDocument originalXml = XDocument.Load(originalStream, LoadOptions.PreserveWhitespace);
        XDocument transportXml = XDocument.Load(transportStream, LoadOptions.PreserveWhitespace);
        XElement originalRoot = Assert.IsType<XElement>(originalXml.Root);
        XElement transportRoot = Assert.IsType<XElement>(transportXml.Root);
        int width = item.GetProperty("width").GetInt32(), height = item.GetProperty("height").GetInt32();
        Assert.Equal(1, item.GetProperty("scale").GetDouble());
        Assert.Equal("transparent", Required(item, "background"));
        Assert.Equal(width, item.GetProperty("viewport").GetProperty("width").GetInt32());
        Assert.Equal(height, item.GetProperty("viewport").GetProperty("height").GetInt32());
        Assert.Equal(width.ToString(System.Globalization.CultureInfo.InvariantCulture), transportRoot.Attribute("width")?.Value);
        Assert.Equal(height.ToString(System.Globalization.CultureInfo.InvariantCulture), transportRoot.Attribute("height")?.Value);
        bool suppliedFont = item.GetProperty("fonts").GetArrayLength() != 0;
        if (suppliedFont)
        {
            XElement definitions = transportRoot.Elements().Last();
            Assert.Equal(XName.Get("defs", "http://www.w3.org/2000/svg"), definitions.Name);
            string style = Assert.Single(definitions.Elements()).Value;
            foreach (JsonElement program in root.GetProperty("fontPrograms").EnumerateArray())
                if (item.GetProperty("fonts").EnumerateArray().Any(f => f.GetString() == Path.GetFileName(Required(program, "file"))))
                {
                    byte[] bytes = Read(program, "file", "sha256");
                    Assert.Contains(Convert.ToBase64String(bytes), style, StringComparison.Ordinal);
                }
            definitions.Remove();
        }
        foreach (XElement element in new[] { originalRoot, transportRoot })
        {
            element.Attribute("width")?.Remove();
            element.Attribute("height")?.Remove();
        }
        Assert.True(XNode.DeepEquals(originalXml, transportXml), "Capture transport must preserve every other original XML node and attribute.");

        bool linux = OperatingSystem.IsLinux();
        byte[] reference = Read(item, linux ? "linuxReference" : "reference", linux ? "linuxReferenceSha256" : "referenceSha256");
        int pixels = checked(width * height);
        RgbaImage expected = PngRgbaDecoder.Decode(reference, pixels);
        Assert.Equal(width, expected.Width);
        Assert.Equal(height, expected.Height);
        byte[] actual;
        if (suppliedFont)
        {
            SvgFontPackCatalog catalog = SvgFontPackCatalog.Parse(File.ReadAllBytes(Path.Combine(DirectoryPath, "font-pack.json")), "upstream-noto-sans");
            using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync(input, catalog, new Provider(DirectoryPath), CancellationToken.None);
            foreach (JsonElement file in item.GetProperty("fonts").EnumerateArray())
            {
                string name = file.GetString() ?? throw new InvalidDataException();
                SvgPreparedFont font = snapshot.GetFont(new("Noto Sans", 400, name.Contains("Italic", StringComparison.Ordinal)));
                Assert.Equal("fonts/" + name, font.Resolution.Resolution.Face.File.RelativePath);
                Assert.False(font.Resolution.Resolution.IsFallback);
            }
            SvgRenderOptions options = new(new SvgViewport(width, height), 1, SvgColor.Transparent, SvgRenderLimits.Default, snapshot);
            actual = SvgConverter.ConvertToPng(input, options, CancellationToken.None).ToArray();
            JsonElement proofs = item.GetProperty("fontProof").GetProperty(linux ? "linux" : "windows");
            Assert.Equal(identity == "text-real-italic" ? 2 : 1, proofs.GetArrayLength());
            foreach (JsonElement proof in proofs.EnumerateArray())
            {
                byte[] negative = Read(proof, "reference", "sha256");
                Assert.Equal(3, proof.GetProperty("freshProcessCaptures").GetInt32());
                Assert.False(PixelComparer.Compare(expected, PngRgbaDecoder.Decode(negative, pixels)).IsExact);
            }
        }
        else
        {
            SvgRenderOptions options = new(new SvgViewport(width, height), 1, SvgColor.Transparent, SvgRenderLimits.Default);
            actual = SvgConverter.ConvertToPng(input, options, CancellationToken.None).ToArray();
        }
        PixelComparison comparison = PixelComparer.Compare(expected, PngRgbaDecoder.Decode(actual, pixels));
        Assert.True(comparison.IsExact, identity + ": " + comparison);

        static byte[] Read(JsonElement record, string path, string hash)
        {
            byte[] bytes = File.ReadAllBytes(Path.Combine(DirectoryPath, Required(record, path)));
            Assert.Equal(Required(record, hash), Convert.ToHexStringLower(SHA256.HashData(bytes)));
            return bytes;
        }
    }

    private static string Required(JsonElement item, string name) => item.GetProperty(name).GetString() ?? throw new InvalidDataException(name + " is missing.");

    /// <summary>Owns independent immutable arrays per acquisition. Concurrent reads share no mutable state, observe cancellation and retain no program or render storage.</summary>
    private sealed class Provider(string directory) : ISvgFontProgramSourceProvider
    {
        public ValueTask<ISvgFontProgramSource?> GetSourceAsync(SvgFontPackFile file, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            byte[] bytes = File.ReadAllBytes(Path.Combine(directory, file.RelativePath));
            token.ThrowIfCancellationRequested();
            return ValueTask.FromResult<ISvgFontProgramSource?>(new SvgMemoryFontProgramSource("upstream-" + file.RelativePath, bytes));
        }
    }
}
