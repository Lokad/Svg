using Lokad.Svg.Parsing;

namespace Lokad.Svg.Tests;

public sealed class SvgCssWhitespaceTests
{
    [Theory]
    [MemberData(nameof(SvgCssSelectorTests.ClassSeparators), MemberType = typeof(SvgCssSelectorTests))]
    public void Selector_boundaries_preserve_non_ascii_identifier_characters(int character, bool whitespace)
    {
        string separator = char.ConvertFromUtf32(character);
        foreach (string selector in new[] { "g" + separator + "rect", separator + "rect", "rect" + separator,
            ".outer" + separator + ".hot", "rect[id" + separator + "=" + separator + "'box']" })
        foreach (bool retained in new[] { false, true })
        {
            string content = "<g class='outer'><rect id='box' class='hot' width='8' height='8'/></g>";
            if (retained) content = "<defs><symbol id='s'>" + content + "</symbol></defs><use href='#s'/>";
            string input = "<style>" + selector + "{fill:red}</style>" + content;
            if (whitespace) Assert.Equal(Render("<rect width='8' height='8' fill='red'/>"), Render(input));
            else Assert.Equal(SvgErrorCode.UnsupportedFeature, Assert.Throws<SvgException>(() => Render(input)).ErrorCode);
        }
    }

    [Theory]
    [MemberData(nameof(SvgCssSelectorTests.ClassSeparators), MemberType = typeof(SvgCssSelectorTests))]
    public void Scalar_tokens_do_not_turn_unicode_characters_into_css_whitespace(int character, bool whitespace)
    {
        string separator = char.ConvertFromUtf32(character);
        foreach ((string property, string value) in new[] { ("fill", "red"), ("stroke", "red"), ("color", "red"),
            ("opacity", ".5"), ("fill-opacity", ".5"), ("stroke-opacity", ".5"),
            ("display", "none"), ("visibility", "hidden"), ("fill-rule", "evenodd"), ("clip-rule", "evenodd"),
            ("stroke-width", "2"), ("stroke-miterlimit", "4"), ("stroke-dasharray", "2 1"),
            ("stroke-dashoffset", "1"), ("stroke-linecap", "round"), ("stroke-linejoin", "round"), ("filter", "none") })
        foreach (string location in new[] { "attribute", "inline", "rule" })
        {
            string token = separator + value + separator;
            string geometry = "<rect width='8' height='8' stroke-width='2'"
                + (property == "fill" ? "" : " fill='currentColor'")
                + (property == "color" ? "" : " color='green'")
                + (property == "stroke" ? "" : " stroke='blue'");
            if (property == "stroke-width") geometry = geometry.Replace(" stroke-width='2'", "");
            string input = location switch
            {
                "attribute" => geometry + " " + property + "='" + token + "'/>",
                "inline" => geometry + " style='" + property + ":" + token + "'/>",
                _ => "<style>.p{" + property + ":" + token + "}</style>" + geometry + " class='p'/>"
            };
            if (whitespace) Assert.Equal(Render(geometry + " " + property + "='" + value + "'/>"), Render(input));
            else
            {
                SvgException error = Assert.Throws<SvgException>(() => Render(input));
                Assert.True(error.ErrorCode is SvgErrorCode.UnsupportedFeature or SvgErrorCode.MalformedInput);
            }
        }
    }

    [Theory]
    [InlineData("fill", "# f f f")]
    [InlineData("fill", "rgb(255\u00a00\u00a00)")]
    [InlineData("fill", "rgba(255,0,0,\u2003.5)")]
    [InlineData("fill", "hsl(\u00a00,100%,50%)")]
    [InlineData("opacity", "50 %")]
    [InlineData("\u00a0fill", "red")]
    [InlineData("fill\u2003", "red")]
    [InlineData("paint-order", "stroke\u00a0fill")]
    [InlineData("stroke-width", "2 px")]
    [InlineData("stroke-dashoffset", "1 px")]
    public void Malformed_css_tokens_reject_without_changing_their_value(string property, string value)
    {
        Assert.Throws<SvgException>(() => Render("<rect width='8' height='8' stroke='blue' style='" + property + ":" + value + "'/>"));
        Assert.Throws<SvgException>(() => Render("<style>rect{" + property + ":" + value + "}</style><rect width='8' height='8' stroke='blue'/>"));
    }

    [Fact]
    public void Css_whitespace_preserves_quoted_unicode_and_form_feed_separation()
    {
        InlineStyle style = SvgInlineStyleParser.Parse("font-family:' \u00a0\u2003 ';\ffill:\fred\f!\fimportant\f", new(1000), CancellationToken.None, true);
        Assert.Equal("' \u00a0\u2003 '", style.TextFont.Family);
        Assert.Equal("red", style.Fill);
        Assert.NotEqual(InlineImportantProperties.None, style.Important & InlineImportantProperties.Fill);
        Assert.Equal(SvgTextDecorationLine.Underline | SvgTextDecorationLine.Overline, SvgTextDecorationSyntax.Parse("\funderline\fsolid\foverline\f"));
    }

    [Theory]
    [MemberData(nameof(SvgCssSelectorTests.ClassSeparators), MemberType = typeof(SvgCssSelectorTests))]
    public void Gradient_stop_styles_preserve_non_ascii_property_and_value_boundaries(int character, bool whitespace)
    {
        string separator = char.ConvertFromUtf32(character);
        foreach (string property in new[] { "stop-color", "stop-opacity" })
        foreach (string edge in new[] { "name-before", "name-after", "value-before", "value-after" })
        {
            string name = edge == "name-before" ? separator + property : edge == "name-after" ? property + separator : property;
            string value = property == "stop-color" ? "red" : ".5";
            string token = edge == "value-before" ? separator + value : edge == "value-after" ? value + separator : value;
            string input = Gradient(name + ":" + token);
            if (whitespace) Assert.Equal(Render(Gradient(property + ":" + value)), Render(input));
            else if (edge.StartsWith("name", StringComparison.Ordinal)) Assert.Equal(Render(Gradient("")), Render(input));
            else Assert.Throws<SvgException>(() => Render(input));
        }

        static string Gradient(string style)
            => "<defs><linearGradient id='p'><stop style='" + style + "'/><stop offset='1' stop-color='blue'/></linearGradient></defs><rect width='8' height='8' fill='url(#p)'/>";
    }

    private static byte[] Render(string body)
        => SvgConverter.ConvertToPng(System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'>" + body + "</svg>"), SvgRenderOptions.Default, CancellationToken.None).ToArray();
}
