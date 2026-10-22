using Lokad.Svg.Fonts;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed partial class OpenTypeFontTests
{
    private const string PatternFontSize = "width='16' height='16'";
    private const string PatternFontText = "<text x='1' y='8' font-family='Example' font-size='200'>A</text>";
    private const string PatternFontTarget = "<rect width='32' height='32' fill='url(#p)'/>";

    [Theory]
    [InlineData("", false)]
    [InlineData("", true)]
    [InlineData("display='none'", false)]
    [InlineData("display='none'", true)]
    [InlineData("style='display:none'", false)]
    [InlineData("style='display:none'", true)]
    [InlineData("class='hidden'", false)]
    [InlineData("class='hidden'", true)]
    public async Task Pattern_font_preparation_ignores_resource_and_definition_ancestor_display(string display, bool ancestor)
    {
        foreach (bool multipleFiles in new[] { false, true })
        {
            (SvgFontPackCatalog catalog, CountingFontProvider provider, CountingFontSource source) = PatternFontPack(multipleFiles);
            string definition = ancestor
                ? "<g " + display + "><defs>" + PatternFontDefinition(PatternFontSize, PatternFontText) + "</defs></g>"
                : "<defs>" + PatternFontDefinition(PatternFontSize + " " + display, PatternFontText) + "</defs>";
            byte[] input = PatternFontInput("<style>.hidden{display:none}</style>" + definition + PatternFontTarget);
            Assert.Equal("Example", Assert.Single(SvgFontRequestDiscovery.Discover(input, CancellationToken.None)).Family);
            using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync(input, catalog, provider, CancellationToken.None);
            Assert.Single(snapshot.Fonts);
            byte[] expected = SvgConverter.ConvertToPng(input, new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, snapshot), CancellationToken.None).ToArray();
            Assert.False(SvgConverter.ConvertToPng(PatternFontInput(""), SvgRenderOptions.Default, CancellationToken.None).Span.SequenceEqual(expected));
            using SvgPreparedDocument document = await SvgPreparedDocument.CreateAsync(input, catalog, provider, SvgRenderOptions.Default, CancellationToken.None);
            Assert.Equal(expected, document.ConvertToPng(CancellationToken.None).ToArray());
            Assert.Null(document.RetainedSceneGeometries);
            Assert.Equal(2, provider.RequestCount);
            Assert.Equal(2, source.RequestCount);
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Pattern_font_templates_keep_requests_in_both_definition_orders(bool hiddenBase, bool forward)
    {
        string sourceDefinition = PatternFontDefinition(PatternFontSize + (hiddenBase ? " display='none'" : ""), PatternFontText).Replace("id='p'", "id='base'", StringComparison.Ordinal);
        string alias = "<pattern id='p' href='#base'" + (hiddenBase ? "" : " display='none'") + "/>";
        byte[] input = PatternFontInput("<defs>" + (forward ? alias + sourceDefinition : sourceDefinition + alias) + "</defs>" + PatternFontTarget);
        foreach (bool multipleFiles in new[] { false, true })
        {
            (SvgFontPackCatalog catalog, CountingFontProvider provider, CountingFontSource source) = PatternFontPack(multipleFiles);
            using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync([CreateResolution(CreateTriangleFont())], CancellationToken.None);
            byte[] expected = SvgConverter.ConvertToPng(input, new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, snapshot), CancellationToken.None).ToArray();
            using SvgPreparedDocument document = await SvgPreparedDocument.CreateAsync(input, catalog, provider, SvgRenderOptions.Default, CancellationToken.None);
            Assert.Equal(expected, document.ConvertToPng(CancellationToken.None).ToArray());
            Assert.Equal(1, provider.RequestCount);
            Assert.Equal(1, source.RequestCount);
        }
    }

    [Theory]
    [InlineData("width='0' height='16'", "")]
    [InlineData("width='16' height='0'", "")]
    [InlineData("width='16'", "")]
    [InlineData("height='16'", "")]
    [InlineData("width='16' height='16'", "display='none'")]
    [InlineData("width='16' height='16' visibility='hidden'", "")]
    public async Task Pattern_font_inert_tiles_and_children_do_not_acquire_fonts(string dimensions, string childDisplay)
    {
        byte[] input = PatternFontInput("<defs>" + PatternFontDefinition(dimensions + " display='none'", "<g " + childDisplay + ">" + PatternFontText + "</g>") + "</defs>" + PatternFontTarget);
        foreach (bool multipleFiles in new[] { false, true })
        {
            (SvgFontPackCatalog catalog, CountingFontProvider provider, CountingFontSource source) = PatternFontPack(multipleFiles);
            Assert.Empty(SvgFontRequestDiscovery.Discover(input, CancellationToken.None));
            using SvgPreparedDocument document = await SvgPreparedDocument.CreateAsync(input, catalog, provider, SvgRenderOptions.Default, CancellationToken.None);
            Assert.Equal(SvgConverter.ConvertToPng(PatternFontInput(""), SvgRenderOptions.Default, CancellationToken.None).ToArray(), document.ConvertToPng(CancellationToken.None).ToArray());
            Assert.Equal(0, provider.RequestCount);
            Assert.Equal(0, source.RequestCount);
        }
    }

    [Theory]
    [InlineData("visibility='visible'", "")]
    [InlineData("visibility='hidden'", "visibility='visible'")]
    public async Task Pattern_font_display_reset_preserves_visibility_overrides_and_inherited_paint(string visibility, string childVisibility)
    {
        byte[] input = PatternFontInput("<g fill='blue' display='none' " + visibility + "><defs>" + PatternFontDefinition(PatternFontSize + " style='display:none'", "<g " + childVisibility + ">" + PatternFontText + "</g>") + "</defs></g>" + PatternFontTarget);
        byte[] control = PatternFontInput("<g fill='blue'><defs>" + PatternFontDefinition(PatternFontSize, PatternFontText) + "</defs></g>" + PatternFontTarget);
        (SvgFontPackCatalog catalog, CountingFontProvider provider, _) = PatternFontPack(false);
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync([CreateResolution(CreateTriangleFont())], CancellationToken.None);
        using SvgPreparedDocument document = await SvgPreparedDocument.CreateAsync(input, catalog, provider, SvgRenderOptions.Default, CancellationToken.None);
        Assert.Equal(SvgConverter.ConvertToPng(control, new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, snapshot), CancellationToken.None).ToArray(), document.ConvertToPng(CancellationToken.None).ToArray());
    }

    [Fact]
    public async Task Pattern_font_discovery_and_preparation_release_on_denial_cancellation_and_retry()
    {
        byte[] input = PatternFontInput("<defs>" + PatternFontDefinition(PatternFontSize + " display='none'", PatternFontText) + "</defs>" + PatternFontTarget);
        byte[] unchanged = input.ToArray();
        RenderBudget measured = new(1_000_000);
        ParserWorkCounter work = new(1_000_000);
        Assert.Single(SvgFontRequestDiscovery.Discover(input, SvgRenderLimits.Default, work, measured, CancellationToken.None));
        Assert.Equal(0, measured.Snapshot().CurrentBytes);
        RenderBudget denied = new(measured.Snapshot().PeakBytes - 1);
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgFontRequestDiscovery.Discover(input, SvgRenderLimits.Default, new(1_000_000), denied, CancellationToken.None)).ErrorCode);
        Assert.Equal(0, denied.Snapshot().CurrentBytes);
        RenderBudget workDenied = new(1_000_000);
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgFontRequestDiscovery.Discover(input, SvgRenderLimits.Default, new(work.Used - 1), workDenied, CancellationToken.None)).ErrorCode);
        Assert.Equal(0, workDenied.Snapshot().CurrentBytes);
        (SvgFontPackCatalog catalog, CountingFontProvider provider, _) = PatternFontPack(false);
        Assert.Equal(SvgErrorCode.UnresolvedResource, (await Assert.ThrowsAsync<SvgException>(async () => await SvgPreparedDocument.CreateAsync(input, catalog, new MissingFontProvider(), SvgRenderOptions.Default, CancellationToken.None))).ErrorCode);
        using CancellationTokenSource cancellation = new();
        using (FontIntegrityProbe.Arm(cancellation.Cancel))
            Assert.Equal(cancellation.Token, (await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await SvgPreparedDocument.CreateAsync(input, catalog, provider, SvgRenderOptions.Default, cancellation.Token))).CancellationToken);
        using SvgPreparedDocument retry = await SvgPreparedDocument.CreateAsync(input, catalog, provider, SvgRenderOptions.Default, CancellationToken.None);
        Assert.NotEmpty(retry.ConvertToPng(CancellationToken.None).ToArray());
        Assert.Null(retry.RetainedSceneGeometries);
        Assert.Equal(unchanged, input);
    }

    private static (SvgFontPackCatalog Catalog, CountingFontProvider Provider, CountingFontSource Source) PatternFontPack(bool multipleFiles)
    {
        byte[] font = CreateTriangleFont();
        (SvgFontPackCatalog catalog, CountingFontProvider provider, CountingFontSource source) = CreateExamplePack(font);
        if (!multipleFiles) return (catalog, provider, source);
        string manifest = System.Text.Json.JsonSerializer.Serialize(new
        {
            packId = "test-pack",
            files = new[] { new { relativePath = "files/example.ttf", byteSize = font.Length, sha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(font)) },
                new { relativePath = "files/unused.ttf", byteSize = 1, sha256 = new string('0', 64) } },
            families = new[] { new { requestedFamily = "Example", resolvedFamily = "Example", relativeFontFile = "files/example.ttf" } }
        });
        return (SvgFontPackCatalog.Parse(System.Text.Encoding.UTF8.GetBytes(manifest), "test-pack"), provider, source);
    }

    private static string PatternFontDefinition(string attributes, string content)
        => "<pattern id='p' patternUnits='userSpaceOnUse' " + attributes + ">" + content + "</pattern>";

    private static byte[] PatternFontInput(string content)
        => System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32' fill='red'>" + content + "</svg>");
}
