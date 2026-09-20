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

    [Fact]
    public async Task Bound_source_preserves_cancellation_when_a_provider_ignores_it()
    {
        byte[] bytes = [1];
        SvgFontPackCatalog catalog = CreateCatalog(bytes);
        SvgFontProgramResolution bound = catalog.Bind(catalog.Resolve(new SvgFontFaceRequest("Example", 400, false)), new IgnoringCancellationSource(bytes));
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        OperationCanceledException exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await bound.Source.GetBytesAsync(cancellation.Token));

        Assert.Equal(cancellation.Token, exception.CancellationToken);
    }

    [Fact]
    public async Task Acquisition_selects_and_verifies_only_the_requested_program()
    {
        byte[] first = [1, 2, 3];
        byte[] second = [4, 5, 6, 7];
        SvgFontPackCatalog catalog = CreateCatalog(first, second);
        var provider = new RecordingProvider(new Dictionary<string, ISvgFontProgramSource>
        {
            ["files/example.ttf"] = new SvgMemoryFontProgramSource("example", first),
            ["files/other.ttf"] = new SvgMemoryFontProgramSource("other", second),
        });

        SvgFontProgramResolution acquired = await catalog.AcquireAsync(catalog.Resolve(new SvgFontFaceRequest("Example", 400, false)), provider, CancellationToken.None);

        Assert.Equal(["files/example.ttf"], provider.RequestedPaths);
        Assert.True((await acquired.Source.GetBytesAsync(CancellationToken.None)).Span.SequenceEqual(first));
    }

    [Fact]
    public async Task Acquisition_reports_a_missing_selected_program_without_probing_other_files()
    {
        byte[] first = [1, 2, 3];
        byte[] second = [4, 5, 6, 7];
        SvgFontPackCatalog catalog = CreateCatalog(first, second);
        var provider = new RecordingProvider(new Dictionary<string, ISvgFontProgramSource>());

        SvgException exception = await Assert.ThrowsAsync<SvgException>(async () => await catalog.AcquireAsync(catalog.Resolve(new SvgFontFaceRequest("Example", 400, false)), provider, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnresolvedResource, exception.ErrorCode);
        Assert.Equal(["files/example.ttf"], provider.RequestedPaths);
    }

    [Fact]
    public async Task Acquisition_preserves_cancellation_when_provider_ignores_it()
    {
        byte[] bytes = [1, 2, 3, 4];
        SvgFontPackCatalog catalog = CreateCatalog(bytes);
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        OperationCanceledException exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await catalog.AcquireAsync(catalog.Resolve(new SvgFontFaceRequest("Example", 400, false)), new IgnoringCancellationProvider(bytes), cancellation.Token));

        Assert.Equal(cancellation.Token, exception.CancellationToken);
    }

    private static SvgFontPackCatalog CreateCatalog(byte[] bytes) => CreateCatalog(bytes, null);

    private static SvgFontPackCatalog CreateCatalog(byte[] exampleBytes, byte[]? otherBytes)
    {
        string hash = Convert.ToHexString(SHA256.HashData(exampleBytes));
        string other = otherBytes is null ? string.Empty : ", { \"relativePath\": \"files/other.ttf\", \"byteSize\": " + otherBytes.Length + ", \"sha256\": \"" + Convert.ToHexString(SHA256.HashData(otherBytes)) + "\" }";
        string manifest = "{ \"packId\": \"test-pack\", \"files\": [{ \"relativePath\": \"files/example.ttf\", \"byteSize\": " + exampleBytes.Length + ", \"sha256\": \"" + hash + "\" }" + other + "], \"families\": [{ \"requestedFamily\": \"Example\", \"resolvedFamily\": \"Example\", \"relativeFontFile\": \"files/example.ttf\" }] }";
        return SvgFontPackCatalog.Parse(System.Text.Encoding.UTF8.GetBytes(manifest), "test-pack");
    }

    private sealed class IgnoringCancellationSource(byte[] bytes) : ISvgFontProgramSource
    {
        public string StableId => "ignoring-cancellation";
        public ValueTask<ReadOnlyMemory<byte>> GetBytesAsync(CancellationToken cancellationToken) => ValueTask.FromResult<ReadOnlyMemory<byte>>(bytes);
    }

    private sealed class RecordingProvider(IReadOnlyDictionary<string, ISvgFontProgramSource> sources) : ISvgFontProgramSourceProvider
    {
        public List<string> RequestedPaths { get; } = [];

        public ValueTask<ISvgFontProgramSource?> GetSourceAsync(SvgFontPackFile file, CancellationToken cancellationToken)
        {
            RequestedPaths.Add(file.RelativePath);
            return ValueTask.FromResult(sources.GetValueOrDefault(file.RelativePath));
        }
    }

    private sealed class IgnoringCancellationProvider(byte[] bytes) : ISvgFontProgramSourceProvider
    {
        public ValueTask<ISvgFontProgramSource?> GetSourceAsync(SvgFontPackFile file, CancellationToken cancellationToken)
            => ValueTask.FromResult<ISvgFontProgramSource?>(new IgnoringCancellationSource(bytes));
    }
}
