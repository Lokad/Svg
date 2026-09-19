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
        SvgRenderLimits limits = new(20_000, 1024, 16, 16, 1024, 16, 16, 1);
        SvgRenderOptions options = new(null, 1, SvgColor.Transparent, limits);

        SvgException exception = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng("<svg xmlns='http://www.w3.org/2000/svg' width='2' height='1'><rect width='2' height='1' fill='red'/></svg>"u8.ToArray(), options, CancellationToken.None));

        Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode);
    }

    [Fact]
    public void Parser_work_reduces_the_raster_work_allowance()
    {
        SvgRenderLimits limits = new(20_000, 1024, 16, 16, 1024, 16, 16, 1);
        SvgRenderOptions options = new(null, 1, SvgColor.Transparent, limits);

        SvgException exception = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng("<svg xmlns='http://www.w3.org/2000/svg' width='1' height='1'><style>rect { fill: red; }</style><rect width='1' height='1'/></svg>"u8.ToArray(), options, CancellationToken.None));

        Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode);
    }

    [Fact]
    public void Encoding_stops_before_exceeding_the_configured_png_limit()
    {
        SvgRenderLimits limits = new(20_000, 1024, 16, 16, 1, 16, 16, 16);
        SvgRenderOptions options = new(null, 1, SvgColor.Transparent, limits);

        SvgException exception = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng("<svg xmlns='http://www.w3.org/2000/svg' width='1' height='1'/>"u8.ToArray(), options, CancellationToken.None));

        Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode);
    }

    [Fact]
    public void Converter_methods_require_all_parameters()
    {
        MethodInfo[] methods = typeof(SvgConverter).GetMethods(BindingFlags.Public | BindingFlags.Static);

        Assert.Equal(6, methods.Length);
        Assert.All(methods.SelectMany(method => method.GetParameters()), parameter => Assert.False(parameter.HasDefaultValue));
        Assert.Equal(3, methods.Count(method => method.Name == "ConvertToPng"));
        Assert.Equal(3, methods.Count(method => method.Name == "ConvertToPngAsync"));
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
        Assert.Equal(128, limits.MaximumXmlDepth);
        Assert.Equal(256, limits.MaximumAttributesPerElement);
    }

    [Fact]
    public void Render_limits_reject_a_nonpositive_XML_depth()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SvgRenderLimits(1024, 1024, 16, 16, 1024, 16, 16, 16, 0));
    }

    [Fact]
    public void Render_limits_reject_a_nonpositive_attribute_limit()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SvgRenderLimits(1024, 1024, 16, 16, 1024, 16, 16, 16, 16, 0));
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
    public void Unsupported_overlapping_root_unit_suffix_fails_before_rasterization()
    {
        SvgException exception = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng("<svg xmlns='http://www.w3.org/2000/svg' width='1vmin' height='1'/>"u8.ToArray(), SvgRenderOptions.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, exception.ErrorCode);
    }

    [Theory]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg'><rect></svg>")]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg'>&undefined;</svg>")]
    public void Malformed_XML_is_categorized_as_malformed_input(string svg)
    {
        SvgException exception = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(System.Text.Encoding.UTF8.GetBytes(svg), SvgRenderOptions.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.MalformedInput, exception.ErrorCode);
    }

    [Fact]
    public void Trailing_document_content_fails_as_malformed_input()
    {
        SvgException extraElement = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng("<svg xmlns='http://www.w3.org/2000/svg' width='1' height='1'/><extra/>"u8.ToArray(), SvgRenderOptions.Default, CancellationToken.None));
        SvgException trailingText = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng("<svg xmlns='http://www.w3.org/2000/svg' width='1' height='1'/>trailing"u8.ToArray(), SvgRenderOptions.Default, CancellationToken.None));
        SvgException nonemptyExtraElement = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng("<svg xmlns='http://www.w3.org/2000/svg' width='1' height='1'><rect/></svg><extra/>"u8.ToArray(), SvgRenderOptions.Default, CancellationToken.None));
        SvgException nonemptyTrailingText = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng("<svg xmlns='http://www.w3.org/2000/svg' width='1' height='1'><rect/></svg>trailing"u8.ToArray(), SvgRenderOptions.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.MalformedInput, extraElement.ErrorCode);
        Assert.Equal(SvgErrorCode.MalformedInput, trailingText.ErrorCode);
        Assert.Equal(SvgErrorCode.MalformedInput, nonemptyExtraElement.ErrorCode);
        Assert.Equal(SvgErrorCode.MalformedInput, nonemptyTrailingText.ErrorCode);
    }

    [Fact]
    public void Animated_rectangle_content_is_rejected_as_unsupported()
    {
        SvgException exception = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng("<svg xmlns='http://www.w3.org/2000/svg' width='1' height='1'><rect width='1' height='1' fill='red'><animate attributeName='fill' values='red;blue' dur='1s'/></rect></svg>"u8.ToArray(), SvgRenderOptions.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, exception.ErrorCode);
    }

    [Theory]
    [InlineData("<line x1='0' y1='0' x2='1' y2='0' stroke='red' stroke-width='2'><animate attributeName='stroke' values='red;blue' dur='1s'/></line>")]
    [InlineData("<path d='M 0 0 H 1 V 1 H 0 Z' fill='red'><animate attributeName='fill' values='red;blue' dur='1s'/></path>")]
    [InlineData("<polygon points='0,0 1,0 1,1 0,1' fill='red'><animate attributeName='fill' values='red;blue' dur='1s'/></polygon>")]
    [InlineData("<polyline points='0,0 1,0 1,1 0,1 0,0' fill='red'><animate attributeName='fill' values='red;blue' dur='1s'/></polyline>")]
    [InlineData("<defs><rect id='tile' width='1' height='1' fill='red'/></defs><use href='#tile'><animate attributeName='x' values='0;1' dur='1s'/></use>")]
    [InlineData("<defs><rect id='tile' width='1' height='1' fill='red'><animate attributeName='fill' values='red;blue' dur='1s'/></rect></defs><use href='#tile'/>")]
    [InlineData("<defs><symbol id='tile'><rect width='1' height='1' fill='red'><animate attributeName='fill' values='red;blue' dur='1s'/></rect></symbol></defs><use href='#tile'/>")]
    public void Animated_content_on_other_supported_leaf_elements_is_rejected_as_unsupported(string content)
    {
        string svg = "<svg xmlns='http://www.w3.org/2000/svg' width='1' height='1'>" + content + "</svg>";

        SvgException exception = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(System.Text.Encoding.UTF8.GetBytes(svg), SvgRenderOptions.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, exception.ErrorCode);
    }

    [Theory]
    [InlineData("<rect width='2' height='2' fill='red'/>")]
    [InlineData("<line x1='0' y1='1' x2='2' y2='1' stroke='red' stroke-width='2'/>")]
    [InlineData("<path d='M 0 0 H 2 V 2 H 0 Z' fill='red'/>")]
    [InlineData("<polygon points='0,0 2,0 2,2 0,2' fill='red'/>")]
    [InlineData("<polyline points='0,0 2,0 2,2 0,2' fill='red'/>")]
    [InlineData("<defs><rect id='tile' width='2' height='2' fill='red'/></defs><use href='#tile'/>")]
    public void Every_currently_supported_paintable_leaf_reaches_public_PNG_conversion(string content)
    {
        string svg = "<svg xmlns='http://www.w3.org/2000/svg' width='2' height='2'>" + content + "</svg>";

        ReadOnlyMemory<byte> png = SvgConverter.ConvertToPng(System.Text.Encoding.UTF8.GetBytes(svg), SvgRenderOptions.Default, CancellationToken.None);

        Assert.Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, png[..8].ToArray());
    }

    [Theory]
    [InlineData("<style>#path { fill: red; opacity: 0.5; }</style><path id='path' d='M 0 0 H 2 V 2 H 0 Z'/>")]
    [InlineData("<style>.accent { stroke: red; }</style><line class='accent' x1='0' y1='1' x2='2' y2='1' stroke-width='2'/>")]
    [InlineData("<style>#instance { opacity: 0.5; }</style><defs><rect id='tile' width='2' height='2' fill='red'/></defs><use id='instance' href='#tile'/>")]
    public void Supported_stylesheet_and_reference_paths_reach_public_PNG_conversion(string content)
    {
        string svg = "<svg xmlns='http://www.w3.org/2000/svg' width='2' height='2'>" + content + "</svg>";

        ReadOnlyMemory<byte> png = SvgConverter.ConvertToPng(System.Text.Encoding.UTF8.GetBytes(svg), SvgRenderOptions.Default, CancellationToken.None);

        Assert.Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, png[..8].ToArray());
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
    public void Surface_budget_fails_before_a_large_pixel_buffer_allocation()
    {
        SvgRenderLimits limits = new(20_000, 1024, 100, 10_000, 1024, 1, 1, 1);
        SvgRenderOptions options = new(null, 1, SvgColor.Transparent, limits);

        SvgException exception = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng("<svg xmlns='http://www.w3.org/2000/svg' width='100' height='100'/>"u8.ToArray(), options, CancellationToken.None));

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
    public void Underflowed_output_dimension_is_an_unsupported_conversion()
    {
        SvgRenderOptions options = new(new SvgViewport(double.Epsilon, 1), 0.5, SvgColor.Transparent, SvgRenderLimits.Default);

        SvgException exception = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng("<svg xmlns='http://www.w3.org/2000/svg' width='1' height='1'/>"u8.ToArray(), options, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, exception.ErrorCode);
    }

    [Fact]
    public void Owned_png_output_stays_within_the_conversion_budget()
    {
        SvgRenderLimits insufficient = new(16_383, 1024, 2, 4, 1024, 1, 1, 1);
        SvgRenderOptions insufficientOptions = new(null, 1, SvgColor.Transparent, insufficient);
        SvgRenderLimits sufficient = new(16_384, 1024, 2, 4, 1024, 1, 1, 1);
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
        SvgRenderLimits insufficient = new(17_047, 1024, 2, 4, 1024, 16, 16, 16);
        SvgRenderOptions insufficientOptions = new(null, 1, SvgColor.Transparent, insufficient);
        SvgRenderLimits sufficient = new(17_048, 1024, 2, 4, 1024, 16, 16, 16);
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
        SvgRenderLimits limits = new(16_384, 1024, 2, 4, 1024, 1, 1, 1);
        SvgRenderOptions options = new(null, 1, SvgColor.Transparent, limits);
        byte[] destination = new byte[70];

        int written = SvgConverter.ConvertToPng("<svg xmlns='http://www.w3.org/2000/svg' width='1' height='1'/>"u8.ToArray(), destination, options, CancellationToken.None);

        Assert.Equal(70, written);
    }

    [Fact]
    public void Writable_stream_destination_does_not_reserve_owned_png_output()
    {
        SvgRenderLimits limits = new(16_384, 1024, 2, 4, 1024, 1, 1, 1);
        SvgRenderOptions options = new(null, 1, SvgColor.Transparent, limits);
        using MemoryStream destination = new(70);

        int written = SvgConverter.ConvertToPng("<svg xmlns='http://www.w3.org/2000/svg' width='1' height='1'/>"u8.ToArray(), destination, options, CancellationToken.None);

        Assert.Equal(70, written);
        Assert.Equal(70, destination.Length);
    }

    [Fact]
    public void Group_opacity_reserves_an_isolated_surface_before_allocation()
    {
        SvgRenderLimits limits = new(20_000, 1024, 64, 4_096, 1024, 16, 16, 16);
        SvgRenderOptions options = new(null, 1, SvgColor.Transparent, limits);

        SvgException exception = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng("<svg xmlns='http://www.w3.org/2000/svg' width='64' height='64'><g opacity='0.5'><rect width='64' height='64'/></g></svg>"u8.ToArray(), options, CancellationToken.None));

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
    public void Stream_destination_writes_the_same_png_without_an_owned_result()
    {
        byte[] svg = "<svg xmlns='http://www.w3.org/2000/svg' width='1' height='1'/>"u8.ToArray();
        byte[] expected = SvgConverter.ConvertToPng(svg, SvgRenderOptions.Default, CancellationToken.None).ToArray();
        using MemoryStream destination = new(expected.Length);

        int written = SvgConverter.ConvertToPng(svg, destination, SvgRenderOptions.Default, CancellationToken.None);

        Assert.Equal(expected.Length, written);
        Assert.Equal(expected, destination.ToArray());
        using MemoryStream nonWritableDestination = new(new byte[expected.Length], false);
        Assert.Throws<ArgumentException>(() => SvgConverter.ConvertToPng(svg, nonWritableDestination, SvgRenderOptions.Default, CancellationToken.None));
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

    [Fact]
    public async Task Async_stream_conversion_uses_asynchronous_destination_writes()
    {
        byte[] svg = "<svg xmlns='http://www.w3.org/2000/svg' width='1' height='1'/>"u8.ToArray();
        byte[] expected = SvgConverter.ConvertToPng(svg, SvgRenderOptions.Default, CancellationToken.None).ToArray();
        using AsyncWriteOnlyStream destination = new();

        int written = await SvgConverter.ConvertToPngAsync(svg, destination, SvgRenderOptions.Default, CancellationToken.None);

        Assert.Equal(expected.Length, written);
        Assert.Equal(expected, destination.ToArray());
    }

    private sealed class AsyncWriteOnlyStream : Stream
    {
        private readonly MemoryStream _storage = new();

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => _storage.Length;
        public override long Position { get => _storage.Position; set => throw new NotSupportedException(); }

        internal byte[] ToArray() => _storage.ToArray();

        public override void Flush() { }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new InvalidOperationException("The async conversion must not use synchronous stream writes.");

        public override void Write(ReadOnlySpan<byte> buffer) => throw new InvalidOperationException("The async conversion must not use synchronous stream writes.");

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _storage.Write(buffer.Span);
            return ValueTask.CompletedTask;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _storage.Dispose();
            base.Dispose(disposing);
        }
    }
}
