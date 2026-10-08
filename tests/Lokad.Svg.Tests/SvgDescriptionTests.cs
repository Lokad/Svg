using Lokad.Svg.Fonts;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgDescriptionTests
{
    [Theory]
    [InlineData("title")]
    [InlineData("desc")]
    [InlineData("metadata")]
    public void Descriptive_descendants_do_not_paint_define_ids_or_validate_shape_attributes(string element)
    {
        string description = "<" + element + "><rect id='unused' width='invalid' transform='invalid'/><text font-family='Unused'>ignored</text></" + element + ">";
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='64' height='64'><g>" + description + "<rect id='unused' width='8' height='8'/></g></svg>");
        RenderBudget budget = new(1_000_000);
        using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None))
        {
            Assert.False(root.HasUnsupportedChild);
            Assert.Single(root.Geometries);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        Assert.Empty(SvgFontRequestDiscovery.Discover(input, CancellationToken.None));
        byte[] withText = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg'><g>" + description + "<text font-family='Example'>x</text></g></svg>");
        Assert.Equal("Example", Assert.Single(SvgFontRequestDiscovery.Discover(withText, CancellationToken.None)).Family);
    }

    [Theory]
    [InlineData("title", "<animate attributeName='opacity' values='0;1' dur='1s'/>")]
    [InlineData("desc", "<script>ignored</script>")]
    [InlineData("metadata", "<style>rect { fill: red; }</style>")]
    public void Descriptions_preserve_explicit_active_content_rejection(string element, string active)
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg'><g><" + element + ">" + active + "</" + element + "><text font-family='Example'>x</text></g></svg>");
        byte[] rendered = System.Text.Encoding.UTF8.GetBytes(System.Text.Encoding.UTF8.GetString(input).Replace("<text font-family='Example'>x</text>", "<rect width='8' height='8'/>", StringComparison.Ordinal));
        using (SvgRoot root = SvgRootParser.Parse(rendered, SvgRenderLimits.Default, CancellationToken.None))
            Assert.True(root.HasUnsupportedChild);
        Assert.Empty(SvgFontRequestDiscovery.Discover(input, CancellationToken.None));
    }
}
