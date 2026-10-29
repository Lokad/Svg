using TextEncoding = System.Text.Encoding;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgPointListSourceTests
{
    [Theory]
    [InlineData("fractional-rectangle-polygon-37.svg")]
    [InlineData("fractional-rectangle-polygon-137.svg")]
    public async Task Fractional_point_lists_preserve_all_six_output_paths(string name)
    {
        byte[] input = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", name));
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
    public void Point_list_instance_storage_survives_the_original_scene_and_releases_on_disposal()
    {
        RenderBudget sceneBudget = new RenderBudget(new HostMemoryBudget(1_000_000)), copyBudget = new RenderBudget(new HostMemoryBudget(1_000_000)), outputBudget = new RenderBudget(new HostMemoryBudget(1_000_000));
        SvgRoot scene = SvgRootParser.Parse(Input(), SvgRenderLimits.Default, sceneBudget, null, CancellationToken.None);
        try
        {
            SvgAuthoredPath source = Assert.IsType<SvgAuthoredPath>(Assert.Single(scene.Geometries).AuthoredPath);
            SvgAuthoredPathCommand[] commands = source.Commands.ToArray();
            using SvgAuthoredPath copy = source.Copy(new(-1.125, 0, 0, 1.125, 80.375, -.625), copyBudget, CancellationToken.None);
            scene.Dispose();
            Assert.Equal(0, sceneBudget.Snapshot().CurrentBytes);
            Assert.Equal(commands, copy.Commands.ToArray());
            using SvgPreparedPathContours prepared = copy.PrepareSource(SvgAffineTransform.Identity, false, outputBudget, CancellationToken.None);
            Assert.True(prepared.Points.Length >= 4);
        }
        finally { scene.Dispose(); }
        Assert.Equal(0, copyBudget.Snapshot().CurrentBytes);
        Assert.Equal(0, outputBudget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Partial_point_list_source_retention_releases_memory_on_memory_and_cancellation_denial()
    {
        RenderBudget measured = new RenderBudget(new HostMemoryBudget(1_000_000));
        using (SvgRootParser.Parse(Input(), SvgRenderLimits.Default, measured, null, CancellationToken.None)) { }
        Assert.Equal(0, measured.Snapshot().CurrentBytes);
        RenderBudget tight = new RenderBudget(new HostMemoryBudget(measured.Snapshot().PeakBytes - 1));
        SvgException memory = Assert.Throws<SvgException>(() => SvgRootParser.Parse(Input(), SvgRenderLimits.Default, tight, null, CancellationToken.None));
        Assert.Equal(SvgErrorCode.LimitExceeded, memory.ErrorCode);
        Assert.Equal(0, tight.Snapshot().CurrentBytes);
        RenderBudget workBudget = new RenderBudget(new HostMemoryBudget(1_000_000));
        Assert.Equal(0, workBudget.Snapshot().CurrentBytes);
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        OperationCanceledException canceled = Assert.Throws<OperationCanceledException>(() => SvgRootParser.Parse(Input(), SvgRenderLimits.Default, workBudget, null, cancellation.Token));
        Assert.Equal(cancellation.Token, canceled.CancellationToken);
        Assert.Equal(0, workBudget.Snapshot().CurrentBytes);
    }

    private static byte[] Input() => TextEncoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='137' height='83' viewBox='0 0 80 48'><g transform='translate(.375 .625)'>"
        + "<polygon points='3.125,3.375 29.625,3.375 29.625,9.875 3.125,9.875' fill='#2876b3'/></g></svg>");
}
