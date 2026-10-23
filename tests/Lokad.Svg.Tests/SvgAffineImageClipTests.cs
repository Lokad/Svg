using TextEncoding = System.Text.Encoding;
using Lokad.Svg.Encoding;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgAffineImageClipTests
{
    public static IEnumerable<object[]> SymbolFringes()
    {
        // Independently measured native bitmap/layer alpha, including full opacity.
        (byte Alpha, string Opacity, byte Expected)[] alphas =
        [
            (127, "1", 127), (127, ".5", 63), (127, ".75", 95), (127, ".9", 114),
            (191, "1", 191), (191, ".5", 96), (191, ".75", 143), (191, ".9", 172),
            (255, "1", 255), (255, ".5", 128), (255, ".75", 191), (255, ".9", 230),
        ];
        foreach ((byte alpha, string opacity, byte expected) in alphas)
        foreach (bool uniform in new[] { false, true })
        foreach (string map in new[] { "rotate(15 16 16)", "rotate(45 16 16)", "matrix(1 .2 .3 1 -4 -3)" })
        foreach (bool forward in new[] { false, true }) yield return [alpha, opacity, expected, uniform, map, forward];
    }

    [Theory]
    [MemberData(nameof(SymbolFringes))]
    public void Symbol_images_keep_native_run_coverage_at_both_fringe_extremes(byte alpha, string opacity, byte expected, bool uniform, string map, bool forward)
    {
        byte[] input = Document(Image(alpha, uniform), map, "symbol", opacity, forward);
        RenderBudget budget = new(1_000_000);
        using SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None);
        using SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, SvgRenderOptions.Default, budget, CancellationToken.None);
        (int x, int y, int oppositeX, int oppositeY) = map switch
        {
            "rotate(15 16 16)" => (9, 5, 22, 26),
            "rotate(45 16 16)" => (1, 13, 30, 18),
            _ => (28, 26, 28, 26),
        };
        Assert.Equal(0, surface.Pixels[(y * 32 + x) * 4 + 3]);
        Assert.Equal(0, surface.Pixels[(oppositeY * 32 + oppositeX) * 4 + 3]);
        int inside = map.StartsWith("matrix", StringComparison.Ordinal) ? (21 * 32 + 8) * 4 : (16 * 32 + 16) * 4;
        Assert.Equal(expected, surface.Pixels[inside + 3]);
    }

    [Theory]
    [InlineData("direct", "rotate(15 16 16)", 9, 5, 2)]
    [InlineData("group", "rotate(15 16 16)", 9, 5, 2)]
    [InlineData("use", "rotate(15 16 16)", 9, 5, 2)]
    [InlineData("direct", "rotate(45 16 16)", 1, 13, 4)]
    [InlineData("group", "rotate(45 16 16)", 1, 13, 4)]
    [InlineData("use", "rotate(45 16 16)", 1, 13, 4)]
    [InlineData("direct", "matrix(1 .2 .3 1 -4 -3)", 28, 26, 2)]
    [InlineData("group", "matrix(1 .2 .3 1 -4 -3)", 28, 26, 2)]
    [InlineData("use", "matrix(1 .2 .3 1 -4 -3)", 28, 26, 2)]
    public void Other_native_image_routes_retain_their_independent_small_coverage(string consumer, string map, int x, int y, byte expected)
    {
        byte[] input = Document(Image(255, true), map, consumer, "1", false);
        RenderBudget budget = new(1_000_000);
        using SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None);
        using SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, SvgRenderOptions.Default, budget, CancellationToken.None);
        Assert.Equal(expected, surface.Pixels[(y * 32 + x) * 4 + 3]);
        if (map.StartsWith("matrix", StringComparison.Ordinal)) Assert.Equal(251, surface.Pixels[(21 * 32 + 8) * 4 + 3]);
    }

    [Theory]
    [InlineData(false, "symbol")]
    [InlineData(true, "symbol")]
    [InlineData(false, "use")]
    [InlineData(true, "use")]
    public void Image_coverage_releases_scratch_after_denied_memory_work_and_cancellation(bool uniform, string consumer)
    {
        byte[] input = Document(Image(191, uniform), "rotate(15 16 16)", consumer, ".75", true);
        byte[] unchanged = input.ToArray();
        RenderBudget budget = new(1_000_000);
        long work;
        using (RenderBudget.BudgetReservation borrowed = budget.Reserve(73, SvgMemoryCategory.Scene))
        {
            using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None))
            {
                using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, SvgRenderOptions.Default, budget, CancellationToken.None))
                {
                    work = surface.WorkUnits;
                }
                SvgRenderLimits limits = SvgRenderLimits.Default;
                SvgRenderLimits denied = new(limits.MaximumAccountedBytes, limits.MaximumInputBytes, limits.MaximumDimension, limits.MaximumPixels,
                    limits.MaximumEncodedPngBytes, limits.MaximumNodes, limits.MaximumPathCommands, checked((int)work - 1), limits.MaximumXmlDepth, limits.MaximumAttributesPerElement);
                SvgRenderOptions options = new(null, 1, SvgColor.Transparent, denied);
                long retained = budget.Snapshot().CurrentBytes;
                Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgConverter.Rasterize(root, options, budget, CancellationToken.None)).ErrorCode);
                Assert.Equal(retained, budget.Snapshot().CurrentBytes);
                using CancellationTokenSource cancellation = new(); cancellation.Cancel();
                Assert.Equal(cancellation.Token, Assert.ThrowsAny<OperationCanceledException>(() => SvgConverter.Rasterize(root, SvgRenderOptions.Default, budget, cancellation.Token)).CancellationToken);
                Assert.Equal(retained, budget.Snapshot().CurrentBytes);
                using SvgConverter.RasterizedSurface retry = SvgConverter.Rasterize(root, SvgRenderOptions.Default, budget, CancellationToken.None);
            }
            Assert.Equal(73, budget.Snapshot().CurrentBytes);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        RenderBudget deniedMemory = new(1_000_000);
        using (RenderBudget.BudgetReservation borrowed = deniedMemory.Reserve(73, SvgMemoryCategory.Scene))
        {
            using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, deniedMemory, CancellationToken.None))
            using (RenderBudget.BudgetReservation pressure = deniedMemory.Reserve(deniedMemory.AvailableBytes - 32 * 32 * 4, SvgMemoryCategory.Surface))
            {
                // Leave room for the output, denying subsequent coverage scratch.
                long retained = deniedMemory.Snapshot().CurrentBytes;
                Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgConverter.Rasterize(root, SvgRenderOptions.Default, deniedMemory, CancellationToken.None)).ErrorCode);
                Assert.Equal(retained, deniedMemory.Snapshot().CurrentBytes);
            }
            Assert.Equal(73, deniedMemory.Snapshot().CurrentBytes);
        }
        Assert.Equal(0, deniedMemory.Snapshot().CurrentBytes);
        Assert.Equal(unchanged, input);
    }

    private static string Image(byte alpha, bool uniform)
    {
        byte[] pixels = [255, 0, 0, alpha, 255, 0, 0, alpha, 255, 0, 0, alpha, 255, 0, 0, alpha];
        if (!uniform) { pixels[12] = 0; pixels[13] = 80; pixels[14] = 255; }
        return "<image x='4' y='8' width='24' height='16' preserveAspectRatio='none' href='data:image/png;base64," + Convert.ToBase64String(PngEncoder.Encode(2, 2, pixels, CancellationToken.None)) + "'/>";
    }

    private static byte[] Document(string image, string map, string consumer, string opacity, bool forward)
    {
        string body;
        if (consumer is "use" or "symbol")
        {
            string source = consumer == "symbol" ? "<symbol id='s'>" + image + "</symbol>" : image.Replace("/>", " id='s'/>", StringComparison.Ordinal);
            string definitions = "<defs>" + source + "</defs>";
            string use = "<use href='#s' transform='" + map + "' opacity='" + opacity + "'/>";
            body = forward ? use + definitions : definitions + use;
        }
        else body = consumer == "group" ? "<g transform='" + map + "' opacity='" + opacity + "'>" + image + "</g>"
            : image.Replace("/>", " transform='" + map + "' opacity='" + opacity + "'/>", StringComparison.Ordinal);
        return TextEncoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'>" + body + "</svg>");
    }
}
