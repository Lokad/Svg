using System.Net;
using System.Text.Json;
using Lokad.Svg.Fonts;
using Lokad.Svg.Testing;

namespace Lokad.Svg.Tests;

public sealed partial class OpenTypeFontTests
{
    [Fact]
    [Trait("Layer", "Contracts")]
    public async Task Http_resolver_coalesces_manifest_waiters_and_does_not_borrow_the_first_token()
    {
        (byte[] manifest, _) = HttpFontFixture("files/example.ttf");
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new HttpFontHandler(async (_, token) =>
        {
            entered.TrySetResult();
            await release.Task.WaitAsync(token);
            return new(HttpStatusCode.OK) { Content = new ByteArrayContent(manifest) };
        });
        using var client = new HttpClient(handler);
        using var budget = new TestMemoryBudget(1_000_000);
        await using var resolver = SvgHttpFontPackResolver.Create("test-pack", new("https://fonts.invalid/pack/"), client, HttpFontOptions(), budget);
        Assert.Equal(0, handler.Requests);
        Assert.Equal(0, budget.Reserved);
        using var first = resolver.OpenOperation();
        using var second = resolver.OpenOperation();
        using var canceled = new CancellationTokenSource();
        Task<SvgFontPackCatalog> pending = first.GetCatalogAsync(canceled.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Task<SvgFontPackCatalog> other = second.GetCatalogAsync(CancellationToken.None);
        canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await pending);
        release.TrySetResult();
        SvgFontPackCatalog catalog = await other;
        Assert.Same(catalog, await second.GetCatalogAsync(CancellationToken.None));
        Assert.Equal(1, handler.Requests);
        first.Dispose(); second.Dispose();
        await resolver.DisposeAsync();
        Assert.Equal(0, budget.Reserved);
        Assert.False(budget.Disposed);
        using HttpResponseMessage response = await client.GetAsync("https://fonts.invalid/pack/manifest.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    [Trait("Layer", "Contracts")]
    public async Task Http_resolver_shares_verified_programs_and_keeps_live_borrows_charged_after_disposal()
    {
        (byte[] manifest, byte[] font) = HttpFontFixture("files/example.ttf");
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int programRequests = 0;
        using var handler = new HttpFontHandler(async (request, token) =>
        {
            if (request.RequestUri?.AbsolutePath.EndsWith("manifest.json", StringComparison.Ordinal) == true)
                return new(HttpStatusCode.OK) { Content = new ByteArrayContent(manifest) };
            Interlocked.Increment(ref programRequests);
            entered.TrySetResult();
            await release.Task.WaitAsync(token);
            return new(HttpStatusCode.OK) { Content = new ByteArrayContent(font) };
        });
        using var client = new HttpClient(handler);
        using var budget = new TestMemoryBudget(1_000_000);
        await using var resolver = SvgHttpFontPackResolver.Create("test-pack", new("https://fonts.invalid/pack/"), client, HttpFontOptions(), budget);
        using var first = resolver.OpenOperation(); using var second = resolver.OpenOperation();
        SvgFontPackCatalog catalog = await first.GetCatalogAsync(CancellationToken.None);
        SvgFontPackFile file = Assert.Single(catalog.Files);
        using var canceled = new CancellationTokenSource();
        Task<ISvgFontProgramSource?> pending = first.GetSourceAsync(file, canceled.Token).AsTask();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Task<ISvgFontProgramSource?> other = second.GetSourceAsync(file, CancellationToken.None).AsTask();
        canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await pending);
        first.Dispose();
        release.TrySetResult();
        ISvgFontProgramSource source = Assert.IsAssignableFrom<ISvgFontProgramSource>(await other);
        Assert.Equal(font, (await source.GetBytesAsync(CancellationToken.None)).ToArray());
        Assert.Equal(1, programRequests);
        using (var third = resolver.OpenOperation())
        {
            ISvgFontProgramSource reused = Assert.IsAssignableFrom<ISvgFontProgramSource>(await third.GetSourceAsync(file, CancellationToken.None));
            Assert.Equal(font, (await reused.GetBytesAsync(CancellationToken.None)).ToArray());
        }
        Assert.Equal(1, programRequests);
        await resolver.DisposeAsync();
        Assert.True(budget.Reserved >= font.Length);
        Assert.Equal(font, (await source.GetBytesAsync(CancellationToken.None)).ToArray());
        Assert.Throws<ObjectDisposedException>(() => resolver.OpenOperation());
        second.Dispose();
        Assert.Equal(0, budget.Reserved);
        Assert.False(budget.Disposed);
        Assert.Throws<ObjectDisposedException>(() => source.GetBytesAsync(CancellationToken.None));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    [Trait("Layer", "Contracts")]
    public async Task Http_resolver_deadline_covers_headers_and_known_or_chunked_bodies(bool body, bool declaredLength)
    {
        using var handler = new HttpFontHandler(async (_, token) =>
        {
            if (!body) await Task.Delay(Timeout.InfiniteTimeSpan, token);
            var content = new StreamContent(new HttpFontBlockedStream());
            if (declaredLength) content.Headers.ContentLength = 100;
            return new(HttpStatusCode.OK) { Content = content };
        });
        using var client = new HttpClient(handler);
        using var budget = new TestMemoryBudget(1_000_000);
        var options = new SvgHttpFontAcquisitionOptions(TimeSpan.FromMilliseconds(100), 3, TimeSpan.Zero, 4096, 1_000_000, 1_000_000);
        await using var resolver = SvgHttpFontPackResolver.Create("test-pack", new("https://fonts.invalid/pack/"), client, options, budget);
        using var operation = resolver.OpenOperation();
        SvgException failure = await Assert.ThrowsAsync<SvgException>(() => operation.GetCatalogAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(SvgFontFailureStage.Timeout, failure.FontDiagnostic?.Stage);
        Assert.Equal(1, handler.Requests);
        await resolver.DisposeAsync();
        operation.Dispose();
        Assert.Equal(0, budget.Reserved);
    }

    [Fact]
    [Trait("Layer", "Contracts")]
    public async Task Http_resolver_retries_transient_status_but_recovers_from_invalid_manifests_only_on_a_later_call()
    {
        (byte[] manifest, _) = HttpFontFixture("files/example.ttf");
        int requests = 0;
        using var handler = new HttpFontHandler((_, _) =>
        {
            int request = Interlocked.Increment(ref requests);
            return Task.FromResult(request switch
            {
                1 => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
                2 => new(HttpStatusCode.OK) { Content = new ByteArrayContent("{}"u8.ToArray()) },
                _ => new(HttpStatusCode.OK) { Content = new ByteArrayContent(manifest) }
            });
        });
        using var client = new HttpClient(handler);
        using var budget = new TestMemoryBudget(1_000_000);
        await using var resolver = SvgHttpFontPackResolver.Create("test-pack", new("https://fonts.invalid/pack/"), client, HttpFontOptions(), budget);
        using var operation = resolver.OpenOperation();
        SvgException invalid = await Assert.ThrowsAsync<SvgException>(() => operation.GetCatalogAsync(CancellationToken.None));
        Assert.Equal(SvgErrorCode.MalformedInput, invalid.ErrorCode);
        Assert.Equal(SvgFontFailureStage.Format, invalid.FontDiagnostic?.Stage);
        Assert.Equal(2, requests);
        Assert.Equal("test-pack", (await operation.GetCatalogAsync(CancellationToken.None)).PackId);
        Assert.Equal(3, requests);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Layer", "Contracts")]
    public async Task Http_resolver_does_not_cache_or_retry_size_and_integrity_failures(bool wrongSize)
    {
        (byte[] manifest, byte[] font) = HttpFontFixture("files/example.ttf");
        int programs = 0;
        using var handler = new HttpFontHandler((request, _) =>
        {
            if (request.RequestUri?.AbsolutePath.EndsWith("manifest.json", StringComparison.Ordinal) == true)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(manifest) });
            byte[] bytes = font.ToArray();
            if (Interlocked.Increment(ref programs) == 1)
            {
                if (wrongSize) bytes = bytes[..^1]; else bytes[0] ^= 1;
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) });
        });
        using var client = new HttpClient(handler);
        using var budget = new TestMemoryBudget(1_000_000);
        await using var resolver = SvgHttpFontPackResolver.Create("test-pack", new("https://fonts.invalid/pack/"), client, HttpFontOptions(), budget);
        using var operation = resolver.OpenOperation();
        SvgFontPackFile file = Assert.Single((await operation.GetCatalogAsync(CancellationToken.None)).Files);
        SvgException failure = await Assert.ThrowsAsync<SvgException>(async () => await operation.GetSourceAsync(file, CancellationToken.None));
        Assert.Equal(wrongSize ? SvgFontFailureStage.Size : SvgFontFailureStage.Integrity, failure.FontDiagnostic?.Stage);
        Assert.Equal(1, programs);
        ISvgFontProgramSource source = Assert.IsAssignableFrom<ISvgFontProgramSource>(await operation.GetSourceAsync(file, CancellationToken.None));
        Assert.Equal(font, (await source.GetBytesAsync(CancellationToken.None)).ToArray());
        Assert.Equal(2, programs);
    }

    [Fact]
    [Trait("Layer", "Contracts")]
    public async Task Http_resolver_admission_refusal_starts_no_transport_and_does_not_retry()
    {
        (byte[] manifest, _) = HttpFontFixture("files/example.ttf");
        using var handler = new HttpFontHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(manifest) }));
        using var client = new HttpClient(handler);
        using var budget = new TestMemoryBudget(0);
        await using var resolver = SvgHttpFontPackResolver.Create("test-pack", new("https://fonts.invalid/pack/"), client, HttpFontOptions(), budget);
        using var operation = resolver.OpenOperation();
        SvgException failure = await Assert.ThrowsAsync<SvgException>(() => operation.GetCatalogAsync(CancellationToken.None));
        Assert.Equal(SvgErrorCode.LimitExceeded, failure.ErrorCode);
        Assert.Equal(0, handler.Requests);
        Assert.Equal(0, budget.Reserved);
        budget.Limit = 1_000_000;
        Assert.Equal("test-pack", (await operation.GetCatalogAsync(CancellationToken.None)).PackId);
        Assert.Equal(1, handler.Requests);
    }

    private static SvgHttpFontAcquisitionOptions HttpFontOptions()
        => new(TimeSpan.FromSeconds(5), 3, TimeSpan.Zero, 4096, 1_000_000, 1_000_000);

    [Theory]
    [InlineData(404)]
    [InlineData(501)]
    [InlineData(505)]
    [Trait("Layer", "Contracts")]
    public async Task Http_resolver_does_not_retry_deterministic_http_rejection(int status)
    {
        using var handler = new HttpFontHandler((_, _) => Task.FromResult(new HttpResponseMessage((HttpStatusCode)status)));
        using var client = new HttpClient(handler);
        using var budget = new TestMemoryBudget(1_000_000);
        await using var resolver = SvgHttpFontPackResolver.Create("test-pack", new("https://fonts.invalid/pack/"), client, HttpFontOptions(), budget);
        using var operation = resolver.OpenOperation();
        SvgException failure = await Assert.ThrowsAsync<SvgException>(() => operation.GetCatalogAsync(CancellationToken.None));
        Assert.Equal(SvgFontFailureStage.Transport, failure.FontDiagnostic?.Stage);
        Assert.Equal(1, handler.Requests);
        Assert.Equal(0, budget.Reserved);
    }

    [Fact]
    [Trait("Layer", "Contracts")]
    public async Task Http_resolver_rejects_a_reported_redirect_outside_the_root()
    {
        using var handler = new HttpFontHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            RequestMessage = new(HttpMethod.Get, "https://elsewhere.invalid/manifest.json"), Content = new ByteArrayContent("{}"u8.ToArray())
        }));
        using var client = new HttpClient(handler);
        using var budget = new TestMemoryBudget(1_000_000);
        await using var resolver = SvgHttpFontPackResolver.Create("test-pack", new("https://fonts.invalid/pack/"), client, HttpFontOptions(), budget);
        using var operation = resolver.OpenOperation();
        SvgException failure = await Assert.ThrowsAsync<SvgException>(() => operation.GetCatalogAsync(CancellationToken.None));
        Assert.Equal(SvgFontFailureStage.Transport, failure.FontDiagnostic?.Stage);
        Assert.Equal(1, handler.Requests);
        Assert.Equal(0, budget.Reserved);
    }

    [Fact]
    [Trait("Layer", "Contracts")]
    public async Task Http_resolver_canceled_waiters_leave_shared_deadline_cleanup_and_later_program_recovery()
    {
        (byte[] manifest, byte[] font) = HttpFontFixture("files/example.ttf");
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int programs = 0;
        using var handler = new HttpFontHandler(async (request, token) =>
        {
            if (request.RequestUri?.AbsolutePath.EndsWith("manifest.json", StringComparison.Ordinal) == true)
                return new(HttpStatusCode.OK) { Content = new ByteArrayContent(manifest) };
            if (Interlocked.Increment(ref programs) == 1)
            {
                entered.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
            }
            return new(HttpStatusCode.OK) { Content = new ByteArrayContent(font) };
        });
        using var client = new HttpClient(handler);
        using var budget = new TestMemoryBudget(1_000_000);
        var options = new SvgHttpFontAcquisitionOptions(TimeSpan.FromMilliseconds(250), 3, TimeSpan.Zero, 4096, 1_000_000, 1_000_000);
        await using var resolver = SvgHttpFontPackResolver.Create("test-pack", new("https://fonts.invalid/pack/"), client, options, budget);
        using var first = resolver.OpenOperation();
        SvgFontPackFile file = Assert.Single((await first.GetCatalogAsync(CancellationToken.None)).Files);
        long metadataBytes = budget.Reserved;
        using var canceled = new CancellationTokenSource();
        Task<ISvgFontProgramSource?> pending = first.GetSourceAsync(file, canceled.Token).AsTask();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await pending);
        first.Dispose();
        using var observer = resolver.OpenOperation();
        SvgException timeout = await Assert.ThrowsAsync<SvgException>(async () => await observer.GetSourceAsync(file, CancellationToken.None));
        Assert.Equal(SvgFontFailureStage.Timeout, timeout.FontDiagnostic?.Stage);
        Assert.Equal(metadataBytes, budget.Reserved);
        ISvgFontProgramSource recovered = Assert.IsAssignableFrom<ISvgFontProgramSource>(await observer.GetSourceAsync(file, CancellationToken.None));
        Assert.Equal(font, (await recovered.GetBytesAsync(CancellationToken.None)).ToArray());
        Assert.Equal(2, programs);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [Trait("Layer", "Contracts")]
    public async Task Http_resolver_bounds_actual_body_bytes_independently_of_declared_length(int declaration)
    {
        var stream = new HttpFontChunkedStream(new byte[8192]);
        using var handler = new HttpFontHandler((_, _) =>
        {
            var content = new StreamContent(stream);
            if (declaration != 0) content.Headers.ContentLength = declaration == 1 ? 1 : 8192;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        });
        using var client = new HttpClient(handler);
        using var budget = new TestMemoryBudget(1_000_000);
        await using var resolver = SvgHttpFontPackResolver.Create("test-pack", new("https://fonts.invalid/pack/"), client, HttpFontOptions(), budget);
        using var operation = resolver.OpenOperation();
        SvgException failure = await Assert.ThrowsAsync<SvgException>(() => operation.GetCatalogAsync(CancellationToken.None));
        Assert.Equal(SvgFontFailureStage.Size, failure.FontDiagnostic?.Stage);
        Assert.Equal(1, handler.Requests);
        Assert.Equal(0, budget.Reserved);
        Assert.False(stream.CanRead);
    }

    [Theory]
    [InlineData("files/regular%20face.ttf", true)]
    [InlineData("files/rate%25.ttf", true)]
    [InlineData("files/%252e%252e/escape.ttf", false)]
    [InlineData("files/%252fescape.ttf", false)]
    [InlineData("files/%255cescape.ttf", false)]
    [Trait("Layer", "Mechanisms")]
    public async Task Http_resolver_keeps_encoded_filenames_but_rejects_nested_traversal_and_separators(string path, bool accepted)
    {
        (byte[] manifest, byte[] font) = HttpFontFixture(path);
        using var handler = new HttpFontHandler((request, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(request.RequestUri?.AbsolutePath.EndsWith("manifest.json", StringComparison.Ordinal) == true ? manifest : font)
        }));
        using var client = new HttpClient(handler);
        using var budget = new TestMemoryBudget(1_000_000);
        await using var resolver = SvgHttpFontPackResolver.Create("test-pack", new("https://fonts.invalid/pack/"), client, HttpFontOptions(), budget);
        using var operation = resolver.OpenOperation();
        SvgFontPackFile file = Assert.Single((await operation.GetCatalogAsync(CancellationToken.None)).Files);
        if (accepted)
        {
            ISvgFontProgramSource source = Assert.IsAssignableFrom<ISvgFontProgramSource>(await operation.GetSourceAsync(file, CancellationToken.None));
            Assert.Equal(font, (await source.GetBytesAsync(CancellationToken.None)).ToArray());
            Assert.Equal(2, handler.Requests);
        }
        else
        {
            SvgException failure = await Assert.ThrowsAsync<SvgException>(async () => await operation.GetSourceAsync(file, CancellationToken.None));
            Assert.Equal(SvgFontFailureStage.Format, failure.FontDiagnostic?.Stage);
            Assert.Equal(1, handler.Requests);
        }
    }

    [Fact]
    [Trait("Layer", "Contracts")]
    public async Task Http_resolver_zero_idle_capacity_releases_programs_only_after_the_last_borrower()
    {
        (byte[] manifest, byte[] font) = HttpFontFixture("files/example.ttf");
        using var handler = new HttpFontHandler((request, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(request.RequestUri?.AbsolutePath.EndsWith("manifest.json", StringComparison.Ordinal) == true ? manifest : font)
        }));
        using var client = new HttpClient(handler);
        using var budget = new TestMemoryBudget(1_000_000);
        var options = new SvgHttpFontAcquisitionOptions(TimeSpan.FromSeconds(5), 3, TimeSpan.Zero, 4096, 1_000_000, 0);
        await using var resolver = SvgHttpFontPackResolver.Create("test-pack", new("https://fonts.invalid/pack/"), client, options, budget);
        using var first = resolver.OpenOperation(); using var second = resolver.OpenOperation();
        SvgFontPackFile file = Assert.Single((await first.GetCatalogAsync(CancellationToken.None)).Files);
        long catalogBytes = budget.Reserved;
        _ = await first.GetSourceAsync(file, CancellationToken.None);
        ISvgFontProgramSource source = Assert.IsAssignableFrom<ISvgFontProgramSource>(await second.GetSourceAsync(file, CancellationToken.None));
        Assert.Equal(2, handler.Requests);
        first.Dispose();
        Assert.Equal(font, (await source.GetBytesAsync(CancellationToken.None)).ToArray());
        Assert.True(budget.Reserved > catalogBytes);
        second.Dispose();
        Assert.Equal(catalogBytes, budget.Reserved);
        using var third = resolver.OpenOperation();
        _ = await third.GetSourceAsync(file, CancellationToken.None);
        Assert.Equal(3, handler.Requests);
    }

    private static (byte[] Manifest, byte[] Font) HttpFontFixture(string path)
    {
        byte[] font = CreateTriangleFont();
        byte[] manifest = JsonSerializer.SerializeToUtf8Bytes(new
        {
            packId = "test-pack",
            files = new[] { new { relativePath = path, byteSize = font.Length, sha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(font)) } },
            families = new[] { new { requestedFamily = "Example", resolvedFamily = "Example", relativeFontFile = path } }
        });
        return (manifest, font);
    }

    private sealed class HttpFontHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        private int _requests;
        internal int Requests => Volatile.Read(ref _requests);
        /// <summary>Borrows the request until completion; the client owns each returned response and cancellation token.</summary>
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _requests);
            return respond(request, cancellationToken);
        }
    }

    private sealed class HttpFontBlockedStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        /// <summary>Completes only through caller cancellation; it neither retains nor writes the borrowed buffer.</summary>
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class HttpFontChunkedStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override bool CanSeek => false;
    }


}
