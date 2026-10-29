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
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(40_000_000));
        using (SvgRoot root = SvgRootParser.Parse(Input(xml.ToString()), SvgRenderLimits.Default, budget, fonts, CancellationToken.None)) { }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Deferred_tile_text_releases_owners_on_memory_denial_and_cancellation(bool named)
    {
        using SvgFontSnapshot fonts = await CreateFonts();
        string content = "<text font-family='Example' font-size='8' mask='url(#m)' clip-path='url(#c)'><textPath href='#short'>A</textPath>A</text>";
        byte[] input = Input("<defs>" + Pattern(named ? "p" : "", content) + Resources + "</defs><rect width='16' height='8' fill='" + (named ? "url(#p)" : "blue") + "'/>");
        RenderBudget measured = new RenderBudget(new HostMemoryBudget(2_000_000));
        using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, measured, fonts, CancellationToken.None)) { }
        Assert.Equal(0, measured.Snapshot().CurrentBytes);
        long peak = measured.Snapshot().PeakBytes;
        for (long limit = 1; limit < peak; limit += 4096) CheckMemoryLimit(limit);
        CheckMemoryLimit(peak - 1);
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        RenderBudget cancelled = new RenderBudget(new HostMemoryBudget(2_000_000));
        Assert.Equal(cancellation.Token, Assert.ThrowsAny<OperationCanceledException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, cancelled, fonts, cancellation.Token)).CancellationToken);
        Assert.Equal(0, cancelled.Snapshot().CurrentBytes);

        void CheckMemoryLimit(long limit)
        {
            RenderBudget bounded = new RenderBudget(new HostMemoryBudget(limit));
            SvgRoot parsed;
            try { parsed = SvgRootParser.Parse(input, SvgRenderLimits.Default, bounded, fonts, CancellationToken.None); }
            catch (SvgException failure)
            {
                Assert.Equal(SvgErrorCode.LimitExceeded, failure.ErrorCode);
                Assert.Equal(0, bounded.Snapshot().CurrentBytes);
                return;
            }
            // Optional scan preparation may yield below the ample-budget peak.
            // A successful parse must retain the same pixels and release owners.
            RenderBudget raster = new RenderBudget(new HostMemoryBudget(2_000_000));
            using (parsed)
                Assert.Equal(Render(input, fonts), SvgConverter.ConvertToPng(parsed, new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, fonts), raster, CancellationToken.None).ToArray());
            Assert.Equal(0, raster.Snapshot().CurrentBytes);
            Assert.Equal(0, bounded.Snapshot().CurrentBytes);
        }
    }

    [Theory]
    [InlineData("fill", false)]
    [InlineData("fill", true)]
    [InlineData("stroke", false)]
    [InlineData("stroke", true)]
    [InlineData("group", false)]
    [InlineData("alias", true)]
    public async Task Recursive_pattern_text_uses_fail_during_parsing(string kind, bool path)
    {
        using SvgFontSnapshot fonts = await CreateFonts();
        string paint = kind == "stroke" ? " fill='none' stroke='url(#p)' stroke-width='.5'" : " fill='url(#p)'";
        string text = Text(path, paint);
        string source = kind == "group" ? "<g id='t'>" + text + "</g>"
            : text.Replace("<text ", "<text id='t' ", StringComparison.Ordinal);
        if (kind == "alias") source += "<use id='alias' href='#t'/>";
        byte[] input = Input("<defs>" + source + Pattern("p", "<use href='#" + (kind == "alias" ? "alias" : "t") + "'/>") + Resources + "</defs><rect width='16' height='8' fill='url(#p)'/>");
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(2_000_000));
        // Parsing must reject the dependency before attempting public rasterization.
        Assert.Equal(SvgErrorCode.UnsupportedFeature, Assert.Throws<SvgException>(() =>
        {
            using SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, fonts, CancellationToken.None);
        }).ErrorCode);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        Assert.Equal(SvgErrorCode.UnsupportedFeature, Assert.Throws<SvgException>(() => Render(input, fonts)).ErrorCode);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Nested_pattern_text_rejection_is_independent_of_definition_order(bool path, bool forward)
    {
        using SvgFontSnapshot fonts = await CreateFonts();
        string outer = Pattern("p", Text(path, " fill='url(#q)'"));
        string inner = Pattern("q", "<rect width='8' height='8' fill='red'/>");
        byte[] input = Input("<defs>" + (forward ? outer + inner : inner + outer) + Resources + "</defs><rect width='16' height='8' fill='url(#p)'/>");
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(2_000_000));
        Assert.Equal(SvgErrorCode.UnsupportedFeature, Assert.Throws<SvgException>(() =>
        {
            using SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, fonts, CancellationToken.None);
        }).ErrorCode);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        Assert.Equal(SvgErrorCode.UnsupportedFeature, Assert.Throws<SvgException>(() => Render(input, fonts)).ErrorCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Root_text_uses_keep_ordinary_pattern_paints(bool path)
    {
        using SvgFontSnapshot fonts = await CreateFonts();
        Assert.Equal(Render(Build(false), fonts), Render(Build(true), fonts));

        byte[] Build(bool use)
        {
            string text = Text(path, " fill='url(#q)'");
            return Input("<defs>" + Pattern("q", "<rect width='8' height='8' fill='red'/>")
                + (use ? text.Replace("<text ", "<text id='t' ", StringComparison.Ordinal) : "")
                + Resources + "</defs>" + (use ? "<use href='#t'/>" : text));
        }
    }

    [Fact]
    public async Task Rejected_pattern_text_uses_release_owners_on_denial_and_cancellation()
    {
        using SvgFontSnapshot fonts = await CreateFonts();
        byte[] input = Input("<defs>" + Text(true, " fill='url(#p)'").Replace("<text ", "<text id='t' ", StringComparison.Ordinal)
            + Pattern("p", "<use href='#t'/>") + Resources + "</defs><rect width='16' height='8' fill='url(#p)'/>");
        RenderBudget measured = new RenderBudget(new HostMemoryBudget(2_000_000));
        SvgException rejection = Assert.Throws<SvgException>(() =>
        {
            using SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, measured, fonts, CancellationToken.None);
        });
        Assert.Equal(SvgErrorCode.UnsupportedFeature, rejection.ErrorCode);
        Assert.Equal(0, measured.Snapshot().CurrentBytes);
        long peak = measured.Snapshot().PeakBytes;
        for (long limit = 1; limit < peak; limit += 4096) CheckMemoryLimit(limit);
        CheckMemoryLimit(peak - 1);
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        RenderBudget cancelled = new RenderBudget(new HostMemoryBudget(2_000_000));
        Assert.Equal(cancellation.Token, Assert.ThrowsAny<OperationCanceledException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, cancelled, fonts, cancellation.Token)).CancellationToken);
        Assert.Equal(0, cancelled.Snapshot().CurrentBytes);

        void CheckMemoryLimit(long limit)
        {
            RenderBudget denied = new RenderBudget(new HostMemoryBudget(limit));
            SvgException failure = Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, denied, fonts, CancellationToken.None));
            // Optional metric scans may yield to the unprepared route, allowing
            // the same source rejection before the memory allowance is exhausted.
            if (failure.ErrorCode != SvgErrorCode.LimitExceeded)
            {
                Assert.Equal(rejection.ErrorCode, failure.ErrorCode);
                Assert.Equal(rejection.Message, failure.Message);
            }
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
