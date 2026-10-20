using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;
using Lokad.Svg.VisualDiff;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.VisualTests;

public sealed class FordVariantTests
{
    [Fact]
    public void Numeric_authoring_matches_every_recorded_Ford_point_and_transform_lane()
    {
        byte[] input = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "candidates", "inv-002.svg"));
        using JsonDocument native = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "tests", "References", "inventory", "inv-002-recorded-native-paths.json")));
        Assert.Equal(native.RootElement.GetProperty("inputSha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(input)));
        using MemoryStream stream = new(input);
        XDocument source = XDocument.Load(stream);
        XNamespace svg = "http://www.w3.org/2000/svg";
        XElement[] paths = source.Descendants(svg + "path").ToArray();
        JsonElement[] draws = native.RootElement.GetProperty("commandLog").EnumerateArray().Where(x => x.GetProperty("method").GetString() == "drawPath").ToArray();
        Assert.Equal(2, paths.Length);
        Assert.Equal(2, draws.Length);
        int compared = 0;
        for (int fill = 0; fill < paths.Length; fill++)
        {
            string data = paths[fill].Attribute("d")?.Value ?? throw new InvalidDataException("Missing Ford path data.");
            Assert.All(data.Where(char.IsLetter), command => Assert.Contains(command, new[] { 'M', 'm', 'c' }));
            SvgContourBuilder builder = new(10_000, 8);
            builder.EnableCubicSpanRetention();
            SvgPathDataParser.ParsePath(data, SvgAffineTransform.Identity, builder, new(0.5, 64, 10_000), 1000, new(1_000_000), CancellationToken.None, null);
            SvgCubicSpan[] cubics = builder.GetCubicSpans();
            Assert.Equal(fill == 0 ? 84 : 60, cubics.Length);
            Assert.Equal(2, builder.ContourCount);
            int contour = -1;
            int cubic = 0;
            foreach (JsonElement verb in draws[fill].GetProperty("params").GetProperty("path").GetProperty("pathPoints").EnumerateArray())
            {
                JsonElement points = verb.GetProperty("points");
                if (verb.GetProperty("verb").GetString() == "Move")
                {
                    Assert.Equal(1, points.GetArrayLength());
                    ComparePoint(points[0], builder.GetContour(++contour)[0]);
                }
                else
                {
                    Assert.Equal("Cubic", verb.GetProperty("verb").GetString());
                    Assert.Equal(3, points.GetArrayLength());
                    SvgCubicSpan span = cubics[cubic++];
                    Assert.Equal(contour, span.Contour);
                    ComparePoint(points[0], span.FirstControl);
                    ComparePoint(points[1], span.SecondControl);
                    ComparePoint(points[2], builder.GetContour(contour)[span.EndIndex]);
                }
            }
            Assert.Equal(cubics.Length, cubic);
        }
        Assert.Equal(436, compared);
        XElement group = Assert.Single(source.Descendants(svg + "g"), x => x.Attribute("transform") is not null);
        SvgAffineTransform transform = SvgGeometrySyntax.ParseTransform(group.Attribute("transform")?.Value, new(1000), CancellationToken.None);
        JsonElement matrix = native.RootElement.GetProperty("commandLog")[10].GetProperty("params").GetProperty("matrix44");
        Assert.Equal(0x3fac3548, BitConverter.SingleToInt32Bits((float)transform.A));
        Assert.Equal(BitConverter.SingleToInt32Bits((float)matrix[0].GetDouble()), BitConverter.SingleToInt32Bits((float)(transform.A * 2)));
        Assert.Equal(BitConverter.SingleToInt32Bits((float)matrix[5].GetDouble()), BitConverter.SingleToInt32Bits((float)(transform.D * 2)));
        Assert.Equal(BitConverter.SingleToInt32Bits((float)matrix[12].GetDouble()), BitConverter.SingleToInt32Bits((float)(transform.E * 2)));
        Assert.Equal(BitConverter.SingleToInt32Bits((float)matrix[13].GetDouble()), BitConverter.SingleToInt32Bits((float)(transform.F * 2)));

        void ComparePoint(JsonElement expected, SvgDevicePoint actual)
        {
            Assert.Equal(BitConverter.SingleToInt32Bits((float)expected.GetProperty("x").GetDouble()), BitConverter.SingleToInt32Bits((float)actual.X));
            Assert.Equal(BitConverter.SingleToInt32Bits((float)expected.GetProperty("y").GetDouble()), BitConverter.SingleToInt32Bits((float)actual.Y));
            compared++;
        }
    }

    [Fact]
    public void Recorded_native_density_closes_all_ten_lower_tile_witnesses()
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "tests", "References", "inventory");
        byte[] input = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "candidates", "inv-002.svg"));
        using JsonDocument evidence = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(directory, "inv-002-native-density.json")));
        using JsonDocument native = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(directory, "inv-002-recorded-native-paths.json")));
        Assert.Equal(evidence.RootElement.GetProperty("inputSha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(input)));
        SvgRenderOptions options = new(new SvgViewport(1000, 400), 1, SvgColor.Transparent, SvgRenderLimits.Default);
        RenderBudget parsing = new(options.Limits.MaximumAccountedBytes);
        (double scaleX, double scaleY) = SvgPreparationTestHelper.GetOutputScales(input, options, parsing, CancellationToken.None);
        using SvgRoot scene = SvgRootParser.Parse(input, options.Limits, parsing, null, new ParserWorkCounter(options.Limits.MaximumWorkUnits), CancellationToken.None, scaleX, scaleY, options.Viewport);
        RgbaImage output = PngRgbaDecoder.Decode(SvgConverter.ConvertToPng(input, options, CancellationToken.None), 400_000);
        RgbaImage oracle = PngRgbaDecoder.Decode(File.ReadAllBytes(Path.Combine(directory, "inv-002-1000x400-transparent.png")), 400_000);
        int witnessed = 0;
        foreach (JsonElement group in evidence.RootElement.GetProperty("groups").EnumerateArray())
        {
            int fill = group.GetProperty("fill").GetInt32();
            int rawPoints = native.RootElement.GetProperty("commandLog")[11 + fill].GetProperty("params").GetProperty("path").GetProperty("pathPoints").EnumerateArray().Sum(verb => verb.GetProperty("points").GetArrayLength());
            Assert.Equal(group.GetProperty("recordedPathPoints").GetInt32(), rawPoints);
            var polygon = scene.Geometries.Where(x => x.Kind == SvgGeometryKind.PolygonFill).ElementAt(fill).Polygon;
            Assert.Equal(group.GetProperty("flattenedPoints").GetInt32(), polygon.Points.Length);
            // Counting is clip-independent. This ordinary preparation proves
            // retained metadata reconstructs the recorded native path count.
            SvgAnalyticContourPreparation preparation = new(polygon.Points, polygon.ContourEnds, [], false, polygon.Signs, polygon.Mapping, polygon.QuadSpans, polygon.CubicSpans, new(0, 0, 1000, 400), 1000, 400);
            RenderBudget budget = new(options.Limits.MaximumAccountedBytes);
            {
                using SvgAnalyticCoverage walker = SvgAnalyticCoverage.Prepare(ref preparation, 0, 1000, 0, 400, new(0, 1000), new(new(0, 0, 1000, 400)), SvgAnalyticPaintClip.Binary, options.Limits.MaximumWorkUnits, budget, out long _, out bool eligible, CancellationToken.None);
                Assert.True(eligible);
                Assert.Equal((long)rawPoints, preparation.NativePointCount);
            }
            Assert.Equal(0, budget.Snapshot().CurrentBytes);
            int height = group.GetProperty("clippedWalkHeight").GetInt32();
            Assert.True(polygon.Points.Length > 2 * height);
            Assert.False(rawPoints > 2 * height);
            Assert.False(group.GetProperty("nativeSkipIntersections").GetBoolean());
            Assert.True(group.GetProperty("baselineSkipIntersections").GetBoolean());
            foreach (JsonElement witness in group.GetProperty("witnesses").EnumerateArray())
            {
                int offset = (witness.GetProperty("y").GetInt32() * 1000 + witness.GetProperty("x").GetInt32()) * 4;
                byte[] rgba = witness.GetProperty("referenceRgba").EnumerateArray().Select(x => x.GetByte()).ToArray();
                Assert.Equal(rgba, oracle.Pixels.AsSpan(offset, 4).ToArray());
                Assert.Equal(rgba, output.Pixels.AsSpan(offset, 4).ToArray());
                Assert.Equal(rgba[3], witness.GetProperty("checkIntersectionsAdditions").EnumerateArray().Sum(x => x.GetInt32()));
                Assert.NotEqual((int)rgba[3], witness.GetProperty("skipIntersectionsAdditions").EnumerateArray().Sum(x => x.GetInt32()));
                witnessed++;
            }
        }
        Assert.Equal(10, witnessed);
    }

    [Fact]
    public void Coupled_numeric_reading_closes_the_upper_tile_269_187_witness()
    {
        byte[] input = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "candidates", "inv-002.svg"));
        SvgRenderOptions options = new(new SvgViewport(1000, 400), 1, SvgColor.Transparent, SvgRenderLimits.Default);
        RgbaImage output = PngRgbaDecoder.Decode(SvgConverter.ConvertToPng(input, options, CancellationToken.None), 400_000);
        RgbaImage reference = PngRgbaDecoder.Decode(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "tests", "References", "inventory", "inv-002-1000x400-transparent.png")), 400_000);
        int offset = (187 * 1000 + 269) * 4;
        Assert.Equal(new byte[] { 0, 50, 119, 107 }, reference.Pixels.AsSpan(offset, 4).ToArray());
        Assert.Equal(reference.Pixels.AsSpan(offset, 4).ToArray(), output.Pixels.AsSpan(offset, 4).ToArray());
    }

    [Fact]
    public void Axis_aligned_path_mapping_closes_the_lower_tile_row_269_witness()
    {
        byte[] input = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "candidates", "inv-002.svg"));
        SvgRenderOptions options = new(new SvgViewport(1000, 400), 1, SvgColor.Transparent, SvgRenderLimits.Default);
        RgbaImage output = PngRgbaDecoder.Decode(SvgConverter.ConvertToPng(input, options, CancellationToken.None), 400_000);
        RgbaImage reference = PngRgbaDecoder.Decode(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "tests", "References", "inventory", "inv-002-1000x400-transparent.png")), 400_000);
        int offset = (269 * 1000 + 375) * 4;
        // The actual captured path through the pinned native mapper/builder/walk
        // produces coverage 107. Identical old prepared edges yield 106 with
        // either intersection-density mode; recurrence changes are unnecessary.
        Assert.Equal(new byte[] { 0, 50, 119, 107 }, reference.Pixels.AsSpan(offset, 4).ToArray());
        Assert.Equal(reference.Pixels.AsSpan(offset, 4).ToArray(), output.Pixels.AsSpan(offset, 4).ToArray());
    }

    [Fact]
    public void Horizontal_tile_clip_forwards_the_remaining_row_thirty_pair_as_a_run()
    {
        byte[] input = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "candidates", "inv-002.svg"));
        using JsonDocument native = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "tests", "References", "inventory", "inv-002-recorded-native-paths.json")));
        Assert.Equal(native.RootElement.GetProperty("inputSha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(input)));
        int[] pointCounts = native.RootElement.GetProperty("commandLog").EnumerateArray()
            .Where(command => command.GetProperty("method").GetString() == "drawPath")
            .Select(command => command.GetProperty("params").GetProperty("path").GetProperty("pathPoints").EnumerateArray().Sum(verb => verb.GetProperty("points").GetArrayLength())).ToArray();
        Assert.Equal(new[] { 254, 182 }, pointCounts);
        SvgRenderOptions options = new(new SvgViewport(1000, 400), 1, SvgColor.Transparent, SvgRenderLimits.Default);
        RgbaImage output = PngRgbaDecoder.Decode(SvgConverter.ConvertToPng(input, options, CancellationToken.None), 400_000);
        RgbaImage reference = PngRgbaDecoder.Decode(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "tests", "References", "inventory", "inv-002-1000x400-transparent.png")), 400_000);
        int offset = (30 * 1000 + 740) * 4;
        // The pinned native walk emits pair coverage 91. A horizontal clip
        // wrapper forwards it as a run: stored PM (0,18,43,91), versus the
        // unwrapped pair's (0,18,42,90), then the pinned capture conversion.
        Assert.Equal(new byte[] { 0, 50, 120, 91 }, reference.Pixels.AsSpan(offset, 4).ToArray());
        Assert.Equal(reference.Pixels.AsSpan(offset, 4).ToArray(), output.Pixels.AsSpan(offset, 4).ToArray());
    }

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
        (double scaleX, double scaleY) = SvgPreparationTestHelper.GetOutputScales(input, options, parsing, CancellationToken.None);
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
            whole.PaintPixel(witness.GetProperty("x").GetInt32() - left, pixel, SvgAnalyticPaint.Solid(polygon.Color), SvgAnalyticClipCoverage.Full);
            Assert.Equal(witness.GetProperty("wholeCanvasAlpha").GetByte(), pixel[3]);
            JsonElement interior = witness.GetProperty("comparedInterior");
            for (int x = interior.GetProperty("left").GetInt32(); x < interior.GetProperty("rightExclusive").GetInt32(); x++)
            {
                pixel.AsSpan().Clear();
                tile.PaintPixel(x - left, pixel, SvgAnalyticPaint.Solid(polygon.Color), SvgAnalyticClipCoverage.Full);
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
        Assert.True(expected.GetProperty("exact").GetBoolean());
        Assert.True(comparison.IsExact);
        Assert.Equal(expected.GetProperty("changedPixelCount").GetInt64(), comparison.ChangedPixelCount);
        Assert.Equal(expected.GetProperty("maximumChannelError").GetByte(), comparison.MaximumChannelError);
        Assert.Equal(expected.GetProperty("maximumAlphaError").GetByte(), comparison.MaximumAlphaError);

        static string Hash(ReadOnlySpan<byte> bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    }
}
