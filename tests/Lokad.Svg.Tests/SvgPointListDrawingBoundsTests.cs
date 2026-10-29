using System.Globalization;
using System.Numerics;
using TextEncoding = System.Text.Encoding;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgPointListDrawingBoundsTests
{
    public static IEnumerable<object[]> SaturatedBounds()
    {
        (string Origin, string Width, string Map)[] cases =
        [
            ("2147483647", "1024", "0.0234375 0 0 1 -50331644 0"),
            ("2147483648", "1024", "0.0234375 0 0 1 -50331644 0"),
            ("2147483649", "1024", "0.0234375 0 0 1 -50331644 0"),
            ("2147483777", "1024", "0.0234375 0 0 1 -50331650 0"),
            ("10000000000", "4096", "0.005859375 0 0 1 -58593746 0"),
            ("-10000000000", "4096", "0.005859375 0 0 1 58593754 0"),
            ("100000000000000000000", "35184372088832", "6.8212102632969618e-13 0 0 1 -68212100 0"),
            ("-100000000000000000000", "35184372088832", "6.8212102632969618e-13 0 0 1 68212108 0"),
            ("100000000000000000000000000000000000000", "40564819207303340847894502572032", "5.9164567891575885e-31 0 0 1 -59164562 0"),
            ("-100000000000000000000000000000000000000", "40564819207303340847894502572032", "5.9164567891575885e-31 0 0 1 59164570 0")
        ];
        foreach ((string origin, string width, string map) in cases)
        foreach (string shape in new[] { "polygon", "polyline" })
        foreach (bool gradient in new[] { false, true })
        foreach (bool decimalSpelling in new[] { false, true }) yield return [origin, width, map, shape, gradient, decimalSpelling];
    }

    [Theory]
    [MemberData(nameof(SaturatedBounds))]
    public void Saturated_native_drawing_bounds_remain_empty_after_a_compensating_map(string origin, string width, string map, string shape, bool gradient, bool decimalSpelling)
    {
        byte[] input = TextEncoding.UTF8.GetBytes(Document(origin, width, map, shape, gradient, decimalSpelling));
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(1_000_000));
        using SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None);
        using SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, SvgRenderOptions.Default, budget, CancellationToken.None);
        // Independently captured whole SVGs are transparent: native drawing
        // records saturate their integer bounds before the enclosing map.
        for (int pixel = 0; pixel < 1024; pixel++) Assert.Equal(0, surface.Pixels[pixel * 4 + 3]);
    }

    [Theory]
    [InlineData("polygon", "use")]
    [InlineData("polyline", "use")]
    [InlineData("polygon", "pattern")]
    [InlineData("polyline", "pattern")]
    [InlineData("polygon", "mask")]
    [InlineData("polyline", "mask")]
    public void Cached_sources_keep_the_pending_instance_or_resource_map(string shape, string consumer)
    {
        string definitions;
        string body;
        int width;
        if (consumer == "use")
        {
            definitions = "<" + shape + " id='q' points='16777215 4 16777231 4 16777231 24 16777215 24' fill='red'/>";
            body = "<use href='#q' transform='matrix(1.5 0 0 1 -25165818.5 0)'/>";
            width = 26;
        }
        else
        {
            string source = "<" + shape + " points='2147483648 4 2147484672 4 2147484672 24 2147483648 24' fill='red' transform='matrix(0.0234375 0 0 1 -50331644 0)'/>";
            definitions = consumer == "pattern"
                ? "<pattern id='p' patternUnits='userSpaceOnUse' width='32' height='32'>" + source + "</pattern>"
                : "<mask id='k' mask-type='alpha' maskUnits='userSpaceOnUse' x='0' y='0' width='32' height='32'>" + source + "</mask>";
            body = consumer == "pattern" ? "<rect width='32' height='32' fill='url(#p)'/>" : "<rect width='32' height='32' fill='red' mask='url(#k)'/>";
            width = 24;
        }
        string actual = "<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'><defs>" + definitions + "</defs>" + body + "</svg>";
        string expected = "<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'><rect x='4' y='4' width='" + width.ToString(CultureInfo.InvariantCulture) + "' height='20' fill='red'/></svg>";
        Assert.Equal(SvgConverter.ConvertToPng(TextEncoding.UTF8.GetBytes(expected), SvgRenderOptions.Default, CancellationToken.None).ToArray(),
            SvgConverter.ConvertToPng(TextEncoding.UTF8.GetBytes(actual), SvgRenderOptions.Default, CancellationToken.None).ToArray());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Culled_native_storage_is_released_after_success_denial_and_retry(bool gradient)
    {
        byte[] input = TextEncoding.UTF8.GetBytes(Document("2147483648", "1024", "0.0234375 0 0 1 -50331644 0", "polygon", gradient, false));
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(1_000_000));
        long peak;
        using (RenderBudget.BudgetReservation borrowed = budget.Reserve(73, SvgMemoryCategory.Scene))
        {
            using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, null, CancellationToken.None)) { peak = budget.Snapshot().PeakBytes; }
            Assert.Equal(73, budget.Snapshot().CurrentBytes);
            Assert.Equal(73, budget.Snapshot().CurrentBytes);
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

    private static string Document(string origin, string width, string map, string shape, bool gradient, bool decimalSpelling)
    {
        string right = (BigInteger.Parse(origin, CultureInfo.InvariantCulture) + BigInteger.Parse(width, CultureInfo.InvariantCulture)).ToString(CultureInfo.InvariantCulture);
        string suffix = decimalSpelling ? ".0" : string.Empty;
        string left = origin + suffix;
        right += suffix;
        return "<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'><defs><linearGradient id='l'><stop stop-color='red'/><stop offset='1' stop-color='blue'/></linearGradient></defs><"
            + shape + " points='" + left + " 4 " + right + " 4 " + right + " 24 " + left + " 24' fill='" + (gradient ? "url(#l)" : "red") + "' transform='matrix(" + map + ")'/></svg>";
    }
}
