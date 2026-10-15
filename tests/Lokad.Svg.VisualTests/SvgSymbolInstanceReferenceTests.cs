using System.Security.Cryptography;
using System.Text.Json;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;
using Lokad.Svg.VisualDiff;

namespace Lokad.Svg.VisualTests;

public sealed class SvgSymbolInstanceReferenceTests
{
    public static IEnumerable<object[]> Cases()
    {
        foreach (string family in new[] { "symbol-instances", "symbol-presentation", "symbol-content", "symbol-content-diagnostics", "symbol-viewport-opacity", "use-paint-properties", "rectangle-frames", "rectangle-path-strokes", "use-stroke-opacity", "use-stroke-style", "rendered-group-styles", "instance-shader-opacity", "container-shader-inheritance", "viewport-percentage-bases", "transformed-rectangle-fills", "iching-library", "pattern-vector-content", "pattern-inheritance", "pattern-affine-placement", "pattern-general-placement", "nested-affine-opacity", "gradient-masks", "mask-local-references", "mask-content-order", "mask-group-scopes", "mask-presentation", "paint-order", "fractional-rectangle-widths", "oval-source-replay", "radiation-symbol", "affine-arcs", "malta-flag", "native-path-replay", "portugal-flag", "source-convexity", "bhutan-flag", "south-africa-flag", "convex-path-clips", "wikifoods-logo", "oval-percentage-lengths", "curved-path-clips", "native-clip-winding", "deferred-paint-leaves", "affine-radial-paints", "compound-clip-content", "transformed-rectangle-strokes", "object-bounding-clips", "affine-rectangle-strokes", "pattern-viewbox", "pattern-instance-content", "gradient-stop-ranges", "open-clip-contours", "group-mask-crop", "clip-intersections", "vector-effects", "filter-regions", "filter-merges", "nonuniform-curve-paints", "affine-paint-instances", "symbol-fractional-frames", "affine-oval-strokes", "scoped-clips-masks", "colored-effects-markers", "static-css-images", "affine-image-maps", "effects-marker-sheet", "composition-sheet", "shared-paint-map-sheet", "primitive-stroke-sheet", "stroke-style-exit-sheet", "shared-map-exit-sheet", "clip-union-exit-sheet", "cubic-viewport-exit-sheet", "composition-resource-exit-sheet", "mask-color-affine-exit-sheet", "mask-viewport-exit-sheet", "viewport-clip-opacity-exit-sheet", "forward-bbox-clip-exit-sheet", "tile-mask-exit-sheet" })
        {
            using JsonDocument manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Research", family, "manifest.json")));
            foreach (JsonElement item in manifest.RootElement.GetProperty("cases").EnumerateArray())
                yield return new object[] { family, RequiredString(item, "id") };
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Symbol_instances_retain_measured_browser_outcomes(string family, string identity)
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "Research", family);
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(directory, "manifest.json")));
        JsonElement root = manifest.RootElement;
        Assert.Equal(family switch { "symbol-content-diagnostics" => 3, "symbol-viewport-opacity" => 18, "use-paint-properties" => 31, "rectangle-frames" => 15, "rectangle-path-strokes" => 30, "use-stroke-opacity" => 81, "use-stroke-style" => 99, "rendered-group-styles" => 123, "instance-shader-opacity" => 150, "container-shader-inheritance" => 192, "viewport-percentage-bases" => 81, "transformed-rectangle-fills" => 15, "iching-library" => 25, "pattern-vector-content" => 22, "pattern-inheritance" => 17, "pattern-affine-placement" => 18, "pattern-general-placement" => 15, "nested-affine-opacity" => 7, "gradient-masks" => 20, "mask-local-references" => 19, "mask-content-order" => 12, "mask-group-scopes" => 16, "mask-presentation" => 33, "paint-order" => 29, "fractional-rectangle-widths" => 10, "oval-source-replay" => 8, "radiation-symbol" => 9, "affine-arcs" => 16, "malta-flag" => 9, "native-path-replay" => 15, "portugal-flag" => 9, "source-convexity" => 12, "bhutan-flag" => 9, "south-africa-flag" => 9, "convex-path-clips" => 11, "wikifoods-logo" => 9, "oval-percentage-lengths" => 12, "curved-path-clips" => 12, "native-clip-winding" => 18, "deferred-paint-leaves" => 29, "affine-radial-paints" => 23, "compound-clip-content" => 18, "transformed-rectangle-strokes" => 6, "object-bounding-clips" => 7, "affine-rectangle-strokes" => 8, "pattern-viewbox" => 6, "pattern-instance-content" => 9, "gradient-stop-ranges" => 6, "open-clip-contours" => 9, "group-mask-crop" => 16, "clip-intersections" => 10, "vector-effects" => 10, "filter-regions" => 26, "filter-merges" => 4, "nonuniform-curve-paints" => 2, "affine-paint-instances" => 8, "symbol-fractional-frames" => 5, "affine-oval-strokes" => 7, "scoped-clips-masks" => 6, "colored-effects-markers" => 13, "static-css-images" => 6, "affine-image-maps" => 4, "effects-marker-sheet" => 12, "composition-sheet" => 2, "shared-paint-map-sheet" => 4, "primitive-stroke-sheet" => 5, "stroke-style-exit-sheet" => 5, "shared-map-exit-sheet" => 6, "clip-union-exit-sheet" => 6, "cubic-viewport-exit-sheet" => 3, "composition-resource-exit-sheet" => 4, "mask-color-affine-exit-sheet" => 4, "mask-viewport-exit-sheet" => 6, "viewport-clip-opacity-exit-sheet" => 7, "forward-bbox-clip-exit-sheet" => 7, "tile-mask-exit-sheet" => 3, _ => 16 }, root.GetProperty("cases").GetArrayLength());
        Assert.Equal(3, root.GetProperty("freshProcessCaptures").GetInt32());
        Assert.Equal("identical", RequiredString(root, "decodedPixelStability"));
        Assert.Equal("healthy", RequiredString(root, "captureStartupHealth"));
        Assert.True(root.GetProperty("captureProfilesCleaned").GetBoolean());
        JsonElement item = root.GetProperty("cases").EnumerateArray().Single(value => RequiredString(value, "id") == identity);
        bool originalCoreControl = (family is "scoped-clips-masks" or "composition-sheet" or "clip-union-exit-sheet") && item.GetProperty("role").GetString() == "retained-core-admission-control";
        bool publicDomainDocument = family is "radiation-symbol" or "malta-flag" or "portugal-flag" or "bhutan-flag" or "south-africa-flag";
        bool retainedDocument = publicDomainDocument || family == "wikifoods-logo";
        Assert.Equal(originalCoreControl || family is "mask-viewport-exit-sheet" or "viewport-clip-opacity-exit-sheet" or "forward-bbox-clip-exit-sheet" or "tile-mask-exit-sheet" ? "MIT" : publicDomainDocument ? "Public domain" : "CC0-1.0", RequiredString(item, "license"));
        Assert.NotEmpty(RequiredString(item, "licenseEvidence"));
        Assert.NotEmpty(RequiredString(item, "selection"));
        string background = RequiredString(item, "background");
        if (retainedDocument) Assert.Contains(background, new[] { "transparent", "white", "black" });
        else Assert.Equal("transparent", background);
        byte[] input = File.ReadAllBytes(Path.Combine(directory, RequiredString(item, "input")));
        bool linuxReference = OperatingSystem.IsLinux() && item.TryGetProperty("linuxReference", out _);
        byte[] referenceBytes = File.ReadAllBytes(Path.Combine(directory, RequiredString(item, linuxReference ? "linuxReference" : "reference")));
        if (retainedDocument)
        {
            byte[] original = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "candidates", family switch { "malta-flag" => "inv-057.svg", "portugal-flag" => "inv-065.svg", "bhutan-flag" => "inv-064.svg", "south-africa-flag" => "inv-061.svg", "wikifoods-logo" => "inv-108.svg", _ => "inv-094.svg" }));
            Assert.Equal(RequiredString(item, "originalSha256"), Convert.ToHexStringLower(SHA256.HashData(original)));
            if (family is "portugal-flag" or "bhutan-flag" or "south-africa-flag" or "wikifoods-logo") Assert.Equal(original, input);
            else
            {
                string nativeSize = family == "malta-flag" ? "width=\"900\" height=\"600\"" : "width=\"600\" height=\"600\"";
                Assert.Equal(System.Text.Encoding.UTF8.GetString(original).Replace(nativeSize, "width=\"" + item.GetProperty("width").GetInt32() + "\" height=\"" + item.GetProperty("height").GetInt32() + "\"", StringComparison.Ordinal), System.Text.Encoding.UTF8.GetString(input));
            }
        }
        else if (!originalCoreControl && family is not ("mask-viewport-exit-sheet" or "viewport-clip-opacity-exit-sheet" or "forward-bbox-clip-exit-sheet" or "tile-mask-exit-sheet")) Assert.True(input.AsSpan().IndexOf("SPDX-License-Identifier: CC0-1.0"u8) >= 0);
        Assert.Equal(RequiredString(item, "sourceSha256"), Convert.ToHexStringLower(SHA256.HashData(input)));
        Assert.Equal(RequiredString(item, linuxReference && item.TryGetProperty("linuxReferenceSha256", out _) ? "linuxReferenceSha256" : "referenceSha256"), Convert.ToHexStringLower(SHA256.HashData(referenceBytes)));
        int width = item.GetProperty("width").GetInt32(), height = item.GetProperty("height").GetInt32();
        if (item.TryGetProperty("captureInput", out JsonElement captureInput))
        {
            byte[] capturedSource = File.ReadAllBytes(Path.Combine(directory, captureInput.GetString() ?? throw new InvalidDataException()));
            Assert.Equal(RequiredString(item, "captureInputSha256"), Convert.ToHexStringLower(SHA256.HashData(capturedSource)));
            string inputText = System.Text.Encoding.UTF8.GetString(input);
            string expectedCapture;
            if (family is "south-africa-flag" or "wikifoods-logo" or "curved-path-clips" or "affine-radial-paints")
            {
                string nativeSize = family switch { "wikifoods-logo" => "width=\"1200\" height=\"1200\"", "curved-path-clips" => "width=\"144\" height=\"144\"", "affine-radial-paints" => "width=\"80\" height=\"80\"", _ => "width=\"900\" height=\"600\"" };
                expectedCapture = inputText.Replace(nativeSize, "width=\"" + width + "\" height=\"" + height + "\"", StringComparison.Ordinal);
            }
            else if (family is "portugal-flag" or "bhutan-flag")
            {
                int rootEnd = inputText.IndexOf('>');
                Assert.True(rootEnd > 0);
                string nativeSize = family == "bhutan-flag" ? "width=\"900\" height=\"600\"" : "width=\"600\" height=\"400\"";
                string nativeViewBox = family == "bhutan-flag" ? "0 0 900 600" : "0 0 600 400";
                expectedCapture = inputText[..rootEnd].Replace(nativeSize, "width=\"" + width + "\" height=\"" + height + "\" viewBox=\"" + nativeViewBox + "\"", StringComparison.Ordinal) + inputText[rootEnd..];
            }
            else
            {
                string insertedRoot = "<svg\n    width=\"" + width + "\"\n    height=\"" + height + "\"\n";
                expectedCapture = inputText.Replace("<svg\n", insertedRoot, StringComparison.Ordinal);
            }
            Assert.Equal(expectedCapture, System.Text.Encoding.UTF8.GetString(capturedSource));
        }
        RgbaImage reference = PngRgbaDecoder.Decode(referenceBytes, checked(width * height));
        Assert.Equal(width, reference.Width);
        Assert.Equal(height, reference.Height);
        SvgColor color = background switch { "white" => new(255, 255, 255, 255), "black" => new(0, 0, 0, 255), _ => SvgColor.Transparent };
        double scale = item.TryGetProperty("scale", out JsonElement scaleValue) ? scaleValue.GetDouble() : 1;
        SvgRenderOptions options = item.TryGetProperty("viewport", out JsonElement viewport)
            ? new(new SvgViewport(viewport.GetProperty("width").GetInt32(), viewport.GetProperty("height").GetInt32()), 1, SvgColor.Transparent, SvgRenderLimits.Default)
            : new(null, scale, color, SvgRenderLimits.Default);
        if (item.TryGetProperty("expectedError", out JsonElement error))
        {
            SvgErrorCode expectedError = Enum.Parse<SvgErrorCode>(error.GetString() ?? throw new InvalidDataException());
            Assert.Equal(expectedError, Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(input, options, CancellationToken.None)).ErrorCode);
            return;
        }
        byte[] expected = SvgConverter.ConvertToPng(input, options, CancellationToken.None).ToArray();
        PixelComparison comparison = PixelComparer.Compare(reference, PngRgbaDecoder.Decode(expected, checked(width * height)));
        if (item.TryGetProperty("knownDifference", out JsonElement difference)) Assert.Equal(difference.Deserialize<PixelComparison>(), comparison);
        else Assert.True(comparison.IsExact, identity + ": " + comparison);
    }

    [Fact]
    public void Shared_maps_and_clip_unions_release_sources_and_rasters_on_denial_cancellation_and_retry()
    {
        foreach ((string Family, string Name) sample in new[]
        {
            ("shared-paint-map-sheet", "pattern-viewport-stroke-use"),
            ("shared-paint-map-sheet", "pattern-skewed-oval"),
            ("clip-union-exit-sheet", "mixed-rectangle-path"),
            ("clip-union-exit-sheet", "overlapping-curved-children"),
            ("clip-union-exit-sheet", "compound-concave-winding"),
            ("clip-union-exit-sheet", "many-child-mask-fallback"),
            ("cubic-viewport-exit-sheet", "cubic-viewport-sheet"),
            ("cubic-viewport-exit-sheet", "cubic-pair-pending"),
            ("cubic-viewport-exit-sheet", "affine-ancestor-viewport"),
            ("composition-resource-exit-sheet", "composition-resource-sheet"),
            ("composition-resource-exit-sheet", "mask-unit-scopes"),
            ("composition-resource-exit-sheet", "mixed-tile-local-scope"),
            ("mask-color-affine-exit-sheet", "mask-color-affine-sheet"),
            ("mask-color-affine-exit-sheet", "colored-luminance"),
            ("mask-color-affine-exit-sheet", "affine-user-mask"),
            ("mask-color-affine-exit-sheet", "affine-object-mask"),
            ("mask-viewport-exit-sheet", "mask-viewport-sheet"),
            ("mask-viewport-exit-sheet", "scaled-custom-region"),
            ("mask-viewport-exit-sheet", "forward-custom-region"),
            ("mask-viewport-exit-sheet", "use-custom-region"),
            ("mask-viewport-exit-sheet", "spatial-group-viewport"),
            ("mask-viewport-exit-sheet", "partial-user-region"),
            ("viewport-clip-opacity-exit-sheet", "viewport-clip-opacity-sheet"),
            ("viewport-clip-opacity-exit-sheet", "scaled-leaf-clip"),
            ("viewport-clip-opacity-exit-sheet", "group-inside-scaled-viewport"),
            ("viewport-clip-opacity-exit-sheet", "group-around-scaled-viewport"),
            ("viewport-clip-opacity-exit-sheet", "viewport-under-root-opacity"),
            ("viewport-clip-opacity-exit-sheet", "group-under-viewport-opacity"),
            ("viewport-clip-opacity-exit-sheet", "stroke-crossing-viewport"),
            ("forward-bbox-clip-exit-sheet", "forward-bbox-sheet"),
            ("forward-bbox-clip-exit-sheet", "forward-clip-masked-group"),
            ("forward-bbox-clip-exit-sheet", "forward-image-box"),
            ("forward-bbox-clip-exit-sheet", "forward-group-box"),
            ("forward-bbox-clip-exit-sheet", "zero-height-line-box"),
            ("forward-bbox-clip-exit-sheet", "forward-circle-box"),
            ("forward-bbox-clip-exit-sheet", "transformed-box-content"),
            ("tile-mask-exit-sheet", "tile-mask-sheet"),
            ("tile-mask-exit-sheet", "original-masked-tile"),
            ("tile-mask-exit-sheet", "spatial-masked-group")
        })
        {
            byte[] input = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Research", sample.Family, sample.Name + ".svg"));
            byte[] unchanged = input.ToArray();
            RenderBudget sceneBudget = new(16_000_000);
            ParserWorkCounter work = new(100_000_000);
            using (SvgRoot scene = SvgRootParser.Parse(input, SvgRenderLimits.Default, sceneBudget, null, work, CancellationToken.None))
            {
                foreach (long limit in new[] { 1, sceneBudget.Snapshot().PeakBytes / 2 })
                {
                    RenderBudget denied = new(limit);
                    Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() =>
                        SvgRootParser.Parse(input, SvgRenderLimits.Default, denied, CancellationToken.None)).ErrorCode);
                    Assert.Equal(0, denied.Snapshot().CurrentBytes);
                }
                RenderBudget workDenied = new(16_000_000);
                Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() =>
                    SvgRootParser.Parse(input, SvgRenderLimits.Default, workDenied, null, new(work.Used - 1), CancellationToken.None)).ErrorCode);
                Assert.Equal(0, workDenied.Snapshot().CurrentBytes);
                RenderBudget accepted = new(16_000_000);
                byte[] expected = SvgConverter.ConvertToPng(scene, SvgRenderOptions.Default, accepted, CancellationToken.None).ToArray();
                Assert.Equal(0, accepted.Snapshot().CurrentBytes);
                RenderBudget rasterDenied = new(accepted.Snapshot().PeakBytes / 2);
                Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() =>
                    SvgConverter.ConvertToPng(scene, SvgRenderOptions.Default, rasterDenied, CancellationToken.None)).ErrorCode);
                Assert.Equal(0, rasterDenied.Snapshot().CurrentBytes);
                using CancellationTokenSource cancellation = new();
                cancellation.Cancel();
                Assert.Equal(cancellation.Token, Assert.ThrowsAny<OperationCanceledException>(() =>
                    SvgRootParser.Parse(input, SvgRenderLimits.Default, workDenied, cancellation.Token)).CancellationToken);
                Assert.Equal(0, workDenied.Snapshot().CurrentBytes);
                Assert.Equal(cancellation.Token, Assert.ThrowsAny<OperationCanceledException>(() =>
                    SvgConverter.ConvertToPng(scene, SvgRenderOptions.Default, accepted, cancellation.Token)).CancellationToken);
                Assert.Equal(0, accepted.Snapshot().CurrentBytes);
                Assert.Equal(expected, SvgConverter.ConvertToPng(scene, SvgRenderOptions.Default, accepted, CancellationToken.None).ToArray());
                Assert.Equal(0, accepted.Snapshot().CurrentBytes);
            }
            Assert.Equal(0, sceneBudget.Snapshot().CurrentBytes);
            Assert.Equal(unchanged, input);
        }
    }
    [Fact]
    public void Primitive_shader_strokes_release_sources_and_rasters_on_denial_cancellation_and_retry()
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "Research", "primitive-stroke-sheet");
        foreach (string name in new[] { "line-shader-alpha", "point-list-radial-map" })
        {
            byte[] input = File.ReadAllBytes(Path.Combine(directory, name + ".svg"));
            byte[] unchanged = input.ToArray();
            RenderBudget sceneBudget = new(16_000_000);
            ParserWorkCounter work = new(100_000_000);
            using (SvgRoot scene = SvgRootParser.Parse(input, SvgRenderLimits.Default, sceneBudget, null, work, CancellationToken.None))
            {
                foreach (long limit in new[] { 1, sceneBudget.Snapshot().PeakBytes / 2 })
                {
                    RenderBudget denied = new(limit);
                    Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() =>
                        SvgRootParser.Parse(input, SvgRenderLimits.Default, denied, CancellationToken.None)).ErrorCode);
                    Assert.Equal(0, denied.Snapshot().CurrentBytes);
                }
                RenderBudget workDenied = new(16_000_000);
                Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() =>
                    SvgRootParser.Parse(input, SvgRenderLimits.Default, workDenied, null, new(work.Used - 1), CancellationToken.None)).ErrorCode);
                Assert.Equal(0, workDenied.Snapshot().CurrentBytes);
                RenderBudget accepted = new(16_000_000);
                byte[] expected = SvgConverter.ConvertToPng(scene, SvgRenderOptions.Default, accepted, CancellationToken.None).ToArray();
                Assert.Equal(0, accepted.Snapshot().CurrentBytes);
                RenderBudget rasterDenied = new(accepted.Snapshot().PeakBytes / 2);
                Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() =>
                    SvgConverter.ConvertToPng(scene, SvgRenderOptions.Default, rasterDenied, CancellationToken.None)).ErrorCode);
                Assert.Equal(0, rasterDenied.Snapshot().CurrentBytes);
                using CancellationTokenSource cancellation = new();
                cancellation.Cancel();
                Assert.Equal(cancellation.Token, Assert.ThrowsAny<OperationCanceledException>(() =>
                    SvgRootParser.Parse(input, SvgRenderLimits.Default, workDenied, cancellation.Token)).CancellationToken);
                Assert.Equal(0, workDenied.Snapshot().CurrentBytes);
                Assert.Equal(cancellation.Token, Assert.ThrowsAny<OperationCanceledException>(() =>
                    SvgConverter.ConvertToPng(scene, SvgRenderOptions.Default, accepted, cancellation.Token)).CancellationToken);
                Assert.Equal(0, accepted.Snapshot().CurrentBytes);
                Assert.Equal(expected, SvgConverter.ConvertToPng(scene, SvgRenderOptions.Default, accepted, CancellationToken.None).ToArray());
                Assert.Equal(0, accepted.Snapshot().CurrentBytes);
            }
            Assert.Equal(0, sceneBudget.Snapshot().CurrentBytes);
            Assert.Equal(unchanged, input);
        }
    }

    [Fact]
    public void Stroke_style_exits_release_sources_and_rasters_on_denial_cancellation_and_retry()
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "Research", "stroke-style-exit-sheet");
        foreach (string name in new[] { "solid-line-caps", "rounded-shader-alpha", "oval-shader-alpha" })
        {
            byte[] input = File.ReadAllBytes(Path.Combine(directory, name + ".svg"));
            byte[] unchanged = input.ToArray();
            RenderBudget sceneBudget = new(16_000_000);
            ParserWorkCounter work = new(100_000_000);
            using (SvgRoot scene = SvgRootParser.Parse(input, SvgRenderLimits.Default, sceneBudget, null, work, CancellationToken.None))
            {
                foreach (long limit in new[] { 1, sceneBudget.Snapshot().PeakBytes / 2 })
                {
                    RenderBudget denied = new(limit);
                    Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() =>
                        SvgRootParser.Parse(input, SvgRenderLimits.Default, denied, CancellationToken.None)).ErrorCode);
                    Assert.Equal(0, denied.Snapshot().CurrentBytes);
                }
                RenderBudget workDenied = new(16_000_000);
                Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() =>
                    SvgRootParser.Parse(input, SvgRenderLimits.Default, workDenied, null, new(work.Used - 1), CancellationToken.None)).ErrorCode);
                Assert.Equal(0, workDenied.Snapshot().CurrentBytes);
                RenderBudget accepted = new(16_000_000);
                byte[] expected = SvgConverter.ConvertToPng(scene, SvgRenderOptions.Default, accepted, CancellationToken.None).ToArray();
                Assert.Equal(0, accepted.Snapshot().CurrentBytes);
                RenderBudget rasterDenied = new(accepted.Snapshot().PeakBytes / 2);
                Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() =>
                    SvgConverter.ConvertToPng(scene, SvgRenderOptions.Default, rasterDenied, CancellationToken.None)).ErrorCode);
                Assert.Equal(0, rasterDenied.Snapshot().CurrentBytes);
                using CancellationTokenSource cancellation = new();
                cancellation.Cancel();
                Assert.Equal(cancellation.Token, Assert.ThrowsAny<OperationCanceledException>(() =>
                    SvgRootParser.Parse(input, SvgRenderLimits.Default, workDenied, cancellation.Token)).CancellationToken);
                Assert.Equal(0, workDenied.Snapshot().CurrentBytes);
                Assert.Equal(cancellation.Token, Assert.ThrowsAny<OperationCanceledException>(() =>
                    SvgConverter.ConvertToPng(scene, SvgRenderOptions.Default, accepted, cancellation.Token)).CancellationToken);
                Assert.Equal(0, accepted.Snapshot().CurrentBytes);
                Assert.Equal(expected, SvgConverter.ConvertToPng(scene, SvgRenderOptions.Default, accepted, CancellationToken.None).ToArray());
                Assert.Equal(0, accepted.Snapshot().CurrentBytes);
            }
            Assert.Equal(0, sceneBudget.Snapshot().CurrentBytes);
            Assert.Equal(unchanged, input);
        }
    }


    [Fact]
    public void Shared_map_exits_release_sources_and_rasters_on_denial_cancellation_and_retry()
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "Research", "shared-map-exit-sheet");
        foreach (string name in new[] { "reflected-basic-use", "scaled-viewport-rectangle-stroke", "scaled-stroked-pattern-content", "nonuniform-nonscaling-dashes" })
        {
            byte[] input = File.ReadAllBytes(Path.Combine(directory, name + ".svg"));
            byte[] unchanged = input.ToArray();
            RenderBudget sceneBudget = new(16_000_000);
            ParserWorkCounter work = new(100_000_000);
            using (SvgRoot scene = SvgRootParser.Parse(input, SvgRenderLimits.Default, sceneBudget, null, work, CancellationToken.None))
            {
                foreach (long limit in new[] { 1, sceneBudget.Snapshot().PeakBytes / 2 })
                {
                    RenderBudget denied = new(limit);
                    Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() =>
                        SvgRootParser.Parse(input, SvgRenderLimits.Default, denied, CancellationToken.None)).ErrorCode);
                    Assert.Equal(0, denied.Snapshot().CurrentBytes);
                }
                RenderBudget workDenied = new(16_000_000);
                Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() =>
                    SvgRootParser.Parse(input, SvgRenderLimits.Default, workDenied, null, new(work.Used - 1), CancellationToken.None)).ErrorCode);
                Assert.Equal(0, workDenied.Snapshot().CurrentBytes);
                RenderBudget accepted = new(16_000_000);
                byte[] expected = SvgConverter.ConvertToPng(scene, SvgRenderOptions.Default, accepted, CancellationToken.None).ToArray();
                Assert.Equal(0, accepted.Snapshot().CurrentBytes);
                RenderBudget rasterDenied = new(accepted.Snapshot().PeakBytes / 2);
                Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() =>
                    SvgConverter.ConvertToPng(scene, SvgRenderOptions.Default, rasterDenied, CancellationToken.None)).ErrorCode);
                Assert.Equal(0, rasterDenied.Snapshot().CurrentBytes);
                using CancellationTokenSource cancellation = new();
                cancellation.Cancel();
                Assert.Equal(cancellation.Token, Assert.ThrowsAny<OperationCanceledException>(() =>
                    SvgRootParser.Parse(input, SvgRenderLimits.Default, workDenied, cancellation.Token)).CancellationToken);
                Assert.Equal(0, workDenied.Snapshot().CurrentBytes);
                Assert.Equal(cancellation.Token, Assert.ThrowsAny<OperationCanceledException>(() =>
                    SvgConverter.ConvertToPng(scene, SvgRenderOptions.Default, accepted, cancellation.Token)).CancellationToken);
                Assert.Equal(0, accepted.Snapshot().CurrentBytes);
                Assert.Equal(expected, SvgConverter.ConvertToPng(scene, SvgRenderOptions.Default, accepted, CancellationToken.None).ToArray());
                Assert.Equal(0, accepted.Snapshot().CurrentBytes);
            }
            Assert.Equal(0, sceneBudget.Snapshot().CurrentBytes);
            Assert.Equal(unchanged, input);
        }
    }




    private static string RequiredString(JsonElement element, string name) => element.GetProperty(name).GetString() ?? throw new InvalidDataException(name + " is missing.");
}
