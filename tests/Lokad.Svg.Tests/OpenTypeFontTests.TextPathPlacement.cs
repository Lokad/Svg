using Lokad.Svg.Fonts;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed partial class OpenTypeFontTests
{
    public static TheoryData<string, string, double> GeneralTextPathPoseCases()
    {
        TheoryData<string, string, double> cases = new();
        foreach (string path in new[] { "M4 48H140", "M4 48H24L50 32L100 48H140", "M140 48H4" })
        foreach (string pose in new[] { "scale(1.5)", "scale(2.3)", "matrix(1 .25 .1 1 1 2)", "translate(3 .5)" })
        foreach (double scale in new[] { 1d, 1.5 }) cases.Add(path, pose, scale);
        return cases;
    }

    [Theory]
    [MemberData(nameof(GeneralTextPathPoseCases))]
    public async Task General_text_path_poses_keep_horizontal_and_zero_tangent_glyphs(string path, string pose, double scale)
    {
        byte[] program = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "text-paint-bounds", "sans.ttf"));
        using SvgFontSnapshot fonts = await SvgFontSnapshot.CreateAsync([CreateResolution(program)], CancellationToken.None);
        byte[] input = GeneralTextPathPoseDocument(path, pose, "AVj");
        RenderBudget budget = new(8_000_000);
        using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, fonts, new(1_000_000), CancellationToken.None))
            Assert.Contains(root.Geometries, geometry => geometry.Kind == SvgGeometryKind.GlyphContours);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        Assert.NotEmpty(SvgConverter.ConvertToPng(input, new(null, scale, SvgColor.Transparent, SvgRenderLimits.Default, fonts), CancellationToken.None).ToArray());
    }

    [Theory]
    [InlineData("M4 48H140", "scale(1.5)")]
    [InlineData("M4 48H24L50 32L100 48H140", "matrix(1 .25 .1 1 1 2)")]
    public async Task General_text_path_pose_denial_and_cancellation_release_owners(string path, string pose)
    {
        using SvgFontSnapshot fonts = await SvgFontSnapshot.CreateAsync([CreateResolution(CreateTriangleFont())], CancellationToken.None);
        byte[] input = GeneralTextPathPoseDocument(path, pose, "A");
        RenderBudget budget = new(2_000_000);
        ParserWorkCounter work = new(100_000);
        using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, fonts, work, CancellationToken.None)) { }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        for (long limit = 0; limit < work.Used; limit++)
        {
            RenderBudget denied = new(2_000_000);
            Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, denied, fonts, new(limit), CancellationToken.None)).ErrorCode);
            Assert.Equal(0, denied.Snapshot().CurrentBytes);
        }
        RenderBudget memoryDenied = new(256);
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, memoryDenied, fonts, new(100_000), CancellationToken.None)).ErrorCode);
        Assert.Equal(0, memoryDenied.Snapshot().CurrentBytes);
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        Assert.Equal(cancellation.Token, Assert.ThrowsAny<OperationCanceledException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, fonts, new(100_000), cancellation.Token)).CancellationToken);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, fonts, new(100_000), CancellationToken.None)) { }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    private static byte[] GeneralTextPathPoseDocument(string path, string pose, string text)
        => System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='360' height='180'><defs><path id='b' d='" + path + "'/></defs><text font-family='Example' font-size='24' fill='#804020' letter-spacing='.1' transform='" + pose + "'><textPath href='#b'>" + text + "</textPath></text></svg>");
}
