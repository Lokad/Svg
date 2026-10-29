using System.Security.Cryptography;
using System.Text.Json;
using Lokad.Svg.Fonts;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed partial class SvgTextPaintBoundsTests
{
    public static IEnumerable<object[]> SharedPathBoxCases()
    {
        using JsonDocument data = JsonDocument.Parse(File.ReadAllBytes(DataPath("shared-path-vectors.json")));
        foreach (JsonElement row in data.RootElement.EnumerateArray())
            yield return [row.GetProperty("id").GetString() ?? throw new InvalidDataException("Missing box case id.")];
    }

    [Theory]
    [MemberData(nameof(SharedPathBoxCases))]
    [Trait("Layer", "Mechanisms")]
    public async Task Direct_and_path_views_share_the_native_whole_text_object_box(string id)
    {
        using SvgFontSnapshot fonts = await CreateSnapshotAsync();
        byte[] bytes = File.ReadAllBytes(DataPath("shared-path-vectors.json"));
        using JsonDocument capture = JsonDocument.Parse(File.ReadAllBytes(DataPath("shared-path-capture.json")));
        Assert.Equal(capture.RootElement.GetProperty("vectorSha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(bytes)));
        using JsonDocument vectors = JsonDocument.Parse(bytes);
        JsonElement row = vectors.RootElement.EnumerateArray().Single(row => row.GetProperty("id").GetString() == id);
        byte[] input = System.Text.Encoding.UTF8.GetBytes(row.GetProperty("source").GetString() ?? throw new InvalidDataException("Missing box source."));
        Assert.Equal(row.GetProperty("sourceSha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(input)));
        double[] expected = row.GetProperty(OperatingSystem.IsWindows() ? "windows" : "linux").EnumerateArray().Select(value => value.GetDouble()).ToArray();
        RenderBudget budget = new(8_000_000);
        using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, fonts, CancellationToken.None))
        {
            int paintedGlyphs = 0;
            foreach (SvgGeometry geometry in root.Geometries)
            {
                if (geometry.Kind != SvgGeometryKind.GlyphContours) continue;
                SvgTextLinearShader shader = Assert.IsType<SvgTextLinearShader>(geometry.Glyph.Shader);
                SvgGradientTransform box = (shader.Gradient.SourcePaint ?? throw new InvalidDataException("Missing shared source box.")).Data.Local;
                double[] actual = [box.E, box.F, box.A, box.D];
                Assert.True(expected.SequenceEqual(actual), id + ": expected " + JsonSerializer.Serialize(expected) + ", actual " + JsonSerializer.Serialize(actual));
                paintedGlyphs++;
            }
            Assert.True(paintedGlyphs > 0);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }
}
