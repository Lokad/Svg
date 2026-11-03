using System.Security.Cryptography;
using System.Text.Json;
using Lokad.Svg.Fonts;

namespace Lokad.Svg.VisualTests;

[Trait("Layer", "Rendering")]
public sealed class SvgTextAnchorReferenceTests
{
    [Fact]
    public Task Uniform_child_anchor_survives_mixed_direct_path_interval_compaction()
        => NativeFontFixture.AssertMatches("text-shared-anchor", "text-shared-anchor", "Example");

    [Theory]
    [InlineData("inherited")]
    [InlineData("chunks")]
    [InlineData("effects")]
    [InlineData("path")]
    public async Task Text_anchor_uses_the_current_style_and_chunk_with_the_pinned_public_font(string identity)
    {
        string directory = Path.Combine(ResearchCorpus.Root, "text-anchor");
        using JsonDocument capture = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(directory, "capture.json")));
        string fontRoot = Path.Combine(ResearchCorpus.Root, "supplied-font-labels");
        byte[] program = File.ReadAllBytes(Path.Combine(fontRoot, "fonts", "NotoSans-Regular.ttf"));
        Assert.Equal(capture.RootElement.GetProperty("fontProgramSha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(program)));
        SvgFontPackCatalog catalog = SvgFontPackCatalog.Parse(File.ReadAllBytes(Path.Combine(fontRoot, "font-pack.json")), "public-noto-sans");
        byte[] input = File.ReadAllBytes(Path.Combine(directory, identity + ".svg"));
        using SvgFontSnapshot fonts = await SvgFontSnapshot.CreateAsync(input, catalog, new MemoryProvider(program), CancellationToken.None);
        SvgNativeControlAssertions.MatchesCanonicalConversion("text-anchor", identity, 4,
            new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, fonts));
    }

    /// <summary>Returns caller-owned immutable font memory without I/O or host discovery; concurrent reads are safe.</summary>
    private sealed class MemoryProvider(byte[] program) : ISvgFontProgramSourceProvider
    {
        public ValueTask<ISvgFontProgramSource?> GetSourceAsync(SvgFontPackFile file, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (file.RelativePath != "fonts/NotoSans-Regular.ttf") throw new InvalidDataException("Unexpected font request.");
            return ValueTask.FromResult<ISvgFontProgramSource?>(new SvgMemoryFontProgramSource("public-noto-sans", program));
        }
    }
}
