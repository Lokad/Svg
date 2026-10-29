using Lokad.Svg.Fonts;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed partial class OpenTypeFontTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Mixed_direct_path_runs_resume_at_the_path_endpoint_with_forward_definitions(bool forward)
    {
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync([CreateResolution(CreateTriangleFont())], CancellationToken.None);
        const string prefix = "<svg xmlns='http://www.w3.org/2000/svg' width='16' height='6'>";
        const string definition = "<defs><path id='p' d='M0 3H8'/></defs>";
        const string source = "<text font-family='Example' font-size='100' y='3'>A<textPath href='#p'>A</textPath>A</text>";
        byte[] input = System.Text.Encoding.UTF8.GetBytes(prefix + (forward ? source + definition : definition + source) + "</svg>");
        const string expected = "<text font-family='Example' font-size='100' y='3'>A</text><text font-family='Example' font-size='100'><textPath href='#p'>A</textPath></text><text font-family='Example' font-size='100' x='8' y='3'>A</text>";
        byte[] reference = System.Text.Encoding.UTF8.GetBytes(prefix + definition + expected + "</svg>");
        SvgRenderOptions options = new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, snapshot);
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(1_000_000));
        using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, snapshot, CancellationToken.None))
            Assert.Equal(SvgConverter.ConvertToPng(reference, options, CancellationToken.None).ToArray(), SvgConverter.ConvertToPng(root, options, budget, CancellationToken.None).ToArray());
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public async Task Original_mixed_mask_input_keeps_one_resource_scope_and_following_siblings()
    {
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync([CreateResolution(CreateTriangleFont())], CancellationToken.None);
        const string source = "<svg xmlns='http://www.w3.org/2000/svg' width='8' height='6'><defs><path id='p' d='M0 3H7'/><mask id='m' maskUnits='userSpaceOnUse' x='0' y='0' width='8' height='6'><rect x='1' y='1' width='3' height='3' fill='white' fill-opacity='.5'/></mask></defs><text font-family='Example' font-size='100' y='3' mask='url(#m)'>A<textPath href='#p'>A</textPath></text><rect x='6' y='4' width='2' height='2' fill='blue'/></svg>";
        byte[] input = System.Text.Encoding.UTF8.GetBytes(source), unchanged = input.ToArray();
        string reference = source.Replace("<text font-family='Example' font-size='100' y='3' mask='url(#m)'>A<textPath href='#p'>A</textPath></text>", "<g mask='url(#m)'><text font-family='Example' font-size='100' y='3'>A</text><text font-family='Example' font-size='100'><textPath href='#p'>A</textPath></text></g>", StringComparison.Ordinal);
        SvgRenderOptions options = new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, snapshot);
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(1_000_000));
        using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, snapshot, CancellationToken.None))
            Assert.Equal(SvgConverter.ConvertToPng(System.Text.Encoding.UTF8.GetBytes(reference), options, CancellationToken.None).ToArray(), SvgConverter.ConvertToPng(root, options, budget, CancellationToken.None).ToArray());
        Assert.Equal(0, budget.Snapshot().CurrentBytes); Assert.Equal(unchanged, input);
    }

    [Fact]
    public void Continuation_coordinate_lists_consume_prior_scalars_and_keep_rotation_repetition()
    {
        SvgTextPositionLists lists = new("1 2 3 4", "10 20 30 40", "0 1 2 3", null, "5 15") { CharacterOffset = 2 };
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(100_000));
        using (OwnedParserArray<SvgTextPositionAdjustment> positions = lists.Build("AA", [], budget, CancellationToken.None))
        {
            Assert.Equal(2, positions.Items.Length);
            Assert.Equal(3, positions.Items[0].X); Assert.Equal(30, positions.Items[0].Y); Assert.Equal(2, positions.Items[0].Dx);
            Assert.Equal(4, positions.Items[1].X); Assert.Equal(40, positions.Items[1].Y); Assert.Equal(3, positions.Items[1].Dx);
            Assert.All(positions.Items, position => Assert.Equal(15, position.Rotation));
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Theory]
    [InlineData("M6 4", "", false)]
    [InlineData("M6 4", "", true)]
    [InlineData("M0 4H8V8H0Z", " startOffset='25%'", false)]
    [InlineData("M0 4H8V8H0Z", " startOffset='25%'", true)]
    public async Task Empty_path_runs_leave_default_and_positioned_source_cursors_unchanged(string data, string attributes, bool positioned)
    {
        // Independent native topology controls establish that an empty path is inert.
        // Retain the original default-origin inputs and add visibly positioned glyphs.
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync([CreateResolution(CreateTriangleFont())], CancellationToken.None);
        string position = positioned ? " x='3' y='4'" : string.Empty;
        string emptyPath = "<textPath href='#p'" + attributes + "/>";
        string source = "<svg xmlns='http://www.w3.org/2000/svg' width='16' height='8'><defs><path id='p' d='" + data + "'/></defs><text font-family='Example' font-size='100'" + position + ">" + emptyPath + "A</text></svg>";
        string reference = source.Replace(emptyPath, string.Empty, StringComparison.Ordinal);
        SvgRenderOptions options = new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, snapshot);
        Assert.Equal(SvgConverter.ConvertToPng(System.Text.Encoding.UTF8.GetBytes(reference), options, CancellationToken.None).ToArray(), SvgConverter.ConvertToPng(System.Text.Encoding.UTF8.GetBytes(source), options, CancellationToken.None).ToArray());
    }
}
