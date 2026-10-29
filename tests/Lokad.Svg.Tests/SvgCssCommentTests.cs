using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgCssCommentTests
{
    public static IEnumerable<object[]> ValidDeclarations()
    {
        foreach (string location in new[] { "inline", "sheet", "stop" })
        foreach (string declaration in new[] { "fill:/*note*/red", "fill:red/*note*/", "/*note*/fill:red",
            "fill: /* ; : } ' */ red", "fill:red !/*note*/important;fill:blue", "fill:red;/*end; :*/" })
            yield return [location, declaration];
    }

    [Theory]
    [MemberData(nameof(ValidDeclarations))]
    public void Comments_preserve_existing_declarations_and_importance(string location, string declaration)
    {
        const string box = "<rect width='8' height='8'/>";
        string content = location switch
        {
            "inline" => "<rect width='8' height='8' style='" + declaration.Replace("'", "&apos;", StringComparison.Ordinal) + "'/>",
            "sheet" => "<style>rect{" + declaration + "}</style>" + box,
            _ => "<defs><linearGradient id='p'><stop style='" + declaration.Replace("fill", "stop-color", StringComparison.Ordinal).Replace("'", "&apos;", StringComparison.Ordinal) + "'/></linearGradient></defs><rect width='8' height='8' fill='url(#p)'/>"
        };
        Assert.Equal(Render("<rect width='8' height='8' fill='red'/>"), Render(content));
    }

    [Theory]
    [InlineData("rect/**/.hot")]
    [InlineData("rect/*one*//**/.hot")]
    [InlineData("rect[id/**/=/**/'box'].hot")]
    public void Selector_comments_preserve_adjacent_compound_tokens(string selector)
        => Assert.Equal(Render("<rect width='8' height='8' fill='red'/>"),
            Render("<style>" + selector + "{fill:red}</style><rect width='8' height='8' id='box' class='hot'/>"));

    [Theory]
    [InlineData(".ho/**/t{fill:red}")]
    [InlineData("re/**/ct{fill:red}")]
    [InlineData("#bo/**/x{fill:red}")]
    [InlineData("#/**/box{fill:red}")]
    [InlineData("rect{fi/**/ll:red}")]
    [InlineData("text{font-/**/family:A/**/B,Example}")]
    public void Comments_cannot_join_identifiers_into_another_supported_token(string sheet)
        => Assert.Equal(SvgErrorCode.UnsupportedFeature,
            Assert.Throws<SvgException>(() => Render("<style>" + sheet + "</style><rect width='8' height='8' id='box' class='hot'/>")).ErrorCode);

    [Fact]
    public void Comment_only_styles_are_inert_and_quoted_family_text_remains_literal()
    {
        Assert.Equal(Render("<rect width='8' height='8'/>"), Render("<rect width='8' height='8' style='/* comment; : */'/>"));
        InlineStyle style = SvgInlineStyleParser.Parse("font-family: '/*literal*/'; fill:/**/red", CancellationToken.None, true);
        Assert.Equal("'/*literal*/'", style.TextFont.Family);
        Assert.Equal("red", style.Fill);
    }

    [Fact]
    public void Long_comment_scans_observe_cancellation_without_owned_storage()
    {
        string declaration = "fill:/*" + new string('x', 65_536) + "*/red";
        Assert.Equal("red", SvgInlineStyleParser.Parse(declaration, CancellationToken.None).Fill);
        string padding = new(' ', 65_536);
        Assert.Equal("red", SvgInlineStyleParser.Parse("/*start*/" + padding + "fill:red" + padding + "/**/", CancellationToken.None).Fill);
        using CancellationTokenSource cancellation = new(); cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => SvgInlineStyleParser.Parse(declaration, cancellation.Token));
        Assert.Equal(SvgErrorCode.MalformedInput, Assert.Throws<SvgException>(() => SvgInlineStyleParser.Parse("fill:/*open", CancellationToken.None)).ErrorCode);
    }

    private static byte[] Render(string body)
        => SvgConverter.ConvertToPng(System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8' fill='blue'>" + body + "</svg>"), SvgRenderOptions.Default, CancellationToken.None).ToArray();
}
