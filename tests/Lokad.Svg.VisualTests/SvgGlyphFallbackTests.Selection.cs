using TextEncoding = System.Text.Encoding;
using System.Text.Json.Nodes;
using Lokad.Svg.Fonts;

namespace Lokad.Svg.VisualTests;

public sealed partial class SvgGlyphFallbackTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Authored_coverage_precedes_host_fallback_and_preserves_its_diagnostic_kind(bool authored)
    {
        (_, FontProvider provider) = Pack();
        JsonObject manifest = JsonNode.Parse(File.ReadAllText(Path.Combine(DirectoryPath, "font-pack.json"))) as JsonObject ?? throw new InvalidDataException();
        JsonArray faces = manifest["families"] as JsonArray ?? throw new InvalidDataException();
        faces.Add(new JsonObject { ["requestedFamily"] = "Host", ["resolvedFamily"] = "FallbackSecondary", ["relativeFontFile"] = "fonts/Secondary.ttf" });
        SvgFontPackCatalog catalog = SvgFontPackCatalog.Parse(TextEncoding.UTF8.GetBytes(manifest.ToJsonString()), "public-glyph-fallback");
        string family = authored ? "FallbackPrimary,FallbackSecondary" : "FallbackPrimary,Missing";
        byte[] input = TextEncoding.UTF8.GetBytes($"<svg xmlns='http://www.w3.org/2000/svg'><text font-family='{family}'>AéA</text></svg>");
        SvgFontSelectionPolicy policy = new(new Dictionary<string, string>(), ["Host"]);
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync(input, catalog, provider, policy, SvgRenderLimits.Default, CancellationToken.None);
        Assert.Equal(new[] { "fonts/Primary.ttf", "fonts/Secondary.ttf" }, provider.Acquisitions);
        Assert.Equal(2, snapshot.Fonts.Count);
        Assert.Equal(authored ? "FallbackSecondary" : "Host", snapshot.Fonts[1].Resolution.Resolution.Face.RequestedFamily);
        Assert.Equal(!authored, snapshot.Fonts[1].Resolution.Resolution.IsFallback);
    }

    [Fact]
    public async Task Disabled_fallback_keeps_a_missing_scalar_failure_and_raw_conversion_does_no_acquisition()
    {
        (SvgFontPackCatalog catalog, FontProvider provider) = Pack();
        byte[] input = """<svg xmlns="http://www.w3.org/2000/svg"><text id="label" font-family="FallbackPrimary">AéA</text></svg>"""u8.ToArray();
        SvgFontSelectionPolicy policy = new(new Dictionary<string, string>(), []);
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync(input, catalog, provider, policy, SvgRenderLimits.Default, CancellationToken.None);
        SvgException failure = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(input, new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, snapshot), CancellationToken.None));
        Assert.Equal(SvgFontFailureStage.Glyph, failure.FontDiagnostic?.Stage);
        Assert.Equal(233, failure.FontDiagnostic?.UnicodeScalar); Assert.Equal(1, failure.FontDiagnostic?.ScalarIndex);
        Assert.Equal("label", failure.SourceDiagnostic?.ElementId);
        Assert.Equal(new[] { "fonts/Primary.ttf" }, provider.Acquisitions);
    }

    [Fact]
    public async Task Request_only_preparation_does_not_claim_unseen_coverage_alternatives()
    {
        (SvgFontPackCatalog catalog, FontProvider provider) = Pack();
        byte[] input = """<svg xmlns="http://www.w3.org/2000/svg"><text font-family="FallbackPrimary,FallbackSecondary">AéA</text></svg>"""u8.ToArray();
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync(catalog, [SvgFontFaceRequest.FromCss("FallbackPrimary,FallbackSecondary", 400, false)], provider, CancellationToken.None);
        SvgException failure = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(input, new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, snapshot), CancellationToken.None));
        Assert.Equal(SvgFontFailureStage.Glyph, failure.FontDiagnostic?.Stage);
        Assert.Equal(new[] { "fonts/Primary.ttf" }, provider.Acquisitions);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Unused_bad_program_is_not_opened_and_required_bad_program_fails_meaningfully(bool corrupt)
    {
        (SvgFontPackCatalog catalog, FontProvider provider) = Pack();
        if (corrupt) provider.Programs["fonts/Secondary.ttf"] = new byte[provider.Programs["fonts/Secondary.ttf"].Length];
        else provider.Programs.Remove("fonts/Secondary.ttf");
        byte[] covered = """<svg xmlns="http://www.w3.org/2000/svg"><text font-family="FallbackPrimary,FallbackSecondary">AAA</text></svg>"""u8.ToArray();
        using (SvgFontSnapshot unused = await SvgFontSnapshot.CreateAsync(covered, catalog, provider, CancellationToken.None)) Assert.Single(unused.Fonts);
        Assert.Equal(new[] { "fonts/Primary.ttf" }, provider.Acquisitions);
        byte[] required = """<svg xmlns="http://www.w3.org/2000/svg"><text id="bad-source" font-family="FallbackPrimary,FallbackSecondary">AéA</text></svg>"""u8.ToArray();
        SvgException failure = await Assert.ThrowsAsync<SvgException>(async () => await SvgPreparedDocument.CreateAsync(required, catalog, provider, SvgRenderOptions.Default, CancellationToken.None));
        Assert.Equal(corrupt ? SvgFontFailureStage.Integrity : SvgFontFailureStage.Source, failure.FontDiagnostic?.Stage);
        Assert.Equal("bad-source", failure.SourceDiagnostic?.ElementId);
        Assert.Equal("fonts/Secondary.ttf", provider.Acquisitions[^1]);
    }

    [Fact]
    public async Task Cancellation_during_alternative_acquisition_preserves_the_token_and_allows_retry()
    {
        (SvgFontPackCatalog catalog, FontProvider provider) = Pack();
        byte[] input = """<svg xmlns="http://www.w3.org/2000/svg"><text font-family="FallbackPrimary,FallbackSecondary">AéA</text></svg>"""u8.ToArray();
        using CancellationTokenSource cancellation = new();
        provider.BeforeAcquire = (path, token) => { if (path == "fonts/Secondary.ttf") { Assert.Equal(cancellation.Token, token); cancellation.Cancel(); } };
        OperationCanceledException failure = await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await SvgPreparedDocument.CreateAsync(input, catalog, provider, SvgRenderOptions.Default, cancellation.Token));
        Assert.Equal(cancellation.Token, failure.CancellationToken);
        provider.BeforeAcquire = null;
        using SvgPreparedDocument retry = await SvgPreparedDocument.CreateAsync(input, catalog, provider, SvgRenderOptions.Default, CancellationToken.None);
        Assert.NotEmpty(retry.ConvertToPng(CancellationToken.None).ToArray());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Normalized_spans_and_local_instances_share_the_same_coverage_layout(bool instance)
    {
        (SvgFontPackCatalog catalog, FontProvider provider) = Pack();
        const string label = "<text id='label' x='12' y='38' font-family='FallbackPrimary,FallbackSecondary' font-size='30'>  A <tspan>é</tspan>A </text>";
        string body = instance ? "<defs>" + label + "</defs><use href='#label'/>" : label;
        byte[] input = TextEncoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='160' height='56'>" + body + "</svg>");
        using SvgPreparedDocument actual = await SvgPreparedDocument.CreateAsync(input, catalog, provider, SvgRenderOptions.Default, CancellationToken.None);
        Assert.Equal(new[] { "fonts/Primary.ttf", "fonts/Secondary.ttf" }, provider.Acquisitions);
        byte[] explicitFaces = TextEncoding.UTF8.GetBytes(TextEncoding.UTF8.GetString(input).Replace("<tspan>é</tspan>", "<tspan font-family='FallbackSecondary'>é</tspan>").Replace("font-family='FallbackPrimary,FallbackSecondary'", "font-family='FallbackPrimary'"));
        using SvgPreparedDocument expected = await SvgPreparedDocument.CreateAsync(explicitFaces, catalog, provider, SvgRenderOptions.Default, CancellationToken.None);
        Assert.Equal(expected.ConvertToPng(CancellationToken.None).ToArray(), actual.ConvertToPng(CancellationToken.None).ToArray());
    }
}
