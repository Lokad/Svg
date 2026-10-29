using System.Buffers.Binary;
using System.Text.Json;
using System.Security.Cryptography;
using Lokad.Svg.Fonts;
using Lokad.Svg.Parsing;

namespace Lokad.Svg.Tests;

public sealed partial class OpenTypeFontTests
{
    [Theory]
    [InlineData("Courier New", true)]
    [InlineData("courier new", false)]
    [InlineData("Courier new", false)]
    [InlineData("Courier Neo", false)]
    public void Windows_span_policy_uses_the_exact_embedded_family_without_mutating_programs(string family, bool expected)
    {
        byte[] bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "windows-font-span-extension", "Courier New.ttf"));
        int tables = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(4));
        for (int index = 0; index < tables; index++)
        {
            int record = 12 + index * 16;
            if (BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(record)) != 0x6E616D65) continue;
            int offset = (int)BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(record + 8));
            int count = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(offset + 2));
            int storage = offset + BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(offset + 4));
            for (int item = 0; item < count; item++)
            {
                int name = offset + 6 + 12 * item;
                if (BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(name + 6)) != 1) continue;
                bool unicode = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(name)) != 1;
                byte[] value = unicode ? System.Text.Encoding.BigEndianUnicode.GetBytes(family) : System.Text.Encoding.ASCII.GetBytes(family);
                Assert.Equal(value.Length, BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(name + 8)));
                value.CopyTo(bytes, storage + BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(name + 10)));
            }
        }
        byte[] unchanged = bytes.ToArray();
        Assert.Equal(expected, OpenTypeFont.ParseMetrics(bytes, CancellationToken.None).ExtendWindowsLcdSpans);
        Assert.Equal(unchanged, bytes);
    }

    [Fact]
    public async Task A_Courier_catalog_alias_does_not_apply_native_metadata_to_a_different_program()
    {
        byte[] bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "windows-font-span-extension", "Original Coverage.ttf"));
        byte[] manifest = JsonSerializer.SerializeToUtf8Bytes(new
        {
            packId = "span-alias",
            files = new[] { new { relativePath = "ordinary.ttf", byteSize = bytes.Length, sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes)) } },
            families = new[] { new { requestedFamily = "Courier New", resolvedFamily = "Original Coverage", relativeFontFile = "ordinary.ttf" } }
        });
        SvgFontPackCatalog catalog = SvgFontPackCatalog.Parse(manifest, "span-alias");
        SvgFontProgramResolution resolution = catalog.Bind(catalog.Resolve(new("Courier New", 400, false)), new SvgMemoryFontProgramSource("ordinary", bytes));
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync([resolution], CancellationToken.None);
        Assert.False(Assert.Single(snapshot.Fonts).Metrics.ExtendWindowsLcdSpans);
        Assert.False(OpenTypeFont.ParseMetrics(CreateTriangleFont(), CancellationToken.None).ExtendWindowsLcdSpans);
    }
}
