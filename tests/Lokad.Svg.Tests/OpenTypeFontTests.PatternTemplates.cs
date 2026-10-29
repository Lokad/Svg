using Lokad.Svg.Encoding;
using Lokad.Svg.Fonts;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed partial class OpenTypeFontTests
{
    [Theory]
    [InlineData("rect")]
    [InlineData("circle")]
    [InlineData("image")]
    [InlineData("varied-image")]
    [InlineData("text")]
    [InlineData("clip")]
    [InlineData("filter")]
    public async Task Pattern_template_content_survives_zero_or_missing_base_dimensions(string kind)
    {
        byte[] pixels = kind == "varied-image" ? [255, 0, 0, 255, 0, 0, 255, 255, 0, 255, 0, 255, 255, 255, 0, 255] : [255, 0, 0, 255, 255, 0, 0, 255, 255, 0, 0, 255, 255, 0, 0, 255];
        string image = "<image width='8' height='8' href='data:image/png;base64," + Convert.ToBase64String(PngEncoder.Encode(2, 2, pixels, CancellationToken.None)) + "'/>";
        string content = kind switch
        {
            "rect" => "<rect width='8' height='8' fill='red'/>",
            "circle" => "<circle cx='4' cy='4' r='4' fill='red'/>",
            "image" or "varied-image" => image,
            "text" => PatternFontText,
            "clip" => "<rect width='8' height='8' fill='red' clip-path='url(#c)'/>",
            "filter" => "<rect width='8' height='8' fill='red' filter='url(#f)'/>",
            _ => throw new InvalidOperationException()
        };
        const string resources = "<defs><clipPath id='c'><rect width='6' height='16'/></clipPath><filter id='f' filterUnits='userSpaceOnUse' x='0' y='0' width='32' height='32'><feOffset dx='1' dy='1'/></filter></defs>";
        using SvgFontSnapshot fonts = await SvgFontSnapshot.CreateAsync([CreateResolution(CreateTriangleFont())], CancellationToken.None);
        SvgRenderOptions options = new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, fonts);
        byte[] expected = SvgConverter.ConvertToPng(PatternFontInput(resources + "<defs>" + PatternFontDefinition(PatternFontSize, content) + "</defs>" + PatternFontTarget), options, CancellationToken.None).ToArray();
        foreach (string dimensions in new[] { PatternFontSize, "width='0' height='16'", "width='16' height='0'", "width='16'", "" })
        foreach (bool forward in new[] { false, true })
        foreach (bool multipleFiles in new[] { false, true })
        {
            string source = PatternFontDefinition(dimensions, content).Replace("id='p'", "id='base'", StringComparison.Ordinal);
            const string alias = "<pattern id='p' href='#base' width='16' height='16'/>";
            byte[] input = PatternFontInput(resources + "<defs>" + (forward ? alias + source : source + alias) + "</defs>" + PatternFontTarget);
            Assert.True(expected.AsSpan().SequenceEqual(SvgConverter.ConvertToPng(input, options, CancellationToken.None).Span), kind + "; " + dimensions + "; forward=" + forward);
            (SvgFontPackCatalog catalog, CountingFontProvider provider, CountingFontSource fontSource) = PatternFontPack(multipleFiles);
            using SvgPreparedDocument document = await SvgPreparedDocument.CreateAsync(input, catalog, provider, SvgRenderOptions.Default, CancellationToken.None);
            Assert.Equal(expected, document.ConvertToPng(CancellationToken.None).ToArray());
            Assert.Equal(kind == "text" ? 1 : 0, provider.RequestCount);
            Assert.Equal(provider.RequestCount, fontSource.RequestCount);
        }
    }

    [Theory]
    [InlineData("width='0' height='16'")]
    [InlineData("width='16' height='0'")]
    [InlineData("width='0' height='0'")]
    [InlineData("")]
    public async Task Pattern_template_effective_zero_sizes_do_not_require_fonts(string dimensions)
    {
        byte[] input = PatternFontInput("<defs>" + PatternFontDefinition("width='0' height='0'", PatternFontText).Replace("id='p'", "id='base'", StringComparison.Ordinal)
            + "<pattern id='p' href='#base' " + dimensions + "/></defs>" + PatternFontTarget);
        Assert.Empty(SvgFontRequestDiscovery.Discover(input, CancellationToken.None));
        (SvgFontPackCatalog catalog, CountingFontProvider provider, _) = PatternFontPack(false);
        using SvgPreparedDocument document = await SvgPreparedDocument.CreateAsync(input, catalog, provider, SvgRenderOptions.Default, CancellationToken.None);
        Assert.Equal(SvgConverter.ConvertToPng(PatternFontInput(""), SvgRenderOptions.Default, CancellationToken.None).ToArray(), document.ConvertToPng(CancellationToken.None).ToArray());
        Assert.Equal(0, provider.RequestCount);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Pattern_template_chains_select_only_the_content_that_survives_overrides(bool forward, bool replacement)
    {
        foreach (string aliasContent in replacement ? new[] { "<rect width='8' height='8' fill='blue'/>" } : new[] { "", "<g/>" })
        {
            string source = PatternFontDefinition("width='0' height='0'", PatternFontText).Replace("id='p'", "id='base'", StringComparison.Ordinal);
            string middle = "<pattern id='middle' href='#base' width='16'>" + aliasContent + "</pattern>";
            const string alias = "<pattern id='p' href='#middle' height='16'/>";
            byte[] input = PatternFontInput("<defs>" + (forward ? alias + middle + source : source + middle + alias) + "</defs>" + PatternFontTarget);
            bool inherits = !replacement && aliasContent.Length == 0;
            string content = replacement ? "<rect width='8' height='8' fill='blue'/>" : inherits ? PatternFontText : "";
            byte[] control = PatternFontInput("<defs>" + PatternFontDefinition(PatternFontSize, content) + "</defs>" + PatternFontTarget);
            (SvgFontPackCatalog catalog, CountingFontProvider provider, _) = PatternFontPack(true);
            using SvgFontSnapshot fonts = await SvgFontSnapshot.CreateAsync([CreateResolution(CreateTriangleFont())], CancellationToken.None);
            using SvgPreparedDocument document = await SvgPreparedDocument.CreateAsync(input, catalog, provider, SvgRenderOptions.Default, CancellationToken.None);
            Assert.Equal(SvgConverter.ConvertToPng(control, new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, fonts), CancellationToken.None).ToArray(), document.ConvertToPng(CancellationToken.None).ToArray());
            Assert.Equal(inherits ? 1 : 0, provider.RequestCount);
        }
    }

    [Theory]
    [InlineData("<desc><pattern id='p' href='#base' width='16' height='16'/></desc>")]
    [InlineData("<switch><g/><defs><pattern id='p' href='#base' width='16' height='16'/></defs></switch>")]
    public async Task Pattern_template_aliases_in_inert_content_do_not_activate_unused_text(string inert)
    {
        byte[] input = PatternFontInput("<defs>" + PatternFontDefinition("width='0' height='0'", PatternFontText).Replace("id='p'", "id='base'", StringComparison.Ordinal) + "</defs>" + inert);
        Assert.Empty(SvgFontRequestDiscovery.Discover(input, CancellationToken.None));
        (SvgFontPackCatalog catalog, CountingFontProvider provider, _) = PatternFontPack(false);
        using SvgPreparedDocument document = await SvgPreparedDocument.CreateAsync(input, catalog, provider, SvgRenderOptions.Default, CancellationToken.None);
        Assert.Equal(SvgConverter.ConvertToPng(PatternFontInput(""), SvgRenderOptions.Default, CancellationToken.None).ToArray(), document.ConvertToPng(CancellationToken.None).ToArray());
        Assert.Equal(0, provider.RequestCount);
    }

    [Fact]
    public async Task Pattern_template_live_content_releases_on_denial_cancellation_and_retry()
    {
        byte[] input = PatternFontInput("<defs>" + PatternFontDefinition("width='0' height='16'", PatternFontText).Replace("id='p'", "id='base'", StringComparison.Ordinal) + "<pattern id='p' href='#base' width='16'/></defs>" + PatternFontTarget);
        Assert.Single(SvgFontRequestDiscovery.Discover(input, CancellationToken.None));
        using SvgFontSnapshot fonts = await SvgFontSnapshot.CreateAsync([CreateResolution(CreateTriangleFont())], CancellationToken.None);
        RenderBudget measured = new(1_000_000);
        using (SvgRoot scene = SvgRootParser.Parse(input, SvgRenderLimits.Default, measured, fonts, CancellationToken.None)) { }
        Assert.Equal(0, measured.Snapshot().CurrentBytes);
        RenderBudget denied = new(measured.Snapshot().PeakBytes - 1);
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, denied, fonts, CancellationToken.None)).ErrorCode);
        Assert.Equal(0, denied.Snapshot().CurrentBytes);
        RenderBudget workDenied = new(1_000_000);
        Assert.Equal(0, workDenied.Snapshot().CurrentBytes);
        (SvgFontPackCatalog catalog, CountingFontProvider provider, _) = PatternFontPack(false);
        using CancellationTokenSource cancellation = new();
        using (FontIntegrityProbe.Arm(cancellation.Cancel))
            Assert.Equal(cancellation.Token, (await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await SvgPreparedDocument.CreateAsync(input, catalog, provider, SvgRenderOptions.Default, cancellation.Token))).CancellationToken);
        using SvgPreparedDocument retry = await SvgPreparedDocument.CreateAsync(input, catalog, provider, SvgRenderOptions.Default, CancellationToken.None);
        Assert.NotEmpty(retry.ConvertToPng(CancellationToken.None).ToArray());
        Assert.Null(retry.RetainedSceneGeometries);
    }

    [Fact]
    public void Pattern_template_content_selection_keeps_long_alias_chains_linear()
    {
        long small = Parse(256), large = Parse(1024);
        Assert.InRange(large, small, small * 5);

        static long Parse(int count)
        {
            System.Text.StringBuilder content = new("<defs>");
            content.Append("<pattern id='p0' width='0' height='0' patternUnits='userSpaceOnUse'><circle cx='4' cy='4' r='4' fill='red'/></pattern>");
            for (int index = 1; index <= count; index++) content.Append("<pattern id='p").Append(index).Append("' href='#p").Append(index - 1).Append("'/>");
            content.Append("<pattern id='p' href='#p").Append(count).Append("' width='16' height='16'/></defs>").Append(PatternFontTarget);
            RenderBudget budget = new(10_000_000);
            long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            using (SvgRoot scene = SvgRootParser.Parse(PatternFontInput(content.ToString()), SvgRenderLimits.Default, budget, null, CancellationToken.None)) { }
            Assert.Equal(0, budget.Snapshot().CurrentBytes);
            return GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        }
    }

    [Theory]
    [InlineData("1em")]
    [InlineData("50%")]
    public void Pattern_template_dimension_inspection_keeps_drawing_validation_separate(string width)
    {
        byte[] input = PatternFontInput("<defs><pattern id='base' width='" + width + "' height='16'/><pattern id='p' href='#base'/></defs>");
        Assert.Equal(new SvgViewport(32, 32), SvgConverter.InspectDimensions(input, SvgRenderOptions.Default, CancellationToken.None).LogicalViewport);
        Assert.Equal(SvgErrorCode.UnsupportedFeature, Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None)).ErrorCode);
    }
}
