using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgInheritedClipIsolationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Forward_clip_layers_rebuild_sibling_commands_with_linear_parser_allocation_growth(bool curvedFilter)
    {
        long small = Measure(32, curvedFilter), large = Measure(512, curvedFilter);
        Assert.True(large < small * 18, "Sixteen times as many sibling scopes must keep linear parser allocation growth.");

        static long Measure(int count, bool curvedFilter)
        {
            System.Text.StringBuilder source = new("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'>");
            string effect = curvedFilter ? "filter='url(#f)'" : "opacity='.5'";
            for (int index = 0; index < count; index++)
                source.Append("<g clip-path='url(#c)'><g " + effect + "><rect width='24' height='24' fill='red'/><rect x='1' y='1' width='24' height='24' fill='blue'/></g></g><g><rect width='2' height='2' fill='green'/></g>");
            string clip = curvedFilter ? "<ellipse cx='16' cy='16' rx='12.5' ry='10.5'/>" : "<rect x='5.25' y='3.75' width='19.5' height='22'/>";
            source.Append("<defs><clipPath id='c'>" + clip + "</clipPath><filter id='f'><feOffset dx='1' dy='1'/></filter></defs></svg>");
            RenderBudget budget = new RenderBudget(new HostMemoryBudget(40_000_000));
            long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            using (SvgRootParser.Parse(System.Text.Encoding.UTF8.GetBytes(source.ToString()), SvgRenderLimits.Default, budget, null, CancellationToken.None)) { }
            Assert.Equal(0, budget.Snapshot().CurrentBytes);
            return GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        }
    }

    [Fact]
    public void An_ancestor_clip_excluding_all_filter_output_keeps_a_transparent_surface()
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'><defs><clipPath id='c'><circle cx='90' cy='90' r='5'/></clipPath><filter id='f'><feOffset dx='1' dy='1'/></filter></defs><g clip-path='url(#c)'><g filter='url(#f)'><rect width='24' height='24' fill='red'/></g></g></svg>");
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(4_000_000));
        using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None))
        using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, SvgRenderOptions.Default, budget, CancellationToken.None))
            Assert.All(surface.Pixels, value => Assert.Equal((byte)0, value));
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
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
            RenderBudget budget = new RenderBudget(new HostMemoryBudget(1_000_000));
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
    [InlineData(false, "opaque-mask")]
    [InlineData(true, "opaque-mask")]
    [InlineData(false, "filter")]
    [InlineData(true, "filter")]
    [InlineData(false, "curved-filter")]
    [InlineData(false, "srgb-filter")]
    public void Isolated_clip_scopes_release_storage_after_denial_and_cancellation(bool forward, string effect)
    {
        string definitions = "<defs><clipPath id='c'><rect x='5.25' y='3.75' width='19.5' height='22'/></clipPath><clipPath id='inner'><rect x='3.5' y='4.5' width='18.75' height='20.25'/></clipPath><mask id='m' mask-type='alpha' maskUnits='userSpaceOnUse' x='0' y='0' width='32' height='32'><rect width='32' height='32' fill='white' fill-opacity='.75'/></mask><filter id='f' filterUnits='userSpaceOnUse' x='0' y='0' width='32' height='32'><feOffset dx='1' dy='1'/></filter></defs>";
        if (effect == "curved-filter") definitions = definitions.Replace("<rect x='5.25' y='3.75' width='19.5' height='22'/>", "<ellipse cx='16' cy='16' rx='12.5' ry='10.5'/>", StringComparison.Ordinal);
        if (effect == "srgb-filter") definitions = definitions.Replace("<filter id='f'", "<filter id='f' color-interpolation-filters='sRGB'", StringComparison.Ordinal);
        string attributes = effect switch
        {
            "own-clip" => " clip-path='url(#inner)'",
            "own-clip-mask" => " clip-path='url(#inner)' mask='url(#m)'",
            "mask" => " mask='url(#m)'",
            "opaque-mask" => " mask='url(#m)'",
            "filter" or "curved-filter" or "srgb-filter" => " filter='url(#f)'",
            _ => ""
        };
        string ownerOpacity = effect == "opaque-mask" ? "1" : ".5";
        string body = "<g clip-path='url(#c)'><g opacity='" + ownerOpacity + "'" + attributes + "><path d='M4 4H24V24H4Z' fill='red'/><path d='M3 3Q25 3 25 25H3Z' clip-path='url(#inner)' fill='blue' fill-opacity='.25'/></g></g>";
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'>" + (forward ? body + definitions : definitions + body) + "</svg>"), unchanged = input.ToArray();
        RenderBudget parser = new RenderBudget(new HostMemoryBudget(8_000_000));
        using (RenderBudget.BudgetReservation borrowed = parser.Reserve(73, SvgMemoryCategory.Scene))
        {
            using (SvgRootParser.Parse(input, SvgRenderLimits.Default, parser, null, CancellationToken.None)) { }
            Assert.Equal(73, parser.Snapshot().CurrentBytes);
            RenderBudget deniedParse = new RenderBudget(new HostMemoryBudget(parser.Snapshot().PeakBytes - 74));
            Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, deniedParse, CancellationToken.None)).ErrorCode);
            Assert.Equal(0, deniedParse.Snapshot().CurrentBytes);
            using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, parser, CancellationToken.None))
            {
                RenderBudget raster = new RenderBudget(new HostMemoryBudget(8_000_000));
                byte[] expected;
                using (SvgConverter.RasterizedSurface first = SvgConverter.Rasterize(root, SvgRenderOptions.Default, raster, CancellationToken.None))
                {
                    expected = first.Pixels.ToArray();
                }
                Assert.Equal(0, raster.Snapshot().CurrentBytes);
                RenderBudget deniedRaster = new RenderBudget(new HostMemoryBudget(raster.Snapshot().PeakBytes - 1));
                Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgConverter.Rasterize(root, SvgRenderOptions.Default, deniedRaster, CancellationToken.None)).ErrorCode);
                Assert.Equal(0, deniedRaster.Snapshot().CurrentBytes);
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
