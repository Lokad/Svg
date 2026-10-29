using System.Text;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgPatternRecordingTransformTests
{
    public static IEnumerable<object[]> NativePixels()
    {
        foreach (string host in new[] { "direct", "leaf", "group", "symbol" })
        foreach ((string effect, byte[] pixel) in new[]
        {
            ("clip", new byte[] { 33, 0, 222, 226 }),
            ("mask", new byte[] { 33, 0, 223, 169 }),
            ("opacity", new byte[] { 32, 0, 223, 113 }),
            ("outer-opacity", new byte[] { 0, 0, 255, 128 })
        })
            yield return [host, effect, pixel];
    }

    [Theory]
    [MemberData(nameof(NativePixels))]
    public void Pattern_sampling_retains_native_recording_boundaries(string host, string effect, byte[] pixel)
    {
        const string pattern = "<pattern id='p' patternUnits='userSpaceOnUse' width='8' height='8'><rect width='4' height='8' fill='red'/><rect x='4' width='4' height='8' fill='blue'/></pattern>";
        const string rectangle = "<rect x='4' y='4' width='20' height='20' fill='url(#p)'/>";
        string source = host switch
        {
            "leaf" => rectangle.Replace("<rect ", "<rect id='source' ", StringComparison.Ordinal),
            "group" => "<g id='source'>" + rectangle + "</g>",
            "symbol" => "<symbol id='source' viewBox='0 0 32 32'>" + rectangle + "</symbol>",
            _ => ""
        };
        string ink = host == "direct" ? rectangle : host == "symbol" ? "<use href='#source' width='32' height='32'/>" : "<use href='#source'/>";
        string attributes = effect switch
        {
            "clip" => " clip-path='url(#c)'",
            "mask" => " mask='url(#m)'",
            "opacity" => " opacity='.5'",
            _ => ""
        };
        string body = "<g transform='scale(1000)'" + attributes + "><g transform='scale(.001)'>" + ink + "</g></g>";
        if (effect == "outer-opacity") body = "<g opacity='.5'>" + body + "</g>";
        string definitions = "<defs>" + pattern + "<clipPath id='c'><rect width='.032' height='.032'/></clipPath><mask id='m' mask-type='alpha' maskUnits='userSpaceOnUse' x='0' y='0' width='32' height='32'><rect width='32' height='32' fill='white' fill-opacity='.75'/></mask>" + source + "</defs>";
        foreach (bool forward in new[] { false, true })
        foreach (bool authoredViewport in new[] { false, true })
        {
            string dimensions = authoredViewport ? "width='48' height='48' viewBox='0 0 32 32'" : "width='32' height='32'";
            byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' " + dimensions + ">" + (forward ? body + definitions : definitions + body) + "</svg>");
            RenderBudget budget = new(8_000_000);
            using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None))
            using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root,
                new SvgRenderOptions(null, authoredViewport ? 1 : 1.5, SvgColor.Transparent, SvgRenderLimits.Default), budget, CancellationToken.None))
                Assert.Equal(pixel, surface.Pixels.AsSpan((6 * 48 + 6) * 4, 4).ToArray());
            Assert.Equal(0, budget.Snapshot().CurrentBytes);
        }
    }

    [Fact]
    public void Authored_viewports_and_external_output_scale_preserve_their_distinct_native_picture_matrices()
    {
        foreach (bool authoredViewport in new[] { false, true })
        {
            string dimensions = authoredViewport ? "width='48' height='48' viewBox='0 0 32 32'" : "width='32' height='32'";
            byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' " + dimensions + "><defs><pattern id='p' patternUnits='userSpaceOnUse' width='8' height='8'><rect width='4' height='8' fill='red'/><rect x='4' width='4' height='8' fill='blue'/></pattern></defs><g transform='scale(1000)'><g transform='scale(.001)'><rect x='4' y='4' width='20' height='20' fill='url(#p)'/></g></g></svg>");
            RenderBudget budget = new(8_000_000);
            using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None))
            using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root,
                new SvgRenderOptions(null, authoredViewport ? 1 : 1.5, SvgColor.Transparent, SvgRenderLimits.Default), budget, CancellationToken.None))
                Assert.Equal(authoredViewport ? new byte[] { 33, 0, 222, 226 } : new byte[] { 0, 0, 255, 255 }, surface.Pixels.AsSpan((6 * 48 + 6) * 4, 4).ToArray());
            Assert.Equal(0, budget.Snapshot().CurrentBytes);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Deep_recording_chains_release_storage_on_denial_cancellation_and_retry(bool forward)
    {
        byte[] input = DeepInput(forward), unchanged = input.ToArray();
        RenderBudget parser = new(8_000_000);
        using (RenderBudget.BudgetReservation borrowed = parser.Reserve(73, SvgMemoryCategory.Scene))
        {
            using (SvgRootParser.Parse(input, SvgRenderLimits.Default, parser, null, CancellationToken.None)) { }
            Assert.Equal(73, parser.Snapshot().CurrentBytes);
            Assert.Equal(73, parser.Snapshot().CurrentBytes);
            RenderBudget deniedParse = new(parser.Snapshot().PeakBytes - 74);
            Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() =>
                SvgRootParser.Parse(input, SvgRenderLimits.Default, deniedParse, CancellationToken.None)).ErrorCode);
            Assert.Equal(0, deniedParse.Snapshot().CurrentBytes);
            using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, parser, CancellationToken.None))
            {
                SvgRecordedTransform recording = Recording(root);
                Assert.Equal(80, recording.Count);
                SvgAffineTransform target = root.Geometries.Single(geometry => geometry.Kind == SvgGeometryKind.PatternFill).Pattern.PaintMatrices?.Target
                    ?? throw new InvalidOperationException("Expected retained picture matrices.");
                SvgShaderMatrix output = new(1.5f, 0, 0, 0, 1.5f, 0);
                RenderBudget deniedScratch = new(ParserCollectionMemory.GetArrayByteCount<SvgAffineTransform>(recording.Count) - 1);
                Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() =>
                    recording.TryMap(output, SvgAffineTransform.Identity, target, deniedScratch, CancellationToken.None, out _)).ErrorCode);
                Assert.Equal(0, deniedScratch.Snapshot().CurrentBytes);
                RenderBudget scratch = new(8_000_000);
                Assert.Equal(0, scratch.Snapshot().CurrentBytes);

                SvgRenderOptions options = new(null, 1.5, SvgColor.Transparent, SvgRenderLimits.Default);
                RenderBudget raster = new(8_000_000);
                byte[] expected;
                using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, options, raster, CancellationToken.None)) expected = surface.Pixels.ToArray();
                Assert.Equal(0, raster.Snapshot().CurrentBytes);
                RenderBudget deniedRaster = new(raster.Snapshot().PeakBytes - 1);
                Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() =>
                    SvgConverter.Rasterize(root, options, deniedRaster, CancellationToken.None)).ErrorCode);
                Assert.Equal(0, deniedRaster.Snapshot().CurrentBytes);
                using CancellationTokenSource cancellation = new();
                bool observed = false;
                using (BackgroundInitializationProbe.Arm(cancellation.Cancel, () => observed = true))
                    Assert.Equal(cancellation.Token, Assert.ThrowsAny<OperationCanceledException>(() =>
                        SvgConverter.Rasterize(root, new(null, 1.5, new SvgColor(255, 255, 255, 255), SvgRenderLimits.Default), raster, cancellation.Token)).CancellationToken);
                Assert.True(cancellation.IsCancellationRequested);
                Assert.True(observed);
                Assert.Equal(0, raster.Snapshot().CurrentBytes);
                using (SvgConverter.RasterizedSurface retry = SvgConverter.Rasterize(root, options, raster, CancellationToken.None)) Assert.Equal(expected, retry.Pixels);
                Assert.Equal(0, raster.Snapshot().CurrentBytes);
            }
            Assert.Equal(73, parser.Snapshot().CurrentBytes);
        }
        Assert.Equal(0, parser.Snapshot().CurrentBytes);
        Assert.Equal(unchanged, input);
    }

    [Fact]
    public void Shared_recording_nodes_keep_output_matrices_local_to_each_draw()
    {
        RenderBudget parser = new(8_000_000);
        using (SvgRoot root = SvgRootParser.Parse(DeepInput(false), SvgRenderLimits.Default, parser, CancellationToken.None))
        {
            SvgRecordedTransform recording = Recording(root);
            SvgAffineTransform target = root.Geometries.Single(geometry => geometry.Kind == SvgGeometryKind.PatternFill).Pattern.PaintMatrices?.Target
                ?? throw new InvalidOperationException("Expected retained picture matrices.");
            Parallel.For(0, 32, index =>
            {
                float scale = 1 + index / 8f;
                SvgShaderMatrix expected = new(scale, 0, .25f, 0, scale, .75f);
                RenderBudget raster = new(8_000_000);
                Assert.True(recording.TryMap(expected, SvgAffineTransform.Identity, target, raster, CancellationToken.None, out SvgShaderMatrix actual));
                Assert.Equal(expected, actual);
                Assert.Equal(0, raster.Snapshot().CurrentBytes);
            });
        }
        Assert.Equal(0, parser.Snapshot().CurrentBytes);
    }

    private static SvgRecordedTransform Recording(SvgRoot root)
    {
        SvgSceneStorage storage = root.SceneStorage ?? throw new InvalidOperationException("Expected an owned scene.");
        return storage.RecordedPaintValues[root.Geometries.Single(geometry => geometry.Kind == SvgGeometryKind.PatternFill).RecordedPaint.Identity - 1].RecordingTransform
            ?? throw new InvalidOperationException("Expected retained recording boundaries.");
    }

    private static byte[] DeepInput(bool forward)
    {
        const string definitions = "<defs><pattern id='p' patternUnits='userSpaceOnUse' width='8' height='8'><rect width='4' height='8' fill='red'/><rect x='4' width='4' height='8' fill='blue'/></pattern><clipPath id='c'><rect width='32' height='32'/></clipPath></defs>";
        StringBuilder body = new();
        for (int index = 0; index < 80; index++) body.Append("<g clip-path='url(#c)' transform='scale(").Append(index % 2 == 0 ? "2" : ".5").Append(")'>");
        body.Append("<rect x='4' y='4' width='20' height='20' fill='url(#p)'/>");
        for (int index = 0; index < 80; index++) body.Append("</g>");
        return System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'>" + (forward ? body + definitions : definitions + body) + "</svg>");
    }
}
