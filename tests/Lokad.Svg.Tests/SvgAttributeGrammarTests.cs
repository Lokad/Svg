namespace Lokad.Svg.Tests;

public sealed class SvgAttributeGrammarTests
{
    [Theory]
    [InlineData("translate", "2 1")]
    [InlineData("scale", "2")]
    [InlineData("rotate", "90 4 4")]
    [InlineData("skewX", "30")]
    [InlineData("skewY", "30")]
    [InlineData("matrix", "1 0 0 1 2 1")]
    public void Svg_transform_function_names_remain_literal_across_geometry_and_paints(string name, string arguments)
    {
        Assert.NotEqual(Render(Input("shape", "")), Render(Input("shape", name + "(" + arguments + ")")));
        foreach (string scope in new[] { "shape", "group", "gradient", "pattern" })
        {
            Assert.NotEmpty(Render(Input(scope, name + "(" + arguments + ")")));
            foreach (string spelling in new[] { name.ToUpperInvariant(), char.ToUpperInvariant(name[0]) + name[1..] })
                Assert.Equal(SvgErrorCode.UnsupportedFeature, Assert.Throws<SvgException>(() => Render(Input(scope, spelling + "(" + arguments + ")"))).ErrorCode);
        }
    }

    [Theory]
    [InlineData("spread", "REPEAT", "repeat")]
    [InlineData("spread", "Reflect", "reflect")]
    [InlineData("aspect", "NONE", "none")]
    [InlineData("aspect", "None", "none")]
    public void Svg_attribute_enumerations_keep_their_literal_case(string scope, string changed, string canonical)
    {
        Assert.NotEmpty(Render(Input(scope, canonical)));
        Assert.Equal(SvgErrorCode.UnsupportedFeature, Assert.Throws<SvgException>(() => Render(Input(scope, changed))).ErrorCode);
    }

    private static byte[] Input(string scope, string value)
    {
        const string stops = "<stop stop-color='red'/><stop offset='1' stop-color='blue'/>";
        string body = scope switch
        {
            "group" => "<g transform='" + value + "'><rect width='2' height='2' fill='red'/></g>",
            "gradient" => "<defs><linearGradient id='p' gradientTransform='" + value + "'>" + stops + "</linearGradient></defs><rect width='8' height='8' fill='url(#p)'/>",
            "pattern" => "<defs><pattern id='p' width='4' height='4' patternUnits='userSpaceOnUse' patternTransform='" + value + "'><rect width='2' height='4' fill='red'/></pattern></defs><rect width='8' height='8' fill='url(#p)'/>",
            "spread" => "<defs><linearGradient id='p' x2='.5' spreadMethod='" + value + "'>" + stops + "</linearGradient></defs><rect width='8' height='8' fill='url(#p)'/>",
            "aspect" => "<svg width='8' height='8' viewBox='0 0 8 4' preserveAspectRatio='" + value + "'><rect width='8' height='4' fill='red'/></svg>",
            _ => "<rect width='2' height='2' fill='red' transform='" + value + "'/>"
        };
        return System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'>" + body + "</svg>");
    }

    private static byte[] Render(byte[] input)
        => SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None).ToArray();
}
