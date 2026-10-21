using Lokad.Svg.Parsing;

namespace Lokad.Svg.Tests;

public sealed class SvgCurrentColorInheritanceTests
{
    [Theory]
    [InlineData("<g color='red'><rect $ width='8' height='8' fill='currentColor' stroke='currentColor' stroke-width='2'/></g>")]
    [InlineData("<g color='red'><g $><rect width='8' height='8' fill='currentColor'/></g></g>")]
    [InlineData("<defs><rect id='s' width='8' height='8' fill='currentColor'/></defs><g color='red'><use $ href='#s'/></g>")]
    [InlineData("<defs><rect id='s' $ width='8' height='8' fill='currentColor'/></defs><use href='#s' color='red'/>")]
    [InlineData("<defs><symbol id='s' $><rect width='8' height='8' fill='currentColor'/></symbol></defs><use href='#s' color='red'/>")]
    [InlineData("<defs><symbol id='s'><g $><rect width='8' height='8' fill='currentColor'/></g></symbol></defs><use href='#s' color='red'/>")]
    [InlineData("<g id='s' $><rect width='4' height='4' fill='currentColor'/></g><use href='#s' x='8' color='red'/>")]
    public void Winning_self_reference_matches_explicit_inheritance_through_source_and_instance_paths(string content)
    {
        byte[] expected = Render(content.Replace("$", "color='inherit'", StringComparison.Ordinal));
        Assert.NotEqual(expected, Render(content.Replace("$", "color='blue'", StringComparison.Ordinal)));
        foreach (string declaration in new[] { "color=' currentColor '", "style='color:CURRENTCOLOR'", "class='self'" })
        {
            string actual = content.Replace("$", declaration, StringComparison.Ordinal) + "<style>.self{color:currentColor}</style>";
            Assert.Equal(expected, Render(actual));
        }
    }

    [Theory]
    [InlineData("color='currentColor'")]
    [InlineData("style='color:currentColor'")]
    [InlineData("class='self'")]
    public void Root_self_reference_uses_the_initial_inherited_color(string declaration)
    {
        const string geometry = "<rect width='8' height='8' fill='currentColor'/>";
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16' " + declaration + ">" + geometry + "<style>.self{color:currentColor}</style></svg>");
        Assert.Equal(Render("<rect width='8' height='8' fill='black'/>"), SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None).ToArray());
    }

    [Theory]
    [InlineData("color='blue' style='color:currentColor'", "", "red")]
    [InlineData("color='blue' style='color:blue'", ".self{color:currentColor!important}", "red")]
    [InlineData("color='currentColor' style='color:blue'", "", "blue")]
    [InlineData("style='color:currentColor!important;color:blue'", "", "red")]
    [InlineData("style='color:currentColor;color:blue!important'", "", "blue")]
    [InlineData("style='color:currentColor!important'", ".self{color:blue!important}", "red")]
    [InlineData("color='currentColor'", ".self{color:blue}", "blue")]
    public void Self_reference_is_resolved_only_after_cascade_and_importance(string attributes, string sheet, string expected)
    {
        string geometry = "<rect class='self' width='8' height='8' fill='currentColor' ";
        Assert.Equal(Render(geometry + "color='" + expected + "'/>"),
            Render("<g color='red'>" + geometry + attributes + "/></g><style>" + sheet + "</style>"));
    }

    [Fact]
    public void Fill_and_stroke_current_color_keep_binding_to_each_descendants_own_color()
    {
        const string shape = "<rect x='2' y='2' width='8' height='8' color='blue' stroke-width='2'/>";
        string actual = "<g color='red'><g color='currentColor' fill='currentColor' stroke='currentColor'>" + shape + "</g></g>";
        Assert.Equal(Render("<g fill='blue' stroke='blue'>" + shape + "</g>"), Render(actual));
        Assert.NotEqual(Render("<g fill='red' stroke='red'>" + shape + "</g>"), Render(actual));
    }

    [Fact]
    public void Self_reference_keeps_the_attribute_token_limit_before_normalization()
    {
        string value = new string(' ', SvgSyntaxLimits.MaximumValueTokenCharacters) + "currentColor";
        SvgException error = Assert.Throws<SvgException>(() => Render("<rect width='8' height='8' color='" + value + "'/>"));
        Assert.Equal(SvgErrorCode.LimitExceeded, error.ErrorCode);
    }

    private static byte[] Render(string content)
        => SvgConverter.ConvertToPng(System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'>" + content + "</svg>"), SvgRenderOptions.Default, CancellationToken.None).ToArray();
}
