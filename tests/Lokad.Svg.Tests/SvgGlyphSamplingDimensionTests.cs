using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Lokad.Svg.Fonts;
using Lokad.Svg.Rendering;
using Xunit;

namespace Lokad.Svg.Tests;

[Trait("Layer", "Contract")]
public sealed class SvgGlyphSamplingDimensionTests
{
    [Fact]
    public async Task Guard_columns_do_not_inflate_the_allocated_sampling_plane_limit()
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'><g transform='translate(4 6.75) scale(1.25)'><text x='2' y='11' font-family='serif' font-size='10' fill='red' stroke='blue' stroke-width='.5'>g</text></g></svg>");
        byte[] bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "text-paint-bounds", "sans.ttf"));
        Assert.Equal("b85c38ecea8a7cfb39c24e395a4007474fa5a4fc864f6ee33309eb4948d232d5", Convert.ToHexStringLower(SHA256.HashData(bytes)));
        SvgFontPackCatalog catalog = SvgFontPackCatalog.Parse(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "text-node-boundaries", "font-pack.json")), "public-noto-sans");
        using SvgFontSnapshot fonts = await SvgFontSnapshot.CreateAsync(input, catalog, new Programs(bytes), CancellationToken.None);
        SvgRenderLimits d = SvgRenderLimits.Default;
        SvgRenderLimits limits = new(d.MaximumInputBytes, 32, d.MaximumPixels, d.MaximumEncodedPngBytes, d.MaximumNodes, d.MaximumPathCommands, d.MaximumXmlDepth, d.MaximumAttributesPerElement);
        SvgRenderOptions normal = new(null, 1, SvgColor.Transparent, d, fonts);
        SvgRenderOptions bounded = new(null, 1, SvgColor.Transparent, limits, fonts);
        byte[] expected = SvgConverter.ConvertToPng(input, normal, CancellationToken.None).ToArray();
        Assert.Equal(expected, SvgConverter.ConvertToPng(input, bounded, CancellationToken.None).ToArray());
        byte[] wider = System.Text.Encoding.UTF8.GetBytes(System.Text.Encoding.UTF8.GetString(input).Replace("stroke-width='.5'", "stroke-width='2'", StringComparison.Ordinal));
        if (OperatingSystem.IsWindows())
            Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(wider, bounded, CancellationToken.None)).ErrorCode);
        else
            Assert.Equal(SvgConverter.ConvertToPng(wider, normal, CancellationToken.None).ToArray(), SvgConverter.ConvertToPng(wider, bounded, CancellationToken.None).ToArray());
    }

    private sealed class Programs(byte[] bytes) : ISvgFontProgramSourceProvider
    {
        /// <summary>Transfers a fresh source over unchanged caller-held font bytes, observes cancellation and allows concurrent requests.</summary>
        public ValueTask<ISvgFontProgramSource?> GetSourceAsync(SvgFontPackFile file, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            return ValueTask.FromResult<ISvgFontProgramSource?>(new SvgMemoryFontProgramSource("public-noto-sans", bytes));
        }
    }
}
