using System.Security.Cryptography;
using System.Text.Json;
using Lokad.Svg.Fonts;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed partial class OpenTypeFontTests
{
    [Fact]
    public async Task Later_authored_families_precede_catalog_fallback_and_keep_span_layout()
    {
        (SvgFontPackCatalog catalog, StackFontProvider provider) = CreateStackPack();
        const string source = "<svg xmlns='http://www.w3.org/2000/svg' width='240' height='48'><style>.labels{font-family:Missing,First,serif}</style><g class='labels'><text font-size='100' x='2' y='30'>A<tspan font-family='Missing,Second'>A</tspan>A</text></g></svg>";
        byte[] svg = System.Text.Encoding.UTF8.GetBytes(source);
        SvgFontSelectionPolicy policy = new(new Dictionary<string, string> { ["serif"] = "First" }, ["Second"]);
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync(svg, catalog, provider, policy, SvgRenderLimits.Default, CancellationToken.None);
        Assert.Equal(new[] { "first.ttf", "second.ttf" }, provider.Requests);
        Assert.All(snapshot.Fonts, font => Assert.False(font.Resolution.Resolution.IsFallback));
        Assert.Equal(new[] { "First", "Second" }, snapshot.Fonts.Select(font => font.Resolution.Resolution.Face.RequestedFamily));
        byte[] actual = SvgConverter.ConvertToPng(svg, new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, snapshot), CancellationToken.None).ToArray();
        byte[] explicitSvg = System.Text.Encoding.UTF8.GetBytes(source.Replace("Missing,First,serif", "First").Replace("Missing,Second", "Second"));
        using SvgPreparedDocument expected = await SvgPreparedDocument.CreateAsync(explicitSvg, catalog, provider, SvgRenderOptions.Default, CancellationToken.None);
        Assert.Equal(expected.ConvertToPng(CancellationToken.None).ToArray(), actual);
    }

    [Fact]
    public async Task Quoted_literal_generic_and_host_generic_select_different_supplied_programs()
    {
        (SvgFontPackCatalog catalog, StackFontProvider provider) = CreateStackPack();
        const string source = "<svg xmlns='http://www.w3.org/2000/svg' width='240' height='48'><text font-family='serif' font-size='100' y='30'>AA</text><text font-family='&quot;serif&quot;' font-size='100' x='120' y='30'>AA</text></svg>";
        byte[] svg = System.Text.Encoding.UTF8.GetBytes(source);
        SvgFontFaceRequest[] requests = SvgFontRequestDiscovery.Discover(svg, CancellationToken.None);
        Assert.Equal(2, requests.Length);
        Assert.True(requests[0].Families[0].IsGeneric); Assert.False(requests[1].Families[0].IsGeneric);
        SvgRenderOptions options = SvgRenderOptions.Default.WithFontSelectionPolicy(new(new Dictionary<string, string> { ["serif"] = "First" }, []));
        using SvgPreparedDocument prepared = await SvgPreparedDocument.CreateAsync(svg, catalog, provider, options, CancellationToken.None);
        byte[] actual = prepared.ConvertToPng(CancellationToken.None).ToArray();
        Assert.Equal(new[] { "first.ttf", "second.ttf" }, provider.Requests);
        byte[] explicitSvg = System.Text.Encoding.UTF8.GetBytes(source.Replace("font-family='serif'", "font-family='First'").Replace("font-family='&quot;serif&quot;'", "font-family='Second'"));
        using SvgPreparedDocument expected = await SvgPreparedDocument.CreateAsync(explicitSvg, catalog, provider, SvgRenderOptions.Default, CancellationToken.None);
        Assert.Equal(expected.ConvertToPng(CancellationToken.None).ToArray(), actual);
    }

    [Fact]
    public void Host_policy_is_immutable_and_never_introduces_an_unconfigured_generic_or_fallback()
    {
        (SvgFontPackCatalog catalog, _) = CreateStackPack();
        var mappings = new Dictionary<string, string> { ["sans-serif"] = "First" };
        var fallbacks = new List<string> { "Second" };
        SvgFontSelectionPolicy policy = new(mappings, fallbacks);
        mappings["sans-serif"] = "Second"; fallbacks.Clear();
        SvgFontPackResolution authored = catalog.ResolveFamilies(SvgFontFaceRequest.FromCss("Missing, sans-serif", 400, false), policy);
        Assert.Equal("First", authored.Face.RequestedFamily); Assert.False(authored.IsFallback);
        SvgFontPackResolution fallback = catalog.ResolveFamilies(SvgFontFaceRequest.FromCss("Missing, monospace", 400, false), policy);
        Assert.Equal("Second", fallback.Face.RequestedFamily); Assert.True(fallback.IsFallback);
        SvgFontSelectionPolicy strict = new(new Dictionary<string, string>(), []);
        SvgException failure = Assert.Throws<SvgException>(() => catalog.ResolveFamilies(SvgFontFaceRequest.FromCss("monospace", 400, false), strict));
        Assert.Equal(SvgErrorCode.UnresolvedResource, failure.ErrorCode);
        Assert.Equal(SvgFontFailureStage.Selection, failure.FontDiagnostic?.Stage);
        // Existing explicit single-family callers retain their configured fallback.
        Assert.True(catalog.Resolve(new("Missing", 400, false)).IsFallback);
        Assert.Equal("Second", catalog.Resolve(new("Missing", 400, false)).Face.RequestedFamily);
    }

    [Fact]
    public void Authored_order_and_explicit_alias_membership_do_not_depend_on_manifest_order()
    {
        (SvgFontPackCatalog catalog, _) = CreateStackPack();
        SvgFontSelectionPolicy policy = new(new Dictionary<string, string> { ["sans-serif"] = "First" }, []);
        Assert.Equal("Second", catalog.ResolveFamilies(SvgFontFaceRequest.FromCss("Second, First", 400, false), policy).Face.RequestedFamily);
        Assert.Equal("First", catalog.ResolveFamilies(SvgFontFaceRequest.FromCss("First, Second", 400, false), policy).Face.RequestedFamily);
        Assert.Equal("First", catalog.ResolveFamilies(SvgFontFaceRequest.FromCss("Arial, sans-serif", 400, false), policy).Face.RequestedFamily);
        Assert.Throws<SvgException>(() => catalog.ResolveFamilies(SvgFontFaceRequest.FromCss("Arial", 400, false), policy));
    }

    [Fact]
    public async Task Selection_failures_keep_original_source_location_before_any_acquisition()
    {
        (SvgFontPackCatalog catalog, StackFontProvider provider) = CreateStackPack();
        byte[] svg = "<svg xmlns='http://www.w3.org/2000/svg'>\n<text id='label' font-family='Missing, monospace'>A</text></svg>"u8.ToArray();
        SvgRenderOptions options = SvgRenderOptions.Default.WithFontSelectionPolicy(new(new Dictionary<string, string>(), []));
        SvgException failure = await Assert.ThrowsAsync<SvgException>(async () => await SvgPreparedDocument.CreateAsync(svg, catalog, provider, options, CancellationToken.None));
        Assert.Equal(SvgErrorCode.UnresolvedResource, failure.ErrorCode);
        Assert.Equal(SvgFontFailureStage.Selection, failure.FontDiagnostic?.Stage);
        Assert.Equal("label", failure.SourceDiagnostic?.ElementId);
        Assert.Equal(2, failure.SourceDiagnostic?.LineNumber);
        Assert.Empty(provider.Requests);
    }

    private static (SvgFontPackCatalog Catalog, StackFontProvider Provider) CreateStackPack()
    {
        byte[] first = CreateTriangleFont(), second = CreateTriangleFont();
        WriteUInt16(second, FindTableOffset(second, 0x686D7478) + 4, 450);
        var programs = new Dictionary<string, byte[]> { ["first.ttf"] = first, ["second.ttf"] = second };
        byte[] manifest = JsonSerializer.SerializeToUtf8Bytes(new
        {
            packId = "stacks",
            files = programs.Select(pair => new { relativePath = pair.Key, byteSize = pair.Value.Length, sha256 = Convert.ToHexString(SHA256.HashData(pair.Value)) }),
            families = new[]
            {
                new { requestedFamily = "Second", resolvedFamily = "Second", relativeFontFile = "second.ttf" },
                new { requestedFamily = "First", resolvedFamily = "First", relativeFontFile = "first.ttf" },
                new { requestedFamily = "serif", resolvedFamily = "Second", relativeFontFile = "second.ttf" },
                new { requestedFamily = "serif Family", resolvedFamily = "First", relativeFontFile = "first.ttf" }
            },
            fallbacks = new[] { new { family = "Second" } }
        });
        return (SvgFontPackCatalog.Parse(manifest, "stacks"), new(programs));
    }

    [Fact]
    public async Task Legacy_prepared_literal_names_still_match_quoted_CSS_requests()
    {
        (SvgFontPackCatalog catalog, StackFontProvider provider) = CreateStackPack();
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync(catalog, [new("serif Family", 400, false)], provider, CancellationToken.None);
        Assert.Same(snapshot.Fonts[0], snapshot.GetFont(SvgFontFaceRequest.FromCss("'serif Family'", 400, false)));
    }

    private sealed class StackFontProvider(Dictionary<string, byte[]> programs) : ISvgFontProgramSourceProvider
    {
        internal List<string> Requests { get; } = [];
        /// <summary>Borrows immutable authored programs for one test operation, observes cancellation and retains no shared cache.</summary>
        public ValueTask<ISvgFontProgramSource?> GetSourceAsync(SvgFontPackFile file, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(file.RelativePath);
            return ValueTask.FromResult<ISvgFontProgramSource?>(new SvgMemoryFontProgramSource(file.RelativePath, programs[file.RelativePath]));
        }
    }
}
