using Lokad.Svg.Parsing;

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

    [Theory]
    [InlineData("viewbox", ",0 0 4 4")]
    [InlineData("viewbox", "0,,0,4,4")]
    [InlineData("viewbox", "0 0 4 4,")]
    [InlineData("shape", "translate(,2 1)")]
    [InlineData("shape", "translate(2,,1)")]
    [InlineData("shape", "translate(2, ,1)")]
    [InlineData("shape", "translate(2 1,)")]
    [InlineData("group", "rotate(90,,4,4)")]
    [InlineData("gradient", "matrix(1,0,,0,1,2,1)")]
    [InlineData("pattern", "scale(2,)")]
    public void Numeric_lists_do_not_erase_missing_values(string scope, string value)
    {
        SvgException error = Assert.Throws<SvgException>(() => Render(Input(scope, value)));
        Assert.Equal(SvgErrorCode.MalformedInput, error.ErrorCode);
    }

    [Theory]
    [InlineData(" ")]
    [InlineData(",")]
    [InlineData(" , ")]
    [InlineData("\t,\n")]
    public void Numeric_lists_preserve_valid_comma_whitespace_combinations(string separator)
    {
        Assert.Equal(Render(Input("viewbox", "0 0 4 4")), Render(Input("viewbox", string.Join(separator, new[] { "0", "0", "4", "4" }))));
        foreach (string scope in new[] { "shape", "group", "gradient", "pattern" })
            Assert.Equal(Render(Input(scope, "translate(2 1)")), Render(Input(scope, "translate(2" + separator + "1)")));
    }

    [Theory]
    [InlineData(",2 1")]
    [InlineData("2,,1")]
    [InlineData("2 1,")]
    public void Dash_and_text_lists_reject_missing_comma_values(string value)
    {
        Assert.Equal(SvgErrorCode.UnsupportedFeature, Assert.Throws<SvgException>(() => SvgStrokeStyleSyntax.ParseDasharray(value)).ErrorCode);
        Assert.Throws<SvgException>(() => SvgTextPositionLists.ReadFirst(value, out _));
    }

    [Theory]
    [InlineData("0,1,2")]
    [InlineData("0 , 1 , 2")]
    [InlineData("0\t,\n1,2")]
    public void Text_span_cursors_resume_at_the_original_list_position(string value)
    {
        SvgTextPositionSpanCursor cursor = new(new(0, 3, new(value, null, null, null, "0,10,20"), null));
        for (int index = 0; index < 3; index++)
        {
            SvgTextPositionValue position = cursor.Next();
            Assert.Equal((double?)index, position.X);
            Assert.Equal((double?)(index * 10), position.Rotation);
        }
        SvgTextPositionValue end = cursor.Next();
        Assert.Null(end.X);
        Assert.Equal((double?)20, end.Rotation);
    }

    [Fact]
    public void Css_dash_lists_keep_their_own_whitespace_grammar()
        => Assert.Equal(new double[] { 2, 1 }, SvgStrokeStyleSyntax.ParseDasharray("2\f,\f1"));

    [Theory]
    [InlineData("shape")]
    [InlineData("group")]
    [InlineData("gradient")]
    [InlineData("pattern")]
    public void Svg_transform_parameters_are_numbers_without_css_length_units(string scope)
    {
        Assert.NotEmpty(Render(Input(scope, "translate(2 1)")));
        Assert.Equal(SvgErrorCode.UnsupportedFeature, Assert.Throws<SvgException>(() => Render(Input(scope, "translate(2px 1)"))).ErrorCode);
    }

    [Theory]
    [InlineData("shape", "\u00a0translate(2 1)")]
    [InlineData("gradient", "translate\u2003(2 1)")]
    [InlineData("aspect", "\u00a0none")]
    [InlineData("aspect", "none\u2003")]
    public void Svg_grammar_does_not_repair_non_ascii_token_boundaries(string scope, string value)
        => Assert.Equal(SvgErrorCode.UnsupportedFeature, Assert.Throws<SvgException>(() => Render(Input(scope, value))).ErrorCode);

    [Theory]
    [InlineData(",.5 1")]
    [InlineData(".5,,1")]
    [InlineData(".5, ,1")]
    [InlineData(".5 1,")]
    public void Blur_lists_preserve_required_numeric_values(string value)
        => Assert.Equal(SvgErrorCode.MalformedInput, Assert.Throws<SvgException>(() => Render(Input("blur", value))).ErrorCode);

    [Theory]
    [InlineData(".5,1")]
    [InlineData(" .5 , 1 ")]
    public void Blur_lists_preserve_valid_comma_whitespace(string value)
        => Assert.Equal(Render(Input("blur", ".5 1")), Render(Input("blur", value)));

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
            "viewbox" => "<svg width='8' height='8' viewBox='" + value + "'><rect width='2' height='2' fill='red'/></svg>",
            "blur" => "<defs><filter id='f' filterUnits='userSpaceOnUse' x='0' y='0' width='8' height='8'><feGaussianBlur stdDeviation='" + value + "'/></filter></defs><rect x='2' y='2' width='4' height='4' fill='red' filter='url(#f)'/>",
            _ => "<rect width='2' height='2' fill='red' transform='" + value + "'/>"
        };
        return System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'>" + body + "</svg>");
    }

    private static byte[] Render(byte[] input)
        => SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None).ToArray();
}
