using System.Reflection;

namespace Lokad.Svg.Tests;

public sealed class PublicContractTests
{
    [Fact]
    public void Explicit_viewports_override_root_outer_dimensions()
    {
        SvgRenderOptions options = new(new SvgViewport(2, 3), 1, SvgColor.Transparent, SvgRenderLimits.Default);

        ReadOnlyMemory<byte> png = SvgConverter.ConvertToPng("<svg xmlns='http://www.w3.org/2000/svg' width='1' height='1'/>"u8.ToArray(), options, CancellationToken.None);

        Assert.Equal(new byte[] { 0, 0, 0, 2, 0, 0, 0, 3 }, png.Span.Slice(16, 8).ToArray());
    }

    [Fact]
    public void Rasterization_stops_before_exceeding_the_configured_work_limit()
    {
        SvgRenderLimits limits = new(1024, 1024, 16, 16, 1024, 16, 16, 1);
        SvgRenderOptions options = new(null, 1, SvgColor.Transparent, limits);

        SvgException exception = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng("<svg xmlns='http://www.w3.org/2000/svg' width='2' height='1'><rect width='2' height='1' fill='red'/></svg>"u8.ToArray(), options, CancellationToken.None));

        Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode);
    }

    [Fact]
    public void Encoding_stops_before_exceeding_the_configured_png_limit()
    {
        SvgRenderLimits limits = new(1024, 1024, 16, 16, 1, 16, 16, 16);
        SvgRenderOptions options = new(null, 1, SvgColor.Transparent, limits);

        SvgException exception = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng("<svg xmlns='http://www.w3.org/2000/svg' width='1' height='1'/>"u8.ToArray(), options, CancellationToken.None));

        Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode);
    }

    [Fact]
    public void Converter_methods_require_all_parameters()
    {
        MethodInfo[] methods = typeof(SvgConverter).GetMethods(BindingFlags.Public | BindingFlags.Static);

        Assert.Equal(4, methods.Length);
        Assert.All(methods.SelectMany(method => method.GetParameters()), parameter => Assert.False(parameter.HasDefaultValue));
        Assert.Equal(2, methods.Count(method => method.Name == "ConvertToPng"));
        Assert.Equal(2, methods.Count(method => method.Name == "ConvertToPngAsync"));
    }

    [Fact]
    public void Default_options_match_the_provisional_plan_limits()
    {
        SvgRenderLimits limits = SvgRenderOptions.Default.Limits;

        Assert.Null(SvgRenderOptions.Default.Viewport);
        Assert.Equal(1, SvgRenderOptions.Default.OutputScale);
        Assert.Equal(268_435_456, limits.MaximumAccountedBytes);
        Assert.Equal(16_777_216, limits.MaximumInputBytes);
        Assert.Equal(16_384, limits.MaximumDimension);
        Assert.Equal(16_777_216, limits.MaximumPixels);
        Assert.Equal(134_217_728, limits.MaximumEncodedPngBytes);
    }

    [Fact]
    public void Cancellation_is_observed_before_the_unimplemented_renderer()
    {
        using CancellationTokenSource source = new();
        source.Cancel();

        OperationCanceledException exception = Assert.Throws<OperationCanceledException>(() => SvgConverter.ConvertToPng(Array.Empty<byte>(), SvgRenderOptions.Default, source.Token));

        Assert.Equal(source.Token, exception.CancellationToken);
    }

    [Fact]
    public void Input_limit_is_checked_before_rendering()
    {
        SvgRenderLimits limits = new(1_024, 1, 1, 1, 1, 1, 1, 1);
        SvgRenderOptions options = new(null, 1, SvgColor.Transparent, limits);

        SvgException exception = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(new byte[] { 1, 2 }, options, CancellationToken.None));

        Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode);
    }

    [Fact]
    public void Malformed_root_fails_before_the_unimplemented_renderer()
    {
        SvgException exception = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng("<not-svg/>"u8.ToArray(), SvgRenderOptions.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.MalformedInput, exception.ErrorCode);
    }

    [Fact]
    public void Trailing_document_content_fails_as_malformed_input()
    {
        SvgException extraElement = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng("<svg xmlns='http://www.w3.org/2000/svg' width='1' height='1'/><extra/>"u8.ToArray(), SvgRenderOptions.Default, CancellationToken.None));
        SvgException trailingText = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng("<svg xmlns='http://www.w3.org/2000/svg' width='1' height='1'/>trailing"u8.ToArray(), SvgRenderOptions.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.MalformedInput, extraElement.ErrorCode);
        Assert.Equal(SvgErrorCode.MalformedInput, trailingText.ErrorCode);
    }

    [Fact]
    public void Animated_rectangle_content_is_rejected_as_unsupported()
    {
        SvgException exception = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng("<svg xmlns='http://www.w3.org/2000/svg' width='1' height='1'><rect width='1' height='1' fill='red'><animate attributeName='fill' values='red;blue' dur='1s'/></rect></svg>"u8.ToArray(), SvgRenderOptions.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, exception.ErrorCode);
    }

    [Fact]
    public void Empty_explicitly_sized_svg_returns_png_bytes()
    {
        ReadOnlyMemory<byte> png = SvgConverter.ConvertToPng("<svg xmlns='http://www.w3.org/2000/svg' width='1' height='1'/>"u8.ToArray(), SvgRenderOptions.Default, CancellationToken.None);
        Assert.Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, png[..8].ToArray());
    }

    [Fact]
    public void Dimensionless_svg_uses_the_css_default_object_size()
    {
        ReadOnlyMemory<byte> png = SvgConverter.ConvertToPng("<svg xmlns='http://www.w3.org/2000/svg'/>"u8.ToArray(), SvgRenderOptions.Default, CancellationToken.None);

        Assert.Equal(new byte[] { 0, 0, 1, 44, 0, 0, 0, 150 }, png.Span.Slice(16, 8).ToArray());
    }

    [Fact]
    public void One_root_dimension_uses_the_view_box_intrinsic_ratio()
    {
        ReadOnlyMemory<byte> png = SvgConverter.ConvertToPng("<svg xmlns='http://www.w3.org/2000/svg' width='20' viewBox='0 0 2 1'/>"u8.ToArray(), SvgRenderOptions.Default, CancellationToken.None);

        Assert.Equal(new byte[] { 0, 0, 0, 20, 0, 0, 0, 10 }, png.Span.Slice(16, 8).ToArray());
    }

    [Fact]
    public void Empty_svg_surface_budget_fails_before_pixel_buffer_allocation()
    {
        SvgRenderLimits limits = new(3, 1024, 2, 4, 1024, 1, 1, 1);
        SvgRenderOptions options = new(null, 1, SvgColor.Transparent, limits);

        SvgException exception = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng("<svg xmlns='http://www.w3.org/2000/svg' width='1' height='1'/>"u8.ToArray(), options, CancellationToken.None));

        Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode);
    }

    [Fact]
    public void Contiguous_rgba_surface_overflow_is_a_configured_limit_failure()
    {
        SvgRenderLimits limits = new(10_000_000_000, 1024, int.MaxValue, int.MaxValue, 1024, 1, 1, 1);
        SvgRenderOptions options = new(null, 1, SvgColor.Transparent, limits);

        SvgException exception = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng("<svg xmlns='http://www.w3.org/2000/svg' width='1073741824' height='2'/>"u8.ToArray(), options, CancellationToken.None));

        Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode);
    }

    [Fact]
    public void Owned_png_output_stays_within_the_conversion_budget()
    {
        SvgRenderLimits insufficient = new(137, 1024, 2, 4, 1024, 1, 1, 1);
        SvgRenderOptions insufficientOptions = new(null, 1, SvgColor.Transparent, insufficient);
        SvgRenderLimits sufficient = new(138, 1024, 2, 4, 1024, 1, 1, 1);
        SvgRenderOptions sufficientOptions = new(null, 1, SvgColor.Transparent, sufficient);
        byte[] svg = "<svg xmlns='http://www.w3.org/2000/svg' width='1' height='1'/>"u8.ToArray();

        SvgException exception = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(svg, insufficientOptions, CancellationToken.None));
        ReadOnlyMemory<byte> png = SvgConverter.ConvertToPng(svg, sufficientOptions, CancellationToken.None);

        Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode);
        Assert.Equal(70, png.Length);
    }

    [Fact]
    public void Retained_scene_arrays_stay_within_the_conversion_budget()
    {
        SvgRenderLimits insufficient = new(663, 1024, 2, 4, 1024, 16, 16, 16);
        SvgRenderOptions insufficientOptions = new(null, 1, SvgColor.Transparent, insufficient);
        SvgRenderLimits sufficient = new(664, 1024, 2, 4, 1024, 16, 16, 16);
        SvgRenderOptions sufficientOptions = new(null, 1, SvgColor.Transparent, sufficient);
        byte[] svg = "<svg xmlns='http://www.w3.org/2000/svg' width='1' height='1'><rect width='1' height='1'/></svg>"u8.ToArray();

        SvgException exception = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(svg, insufficientOptions, CancellationToken.None));
        ReadOnlyMemory<byte> png = SvgConverter.ConvertToPng(svg, sufficientOptions, CancellationToken.None);

        Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode);
        Assert.Equal(70, png.Length);
    }

    [Fact]
    public void Caller_destination_does_not_reserve_owned_png_output()
    {
        SvgRenderLimits limits = new(73, 1024, 2, 4, 1024, 1, 1, 1);
        SvgRenderOptions options = new(null, 1, SvgColor.Transparent, limits);
        byte[] destination = new byte[70];

        int written = SvgConverter.ConvertToPng("<svg xmlns='http://www.w3.org/2000/svg' width='1' height='1'/>"u8.ToArray(), destination, options, CancellationToken.None);

        Assert.Equal(70, written);
    }

    [Fact]
    public void Group_opacity_reserves_an_isolated_surface_before_allocation()
    {
        SvgRenderLimits limits = new(4, 1024, 2, 4, 1024, 16, 16, 16);
        SvgRenderOptions options = new(null, 1, SvgColor.Transparent, limits);

        SvgException exception = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng("<svg xmlns='http://www.w3.org/2000/svg' width='1' height='1'><g opacity='0.5'><rect width='1' height='1'/></g></svg>"u8.ToArray(), options, CancellationToken.None));

        Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode);
    }

    [Fact]
    public async Task Async_cancellation_returns_a_canceled_task()
    {
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        Task<ReadOnlyMemory<byte>> task = SvgConverter.ConvertToPngAsync("<svg xmlns='http://www.w3.org/2000/svg' width='1' height='1'/>"u8.ToArray(), SvgRenderOptions.Default, cancellation.Token);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await task);
        Assert.True(task.IsCanceled);
    }

    [Fact]
    public async Task Async_conversion_matches_the_synchronous_owned_result()
    {
        byte[] svg = "<svg xmlns='http://www.w3.org/2000/svg' width='1' height='1'><rect width='1' height='1' fill='#ff0000'/></svg>"u8.ToArray();

        ReadOnlyMemory<byte> synchronous = SvgConverter.ConvertToPng(svg, SvgRenderOptions.Default, CancellationToken.None);
        ReadOnlyMemory<byte> asynchronous = await SvgConverter.ConvertToPngAsync(svg, SvgRenderOptions.Default, CancellationToken.None);

        Assert.Equal(synchronous.ToArray(), asynchronous.ToArray());
    }

    [Fact]
    public void Prohibited_doctype_is_reported_as_malformed_public_input()
    {
        SvgException exception = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng("<!DOCTYPE svg><svg xmlns='http://www.w3.org/2000/svg' width='1' height='1'/>"u8.ToArray(), SvgRenderOptions.Default, CancellationToken.None));
        Assert.Equal(SvgErrorCode.MalformedInput, exception.ErrorCode);
    }

    [Fact]
    public void Destination_conversion_writes_the_same_png_without_an_owned_result()
    {
        byte[] svg = "<svg xmlns='http://www.w3.org/2000/svg' width='1' height='1'/>"u8.ToArray();
        byte[] expected = SvgConverter.ConvertToPng(svg, SvgRenderOptions.Default, CancellationToken.None).ToArray();
        byte[] destination = new byte[expected.Length];

        int written = SvgConverter.ConvertToPng(svg, destination, SvgRenderOptions.Default, CancellationToken.None);

        Assert.Equal(expected.Length, written);
        Assert.Equal(expected, destination);
        SvgException exception = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(svg, new byte[1], SvgRenderOptions.Default, CancellationToken.None));
        Assert.Equal(SvgErrorCode.InsufficientDestination, exception.ErrorCode);
    }

    [Fact]
    public async Task Async_destination_conversion_matches_sync_destination_conversion()
    {
        byte[] svg = "<svg xmlns='http://www.w3.org/2000/svg' width='1' height='1'/>"u8.ToArray();
        byte[] synchronous = new byte[128];
        byte[] asynchronous = new byte[128];

        int syncLength = SvgConverter.ConvertToPng(svg, synchronous, SvgRenderOptions.Default, CancellationToken.None);
        int asyncLength = await SvgConverter.ConvertToPngAsync(svg, asynchronous, SvgRenderOptions.Default, CancellationToken.None);

        Assert.Equal(syncLength, asyncLength);
        Assert.Equal(synchronous.AsSpan(0, syncLength).ToArray(), asynchronous.AsSpan(0, asyncLength).ToArray());
    }
}
