namespace Lokad.Svg.Tests;

using Lokad.Svg.Fonts;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

public sealed partial class OpenTypeFontTests
{
    [Theory]
    [InlineData("<defs><text id='label' font-family='Example' font-size='20' x='3' y='25'>A</text><g id='unused' font-family='NotInCatalog'><text>A</text></g></defs>", "<use href='#label' x='32'/>")]
    [InlineData("<defs><g id='label' font-family='Example' font-size='20'><text x='3' y='25'>A</text></g></defs>", "<use href='#label' x='32'/>")]
    [InlineData("<defs><symbol id='label' viewBox='0 0 32 48' font-family='Example' font-size='20'><text x='3' y='25'>A</text></symbol></defs>", "<use href='#label' x='32' width='32' height='48'/>")]
    [InlineData("<defs><text id='target' x='3' y='25'>A</text><g id='label'><use href='#target' font-family='Example' font-size='20'/></g></defs>", "<use href='#label' x='32'/>")]
    [InlineData("<style>.owner > text > tspan { font-family: Example !important; font-size:20px; }</style><defs><g class='owner' font-family='NotInCatalog'><text id='label' x='3' y='25'><tspan>A</tspan></text></g></defs>", "<use href='#label' x='32'/>")]
    public async Task Referenced_text_sources_discover_only_needed_faces_and_match_direct_layout(string definitions, string instance)
    {
        const string prefix = "<svg xmlns='http://www.w3.org/2000/svg' width='64' height='48' font-family='NotInCatalog'>";
        byte[] input = System.Text.Encoding.UTF8.GetBytes(prefix + instance + definitions + "</svg>"), unchanged = input.ToArray();
        SvgFontFaceRequest request = Assert.Single(SvgFontRequestDiscovery.Discover(input, CancellationToken.None));
        Assert.Equal("Example", request.Family);
        Assert.Equal(400, request.Weight);
        Assert.False(request.Italic);
        (SvgFontPackCatalog catalog, CountingFontProvider provider, CountingFontSource source) = CreateExamplePack(CreateTriangleFont());
        using SvgPreparedDocument document = await SvgPreparedDocument.CreateAsync(input, catalog, provider, SvgRenderOptions.Default, CancellationToken.None);
        Assert.Equal(1, provider.RequestCount); Assert.Equal(1, source.RequestCount);
        byte[] direct = System.Text.Encoding.UTF8.GetBytes(prefix + "<text x='35' y='25' font-family='Example' font-size='20'>A</text></svg>");
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync([CreateResolution(CreateTriangleFont())], CancellationToken.None);
        SvgRenderOptions options = new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, snapshot);
        Assert.Equal(SvgConverter.ConvertToPng(direct, options, CancellationToken.None).ToArray(), document.ConvertToPng(CancellationToken.None).ToArray());
        Assert.Equal(unchanged, input);
    }

    [Theory]
    [InlineData("<defs><g id='label' font-family='NotInCatalog'><text>A</text></g></defs>")]
    [InlineData("<defs><g id='label' font-family='NotInCatalog' display='none'><text>A</text></g></defs><use href='#label'/>")]
    [InlineData("<defs><g id='label' font-family='NotInCatalog'><switch><rect width='1' height='1'/><text>A</text></switch></g></defs><use href='#label'/>")]
    public async Task Unused_hidden_and_unselected_source_faces_are_not_requested(string body)
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'>" + body + "</svg>");
        Assert.Empty(SvgFontRequestDiscovery.Discover(input, CancellationToken.None));
        (SvgFontPackCatalog catalog, CountingFontProvider provider, CountingFontSource source) = CreateExamplePack(CreateTriangleFont());
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync(input, catalog, provider, CancellationToken.None);
        Assert.Equal(0, provider.RequestCount); Assert.Equal(0, source.RequestCount);
    }

    [Fact]
    public async Task Original_unused_symbol_text_remains_blank_without_acquiring_a_face()
    {
        byte[] input = "<svg xmlns='http://www.w3.org/2000/svg'><symbol id='s'><text font-family='Unused'>x</text></symbol></svg>"u8.ToArray();
        Assert.Empty(SvgFontRequestDiscovery.Discover(input, CancellationToken.None));
        (SvgFontPackCatalog catalog, CountingFontProvider provider, CountingFontSource source) = CreateExamplePack(CreateTriangleFont());
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync(input, catalog, provider, CancellationToken.None);
        SvgRenderOptions options = new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, snapshot);
        Assert.Equal(SvgConverter.ConvertToPng("<svg xmlns='http://www.w3.org/2000/svg'/>"u8.ToArray(), options, CancellationToken.None).ToArray(), SvgConverter.ConvertToPng(input, options, CancellationToken.None).ToArray());
        Assert.Equal(0, provider.RequestCount); Assert.Equal(0, source.RequestCount);
    }

    [Fact]
    public async Task Original_referenced_symbol_text_requires_its_missing_catalog_face()
    {
        byte[] input = "<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><symbol id='s'><text font-family='Unused'>x</text></symbol><use href='#s'/></svg>"u8.ToArray();
        Assert.Equal("Unused", Assert.Single(SvgFontRequestDiscovery.Discover(input, CancellationToken.None)).Family);
        (SvgFontPackCatalog catalog, CountingFontProvider provider, CountingFontSource source) = CreateExamplePack(CreateTriangleFont());
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync(input, catalog, provider, CancellationToken.None);
        SvgRenderOptions options = new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, snapshot);
        SvgException failure = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(input, options, CancellationToken.None));
        Assert.Equal(SvgErrorCode.UnresolvedResource, failure.ErrorCode);
        Assert.Equal(1, provider.RequestCount); Assert.Equal(1, source.RequestCount);
    }

    [Fact]
    public async Task Rendered_text_id_remains_painted_and_can_be_instanced()
    {
        const string prefix = "<svg xmlns='http://www.w3.org/2000/svg' width='64' height='48' font-family='Example' font-size='20'>";
        byte[] input = System.Text.Encoding.UTF8.GetBytes(prefix + "<text id='label' x='3' y='25'>A</text><use href='#label' x='32'/></svg>");
        byte[] direct = System.Text.Encoding.UTF8.GetBytes(prefix + "<text x='3' y='25'>A</text><text x='35' y='25'>A</text></svg>");
        (SvgFontPackCatalog catalog, CountingFontProvider provider, CountingFontSource source) = CreateExamplePack(CreateTriangleFont());
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync(input, catalog, provider, CancellationToken.None);
        SvgRenderOptions options = new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, snapshot);
        Assert.Equal(SvgConverter.ConvertToPng(direct, options, CancellationToken.None).ToArray(), SvgConverter.ConvertToPng(input, options, CancellationToken.None).ToArray());
        Assert.Equal(1, provider.RequestCount); Assert.Equal(1, source.RequestCount);
    }

    [Fact]
    public void Instance_font_graph_releases_storage_after_material_denial_and_retry()
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg'><use href='#label'/><defs><g id='label' font-family='Example'><text>" + new string('A', 8192) + "<tspan dx='3'>A</tspan></text></g></defs></svg>");
        RenderBudget accepted = new(8_000_000);         Assert.Single(SvgFontRequestDiscovery.Discover(input, SvgRenderLimits.Default, accepted, CancellationToken.None));
        Assert.Equal(0, accepted.Snapshot().CurrentBytes);
        RenderBudget denied = new(accepted.Snapshot().PeakBytes * 3 / 4);
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgFontRequestDiscovery.Discover(input, SvgRenderLimits.Default, denied, CancellationToken.None)).ErrorCode);
        Assert.Equal(0, denied.Snapshot().CurrentBytes);
        Assert.Equal(0, accepted.Snapshot().CurrentBytes);
        using CancellationTokenSource cancelled = new(); cancelled.Cancel();
        Assert.Equal(cancelled.Token, Assert.Throws<OperationCanceledException>(() => SvgFontRequestDiscovery.Discover(input, SvgRenderLimits.Default, accepted, cancelled.Token)).CancellationToken);
        Assert.Equal(0, accepted.Snapshot().CurrentBytes);
        Assert.Single(SvgFontRequestDiscovery.Discover(input, SvgRenderLimits.Default, accepted, CancellationToken.None));
        Assert.Equal(0, accepted.Snapshot().CurrentBytes);
    }

    [Fact]
    public async Task Cyclic_text_sources_reject_before_acquiring_a_face()
    {
        byte[] input = "<svg xmlns='http://www.w3.org/2000/svg'><use href='#a'/><defs><g id='a' font-family='Example'><text>A</text><use href='#a'/></g></defs></svg>"u8.ToArray();
        Assert.Equal(SvgErrorCode.UnsupportedFeature, Assert.Throws<SvgException>(() => SvgFontRequestDiscovery.Discover(input, CancellationToken.None)).ErrorCode);
        (SvgFontPackCatalog catalog, CountingFontProvider provider, CountingFontSource source) = CreateExamplePack(CreateTriangleFont());
        SvgException failure = await Assert.ThrowsAsync<SvgException>(async () => await SvgPreparedDocument.CreateAsync(input, catalog, provider, SvgRenderOptions.Default, CancellationToken.None));
        Assert.Equal(SvgErrorCode.UnsupportedFeature, failure.ErrorCode);
        Assert.Equal(0, provider.RequestCount); Assert.Equal(0, source.RequestCount);
    }
}
