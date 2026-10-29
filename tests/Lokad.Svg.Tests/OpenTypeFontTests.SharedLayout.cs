using System.Text;
using Lokad.Svg.Fonts;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed partial class OpenTypeFontTests
{
    public enum SharedFilterScope { Clip, Mask, Both }

    [Fact]
    [Trait("Layer", "Contract")]
    public async Task Shared_glyph_view_disposal_preserves_parent_storage_and_offset_metadata()
    {
        using SvgFontSnapshot fonts = await SvgFontSnapshot.CreateAsync([CreateResolution(CreateTriangleFont())], CancellationToken.None);
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(1_000_000));
        SvgTextStyle style = new(new("Example", 400, false), 100, 0, 3, 0, 0, 0, SvgTextAnchor.Start, "");
        SvgTextGlyphPaint red = new(new(new(255, 0, 0, 255), 1), true);
        SvgTextGlyphPaint blue = new(new(new(0, 0, 255, 255), 1), true);
        using (SvgTextRun parent = SvgTextRunBuilder.Create(style, "AAA", [], [new(1, null, null, 0, 0) { Rotation = 20 }],
            [new(0, 1, red), new(1, 3, blue)], red, null, fonts, budget, CancellationToken.None))
        {
            long retained = budget.Snapshot().CurrentBytes;
            using (budget.Reserve(512, SvgMemoryCategory.Parser))
            {
                OwnedParserArray<SvgTextPaintFragment> fragments = new([new(0, 1, parent.Glyphs[1].Advance)],
                    budget.Reserve(ParserCollectionMemory.GetArrayByteCount<SvgTextPaintFragment>(1), SvgMemoryCategory.Geometry));
                using SvgTextRun view = new(parent, 1, 1, fragments, style, null, CancellationToken.None);
                Assert.Equal(parent.Glyphs[1], Assert.Single(view.Glyphs.ToArray()));
                Assert.Equal(parent.GetGlyphPaint(1, SvgColor.Transparent), view.GetGlyphPaint(0, SvgColor.Transparent));
                Assert.Equal(20, view.GetGlyphRotation(0));
                view.Dispose();
                view.Dispose();
            }
            Assert.Equal(retained, budget.Snapshot().CurrentBytes);
            Assert.Equal(3, parent.Glyphs.Length);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    [Trait("Layer", "Mechanism")]
    public async Task Repeated_own_filters_remap_only_their_source_command_ranges()
    {
        using SvgFontSnapshot fonts = await SvgFontSnapshot.CreateAsync([CreateResolution(CreateTriangleFont())], CancellationToken.None);
        long small = Measure(128), large = Measure(512);
        Assert.True(large < small * 5, $"128 leaves: {small} work; 512 leaves: {large} work.");

        long Measure(int count)
        {
            StringBuilder xml = new("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='6'><defs><path id='p' d='M0 3H8'/><filter id='f' filterUnits='userSpaceOnUse' x='0' y='0' width='16' height='6'><feOffset dx='0' dy='0'/></filter></defs>");
            for (int index = 0; index < count; index++)
                xml.Append("<text font-family='Example' font-size='100' filter='url(#f)' y='3'>A<textPath href='#p'>A</textPath>A</text>");
            xml.Append("</svg>");
            RenderBudget budget = new RenderBudget(new HostMemoryBudget(20_000_000));
            long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            using (SvgRoot root = SvgRootParser.Parse(System.Text.Encoding.UTF8.GetBytes(xml.ToString()), SvgRenderLimits.Default, budget, fonts, CancellationToken.None)) { }
            Assert.Equal(0, budget.Snapshot().CurrentBytes);
            return GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        }
    }

    [Theory]
    [InlineData(SharedFilterScope.Clip)]
    [InlineData(SharedFilterScope.Mask)]
    [InlineData(SharedFilterScope.Both)]
    [Trait("Layer", "Contract")]
    public async Task Own_filtered_shared_text_retains_output_scopes_and_neighboring_pending_uses(SharedFilterScope scopes)
    {
        using SvgFontSnapshot fonts = await SvgFontSnapshot.CreateAsync([CreateResolution(CreateTriangleFont())], CancellationToken.None);
        (byte[] input, byte[] control) = SharedFilterDocuments(scopes);
        SvgRenderOptions options = new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, fonts);
        Assert.Equal(SvgConverter.ConvertToPng(control, options, CancellationToken.None).ToArray(),
            SvgConverter.ConvertToPng(input, options, CancellationToken.None).ToArray());
    }

    [Fact]
    [Trait("Layer", "Contract")]
    public async Task Shared_glyph_views_release_owners_after_denial_cancellation_and_retry()
    {
        using SvgFontSnapshot fonts = await SvgFontSnapshot.CreateAsync([CreateResolution(CreateTriangleFont())], CancellationToken.None);
        (byte[] input, _) = SharedFilterDocuments(SharedFilterScope.Both);
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(2_000_000));
        using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, fonts, CancellationToken.None)) { }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        foreach (long limit in new[] { 256L, budget.Snapshot().PeakBytes / 2, budget.Snapshot().PeakBytes - 1 })
        {
            RenderBudget denied = new RenderBudget(new HostMemoryBudget(limit));
            Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() =>
                SvgRootParser.Parse(input, SvgRenderLimits.Default, denied, fonts, CancellationToken.None)).ErrorCode);
            Assert.Equal(0, denied.Snapshot().CurrentBytes);
        }
        using CancellationTokenSource cancellation = new();
        bool transformed = false;
        using (GlyphOutlineProbe.Arm(() => { transformed = true; cancellation.Cancel(); }))
        {
            Assert.Equal(cancellation.Token, Assert.ThrowsAny<OperationCanceledException>(() =>
                SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, fonts, cancellation.Token)).CancellationToken);
        }
        Assert.True(transformed);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        using RenderBudget.BudgetReservation held = budget.Reserve(73, SvgMemoryCategory.Scene);
        using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, fonts, CancellationToken.None)) { }
        Assert.Equal(73, budget.Snapshot().CurrentBytes);
    }

    private static (byte[] Input, byte[] Control) SharedFilterDocuments(SharedFilterScope scopes)
    {
        string attributes = (scopes is SharedFilterScope.Clip or SharedFilterScope.Both ? " clip-path='url(#c)'" : "")
            + (scopes is SharedFilterScope.Mask or SharedFilterScope.Both ? " mask='url(#m)'" : "");
        const string text = "<text font-family='Example' font-size='100' y='3' fill='url(#g)'>A<textPath href='#p'>A</textPath>A</text>";
        string body = text.Replace("<text ", "<text filter='url(#f)'" + attributes + " ", StringComparison.Ordinal);
        string control = "<g" + attributes + "><g filter='url(#f)'>" + text + "</g></g>";
        const string prefix = "<svg xmlns='http://www.w3.org/2000/svg' width='16' height='6'><use href='#u'/>";
        const string suffix = "<rect x='13' y='4' width='1' height='1' fill='blue'/><defs><path id='p' d='M0 3H8'/><rect id='u' x='14' y='4' width='1' height='1' fill='red'/><filter id='f' filterUnits='userSpaceOnUse' x='0' y='0' width='16' height='6'><feOffset dx='1' dy='1'/></filter><clipPath id='c'><rect x='1' width='10' height='6'/></clipPath><mask id='m' mask-type='alpha' maskUnits='userSpaceOnUse' x='0' y='0' width='16' height='6'><rect width='14' height='6' fill='white' fill-opacity='.5'/></mask><linearGradient id='g'><stop stop-color='red'/><stop offset='1' stop-color='blue'/></linearGradient></defs></svg>";
        return (System.Text.Encoding.UTF8.GetBytes(prefix + body + suffix), System.Text.Encoding.UTF8.GetBytes(prefix + control + suffix));
    }
}
