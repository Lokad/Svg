using System.Text.Json;
using Lokad.Svg.Fonts;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

[Trait("Layer", "Contracts")]
public sealed class SvgOrderedClipOwnershipTests
{
    [Fact]
    public void Ordered_union_borrows_sources_and_releases_scratch_after_denial_cancellation_and_retry()
    {
        RenderBudget sources = new(1_000_000), scratch = new(1_000_000);
        using (SvgAuthoredPath first = SvgOrderedClipTests.Rectangle(8, 20, sources))
        using (SvgAuthoredPath second = SvgOrderedClipTests.Rectangle(16, 24, sources))
        using (SvgAuthoredPath third = SvgOrderedClipTests.Rectangle(12, 20, sources))
        {
            SvgClipChild[] children = [new(first, false, SvgClipPaintKind.Rectangle), new(second, false, SvgClipPaintKind.Rectangle), new(third, false, SvgClipPaintKind.Rectangle)];
            SvgAuthoredPathCommand[][] before = children.Select(child => child.Path.Commands.ToArray()).ToArray();
            ParserWorkCounter work = new(1_000_000);
            SvgAuthoredPathCommand[] expected;
            using (SvgAuthoredPath result = SvgClipUnion.ResolveOrdered(children, scratch, work, CancellationToken.None).Path)
                expected = result.Commands.ToArray();
            Assert.Equal(0, scratch.Snapshot().CurrentBytes);
            RenderBudget denied = new(scratch.Snapshot().PeakBytes - 1);
            Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgClipUnion.ResolveOrdered(children, denied, new(1_000_000), CancellationToken.None)).ErrorCode);
            Assert.Equal(0, denied.Snapshot().CurrentBytes);
            foreach (long limit in new[] { work.Used / 2, work.Used - 1 })
            {
                Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgClipUnion.ResolveOrdered(children, scratch, new(limit), CancellationToken.None)).ErrorCode);
                Assert.Equal(0, scratch.Snapshot().CurrentBytes);
            }
            using CancellationTokenSource cancellation = new();
            cancellation.Cancel();
            Assert.Equal(cancellation.Token, Assert.ThrowsAny<OperationCanceledException>(() =>
                SvgClipUnion.ResolveOrdered(children, scratch, new(1_000_000), cancellation.Token)).CancellationToken);
            Assert.Equal(0, scratch.Snapshot().CurrentBytes);
            using (SvgAuthoredPath retry = SvgClipUnion.ResolveOrdered(children, scratch, new(1_000_000), CancellationToken.None).Path)
                Assert.Equal(expected, retry.Commands.ToArray());
            for (int index = 0; index < children.Length; index++) Assert.Equal(before[index], children[index].Path.Commands.ToArray());
            Assert.Equal(0, scratch.Snapshot().CurrentBytes);
        }
        Assert.Equal(0, sources.Snapshot().CurrentBytes);
    }

    [Theory]
    [InlineData("contained-count-0424-candidate")]
    [InlineData("fallback-opacity")]
    [InlineData("fallback-object-box")]
    public void Painted_clip_scope_paths_release_after_refusal_cancellation_and_retry(string identity)
    {
        string folder = Path.Combine(AppContext.BaseDirectory, "TestData", "ordered-clip-coverage");
        using JsonDocument capture = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(folder, "capture.json")));
        JsonElement row = capture.RootElement.GetProperty("cases").EnumerateArray().Single(item => item.GetProperty("id").GetString() == identity);
        byte[] input = File.ReadAllBytes(Path.Combine(folder, row.GetProperty("input").GetString() ?? throw new InvalidDataException()));
        string expected = row.GetProperty("platforms").GetProperty(OperatingSystem.IsWindows() ? "windows" : "linux")
            .GetProperty("rgbaSha256").GetString() ?? throw new InvalidDataException();
        SvgRasterOwnershipAssertions.ParsesAndReleasesAfterFailures(input, SvgRenderOptions.Default, 2_000_000, 1, expected);
    }

    [Fact]
    public async Task Prepared_use_replay_releases_cancelled_document_and_retries_with_new_owner()
    {
        byte[] input = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData/ordered-clip-coverage/inputs/fallback-use.svg"));
        byte[] unchanged = input.ToArray();
        byte[] expected = SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None).ToArray();
        SvgFontPackCatalog catalog = SvgFontPackCatalog.Parse(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory,
            "TestData/text-node-boundaries/font-pack.json")), "public-noto-sans");
        using SvgPreparedDocument cancelled = await SvgPreparedDocument.CreateAsync(input, catalog, new NoFonts(), SvgRenderOptions.Default, CancellationToken.None);
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        Assert.Equal(cancellation.Token, Assert.ThrowsAny<OperationCanceledException>(() => cancelled.ConvertToPng(cancellation.Token)).CancellationToken);
        Assert.Throws<ObjectDisposedException>(() => cancelled.ConvertToPng(CancellationToken.None));
        using SvgPreparedDocument document = await SvgPreparedDocument.CreateAsync(input, catalog, new NoFonts(), SvgRenderOptions.Default, CancellationToken.None);
        using MemoryStream stream = new();
        Assert.Equal(expected.Length, await document.ConvertToPngAsync(stream, CancellationToken.None));
        Assert.True(stream.CanWrite);
        Assert.Equal(expected, stream.ToArray());
        Assert.Equal(unchanged, input);
    }

    private sealed class NoFonts : ISvgFontProgramSourceProvider
    {
        public ValueTask<ISvgFontProgramSource?> GetSourceAsync(SvgFontPackFile file, CancellationToken cancellationToken)
            => throw new InvalidOperationException("Font-free clip replay must not request font programs.");
    }
}
