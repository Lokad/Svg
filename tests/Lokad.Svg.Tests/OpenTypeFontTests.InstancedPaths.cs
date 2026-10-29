using Lokad.Svg.Fonts;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed partial class OpenTypeFontTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Instanced_path_scopes_preserve_mixed_flow_parent_ranges_and_following_siblings(bool mixed)
    {
        const string prefix = "<svg xmlns='http://www.w3.org/2000/svg' width='64' height='16' font-family='Example' font-size='100'>";
        const string resources = "<defs><path id='p' d='M0 5H16'/><clipPath id='c'><rect x='1' width='16' height='10'/></clipPath><mask id='m' maskUnits='userSpaceOnUse' x='0' y='0' width='24' height='12'><rect width='24' height='12' fill='white'/><rect x='4' width='4' height='12' fill='#888'/></mask></defs>";
        string content = mixed ? "A<textPath href='#p'>AA</textPath>A" : "<textPath href='#p'>AA</textPath>";
        string text = "<text y='5' fill='#244a77' stroke='#d65b3b' stroke-width='.45' clip-path='url(#c)' mask='url(#m)'>" + content + "</text>";
        const string siblings = "<g clip-path='url(#c)'><text y='10'><textPath href='#p'>A</textPath>A</text></g><g mask='url(#m)'><rect x='12' y='9' width='4' height='3' fill='blue'/></g><rect x='55' y='13' width='7' height='2' fill='green'/>";
        byte[] input = System.Text.Encoding.UTF8.GetBytes(prefix + "<use href='#label' x='24'/><defs><g id='label'>" + text + "</g></defs>" + siblings + resources + "</svg>"), unchanged = input.ToArray();
        byte[] direct = System.Text.Encoding.UTF8.GetBytes(prefix + "<g transform='translate(24)'>" + text + "</g>" + siblings + resources + "</svg>");
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync([CreateResolution(CreateTriangleFont())], CancellationToken.None);
        SvgRenderOptions options = new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, snapshot);
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(4_000_000));
        using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, snapshot, CancellationToken.None))
            Assert.Equal(SvgConverter.ConvertToPng(direct, options, CancellationToken.None).ToArray(), SvgConverter.ConvertToPng(root, options, budget, CancellationToken.None).ToArray());
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        Assert.Equal(unchanged, input);
    }
}
