using System.Xml;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgGradientStopStyleTests
{
    [Theory]
    [InlineData("stop-color:red!important;stop-color:blue", "stop-color:red")]
    [InlineData("stop-opacity:.27!important;stop-opacity:.9", "stop-opacity:.27")]
    [InlineData("stop-color:red!important;stop-color:blue;stop-opacity:.27", "stop-color:red;stop-opacity:.27")]
    [InlineData("stop-opacity:.27!important;stop-opacity:.9;stop-color:red", "stop-color:red;stop-opacity:.27")]
    [InlineData("stop-color:blue;stop-color:red!important", "stop-color:red")]
    [InlineData("stop-color:red!important;stop-color:blue!important", "stop-color:blue")]
    [InlineData("stop-color:red;stop-color:blue", "stop-color:blue")]
    [InlineData("stop-opacity:.27!important;stop-opacity:.81!important", "stop-opacity:.81")]
    [InlineData("stop-opacity:.27;stop-opacity:.81", "stop-opacity:.81")]
    [InlineData("stop-color:red ! IMPORTANT;stop-color:blue", "stop-color:red")]
    [InlineData("stop-opacity:27% !\timportant;stop-opacity:1", "stop-opacity:27%")]
    [InlineData("unparsed;exporter:ignored;stop-color:red!important;stop-color:blue;", "stop-color:red")]
    public void Stop_styles_resolve_each_property_by_importance_then_source_order(string actual, string expected)
    {
        foreach (string kind in new[] { "linearGradient", "radialGradient" })
        {
            Assert.Equal(Render(Input(kind, expected)), Render(Input(kind, actual)));
        }
    }

    [Fact]
    public void Important_stop_opacity_retains_unquantized_source_alpha()
    {
        using XmlReader reader = XmlReader.Create(new StringReader("<stop stop-color='#46596d80' stop-opacity='.9' style='stop-opacity:27%!important;stop-opacity:1'/>"));
        reader.MoveToContent();
        SvgGradientStop stop = SvgGradientSyntax.ParseStop(reader, SvgRootParser.CssNamedColors, CancellationToken.None);
        Assert.Equal(128 * (1f / 255f) * .27f, stop.Alpha);
        Assert.Equal(new SvgColor(70, 89, 109, 35), stop.Color);
    }

    [Theory]
    [InlineData("stop-color:not-a-color!important;stop-color:red", SvgErrorCode.UnsupportedFeature)]
    [InlineData("stop-opacity:NaN!important;stop-opacity:1", SvgErrorCode.MalformedInput)]
    public void Invalid_winning_values_keep_their_errors_and_release_storage(string style, SvgErrorCode code)
    {
        RenderBudget budget = new(1_000_000);
        SvgException error = Assert.Throws<SvgException>(() => SvgRootParser.Parse(Input("linearGradient", style), SvgRenderLimits.Default, budget, CancellationToken.None));
        Assert.Equal(code, error.ErrorCode);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    private static byte[] Input(string kind, string declarations)
        => System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><defs><" + kind + " id='p' href='#base'/><" + kind + " id='base'><stop stop-color='blue' stop-opacity='.9' style='" + declarations + "'/><stop offset='1' stop-color='green'/></" + kind + "></defs><rect width='16' height='16' fill='url(#p)'/></svg>");

    private static byte[] Render(byte[] input) => SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None).ToArray();
}
