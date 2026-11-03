using Lokad.Svg.Fonts;

namespace Lokad.Svg.Tests;

public sealed partial class OpenTypeFontTests
{
    [Fact]
    [Trait("Layer", "Rendering")]
    public async Task Relative_lengths_bind_instance_fonts_and_preserve_path_y_boundaries()
    {
        using SvgFontSnapshot fonts = await SvgFontSnapshot.CreateAsync([CreateResolution(CreateTriangleFont())], CancellationToken.None);
        const string start = "<svg xmlns='http://www.w3.org/2000/svg' width='128' height='128' font-family='Example'>";
        const string path = "<path id='p' d='M8 104H120'/>";
        string actual = start + "<defs>" + path + "<text id='s' y='24'>A<tspan dy='1em'>A</tspan></text></defs>"
            + "<use href='#s' x='8'/><use href='#s' x='72' font-size='24'/>"
            + "<text y='1em 2em'><textPath href='#p'><tspan dx='.5em' dy='.25em'>AA</tspan></textPath></text></svg>";
        string expected = start + "<defs>" + path + "</defs><text x='8' y='24'>A<tspan dy='16'>A</tspan></text>"
            + "<text x='72' y='24' font-size='24'>A<tspan dy='24'>A</tspan></text>"
            + "<text y='16 32'><textPath href='#p'><tspan dx='8' dy='4'>AA</tspan></textPath></text></svg>";
        SvgRenderOptions options = new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, fonts);
        Assert.Equal(SvgConverter.ConvertToPng(System.Text.Encoding.UTF8.GetBytes(expected), options, CancellationToken.None).ToArray(),
            SvgConverter.ConvertToPng(System.Text.Encoding.UTF8.GetBytes(actual), options, CancellationToken.None).ToArray());
    }

    [Fact]
    [Trait("Layer", "Contract")]
    public async Task Declaration_relative_position_lists_preserve_borrowed_storage_on_refusal_cancellation_and_retry()
    {
        using SvgFontSnapshot fonts = await SvgFontSnapshot.CreateAsync([CreateResolution(CreateTriangleFont())], CancellationToken.None);
        byte[] input = "<svg xmlns='http://www.w3.org/2000/svg' width='80' height='80'><text font-family='Example' font-size='20' x='1em 2em' y='1em'>A<tspan font-size='24' dx='.25em .5em' dy='.5em'>AA</tspan>A</text></svg>"u8.ToArray();
        SvgParseOwnershipAssertions.PreservesBorrowedStorageOnDenialCancellationAndRetry(input, fonts);
    }
}
