namespace Lokad.Svg.Tests;

public sealed class SvgDisplayInheritanceTests
{
    public static IEnumerable<object[]> Cases(bool text)
    {
        foreach (string location in new[] { "leaf", "group", "use", "symbol", "alias", "rendered", "viewport", "root", "pattern", "mask", "marker", "clip", "span" })
        foreach (string parent in new[] { "inline", "none" })
        foreach (bool forward in new[] { false, true })
        foreach (string form in new[] { "attribute", "inline", "stylesheet" })
        {
            if (location == "span" && !text || location is "mask" or "marker" or "clip" && text) continue;
            if (forward && location is "leaf" or "group" or "viewport" or "root" or "span") continue;
            if (location == "root" && parent == "none") continue;
            string expected = location is "symbol" or "alias" or "rendered" or "root" ? "inline" : parent;
            yield return [location, parent, forward, form, Document("inherit"), Document(expected)];

            string Document(string display)
            {
                string declaration = form == "attribute" ? "display='" + display + "'"
                    : form == "inline" ? "style='display:" + display + "'" : "class='subject'";
                string css = form == "stylesheet" ? "<style>.subject{display:" + display + "}</style>" : "";
                string bare = text ? "<text x='2' y='15' font-size='12' font-family='Example' fill='blue'>A</text>"
                    : "<rect x='4' y='4' width='8' height='8' fill='blue'/>";
                string child = bare.Replace(text ? "<text " : "<rect ", (text ? "<text " : "<rect ") + declaration + " ", StringComparison.Ordinal);
                string definitions = "";
                string body;
                switch (location)
                {
                    case "leaf": body = "<g display='" + parent + "'>" + child + "</g>"; break;
                    case "group": body = "<g display='" + parent + "'><g " + declaration + ">" + bare + "</g></g>"; break;
                    case "use":
                        definitions = "<defs>" + bare.Replace(text ? "<text " : "<rect ", (text ? "<text " : "<rect ") + "id='s' ", StringComparison.Ordinal) + "</defs>";
                        body = "<g display='" + parent + "'><use href='#s' " + declaration + "/></g>"; break;
                    case "symbol":
                        definitions = "<defs><symbol id='s' viewBox='0 0 32 32' " + declaration + ">" + bare + "</symbol></defs>";
                        body = "<use href='#s' width='32' height='32' display='" + parent + "'/>"; break;
                    case "alias":
                        definitions = "<defs><symbol id='s' viewBox='0 0 32 32'><g display='none'>"
                            + child.Replace(text ? "<text " : "<rect ", (text ? "<text " : "<rect ") + "id='base' ", StringComparison.Ordinal)
                            + "</g><use href='#base' " + declaration + "/></symbol></defs>";
                        body = "<use href='#s' display='" + parent + "'/>"; break;
                    case "rendered":
                        definitions = "<g display='none'><g id='s' " + declaration + ">" + child + "</g></g>";
                        body = "<use href='#s' display='" + parent + "'/>"; break;
                    case "viewport":
                        css += "<style>#v{display:" + parent + "}</style>";
                        body = "<svg id='v' width='32' height='32'>" + child + "</svg>"; break;
                    case "root": return Start.Replace("width='32'", declaration + " width='32'", StringComparison.Ordinal) + css + bare + "</svg>";
                    case "pattern":
                        definitions = "<g display='none'><defs><pattern id='p' patternUnits='userSpaceOnUse' width='32' height='32' display='" + parent + "'>" + child + "</pattern></defs></g>";
                        body = "<rect width='32' height='32' fill='url(#p)'/>"; break;
                    case "mask":
                        definitions = "<defs><mask id='m' mask-type='alpha' maskUnits='userSpaceOnUse' x='0' y='0' width='32' height='32' style='display:" + parent + "'>" + child + "</mask></defs>";
                        body = "<rect width='32' height='32' fill='blue' mask='url(#m)'/>"; break;
                    case "marker":
                        definitions = "<defs><marker id='m' markerUnits='userSpaceOnUse' markerWidth='32' markerHeight='32' display='" + parent + "'>" + child + "</marker></defs>";
                        body = "<path d='M0 0H28' stroke='black' marker-start='url(#m)'/>"; break;
                    case "clip":
                        definitions = "<defs><clipPath id='c' style='display:" + parent + "'>" + child + "</clipPath></defs>";
                        body = "<rect width='32' height='32' fill='blue' clip-path='url(#c)'/>"; break;
                    default:
                        body = "<text x='2' y='15' font-size='12' font-family='Example' fill='blue' display='" + parent + "'><tspan " + declaration + ">A</tspan></text>"; break;
                }
                return Start + css + (forward ? body + definitions : definitions + body) + "</svg>";
            }
        }
    }

    [Theory]
    [MemberData(nameof(Cases), false)]
    public void Explicit_inheritance_matches_the_logical_parent_through_source_replay(string location, string parent, bool forward, string form, string actual, string expected)
    {
        _ = (location, parent, forward, form);
        Assert.Equal(Render(expected), Render(actual));
    }

    [Theory]
    [InlineData("initial")]
    [InlineData("unset")]
    public void Noninherited_keywords_reset_display_instead_of_copying_the_parent(string keyword)
    {
        const string prefix = Start + "<defs><marker id='m' markerUnits='userSpaceOnUse' markerWidth='32' markerHeight='32' display='none'>";
        const string suffix = "</marker></defs><path d='M0 0H28' stroke='black' marker-start='url(#m)'/></svg>";
        string Child(string display) => "<rect width='8' height='8' fill='blue' style='display:" + display + "'/>";
        Assert.Equal(Render(prefix + Child("inline") + suffix), Render(prefix + Child(keyword) + suffix));
        Assert.NotEqual(Render(prefix + Child("inherit") + suffix), Render(prefix + Child(keyword) + suffix));
    }

    private const string Start = "<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'>";
    private static byte[] Render(string input)
        => SvgConverter.ConvertToPng(System.Text.Encoding.UTF8.GetBytes(input), SvgRenderOptions.Default, CancellationToken.None).ToArray();
}
