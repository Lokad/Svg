using TextEncoding = System.Text.Encoding;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgPointListCoverageTests
{
    public static IEnumerable<object[]> Placements()
    {
        foreach (string shape in new[] { "polygon", "polyline" })
        foreach (string consumer in new[] { "direct", "group", "use", "symbol", "viewport", "pattern", "mask" })
        foreach (string paint in new[] { "red", "url(#l)", "url(#r)", "url(#p)" })
        foreach (string map in new[] { "translate(.5 .5)", "translate(2 3) scale(.75)" })
        foreach (bool stroke in new[] { false, true })
        foreach (double scale in new[] { 1d, 1.125d }) yield return [shape, consumer, paint, map, stroke, scale];
    }

    [Theory]
    [MemberData(nameof(Placements))]
    public void Point_lists_keep_authored_coverage_paint_and_placement(string shape, string consumer, string paint, string map, bool stroke, double scale)
    {
        // Actual SVG point/path pairs are independently calibrated, including
        // output-only scaling. Nested pattern paints retain their restriction.
        byte[] points = TextEncoding.UTF8.GetBytes(Document(shape, consumer, paint, map, stroke, false));
        byte[] path = TextEncoding.UTF8.GetBytes(Document(shape, consumer, paint, map, stroke, true));
        SvgRenderOptions options = new(null, scale, SvgColor.Transparent, SvgRenderLimits.Default);
        if (consumer == "pattern" && paint == "url(#p)")
        {
            foreach (byte[] input in new[] { points, path })
                Assert.Equal(SvgErrorCode.UnsupportedFeature, Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(input, options, CancellationToken.None)).ErrorCode);
            return;
        }
        Assert.Equal(SvgConverter.ConvertToPng(path, options, CancellationToken.None).ToArray(),
            SvgConverter.ConvertToPng(points, options, CancellationToken.None).ToArray());
    }

    [Fact]
    public void Fractional_pattern_translation_preserves_native_bilinear_stripe_alpha()
    {
        byte[] input = TextEncoding.UTF8.GetBytes(Document("polygon", "direct", "url(#p)", "translate(.5 .5)", false, false));
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(1_000_000));
        using SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None);
        using SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, SvgRenderOptions.Default, budget, CancellationToken.None);
        // Independent native capture: half-covered outer edges multiply the
        // half-filtered stripe, while interior transitions retain alpha 128.
        byte[] expected = [64, 255, 128, 0, 128, 255, 128, 0, 128, 255, 128, 0, 128, 255, 128, 0, 64];
        for (int x = 8; x <= 24; x++) Assert.Equal(expected[x - 8], surface.Pixels[(12 * 32 + x) * 4 + 3]);
    }

    [Theory]
    [InlineData("direct")]
    [InlineData("use")]
    [InlineData("symbol")]
    [InlineData("viewport")]
    [InlineData("pattern")]
    [InlineData("mask")]
    public void Native_point_and_pattern_owners_preserve_borrowed_storage_on_denial_and_retry(string consumer)
    {
        string paint = consumer == "pattern" ? "url(#l)" : "url(#p)";
        byte[] input = TextEncoding.UTF8.GetBytes(Document("polygon", consumer, paint, "translate(.5 .5)", true, false));
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(1_000_000));
        long peak;
        using (RenderBudget.BudgetReservation borrowed = budget.Reserve(73, SvgMemoryCategory.Scene))
        {
            using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, null, CancellationToken.None)) { peak = budget.Snapshot().PeakBytes; }
            Assert.Equal(73, budget.Snapshot().CurrentBytes);
            Assert.Equal(73, budget.Snapshot().CurrentBytes);
            using CancellationTokenSource cancellation = new(); cancellation.Cancel();
            Assert.Equal(cancellation.Token, Assert.ThrowsAny<OperationCanceledException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, null, cancellation.Token)).CancellationToken);
            using (SvgRoot retry = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None)) { }
            Assert.Equal(73, budget.Snapshot().CurrentBytes);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        RenderBudget denied = new RenderBudget(new HostMemoryBudget(peak - 1));
        using (RenderBudget.BudgetReservation borrowed = denied.Reserve(73, SvgMemoryCategory.Scene))
        {
            Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, denied, CancellationToken.None)).ErrorCode);
            Assert.Equal(73, denied.Snapshot().CurrentBytes);
        }
        Assert.Equal(0, denied.Snapshot().CurrentBytes);
    }

    private static string Document(string shape, string consumer, string paint, string map, bool stroke, bool path)
    {
        string definitions = "<linearGradient id='l'><stop stop-color='red'/><stop offset='1' stop-color='blue'/></linearGradient><radialGradient id='r'><stop stop-color='red'/><stop offset='1' stop-color='blue'/></radialGradient><pattern id='p' patternUnits='userSpaceOnUse' width='4' height='4'><rect width='2' height='4' fill='red'/></pattern>";
        string source = (path ? "<path d='M8 8L24 8L24 24L8 24" + (shape == "polygon" ? "Z" : "") + "'" : "<" + shape + " points='8 8 24 8 24 24 8 24'")
            + " fill='" + paint + "'" + (stroke ? " stroke='black' stroke-width='2' stroke-linejoin='round'" : "") + " id='q'/>";
        string body;
        switch (consumer)
        {
            case "direct": body = source.Replace("/>", " transform='" + map + "'/>", StringComparison.Ordinal); break;
            case "group": body = "<g transform='" + map + "'>" + source + "</g>"; break;
            case "use": definitions += source; body = "<use href='#q' transform='" + map + "'/>"; break;
            case "symbol": definitions += "<symbol id='s' viewBox='0 0 32 32'>" + source + "</symbol>"; body = "<use href='#s' width='32' height='32' transform='" + map + "'/>"; break;
            case "viewport": body = "<svg width='32' height='32' viewBox='0 0 32 32'><g transform='" + map + "'>" + source + "</g></svg>"; break;
            case "pattern": definitions += "<pattern id='r2' patternUnits='userSpaceOnUse' width='32' height='32'><g transform='" + map + "'>" + source + "</g></pattern>"; body = "<rect width='32' height='32' fill='url(#r2)'/>"; break;
            default: definitions += "<mask id='k' mask-type='alpha' maskUnits='userSpaceOnUse' x='0' y='0' width='32' height='32'><g transform='" + map + "'>" + source + "</g></mask>"; body = "<rect width='32' height='32' fill='red' mask='url(#k)'/>"; break;
        }
        return "<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'><defs>" + definitions + "</defs>" + body + "</svg>";
    }
}
