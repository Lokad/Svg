using Lokad.Svg.Fonts;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed partial class OpenTypeFontTests
{
    [Theory]
    [InlineData("direct")]
    [InlineData("pattern")]
    [InlineData("symbol")]
    [InlineData("pattern-and-symbol")]
    public async Task Materialized_mask_scenes_keep_source_owners_across_consumers_failures_and_retry(string consumer)
    {
        const string mask = "<text id='t' x='2' y='14' font-family='Example' font-size='200' fill='white'>A</text><mask id='m' maskUnits='userSpaceOnUse' x='0' y='0' width='32' height='32'><use href='#t'/></mask>";
        const string shape = "<rect width='16' height='16' fill='red' mask='url(#m)'/>";
        string definitions = mask, body = "";
        if (consumer is "pattern" or "pattern-and-symbol")
        {
            definitions += "<pattern id='p' patternUnits='userSpaceOnUse' width='16' height='16'>" + shape + "</pattern>";
            body += "<rect width='16' height='16' fill='url(#p)'/>";
        }
        if (consumer is "symbol" or "pattern-and-symbol")
        {
            definitions += "<symbol id='s'>" + shape + "</symbol>";
            body += "<use href='#s'/>";
        }
        body += "<g transform='translate(16)'>" + shape + "</g>";
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'><defs>" + definitions + "</defs>" + body + "</svg>");
        (var catalog, var provider, _) = CreateExamplePack(CreateTriangleFont());
        using SvgFontSnapshot fonts = await SvgFontSnapshot.CreateAsync(catalog, new[] { new SvgFontFaceRequest("Example", 400, false) }, provider, CancellationToken.None);
        SvgRenderOptions options = new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, fonts);
        RenderBudget budget = new(4_000_000);
        long parseWork;
        byte[] expected;
        using (RenderBudget.BudgetReservation borrowed = budget.Reserve(73, SvgMemoryCategory.Scene))
        {
            using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, fonts, CancellationToken.None))
            {
                parseWork = root.ParserWorkUnits;
                long retained = budget.Snapshot().CurrentBytes;
                using (SvgConverter.RasterizedSurface first = SvgConverter.Rasterize(root, options, budget, CancellationToken.None))
                {
                    expected = first.Pixels.ToArray();
                    Assert.Contains(expected, value => value != 0);
                }
                Assert.Equal(retained, budget.Snapshot().CurrentBytes);
                using (RenderBudget.BudgetReservation pressure = budget.Reserve(budget.AvailableBytes - 1, SvgMemoryCategory.Surface))
                    Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgConverter.Rasterize(root, options, budget, CancellationToken.None)).ErrorCode);
                Assert.Equal(retained, budget.Snapshot().CurrentBytes);
                using SvgConverter.RasterizedSurface retry = SvgConverter.Rasterize(root, options, budget, CancellationToken.None);
                Assert.Equal(expected, retry.Pixels);
            }
            Assert.Equal(73, budget.Snapshot().CurrentBytes);
            SvgRenderLimits limits = SvgRenderLimits.Default;
            SvgRenderLimits denied = new(limits.MaximumAccountedBytes, limits.MaximumInputBytes, limits.MaximumDimension, limits.MaximumPixels,
                limits.MaximumEncodedPngBytes, limits.MaximumNodes, limits.MaximumPathCommands, checked((int)(parseWork - 1)), limits.MaximumXmlDepth, limits.MaximumAttributesPerElement);
            Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, denied, budget, fonts, CancellationToken.None)).ErrorCode);
            Assert.Equal(73, budget.Snapshot().CurrentBytes);
            using (SvgRoot retryRoot = SvgRootParser.Parse(input, limits, budget, fonts, CancellationToken.None))
            using (SvgConverter.RasterizedSurface retry = SvgConverter.Rasterize(retryRoot, options, budget, CancellationToken.None))
                Assert.Equal(expected, retry.Pixels);
            Assert.Equal(73, budget.Snapshot().CurrentBytes);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        Assert.NotEmpty(SvgConverter.ConvertToPng(input, options, CancellationToken.None).ToArray());
    }

    public static IEnumerable<object[]> DormantMaskText()
    {
        foreach (bool group in new[] { false, true })
        foreach (string mode in new[] { "unused", "disabled-mask", "disabled-target", "hidden-content", "empty-region" })
        foreach (string family in new[] { "Example", "Unavailable" })
        foreach (bool shader in new[] { false, true })
            yield return [group, mode, family, shader];
    }

    [Theory]
    [MemberData(nameof(DormantMaskText))]
    public async Task Dormant_mask_text_needs_no_font_program_or_catalog_face(bool group, string mode, string family, bool shader)
    {
        string glyphs = "<text x='2' y='14' font-family='" + family + "' font-size='200' fill='white'>A</text>";
        string definitions = group ? "" : glyphs.Replace("<text ", "<text id='t' ", StringComparison.Ordinal);
        string content = group ? "<g>" + glyphs + "</g>" : "<use href='#t'/>";
        if (mode == "hidden-content") content = group ? "<g visibility='hidden'>" + glyphs + "</g>" : "<use href='#t' visibility='hidden'/>";
        if (shader)
        {
            definitions += "<linearGradient id='paint'><stop stop-color='white'/><stop offset='1' stop-color='gray'/></linearGradient>";
            content = content.Replace("<use ", "<use fill='url(#paint)' ", StringComparison.Ordinal)
                .Replace("<g", "<g fill='url(#paint)'", StringComparison.Ordinal);
        }
        definitions += "<mask id='m' maskUnits='userSpaceOnUse' x='0' y='0' width='" + (mode == "empty-region" ? "0" : "32") + "' height='32'" + (mode == "disabled-mask" ? " style='display:none'" : "") + ">" + content + "</mask>";
        string attributes = mode == "unused" ? "" : " mask='url(#m)'";
        if (mode == "disabled-target") attributes += " display='none'";
        const string prefix = "<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'>";
        byte[] input = System.Text.Encoding.UTF8.GetBytes(prefix + "<defs>" + definitions + "</defs><rect width='32' height='32' fill='red'" + attributes + "/></svg>"), unchanged = input.ToArray();
        byte[] control = System.Text.Encoding.UTF8.GetBytes(prefix + (mode is "unused" or "disabled-mask" ? "<rect width='32' height='32' fill='red'/>" : "") + "</svg>");
        byte[] expected = SvgConverter.ConvertToPng(control, SvgRenderOptions.Default, CancellationToken.None).ToArray();
        Assert.Equal(expected, SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None).ToArray());
        (var catalog, var provider, _) = CreateExamplePack(CreateTriangleFont());
        using SvgPreparedDocument prepared = await SvgPreparedDocument.CreateAsync(input, catalog, provider, SvgRenderOptions.Default, CancellationToken.None);
        Assert.Equal(expected, prepared.ConvertToPng(CancellationToken.None).ToArray());
        Assert.Equal(0, provider.RequestCount);
        Assert.Equal(unchanged, input);
    }
}
