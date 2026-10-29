using System.Text.Json;
using Lokad.Svg.Fonts;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed partial class OpenTypeFontTests
{
    [Fact]
    [Trait("Layer", "Contracts")]
    public void Discovery_uses_the_preparation_budget_and_recovers_after_refused_temporary_storage()
    {
        byte[] svg = "<svg xmlns='http://www.w3.org/2000/svg'><text class='label'>A</text><style>.label { font-family: Example; }</style></svg>"u8.ToArray();
        var budget = new RenderBudget(8 * 1024 * 1024);
        using (SvgDocumentPreparation preparation = SvgDocumentPreparation.Create(svg, SvgRenderLimits.Default,
            budget, new(long.MaxValue), CancellationToken.None))
        {
            long retained = budget.Snapshot().CurrentBytes;
            using (RenderBudget.BudgetReservation competing = budget.Reserve(budget.AvailableBytes, SvgMemoryCategory.Parser))
            {
                SvgException refusal = Assert.Throws<SvgException>(() => preparation.DiscoverFontRequests());
                Assert.Equal(SvgErrorCode.LimitExceeded, refusal.ErrorCode);
                Assert.Equal(8 * 1024 * 1024, budget.Snapshot().CurrentBytes);
            }
            Assert.Equal(retained, budget.Snapshot().CurrentBytes);
            Assert.Equal("Example", Assert.Single(preparation.DiscoverFontRequests()).Family);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Layer", "Contracts")]
    public async Task Prepared_operation_collects_styles_once_for_request_and_coverage_acquisition(bool multipleFiles)
    {
        byte[] font = CreateTriangleFont();
        (SvgFontPackCatalog catalog, CountingFontProvider provider, CountingFontSource source) = CreateExamplePack(font);
        if (multipleFiles)
        {
            string hash = catalog.Files[0].Sha256;
            catalog = SvgFontPackCatalog.Parse(JsonSerializer.SerializeToUtf8Bytes(new
            {
                packId = "test-pack",
                files = new[]
                {
                    new { relativePath = "files/example.ttf", byteSize = font.Length, sha256 = hash },
                    new { relativePath = "files/unused.ttf", byteSize = font.Length, sha256 = hash }
                },
                families = new[] { new { requestedFamily = "Example", resolvedFamily = "Example", relativeFontFile = "files/example.ttf" } }
            }), "test-pack");
        }
        string geometry = string.Concat(Enumerable.Repeat("<rect width='1' height='1' fill='blue'/>", 200));
        byte[] svg = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'>"
            + geometry + "<text class='label' y='3'>A</text><style>.label { font-family: Example; font-size: 100; }</style></svg>");
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync(catalog,
            new[] { new SvgFontFaceRequest("Example", 400, false) }, provider, CancellationToken.None);
        var options = new SvgRenderOptions(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, snapshot);
        byte[] expected = SvgConverter.ConvertToPng(svg, options, CancellationToken.None).ToArray();
        int beforeRequests = source.RequestCount;
        var phases = new List<SvgPreparationPhase>();
        using IDisposable observation = SvgPreparationProbe.Arm(phases.Add);
        using SvgPreparedDocument prepared = await SvgPreparedDocument.CreateAsync(svg, catalog, provider,
            SvgRenderOptions.Default, CancellationToken.None);
        Assert.Equal(expected, prepared.ConvertToPng(CancellationToken.None).ToArray());
        Assert.Equal(1, source.RequestCount - beforeRequests);
        Assert.Equal(1, phases.Count(phase => phase == SvgPreparationPhase.Workspace));
        Assert.Equal(1, phases.Count(phase => phase == SvgPreparationPhase.StyleSheets));
        Assert.Equal(1, phases.Count(phase => phase == SvgPreparationPhase.Scene));
    }
}
