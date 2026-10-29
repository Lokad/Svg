using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgShaderStrokePathTests
{
    [Theory]
    [InlineData("linearGradient", "x2='1'")]
    [InlineData("radialGradient", "")]
    public void Unpainted_curved_fill_preserves_the_source_box_for_gradient_stroke(string element, string attributes)
    {
        string definition = "<" + element + " id='p' " + attributes + "><stop stop-color='red'/><stop offset='1' stop-color='blue'/></" + element + ">";
        string path = "<path d='M2 12 Q12 2 22 12' stroke='url(#p)' stroke-width='2' ";
        byte[] unfilled = Document(definition, path + "fill='none'/>");
        byte[] invisibleFill = Document(definition, path + "fill='black' fill-opacity='0'/>");
        Assert.Equal(SvgConverter.ConvertToPng(invisibleFill, SvgRenderOptions.Default, CancellationToken.None).ToArray(),
            SvgConverter.ConvertToPng(unfilled, SvgRenderOptions.Default, CancellationToken.None).ToArray());
    }

    [Theory]
    [InlineData("linear")]
    [InlineData("radial")]
    [InlineData("pattern")]
    public void Mixed_shader_strokes_preserve_source_and_release_owners_after_denial_cancellation_and_retry(string paint)
    {
        byte[] input = Document(Definitions, MixedPath(paint));
        byte[] unchanged = input.ToArray();
        SvgRenderOptions options = new(null, 4, SvgColor.Transparent, SvgRenderLimits.Default);
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(8_000_000));
        using (SvgRoot scene = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, null, CancellationToken.None))
        {
            byte[] expected;
            using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(scene, options, budget, CancellationToken.None))
            {
                expected = surface.Pixels.ToArray();
                Assert.Contains(expected, value => value != 0);
            }
            long retained = budget.Snapshot().CurrentBytes;
            using CancellationTokenSource cancellation = new();
            bool reached = false;
            using (SvgAnalyticPaintProbe.Arm((phase, count) => { reached = true; cancellation.Cancel(); }))
            {
                OperationCanceledException error = Assert.ThrowsAny<OperationCanceledException>(() =>
                {
                    using SvgConverter.RasterizedSurface denied = SvgConverter.Rasterize(scene, options, budget, cancellation.Token);
                });
                Assert.Equal(cancellation.Token, error.CancellationToken);
            }
            Assert.True(reached);
            Assert.Equal(retained, budget.Snapshot().CurrentBytes);
            using SvgConverter.RasterizedSurface retry = SvgConverter.Rasterize(scene, options, budget, CancellationToken.None);
            Assert.Equal(expected, retry.Pixels);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        RenderBudget tight = new RenderBudget(new HostMemoryBudget(Math.Max(1, budget.Snapshot().PeakBytes - 1_024)));
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() =>
        {
            using SvgRoot scene = SvgRootParser.Parse(input, SvgRenderLimits.Default, tight, CancellationToken.None);
            using SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(scene, options, tight, CancellationToken.None);
        }).ErrorCode);
        Assert.Equal(0, tight.Snapshot().CurrentBytes);
        Assert.Equal(unchanged, input);
    }

    [Theory]
    [InlineData("stroke-dasharray='-1 2'")]
    [InlineData("stroke-width='NaN'")]
    public void Shader_stroke_admission_preserves_invalid_style_errors_without_output(string attributes)
    {
        byte[] input = Document(Definitions, "<path d='M2 2L22 18' fill='none' stroke='url(#linear)' " + attributes + "/>");
        using MemoryStream output = new();
        Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(input, output, SvgRenderOptions.Default, CancellationToken.None));
        Assert.Equal(0, output.Length);
    }

    private const string Definitions = "<linearGradient id='linear' gradientUnits='userSpaceOnUse' x2='24'><stop stop-color='#dc442b'/><stop offset='1' stop-color='#3565cc'/></linearGradient><radialGradient id='radial' gradientUnits='userSpaceOnUse' cx='12' cy='10' r='12'><stop stop-color='#294dc9'/><stop offset='1' stop-color='#6cbb3c'/></radialGradient><pattern id='pattern' patternUnits='userSpaceOnUse' width='4' height='4'><rect width='4' height='4' fill='#427ac9'/></pattern>";
    private static string MixedPath(string paint) => "<path d='M2 2H22V18H2Z M10 7H14V11H10Z M4 16L20 2' fill='none' stroke='url(#" + paint + ")' stroke-width='2'/>";
    private static byte[] Document(string definitions, string content) => System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='24' height='20'><defs>" + definitions + "</defs>" + content + "</svg>");
}
