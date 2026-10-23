using TextEncoding = System.Text.Encoding;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgRectangleClipUnionTests
{
    public static IEnumerable<object[]> ContainedRectangles()
    {
        foreach (bool objectBox in new[] { false, true })
        foreach (string map in objectBox ? new[] { "translate(.5 .25)" } : new[] { "translate(.5 .25)", "matrix(1 .2 .3 1 0 0)" })
        foreach (string context in new[] { "direct", "use", "symbol", "pattern", "mask", "marker" })
        foreach (bool curved in new[] { false, true })
        foreach (bool duplicate in new[] { false, true })
            yield return [map, context, curved, duplicate, objectBox];
    }

    [Theory]
    [MemberData(nameof(ContainedRectangles))]
    public void Contained_rectangles_keep_the_outer_native_clip_coverage(string map, string context, bool curved, bool duplicate, bool objectBox)
    {
        // Native browser calibration establishes this equality. Adjacent rects
        // and repeated curved children retain distinct native coverage paths.
        string outer = objectBox ? "<rect x='.2' y='.2' width='.6' height='.6'/>" : "<rect x='8' y='8' width='16' height='16'/>";
        string inner = duplicate ? outer : objectBox ? "<rect x='.3' y='.3' width='.2' height='.2'/>" : "<rect x='12' y='12' width='4' height='4'/>";
        byte[] expected = SvgConverter.ConvertToPng(Document(outer), SvgRenderOptions.Default, CancellationToken.None).ToArray();
        Assert.Equal(expected, SvgConverter.ConvertToPng(Document(outer + inner), SvgRenderOptions.Default, CancellationToken.None).ToArray());
        Assert.Equal(expected, SvgConverter.ConvertToPng(Document(inner + outer), SvgRenderOptions.Default, CancellationToken.None).ToArray());

        byte[] Document(string children)
        {
            string target = curved ? "<path d='M4 26Q16 -4 28 26Z' fill='red'/>" : "<rect x='4' y='4' width='24' height='24' fill='red'/>";
            string clip = "<clipPath id='c' clipPathUnits='" + (objectBox ? "objectBoundingBox" : "userSpaceOnUse") + "'>" + children + "</clipPath>";
            string clipped = "<g transform='" + map + "' clip-path='url(#c)'>" + target + "</g>";
            string content = context switch
            {
                "direct" => "<defs>" + clip + "</defs>" + clipped,
                "use" => "<defs>" + clip + "<g id='q'>" + target + "</g></defs><g transform='" + map + "'><use href='#q' clip-path='url(#c)'/></g>",
                "symbol" => "<defs>" + clip + "<symbol id='s' viewBox='0 0 32 32'>" + target + "</symbol></defs><g transform='" + map + "'><use href='#s' width='32' height='32' clip-path='url(#c)'/></g>",
                "pattern" => "<defs>" + clip + "<pattern id='p' patternUnits='userSpaceOnUse' width='32' height='32'>" + clipped + "</pattern></defs><rect width='32' height='32' fill='url(#p)'/>",
                "mask" => "<defs>" + clip + "<mask id='m' mask-type='alpha' maskUnits='userSpaceOnUse' x='0' y='0' width='32' height='32'>" + clipped + "</mask></defs><rect width='32' height='32' fill='red' mask='url(#m)'/>",
                "marker" => "<defs>" + clip + "<marker id='m' markerUnits='userSpaceOnUse' markerWidth='32' markerHeight='32' overflow='visible'>" + clipped + "</marker></defs><path d='M0 0H1' fill='none' marker-start='url(#m)'/>",
                _ => throw new ArgumentOutOfRangeException(nameof(context))
            };
            return TextEncoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'>" + content + "</svg>");
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_known_retained_group_keeps_object_box_coverage_in_either_clip_definition_order(bool curved)
    {
        string shape = curved ? "<path d='M4 26Q16 -4 28 26Z' fill='red'/>" : "<rect x='4' y='4' width='24' height='24' fill='red'/>";
        const string clip = "<defs><clipPath id='c' clipPathUnits='objectBoundingBox'><rect x='.2' y='.2' width='.6' height='.6'/></clipPath></defs>";
        byte[] expected = SvgConverter.ConvertToPng(Document(false, false), SvgRenderOptions.Default, CancellationToken.None).ToArray();
        foreach (bool forward in new[] { false, true })
        {
            byte[] input = Document(true, forward);
            Assert.Equal(expected, SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None).ToArray());
            RenderBudget budget = new(1_000_000);
            using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None))
            using (SvgConverter.RasterizedSurface raster = SvgConverter.Rasterize(root, SvgRenderOptions.Default, budget, CancellationToken.None))
                Assert.Equal((byte)255, raster.Pixels[(16 * 32 + 16) * 4 + 3]);
            Assert.Equal(0, budget.Snapshot().CurrentBytes);
        }

        byte[] Document(bool instance, bool forward)
        {
            string target = instance ? "<use href='#q' clip-path='url(#c)'/>" : "<g clip-path='url(#c)'>" + shape + "</g>";
            string content = "<defs><g id='q'>" + shape + "</g></defs><g transform='translate(.5 .25)'>" + target + "</g>";
            return TextEncoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'>"
                + (forward ? content + clip : clip + content) + "</svg>");
        }
    }

    [Theory]
    [InlineData(2)]
    [InlineData(42)]
    [InlineData(43)]
    [InlineData(44)]
    public void Containment_keeps_authored_fallback_counts_bounds_and_owned_storage(int count)
    {
        foreach (bool outerFirst in new[] { false, true })
        {
            const string outer = "<rect x='8' y='8' width='16' height='16'/>";
            string inner = string.Concat(Enumerable.Repeat("<rect x='12' y='12' width='4' height='4'/>", count - 1));
            byte[] input = TextEncoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'><defs><clipPath id='c'>"
                + (outerFirst ? outer + inner : inner + outer) + "</clipPath></defs><rect width='32' height='32' fill='red' clip-path='url(#c)'/></svg>");
            RenderBudget budget = new(1_000_000);
            ParserWorkCounter work = new(10_000_000);
            long parserPeak;
            using (RenderBudget.BudgetReservation borrowed = budget.Reserve(73, SvgMemoryCategory.Scene))
            {
                using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, null, work, CancellationToken.None))
                {
                    parserPeak = budget.Snapshot().PeakBytes;
                    using SvgConverter.RasterizedSurface raster = SvgConverter.Rasterize(root, SvgRenderOptions.Default, budget, CancellationToken.None);
                    Assert.Equal((byte)255, raster.Pixels[(16 * 32 + 16) * 4 + 3]);
                }
                Assert.Equal(73, budget.Snapshot().CurrentBytes);
                Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgRootParser.Parse(input,
                    SvgRenderLimits.Default, budget, null, new(work.Used - 1), CancellationToken.None)).ErrorCode);
                Assert.Equal(73, budget.Snapshot().CurrentBytes);
                using CancellationTokenSource cancellation = new();
                cancellation.Cancel();
                Assert.Equal(cancellation.Token, Assert.ThrowsAny<OperationCanceledException>(() => SvgRootParser.Parse(input,
                    SvgRenderLimits.Default, budget, null, new(10_000_000), cancellation.Token)).CancellationToken);
                Assert.Equal(73, budget.Snapshot().CurrentBytes);
                using (SvgRoot retry = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None)) { }
                Assert.Equal(73, budget.Snapshot().CurrentBytes);
            }
            Assert.Equal(0, budget.Snapshot().CurrentBytes);
            RenderBudget denied = new(parserPeak - 1);
            using (RenderBudget.BudgetReservation borrowed = denied.Reserve(73, SvgMemoryCategory.Scene))
            {
                Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgRootParser.Parse(input,
                    SvgRenderLimits.Default, denied, CancellationToken.None)).ErrorCode);
                Assert.Equal(73, denied.Snapshot().CurrentBytes);
            }
            Assert.Equal(0, denied.Snapshot().CurrentBytes);
        }
    }
}
