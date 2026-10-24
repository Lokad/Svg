using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgInheritedClipIsolationTests
{
    [Fact]
    public void Forward_clip_layers_rebuild_sibling_commands_with_linear_parser_work()
    {
        long small = Measure(32), large = Measure(512);
        Assert.True(large < small * 18, "Sixteen times as many sibling scopes must keep linear parser work.");

        static long Measure(int count)
        {
            System.Text.StringBuilder source = new("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'>");
            for (int index = 0; index < count; index++)
                source.Append("<g clip-path='url(#c)'><g opacity='.5'><rect width='24' height='24' fill='red'/><rect x='1' y='1' width='24' height='24' fill='blue'/></g></g><g><rect width='2' height='2' fill='green'/></g>");
            source.Append("<defs><clipPath id='c'><rect x='5.25' y='3.75' width='19.5' height='22'/></clipPath></defs></svg>");
            RenderBudget budget = new(40_000_000);
            ParserWorkCounter work = new(SvgRenderLimits.Default.MaximumWorkUnits);
            using (SvgRootParser.Parse(System.Text.Encoding.UTF8.GetBytes(source.ToString()), SvgRenderLimits.Default, budget, null, work, CancellationToken.None)) { }
            Assert.Equal(0, budget.Snapshot().CurrentBytes);
            return work.Used;
        }
    }

    [Theory]
    [InlineData(true, ".5", 191, 64, 96)]
    [InlineData(false, ".5", 195, 60, 102)]
    [InlineData(true, ".75", 191, 64, 143)]
    [InlineData(false, ".75", 195, 60, 152)]
    public void Inherited_and_inner_clips_keep_their_distinct_native_edge_colors(bool outer, string opacity, byte red, byte blue, byte alpha)
    {
        const string definitions = "<defs><clipPath id='c'><rect x='5.25' y='3.75' width='19.5' height='22'/></clipPath></defs>";
        const string paints = "<rect x='4' y='4' width='20' height='20' fill='red'/><rect x='3' y='3' width='22' height='22' fill='blue' fill-opacity='.25'/>";
        foreach (bool forward in new[] { false, true })
        foreach (string container in new[] { "g", "a" })
        {
            string clip = "<" + container + " clip-path='url(#c)'>", group = "<g opacity='" + opacity + "'>";
            string body = (outer ? clip + group : group + clip) + paints + (outer ? "</g></" + container + ">" : "</" + container + "></g>");
            byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'>" + (forward ? body + definitions : definitions + body) + "<rect width='2' height='2' fill='green'/></svg>");
            RenderBudget budget = new(1_000_000);
            using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None))
            using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, SvgRenderOptions.Default, budget, CancellationToken.None))
            {
                // Independently captured native bytes, including the observable
                // difference between clipping children and restoring a group.
                Assert.True(surface.Pixels.AsSpan((4 * 32 + 5) * 4, 4).SequenceEqual(new byte[] { red, 0, blue, alpha }), "Clip edge mismatch: forward=" + forward + ", container=" + container);
                Assert.Equal(new byte[] { 0, 128, 0, 255 }, surface.Pixels.AsSpan(0, 4).ToArray());
            }
            Assert.Equal(0, budget.Snapshot().CurrentBytes);
        }
    }

    [Theory]
    [InlineData(false, "opacity")]
    [InlineData(true, "opacity")]
    [InlineData(false, "own-clip")]
    [InlineData(true, "own-clip")]
    [InlineData(false, "own-clip-mask")]
    [InlineData(true, "own-clip-mask")]
    [InlineData(false, "mask")]
    [InlineData(true, "mask")]
    [InlineData(false, "filter")]
    [InlineData(true, "filter")]
    public void Isolated_clip_scopes_release_storage_after_denial_and_cancellation(bool forward, string effect)
    {
        const string definitions = "<defs><clipPath id='c'><rect x='5.25' y='3.75' width='19.5' height='22'/></clipPath><clipPath id='inner'><rect x='3.5' y='4.5' width='18.75' height='20.25'/></clipPath><mask id='m' mask-type='alpha' maskUnits='userSpaceOnUse' x='0' y='0' width='32' height='32'><rect width='32' height='32' fill='white' fill-opacity='.75'/></mask><filter id='f' filterUnits='userSpaceOnUse' x='0' y='0' width='32' height='32'><feOffset dx='1' dy='1'/></filter></defs>";
        string attributes = effect switch
        {
            "own-clip" => " clip-path='url(#inner)'",
            "own-clip-mask" => " clip-path='url(#inner)' mask='url(#m)'",
            "mask" => " mask='url(#m)'",
            "filter" => " filter='url(#f)'",
            _ => ""
        };
        string body = "<g clip-path='url(#c)'><g opacity='.5'" + attributes + "><path d='M4 4H24V24H4Z' fill='red'/><path d='M3 3Q25 3 25 25H3Z' clip-path='url(#inner)' fill='blue' fill-opacity='.25'/></g></g>";
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'>" + (forward ? body + definitions : definitions + body) + "</svg>"), unchanged = input.ToArray();
        RenderBudget parser = new(8_000_000);
        using (RenderBudget.BudgetReservation borrowed = parser.Reserve(73, SvgMemoryCategory.Scene))
        {
            ParserWorkCounter parseWork = new(SvgRenderLimits.Default.MaximumWorkUnits);
            using (SvgRootParser.Parse(input, SvgRenderLimits.Default, parser, null, parseWork, CancellationToken.None)) { }
            Assert.Equal(73, parser.Snapshot().CurrentBytes);
            Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, parser, null, new(parseWork.Used - 1), CancellationToken.None)).ErrorCode);
            Assert.Equal(73, parser.Snapshot().CurrentBytes);
            RenderBudget deniedParse = new(parser.Snapshot().PeakBytes - 74);
            Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, deniedParse, CancellationToken.None)).ErrorCode);
            Assert.Equal(0, deniedParse.Snapshot().CurrentBytes);
            using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, parser, CancellationToken.None))
            {
                RenderBudget raster = new(8_000_000);
                byte[] expected;
                long work;
                using (SvgConverter.RasterizedSurface first = SvgConverter.Rasterize(root, SvgRenderOptions.Default, raster, CancellationToken.None))
                {
                    expected = first.Pixels.ToArray();
                    work = first.WorkUnits;
                }
                Assert.Equal(0, raster.Snapshot().CurrentBytes);
                RenderBudget deniedRaster = new(raster.Snapshot().PeakBytes - 1);
                Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgConverter.Rasterize(root, SvgRenderOptions.Default, deniedRaster, CancellationToken.None)).ErrorCode);
                Assert.Equal(0, deniedRaster.Snapshot().CurrentBytes);
                SvgRenderLimits limits = SvgRenderLimits.Default;
                SvgRenderLimits deniedWork = new(limits.MaximumAccountedBytes, limits.MaximumInputBytes, limits.MaximumDimension, limits.MaximumPixels, limits.MaximumEncodedPngBytes, limits.MaximumNodes, limits.MaximumPathCommands, checked((int)work - 1), limits.MaximumXmlDepth, limits.MaximumAttributesPerElement);
                Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgConverter.Rasterize(root, new(null, 1, SvgColor.Transparent, deniedWork), raster, CancellationToken.None)).ErrorCode);
                Assert.Equal(0, raster.Snapshot().CurrentBytes);
                using CancellationTokenSource cancellation = new();
                using (SvgAnalyticPaintProbe.Arm((phase, _) =>
                {
                    if (phase == SvgAnalyticPaintPhase.BeforeReplay) cancellation.Cancel();
                }))
                    Assert.Equal(cancellation.Token, Assert.ThrowsAny<OperationCanceledException>(() => SvgConverter.Rasterize(root, SvgRenderOptions.Default, raster, cancellation.Token)).CancellationToken);
                Assert.True(cancellation.IsCancellationRequested);
                Assert.Equal(0, raster.Snapshot().CurrentBytes);
                using (SvgConverter.RasterizedSurface retry = SvgConverter.Rasterize(root, SvgRenderOptions.Default, raster, CancellationToken.None)) Assert.Equal(expected, retry.Pixels);
                Assert.Equal(0, raster.Snapshot().CurrentBytes);
            }
            Assert.Equal(73, parser.Snapshot().CurrentBytes);
        }
        Assert.Equal(0, parser.Snapshot().CurrentBytes);
        Assert.Equal(unchanged, input);
    }
}
