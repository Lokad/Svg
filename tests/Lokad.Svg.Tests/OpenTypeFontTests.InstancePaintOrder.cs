using Lokad.Svg.Fonts;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed partial class OpenTypeFontTests
{
    public static IEnumerable<object[]> InstancePaintOrders()
    {
        foreach (string paint in new[] { "pattern", "linear", "radial" })
        foreach (string role in new[] { "fill", "stroke" })
        foreach (string binding in new[] { "source", "use", "inherited" })
        foreach (string consumer in new[] { "plain", "group", "symbol", "pattern", "mask", "filter" })
        foreach (string shape in new[] { "rect", "path", "circle", "line" })
            if ((shape != "line" || role == "stroke") && (consumer != "pattern" || paint != "pattern"))
                yield return [paint, role, binding, consumer, shape];
    }

    [Theory]
    [MemberData(nameof(InstancePaintOrders))]
    public async Task Local_instance_paints_resolve_independently_of_source_use_and_resource_order(string paint, string role, string binding, string consumer, string shape)
    {
        (var catalog, var provider, _) = CreateExamplePack(CreateTriangleFont());
        string attributes = role == "fill" ? " fill='url(#p)'" : " stroke='url(#p)' stroke-width='2'";
        string geometry = (shape switch
        {
            "path" => "<path d='M4 4H20V20H4Z'",
            "circle" => "<circle cx='12' cy='12' r='8'",
            "line" => "<line x1='4' y1='12' x2='20' y2='12'",
            _ => "<rect x='4' y='4' width='16' height='16'"
        }) + (role == "stroke" ? " fill='none'" : "") + (binding == "source" ? attributes : "") + "/>";
        string source = "<defs>" + (consumer switch
        {
            "group" => "<g id='s'>" + geometry + "</g>",
            "symbol" => "<symbol id='s' viewBox='0 0 32 32'>" + geometry + "</symbol>",
            _ => geometry.Replace("/>", " id='s'/>", StringComparison.Ordinal)
        }) + "</defs>";
        string use = "<use href='#s' width='32' height='32'" + (binding == "use" ? attributes : "") + "/>";
        if (binding == "inherited") use = "<g" + attributes + ">" + use + "</g>";
        string body = consumer switch
        {
            "pattern" => "<defs><pattern id='tile' width='32' height='32' patternUnits='userSpaceOnUse'>" + use + "</pattern></defs><rect width='32' height='32' fill='url(#tile)'/>",
            "mask" => "<defs><mask id='m' mask-type='alpha' maskUnits='userSpaceOnUse' x='0' y='0' width='32' height='32'>" + use + "</mask></defs><rect width='32' height='32' fill='blue' mask='url(#m)'/>",
            "filter" => "<defs><filter id='f' filterUnits='userSpaceOnUse' x='0' y='0' width='32' height='32'><feOffset dx='1' dy='0'/></filter></defs><g filter='url(#f)'>" + use + "</g>",
            _ => use
        };
        string resource = "<defs>" + InstancePaintResource(paint) + "</defs>";
        byte[] expected = Render(InstancePaintDocument(resource + source + body));
        Assert.NotEqual(Render(InstancePaintDocument("")), expected);
        foreach (string input in new[] { source + body + resource, body + source + resource, source + resource + body })
        {
            byte[] svg = InstancePaintDocument(input);
            Assert.Equal(expected, Render(svg));
            using SvgPreparedDocument prepared = await SvgPreparedDocument.CreateAsync(svg, catalog, provider, SvgRenderOptions.Default, CancellationToken.None);
            Assert.Equal(expected, prepared.ConvertToPng(CancellationToken.None).ToArray());
        }
        Assert.Equal(0, provider.RequestCount);
    }

    [Theory]
    [InlineData("pattern", false)]
    [InlineData("pattern", true)]
    [InlineData("linear", false)]
    [InlineData("linear", true)]
    [InlineData("radial", false)]
    [InlineData("radial", true)]
    public void Deferred_instance_paints_preserve_interleaved_effect_and_drawing_order(string paint, bool stroke)
    {
        string shape = "<path id='s' d='M4 4H20V20H4Z' " + (stroke ? "fill='none' stroke='url(#p)' stroke-width='2'" : "fill='url(#p)'") + "/>";
        string source = "<defs>" + shape + "<g id='alias'><use href='#s'/><use href='#s' x='4'/></g><clipPath id='c'><rect width='28' height='28'/></clipPath><mask id='m' mask-type='alpha'><rect width='32' height='32' fill='white'/></mask></defs>";
        const string body = "<rect width='32' height='32' fill='white'/><g opacity='.5' clip-path='url(#c)' mask='url(#m)'><use href='#s'/><rect x='8' y='8' width='16' height='16' fill='red'/><use href='#alias' x='1'/></g><rect x='12' y='12' width='4' height='4' fill='green'/>";
        string resource = "<defs>" + InstancePaintResource(paint) + "</defs>";
        Assert.Equal(Render(InstancePaintDocument(resource + source + body)), Render(InstancePaintDocument(source + body + resource)));
    }

    [Theory]
    [InlineData("pattern")]
    [InlineData("linear")]
    [InlineData("radial")]
    public void Deferred_instance_paint_owners_release_on_every_work_denial_and_sampled_memory_denials(string paint)
    {
        byte[] input = InstancePaintDocument("<defs><path id='s' d='M4 4H20V20H4Z' fill='url(#p)'/><clipPath id='c'><rect width='28' height='28'/></clipPath></defs><g clip-path='url(#c)'><use href='#s'/><rect x='8' y='8' width='8' height='8' fill='red'/></g><defs>" + InstancePaintResource(paint) + "</defs>");
        byte[] unchanged = input.ToArray();
        RenderBudget measured = new(1_000_000);
        ParserWorkCounter work = new(1_000_000);
        using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, measured, null, work, CancellationToken.None))
            Assert.NotEmpty(root.Geometries.ToArray());
        Assert.Equal(0, measured.Snapshot().CurrentBytes);
        for (long limit = 0; limit < work.Used; limit++)
        {
            RenderBudget denied = new(1_000_000);
            using RenderBudget.BudgetReservation borrower = denied.Reserve(73, SvgMemoryCategory.Parser);
            Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, denied, null, new(limit), CancellationToken.None)).ErrorCode);
            Assert.Equal(73, denied.Snapshot().CurrentBytes);
        }
        for (long limit = 1; limit < measured.Snapshot().PeakBytes; limit += 256) DenyMemory(limit);
        DenyMemory(measured.Snapshot().PeakBytes - 1);
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        RenderBudget canceled = new(1_000_000);
        Assert.Equal(cancellation.Token, Assert.ThrowsAny<OperationCanceledException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, canceled, cancellation.Token)).CancellationToken);
        Assert.Equal(0, canceled.Snapshot().CurrentBytes);
        using (SvgRoot retry = SvgRootParser.Parse(input, SvgRenderLimits.Default, measured, null, new(work.Used), CancellationToken.None))
        using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(retry, SvgRenderOptions.Default, measured, CancellationToken.None))
            Assert.Contains(surface.Pixels, value => value != 0);
        Assert.Equal(0, measured.Snapshot().CurrentBytes);
        Assert.Equal(unchanged, input);

        void DenyMemory(long limit)
        {
            RenderBudget denied = new(limit + 73);
            using RenderBudget.BudgetReservation borrower = denied.Reserve(73, SvgMemoryCategory.Parser);
            Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, denied, null, new(1_000_000), CancellationToken.None)).ErrorCode);
            Assert.Equal(73, denied.Snapshot().CurrentBytes);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("<filter id='p'><feFlood/></filter>")]
    [InlineData("<mask id='p'><rect width='32' height='32'/></mask>")]
    [InlineData("<clipPath id='p'><rect width='32' height='32'/></clipPath>")]
    [InlineData("<rect id='p' width='32' height='32'/>")]
    [InlineData("<linearGradient id='p' href='#p'/>")]
    public void Invalid_instance_paint_references_reject_in_either_order_without_leaking(string resource)
    {
        foreach (string paint in new[] { "fill='url(#p)'", "fill='none' stroke='url(#p)' stroke-width='2'" })
        foreach (bool forward in new[] { false, true })
        {
            string source = "<defs><rect id='s' width='16' height='16' " + paint + "/></defs><use href='#s'/>";
            byte[] input = InstancePaintDocument(forward ? source + "<defs>" + resource + "</defs>" : "<defs>" + resource + "</defs>" + source);
            RenderBudget budget = new(1_000_000);
            Assert.Equal(SvgErrorCode.UnsupportedFeature, Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None)).ErrorCode);
            Assert.Equal(0, budget.Snapshot().CurrentBytes);
        }
    }

    private static byte[] InstancePaintDocument(string body)
        => System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'>" + body + "</svg>");

    private static string InstancePaintResource(string paint) => paint switch
    {
        "linear" => "<linearGradient id='p' gradientUnits='userSpaceOnUse' x2='32'><stop stop-color='blue'/><stop offset='1' stop-color='blue'/></linearGradient>",
        "radial" => "<radialGradient id='p' gradientUnits='userSpaceOnUse' cx='16' cy='16' r='16'><stop stop-color='blue'/><stop offset='1' stop-color='blue'/></radialGradient>",
        _ => "<pattern id='p' width='32' height='32' patternUnits='userSpaceOnUse'><rect width='32' height='32' fill='blue'/></pattern>"
    };
}
