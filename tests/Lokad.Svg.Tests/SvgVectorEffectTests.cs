using TextEncoding = System.Text.Encoding;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgVectorEffectTests
{
    [Fact]
    public void Instance_vector_effect_keeps_the_scalar_value_limit()
    {
        string value = "none" + new string(' ', SvgSyntaxLimits.MaximumValueTokenCharacters);
        byte[] input = TextEncoding.UTF8.GetBytes(Document("", "<defs><path id='p' d='M1 1H7V7H1Z'/></defs><use href='#p' vector-effect='" + value + "'/>"));
        using MemoryStream stream = new();
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(input, stream, SvgRenderOptions.Default, CancellationToken.None)).ErrorCode);
        Assert.Equal(0, stream.Length);
    }

    [Theory]
    [InlineData("viewport-fill='none'")]
    [InlineData("viewport-fill='currentColor'")]
    [InlineData("viewport-fill='inherit'")]
    [InlineData("viewport-fill='#cfe' viewport-fill-opacity='.9'")]
    [InlineData("viewport-fill-opacity='inherit'")]
    [InlineData("viewport-fill-opacity='-2'")]
    [InlineData("viewport-fill-opacity='3'")]
    public void Legacy_viewport_presentation_keeps_native_transparent_background(string attributes)
    {
        byte[] plain = TextEncoding.UTF8.GetBytes(Document("", "<path d='M1 1H7V7H1Z' fill='red'/>"));
        byte[] declared = TextEncoding.UTF8.GetBytes(Document(attributes, "<path d='M1 1H7V7H1Z' fill='red'/>"));
        Assert.Equal(SvgConverter.ConvertToPng(plain, SvgRenderOptions.Default, CancellationToken.None).ToArray(), SvgConverter.ConvertToPng(declared, SvgRenderOptions.Default, CancellationToken.None).ToArray());
    }

    [Theory]
    [InlineData("viewport-fill-opacity='NaN'", SvgErrorCode.MalformedInput)]
    [InlineData("viewport-fill-opacity='Infinity'", SvgErrorCode.MalformedInput)]
    [InlineData("viewport-fill-opacity='bogus'", SvgErrorCode.MalformedInput)]
    [InlineData("viewport-fill='url(#p)'", SvgErrorCode.UnsupportedFeature)]
    [InlineData("viewport-fill-new='red'", SvgErrorCode.UnsupportedFeature)]
    [InlineData("vector-effect='non-rotation'", SvgErrorCode.UnsupportedFeature)]
    [InlineData("vector-effect='non-scaling-stroke'", SvgErrorCode.UnsupportedFeature)]
    public void Invalid_root_values_reject_before_writing_output(string attributes, SvgErrorCode code)
    {
        byte[] input = TextEncoding.UTF8.GetBytes(Document(attributes, "<path d='M1 1H7V7H1Z'/>"));
        byte[] destination = Enumerable.Repeat((byte)0x5a, 512).ToArray();
        Assert.Equal(code, Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(input, destination, SvgRenderOptions.Default, CancellationToken.None)).ErrorCode);
        Assert.All(destination, value => Assert.Equal((byte)0x5a, value));
        using MemoryStream stream = new();
        Assert.Equal(code, Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(input, stream, SvgRenderOptions.Default, CancellationToken.None)).ErrorCode);
        Assert.Equal(0, stream.Length);
    }

    [Fact]
    public void Instance_host_construction_releases_owners_on_memory_denial_and_retry()
    {
        byte[] input = TextEncoding.UTF8.GetBytes(Document("viewBox='0 0 4 4'", "<defs><path id='p' vector-effect='inherit' d='M1 1C1 3 3 3 3 1' fill='none' stroke='red' stroke-width='.5'/></defs><use href='#p' transform='scale(1.1)' vector-effect='non-scaling-stroke'/>"));
        SvgRenderOptions options = SvgRenderOptions.Default;
        RenderBudget measured = new RenderBudget(new HostMemoryBudget(8_000_000));
        SvgAffineTransform outputMap = SvgPreparationTestHelper.GetOutputMap(input, options, measured, CancellationToken.None);
        byte[] expected;
        using (SvgRoot root = SvgRootParser.Parse(input, options.Limits, measured, null, CancellationToken.None, outputMap, null))
            expected = SvgConverter.ConvertToPng(root, options, measured, CancellationToken.None).ToArray();
        Assert.Equal(0, measured.Snapshot().CurrentBytes);
        RenderBudget memory = new RenderBudget(new HostMemoryBudget(measured.Snapshot().PeakBytes - 1));
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() =>
        {
            using SvgRoot root = SvgRootParser.Parse(input, options.Limits, memory, null, CancellationToken.None, outputMap, null);
            SvgConverter.ConvertToPng(root, options, memory, CancellationToken.None);
        }).ErrorCode);
        Assert.Equal(0, memory.Snapshot().CurrentBytes);
        Assert.Equal(expected, SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None).ToArray());
    }

    [Fact]
    public void Canceled_host_stroke_replay_preserves_scene_and_retry()
    {
        byte[] input = TextEncoding.UTF8.GetBytes(Document("viewBox='0 0 4 4'", "<path d='M1 1C1 3 3 3 3 1' fill='none' stroke='red' stroke-width='.5' vector-effect='non-scaling-stroke'/>"));
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(8_000_000));
        SvgRenderOptions options = SvgRenderOptions.Default;
        SvgAffineTransform outputMap = SvgPreparationTestHelper.GetOutputMap(input, options, budget, CancellationToken.None);
        using (SvgRoot root = SvgRootParser.Parse(input, options.Limits, budget, null, CancellationToken.None, outputMap, null))
        {
            byte[] expected;
            using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, options, budget, CancellationToken.None)) expected = surface.Pixels.ToArray();
            long retained = budget.Snapshot().CurrentBytes;
            using CancellationTokenSource cancellation = new();
            cancellation.Cancel();
            Assert.Equal(cancellation.Token, Assert.ThrowsAny<OperationCanceledException>(() =>
            {
                using SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, options, budget, cancellation.Token);
            }).CancellationToken);
            Assert.Equal(retained, budget.Snapshot().CurrentBytes);
            using SvgConverter.RasterizedSurface retry = SvgConverter.Rasterize(root, options, budget, CancellationToken.None);
            Assert.Equal(expected, retry.Pixels);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    private static string Document(string attributes, string body)
        => "<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8' " + attributes + ">" + body + "</svg>";
}
