namespace Lokad.Svg.Tests;

public sealed partial class OpenTypeFontTests
{
    [Theory]
    [InlineData("rect", false)]
    [InlineData("rect", true)]
    [InlineData("path", false)]
    [InlineData("path", true)]
    [InlineData("polygon", false)]
    [InlineData("polygon", true)]
    public async Task Referenced_leaf_masks_survive_reuse_of_unchanged_paint(string element, bool symbol)
    {
        (var catalog, var provider, _) = CreateExamplePack(CreateTriangleFont());
        foreach (bool forward in new[] { false, true })
            foreach (bool disabled in new[] { false, true })
            {
                string mask = "<mask id='m' mask-type='alpha'" + (disabled ? " style='display:none'" : "") + "><rect width='4' height='8' fill='white'/></mask>";
                string node = "<" + element + " " + (element == "rect" ? "width='8' height='8'" : element == "path" ? "d='M0 0H8V8H0Z'" : "points='0 0 8 0 8 8 0 8'") + " fill='red' mask='url(#m)'";
                byte[] expectedInput = Document((forward ? "" : "<defs>" + mask + "</defs>") + node + "/>" + (forward ? "<defs>" + mask + "</defs>" : ""));
                string source = symbol ? "<symbol id='s'>" + node + "/></symbol>" : node + " id='s'/>";
                byte[] input = Document((forward ? "" : "<defs>" + mask + source + "</defs>") + "<use href='#s'/>" + (forward ? "<defs>" + mask + source + "</defs>" : ""));
                byte[] expected = Render(expectedInput);
                byte[] unmasked = Render(Document("<rect width='8' height='8' fill='red'/>"));
                if (disabled) Assert.Equal(unmasked, expected);
                else Assert.NotEqual(unmasked, expected);
                Assert.Equal(expected, Render(input));
                using SvgPreparedDocument prepared = await SvgPreparedDocument.CreateAsync(input, catalog, provider, SvgRenderOptions.Default, CancellationToken.None);
                Assert.Equal(expected, prepared.ConvertToPng(CancellationToken.None).ToArray());
            }
    }
}
