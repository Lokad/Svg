using System.Security.Cryptography;
using System.Text.Json;
using Lokad.Svg.VisualDiff;

namespace Lokad.Svg.VisualTests;

public sealed class FixtureManifestTests
{
    [Fact]
    public void Exact_solid_red_fixture_matches_its_admitted_chrome_reference()
    {
        string fixtures = Path.Combine(AppContext.BaseDirectory, "Fixtures");
        byte[] svg = File.ReadAllBytes(Path.Combine(fixtures, "original", "solid-red.svg"));
        RgbaImage reference = PngRgbaDecoder.Decode(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "tests", "References", "original-solid-red.png")), 4);
        RgbaImage candidate = PngRgbaDecoder.Decode(SvgConverter.ConvertToPng(svg, SvgRenderOptions.Default, CancellationToken.None), 4);

        PixelComparison result = PixelComparer.Compare(reference, candidate);

        Assert.True(result.IsExact, "Changed pixels: " + result.ChangedPixelCount);
    }

    [Fact]
    public void Exact_integer_alpha_overlap_fixture_matches_its_admitted_chrome_reference()
    {
        string fixtures = Path.Combine(AppContext.BaseDirectory, "Fixtures");
        byte[] svg = File.ReadAllBytes(Path.Combine(fixtures, "original", "integer-alpha-overlap.svg"));
        RgbaImage reference = PngRgbaDecoder.Decode(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "tests", "References", "original-integer-alpha-overlap.png")), 4);
        RgbaImage candidate = PngRgbaDecoder.Decode(SvgConverter.ConvertToPng(svg, SvgRenderOptions.Default, CancellationToken.None), 4);

        PixelComparison result = PixelComparer.Compare(reference, candidate);

        Assert.True(result.IsExact, "Changed pixels: " + result.ChangedPixelCount);
    }

    [Fact]
    public void Exact_nested_groups_fixture_matches_its_admitted_chrome_reference()
    {
        byte[] svg = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "original", "nested-groups.svg"));
        RgbaImage reference = PngRgbaDecoder.Decode(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "tests", "References", "original-nested-groups.png")), 2);
        PixelComparison result = PixelComparer.Compare(reference, PngRgbaDecoder.Decode(SvgConverter.ConvertToPng(svg, SvgRenderOptions.Default, CancellationToken.None), 2));
        Assert.True(result.IsExact, "Changed pixels: " + result.ChangedPixelCount);
    }

    [Fact]
    public void Exact_inherited_paint_fixture_matches_its_admitted_chrome_reference()
    {
        byte[] svg = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "original", "inherited-paint.svg"));
        RgbaImage reference = PngRgbaDecoder.Decode(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "tests", "References", "original-inherited-paint.png")), 2);
        PixelComparison result = PixelComparer.Compare(reference, PngRgbaDecoder.Decode(SvgConverter.ConvertToPng(svg, SvgRenderOptions.Default, CancellationToken.None), 2));
        Assert.True(result.IsExact, "Changed pixels: " + result.ChangedPixelCount);
    }

    [Fact]
    public void Exact_inline_styles_fixture_matches_its_admitted_chrome_reference()
    {
        byte[] svg = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "original", "inline-styles.svg"));
        RgbaImage reference = PngRgbaDecoder.Decode(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "tests", "References", "original-inline-styles.png")), 2);
        PixelComparison result = PixelComparer.Compare(reference, PngRgbaDecoder.Decode(SvgConverter.ConvertToPng(svg, SvgRenderOptions.Default, CancellationToken.None), 2));
        Assert.True(result.IsExact, "Changed pixels: " + result.ChangedPixelCount);
    }

    [Fact]
    public void Exact_view_box_meet_fixture_matches_its_admitted_chrome_reference()
    {
        byte[] svg = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "original", "viewbox-meet.svg"));
        RgbaImage reference = PngRgbaDecoder.Decode(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "tests", "References", "original-viewbox-meet.png")), 16);
        PixelComparison result = PixelComparer.Compare(reference, PngRgbaDecoder.Decode(SvgConverter.ConvertToPng(svg, SvgRenderOptions.Default, CancellationToken.None), 16));
        Assert.True(result.IsExact, "Changed pixels: " + result.ChangedPixelCount);
    }

    [Fact]
    public void Exact_view_box_none_fixture_matches_its_admitted_chrome_reference()
    {
        byte[] svg = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "original", "viewbox-none.svg"));
        RgbaImage reference = PngRgbaDecoder.Decode(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "tests", "References", "original-viewbox-none.png")), 16);
        PixelComparison result = PixelComparer.Compare(reference, PngRgbaDecoder.Decode(SvgConverter.ConvertToPng(svg, SvgRenderOptions.Default, CancellationToken.None), 16));
        Assert.True(result.IsExact, "Changed pixels: " + result.ChangedPixelCount);
    }

    [Fact]
    public void Exact_view_box_alignment_fixture_matches_its_admitted_chrome_reference()
    {
        byte[] svg = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "original", "viewbox-alignment.svg"));
        RgbaImage reference = PngRgbaDecoder.Decode(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "tests", "References", "original-viewbox-alignment.png")), 16);
        PixelComparison result = PixelComparer.Compare(reference, PngRgbaDecoder.Decode(SvgConverter.ConvertToPng(svg, SvgRenderOptions.Default, CancellationToken.None), 16));
        Assert.True(result.IsExact, "Changed pixels: " + result.ChangedPixelCount);
    }

    [Fact]
    public void Exact_view_box_slice_fixture_matches_its_admitted_chrome_reference()
    {
        byte[] svg = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "original", "viewbox-slice.svg"));
        RgbaImage reference = PngRgbaDecoder.Decode(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "tests", "References", "original-viewbox-slice.png")), 16);
        Assert.True(PixelComparer.Compare(reference, PngRgbaDecoder.Decode(SvgConverter.ConvertToPng(svg, SvgRenderOptions.Default, CancellationToken.None), 16)).IsExact);
    }

    [Fact]
    public void Exact_integer_transforms_fixture_matches_its_admitted_chrome_reference()
    {
        byte[] svg = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "original", "integer-transforms.svg"));
        RgbaImage reference = PngRgbaDecoder.Decode(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "tests", "References", "original-integer-transforms.png")), 12);
        Assert.True(PixelComparer.Compare(reference, PngRgbaDecoder.Decode(SvgConverter.ConvertToPng(svg, SvgRenderOptions.Default, CancellationToken.None), 12)).IsExact);
    }

    [Fact]
    public void Exact_integer_scale_transforms_fixture_matches_its_admitted_chrome_reference()
    {
        byte[] svg = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "original", "integer-scale-transforms.svg"));
        RgbaImage reference = PngRgbaDecoder.Decode(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "tests", "References", "original-integer-scale-transforms.png")), 20);
        Assert.True(PixelComparer.Compare(reference, PngRgbaDecoder.Decode(SvgConverter.ConvertToPng(svg, SvgRenderOptions.Default, CancellationToken.None), 20)).IsExact);
    }

    [Fact]
    public void Exact_default_object_size_fixture_matches_its_admitted_chrome_reference()
    {
        byte[] svg = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "original", "default-object-size.svg"));
        RgbaImage reference = PngRgbaDecoder.Decode(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "tests", "References", "original-default-object-size.png")), 45_000);
        Assert.True(PixelComparer.Compare(reference, PngRgbaDecoder.Decode(SvgConverter.ConvertToPng(svg, SvgRenderOptions.Default, CancellationToken.None), 45_000)).IsExact);
    }

    [Fact]
    public void Exact_intrinsic_ratio_size_fixture_matches_its_admitted_chrome_reference()
    {
        byte[] svg = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "original", "intrinsic-ratio-size.svg"));
        RgbaImage reference = PngRgbaDecoder.Decode(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "tests", "References", "original-intrinsic-ratio-size.png")), 200);
        Assert.True(PixelComparer.Compare(reference, PngRgbaDecoder.Decode(SvgConverter.ConvertToPng(svg, SvgRenderOptions.Default, CancellationToken.None), 200)).IsExact);
    }

    [Fact]
    public void Exact_integer_matrix_transform_fixture_matches_its_admitted_chrome_reference()
    {
        byte[] svg = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "original", "integer-matrix-transform.svg"));
        RgbaImage reference = PngRgbaDecoder.Decode(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "tests", "References", "original-integer-matrix-transform.png")), 20);
        Assert.True(PixelComparer.Compare(reference, PngRgbaDecoder.Decode(SvgConverter.ConvertToPng(svg, SvgRenderOptions.Default, CancellationToken.None), 20)).IsExact);
    }

    [Fact]
    public void Exact_rectangle_path_fixture_matches_its_admitted_chrome_reference()
    {
        byte[] svg = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "original", "rectangle-path.svg"));
        RgbaImage reference = PngRgbaDecoder.Decode(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "tests", "References", "original-rectangle-path.png")), 6);
        Assert.True(PixelComparer.Compare(reference, PngRgbaDecoder.Decode(SvgConverter.ConvertToPng(svg, SvgRenderOptions.Default, CancellationToken.None), 6)).IsExact);
    }

    [Fact]
    public void Exact_line_rectangle_path_fixture_matches_its_admitted_chrome_reference()
    {
        byte[] svg = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "original", "line-rectangle-path.svg"));
        RgbaImage reference = PngRgbaDecoder.Decode(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "tests", "References", "original-line-rectangle-path.png")), 6);
        Assert.True(PixelComparer.Compare(reference, PngRgbaDecoder.Decode(SvgConverter.ConvertToPng(svg, SvgRenderOptions.Default, CancellationToken.None), 6)).IsExact);
    }

    [Fact]
    public void Exact_orthogonal_polygon_fixture_matches_its_admitted_chrome_reference()
    {
        byte[] svg = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "original", "orthogonal-polygon.svg"));
        RgbaImage reference = PngRgbaDecoder.Decode(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "tests", "References", "original-orthogonal-polygon.png")), 9);
        Assert.True(PixelComparer.Compare(reference, PngRgbaDecoder.Decode(SvgConverter.ConvertToPng(svg, SvgRenderOptions.Default, CancellationToken.None), 9)).IsExact);
    }

    [Fact]
    public void Exact_orthogonal_path_fixture_matches_its_admitted_chrome_reference()
    {
        byte[] svg = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "original", "orthogonal-path.svg"));
        RgbaImage reference = PngRgbaDecoder.Decode(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "tests", "References", "original-orthogonal-path.png")), 9);
        Assert.True(PixelComparer.Compare(reference, PngRgbaDecoder.Decode(SvgConverter.ConvertToPng(svg, SvgRenderOptions.Default, CancellationToken.None), 9)).IsExact);
    }

    [Fact]
    public void Exact_orthogonal_polyline_fixture_matches_its_admitted_chrome_reference()
    {
        byte[] svg = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "original", "orthogonal-polyline.svg"));
        RgbaImage reference = PngRgbaDecoder.Decode(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "tests", "References", "original-orthogonal-polyline.png")), 9);
        Assert.True(PixelComparer.Compare(reference, PngRgbaDecoder.Decode(SvgConverter.ConvertToPng(svg, SvgRenderOptions.Default, CancellationToken.None), 9)).IsExact);
    }

    [Fact]
    public void Exact_color_alpha_fill_opacity_fixture_matches_its_admitted_chrome_reference()
    {
        byte[] svg = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "original", "color-alpha-fill-opacity.svg"));
        RgbaImage reference = PngRgbaDecoder.Decode(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "tests", "References", "original-color-alpha-fill-opacity.png")), 1);
        Assert.True(PixelComparer.Compare(reference, PngRgbaDecoder.Decode(SvgConverter.ConvertToPng(svg, SvgRenderOptions.Default, CancellationToken.None), 1)).IsExact);
    }

    [Fact]
    public void Exact_group_opacity_overlap_fixture_matches_its_admitted_chrome_reference()
    {
        byte[] svg = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "original", "group-opacity-overlap.svg"));
        RgbaImage reference = PngRgbaDecoder.Decode(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "tests", "References", "original-group-opacity-overlap.png")), 3);
        Assert.True(PixelComparer.Compare(reference, PngRgbaDecoder.Decode(SvgConverter.ConvertToPng(svg, SvgRenderOptions.Default, CancellationToken.None), 3)).IsExact);
    }

    [Fact]
    public void Exact_root_opacity_overlap_fixture_matches_its_admitted_chrome_reference()
    {
        byte[] svg = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "original", "root-opacity-overlap.svg"));
        RgbaImage reference = PngRgbaDecoder.Decode(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "tests", "References", "original-root-opacity-overlap.png")), 3);
        Assert.True(PixelComparer.Compare(reference, PngRgbaDecoder.Decode(SvgConverter.ConvertToPng(svg, SvgRenderOptions.Default, CancellationToken.None), 3)).IsExact);
    }

    [Fact]
    public void Exact_fractional_overlap_fixture_matches_its_admitted_chrome_reference()
    {
        byte[] svg = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "original", "fractional-overlap.svg"));
        RgbaImage reference = PngRgbaDecoder.Decode(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "tests", "References", "original-fractional-overlap.png")), 64);
        Assert.True(PixelComparer.Compare(reference, PngRgbaDecoder.Decode(SvgConverter.ConvertToPng(svg, SvgRenderOptions.Default, CancellationToken.None), 64)).IsExact);
    }

    [Fact]
    public void Manifest_has_unique_ids_hash_matched_assets_and_explicit_fidelity_status()
    {
        string fixtures = Path.Combine(AppContext.BaseDirectory, "Fixtures");
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(fixtures, "manifest.json")));
        HashSet<string> ids = new(StringComparer.Ordinal);
        foreach (JsonElement fixture in manifest.RootElement.GetProperty("fixtures").EnumerateArray())
        {
            string id = fixture.GetProperty("id").GetString() ?? throw new InvalidDataException("Fixture ID is missing.");
            Assert.True(ids.Add(id), "Duplicate fixture ID: " + id);
            string asset = Path.Combine(fixtures, fixture.GetProperty("path").GetString() ?? throw new InvalidDataException("Fixture path is missing."));
            Assert.True(File.Exists(asset), "Missing fixture: " + asset);
            string actualHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(asset))).ToLowerInvariant();
            Assert.Equal(fixture.GetProperty("originalSha256").GetString(), actualHash);
            Assert.Equal(fixture.GetProperty("materializedSha256").GetString(), actualHash);
            Assert.Equal("CC0-1.0", fixture.GetProperty("license").GetString());
            Assert.Contains(fixture.GetProperty("expectedFidelity").GetString(), new[] { "exact", "known-difference", "unsupported", "oracle-failure" });
            if (fixture.TryGetProperty("oracleIdentity", out JsonElement oracleIdentity) && oracleIdentity.GetString() is string referenceMetadataPath)
            {
                string metadataPath = Path.Combine(AppContext.BaseDirectory, referenceMetadataPath.Replace('/', Path.DirectorySeparatorChar));
                Assert.True(File.Exists(metadataPath), "Missing reference metadata: " + metadataPath);
                using JsonDocument reference = JsonDocument.Parse(File.ReadAllText(metadataPath));
                string pngPath = Path.Combine(Path.GetDirectoryName(metadataPath) ?? throw new InvalidDataException("Reference metadata has no directory."), reference.RootElement.GetProperty("referencePng").GetString() ?? throw new InvalidDataException("Reference PNG is missing."));
                string referenceHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(pngPath))).ToLowerInvariant();
                Assert.Equal(reference.RootElement.GetProperty("referenceSha256").GetString(), referenceHash);
                Assert.Equal(fixture.GetProperty("referenceSha256").GetString(), referenceHash);
                if (fixture.GetProperty("expectedFidelity").GetString() == "exact")
                {
                    Assert.Equal(3, reference.RootElement.GetProperty("freshProcessCaptures").GetInt32());
                    Assert.Equal("identical", reference.RootElement.GetProperty("decodedPixelStability").GetString());
                    Assert.True(reference.RootElement.GetProperty("comparison").GetProperty("exact").GetBoolean());
                }
            }
        }
    }
}
