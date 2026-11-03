using Lokad.Svg.Fonts;
using Lokad.Svg.Parsing;

namespace Lokad.Svg.Tests;

[Trait("Layer", "Mechanism")]
public sealed class SvgClipChildRoleTests
{
    [Theory]
    [InlineData("<g><path d='M0 0H4V4Z'/></g>")]
    [InlineData("<g id='dormant'><g><path id='leaf' d='M0 0H4V4Z'/></g></g>")]
    [InlineData("<g/>")]
    public void Clip_groups_keep_an_empty_resource_without_adding_descendant_geometry(string child)
    {
        using SvgRoot root = SvgRootParser.Parse(Wrap("<defs><clipPath id='c'>" + child + "</clipPath></defs><rect width='8' height='8' clip-path='url(#c)'/>"), SvgRenderLimits.Default, CancellationToken.None);
        AssertEmptyClip(root);
    }

    [Theory]
    [InlineData("<g><text font-family='NeverOpen'>Z</text></g>")]
    [InlineData("<g id='dormant'><text font-family='NeverOpen'>Z</text></g>")]
    public void Unselected_clip_group_text_does_not_request_fonts(string group)
    {
        byte[] input = Wrap("<defs><clipPath id='c'>" + group + "</clipPath></defs><path d='M0 0H8V8Z'/>");
        Assert.Empty(SvgFontRequestDiscovery.Discover(input, CancellationToken.None));
        using SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, CancellationToken.None);
        Assert.False(root.HasUnsupportedChild);
        Assert.Single(root.Geometries);
    }

    [Theory]
    [InlineData("label")]
    [InlineData("group")]
    public void Selected_clip_descendants_discover_only_their_live_font_and_emitted_scalars(string target)
    {
        byte[] input = Wrap("<defs><clipPath id='c'><g><text font-family='NeverOpen'>Z</text><g id='group' font-family='Example' style='text-transform:lowercase'><text id='label'>A</text></g></g></clipPath></defs><use href='#" + target + "' font-family='Example' style='text-transform:lowercase'/>");
        using SvgFontCoverageDiscovery coverage = SvgFontRequestDiscovery.DiscoverWithCoverage(input, SvgRenderLimits.Default, CancellationToken.None);
        SvgFontCoverageRequest entry = Assert.Single(coverage.Requests);
        Assert.Equal("Example", entry.Request.Family);
        Assert.Equal(new uint[] { 'a' }, entry.Scalars);
    }

    [Fact]
    public void A_group_referenced_as_clip_content_does_not_acquire_its_text_font()
    {
        byte[] input = Wrap("<defs><clipPath id='c'><g id='group'><text font-family='NeverOpen'>Z</text></g></clipPath><clipPath id='d'><use href='#group'/></clipPath></defs><rect width='8' height='8' clip-path='url(#d)'/>");
        Assert.Empty(SvgFontRequestDiscovery.Discover(input, CancellationToken.None));
        using SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, CancellationToken.None);
        AssertEmptyClip(root);
    }

    [Fact]
    public void Duplicate_ids_inside_dormant_clip_groups_keep_the_normal_definition_contract()
    {
        byte[] input = Wrap("<defs><path id='p' d='M0 0H4V4Z'/><clipPath id='c'><g><path id='p' d='M0 0H2V2Z'/></g></clipPath></defs>");
        Assert.Equal(SvgErrorCode.MalformedInput, Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, CancellationToken.None)).ErrorCode);
    }

    private static byte[] Wrap(string body)
        => System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'>" + body + "</svg>");

    private static void AssertEmptyClip(SvgRoot root)
    {
        Assert.False(root.HasUnsupportedChild);
        // The scene retains its source rectangle with an empty paint binding.
        // Clip descendants must neither add scene geometry nor expand that binding.
        SvgRectangle rectangle = Assert.Single(root.Rectangles);
        Assert.Equal(0, rectangle.Width);
        Assert.Equal(0, rectangle.Height);
    }
}
