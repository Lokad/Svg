using Lokad.Svg.Parsing;

namespace Lokad.Svg.Tests;

public sealed class SvgPaintWhitespaceTests
{
    [Theory]
    [InlineData("red")]
    [InlineData("#f00")]
    [InlineData("#ff000080")]
    [InlineData("rgb(255,0,0)")]
    [InlineData("rgba(255,0,0,.5)")]
    [InlineData("hsl(0,100%,50%)")]
    [InlineData("transparent")]
    [InlineData("currentColor")]
    [InlineData("none")]
    [InlineData("inherit")]
    [InlineData("initial")]
    [InlineData("unset")]
    public void Fill_and_stroke_attributes_ignore_surrounding_whitespace(string paint)
    {
        foreach (string property in new[] { "fill", "stroke" })
        {
            string shape = "<g fill='blue' color='red' stroke='green' stroke-width='2'><rect x='2' y='2' width='8' height='8' " + property + "='";
            Assert.Equal(Render(shape + paint + "'/></g>"), Render(shape + " &#x9;\r\n" + paint + "&#xD; '/></g>"));
            Assert.Equal(Render(shape + paint + "'/></g>"), Render("<g fill='blue' color='red' stroke='green' stroke-width='2'><rect x='2' y='2' width='8' height='8' style='" + property + ": " + paint + " '/></g>"));
        }
    }

    [Theory]
    [InlineData("<g color='$'><rect width='8' height='8' fill='currentColor'/></g>")]
    [InlineData("<g fill='$'><circle cx='6' cy='6' r='4'/></g>")]
    [InlineData("<defs><linearGradient id='p'><stop stop-color='$'/><stop offset='1' stop-color='blue'/></linearGradient></defs><rect width='8' height='8' fill='url(#p)'/>")]
    [InlineData("<defs><radialGradient id='p'><stop stop-color='$'/><stop offset='1' stop-color='blue'/></radialGradient></defs><rect width='8' height='8' fill='url(#p)'/>")]
    [InlineData("<defs><filter id='p'><feFlood flood-color='$' flood-opacity='.5'/></filter></defs><rect width='8' height='8' filter='url(#p)'/>")]
    [InlineData("<defs><rect id='p' width='8' height='8' fill='currentColor'/></defs><use href='#p' color='$'/>")]
    public void Inheritance_and_resource_colors_use_the_same_whitespace_rules(string content)
        => Assert.Equal(Render(content.Replace("$", "red", StringComparison.Ordinal)), Render(content.Replace("$", " &#x9;red&#xD; ", StringComparison.Ordinal)));

    [Fact]
    public void Whitespace_normalization_preserves_cascade_priority()
    {
        const string expected = "<rect width='8' height='8' fill='blue'/>";
        const string actual = "<style>rect{fill: blue !important}</style><rect width='8' height='8' fill=' not-a-color ' style='fill: red '/>";
        Assert.Equal(Render(expected), Render(actual));
    }

    [Fact]
    public void Paint_server_references_keep_their_existing_token_bound()
    {
        string id = new('p', SvgSyntaxLimits.MaximumValueTokenCharacters - "url(#)".Length);
        Assert.Equal(Render("<rect width='8' height='8' fill='red'/>"), Render("<defs><linearGradient id='" + id + "'><stop stop-color='red'/></linearGradient></defs><rect width='8' height='8' fill='url(#" + id + ")'/>"));
    }

    [Theory]
    [InlineData("<rect width='8' height='8' fill='$'/>")]
    [InlineData("<rect width='8' height='8' stroke='$'/>")]
    [InlineData("<g color='$'><rect width='8' height='8' fill='currentColor'/></g>")]
    [InlineData("<defs><linearGradient id='p'><stop stop-color='$'/></linearGradient></defs><rect width='8' height='8' fill='url(#p)'/>")]
    [InlineData("<defs><filter id='p'><feFlood flood-color='$'/></filter></defs><rect width='8' height='8' filter='url(#p)'/>")]
    public void Whitespace_does_not_hide_an_overlong_authored_token(string content)
    {
        string paint = new string(' ', SvgSyntaxLimits.MaximumValueTokenCharacters) + "red";
        SvgException error = Assert.Throws<SvgException>(() => Render(content.Replace("$", paint, StringComparison.Ordinal)));
        Assert.Equal(SvgErrorCode.LimitExceeded, error.ErrorCode);
    }

    [Theory]
    [InlineData("r ed")]
    [InlineData("#f f0000")]
    [InlineData("current Color")]
    [InlineData("no ne")]
    [InlineData("rgb (255,0,0)")]
    [InlineData("")]
    public void Normalization_does_not_accept_invalid_or_empty_colors(string paint)
    {
        SvgException control = Assert.Throws<SvgException>(() => Render("<rect width='8' height='8' fill='" + paint + "'/>"));
        SvgException padded = Assert.Throws<SvgException>(() => Render("<rect width='8' height='8' fill=' " + paint + " '/>"));
        Assert.Equal(control.ErrorCode, padded.ErrorCode);
    }

    private static byte[] Render(string content)
        => SvgConverter.ConvertToPng(System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='12' height='12'>" + content + "</svg>"), SvgRenderOptions.Default, CancellationToken.None).ToArray();
}
