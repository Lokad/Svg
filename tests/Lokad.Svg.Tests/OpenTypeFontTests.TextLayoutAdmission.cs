using System.Text;
using Lokad.Svg.Fonts;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;
using Lokad.Svg.Testing;

namespace Lokad.Svg.Tests;

public sealed partial class OpenTypeFontTests
{
    public enum UnsupportedTextFixture { Bidirectional, Syllabic }
    public enum TextAdmissionScope { Direct, Symbol, Pattern }

    [Theory]
    [Trait("Layer", "Mechanism")]
    [InlineData(0x0041, 0)]
    [InlineData(0x00AD, 0)]
    [InlineData(0x0301, 0)]
    [InlineData(0x034F, 0)]
    [InlineData(0x03A3, 0)]
    [InlineData(0x0416, 0)]
    [InlineData(0x058F, 0)]
    [InlineData(0x0590, 512)]
    [InlineData(0x05B0, 0)]
    [InlineData(0x05BE, 512)]
    [InlineData(0x05D0, 512)]
    [InlineData(0x061C, 1536)]
    [InlineData(0x0627, 1536)]
    [InlineData(0x0660, 1024)]
    [InlineData(0x0915, 1024)]
    [InlineData(0x093F, 1024)]
    [InlineData(0x1100, 1024)]
    [InlineData(0x1820, 1024)]
    [InlineData(0x200B, 0)]
    [InlineData(0x200C, 0)]
    [InlineData(0x200D, 0)]
    [InlineData(0x200E, 512)]
    [InlineData(0x200F, 512)]
    [InlineData(0x202A, 512)]
    [InlineData(0x2066, 512)]
    [InlineData(0xAC00, 1024)]
    [InlineData(0xFE0F, 0)]
    [InlineData(0x10400, 0)]
    [InlineData(0x10800, 512)]
    [InlineData(0x1D400, 0)]
    [InlineData(0x1E900, 1536)]
    [InlineData(0x1F600, 0)]
    [InlineData(0x10FFFF, 0)]
    public void Layout_requirements_keep_direction_script_and_mark_boundaries_distinct(int scalar, int expected)
    {
        Rune value = new(scalar);
        Assert.Equal((SvgTextLayoutRequirement)expected, SvgCanonicalText.GetLayoutRequirements(value));
        if (expected == 0) SvgCanonicalText.ValidateSimpleLayout(value);
        else Assert.Equal(SvgErrorCode.UnsupportedFeature, Assert.Throws<SvgException>(() => SvgCanonicalText.ValidateSimpleLayout(value)).ErrorCode);
    }

    public static IEnumerable<object[]> PreparedTextAdmissionCases()
    {
        foreach (UnsupportedTextFixture fixture in Enum.GetValues<UnsupportedTextFixture>())
        foreach (TextAdmissionScope scope in Enum.GetValues<TextAdmissionScope>())
            yield return [fixture, scope];
    }

    [Theory]
    [Trait("Layer", "Contract")]
    [MemberData(nameof(PreparedTextAdmissionCases))]
    public async Task Unsupported_layout_rejects_before_prepared_acquisition_and_reuses_the_allowance(
        UnsupportedTextFixture fixture, TextAdmissionScope scope)
    {
        (var catalog, var provider, var source) = CreateExamplePack(LayoutAdmissionFont(fixture));
        using TestMemoryBudget allowance = new(1_000_000);
        SvgRenderOptions options = SvgRenderOptions.Default.WithMemoryBudget(allowance);
        byte[] input = LayoutAdmissionInput(LayoutAdmissionText(fixture), scope);
        SvgException failure = await Assert.ThrowsAsync<SvgException>(async () =>
        {
            using SvgPreparedDocument document = await SvgPreparedDocument.CreateAsync(input, catalog, provider, options, CancellationToken.None);
        });
        Assert.Equal(SvgErrorCode.UnsupportedFeature, failure.ErrorCode);
        Assert.Equal("label", failure.SourceDiagnostic?.ElementId);
        Assert.Equal(0, provider.RequestCount);
        Assert.Equal(0, source.RequestCount);
        Assert.Equal(0, allowance.Reserved);
        using (SvgPreparedDocument retry = await SvgPreparedDocument.CreateAsync(LayoutAdmissionInput("AV", scope), catalog, provider, options, CancellationToken.None))
        {
            using MemoryStream output = new();
            retry.ConvertToPng(output, CancellationToken.None);
            Assert.True(output.Length > 0);
        }
        Assert.Equal(1, provider.RequestCount);
        Assert.Equal(1, source.RequestCount);
        Assert.Equal(0, allowance.Reserved);
    }

    [Theory]
    [Trait("Layer", "Contract")]
    [InlineData(UnsupportedTextFixture.Bidirectional)]
    [InlineData(UnsupportedTextFixture.Syllabic)]
    public async Task Unsupported_layout_keeps_raw_stream_empty_fonts_borrowed_and_retry_usable(UnsupportedTextFixture fixture)
    {
        byte[] program = LayoutAdmissionFont(fixture), unchanged = program.ToArray();
        using SvgFontSnapshot fonts = await SvgFontSnapshot.CreateAsync([CreateResolution(program)], CancellationToken.None);
        using TestMemoryBudget allowance = new(1_000_000);
        SvgRenderOptions options = new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, fonts);
        options = options.WithMemoryBudget(allowance);
        using MemoryStream output = new();
        SvgException failure = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(
            LayoutAdmissionInput(LayoutAdmissionText(fixture), TextAdmissionScope.Direct), output, options, CancellationToken.None));
        Assert.Equal(SvgErrorCode.UnsupportedFeature, failure.ErrorCode);
        Assert.Equal("label", failure.SourceDiagnostic?.ElementId);
        Assert.Equal(0, output.Length);
        Assert.True(output.CanWrite);
        Assert.Equal(0, allowance.Reserved);
        SvgConverter.ConvertToPng(LayoutAdmissionInput("AV", TextAdmissionScope.Direct), output, options, CancellationToken.None);
        Assert.True(output.Length > 0);
        Assert.True(output.CanWrite);
        Assert.Equal(unchanged, program);
        Assert.Equal(0, allowance.Reserved);
    }

    [Fact]
    [Trait("Layer", "Contract")]
    public async Task Coverage_catalog_rejects_unsupported_layout_before_source_acquisition()
    {
        byte[] program = LayoutAdmissionFont(UnsupportedTextFixture.Syllabic);
        string hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(program));
        byte[] manifest = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new
        {
            packId = "two-files",
            files = new[]
            {
                new { relativePath = "first.ttf", byteSize = program.Length, sha256 = hash },
                new { relativePath = "unused.ttf", byteSize = program.Length, sha256 = hash }
            },
            families = new[] { new { requestedFamily = "Example", resolvedFamily = "Example", relativeFontFile = "first.ttf" } }
        });
        SvgFontPackCatalog catalog = SvgFontPackCatalog.Parse(manifest, "two-files");
        CountingFontSource source = new(program);
        CountingFontProvider provider = new(source);
        SvgException failure = await Assert.ThrowsAsync<SvgException>(async () =>
        {
            using SvgPreparedDocument document = await SvgPreparedDocument.CreateAsync(
                LayoutAdmissionInput(LayoutAdmissionText(UnsupportedTextFixture.Syllabic), TextAdmissionScope.Direct),
                catalog, provider, SvgRenderOptions.Default, CancellationToken.None);
        });
        Assert.Equal(SvgErrorCode.UnsupportedFeature, failure.ErrorCode);
        Assert.Equal("label", failure.SourceDiagnostic?.ElementId);
        Assert.Equal(0, provider.RequestCount);
        Assert.Equal(0, source.RequestCount);
    }

    [Theory]
    [Trait("Layer", "Contract")]
    [InlineData("<defs><text font-family='Example'>VALUE</text></defs><rect width='8' height='8'/>")]
    [InlineData("<text display='none' font-family='Example'>VALUE</text><rect width='8' height='8'/>")]
    [InlineData("<text><desc>VALUE</desc></text><rect width='8' height='8'/>")]
    [InlineData("<switch><rect width='8' height='8'/><text font-family='Example'>VALUE</text></switch>")]
    public async Task Inactive_unsupported_text_remains_font_free(string body)
    {
        (var catalog, var provider, _) = CreateExamplePack(CreateTriangleFont());
        byte[] input = Document(body.Replace("VALUE", "\u05D0\u05D1\u0915\u093F", StringComparison.Ordinal));
        using SvgPreparedDocument document = await SvgPreparedDocument.CreateAsync(input, catalog, provider, SvgRenderOptions.Default, CancellationToken.None);
        Assert.Equal(Render(Document("<rect width='8' height='8'/>")), document.ConvertToPng(CancellationToken.None).ToArray());
        Assert.Equal(0, provider.RequestCount);
    }

    [Fact]
    [Trait("Layer", "Mechanism")]
    public async Task A_prior_mark_cannot_hide_a_later_unsupported_layout_scalar()
    {
        using SvgFontSnapshot fonts = await SvgFontSnapshot.CreateAsync([CreateResolution(CreateTriangleFont())], CancellationToken.None);
        RenderBudget budget = new(new HostMemoryBudget(1_000_000));
        SvgTextStyle style = new(new("Example", 400, false), 20, 0, 20, 0, 0, 0, SvgTextAnchor.Start, "label");
        SvgException failure = Assert.Throws<SvgException>(() => SvgTextRunBuilder.Create(style, "A\u0301\u05D0", fonts, budget, CancellationToken.None));
        Assert.Equal(SvgErrorCode.UnsupportedFeature, failure.ErrorCode);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Theory]
    [Trait("Layer", "Mechanism")]
    [InlineData(0x10400, false)]
    [InlineData(0x10800, true)]
    public void Discovery_layout_admission_retains_a_scalar_split_between_chunks(int scalar, bool rejects)
    {
        string text = new Rune(scalar).ToString();
        char pending = '\0';
        SvgCanonicalText.ValidateSimpleLayout(text.AsSpan(0, 1), ref pending, null, CancellationToken.None);
        Assert.Equal(text[0], pending);
        if (rejects)
            Assert.Equal(SvgErrorCode.UnsupportedFeature, Assert.Throws<SvgException>(() =>
                SvgCanonicalText.ValidateSimpleLayout(text.AsSpan(1), ref pending, null, CancellationToken.None)).ErrorCode);
        else
        {
            SvgCanonicalText.ValidateSimpleLayout(text.AsSpan(1), ref pending, null, CancellationToken.None);
            Assert.Equal('\0', pending);
        }
    }

    private static byte[] LayoutAdmissionFont(UnsupportedTextFixture fixture)
        => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "text-layout-admission",
            fixture == UnsupportedTextFixture.Bidirectional ? "rtl.ttf" : "syllabic.ttf"));

    private static string LayoutAdmissionText(UnsupportedTextFixture fixture)
        => fixture == UnsupportedTextFixture.Bidirectional ? "\u05D0\u05D1" : "\u0915\u093F";

    private static byte[] LayoutAdmissionInput(string text, TextAdmissionScope scope)
    {
        string element = "<text id='label' font-family='Example' font-size='40' x='12' y='52'>" + text + "</text>";
        string body = scope switch
        {
            TextAdmissionScope.Direct => element,
            TextAdmissionScope.Symbol => "<defs><symbol id='s'>" + element + "</symbol></defs><use href='#s'/>",
            TextAdmissionScope.Pattern => "<defs><pattern id='p' width='128' height='72' patternUnits='userSpaceOnUse'>" + element + "</pattern></defs><rect width='128' height='72' fill='url(#p)'/>",
            _ => throw new ArgumentOutOfRangeException(nameof(scope))
        };
        return System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='128' height='72'>" + body + "</svg>");
    }
}
