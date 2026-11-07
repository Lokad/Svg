using Lokad.Svg.Fonts;

namespace Lokad.Svg.Tests;

public sealed partial class OpenTypeFontTests
{
    [Theory]
    [Trait("Layer", "Mechanism")]
    [InlineData(false)]
    [InlineData(true)]
    public void A_cmap_batch_maps_the_BMP_domain_and_supplementary_boundaries_with_one_validation(bool format12)
    {
        byte[] bytes = format12 ? CreateFormat12Font() : CreateFont(1024, 3, 2, 0);
        OpenTypeFontMetrics metrics = OpenTypeFont.ParseMetrics(bytes, CancellationToken.None);
        OpenTypeFont.CmapLookup lookup = new(bytes, metrics);
        int validations = 0;
        using IDisposable probe = CmapLookupProbe.ArmValidation(() => validations++);
        Assert.Equal(0, validations);
        for (uint scalar = 0; scalar <= 0xFFFF; scalar++)
        {
            if (scalar is >= 0xD800 and <= 0xDFFF) continue;
            ushort expected = !format12 && scalar == 'A' ? (ushort)1 : (ushort)0;
            Assert.Equal(expected, lookup.GetGlyphIndex(scalar, CancellationToken.None));
        }
        foreach (uint scalar in new uint[] { 0x10000, 0x1F5FF, 0x1F600, 0x1F601, 0x10FFFF })
            Assert.Equal(format12 && scalar == 0x1F600 ? (ushort)2 : (ushort)0, lookup.GetGlyphIndex(scalar, CancellationToken.None));
        Assert.Equal(1, validations);
    }

    [Fact]
    [Trait("Layer", "Mechanism")]
    public void Independent_cmap_reads_still_validate_each_call()
    {
        byte[] bytes = CreateFont(1024, 3, 2, 0);
        OpenTypeFontMetrics metrics = OpenTypeFont.ParseMetrics(bytes, CancellationToken.None);
        int validations = 0;
        using IDisposable probe = CmapLookupProbe.ArmValidation(() => validations++);
        Assert.Equal(1, OpenTypeFont.GetGlyphIndex(bytes, 'A', metrics, CancellationToken.None));
        Assert.Equal(0, OpenTypeFont.GetGlyphIndex(bytes, 'B', metrics, CancellationToken.None));
        Assert.Equal(2, validations);
    }

    [Fact]
    [Trait("Layer", "Mechanism")]
    public void Canceled_cmap_preparation_can_retry_without_retaining_partial_validation()
    {
        byte[] bytes = CreateFont(1024, 3, 2, 0);
        OpenTypeFont.CmapLookup lookup = new(bytes, OpenTypeFont.ParseMetrics(bytes, CancellationToken.None));
        using CancellationTokenSource cancellation = new();
        int validations = 0;
        using IDisposable probe = CmapLookupProbe.ArmValidation(() => { validations++; cancellation.Cancel(); });
        try
        {
            lookup.GetGlyphIndex('A', cancellation.Token);
            Assert.Fail("Canceled validation completed.");
        }
        catch (OperationCanceledException error) { Assert.Equal(cancellation.Token, error.CancellationToken); }
        Assert.Equal(1, lookup.GetGlyphIndex('A', CancellationToken.None));
        Assert.Equal(0, lookup.GetGlyphIndex('B', CancellationToken.None));
        Assert.Equal(2, validations);
    }

    [Fact]
    [Trait("Layer", "Mechanism")]
    public void Prepared_cmap_lookups_keep_the_scalar_entry_cancellation_checkpoint()
    {
        byte[] bytes = CreateFont(1024, 3, 2, 0);
        OpenTypeFont.CmapLookup lookup = new(bytes, OpenTypeFont.ParseMetrics(bytes, CancellationToken.None));
        Assert.Equal(1, lookup.GetGlyphIndex('A', CancellationToken.None));
        using CancellationTokenSource cancellation = new();
        using (IDisposable probe = CmapLookupProbe.Arm(cancellation.Cancel))
        {
            try
            {
                lookup.GetGlyphIndex('B', cancellation.Token);
                Assert.Fail("Prepared lookup skipped its cancellation checkpoint.");
            }
            catch (OperationCanceledException error) { Assert.Equal(cancellation.Token, error.CancellationToken); }
        }
        Assert.Equal(1, lookup.GetGlyphIndex('A', CancellationToken.None));
    }

    [Theory]
    [Trait("Layer", "Mechanism")]
    [InlineData(false)]
    [InlineData(true)]
    public void Batch_construction_defers_malformed_cmap_admission_until_lookup_and_does_not_cache_failure(bool format12)
    {
        byte[] bytes = format12 ? CreateFormat12FontWithLaterMalformedGroup() : CreateFont(1024, 3, 2, 0);
        OpenTypeFontMetrics metrics = OpenTypeFont.ParseMetrics(bytes, CancellationToken.None);
        if (!format12)
        {
            OpenTypeTableRange table = Assert.IsType<OpenTypeTableRange>(metrics.Cmap);
            WriteUInt16(bytes, table.Offset + 12 + 16, 0xFFFE);
        }
        int validations = 0;
        using IDisposable probe = CmapLookupProbe.ArmValidation(() => validations++);
        OpenTypeFont.CmapLookup lookup = new(bytes, metrics);
        Assert.Equal(0, validations);
        for (int attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                lookup.GetGlyphIndex('A', CancellationToken.None);
                Assert.Fail("Malformed unqueried cmap metadata was accepted.");
            }
            catch (SvgException error) { Assert.Equal(SvgErrorCode.MalformedInput, error.ErrorCode); }
        }
        Assert.Equal(2, validations);
    }
}
