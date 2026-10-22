using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgRootAutoSizingTests
{
    [Theory]
    [InlineData("width", false)]
    [InlineData("width", true)]
    [InlineData("height", false)]
    [InlineData("height", true)]
    [InlineData("both", false)]
    [InlineData("both", true)]
    public void Winning_auto_dimensions_replace_attributes_in_inspection_and_rendering(string axes, bool sheet)
    {
        foreach (string attributes in new[] { "", "width='32' height='16'", "width='50%' height='25%'", "width='32' height='16' viewBox='0 0 32 16'" })
        foreach (bool supplied in new[] { false, true })
        foreach (string keyword in new[] { "auto", " AUTO ", "inherit" })
        {
            string declaration = axes == "both" ? "width:" + keyword + ";height:" + keyword : axes + ":" + keyword;
            string referenceDeclaration = declaration.Replace(keyword, "100%", StringComparison.Ordinal);
            SvgRenderOptions options = new(supplied ? new SvgViewport(80, 60) : null, 1, SvgColor.Transparent, SvgRenderLimits.Default);
            byte[] actual = Document(attributes, sheet ? "" : declaration, sheet ? declaration : "");
            byte[] expected = Document(attributes, sheet ? "" : referenceDeclaration, sheet ? referenceDeclaration : "");
            SvgDocumentDimensions result = SvgConverter.InspectDimensions(actual, options, CancellationToken.None);
            SvgDocumentDimensions control = SvgConverter.InspectDimensions(expected, options, CancellationToken.None);
            Assert.Equal(control.LogicalViewport, result.LogicalViewport);
            Assert.Equal(control.Width, result.Width);
            Assert.Equal(control.Height, result.Height);
            Assert.Equal(control.WidthSource, result.WidthSource);
            Assert.Equal(control.HeightSource, result.HeightSource);
            Assert.Equal(control.WidthUsesFallbackBase, result.WidthUsesFallbackBase);
            Assert.Equal(control.HeightUsesFallbackBase, result.HeightUsesFallbackBase);
            Assert.Equal(SvgConverter.ConvertToPng(expected, options, CancellationToken.None).ToArray(), SvgConverter.ConvertToPng(actual, options, CancellationToken.None).ToArray());
        }
    }

    [Theory]
    [InlineData("width:auto!important;height:auto!important", "width:12px;height:8px", "width:100%;height:100%")]
    [InlineData("width:12px;height:8px", "width:auto!important;height:auto!important", "width:100%;height:100%")]
    [InlineData("width:12px!important;height:8px!important", "width:auto;height:auto", "width:12px;height:8px")]
    [InlineData("width:auto;height:auto", "width:12px!important;height:8px!important", "width:12px;height:8px")]
    [InlineData("width:12px;width:auto;height:8px;height:auto", "", "width:100%;height:100%")]
    [InlineData("width:auto;width:12px;height:auto;height:8px", "", "width:12px;height:8px")]
    public void Auto_dimensions_retain_importance_and_source_order(string inline, string sheet, string expected)
        => Assert.Equal(SvgConverter.ConvertToPng(Document("", expected, ""), SvgRenderOptions.Default, CancellationToken.None).ToArray(),
            SvgConverter.ConvertToPng(Document("width='32' height='16'", inline, sheet), SvgRenderOptions.Default, CancellationToken.None).ToArray());

    [Theory]
    [InlineData("width:auto", 300, 16)]
    [InlineData("height:auto", 32, 150)]
    [InlineData("width:auto;height:auto", 300, 150)]
    public void Auto_uses_percentage_bases_instead_of_the_other_axis_aspect_ratio(string declaration, int width, int height)
    {
        byte[] input = Document("width='32' height='16' viewBox='0 0 32 16'", declaration, "");
        SvgDocumentDimensions dimensions = SvgConverter.InspectDimensions(input, SvgRenderOptions.Default, CancellationToken.None);
        Assert.Equal(new SvgViewport(width, height), dimensions.LogicalViewport);
        Assert.Equal(new SvgViewport(width, height), SvgConverter.InspectDimensions(input, new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, null, new(20, 20, 200)), CancellationToken.None).LogicalViewport);
        SvgRenderOptions fitted = new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, null, new(20, 20, 200));
        Assert.Equal(SvgConverter.ConvertToPng(Document("width='32' height='16' viewBox='0 0 32 16'", declaration.Replace("auto", "100%", StringComparison.Ordinal), ""), fitted, CancellationToken.None).ToArray(), SvgConverter.ConvertToPng(input, fitted, CancellationToken.None).ToArray());
        SvgRasterSize raster = dimensions.GetRasterSize(1, fitted.RasterBounds, fitted.Limits);
        ReadOnlyMemory<byte> png = SvgConverter.ConvertToPng(input, fitted, CancellationToken.None);
        Assert.Equal(raster.Width, System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.Span.Slice(16, 4)));
        Assert.Equal(raster.Height, System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.Span.Slice(20, 4)));
    }

    [Fact]
    public void Auto_sizing_owners_release_on_denial_cancellation_and_retry()
    {
        byte[] input = Document("width='32' height='16'", "width:auto;height:auto", "");
        SvgRenderOptions options = new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default);
        RenderBudget measured = new(1_000_000);
        ParserWorkCounter work = new(1_000_000);
        Assert.Equal(new SvgViewport(300, 150), SvgDocumentSizing.Inspect(input, options, measured, work, CancellationToken.None).LogicalViewport);
        Assert.Equal(0, measured.Snapshot().CurrentBytes);
        RenderBudget denied = new(measured.Snapshot().PeakBytes - 1);
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgDocumentSizing.Inspect(input, options, denied, new(1_000_000), CancellationToken.None)).ErrorCode);
        Assert.Equal(0, denied.Snapshot().CurrentBytes);
        RenderBudget workDenied = new(1_000_000);
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgDocumentSizing.Inspect(input, options, workDenied, new(work.Used - 1), CancellationToken.None)).ErrorCode);
        Assert.Equal(0, workDenied.Snapshot().CurrentBytes);
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        Assert.Equal(cancellation.Token, Assert.Throws<OperationCanceledException>(() => SvgDocumentSizing.Inspect(input, options, workDenied, new(1_000_000), cancellation.Token)).CancellationToken);
        Assert.Equal(0, workDenied.Snapshot().CurrentBytes);
        Assert.Equal(new SvgViewport(300, 150), SvgDocumentSizing.Inspect(input, options, workDenied, new(1_000_000), CancellationToken.None).LogicalViewport);
        Assert.Equal(0, workDenied.Snapshot().CurrentBytes);
    }

    private static byte[] Document(string attributes, string inline, string sheet)
        => System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' " + attributes + " style='" + inline + "'><rect width='100%' height='100%' fill='red'/><style>svg{" + sheet + "}</style></svg>");
}
