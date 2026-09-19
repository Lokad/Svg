using System.Reflection;

namespace Lokad.Svg.Tests;

public sealed class PublicContractTests
{
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
}
