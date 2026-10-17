using Lokad.Svg.Fonts;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed partial class OpenTypeFontTests
{
    [Fact]
    public async Task Missing_scalar_index_counts_original_run_scalars_after_a_ligature_and_releases_scratch()
    {
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync([CreateResolution(CreateLigatureFont(false))], CancellationToken.None);
        RenderBudget budget = new(4_096);
        SvgException error = Assert.Throws<SvgException>(() => SvgGlyphLayout.CreateStorage(Assert.Single(snapshot.Fonts),
            "AAAé", 20, 0, 30, SvgTextAnchor.Start, budget, CancellationToken.None));
        SvgFontDiagnostic font = Assert.IsType<SvgFontDiagnostic>(error.FontDiagnostic);
        Assert.Equal(0xE9, font.UnicodeScalar); Assert.Equal(3, font.ScalarIndex);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public async Task Unprepared_bold_italic_request_is_actionable_and_deliberate_catalog_fallback_still_works()
    {
        (SvgFontPackCatalog catalog, CountingFontProvider provider, _) = CreateExamplePack(CreateTriangleFont());
        using SvgFontSnapshot supplied = await SvgFontSnapshot.CreateAsync(catalog,
            new[] { new SvgFontFaceRequest("Example", 400, false) }, provider, CancellationToken.None);
        byte[] svg = "<svg xmlns='http://www.w3.org/2000/svg' width='64' height='64'>\n <text id='label' font-family='Missing' font-weight='700' font-style='italic' x='4' y='40'>A</text>\n</svg>"u8.ToArray();
        SvgException error = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(svg,
            new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, supplied), CancellationToken.None));
        SvgFontDiagnostic font = Assert.IsType<SvgFontDiagnostic>(error.FontDiagnostic);
        Assert.Equal(SvgErrorCode.UnresolvedResource, error.ErrorCode);
        Assert.Equal(SvgFontFailureStage.Source, font.Stage);
        Assert.Equal("Missing", font.RequestedFamily); Assert.Equal(700, font.RequestedWeight); Assert.True(font.RequestedItalic);
        Assert.Equal("label", font.SourceElementId);
        Assert.Equal(2, Assert.IsType<SvgSourceDiagnostic>(error.SourceDiagnostic).LineNumber);
        using SvgFontSnapshot selected = await SvgFontSnapshot.CreateAsync(svg, catalog, provider, CancellationToken.None);
        Assert.True(Assert.Single(selected.Fonts).Resolution.Resolution.IsFallback);
        Assert.NotEmpty(SvgConverter.ConvertToPng(svg, new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, selected), CancellationToken.None).ToArray());
    }

    [Fact]
    public async Task Missing_glyph_reports_scalar_index_face_and_source_in_raw_and_prepared_paths()
    {
        (SvgFontPackCatalog catalog, CountingFontProvider provider, _) = CreateExamplePack(CreateTriangleFont());
        byte[] svg = "<svg xmlns='http://www.w3.org/2000/svg' width='64' height='64'>\n <text id='label' font-family='Example'>AAé</text>\n</svg>"u8.ToArray();
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync(svg, catalog, provider, CancellationToken.None);
        SvgException raw = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(svg,
            new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, snapshot), CancellationToken.None));
        SvgException prepared = await Assert.ThrowsAsync<SvgException>(async () =>
            await SvgPreparedDocument.CreateAsync(svg, catalog, provider, SvgRenderOptions.Default, CancellationToken.None));
        foreach (SvgException error in new[] { raw, prepared })
        {
            SvgFontDiagnostic font = Assert.IsType<SvgFontDiagnostic>(error.FontDiagnostic);
            Assert.Equal(SvgFontFailureStage.Glyph, font.Stage); Assert.Equal(0xE9, font.UnicodeScalar); Assert.Equal(2, font.ScalarIndex);
            Assert.Equal(400, font.RequestedWeight); Assert.False(font.RequestedItalic); Assert.False(font.IsFallback);
            Assert.NotEmpty(font.ResolvedFamily); Assert.NotEmpty(font.FaceIdentity); Assert.Equal("label", font.SourceElementId);
            Assert.Contains("U+00E9", error.Message);
            SvgSourceDiagnostic source = Assert.IsType<SvgSourceDiagnostic>(error.SourceDiagnostic);
            Assert.Equal("text", source.ElementName); Assert.Equal("label", source.ElementId);
            Assert.Equal(2, source.LineNumber); Assert.Equal(3, source.LinePosition);
        }
    }

    [Fact]
    public async Task Forward_use_preserves_original_text_location_and_instance_identity()
    {
        (SvgFontPackCatalog catalog, CountingFontProvider provider, _) = CreateExamplePack(CreateTriangleFont());
        byte[] svg = "<svg xmlns='http://www.w3.org/2000/svg' width='64' height='64'>\n <use id='copy' href='#label'/>\n <defs>\n  <text id='label' font-family='Example'>Aé</text>\n </defs>\n</svg>"u8.ToArray();
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync(svg, catalog, provider, CancellationToken.None);
        SvgException error = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(svg,
            new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, snapshot), CancellationToken.None));
        SvgSourceDiagnostic source = Assert.IsType<SvgSourceDiagnostic>(error.SourceDiagnostic);
        Assert.Equal("text", source.ElementName); Assert.Equal("label", source.ElementId);
        Assert.Equal(4, source.LineNumber); Assert.Equal(4, source.LinePosition);
        Assert.Equal("copy", source.InstanceElementId);
        Assert.Equal(1, Assert.IsType<SvgFontDiagnostic>(error.FontDiagnostic).ScalarIndex);
    }

    [Fact]
    public async Task Provider_failure_retains_discovered_request_style_and_source_position()
    {
        (SvgFontPackCatalog catalog, _, _) = CreateExamplePack(CreateTriangleFont());
        byte[] svg = "<svg xmlns='http://www.w3.org/2000/svg' width='64' height='64'>\n <text id='label' font-family='Missing' font-weight='700' font-style='italic'>A</text>\n</svg>"u8.ToArray();
        SvgException error = await Assert.ThrowsAsync<SvgException>(async () =>
            await SvgPreparedDocument.CreateAsync(svg, catalog, new MissingDiagnosticFontProvider(), SvgRenderOptions.Default, CancellationToken.None));
        SvgFontDiagnostic font = Assert.IsType<SvgFontDiagnostic>(error.FontDiagnostic);
        Assert.Equal(SvgFontFailureStage.Source, font.Stage); Assert.Equal(700, font.RequestedWeight); Assert.True(font.RequestedItalic); Assert.True(font.IsFallback);
        SvgSourceDiagnostic source = Assert.IsType<SvgSourceDiagnostic>(error.SourceDiagnostic);
        Assert.Equal("label", source.ElementId); Assert.Equal(2, source.LineNumber);
    }

    /// <summary>Provides no source or owned storage; observes cancellation and permits concurrent requests.</summary>
    private sealed class MissingDiagnosticFontProvider : ISvgFontProgramSourceProvider
    {
        public ValueTask<ISvgFontProgramSource?> GetSourceAsync(SvgFontPackFile file, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            return ValueTask.FromResult<ISvgFontProgramSource?>(null);
        }
    }
}
