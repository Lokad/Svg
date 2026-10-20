using System.Buffers.Binary;
using Lokad.Svg.Encoding;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgRasterSizingTests
{
    [Theory]
    [InlineData(4800, 2400, 1, 800, 600, null, 800, 400)]
    [InlineData(4800, 2400, 1, 800, 600, 180000L, 600, 300)]
    [InlineData(4800, 2400, 1, null, null, 180000L, 600, 300)]
    [InlineData(100, 50, 1, 800, 600, null, 100, 50)]
    [InlineData(100, 50, 2, 800, 600, null, 200, 100)]
    [InlineData(100, 50, .5, 800, 600, null, 50, 25)]
    [InlineData(5, 5, 1, null, null, 8L, 2, 2)]
    [InlineData(1.4, 1.4, 1, null, null, 1L, 1, 1)]
    [InlineData(120, .4, 1, 8, 8, null, 8, 1)]
    [InlineData(.4, 120, 1, 8, 8, null, 1, 8)]
    public void Raster_plans_fit_actual_integer_bounds_without_changing_logical_size(double width, double height, double scale,
        int? maximumWidth, int? maximumHeight, long? maximumPixels, int expectedWidth, int expectedHeight)
    {
        SvgRasterBounds bounds = new(maximumWidth, maximumHeight, maximumPixels);
        SvgRasterSize result = SvgRasterSizing.Resolve(new(width, height), scale, bounds, SvgRenderLimits.Default);
        Assert.Equal(expectedWidth, result.Width); Assert.Equal(expectedHeight, result.Height);
        Assert.InRange(result.OutputScale, double.Epsilon, scale);
        Assert.True(result.Width <= (maximumWidth ?? SvgRenderLimits.Default.MaximumDimension));
        Assert.True(result.Height <= (maximumHeight ?? SvgRenderLimits.Default.MaximumDimension));
        Assert.True(result.PixelCount <= (maximumPixels ?? SvgRenderLimits.Default.MaximumPixels));
    }

    [Fact]
    public void Fitting_preserves_percentage_layout_and_matches_the_same_explicit_scale()
    {
        byte[] svg = "<svg xmlns='http://www.w3.org/2000/svg' width='4800' height='2400'><rect x='25%' y='25%' width='50%' height='50%' fill='red'/></svg>"u8.ToArray();
        SvgRasterBounds bounds = new(800, 600, 180_000);
        SvgRenderOptions options = new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, null, bounds);
        SvgDocumentDimensions dimensions = SvgConverter.InspectDimensions(svg, options, CancellationToken.None);
        Assert.Equal(new SvgViewport(4800, 2400), dimensions.LogicalViewport);
        SvgRasterSize plan = dimensions.GetRasterSize(1, bounds, options.Limits);
        Assert.Equal(new SvgRasterSize(600, 300, .125), plan);
        ReadOnlyMemory<byte> png = SvgConverter.ConvertToPng(svg, options, CancellationToken.None);
        Assert.Equal(600, BinaryPrimitives.ReadInt32BigEndian(png.Span.Slice(16, 4)));
        Assert.Equal(300, BinaryPrimitives.ReadInt32BigEndian(png.Span.Slice(20, 4)));
        Assert.Equal(SvgConverter.ConvertToPng(svg, new(null, .125, SvgColor.Transparent, SvgRenderLimits.Default), CancellationToken.None).ToArray(), png.ToArray());
        RenderBudget budget = new(32_000_000);
        DecodedPngImage decoded = PngImageDecoder.Decode(png, SvgRenderLimits.Default, budget, new(10_000_000), CancellationToken.None);
        using (DecodedPngRaster pixels = Assert.IsType<DecodedPngRaster>(decoded.Raster))
        {
            Assert.Equal(new byte[] { 255, 0, 0, 255 }, pixels.Pixels.AsSpan((150 * decoded.Width + 300) * 4, 4).ToArray());
            Assert.Equal(0, pixels.Pixels[(70 * decoded.Width + 140) * 4 + 3]);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Hard_limits_still_reject_without_fitting_and_constrain_an_enabled_fit()
    {
        SvgRenderLimits d = SvgRenderLimits.Default;
        SvgRenderLimits limits = new(d.MaximumAccountedBytes, d.MaximumInputBytes, 128, 16_384,
            d.MaximumEncodedPngBytes, d.MaximumNodes, d.MaximumPathCommands, d.MaximumWorkUnits);
        byte[] svg = "<svg xmlns='http://www.w3.org/2000/svg' width='4800' height='2400'/>"u8.ToArray();
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(svg,
            new(null, 1, SvgColor.Transparent, limits), CancellationToken.None)).ErrorCode);
        SvgRenderOptions bounded = new(null, 1, SvgColor.Transparent, limits, null, new(800, 600, null));
        ReadOnlyMemory<byte> png = SvgConverter.ConvertToPng(svg, bounded, CancellationToken.None);
        Assert.Equal(128, BinaryPrimitives.ReadInt32BigEndian(png.Span.Slice(16, 4)));
        Assert.Equal(64, BinaryPrimitives.ReadInt32BigEndian(png.Span.Slice(20, 4)));
    }

    [Fact]
    public void Fit_can_plan_large_finite_logical_dimensions_without_area_overflow()
    {
        SvgRasterSize result = SvgRasterSizing.Resolve(new(1e300, 1e300), 1, new(128, 128, null), SvgRenderLimits.Default);
        Assert.Equal(128, result.Width); Assert.Equal(128, result.Height);
        Assert.True(double.IsFinite(result.OutputScale) && result.OutputScale > 0);
    }

    [Fact]
    public void Fit_pixel_caps_use_wide_products_and_do_not_bypass_rgba_storage_limits()
    {
        SvgRenderLimits d = SvgRenderLimits.Default;
        SvgRenderLimits limits = new(d.MaximumAccountedBytes, d.MaximumInputBytes, int.MaxValue, int.MaxValue,
            d.MaximumEncodedPngBytes, d.MaximumNodes, d.MaximumPathCommands, d.MaximumWorkUnits);
        SvgRasterSize result = SvgRasterSizing.Resolve(new(1_000_000, 1_000_000), 1,
            new(int.MaxValue, int.MaxValue, long.MaxValue), limits);
        Assert.InRange(result.PixelCount, 1, int.MaxValue / 4);
    }

    [Fact]
    public void Empty_or_nonpositive_bounds_are_argument_errors()
    {
        Assert.Throws<ArgumentException>(() => new SvgRasterBounds(null, null, null));
        Assert.Equal("maximumWidth", Assert.Throws<ArgumentOutOfRangeException>(() => new SvgRasterBounds(0, null, null)).ParamName);
        Assert.Equal("maximumHeight", Assert.Throws<ArgumentOutOfRangeException>(() => new SvgRasterBounds(null, -1, null)).ParamName);
        Assert.Equal("maximumPixels", Assert.Throws<ArgumentOutOfRangeException>(() => new SvgRasterBounds(null, null, 0)).ParamName);
    }

    [Theory]
    [InlineData("", 30, 15)]
    [InlineData("width='200'", 30, 23)]
    [InlineData("width='50%' height='25%'", 30, 8)]
    [InlineData("width='200' viewBox='0 0 400 200'", 30, 15)]
    public void Fitting_uses_shared_fallback_percentage_and_partial_sizing(string attributes, int width, int height)
    {
        byte[] svg = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' " + attributes + "/>");
        SvgRenderOptions options = new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, null, new(30, 30, null));
        SvgRasterSize plan = SvgConverter.InspectDimensions(svg, options, CancellationToken.None).GetRasterSize(1, options.RasterBounds, options.Limits);
        Assert.Equal(width, plan.Width); Assert.Equal(height, plan.Height);
        ReadOnlyMemory<byte> png = SvgConverter.ConvertToPng(svg, options, CancellationToken.None);
        Assert.Equal(width, BinaryPrimitives.ReadInt32BigEndian(png.Span.Slice(16, 4)));
        Assert.Equal(height, BinaryPrimitives.ReadInt32BigEndian(png.Span.Slice(20, 4)));
    }

    [Fact]
    public void Raster_resolution_releases_parser_storage_on_success_denial_and_malformed_input()
    {
        byte[] svg = "<svg xmlns='http://www.w3.org/2000/svg' width='4800' height='2400'/>"u8.ToArray();
        SvgRenderOptions options = new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, null, new(80, 60, null));
        RenderBudget budget = new(1_000_000);
        SvgRenderOptions resolved = SvgPreparationTestHelper.ResolveRasterOptions(svg, options, budget, new(10_000), CancellationToken.None);
        Assert.Equal(new SvgRasterSize(80, 40, 1d / 60), resolved.ResolvedRasterSize);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        RenderBudget denied = new(1);
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgPreparationTestHelper.ResolveRasterOptions(svg, options, denied, new(10_000), CancellationToken.None)).ErrorCode);
        Assert.Equal(0, denied.Snapshot().CurrentBytes);
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgPreparationTestHelper.ResolveRasterOptions(svg, options, budget, new(1), CancellationToken.None)).ErrorCode);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        Assert.Equal(SvgErrorCode.MalformedInput, Assert.Throws<SvgException>(() => SvgPreparationTestHelper.ResolveRasterOptions("<svg"u8.ToArray(), options, budget, new(10_000), CancellationToken.None)).ErrorCode);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        SvgDocumentDimensions dimensions = SvgConverter.InspectDimensions(svg, options, CancellationToken.None);
        Assert.Equal("outputScale", Assert.Throws<ArgumentOutOfRangeException>(() => dimensions.GetRasterSize(double.NaN, options.RasterBounds, options.Limits)).ParamName);
    }

    [Fact]
    public async Task Fitted_sinks_agree_and_cancellation_or_encoded_denial_preserves_existing_output()
    {
        byte[] svg = "<svg xmlns='http://www.w3.org/2000/svg' width='4800' height='2400'><rect width='50%' height='100%' fill='red'/></svg>"u8.ToArray();
        SvgRasterBounds bounds = new(80, 60, null);
        SvgRenderOptions options = new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, null, bounds);
        byte[] expected = SvgConverter.ConvertToPng(svg, options, CancellationToken.None).ToArray();
        byte[] destination = new byte[expected.Length];
        Assert.Equal(expected.Length, SvgConverter.ConvertToPng(svg, destination, options, CancellationToken.None));
        Assert.Equal(expected, destination);
        using MemoryStream stream = new();
        Assert.Equal(expected.Length, await SvgConverter.ConvertToPngAsync(svg, stream, options, CancellationToken.None));
        Assert.Equal(expected, stream.ToArray());
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        Assert.Equal(cancellation.Token, (await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await SvgConverter.ConvertToPngAsync(svg, stream, options, cancellation.Token))).CancellationToken);
        Assert.Equal(expected, stream.ToArray());
        SvgRenderLimits d = SvgRenderLimits.Default;
        SvgRenderLimits limits = new(d.MaximumAccountedBytes, d.MaximumInputBytes, d.MaximumDimension, d.MaximumPixels,
            1, d.MaximumNodes, d.MaximumPathCommands, d.MaximumWorkUnits);
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(svg, stream,
            new(null, 1, SvgColor.Transparent, limits, null, bounds), CancellationToken.None)).ErrorCode);
        Assert.Equal(expected, stream.ToArray());
    }
}
