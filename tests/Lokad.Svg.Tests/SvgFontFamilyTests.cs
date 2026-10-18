using Lokad.Svg.Fonts;
using Lokad.Svg.Parsing;

namespace Lokad.Svg.Tests;

public sealed class SvgFontFamilyTests
{
    [Theory]
    [InlineData("""<svg xmlns="http://www.w3.org/2000/svg"><text font-family="'A,B', E\78 ample">A</text></svg>""", "A,B")]
    [InlineData("""<svg xmlns="http://www.w3.org/2000/svg"><text style="font-family:'A;B', E\78 ample;font-weight:700">A</text></svg>""", "A;B")]
    [InlineData("""<svg xmlns="http://www.w3.org/2000/svg"><style>.a{font-family:'A,B', E\78 ample}</style><text class="a">A</text></svg>""", "A,B")]
    [InlineData("""<svg xmlns="http://www.w3.org/2000/svg"><text style="font-family:'A:B',Example">A</text></svg>""", "A:B")]
    [InlineData("""<svg xmlns="http://www.w3.org/2000/svg"><text style="font-family:A\;B,Example">A</text></svg>""", "A;B")]
    [InlineData("""<svg xmlns="http://www.w3.org/2000/svg"><text style="font-family:'A!important',Example">A</text></svg>""", "A!important")]
    [InlineData("""<svg xmlns="http://www.w3.org/2000/svg"><text style="font-family:A\!important,Example">A</text></svg>""", "A!important")]
    [InlineData("""<svg xmlns="http://www.w3.org/2000/svg"><style>.a{font-family:'A!important',Example}</style><text class="a">A</text></svg>""", "A!important")]
    [InlineData("""<svg xmlns="http://www.w3.org/2000/svg"><style>.a{font-family:'A}B',Example;fill:red}</style><text class="a">A</text></svg>""", "A}B")]
    [InlineData("""<svg xmlns="http://www.w3.org/2000/svg"><style>.a{font-family:'A{B',Example}</style><text class="a">A</text></svg>""", "A{B")]
    [InlineData("""<svg xmlns="http://www.w3.org/2000/svg"><style>.a{font-family:'A/*B*/C',Example}</style><text class="a">A</text></svg>""", "A/*B*/C")]
    [InlineData("""<svg xmlns="http://www.w3.org/2000/svg"><style>.a{font-family:A/**/B,Example}</style><text class="a">A</text></svg>""", "A B")]
    [InlineData("""<svg xmlns="http://www.w3.org/2000/svg"><style>.a{font-/**/family:A/**/B,Example}</style><text class="a">A</text></svg>""", "A B")]
    [InlineData("""<svg xmlns="http://www.w3.org/2000/svg"><style>.a{font-family:A\}B,Example}</style><text class="a">A</text></svg>""", "A}B")]
    public void Discovery_shares_quoted_and_escaped_syntax_across_presentation_inline_and_stylesheet(string svg, string first)
    {
        SvgFontFaceRequest request = Assert.Single(SvgFontRequestDiscovery.Discover(System.Text.Encoding.UTF8.GetBytes(svg), CancellationToken.None));
        Assert.Equal(new[] { first, "Example" }, request.Families.Select(family => family.Name));
    }

    [Theory]
    [InlineData("font-family:'A!important',Example", false)]
    [InlineData("font-family:'A!important',Example!important", true)]
    [InlineData("font-family:A\\!important,Example", false)]
    [InlineData("font-family:'A!important',Example ! important", true)]
    public void Literal_bangs_do_not_change_real_declaration_priority(string css, bool important)
    {
        InlineStyle style = SvgInlineStyleParser.Parse(css, new ParserWorkCounter(1000), CancellationToken.None, true);
        SvgFontFaceRequest request = SvgFontFaceRequest.FromCss(style.TextFont.Family ?? throw new InvalidDataException(), 400, false);
        Assert.Equal("A!important", request.Families[0].Name);
        Assert.Equal(important, (style.Important & InlineImportantProperties.Family) != 0);
    }

    [Fact]
    public void An_escaped_bang_at_the_end_of_a_name_is_not_an_important_suffix()
    {
        InlineStyle style = SvgInlineStyleParser.Parse("font-family:A\\!important", new ParserWorkCounter(1000), CancellationToken.None, true);
        Assert.Equal("A!important", Assert.Single(SvgFontFaceRequest.FromCss(style.TextFont.Family ?? throw new InvalidDataException(), 400, false).Families).Name);
        Assert.Equal(InlineImportantProperties.None, style.Important);
    }

    [Fact]
    public void Inherit_and_initial_keep_family_list_and_generic_semantics()
    {
        byte[] svg = """<svg xmlns="http://www.w3.org/2000/svg"><g font-family="Missing, Example"><text style="font-family:inherit">A<tspan font-family="initial">A</tspan></text></g></svg>"""u8.ToArray();
        SvgFontFaceRequest[] requests = SvgFontRequestDiscovery.Discover(svg, CancellationToken.None);
        Assert.Equal(2, requests.Length);
        Assert.Equal(new[] { "Missing", "Example" }, requests[0].Families.Select(family => family.Name));
        Assert.True(requests[1].Families[0].IsGeneric);
        Assert.Equal("serif", requests[1].Families[0].Name);
    }

    [Theory]
    [InlineData("Arial, sans-serif", "Arial|sans-serif", "False|True")]
    [InlineData("'Missing Face', Arimo, sans-serif", "Missing Face|Arimo|sans-serif", "False|False|True")]
    [InlineData("'serif', SERIF", "serif|serif", "False|True")]
    [InlineData("'Comma, Face', A\\,B", "Comma, Face|A,B", "False|False")]
    [InlineData("  Gill   Sans  , monospace ", "Gill Sans|monospace", "False|True")]
    [InlineData("\\41 rimo, s\\61 ns-serif", "Arimo|sans-serif", "False|True")]
    [InlineData("'A\\'B', \"C\\\"D\"", "A'B|C\"D", "False|False")]
    [InlineData("A/**/B, serif", "A B|serif", "False|True")]
    [InlineData("'A\\\r\nB', Example", "AB|Example", "False|False")]
    [InlineData("'initial', Example", "initial|Example", "False|False")]
    [InlineData("Noto Serif, serif", "Noto Serif|serif", "False|True")]
    [InlineData("Family serif, serif", "Family serif|serif", "False|True")]
    [InlineData("inherit Family, serif", "inherit Family|serif", "False|True")]
    [InlineData("Noto inherit, serif", "Noto inherit|serif", "False|True")]
    [InlineData("'serif Family', serif", "serif Family|serif", "False|True")]
    [InlineData("'A\\0 B', Example", "A\uFFFDB|Example", "False|False")]
    public void Decodes_ordered_names_and_distinguishes_generic_keywords(string css, string names, string generics)
    {
        SvgFontFaceRequest request = SvgFontFaceRequest.FromCss(css, 700, true);
        Assert.Equal(names, string.Join("|", request.Families.Select(family => family.Name)));
        Assert.Equal(generics, string.Join("|", request.Families.Select(family => family.IsGeneric)));
        SvgFontFaceRequest inherited = SvgFontFaceRequest.FromCss(request.Family, 700, true);
        Assert.True(request.Matches(inherited));
    }

    [Theory]
    [InlineData("")]
    [InlineData("A,")]
    [InlineData(",A")]
    [InlineData("A,,B")]
    [InlineData("''")]
    [InlineData("'Unclosed")]
    [InlineData("'A' B")]
    [InlineData("5font")]
    [InlineData("A!")]
    [InlineData("A\\\nB")]
    [InlineData("A/*")]
    [InlineData("serif Family")]
    [InlineData("inherit, Example")]
    public void Categorizes_malformed_lists_without_selecting_a_prefix(string css)
        => Assert.Equal(SvgErrorCode.MalformedInput, Assert.Throws<SvgException>(() => SvgFontFaceRequest.FromCss(css, 400, false)).ErrorCode);

    [Fact]
    public void Tokenization_is_bounded_and_observes_shared_work_and_original_cancellation()
    {
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgFontFaceRequest.FromCss(new string('a', 4097), 400, false)).ErrorCode);
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgFontFaceRequest.FromCss(string.Join(',', Enumerable.Repeat("A", 257)), 400, false)).ErrorCode);
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgFontFaceRequest.FromCss("Example", 400, false, null, new(2), CancellationToken.None)).ErrorCode);
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        Assert.Equal(cancellation.Token, Assert.Throws<OperationCanceledException>(() => SvgFontFaceRequest.FromCss("A", 400, false, null, new(100), cancellation.Token)).CancellationToken);
    }
}
