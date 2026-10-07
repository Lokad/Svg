using System.Security.Cryptography;
using System.Text.Json;
using Lokad.Svg.VisualDiff;

namespace Lokad.Svg.VisualTests;

public sealed class AntialiasedPaintClipReferenceTests
{
    [Fact]
    public void Every_control_preserves_its_original_capture_triple_and_complete_candidate()
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "tests", "References");
        using JsonDocument checkpoint = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(directory, "antialiased-paint-clip-dispatch.json")));
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "manifest.json")));
        Dictionary<string, JsonElement> fixtures = manifest.RootElement.GetProperty("fixtures").EnumerateArray().ToDictionary(fixture => Required(fixture, "id"), StringComparer.Ordinal);
        JsonElement[] controls = checkpoint.RootElement.GetProperty("controls").EnumerateArray().ToArray();
        Assert.Equal(41, controls.Length);
        Assert.Equal(27, controls.Count(control => control.GetProperty("declaredBeforeProductionEdit").GetBoolean()));
        int exact = 0, known = 0, rejected = 0;
        foreach (JsonElement control in controls)
        {
            string id = Required(control, "id");
            JsonElement fixture = fixtures[id];
            byte[] input = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", Required(fixture, "path")));
            Assert.Equal(Required(control, "inputSha256"), Hash(input));
            byte[] reference = File.ReadAllBytes(Path.Combine(directory, id + ".png"));
            Assert.Equal(Required(control, "referenceSha256"), Hash(reference));
            JsonElement[] captures = control.GetProperty("captures").EnumerateArray().ToArray();
            Assert.Equal(3, captures.Length);
            Assert.All(captures, capture =>
            {
                Assert.Equal(Hash(reference), Required(capture, "pngSha256"));
                Assert.Equal("healthy", Required(capture, "startupHealth"));
                Assert.True(capture.GetProperty("profileCleaned").GetBoolean());
            });
            SvgColor background = Required(fixture, "background") switch { "transparent" => SvgColor.Transparent, "white" => new(255, 255, 255, 255), "black" => new(0, 0, 0, 255), _ => throw new InvalidDataException("Invalid background.") };
            int width = fixture.GetProperty("width").GetInt32(), height = fixture.GetProperty("height").GetInt32();
            SvgRenderOptions options = new(new SvgViewport(width, height), 1, background, SvgRenderLimits.Default);
            if (Required(control, "outcome") != "renders")
            {
                SvgException exception = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(input, options, CancellationToken.None));
                Assert.Equal(Required(control, "outcome"), exception.ErrorCode.ToString());
                rejected++;
                continue;
            }
            byte[] candidate = SvgConverter.ConvertToPng(input, options, CancellationToken.None).ToArray();
            // The checkpoint retains its historical comparison. Current expectations live
            // with the fixture; exact pixels do not depend on candidate PNG encoding.
            using JsonDocument current = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(directory, id + ".json")));
            JsonElement after = current.RootElement.GetProperty("comparison");
            if (!after.GetProperty("exact").GetBoolean())
                Assert.Equal(Required(current.RootElement, "candidateSha256"), Hash(candidate));
            PixelComparison comparison = PixelComparer.Compare(PngRgbaDecoder.Decode(reference, width * height), PngRgbaDecoder.Decode(candidate, width * height));
            Assert.Equal(after.GetProperty("exact").GetBoolean(), comparison.IsExact);
            Assert.Equal(after.GetProperty("changedPixelCount").GetInt64(), comparison.ChangedPixelCount);
            if (comparison.IsExact) exact++; else known++;
        }
        Assert.Equal((32, 0, 9), (exact, known, rejected));
    }

    [Fact]
    public void Contained_convex_fringe_pairs_match_the_independent_material_grid_pixels()
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "tests", "References");
        using JsonDocument checkpoint = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(directory, "antialiased-paint-clip-dispatch.json")));
        const string id = "original-aa-clip-root-material-pairs-transparent";
        byte[] input = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "original", "aa-clip-root-material-pairs.svg"));
        RgbaImage reference = PngRgbaDecoder.Decode(File.ReadAllBytes(Path.Combine(directory, id + ".png")), 768 * 96);
        RgbaImage candidate = PngRgbaDecoder.Decode(SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None), 768 * 96);
        JsonElement[] witnesses = checkpoint.RootElement.GetProperty("pairWitnesses").EnumerateArray().ToArray();
        Assert.Equal(8, witnesses.Length);
        foreach (JsonElement witness in witnesses)
        {
            Assert.Equal(id, Required(witness, "id"));
            int offset = (witness.GetProperty("y").GetInt32() * 768 + witness.GetProperty("x").GetInt32()) * 4;
            byte[] expected = witness.GetProperty("reference").EnumerateArray().Select(value => value.GetByte()).ToArray();
            byte[] previous = witness.GetProperty("before").EnumerateArray().Select(value => value.GetByte()).ToArray();
            Assert.False(expected.AsSpan().SequenceEqual(previous));
            Assert.Equal(expected, reference.Pixels.AsSpan(offset, 4).ToArray());
            Assert.Equal(expected, candidate.Pixels.AsSpan(offset, 4).ToArray());
        }
    }

    [Fact]
    public void Full_white_mask_and_unmasked_twins_preserve_the_identical_original_browser_reference()
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "tests", "References");
        byte[] original = File.ReadAllBytes(Path.Combine(directory, "original-logo-mask-rounded-mark.png"));
        byte[] maskedReference = File.ReadAllBytes(Path.Combine(directory, "original-aa-clip-mask-region-masked-transparent.png"));
        byte[] unmaskedReference = File.ReadAllBytes(Path.Combine(directory, "original-aa-clip-mask-region-unmasked-transparent.png"));
        Assert.Equal(original, maskedReference);
        Assert.Equal(original, unmaskedReference);
        byte[] masked = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "original", "aa-clip-mask-region-masked.svg"));
        byte[] unmasked = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "original", "aa-clip-mask-region-unmasked.svg"));
        Assert.Equal(SvgConverter.ConvertToPng(masked, SvgRenderOptions.Default, CancellationToken.None).ToArray(), SvgConverter.ConvertToPng(unmasked, SvgRenderOptions.Default, CancellationToken.None).ToArray());
    }

    private static string Required(JsonElement item, string name) => item.GetProperty(name).GetString() ?? throw new InvalidDataException("Missing " + name);
    private static string Hash(ReadOnlySpan<byte> bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
}
