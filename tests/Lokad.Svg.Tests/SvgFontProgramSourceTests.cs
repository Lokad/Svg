using System.Security.Cryptography;
using System.Text;
using System.Runtime.InteropServices;
using Lokad.Svg.Fonts;

namespace Lokad.Svg.Tests;

public sealed class SvgFontProgramSourceTests
{
    [Fact]
    public async Task Bound_source_preserves_caller_memory_and_verifies_declared_integrity()
    {
        byte[] bytes = [1, 2, 3, 4];
        SvgFontPackCatalog catalog = CreateCatalog(bytes);
        SvgFontProgramResolution bound = catalog.Bind(catalog.Resolve(new SvgFontFaceRequest("Example", 400, false)), new SvgMemoryFontProgramSource("caller-memory", bytes));

        ReadOnlyMemory<byte> actual = await bound.Source.GetBytesAsync(CancellationToken.None);

        Assert.Equal("lokad-svg-font-pack:test-pack:" + Convert.ToHexString(SHA256.HashData(bytes)) + ":0", bound.Source.StableId);
        Assert.True(actual.Span.SequenceEqual(bytes));
        Assert.True(MemoryMarshal.TryGetArray(actual, out ArraySegment<byte> segment));
        Assert.Same(bytes, segment.Array);
    }

    [Fact]
    public async Task Bound_source_rejects_changed_or_wrong_sized_bytes()
    {
        byte[] expected = [1, 2, 3, 4];
        SvgFontPackCatalog catalog = CreateCatalog(expected);
        SvgFontPackResolution resolution = catalog.Resolve(new SvgFontFaceRequest("Example", 400, false));
        SvgFontProgramResolution wrongHash = catalog.Bind(resolution, new SvgMemoryFontProgramSource("wrong-hash", new byte[] { 1, 2, 3, 5 }));
        SvgFontProgramResolution wrongSize = catalog.Bind(resolution, new SvgMemoryFontProgramSource("wrong-size", new byte[] { 1, 2, 3 }));

        SvgException hash = await Assert.ThrowsAsync<SvgException>(async () => await wrongHash.Source.GetBytesAsync(CancellationToken.None));
        SvgException size = await Assert.ThrowsAsync<SvgException>(async () => await wrongSize.Source.GetBytesAsync(CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnresolvedResource, hash.ErrorCode);
        Assert.Equal(SvgErrorCode.UnresolvedResource, size.ErrorCode);
    }

    [Fact]
    public void Bound_source_rejects_resolution_from_a_different_catalog()
    {
        byte[] bytes = [1];
        SvgFontPackCatalog first = CreateCatalog(bytes);
        SvgFontPackCatalog second = CreateCatalog(bytes);

        Assert.Throws<ArgumentException>(() => first.Bind(second.Resolve(new SvgFontFaceRequest("Example", 400, false)), new SvgMemoryFontProgramSource("memory", bytes)));
    }

    private static SvgFontPackCatalog CreateCatalog(byte[] bytes)
    {
        string hash = Convert.ToHexString(SHA256.HashData(bytes));
        string manifest = "{ \"packId\": \"test-pack\", \"files\": [{ \"relativePath\": \"files/example.ttf\", \"byteSize\": " + bytes.Length + ", \"sha256\": \"" + hash + "\" }], \"families\": [{ \"requestedFamily\": \"Example\", \"resolvedFamily\": \"Example\", \"relativeFontFile\": \"files/example.ttf\" }] }";
        return SvgFontPackCatalog.Parse(System.Text.Encoding.UTF8.GetBytes(manifest), "test-pack");
    }
}
