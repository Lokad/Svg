using System.Security.Cryptography;
using System.Text.Json;
using Lokad.Svg.VisualDiff;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.VisualTests;

public sealed class FordVariantTests
{
    [Fact]
    public void Observed_native_tile_clip_closes_the_first_large_Ford_alpha_divergence()
    {
        byte[] input = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "candidates", "inv-002.svg"));
        string directory = Path.Combine(AppContext.BaseDirectory, "tests", "References", "inventory");
        using JsonDocument evidence = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(directory, "inv-002-native-clip-context.json")));
        Assert.Equal(evidence.RootElement.GetProperty("inputSha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(input)));
        Assert.Equal(3, evidence.RootElement.GetProperty("variants").GetArrayLength());
        JsonElement witness = evidence.RootElement.GetProperty("firstLargeWitness");
        JsonElement clip = witness.GetProperty("constructionClip");
        SvgAnalyticClipBounds constructionBounds = new(clip.GetProperty("left").GetInt32(), clip.GetProperty("top").GetInt32(), clip.GetProperty("right").GetInt32(), clip.GetProperty("bottom").GetInt32());
        int row = witness.GetProperty("row").GetInt32();
        SvgRenderOptions options = new(new SvgViewport(1000, 400), 1, SvgColor.Transparent, SvgRenderLimits.Default);
        RenderBudget parsing = new(options.Limits.MaximumAccountedBytes);
        (double scaleX, double scaleY) = SvgConverter.TryResolveOutputScales(input, options, parsing, CancellationToken.None);
        using SvgRoot root = SvgRootParser.Parse(input, options.Limits, parsing, null, new ParserWorkCounter(options.Limits.MaximumWorkUnits), CancellationToken.None, scaleX, scaleY, options.Viewport);
        var polygon = root.Geometries.Where(geometry => geometry.Kind == SvgGeometryKind.PolygonFill).ElementAt(1).Polygon;
        SvgDevicePoint[] points = polygon.Points.Select(point => new SvgDevicePoint(point.X * scaleX, point.Y * scaleY)).ToArray();
        SvgCubicSpan[] cubics = polygon.CubicSpans.Select(span => new SvgCubicSpan(span.Contour, span.StartIndex, span.EndIndex, new(span.FirstControl.X * scaleX, span.FirstControl.Y * scaleY), new(span.SecondControl.X * scaleX, span.SecondControl.Y * scaleY))).ToArray();
        Assert.Empty(polygon.QuadSpans);
        int left = Math.Max(0, (int)Math.Floor(points.Min(point => point.X)));
        int right = Math.Min(1000, (int)Math.Ceiling(points.Max(point => point.X)));
        int top = Math.Max(0, (int)Math.Floor(points.Min(point => point.Y)));
        int bottom = Math.Min(400, (int)Math.Ceiling(points.Max(point => point.Y)));
        ReadOnlySpan<int> votes = polygon.Signs is int[] signs ? signs : [];
        RenderBudget budget = new(options.Limits.MaximumAccountedBytes);
        RgbaImage reference = PngRgbaDecoder.Decode(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "tests", "References", "inventory", "inv-002-1000x400-transparent.png")), 400_000);
        RgbaImage rendered = PngRgbaDecoder.Decode(SvgConverter.ConvertToPng(input, options, CancellationToken.None), 400_000);
        {
            using SvgAnalyticCoverage whole = SvgAnalyticCoverage.Prepare(points, polygon.ContourEnds, votes, polygon.IsEvenOdd, polygon.Signs, polygon.Mapping, [], cubics, left, right, top, bottom, 1000, 400, new(0, 1000), null, options.Limits.MaximumWorkUnits, budget, out long _, out bool wholeEligible, CancellationToken.None);
            // The pinned Chrome trace records the bitmap rectangle (254,0,256,256).
            // Its construction clip changes the native LowerRight X at this row;
            // merely clipping the already prepared whole-canvas row cannot do so.
            using SvgAnalyticCoverage tile = SvgAnalyticCoverage.Prepare(points, polygon.ContourEnds, votes, polygon.IsEvenOdd, polygon.Signs, polygon.Mapping, [], cubics, left, right, top, bottom, 1000, 400, new(constructionBounds.Left, constructionBounds.Right), new(constructionBounds), options.Limits.MaximumWorkUnits, budget, out long _, out bool tileEligible, CancellationToken.None);
            Assert.True(wholeEligible);
            Assert.True(tileEligible);
            whole.PrepareRow(row, out long _, CancellationToken.None);
            tile.PrepareRow(row, out long _, CancellationToken.None);
            byte[] pixel = new byte[4];
            whole.PaintPixel(witness.GetProperty("x").GetInt32() - left, pixel, SvgAnalyticPaint.Solid(polygon.Color), 1);
            Assert.Equal(witness.GetProperty("wholeCanvasAlpha").GetByte(), pixel[3]);
            JsonElement interior = witness.GetProperty("comparedInterior");
            for (int x = interior.GetProperty("left").GetInt32(); x < interior.GetProperty("rightExclusive").GetInt32(); x++)
            {
                pixel.AsSpan().Clear();
                tile.PaintPixel(x - left, pixel, SvgAnalyticPaint.Solid(polygon.Color), 1);
                Assert.Equal(reference.Pixels[(row * 1000 + x) * 4 + 3], pixel[3]);
                Assert.Equal(reference.Pixels[(row * 1000 + x) * 4 + 3], rendered.Pixels[(row * 1000 + x) * 4 + 3]);
            }
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Theory]
    [InlineData(250, 100, "transparent")]
    [InlineData(250, 100, "white")]
    [InlineData(250, 100, "black")]
    [InlineData(500, 200, "transparent")]
    [InlineData(500, 200, "white")]
    [InlineData(500, 200, "black")]
    [InlineData(1000, 400, "transparent")]
    [InlineData(1000, 400, "white")]
    [InlineData(1000, 400, "black")]
    public void Every_frozen_Ford_variant_retains_its_original_oracle_and_current_disposition(int width, int height, string background)
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "tests", "References", "inventory");
        using JsonDocument metadata = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(directory, "inv-002-variants.json")));
        JsonElement root = metadata.RootElement;
        Assert.Equal(9, root.GetProperty("variants").GetArrayLength());
        Assert.Equal(3, root.GetProperty("freshProcessCaptures").GetInt32());
        Assert.Equal("identical", root.GetProperty("decodedPixelStability").GetString());
        string id = $"inv-002-{width}x{height}-{background}";
        JsonElement variant = Assert.Single(root.GetProperty("variants").EnumerateArray(), value => value.GetProperty("id").GetString() == id);
        Assert.Equal(width, variant.GetProperty("width").GetInt32());
        Assert.Equal(height, variant.GetProperty("height").GetInt32());
        Assert.Equal(background, variant.GetProperty("background").GetString());
        byte[] input = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "candidates", "inv-002.svg"));
        Assert.Equal(root.GetProperty("inputSha256").GetString(), Hash(input));
        string referenceName = variant.GetProperty("referencePng").GetString() ?? throw new InvalidDataException("Ford reference filename is missing.");
        byte[] referenceBytes = File.ReadAllBytes(Path.Combine(directory, referenceName));
        Assert.Equal(variant.GetProperty("referenceSha256").GetString(), Hash(referenceBytes));
        Assert.All(variant.GetProperty("captureSha256").EnumerateArray(), capture => Assert.Equal(Hash(referenceBytes), capture.GetString()));
        Assert.All(variant.GetProperty("captureStartupHealth").EnumerateArray(), health => Assert.Equal("healthy", health.GetString()));
        Assert.All(variant.GetProperty("captureProfilesCleaned").EnumerateArray(), cleaned => Assert.True(cleaned.GetBoolean()));
        SvgColor color = background switch { "transparent" => SvgColor.Transparent, "white" => new(255, 255, 255, 255), "black" => new(0, 0, 0, 255), _ => throw new ArgumentOutOfRangeException(nameof(background)) };
        SvgRenderOptions options = new(new SvgViewport(width, height), 1, color, SvgRenderLimits.Default);
        ReadOnlyMemory<byte> png = SvgConverter.ConvertToPng(input, options, CancellationToken.None);
        Assert.Equal(variant.GetProperty("candidateSha256").GetString(), Hash(png.Span));
        RgbaImage reference = PngRgbaDecoder.Decode(referenceBytes, width * height);
        RgbaImage candidate = PngRgbaDecoder.Decode(png, width * height);
        PixelComparison comparison = PixelComparer.Compare(reference, candidate);
        JsonElement expected = variant.GetProperty("comparison");
        Assert.True(comparison.DimensionsMatch);
        Assert.Equal(expected.GetProperty("exact").GetBoolean(), comparison.IsExact);
        Assert.Equal(expected.GetProperty("changedPixelCount").GetInt64(), comparison.ChangedPixelCount);
        Assert.Equal(expected.GetProperty("maximumChannelError").GetByte(), comparison.MaximumChannelError);
        Assert.Equal(expected.GetProperty("maximumAlphaError").GetByte(), comparison.MaximumAlphaError);

        static string Hash(ReadOnlySpan<byte> bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    }
}
