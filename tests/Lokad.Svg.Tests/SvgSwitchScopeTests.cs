using TextEncoding = System.Text.Encoding;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgSwitchScopeTests
{
    public static IEnumerable<object[]> Cases()
    {
        const string red = "<rect x='2' y='2' width='6' height='6' fill='red'/>";
        const string blue = "<rect x='12' y='12' width='6' height='6' fill='blue'/>";
        const string green = "<rect width='32' height='32' fill='green'/>";
        const string source = "<rect id='s' x='12' y='12' width='6' height='6' fill='blue'/>";
        const string use = "<use href='#s'/>";
        const string gradient = "<linearGradient id='p'><stop stop-color='blue'/><stop offset='1' stop-color='red'/></linearGradient>";
        const string painted = "<rect x='12' y='12' width='6' height='6' fill='url(#p)'/>";
        (string Name, string Subject, string Expected)[] cases =
        [
            ("following-group", "<switch>" + red + "</switch><g>" + blue + "</g>", red + blue),
            ("following-link", "<switch>" + red + "</switch><a>" + blue + "</a>", red + blue),
            ("following-viewport", "<switch>" + red + "</switch><svg width='32' height='32'>" + blue + "</svg>", red + blue),
            ("following-definitions", "<switch>" + red + "</switch><defs>" + source + "</defs>" + use, "<defs>" + source + "</defs>" + red + use),
            ("following-gradient", "<switch>" + red + "</switch><defs>" + gradient + "</defs>" + painted, "<defs>" + gradient + "</defs>" + red + painted),
            ("following-switch", "<switch>" + red + "</switch><switch><g>" + blue + "</g>" + green + "</switch>", red + blue),
            ("nested-switch", "<switch><g><switch>" + red + "</switch>" + blue + "</g>" + green + "</switch>", red + blue),
            ("nested-group", "<g><switch>" + red + "</switch></g><g><g>" + blue + "</g></g>", red + blue),
            ("closed-empty-switch", "<switch></switch><g>" + red + blue + "</g>", red + blue),
            ("unselected-nested-switch", "<switch>" + red + "<switch>" + green + "</switch></switch><g>" + blue + "</g>", red + blue),
            ("language-fallback", "<switch><g systemLanguage='en'>" + green + "</g>" + red + "</switch><g>" + blue + "</g>", red + blue),
            ("filtered-empty-sibling", "<defs><filter id='f'><feFlood flood-color='blue'/></filter></defs><switch>" + red + "</switch><g filter='url(#f)'/><g>" + blue + "</g>", "<defs><filter id='f'><feFlood flood-color='blue'/></filter></defs>" + red + "<g filter='url(#f)'/>" + blue)
        ];
        foreach ((string name, string subject, string expected) in cases)
        foreach (double scale in new[] { 1d, 1.5 })
            yield return [name, subject, expected, scale];
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Closing_a_switch_restores_surrounding_selection(string name, string subject, string expected, double scale)
    {
        const string prefix = "<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'>";
        byte[] input = TextEncoding.UTF8.GetBytes(prefix + subject + "</svg>");
        byte[] direct = TextEncoding.UTF8.GetBytes(prefix + expected + "</svg>");
        SvgRenderOptions options = new(null, scale, SvgColor.Transparent, SvgRenderLimits.Default);
        Assert.True(SvgConverter.ConvertToPng(direct, options, CancellationToken.None).Span.SequenceEqual(
            SvgConverter.ConvertToPng(input, options, CancellationToken.None).Span), name);

        RenderBudget budget = new RenderBudget(new HostMemoryBudget(8_000_000));
        using (SvgRoot scene = SvgRootParser.Parse(input, options.Limits, budget, CancellationToken.None))
        using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(scene, options, budget, CancellationToken.None))
            Assert.False(scene.HasUnsupportedChild);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Theory]
    [InlineData("<unknown/>")]
    [InlineData("<animate attributeName='x' to='4'/>")]
    [InlineData("<rect width='8' height='8' mask='url(#missing)'/>")]
    public void Later_containers_keep_their_normal_diagnostics(string child)
    {
        byte[] input = TextEncoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'><switch><g/></switch><g>" + child + "</g></svg>");
        SvgException error = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None));
        Assert.Equal(SvgErrorCode.UnsupportedFeature, error.ErrorCode);
    }
}
