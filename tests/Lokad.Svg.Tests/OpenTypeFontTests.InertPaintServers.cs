using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed partial class OpenTypeFontTests
{
    public static IEnumerable<object[]> InertPaintServerContracts(bool research)
    {
        foreach (object[] row in SvgInertPaintTests.PaintCases())
        {
            // All paints/consumers on a curve, plus every source lowering route
            // with direct solid paint. Full source graphs remain rendering cases.
            bool routine = (string)row[1] == "curve" || ((string)row[3] == "direct" && (string)row[4] == "red");
            if (routine != research) yield return row;
        }
    }

    public static IEnumerable<object[]> InertTemplateContracts(bool research)
    {
        foreach (object[] row in SvgInertPaintTests.TemplateCases())
        {
            // Retain every server/units/consumer/order/effect combination with
            // rotation, and every map/effect on the direct object-box route.
            bool routine = (string)row[5] == "rotate(7 16 16)" || ((string)row[3] == "direct" && (bool)row[2]);
            if (routine != research) yield return row;
        }
    }

    [Theory]
    [Trait("Layer", "Contract")]
    [MemberData(nameof(InertPaintServerContracts), false)]
    public Task Inert_paint_servers_prepare_without_acquiring_fonts(string server, string shape, bool fill, string context, string paint)
        => AssertInertPaintPrepared(SvgInertPaintTests.PaintInput(server, shape, fill, context, paint, "none"), SvgInertPaintTests.PaintInput(server, shape, fill, context, paint, "url(#e)"));

    [Theory]
    [Trait("Layer", "Contract")]
    [MemberData(nameof(InertTemplateContracts), false)]
    public Task Inert_template_paints_prepare_with_maps_effects_and_forward_replay(string server, bool fill, bool objectBox, string consumer, bool forward, string map, string effects)
        => AssertInertPaintPrepared(SvgInertPaintTests.TemplateInput(server, fill, objectBox, consumer, forward, map, effects, "none"), SvgInertPaintTests.TemplateInput(server, fill, objectBox, consumer, forward, map, effects, "url(#e)"));

    [Theory]
    [Trait("Layer", "Research")]
    [Trait("Category", "Research")]
    [MemberData(nameof(InertPaintServerContracts), true)]
    public Task Inert_paint_servers_extended_prepared_equivalence(string server, string shape, bool fill, string context, string paint)
        => AssertInertPaintPrepared(SvgInertPaintTests.PaintInput(server, shape, fill, context, paint, "none"), SvgInertPaintTests.PaintInput(server, shape, fill, context, paint, "url(#e)"));

    [Theory]
    [Trait("Layer", "Research")]
    [Trait("Category", "Research")]
    [MemberData(nameof(InertTemplateContracts), true)]
    public Task Inert_template_paints_extended_prepared_equivalence(string server, bool fill, bool objectBox, string consumer, bool forward, string map, string effects)
        => AssertInertPaintPrepared(SvgInertPaintTests.TemplateInput(server, fill, objectBox, consumer, forward, map, effects, "none"), SvgInertPaintTests.TemplateInput(server, fill, objectBox, consumer, forward, map, effects, "url(#e)"));

    private static async Task AssertInertPaintPrepared(byte[] control, byte[] input)
    {
        (var catalog, var provider, _) = CreateExamplePack(CreateTriangleFont());
        using SvgPreparedDocument prepared = await SvgPreparedDocument.CreateAsync(input, catalog, provider, SvgRenderOptions.Default, CancellationToken.None);
        Assert.Equal(Render(control), prepared.ConvertToPng(CancellationToken.None).ToArray());
        Assert.Equal(0, provider.RequestCount);
    }

    [Theory]
    [Trait("Layer", "Contract")]
    [InlineData("stroke-width", "-1", SvgErrorCode.UnsupportedFeature)]
    [InlineData("stroke-width", "NaN", SvgErrorCode.UnsupportedFeature)]
    [InlineData("stroke-miterlimit", "0", SvgErrorCode.UnsupportedFeature)]
    [InlineData("stroke-linecap", "invalid", SvgErrorCode.UnsupportedFeature)]
    [InlineData("stroke-linejoin", "invalid", SvgErrorCode.UnsupportedFeature)]
    [InlineData("stroke-dasharray", "-1", SvgErrorCode.MalformedInput)]
    [InlineData("stroke-dashoffset", "NaN", SvgErrorCode.UnsupportedFeature)]
    [InlineData("pathLength", "-1", SvgErrorCode.MalformedInput)]
    [InlineData("stroke-opacity", "NaN", SvgErrorCode.MalformedInput)]
    public async Task Inert_stroke_servers_do_not_hide_invalid_authored_values(string property, string value, SvgErrorCode category)
    {
        foreach (string resource in new[] { "<linearGradient id='e'/>", "<radialGradient id='e'/>", "<pattern id='e' width='4' height='4' patternUnits='userSpaceOnUse'/>" })
        {
            byte[] input = Document("<defs>" + resource + "</defs><path d='M1 1Q7 1 7 7Z' fill='red' stroke='url(#e)' " + property + "='" + value + "'/>");
            Assert.Equal(category, Assert.Throws<SvgException>(() => Render(input)).ErrorCode);
            (var catalog, var provider, _) = CreateExamplePack(CreateTriangleFont());
            SvgException error = await Assert.ThrowsAsync<SvgException>(async () =>
            {
                using SvgPreparedDocument prepared = await SvgPreparedDocument.CreateAsync(input, catalog, provider, SvgRenderOptions.Default, CancellationToken.None);
            });
            Assert.Equal(category, error.ErrorCode);
            Assert.Equal(0, provider.RequestCount);
        }
    }

    [Fact]
    [Trait("Layer", "Contract")]
    public void Inert_mask_paint_replay_releases_partial_owners_and_preserves_borrowed_storage()
    {
        byte[] input = Document("<defs><mask id='m' mask-type='alpha'><rect x='1' y='1' width='6' height='6' rx='2' fill='url(#live)' stroke='url(#empty)' stroke-width='2'/></mask><linearGradient id='live'><stop stop-color='red'/><stop offset='1' stop-color='blue'/></linearGradient><pattern id='empty' patternUnits='userSpaceOnUse' width='4' height='4'/></defs><rect width='8' height='8' mask='url(#m)'/>");
        byte[] unchanged = input.ToArray();
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(1_000_000));
        using RenderBudget.BudgetReservation borrowed = budget.Reserve(73, SvgMemoryCategory.Parser);
        using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, null, CancellationToken.None))
        {
            Assert.False(root.HasUnsupportedChild);
        }
        Assert.Equal(73, budget.Snapshot().CurrentBytes);
        RenderBudget tight = new RenderBudget(new HostMemoryBudget(budget.Snapshot().PeakBytes - 1));
        using RenderBudget.BudgetReservation tightBorrowed = tight.Reserve(73, SvgMemoryCategory.Parser);
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, tight, CancellationToken.None)).ErrorCode);
        Assert.Equal(73, tight.Snapshot().CurrentBytes);
        using CancellationTokenSource cancellation = new(); cancellation.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, cancellation.Token));
        Assert.Equal(73, budget.Snapshot().CurrentBytes);
        using (SvgRoot retry = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None)) Assert.False(retry.HasUnsupportedChild);
        Assert.Equal(73, budget.Snapshot().CurrentBytes);
        Assert.Equal(unchanged, input);
    }
}
