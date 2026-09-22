using System.Security.Cryptography;
using System.Text;
using System.Runtime.InteropServices;
using Lokad.Svg.Fonts;

namespace Lokad.Svg.Tests;

public sealed class SvgFontProgramSourceTests
{
    [Fact]
    public async Task Prepared_document_converts_once_and_releases_its_snapshot_lease()
    {
        SvgFontPackCatalog catalog = CreateCatalog([1, 2, 3, 4]);
        var provider = new RecordingProvider(new Dictionary<string, ISvgFontProgramSource>());
        SvgRenderOptions options = new(new SvgViewport(1, 1), 1, SvgColor.Transparent, SvgRenderLimits.Default);

        using SvgPreparedDocument document = await SvgPreparedDocument.CreateAsync("<svg xmlns='http://www.w3.org/2000/svg'/>"u8.ToArray(), catalog, provider, options, CancellationToken.None);
        ReadOnlyMemory<byte> png = document.ConvertToPng(CancellationToken.None);

        Assert.NotEmpty(png.ToArray());
        Assert.Empty(provider.RequestedPaths);
        Assert.Throws<ObjectDisposedException>(() => document.ConvertToPng(CancellationToken.None));
    }

    [Fact]
    public async Task Prepared_document_asynchronously_converts_once()
    {
        SvgFontPackCatalog catalog = CreateCatalog([1, 2, 3, 4]);
        var provider = new RecordingProvider(new Dictionary<string, ISvgFontProgramSource>());
        SvgRenderOptions options = new(new SvgViewport(1, 1), 1, SvgColor.Transparent, SvgRenderLimits.Default);

        using SvgPreparedDocument document = await SvgPreparedDocument.CreateAsync("<svg xmlns='http://www.w3.org/2000/svg'/>"u8.ToArray(), catalog, provider, options, CancellationToken.None);
        ReadOnlyMemory<byte> png = await document.ConvertToPngAsync(CancellationToken.None);

        Assert.NotEmpty(png.ToArray());
        await Assert.ThrowsAsync<ObjectDisposedException>(() => document.ConvertToPngAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Prepared_document_writes_caller_memory_once()
    {
        SvgFontPackCatalog catalog = CreateCatalog([1, 2, 3, 4]);
        var provider = new RecordingProvider(new Dictionary<string, ISvgFontProgramSource>());
        SvgRenderOptions options = new(new SvgViewport(1, 1), 1, SvgColor.Transparent, SvgRenderLimits.Default);
        byte[] svg = "<svg xmlns='http://www.w3.org/2000/svg'/>"u8.ToArray();
        ReadOnlyMemory<byte> expected = SvgConverter.ConvertToPng(svg, options, CancellationToken.None);
        byte[] destination = new byte[expected.Length];

        using SvgPreparedDocument document = await SvgPreparedDocument.CreateAsync(svg, catalog, provider, options, CancellationToken.None);
        int written = document.ConvertToPng(destination, CancellationToken.None);

        Assert.Equal(expected.Length, written);
        Assert.Equal(expected.ToArray(), destination);
        Assert.Throws<ObjectDisposedException>(() => document.ConvertToPng(destination, CancellationToken.None));
    }

    [Fact]
    public async Task Prepared_document_writes_stream_once()
    {
        SvgFontPackCatalog catalog = CreateCatalog([1, 2, 3, 4]);
        var provider = new RecordingProvider(new Dictionary<string, ISvgFontProgramSource>());
        SvgRenderOptions options = new(new SvgViewport(1, 1), 1, SvgColor.Transparent, SvgRenderLimits.Default);
        byte[] svg = "<svg xmlns='http://www.w3.org/2000/svg'/>"u8.ToArray();
        ReadOnlyMemory<byte> expected = SvgConverter.ConvertToPng(svg, options, CancellationToken.None);
        using MemoryStream destination = new(expected.Length);

        using SvgPreparedDocument document = await SvgPreparedDocument.CreateAsync(svg, catalog, provider, options, CancellationToken.None);
        int written = document.ConvertToPng(destination, CancellationToken.None);

        Assert.Equal(expected.Length, written);
        Assert.Equal(expected.ToArray(), destination.ToArray());
        Assert.Throws<ObjectDisposedException>(() => document.ConvertToPng(destination, CancellationToken.None));
    }

    [Fact]
    public async Task Prepared_document_asynchronously_writes_caller_memory_once()
    {
        SvgFontPackCatalog catalog = CreateCatalog([1, 2, 3, 4]);
        var provider = new RecordingProvider(new Dictionary<string, ISvgFontProgramSource>());
        SvgRenderOptions options = new(new SvgViewport(1, 1), 1, SvgColor.Transparent, SvgRenderLimits.Default);
        byte[] svg = "<svg xmlns='http://www.w3.org/2000/svg'/>"u8.ToArray();
        ReadOnlyMemory<byte> expected = SvgConverter.ConvertToPng(svg, options, CancellationToken.None);
        byte[] destination = new byte[expected.Length];

        using SvgPreparedDocument document = await SvgPreparedDocument.CreateAsync(svg, catalog, provider, options, CancellationToken.None);
        int written = await document.ConvertToPngAsync(destination, CancellationToken.None);

        Assert.Equal(expected.Length, written);
        Assert.Equal(expected.ToArray(), destination);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => document.ConvertToPngAsync(destination, CancellationToken.None));
    }

    [Fact]
    public async Task Prepared_document_asynchronously_writes_stream_once()
    {
        SvgFontPackCatalog catalog = CreateCatalog([1, 2, 3, 4]);
        var provider = new RecordingProvider(new Dictionary<string, ISvgFontProgramSource>());
        SvgRenderOptions options = new(new SvgViewport(1, 1), 1, SvgColor.Transparent, SvgRenderLimits.Default);
        byte[] svg = "<svg xmlns='http://www.w3.org/2000/svg'/>"u8.ToArray();
        ReadOnlyMemory<byte> expected = SvgConverter.ConvertToPng(svg, options, CancellationToken.None);
        using MemoryStream destination = new(expected.Length);

        using SvgPreparedDocument document = await SvgPreparedDocument.CreateAsync(svg, catalog, provider, options, CancellationToken.None);
        int written = await document.ConvertToPngAsync(destination, CancellationToken.None);

        Assert.Equal(expected.Length, written);
        Assert.Equal(expected.ToArray(), destination.ToArray());
        await Assert.ThrowsAsync<ObjectDisposedException>(() => document.ConvertToPngAsync(destination, CancellationToken.None));
    }

    [Fact]
    public async Task Prepared_document_charges_preparation_against_conversion_work()
    {
        SvgFontPackCatalog catalog = CreateCatalog([1, 2, 3, 4]);
        var provider = new RecordingProvider(new Dictionary<string, ISvgFontProgramSource>());
        SvgRenderLimits limits = new(200_000, 1_024, 16, 16, 1_024, 16, 16, 30);
        SvgRenderOptions options = new(new SvgViewport(1, 1), 1, SvgColor.Transparent, limits);
        byte[] svg = "<svg xmlns='http://www.w3.org/2000/svg'/>"u8.ToArray();

        Assert.NotEmpty(SvgConverter.ConvertToPng(svg, options, CancellationToken.None).ToArray());
        using SvgPreparedDocument document = await SvgPreparedDocument.CreateAsync(svg, catalog, provider, options, CancellationToken.None);

        SvgException exception = Assert.Throws<SvgException>(() => document.ConvertToPng(CancellationToken.None));

        Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode);
        Assert.Contains("PNG serialization", exception.Message);
    }

    [Fact]
    public async Task Prepared_document_charges_preparation_against_every_conversion_output_form()
    {
        SvgFontPackCatalog catalog = CreateCatalog([1, 2, 3, 4]);
        var provider = new RecordingProvider(new Dictionary<string, ISvgFontProgramSource>());
        SvgRenderLimits limits = new(200_000, 1_024, 16, 16, 1_024, 16, 16, 16);
        SvgRenderOptions options = new(new SvgViewport(1, 1), 1, SvgColor.Transparent, limits);
        byte[] svg = "<svg xmlns='http://www.w3.org/2000/svg'/>"u8.ToArray();
        byte[] destination = new byte[1_024];

        using (SvgPreparedDocument document = await CreateDocument(catalog, provider, options, svg))
        {
            AssertPngWorkLimit(Assert.Throws<SvgException>(() => document.ConvertToPng(CancellationToken.None)));
        }
        using (SvgPreparedDocument document = await CreateDocument(catalog, provider, options, svg))
        {
            AssertPngWorkLimit(Assert.Throws<SvgException>(() => document.ConvertToPng(destination, CancellationToken.None)));
        }
        using (SvgPreparedDocument document = await CreateDocument(catalog, provider, options, svg))
        using (var stream = new MemoryStream())
        {
            AssertPngWorkLimit(Assert.Throws<SvgException>(() => document.ConvertToPng(stream, CancellationToken.None)));
        }
        using (SvgPreparedDocument document = await CreateDocument(catalog, provider, options, svg))
        {
            AssertPngWorkLimit(await Assert.ThrowsAsync<SvgException>(async () => await document.ConvertToPngAsync(CancellationToken.None)));
        }
        using (SvgPreparedDocument document = await CreateDocument(catalog, provider, options, svg))
        {
            AssertPngWorkLimit(await Assert.ThrowsAsync<SvgException>(async () => await document.ConvertToPngAsync(destination, CancellationToken.None)));
        }
        using (SvgPreparedDocument document = await CreateDocument(catalog, provider, options, svg))
        using (var stream = new MemoryStream())
        {
            AssertPngWorkLimit(await Assert.ThrowsAsync<SvgException>(async () => await document.ConvertToPngAsync(stream, CancellationToken.None)));
        }

        static ValueTask<SvgPreparedDocument> CreateDocument(SvgFontPackCatalog catalog, RecordingProvider provider, SvgRenderOptions options, byte[] svg)
            => SvgPreparedDocument.CreateAsync(svg, catalog, provider, options, CancellationToken.None);

        static void AssertPngWorkLimit(SvgException exception)
        {
            Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode);
            Assert.Contains("PNG compression", exception.Message);
        }
    }

    [Fact]
    public async Task Prepared_document_lowers_its_scene_during_creation()
    {
        SvgFontPackCatalog catalog = CreateCatalog([1, 2, 3, 4]);
        var provider = new RecordingProvider(new Dictionary<string, ISvgFontProgramSource>());
        SvgRenderLimits limits = new(200_000, 1_024, 16, 16, 1_024, 16, 16, 3);
        SvgRenderOptions options = new(new SvgViewport(1, 1), 1, SvgColor.Transparent, limits);
        byte[] svg = "<svg xmlns='http://www.w3.org/2000/svg'/>"u8.ToArray();

        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync(svg, catalog, provider, limits, CancellationToken.None);
        SvgException exception = await Assert.ThrowsAsync<SvgException>(async () => await SvgPreparedDocument.CreateAsync(svg, catalog, provider, options, CancellationToken.None));

        Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode);
        Assert.Contains("parsing work", exception.Message);
    }

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

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Memory_sources_reject_blank_stable_ids(string stableId)
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(() => new SvgMemoryFontProgramSource(stableId, ReadOnlyMemory<byte>.Empty));

        Assert.Equal("stableId", exception.ParamName);
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
    public async Task Snapshot_reverifies_bytes_mutated_after_acquisition()
    {
        byte[] program = [1, 2, 3, 4];
        SvgFontPackCatalog catalog = CreateCatalog(program);
        var provider = new RecordingProvider(new Dictionary<string, ISvgFontProgramSource>
        {
            ["files/example.ttf"] = new SvgMemoryFontProgramSource("mutating", program),
        });

        SvgFontProgramResolution acquired = await catalog.AcquireAsync(catalog.Resolve(new SvgFontFaceRequest("Example", 400, false)), provider, CancellationToken.None);
        program[0] = 9;

        SvgException exception = await Assert.ThrowsAsync<SvgException>(async () => await SvgFontSnapshot.CreateAsync([acquired], CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnresolvedResource, exception.ErrorCode);
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
        SvgFontDiagnostic diagnostic = Assert.IsType<SvgFontDiagnostic>(exception.FontDiagnostic);
        Assert.Equal(SvgFontFailureStage.Source, diagnostic.Stage);
        Assert.Equal("Example", diagnostic.RequestedFamily);
        Assert.Equal("Example", diagnostic.ResolvedFamily);
        Assert.Equal("files/example.ttf:0", diagnostic.FaceIdentity);
        Assert.Empty(diagnostic.SourceElementId);
    }

    [Theory]
    [InlineData(SvgErrorCode.UnresolvedResource, false)]
    [InlineData(SvgErrorCode.UnresolvedResource, true)]
    [InlineData(SvgErrorCode.LimitExceeded, false)]
    [InlineData(SvgErrorCode.LimitExceeded, true)]
    public async Task Acquisition_classifies_provider_failures_by_origin(SvgErrorCode errorCode, bool throwsSynchronously)
    {
        SvgFontPackCatalog catalog = CreateCatalog([1, 2, 3, 4]);

        SvgException exception = await Assert.ThrowsAsync<SvgException>(async () => await catalog.AcquireAsync(catalog.Resolve(new SvgFontFaceRequest("Example", 400, false)), new FailingProvider(errorCode, throwsSynchronously), CancellationToken.None));

        Assert.Equal(errorCode, exception.ErrorCode);
        Assert.DoesNotContain("private-provider-detail", exception.Message, StringComparison.Ordinal);
        SvgFontDiagnostic diagnostic = Assert.IsType<SvgFontDiagnostic>(exception.FontDiagnostic);
        Assert.Equal(SvgFontFailureStage.Source, diagnostic.Stage);
        Assert.Equal("Example", diagnostic.RequestedFamily);
        Assert.Equal("Example", diagnostic.ResolvedFamily);
        Assert.Equal("files/example.ttf:0", diagnostic.FaceIdentity);
        Assert.Empty(diagnostic.SourceElementId);
    }

    [Theory]
    [InlineData(SvgErrorCode.UnresolvedResource, false)]
    [InlineData(SvgErrorCode.UnresolvedResource, true)]
    [InlineData(SvgErrorCode.LimitExceeded, false)]
    [InlineData(SvgErrorCode.LimitExceeded, true)]
    public async Task Acquisition_classifies_bound_source_failures_by_origin(SvgErrorCode errorCode, bool throwsSynchronously)
    {
        SvgFontPackCatalog catalog = CreateCatalog([1, 2, 3, 4]);
        var provider = new RecordingProvider(new Dictionary<string, ISvgFontProgramSource>
        {
            ["files/example.ttf"] = new FailingSource(errorCode, throwsSynchronously),
        });

        SvgException exception = await Assert.ThrowsAsync<SvgException>(async () => await catalog.AcquireAsync(catalog.Resolve(new SvgFontFaceRequest("Example", 400, false)), provider, CancellationToken.None));

        Assert.Equal(errorCode, exception.ErrorCode);
        Assert.DoesNotContain("private-source-detail", exception.Message, StringComparison.Ordinal);
        SvgFontDiagnostic diagnostic = Assert.IsType<SvgFontDiagnostic>(exception.FontDiagnostic);
        Assert.Equal(SvgFontFailureStage.Source, diagnostic.Stage);
        Assert.Equal("Example", diagnostic.RequestedFamily);
        Assert.Equal("Example", diagnostic.ResolvedFamily);
        Assert.Equal("files/example.ttf:0", diagnostic.FaceIdentity);
        Assert.Empty(diagnostic.SourceElementId);
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

    private sealed class FailingProvider(SvgErrorCode errorCode, bool throwsSynchronously) : ISvgFontProgramSourceProvider
    {
        public ValueTask<ISvgFontProgramSource?> GetSourceAsync(SvgFontPackFile file, CancellationToken cancellationToken)
        {
            if (throwsSynchronously) throw new SvgException(errorCode, "private-provider-detail");
            return ValueTask.FromException<ISvgFontProgramSource?>(new SvgException(errorCode, "private-provider-detail"));
        }
    }

    private sealed class FailingSource(SvgErrorCode errorCode, bool throwsSynchronously) : ISvgFontProgramSource
    {
        public string StableId => "failing-source";

        public ValueTask<ReadOnlyMemory<byte>> GetBytesAsync(CancellationToken cancellationToken)
        {
            if (throwsSynchronously) throw new SvgException(errorCode, "private-source-detail");
            return ValueTask.FromException<ReadOnlyMemory<byte>>(new SvgException(errorCode, "private-source-detail"));
        }
    }
}
