using System.Security.Cryptography;
using System.Text;
using Lokad.Svg.Fonts;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgTextReplayRangeTests
{
    [Theory]
    [InlineData(128, "clip")]
    [InlineData(256, "clip")]
    [InlineData(512, "clip")]
    [InlineData(1024, "clip")]
    [InlineData(1024, "mask")]
    [InlineData(1024, "text")]
    public async Task Retained_text_work_does_not_scan_unrelated_document_ranges(int count, string kind)
    {
        using SvgFontSnapshot fonts = await CreateFonts();
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(40_000_000));
        using (SvgRoot root = SvgRootParser.Parse(Input(count, true, kind), SvgRenderLimits.Default, budget, fonts, CancellationToken.None)) { }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public async Task Ten_thousand_clipped_groups_do_not_change_text_use_order_or_default_limit_outcome()
    {
        using SvgFontSnapshot fonts = await CreateFonts();
        SvgRenderOptions options = new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, fonts);
        byte[] expected = SvgConverter.ConvertToPng(Input(10_000, false, "clip"), options, CancellationToken.None).ToArray();
        byte[] actual = SvgConverter.ConvertToPng(Input(10_000, true, "clip"), options, CancellationToken.None).ToArray();
        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task Local_replay_releases_storage_on_memory_denial_and_cancellation()
    {
        using SvgFontSnapshot fonts = await CreateFonts();
        byte[] input = Input(2, true, "clip");
        RenderBudget measured = new RenderBudget(new HostMemoryBudget(2_000_000));
        using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, measured, fonts, CancellationToken.None)) { }
        Assert.Equal(0, measured.Snapshot().CurrentBytes);
        long peak = measured.Snapshot().PeakBytes;
        for (long limit = 1; limit < peak; limit += 4096) DenyMemory(limit);
        DenyMemory(peak - 1);
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        RenderBudget cancelled = new RenderBudget(new HostMemoryBudget(2_000_000));
        Assert.Equal(cancellation.Token, Assert.ThrowsAny<OperationCanceledException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, cancelled, fonts, cancellation.Token)).CancellationToken);
        Assert.Equal(0, cancelled.Snapshot().CurrentBytes);

        void DenyMemory(long limit)
        {
            RenderBudget denied = new RenderBudget(new HostMemoryBudget(limit));
            Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, denied, fonts, CancellationToken.None)).ErrorCode);
            Assert.Equal(0, denied.Snapshot().CurrentBytes);
        }
    }

    private static async ValueTask<SvgFontSnapshot> CreateFonts()
    {
        byte[] bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "kerning-precedence", "PlainKern.ttf"));
        string hash = Convert.ToHexString(SHA256.HashData(bytes));
        string manifest = "{\"packId\":\"replay\",\"files\":[{\"relativePath\":\"font.ttf\",\"byteSize\":" + bytes.Length + ",\"sha256\":\"" + hash + "\"}],\"families\":[{\"requestedFamily\":\"Example\",\"resolvedFamily\":\"Example\",\"relativeFontFile\":\"font.ttf\",\"weight\":400}]}";
        SvgFontPackCatalog catalog = SvgFontPackCatalog.Parse(System.Text.Encoding.UTF8.GetBytes(manifest), "replay");
        SvgFontProgramResolution resolution = catalog.Bind(catalog.Resolve(new("Example", 400, false)), new SvgMemoryFontProgramSource("replay", bytes));
        return await SvgFontSnapshot.CreateAsync([resolution], CancellationToken.None);
    }

    private static byte[] Input(int count, bool rangesFirst, string kind)
    {
        StringBuilder xml = new("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><path id='p' d='M0 6H40'/><text id='t' font-family='Example' font-size='8'><textPath href='#p'>AA</textPath></text></defs>");
        if (rangesFirst) AppendRanges();
        for (int index = 0; index < count; index++) xml.Append("<use href='#t' x='100'/>");
        if (!rangesFirst) AppendRanges();
        xml.Append("<defs><clipPath id='c'><rect width='8' height='8'/></clipPath><mask id='m'><rect width='8' height='8' fill='white'/></mask></defs></svg>");
        return System.Text.Encoding.UTF8.GetBytes(xml.ToString());

        void AppendRanges()
        {
            string content = kind switch
            {
                "clip" => "<g clip-path='url(#c)'><rect width='1' height='1' fill='red'/></g>",
                "mask" => "<g mask='url(#m)'><rect width='1' height='1' fill='red'/></g>",
                "text" => "<text x='100' font-family='Example' font-size='8'><textPath href='#p'>AA</textPath></text>",
                _ => throw new ArgumentOutOfRangeException(nameof(kind))
            };
            for (int index = 0; index < count; index++) xml.Append(content);
        }
    }
}
