using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgCurveOwnershipTests
{
    public static IEnumerable<object[]> Inputs()
    {
        foreach (string name in new[] { "mixed-leaves", "stroked-fill-override", "child-alias", "magnified-symbol-curves" })
            yield return new object[] { "symbol-" + name, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "symbol-instances", name + ".svg")), false };
        yield return new object[] { "rejects-translucent-paint-strokes-on-circles-1", "<svg xmlns=\"http://www.w3.org/2000/svg\"><defs><linearGradient id=\"g\"><stop offset=\"0\" stop-color=\"#ff0000\"/><stop offset=\"1\" stop-color=\"#0000ff\"/></linearGradient></defs><circle cx=\"4\" cy=\"4\" r=\"4\" fill=\"red\" stroke=\"url(#g)\" stroke-width=\"2\" opacity=\"0.5\"/></svg>", false };
        yield return new object[] { "rejects-translucent-paint-strokes-on-rounded-definitions-1", "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"64\" height=\"64\"><defs><linearGradient id=\"g\"><stop offset=\"0\" stop-color=\"#ff0000\"/><stop offset=\"1\" stop-color=\"#0000ff\"/></linearGradient></defs><defs><rect id=\"r\" x=\"8\" y=\"8\" width=\"48\" height=\"48\" rx=\"12\" fill=\"red\" stroke=\"url(#g)\" stroke-width=\"8\" opacity=\"0.5\"/></defs><use href=\"#r\"/></svg>", false };
        yield return new object[] { "rejects-translucent-round-capped-paint-lines-1", "<svg xmlns=\"http://www.w3.org/2000/svg\"><defs><linearGradient id=\"g\" gradientUnits=\"userSpaceOnUse\" x1=\"0\" y1=\"0\" x2=\"16\" y2=\"0\"><stop offset=\"0\" stop-color=\"#ff0000\"/><stop offset=\"1\" stop-color=\"#0000ff\"/></linearGradient></defs><line x1=\"0\" y1=\"4\" x2=\"16\" y2=\"4\" stroke=\"url(#g)\" stroke-width=\"2\" stroke-linecap=\"round\" opacity=\"0.5\"/></svg>", false };
        yield return new object[] { "rejects-translucent-square-capped-radial-dashes-1", "<svg xmlns='http://www.w3.org/2000/svg'><defs><radialGradient id='s' gradientUnits='userSpaceOnUse' cx='8' cy='4' r='8'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><line x1='0' y1='4' x2='16' y2='4' stroke='url(#s)' opacity='0.5' stroke-width='2' stroke-dasharray='8 4' stroke-linecap='square'/></svg>", false };
        yield return new object[] { "rejects-translucent-round-capped-lines-1", "<svg xmlns='http://www.w3.org/2000/svg'><line x1='0' y1='4' x2='16' y2='4' stroke='red' opacity='0.5' stroke-width='2' stroke-linecap='round'/></svg>", false };
        yield return new object[] { "rejects-translucent-square-capped-gradient-dashes-1", "<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='s' gradientUnits='userSpaceOnUse' x1='0' y1='4' x2='16' y2='4'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><line x1='0' y1='4' x2='16' y2='4' stroke='url(#s)' opacity='0.5' stroke-width='2' stroke-dasharray='8 4' stroke-linecap='square'/></svg>", false };
        yield return new object[] { "rejects-translucent-paint-strokes-on-ellipses-1", "<svg xmlns=\"http://www.w3.org/2000/svg\"><defs><linearGradient id=\"g\"><stop offset=\"0\" stop-color=\"#ff0000\"/><stop offset=\"1\" stop-color=\"#0000ff\"/></linearGradient></defs><ellipse cx=\"32\" cy=\"32\" rx=\"20\" ry=\"12\" fill=\"red\" stroke=\"url(#g)\" stroke-width=\"8\" opacity=\"0.5\"/></svg>", false };
        yield return new object[] { "rejects-translucent-paint-strokes-on-rounded-rectangles-1", "<svg xmlns=\"http://www.w3.org/2000/svg\"><defs><linearGradient id=\"g\"><stop offset=\"0\" stop-color=\"#ff0000\"/><stop offset=\"1\" stop-color=\"#0000ff\"/></linearGradient></defs><rect x=\"0\" y=\"0\" width=\"8\" height=\"8\" rx=\"2\" fill=\"red\" stroke=\"url(#g)\" stroke-width=\"2\" opacity=\"0.5\"/></svg>", false };
        yield return new object[] { "rejects-translucent-square-capped-pattern-dashes-1", "<svg xmlns='http://www.w3.org/2000/svg'><defs><pattern id='p' width='8' height='8' patternUnits='userSpaceOnUse'><rect width='8' height='8' fill='red'/></pattern></defs><line x1='0' y1='4' x2='16' y2='4' stroke='url(#p)' opacity='0.5' stroke-width='2' stroke-dasharray='8 4' stroke-linecap='square'/></svg>", false };
        yield return new object[] { "rejects-translucent-square-capped-dashed-lines-1", "<svg xmlns='http://www.w3.org/2000/svg'><line x1='0' y1='4' x2='16' y2='4' stroke='red' opacity='0.5' stroke-width='2' stroke-dasharray='8 4' stroke-linecap='square'/></svg>", false };
        yield return new object[] { "rejects-translucent-strokes-on-filled-rounded-rectangles-1", "<svg xmlns='http://www.w3.org/2000/svg'><rect x='0' y='0' width='8' height='8' rx='2' fill='red' stroke='blue' stroke-width='2' opacity='0.5'/></svg>", false };
        yield return new object[] { "tile-linear-rejection", "<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><linearGradient id='g'><stop offset='0' stop-color='red'/><stop offset='1' stop-color='blue'/></linearGradient><radialGradient id='r'><stop offset='0' stop-color='red'/><stop offset='1' stop-color='blue'/></radialGradient><pattern id='p' width='4' height='4' patternUnits='userSpaceOnUse'><rect width='2' height='2' fill='url(#g)'/></pattern></defs><rect width='8' height='8' fill='url(#p)'/></svg>", true };
        yield return new object[] { "tile-radial-rejection", "<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><linearGradient id='g'><stop offset='0' stop-color='red'/><stop offset='1' stop-color='blue'/></linearGradient><radialGradient id='r'><stop offset='0' stop-color='red'/><stop offset='1' stop-color='blue'/></radialGradient><pattern id='p' width='4' height='4' patternUnits='userSpaceOnUse'><rect width='2' height='2' fill='url(#r)'/></pattern></defs><rect width='8' height='8' fill='url(#p)'/></svg>", true };
        yield return new object[] { "tile-rounded-rejection", "<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><linearGradient id='g'><stop offset='0' stop-color='red'/><stop offset='1' stop-color='blue'/></linearGradient><radialGradient id='r'><stop offset='0' stop-color='red'/><stop offset='1' stop-color='blue'/></radialGradient><pattern id='p' width='4' height='4' patternUnits='userSpaceOnUse'><rect width='2' height='2' rx='1' fill='red'/></pattern></defs><rect width='8' height='8' fill='url(#p)'/></svg>", true };
        yield return new object[] { "applies-a-gray-mask-to-gradient-stroke-geometry", "<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='g'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient><mask id='m'><rect x='0' y='0' width='64' height='64' fill='#808080'/></mask></defs><rect x='16' y='16' width='32' height='32' fill='#00ff00' stroke='url(#g)' stroke-width='8' mask='url(#m)'/></svg>", false };
        yield return new object[] { "applies-a-gray-mask-to-a-stroked-polygon-pair", "<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><defs><mask id='fade'><rect x='0' y='0' width='16' height='16' fill='#808080'/></mask></defs><polygon points='4,4 12,4 4,12' fill='#ff0000' stroke='#0000ff' stroke-width='2' mask='url(#fade)'/></svg>", false };
        yield return new object[] { "applies-a-gray-mask-to-a-gradient-outline-pair", "<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><defs><linearGradient id='g' gradientUnits='userSpaceOnUse' x1='0' y1='0' x2='16' y2='0'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient><mask id='fade'><rect x='0' y='0' width='16' height='16' fill='#808080'/></mask></defs><polygon points='4,4 12,4 4,12' fill='#ff0000' stroke='url(#g)' stroke-width='2' mask='url(#fade)'/></svg>", false };
        yield return new object[] { "applies-a-gray-mask-to-a-gradient-outline-use-instance", "<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='g' gradientUnits='userSpaceOnUse' x1='0' y1='0' x2='8' y2='0'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient><mask id='fade'><rect x='-4' y='-4' width='24' height='16' fill='#808080'/></mask></defs><defs><polygon id='q' points='0,0 8,0 0,8' fill='none' stroke='url(#g)' stroke-width='2'/></defs><use href='#q' mask='url(#fade)'/></svg>", false };
        yield return new object[] { "tile-fill-stroke-element", "<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><linearGradient id='g'><stop offset='0' stop-color='red'/><stop offset='1' stop-color='blue'/></linearGradient><radialGradient id='r'><stop offset='0' stop-color='red'/><stop offset='1' stop-color='blue'/></radialGradient><pattern id='p' width='4' height='4' patternUnits='userSpaceOnUse'><rect x='1' y='1' width='2' height='2' fill='red' stroke='blue' stroke-width='2' opacity='0.5'/></pattern></defs><rect width='8' height='8' fill='url(#p)'/></svg>", true };
        yield return new object[] { "white-masked-fade-matches-unmasked-fade-1", "<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><defs><linearGradient id='g'><stop offset='0' stop-color='red'/><stop offset='1' stop-color='blue' stop-opacity='0'/></linearGradient></defs><polygon points='0,0 16,0 16,8 8,8 8,16 0,16' fill='url(#g)'/></svg>", false };
        yield return new object[] { "white-masked-fade-matches-unmasked-fade-2", "<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><defs><linearGradient id='g'><stop offset='0' stop-color='red'/><stop offset='1' stop-color='blue' stop-opacity='0'/></linearGradient></defs><defs><mask id='m'><rect x='0' y='0' width='16' height='16' fill='#ffffff'/></mask></defs><polygon points='0,0 16,0 16,8 8,8 8,16 0,16' fill='url(#g)' mask='url(#m)'/></svg>", false };
        yield return new object[] { "concave-pattern-bands", "<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><defs><pattern id='p' width='4' height='4' patternUnits='userSpaceOnUse'><rect width='2' height='2' fill='red'/></pattern></defs><polygon points='0,0 16,0 16,8 8,8 8,16 0,16' fill='url(#p)'/></svg>", false };
    }

    [Theory]
    [MemberData(nameof(Inputs))]
    public void Partial_lowering_releases_owners_at_every_parser_work_boundary(string identity, string svg, bool rejected)
    {
        Assert.NotEmpty(identity);
        byte[] input = System.Text.Encoding.UTF8.GetBytes(svg);
        RenderBudget measured = new(10_000_000);
        ParserWorkCounter counter = new(1_000_000);
        Assert.Equal(rejected ? SvgErrorCode.UnsupportedFeature : null, Parse(measured, counter));
        Assert.Equal(0, measured.Snapshot().CurrentBytes);
        for (long limit = 0; limit <= counter.Used; limit++)
        {
            RenderBudget budget = new(10_000_000);
            Assert.Equal(limit < counter.Used ? SvgErrorCode.LimitExceeded : rejected ? SvgErrorCode.UnsupportedFeature : null, Parse(budget, new(limit)));
            Assert.Equal(0, budget.Snapshot().CurrentBytes);
        }
        long peak = measured.Snapshot().PeakBytes;
        SortedSet<long> memoryLimits = new() { 1, peak - 1, peak, peak + 1 };
        for (long limit = 2048; limit < peak; limit += 2048) memoryLimits.Add(limit);
        foreach (long limit in memoryLimits)
        {
            RenderBudget budget = new(limit);
            Assert.Equal(limit < peak ? SvgErrorCode.LimitExceeded : rejected ? SvgErrorCode.UnsupportedFeature : null, Parse(budget, new(1_000_000)));
            Assert.Equal(0, budget.Snapshot().CurrentBytes);
        }

        SvgErrorCode? Parse(RenderBudget budget, ParserWorkCounter work)
        {
            try
            {
                using SvgRoot scene = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, null, work, CancellationToken.None);
                return scene.HasUnsupportedChild ? SvgErrorCode.UnsupportedFeature : null;
            }
            catch (SvgException exception) { return exception.ErrorCode; }
        }
    }
}
