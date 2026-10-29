using System.Security.Cryptography;
using System.Text.Json;
using Lokad.Svg.Fonts;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;
using TextEncoding = System.Text.Encoding;

namespace Lokad.Svg.Tests;

public sealed partial class SvgTextPaintBoundsTests
{
    public static IEnumerable<object[]> FilterSourceBoundsCases()
    {
        using JsonDocument vectors = JsonDocument.Parse(File.ReadAllBytes(DataPath("filter-source-vectors.json")));
        foreach (JsonElement row in vectors.RootElement.EnumerateArray())
            yield return [row.GetProperty("id").GetString() ?? throw new InvalidDataException("Missing recording bounds id.")];
    }

    [Theory]
    [MemberData(nameof(FilterSourceBoundsCases))]
    [Trait("Layer", "Mechanisms")]
    public async Task Filter_recording_and_element_bounds_match_independent_native_owner_boxes(string id)
    {
        byte[] bytes = File.ReadAllBytes(DataPath("filter-source-vectors.json"));
        using JsonDocument capture = JsonDocument.Parse(File.ReadAllBytes(DataPath("filter-source-capture.json")));
        Assert.Equal(capture.RootElement.GetProperty("vectorSha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(bytes)));
        using JsonDocument vectors = JsonDocument.Parse(bytes);
        JsonElement row = vectors.RootElement.EnumerateArray().Single(item => item.GetProperty("id").GetString() == id);
        JsonElement expected = row.GetProperty(OperatingSystem.IsWindows() ? "windows" : "linux");
        byte[] input = TextEncoding.UTF8.GetBytes(row.GetProperty("source").GetString() ?? throw new InvalidDataException("Missing source document."));
        using SvgFontSnapshot fonts = await CreateSnapshotAsync();
        RenderBudget budget = new(8_000_000);
        using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, fonts, CancellationToken.None))
        {
            SvgSceneStorage scene = root.SceneStorage ?? throw new InvalidDataException("Missing source paint records.");
            SvgPaintBounds? recorded = null, element = null;
            foreach (SvgRecordedPaintValue paint in scene.RecordedPaintValues)
            {
                if (paint.SourceVisualBounds is {} sourceBox) recorded = Union(recorded, sourceBox);
                if (paint.ElementVisualBounds is {} elementBox) element = Union(element, elementBox);
            }
            AssertBounds(expected.GetProperty("recorded"), recorded ?? throw new InvalidDataException("Missing recording bounds."));
            AssertBounds(expected.GetProperty("element"), element ?? throw new InvalidDataException("Missing element bounds."));
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);

        static SvgPaintBounds Union(SvgPaintBounds? previous, SvgPaintBounds box)
            => previous is {} value ? new(MathF.Min(value.Left, box.Left), MathF.Min(value.Top, box.Top),
                MathF.Max(value.Right, box.Right), MathF.Max(value.Bottom, box.Bottom)) : box;

        static void AssertBounds(JsonElement expected, SvgPaintBounds actual)
            => Assert.Equal(expected.EnumerateArray().Select(value => value.GetSingle()).ToArray(),
                new float[] { actual.Left, actual.Top, actual.Right, actual.Bottom });
    }

    [Fact]
    [Trait("Layer", "Contracts")]
    public async Task Recording_scope_copy_releases_after_refusal_and_partial_failure_then_retries_and_borrows()
    {
        using SvgFontSnapshot fonts = await CreateSnapshotAsync();
        RenderBudget runBudget = new(1_000_000), metadata = new(1_000_000), denied = new(1);
        using (SvgTextRun run = CreateRun("Aj", fonts, runBudget))
        {
            SvgTextRecordingScope[] scopes = [new(0, 1, 0, 4, false), new(1, 2, 1, 2, false)];
            Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() =>
                run.RetainRecordingScopes(scopes, SvgTextRecordingStroke.None, denied, CancellationToken.None)).ErrorCode);
            Assert.Equal(0, denied.Snapshot().CurrentBytes);
            Assert.Equal(0, metadata.Snapshot().CurrentBytes);
            using CancellationTokenSource cancellation = new();
            cancellation.Cancel();
            Assert.Throws<OperationCanceledException>(() => run.RetainRecordingScopes(scopes, SvgTextRecordingStroke.None, metadata, cancellation.Token));
            Assert.Equal(0, metadata.Snapshot().CurrentBytes);
            run.RetainRecordingScopes(scopes, SvgTextRecordingStroke.None, metadata, CancellationToken.None);
            Assert.True(metadata.Snapshot().PeakBytes > 0);
            scopes[1] = scopes[1] with { StrokeWidth = 99 };
            Assert.Equal(2, run.RecordingScopes[1].StrokeWidth);
            long retained = metadata.Snapshot().CurrentBytes;
            using (runBudget.Reserve(512, SvgMemoryCategory.Parser))
            {
                OwnedParserArray<SvgTextPaintFragment> fragments = new([new(0, 1, run.Glyphs[1].Advance) { SourceStart = 1, SourceEnd = 2 }],
                    runBudget.Reserve(ParserCollectionMemory.GetArrayByteCount<SvgTextPaintFragment>(1), SvgMemoryCategory.Geometry));
                using SvgTextRun view = new(run, 1, 1, fragments, run.Style, run.DeviceMetrics, CancellationToken.None);
                Assert.Equal(new SvgTextRecordingScope(1, 2, 1, 2, false), Assert.Single(view.RecordingScopes.ToArray()));
                view.Dispose();
                view.Dispose();
            }
            Assert.Equal(retained, metadata.Snapshot().CurrentBytes);
            Assert.Equal(2, run.RecordingScopes.Length);
        }
        Assert.Equal(0, metadata.Snapshot().CurrentBytes);
        Assert.Equal(0, runBudget.Snapshot().CurrentBytes);
    }

    [Fact]
    [Trait("Layer", "Contracts")]
    public async Task Recording_box_scratch_releases_after_denial_cancellation_then_retries()
    {
        using SvgFontSnapshot fonts = await CreateSnapshotAsync();
        RenderBudget runBudget = new(1_000_000), scratch = new(1_000_000), denied = new(256);
        using (SvgTextRun run = CreateRun("Aj", fonts, runBudget))
        {
            run.RetainRecordingScopes([new(0, 2, 0, 4, false)], SvgTextRecordingStroke.None, runBudget, CancellationToken.None);
            using (SvgTextLayoutBounds.MetricCache limited = SvgTextLayoutBounds.MetricCache.Create(denied))
            {
                long retained = denied.Snapshot().CurrentBytes;
                Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() =>
                    SvgTextLayoutBounds.MeasureSourceVisual(run, [], SvgAffineTransform.Identity, limited, CancellationToken.None)).ErrorCode);
                Assert.Equal(retained, denied.Snapshot().CurrentBytes);
            }
            Assert.Equal(0, denied.Snapshot().CurrentBytes);
            using (SvgTextLayoutBounds.MetricCache cache = SvgTextLayoutBounds.MetricCache.Create(scratch))
            {
                long retained = scratch.Snapshot().CurrentBytes;
                Assert.Equal(retained, scratch.Snapshot().CurrentBytes);
                using CancellationTokenSource cancellation = new();
                cancellation.Cancel();
                Assert.Throws<OperationCanceledException>(() =>
                    SvgTextLayoutBounds.MeasureSourceVisual(run, [], SvgAffineTransform.Identity, cache, cancellation.Token));
                Assert.Equal(retained, scratch.Snapshot().CurrentBytes);
                SvgRectangle result = SvgTextLayoutBounds.MeasureSourceVisual(run, [], SvgAffineTransform.Identity, cache, CancellationToken.None);
                Assert.True(result.Width > 0 && result.Height > 0);
                Assert.True(scratch.Snapshot().PeakBytes > retained);
            }
            Assert.Equal(0, scratch.Snapshot().CurrentBytes);
        }
        Assert.Equal(0, runBudget.Snapshot().CurrentBytes);
    }

    [Fact]
    [Trait("Layer", "Contracts")]
    public async Task Prepared_filter_owners_release_after_cancellation_and_preserve_input_independence_on_fresh_retry()
    {
        using JsonDocument vectors = JsonDocument.Parse(File.ReadAllBytes(DataPath("filter-source-vectors.json")));
        JsonElement row = vectors.RootElement.EnumerateArray().Single(item => item.GetProperty("id").GetString() == "r145-path-owner-1");
        byte[] input = TextEncoding.UTF8.GetBytes(row.GetProperty("source").GetString() ?? throw new InvalidDataException("Missing replay source."));
        using SvgFontSnapshot fonts = await CreateSnapshotAsync();
        byte[] expected = SvgConverter.ConvertToPng(input, new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, fonts), CancellationToken.None).ToArray();
        SvgFontPackCatalog catalog = SvgFontPackCatalog.Parse(File.ReadAllBytes(DataPath("font-pack.json")), "r99-fonts");
        byte[] retryInput = input.ToArray();
        using MemoryStream destination = new();
        using (SvgPreparedDocument document = await SvgPreparedDocument.CreateAsync(input, catalog, new FontPrograms(),
            new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default), CancellationToken.None))
        {
            Array.Fill(input, (byte)0);
            using CancellationTokenSource cancellation = new();
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => document.ConvertToPngAsync(destination, cancellation.Token));
            Assert.Equal(0, destination.Length);
            Assert.Null(document.RetainedSceneGeometries);
            Assert.Throws<ObjectDisposedException>(() => document.ConvertToPng(CancellationToken.None));
        }
        using (SvgPreparedDocument retry = await SvgPreparedDocument.CreateAsync(retryInput, catalog, new FontPrograms(),
            new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default), CancellationToken.None))
        {
            Array.Fill(retryInput, (byte)0);
            Assert.Equal(expected.Length, await retry.ConvertToPngAsync(destination, CancellationToken.None));
            Assert.Equal(expected, destination.ToArray());
        }
        Assert.True(destination.CanWrite);
    }
}
