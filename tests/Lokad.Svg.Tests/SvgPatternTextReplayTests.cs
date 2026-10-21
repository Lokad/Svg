using System.Security.Cryptography;
using System.Text;
using Lokad.Svg.Fonts;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgPatternTextReplayTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Unused_pattern_text_never_replaces_root_drawing(bool named)
    {
        using SvgFontSnapshot fonts = await CreateFonts();
        const string blue = "<rect width='16' height='8' fill='blue'/>";
        string pattern = "<defs>" + Pattern(named ? "p" : "", Text(true, "")) + Resources + "</defs>";
        Assert.Equal(Render(Input(blue), fonts), Render(Input(pattern + blue), fonts));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Path_text_repeats_with_its_tile_despite_root_and_tile_prefixes(bool rootPrefix, bool tilePrefix)
    {
        using SvgFontSnapshot fonts = await CreateFonts();
        const string blue = "<rect width='16' height='8' fill='blue'/>";
        string prefix = tilePrefix ? "<rect width='8' height='8' fill='blue'/>" : "";
        string output = (rootPrefix ? blue : "") + "<rect width='16' height='8' fill='url(#p)'/>";
        Assert.Equal(Render(Build(false), fonts), Render(Build(true), fonts));

        byte[] Build(bool path) => Input("<defs>" + Pattern("p", prefix + Text(path, "")) + Resources + "</defs>" + output);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" clip-path='url(#c)' mask='url(#m)'")]
    [InlineData(" filter='url(#f)' mask='url(#m)'")]
    public async Task Interleaved_text_uses_and_nested_scopes_keep_their_tile_order(string effects)
    {
        using SvgFontSnapshot fonts = await CreateFonts();
        Assert.Equal(Render(Mixed(false, effects), fonts), Render(Mixed(true, effects), fonts));
    }

    [Fact]
    public async Task Root_and_multiple_tile_scenes_resolve_only_their_own_forward_path_runs()
    {
        using SvgFontSnapshot fonts = await CreateFonts();
        Assert.Equal(Render(Build(false), fonts), Render(Build(true), fonts));

        static byte[] Build(bool path)
            => Input(Text(path, "") + "<defs>" + Pattern("p", Text(path, "")) + "</defs>"
                + "<rect width='8' height='8' fill='url(#p)'/>" + Text(path, " fill='blue'")
                + "<defs>" + Pattern("q", "<rect width='8' height='8' fill='green'/>" + Text(path, "")) + Resources + "</defs>"
                + "<rect x='8' width='8' height='8' fill='url(#q)'/>");
    }

    [Theory]
    [InlineData("continuation")]
    [InlineData("gradient")]
    public async Task Deferred_continuations_and_direct_shader_text_stay_in_the_tile(string kind)
    {
        using SvgFontSnapshot fonts = await CreateFonts();
        string actual = kind == "gradient" ? Text(false, " fill='url(#g)'")
            : "<text font-family='Example' font-size='8'><textPath href='#short'>A</textPath>A</text>";
        string expected = kind == "gradient" ? Text(false, " fill='red'")
            : "<text font-family='Example' font-size='8' y='6'>A</text><text font-family='Example' font-size='8' x='4' y='6'>A</text>";
        Assert.Equal(Render(Build(expected), fonts), Render(Build(actual), fonts));

        static byte[] Build(string text) => Input("<defs>" + Pattern("p", text) + Resources + "</defs><rect width='16' height='8' fill='url(#p)'/>");
    }

    [Fact]
    public async Task Tile_text_uses_keep_their_private_replay_separate_from_queued_siblings()
    {
        using SvgFontSnapshot fonts = await CreateFonts();
        Assert.Equal(Render(Build(false), fonts), Render(Build(true), fonts));

        static byte[] Build(bool path)
            => Input("<defs><text id='t' font-family='Example' font-size='8'" + (path ? "><textPath href='#base'>AA</textPath>" : " y='6'>AA")
                + "</text>" + Pattern("p", Text(path, "") + "<use href='#t' x='1'/>" + Text(path, " fill='blue'"))
                + Resources + "</defs><rect width='16' height='8' fill='url(#p)'/>");
    }

    [Fact]
    public async Task Unresolved_tile_path_keeps_the_public_error_category()
    {
        using SvgFontSnapshot fonts = await CreateFonts();
        byte[] input = Input("<defs>" + Pattern("p", Text(true, "")) + "</defs><rect width='16' height='8' fill='url(#p)'/>");
        Assert.Equal(SvgErrorCode.UnresolvedResource, Assert.Throws<SvgException>(() => Render(input, fonts)).ErrorCode);
    }

    [Fact]
    public async Task Forward_tile_paints_expand_in_order_with_text_paths_and_private_text_uses()
    {
        using SvgFontSnapshot fonts = await CreateFonts();
        Assert.Equal(Render(Build(false), fonts), Render(Build(true), fonts));

        static byte[] Build(bool forward)
        {
            const string gradient = "<linearGradient id='paint'><stop stop-color='red'/><stop offset='1' stop-color='blue'/></linearGradient>";
            string pattern = Pattern("p", "<rect width='8' height='8' fill='url(#paint)'/>" + Text(true, "")
                + "<g clip-path='url(#c)' mask='url(#m)'><rect width='4' height='4' fill='url(#paint)'/><use href='#t'/>" + Text(true, " fill='blue'") + "</g><rect x='6' width='2' height='8' fill='url(#paint)'/>");
            return Input("<defs><text id='t' font-family='Example' font-size='8'><textPath href='#base'>AA</textPath></text>"
                + (forward ? pattern + gradient : gradient + pattern) + Resources + "</defs><rect width='16' height='8' fill='url(#p)'/>");
        }
    }

    [Theory]
    [InlineData(128)]
    [InlineData(512)]
    public async Task Separate_tile_text_scenes_do_not_rescan_unrelated_ranges(int count)
    {
        using SvgFontSnapshot fonts = await CreateFonts();
        StringBuilder xml = new();
        for (int index = 0; index < count; index++) xml.Append("<g clip-path='url(#c)'><rect width='1' height='1'/></g>");
        xml.Append("<defs>");
        for (int index = 0; index < count; index++) xml.Append(Pattern("p" + index, Text(true, "")));
        xml.Append(Resources).Append("</defs><rect width='16' height='8' fill='url(#p0)'/>");
        RenderBudget budget = new(40_000_000);
        ParserWorkCounter work = new(count * 900 + 3000);
        using (SvgRoot root = SvgRootParser.Parse(Input(xml.ToString()), SvgRenderLimits.Default, budget, fonts, work, CancellationToken.None)) { }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Deferred_tile_text_releases_owners_on_work_memory_denial_and_cancellation(bool named)
    {
        using SvgFontSnapshot fonts = await CreateFonts();
        string content = "<text font-family='Example' font-size='8' mask='url(#m)' clip-path='url(#c)'><textPath href='#short'>A</textPath>A</text>";
        byte[] input = Input("<defs>" + Pattern(named ? "p" : "", content) + Resources + "</defs><rect width='16' height='8' fill='" + (named ? "url(#p)" : "blue") + "'/>");
        RenderBudget measured = new(2_000_000);
        ParserWorkCounter work = new(100_000);
        using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, measured, fonts, work, CancellationToken.None)) { }
        Assert.Equal(0, measured.Snapshot().CurrentBytes);
        for (long limit = 0; limit < work.Used; limit++)
        {
            RenderBudget denied = new(2_000_000);
            Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, denied, fonts, new(limit), CancellationToken.None)).ErrorCode);
            Assert.Equal(0, denied.Snapshot().CurrentBytes);
        }
        long peak = measured.Snapshot().PeakBytes;
        for (long limit = 1; limit < peak; limit += 4096) DenyMemory(limit);
        DenyMemory(peak - 1);
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        RenderBudget cancelled = new(2_000_000);
        Assert.Equal(cancellation.Token, Assert.ThrowsAny<OperationCanceledException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, cancelled, fonts, new(100_000), cancellation.Token)).CancellationToken);
        Assert.Equal(0, cancelled.Snapshot().CurrentBytes);

        void DenyMemory(long limit)
        {
            RenderBudget denied = new(limit);
            Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, denied, fonts, new(100_000), CancellationToken.None)).ErrorCode);
            Assert.Equal(0, denied.Snapshot().CurrentBytes);
        }
    }

    private static byte[] Mixed(bool path, string effects)
        => Input("<defs>" + Pattern("p", "<use href='#r' x='6'/><g opacity='.7' mask='url(#m)'><g clip-path='url(#c)'>"
            + Text(path, effects) + "<use href='#r' x='4'/></g><g clip-path='url(#c)'>" + Text(path, " fill='blue'")
            + "<use href='#r' x='2'/></g></g><use href='#r'/>" + Text(path, ""))
            + Resources + "</defs><rect width='16' height='8' fill='url(#p)'/>");

    private static string Text(bool path, string effects)
        => "<text font-family='Example' font-size='8'" + effects + (path ? "><textPath href='#base'>AA</textPath>" : " y='6'>AA") + "</text>";

    private static string Pattern(string id, string content)
        => "<pattern " + (id.Length == 0 ? "" : "id='" + id + "' ") + "width='8' height='8' patternUnits='userSpaceOnUse'>" + content + "</pattern>";

    private const string Resources = "<path id='base' d='M0 6H40'/><path id='short' d='M0 6H4'/><rect id='r' width='1' height='1' fill='green'/><clipPath id='c'><rect width='6' height='8'/></clipPath><mask id='m'><rect width='8' height='8' fill='white'/></mask><filter id='f'><feOffset dx='0' dy='0'/></filter><linearGradient id='g'><stop stop-color='red'/><stop offset='1' stop-color='red'/></linearGradient>";

    private static byte[] Input(string content) => System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='8'>" + content + "</svg>");

    private static byte[] Render(byte[] input, SvgFontSnapshot fonts)
        => SvgConverter.ConvertToPng(input, new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, fonts), CancellationToken.None).ToArray();

    private static async ValueTask<SvgFontSnapshot> CreateFonts()
    {
        byte[] bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "kerning-precedence", "PlainKern.ttf"));
        string hash = Convert.ToHexString(SHA256.HashData(bytes));
        string manifest = "{\"packId\":\"tile-text\",\"files\":[{\"relativePath\":\"font.ttf\",\"byteSize\":" + bytes.Length + ",\"sha256\":\"" + hash + "\"}],\"families\":[{\"requestedFamily\":\"Example\",\"resolvedFamily\":\"Example\",\"relativeFontFile\":\"font.ttf\",\"weight\":400}]}";
        SvgFontPackCatalog catalog = SvgFontPackCatalog.Parse(System.Text.Encoding.UTF8.GetBytes(manifest), "tile-text");
        SvgFontProgramResolution resolution = catalog.Bind(catalog.Resolve(new("Example", 400, false)), new SvgMemoryFontProgramSource("tile-text", bytes));
        return await SvgFontSnapshot.CreateAsync([resolution], CancellationToken.None);
    }
}
