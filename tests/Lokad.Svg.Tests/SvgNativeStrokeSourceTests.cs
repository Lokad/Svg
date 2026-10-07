using TextEncoding = System.Text.Encoding;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgNativeStrokeSourceTests
{
    [Theory]
    [InlineData("native-stroke-source-ring-161.svg")]
    [InlineData("native-stroke-source-clipped-cubic-161.svg")]
    [InlineData("stroke-duplicate-diagonal-16x16.svg")]
    public async Task Source_outline_mapping_preserves_all_six_outputs(string source)
    {
        byte[] input = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", source));
        byte[] expected = SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None).ToArray();
        byte[] destination = new byte[expected.Length];
        Assert.Equal(expected.Length, SvgConverter.ConvertToPng(input, destination, SvgRenderOptions.Default, CancellationToken.None));
        Assert.Equal(expected, destination);
        using MemoryStream stream = new();
        Assert.Equal(expected.Length, SvgConverter.ConvertToPng(input, stream, SvgRenderOptions.Default, CancellationToken.None));
        Assert.Equal(expected, stream.ToArray());
        Assert.Equal(expected, (await SvgConverter.ConvertToPngAsync(input, SvgRenderOptions.Default, CancellationToken.None)).ToArray());
        Assert.Equal(expected.Length, await SvgConverter.ConvertToPngAsync(input, destination, SvgRenderOptions.Default, CancellationToken.None));
        Assert.Equal(expected, destination);
        using MemoryStream asyncStream = new();
        Assert.Equal(expected.Length, await SvgConverter.ConvertToPngAsync(input, asyncStream, SvgRenderOptions.Default, CancellationToken.None));
        Assert.Equal(expected, asyncStream.ToArray());
    }

    [Fact]
    public void Copied_and_transferred_outlines_survive_the_original_scene_and_release_both_owners()
    {
        RenderBudget sceneBudget = new(1_000_000), copyBudget = new(1_000_000), preparationBudget = new(1_000_000);
        SvgRoot scene = SvgRootParser.Parse(Input(), SvgRenderLimits.Default, sceneBudget, null, new(1_000_000), CancellationToken.None);
        try
        {
            SvgAuthoredPath centerline = Assert.IsType<SvgAuthoredPath>(Assert.Single(scene.Geometries).AuthoredPath);
            SvgAuthoredPath outline = Assert.IsType<SvgAuthoredPath>(centerline.StrokeOutline);
            SvgAuthoredPathCommand[] original = outline.Commands.ToArray();
            SvgAffineTransform placement = new(-1.125, 0, 0, 1.125, 160.375, -.625);
            using SvgAuthoredPath copied = centerline.Copy(placement, copyBudget, new(1_000_000), CancellationToken.None);
            SvgAuthoredPath copiedOutline = Assert.IsType<SvgAuthoredPath>(copied.StrokeOutline);
            Assert.NotSame(outline, copiedOutline);
            Assert.Equal(placement.ComposeChecked(outline.SceneMap), copiedOutline.SceneMap);
            using SvgAuthoredPath transferred = copied.Transfer();
            copied.Dispose();
            scene.Dispose();
            Assert.Equal(0, sceneBudget.Snapshot().CurrentBytes);
            Assert.True(copyBudget.Snapshot().CurrentBytes > 0);
            Assert.Equal(original, copiedOutline.Commands.ToArray());
            Assert.Same(copiedOutline, transferred.StrokeOutline);
            Assert.Throws<InvalidOperationException>(() => transferred.PrepareSource(SvgAffineTransform.Identity, false, preparationBudget, new(1_000_000), CancellationToken.None));
            using SvgPreparedPathContours prepared = copiedOutline.PrepareSource(SvgAffineTransform.Identity, false, preparationBudget, new(1_000_000), CancellationToken.None);
            Assert.True(prepared.Points.Length > 3);
            Assert.NotEmpty(prepared.QuadSpans);
        }
        finally { scene.Dispose(); }
        Assert.Equal(0, copyBudget.Snapshot().CurrentBytes);
        Assert.Equal(0, preparationBudget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Partial_outline_copies_release_storage_after_memory_work_or_cancellation_denial()
    {
        RenderBudget sceneBudget = new(1_000_000), measuredBudget = new(1_000_000);
        using (SvgRoot scene = SvgRootParser.Parse(Input(), SvgRenderLimits.Default, sceneBudget, null, new(1_000_000), CancellationToken.None))
        {
            SvgAuthoredPath source = Assert.IsType<SvgAuthoredPath>(Assert.Single(scene.Geometries).AuthoredPath);
            ParserWorkCounter measuredWork = new(1_000_000);
            using (source.Copy(SvgAffineTransform.Identity, measuredBudget, measuredWork, CancellationToken.None)) { }
            Assert.Equal(0, measuredBudget.Snapshot().CurrentBytes);
            RenderBudget tight = new(measuredBudget.Snapshot().PeakBytes - 1);
            SvgException memory = Assert.Throws<SvgException>(() => source.Copy(SvgAffineTransform.Identity, tight, new(1_000_000), CancellationToken.None));
            Assert.Equal(SvgErrorCode.LimitExceeded, memory.ErrorCode);
            Assert.Equal(0, tight.Snapshot().CurrentBytes);
            RenderBudget workBudget = new(1_000_000);
            SvgException work = Assert.Throws<SvgException>(() => source.Copy(SvgAffineTransform.Identity, workBudget, new(measuredWork.Used - 1), CancellationToken.None));
            Assert.Equal(SvgErrorCode.LimitExceeded, work.ErrorCode);
            Assert.Equal(0, workBudget.Snapshot().CurrentBytes);
            using CancellationTokenSource cancellation = new();
            cancellation.Cancel();
            OperationCanceledException error = Assert.Throws<OperationCanceledException>(() => source.Copy(SvgAffineTransform.Identity, workBudget, new(1_000_000), cancellation.Token));
            Assert.Equal(cancellation.Token, error.CancellationToken);
            Assert.Equal(0, workBudget.Snapshot().CurrentBytes);
            using SvgAuthoredPath retry = source.Copy(SvgAffineTransform.Identity, workBudget, new(1_000_000), CancellationToken.None);
            Assert.NotNull(retry.StrokeOutline);
        }
        Assert.Equal(0, sceneBudget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Source_outline_scene_storage_is_bounded_and_released_after_partial_parse()
    {
        byte[] input = Input();
        RenderBudget measured = new(1_000_000);
        ParserWorkCounter work = new(1_000_000);
        using (SvgRootParser.Parse(input, SvgRenderLimits.Default, measured, null, work, CancellationToken.None)) { }
        Assert.Equal(0, measured.Snapshot().CurrentBytes);
        RenderBudget tight = new(measured.Snapshot().PeakBytes - 1);
        SvgException memory = Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, tight, null, new(1_000_000), CancellationToken.None));
        Assert.Equal(SvgErrorCode.LimitExceeded, memory.ErrorCode);
        Assert.Equal(0, tight.Snapshot().CurrentBytes);
        RenderBudget workBudget = new(1_000_000);
        SvgException denied = Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, workBudget, null, new(work.Used - 1), CancellationToken.None));
        Assert.Equal(SvgErrorCode.LimitExceeded, denied.ErrorCode);
        Assert.Equal(0, workBudget.Snapshot().CurrentBytes);
    }

    private static byte[] Input() => TextEncoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='80' height='60' viewBox='0 0 160 120'>"
        + "<g transform='matrix(1.713,0,0,1.713,-2048.375,-1024.125)'><path d='M1205.625 615.375 Q1225.75 597.125 1248.375 620.625 L1252.625 637.375' "
        + "fill='none' stroke='#397ba4' stroke-width='4.375' stroke-linecap='round' stroke-linejoin='round'/></g></svg>");
}
