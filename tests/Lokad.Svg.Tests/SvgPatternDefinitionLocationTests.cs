using TextEncoding = System.Text.Encoding;
using Lokad.Svg.Fonts;

namespace Lokad.Svg.Tests;

[Trait("Layer", "Mechanism")]
public sealed class SvgPatternDefinitionLocationTests
{
    public enum Location { Root, Definitions, Group, LinkedGroup, ViewportGroup }

    [Theory]
    [InlineData("<a>{0}</a>")]
    [InlineData("<svg width='16' height='16'>{0}</svg>")]
    [InlineData("<defs><svg width='16' height='16'>{0}</svg></defs>")]
    public void Unadmitted_pattern_parents_remain_unsupported(string parent)
    {
        const string pattern = "<pattern id='p' width='4' height='4' patternUnits='userSpaceOnUse'><rect width='2' height='2' fill='red'/></pattern>";
        byte[] input = TextEncoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'>"
            + parent.Replace("{0}", pattern, StringComparison.Ordinal) + "<rect width='16' height='16' fill='url(#p)'/></svg>");
        Assert.Equal(SvgErrorCode.UnsupportedFeature, Assert.Throws<SvgException>(() =>
            SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None)).ErrorCode);
    }

    [Theory]
    [InlineData(Location.Root)]
    [InlineData(Location.Definitions)]
    [InlineData(Location.Group)]
    [InlineData(Location.LinkedGroup)]
    [InlineData(Location.ViewportGroup)]
    public void Eligible_pattern_locations_select_their_actual_font_content(Location location)
    {
        const string pattern = "<pattern id='p' width='8' height='8' patternUnits='userSpaceOnUse'><text font-family='Example'>A</text></pattern>";
        string declaration = location switch
        {
            Location.Root => pattern,
            Location.Definitions => "<defs>" + pattern + "</defs>",
            Location.Group => "<g>" + pattern + "</g>",
            Location.LinkedGroup => "<g><a>" + pattern + "</a></g>",
            Location.ViewportGroup => "<g><svg width='16' height='16'>" + pattern + "</svg></g>",
            _ => throw new ArgumentOutOfRangeException(nameof(location))
        };
        byte[] input = TextEncoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'>" + declaration
            + "<rect width='16' height='16' fill='url(#p)'/></svg>");
        Assert.Equal("Example", Assert.Single(SvgFontRequestDiscovery.Discover(input, CancellationToken.None)).Family);
    }
}
