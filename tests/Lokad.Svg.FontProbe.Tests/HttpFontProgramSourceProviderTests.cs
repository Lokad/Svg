using System.Net;
using Lokad.Svg.FontProbe;
using Lokad.Svg.Fonts;

namespace Lokad.Svg.FontProbe.Tests;

public sealed class HttpFontProgramSourceProviderTests
{
    [Fact]
    public async Task Copies_nonseekable_svg_input_byte_for_byte()
    {
        byte[] expected = [0xEF, 0xBB, 0xBF, (byte)'<', (byte)'s', (byte)'v', (byte)'g', (byte)'/', (byte)'>'];
        await using var source = new ChunkedReadStream(expected, 2);

        byte[] actual = await Program.ReadBoundedStreamAsync(source, expected.Length, CancellationToken.None);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task Rejects_nonseekable_svg_input_that_grows_past_its_limit()
    {
        await using var source = new ChunkedReadStream([1, 2, 3], 1);

        await Assert.ThrowsAsync<Program.HostSourceException>(async () => await Program.ReadBoundedStreamAsync(source, 2, CancellationToken.None));
    }

    [Fact]
    public async Task Coalesces_concurrent_reads_and_reuses_the_bounded_cache()
    {
        var handler = new BlockingSuccessHandler([1, 2, 3]);
        using var client = new HttpClient(handler);
        ISvgFontProgramSource source = await GetSourceAsync(client);

        Task<ReadOnlyMemory<byte>> first = source.GetBytesAsync(CancellationToken.None).AsTask();
        await handler.RequestStarted;
        Task<ReadOnlyMemory<byte>>[] concurrent = Enumerable.Range(0, 7).Select(_ => source.GetBytesAsync(CancellationToken.None).AsTask()).ToArray();
        handler.Release();

        ReadOnlyMemory<byte>[] results = await Task.WhenAll([first, .. concurrent]);

        Assert.Equal(1, handler.RequestCount);
        Assert.All(results, result => Assert.Equal([1, 2, 3], result.ToArray()));
        Assert.Equal([1, 2, 3], (await source.GetBytesAsync(CancellationToken.None)).ToArray());
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task Retries_transient_transport_failures_before_caching_success()
    {
        var handler = new TransientFailureHandler(2, [4, 5]);
        using var client = new HttpClient(handler);
        ISvgFontProgramSource source = await GetSourceAsync(client);

        ReadOnlyMemory<byte> bytes = await source.GetBytesAsync(CancellationToken.None);

        Assert.Equal([4, 5], bytes.ToArray());
        Assert.Equal(3, handler.RequestCount);
        Assert.Equal([4, 5], (await source.GetBytesAsync(CancellationToken.None)).ToArray());
        Assert.Equal(3, handler.RequestCount);
    }

    [Fact]
    public async Task Releases_the_coalescing_gate_after_cancellation_for_a_later_recovery()
    {
        var handler = new CancellationThenSuccessHandler([6]);
        using var client = new HttpClient(handler);
        ISvgFontProgramSource source = await GetSourceAsync(client);
        using var cancellation = new CancellationTokenSource();

        Task<ReadOnlyMemory<byte>> pending = source.GetBytesAsync(cancellation.Token).AsTask();
        await handler.FirstRequestStarted;
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await pending);
        Assert.Equal([6], (await source.GetBytesAsync(CancellationToken.None)).ToArray());
        Assert.Equal(2, handler.RequestCount);
    }

    [Fact]
    public async Task Does_not_retain_a_program_that_exceeds_the_shared_cache_cap()
    {
        var handler = new SuccessHandler([7, 8, 9]);
        using var client = new HttpClient(handler);
        ISvgFontProgramSource source = await GetSourceAsync(client, 3, 2, 3);

        Assert.Equal([7, 8, 9], (await source.GetBytesAsync(CancellationToken.None)).ToArray());
        Assert.Equal([7, 8, 9], (await source.GetBytesAsync(CancellationToken.None)).ToArray());

        Assert.Equal(2, handler.RequestCount);
    }

    private static ValueTask<ISvgFontProgramSource> GetSourceAsync(HttpClient client)
        => GetSourceAsync(client, 3, 32, 3);

    private static async ValueTask<ISvgFontProgramSource> GetSourceAsync(HttpClient client, long maximumProgramBytes, long maximumCachedProgramBytes, int maximumAttempts)
    {
        SvgFontPackCatalog catalog = SvgFontPackCatalog.Parse("""
            { "packId": "test-pack", "files": [{ "relativePath": "files/example.ttf", "byteSize": 3, "sha256": "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA" }], "families": [{ "requestedFamily": "Example", "resolvedFamily": "Example", "relativeFontFile": "files/example.ttf" }] }
            """u8.ToArray(), "test-pack");
        var provider = new Program.HttpFontProgramSourceProvider(client, new Uri("https://example.test/test-pack/"), maximumProgramBytes, maximumCachedProgramBytes, maximumAttempts);
        return await provider.GetSourceAsync(Assert.Single(catalog.Files), CancellationToken.None).ConfigureAwait(false) ?? throw new InvalidOperationException("The HTTP provider did not return a source.");
    }

    private sealed class BlockingSuccessHandler(byte[] bytes) : HttpMessageHandler
    {
        private readonly TaskCompletionSource _requestStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _requestCount;

        internal Task RequestStarted => _requestStarted.Task;

        internal int RequestCount => Volatile.Read(ref _requestCount);

        internal void Release() => _release.SetResult();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _requestCount);
            _requestStarted.TrySetResult();
            await _release.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            return CreateSuccess(bytes);
        }
    }

    private sealed class TransientFailureHandler(int failures, byte[] bytes) : HttpMessageHandler
    {
        private readonly int _failures = failures;
        private int _requestCount;

        internal int RequestCount => Volatile.Read(ref _requestCount);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            int requestCount = Interlocked.Increment(ref _requestCount);
            if (requestCount <= _failures) throw new HttpRequestException("Synthetic transient failure.");
            return Task.FromResult(CreateSuccess(bytes));
        }
    }

    private sealed class SuccessHandler(byte[] bytes) : HttpMessageHandler
    {
        private int _requestCount;

        internal int RequestCount => Volatile.Read(ref _requestCount);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _requestCount);
            return Task.FromResult(CreateSuccess(bytes));
        }
    }

    private sealed class CancellationThenSuccessHandler(byte[] bytes) : HttpMessageHandler
    {
        private readonly TaskCompletionSource _firstRequestStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _requestCount;

        internal Task FirstRequestStarted => _firstRequestStarted.Task;

        internal int RequestCount => Volatile.Read(ref _requestCount);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            int requestCount = Interlocked.Increment(ref _requestCount);
            if (requestCount != 1) return CreateSuccess(bytes);
            _firstRequestStarted.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
            throw new InvalidOperationException("The canceled synthetic request continued unexpectedly.");
        }
    }

    private static HttpResponseMessage CreateSuccess(byte[] bytes)
        => new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };

    private sealed class ChunkedReadStream(byte[] bytes, int maximumChunkBytes) : Stream
    {
        private readonly byte[] _bytes = bytes;
        private readonly int _maximumChunkBytes = maximumChunkBytes;
        private int _offset;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override void Flush() => throw new NotSupportedException();

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_offset == _bytes.Length) return ValueTask.FromResult(0);
            int count = Math.Min(Math.Min(buffer.Length, _maximumChunkBytes), _bytes.Length - _offset);
            _bytes.AsSpan(_offset, count).CopyTo(buffer.Span);
            _offset += count;
            return ValueTask.FromResult(count);
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
