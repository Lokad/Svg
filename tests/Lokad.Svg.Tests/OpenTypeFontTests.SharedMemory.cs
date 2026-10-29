using Lokad.Svg.Fonts;
using Lokad.Svg.Testing;

namespace Lokad.Svg.Tests;

public sealed partial class OpenTypeFontTests
{
    public enum GovernedDestination { Owned, Memory, Stream }

    public static IEnumerable<object[]> GovernedOutputCases()
    {
        foreach (bool prepared in new[] { false, true })
        foreach (bool asynchronous in new[] { false, true })
        foreach (GovernedDestination destination in Enum.GetValues<GovernedDestination>())
            yield return [prepared, asynchronous, destination];
    }

    [Theory]
    [Trait("Layer", "Contracts")]
    [MemberData(nameof(GovernedOutputCases))]
    public async Task Governed_output_paths_share_the_host_and_keep_destination_ownership(bool prepared, bool asynchronous, GovernedDestination destination)
    {
        byte[] input = "<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><path d='M1 7Q4 0 7 7Z' fill='red' fill-opacity='.5'/></svg>"u8.ToArray();
        byte[] expected = SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None).ToArray();
        using TestMemoryBudget host = new(2_000_000);
        SvgRenderOptions options = SvgRenderOptions.Default.WithMemoryBudget(host);
        (var catalog, var provider, _) = CreateExamplePack(CreateTriangleFont());
        using SvgPreparedDocument? document = prepared
            ? await SvgPreparedDocument.CreateAsync(input, catalog, provider, options, CancellationToken.None) : null;
        if (prepared) Assert.True(host.Reserved > 0);
        byte[] actual;
        if (destination == GovernedDestination.Owned)
        {
            using SvgPngResult result = document is null
                ? asynchronous ? await SvgConverter.ConvertToOwnedPngAsync(input, options, CancellationToken.None) : SvgConverter.ConvertToOwnedPng(input, options, CancellationToken.None)
                : asynchronous ? await document.ConvertToOwnedPngAsync(CancellationToken.None) : document.ConvertToOwnedPng(CancellationToken.None);
            actual = result.Memory.ToArray();
            Assert.Equal(result.Memory.Length, host.Reserved);
        }
        else if (destination == GovernedDestination.Memory)
        {
            byte[] memory = Enumerable.Repeat((byte)0xA5, expected.Length + 13).ToArray();
            int count = document is null
                ? asynchronous ? await SvgConverter.ConvertToPngAsync(input, memory, options, CancellationToken.None) : SvgConverter.ConvertToPng(input, memory, options, CancellationToken.None)
                : asynchronous ? await document.ConvertToPngAsync(memory, CancellationToken.None) : document.ConvertToPng(memory, CancellationToken.None);
            actual = memory[..count];
            Assert.All(memory.AsSpan(count).ToArray(), value => Assert.Equal((byte)0xA5, value));
        }
        else
        {
            using MemoryStream stream = new();
            int count = document is null
                ? asynchronous ? await SvgConverter.ConvertToPngAsync(input, stream, options, CancellationToken.None) : SvgConverter.ConvertToPng(input, stream, options, CancellationToken.None)
                : asynchronous ? await document.ConvertToPngAsync(stream, CancellationToken.None) : document.ConvertToPng(stream, CancellationToken.None);
            actual = stream.ToArray();
            Assert.Equal(count, actual.Length);
            Assert.True(stream.CanWrite);
        }
        Assert.Equal(expected, actual);
        Assert.Equal(0, host.Reserved);
        Assert.False(host.Disposed);
        Assert.Equal(0, provider.RequestCount);
    }

    [Theory]
    [Trait("Layer", "Contracts")]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Governed_bare_memory_rejection_leaves_the_operation_usable(bool prepared, bool asynchronous)
    {
        byte[] input = "<svg xmlns='http://www.w3.org/2000/svg' width='1' height='1'/>"u8.ToArray();
        using TestMemoryBudget host = new(2_000_000);
        SvgRenderOptions options = SvgRenderOptions.Default.WithMemoryBudget(host);
        (var catalog, var provider, _) = CreateExamplePack(CreateTriangleFont());
        using SvgPreparedDocument? document = prepared ? await SvgPreparedDocument.CreateAsync(input, catalog, provider, options, CancellationToken.None) : null;
        long retained = host.Reserved;
        if (asynchronous)
            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                if (document is null) await SvgConverter.ConvertToPngAsync(input, options, CancellationToken.None);
                else await document.ConvertToPngAsync(CancellationToken.None);
            });
        else
            Assert.Throws<InvalidOperationException>(() =>
            {
                if (document is null) SvgConverter.ConvertToPng(input, options, CancellationToken.None);
                else document.ConvertToPng(CancellationToken.None);
            });
        Assert.Equal(retained, host.Reserved);
        using (SvgPngResult result = document is null ? SvgConverter.ConvertToOwnedPng(input, options, CancellationToken.None) : document.ConvertToOwnedPng(CancellationToken.None))
            Assert.Equal(result.Memory.Length, host.Reserved);
        Assert.Equal(0, host.Reserved);
    }

    [Fact]
    [Trait("Layer", "Contracts")]
    public void Inspection_borrows_the_root_and_releases_refused_or_successful_workspace()
    {
        byte[] input = "<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'/>"u8.ToArray();
        using TestMemoryBudget host = new(1);
        SvgRenderOptions options = SvgRenderOptions.Default.WithMemoryBudget(host)
            .WithFontSelectionPolicy(new SvgFontSelectionPolicy(new Dictionary<string, string>(), []));
        Assert.Same(host, options.MemoryBudget);
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgConverter.InspectDimensions(input, options, CancellationToken.None)).ErrorCode);
        Assert.Equal(0, host.Reserved);
        host.Limit = 2_000_000;
        _ = SvgConverter.InspectDimensions(input, options, CancellationToken.None);
        Assert.Equal(0, host.Reserved);
        Assert.False(host.Disposed);
    }

    [Fact]
    [Trait("Layer", "Contracts")]
    public async Task Http_document_and_output_use_the_active_host_scope_while_cache_uses_the_root()
    {
        (byte[] manifest, byte[] font) = HttpFontFixture("files/example.ttf");
        using var handler = new HttpFontHandler((request, _) => Task.FromResult(new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(request.RequestUri?.AbsolutePath.EndsWith("manifest.json", StringComparison.Ordinal) == true ? manifest : font)
        }));
        using HttpClient client = new(handler);
        using TestMemoryBudget host = new(2_000_000);
        using TestMemoryBudget invocation = Assert.IsType<TestMemoryBudget>(host.CreateScope());
        await using SvgHttpFontPackResolver resolver = SvgHttpFontPackResolver.Create("test-pack", new Uri("https://fonts.test/pack/"), client, HttpFontOptions(), host);
        SvgRenderOptions options = SvgRenderOptions.Default.WithMemoryBudget(invocation);
        byte[] svg = "<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><text font-family='Example' font-size='100' y='4'>A</text></svg>"u8.ToArray();
        using SvgPreparedDocument document = await SvgPreparedDocument.CreateAsync(svg, resolver, options, CancellationToken.None);
        Assert.Equal(1, invocation.ActiveScopeCount);
        using (SvgPngResult result = document.ConvertToOwnedPng(CancellationToken.None))
        {
            Assert.Equal(1, invocation.ActiveScopeCount);
            Assert.True(host.Reserved > result.Memory.Length);
        }
        Assert.Equal(0, invocation.ActiveScopeCount);
        invocation.Dispose();
        Assert.True(host.Reserved > 0);
        await resolver.DisposeAsync();
        Assert.Equal(0, host.Reserved);
        Assert.False(host.Disposed);
    }

    [Fact]
    [Trait("Layer", "Contracts")]
    public void Retained_png_charge_competes_with_later_conversion_and_reuses_immediately()
    {
        byte[] input = "<svg xmlns='http://www.w3.org/2000/svg' width='4' height='4'><rect width='4' height='4' fill='red'/></svg>"u8.ToArray();
        using TestMemoryBudget host = new(2_000_000);
        SvgRenderOptions options = SvgRenderOptions.Default.WithMemoryBudget(host);
        using SvgPngResult first = SvgConverter.ConvertToOwnedPng(input, options, CancellationToken.None);
        byte[] expected = first.Memory.ToArray();
        long retained = host.Reserved;
        Assert.Equal(first.Memory.Length, retained);
        host.Limit = retained;
        SvgException refusal = Assert.Throws<SvgException>(() => SvgConverter.ConvertToOwnedPng(input, options, CancellationToken.None));
        Assert.Equal(SvgErrorCode.LimitExceeded, refusal.ErrorCode);
        Assert.Equal(retained, host.Reserved);
        Assert.Equal(expected, first.Memory.ToArray());
        host.Limit = 2_000_000;
        using (SvgPngResult second = SvgConverter.ConvertToOwnedPng(input, options, CancellationToken.None))
        {
            Assert.Equal(expected, second.Memory.ToArray());
            Assert.Equal(retained + second.Memory.Length, host.Reserved);
        }
        first.Dispose();
        first.Dispose();
        Assert.Equal(0, host.Reserved);
        Assert.Throws<ObjectDisposedException>(() => first.Memory);
        Assert.False(host.Disposed);
    }

    [Fact]
    [Trait("Layer", "Contracts")]
    public async Task Snapshot_metadata_stays_charged_while_caller_program_bytes_remain_borrowed()
    {
        byte[] original = CreateTriangleFont();
        byte[] program = new byte[4 * 1024 * 1024];
        original.CopyTo(program, 0);
        (var catalog, var provider, _) = CreateExamplePack(program);
        using TestMemoryBudget host = new(512_000);
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync(catalog,
            [new SvgFontFaceRequest("Example", 400, false)], provider, host, CancellationToken.None);
        long metadata = host.Reserved;
        Assert.InRange(metadata, 1, 511_999);
        Assert.Equal(program.Length, Assert.Single(snapshot.Fonts).Bytes.Length);
        SvgRenderOptions options = new SvgRenderOptions(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, snapshot).WithMemoryBudget(host);
        byte[] input = "<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><text font-family='Example' font-size='100' y='4'>A</text></svg>"u8.ToArray();
        using (SvgPngResult result = SvgConverter.ConvertToOwnedPng(input, options, CancellationToken.None))
            Assert.Equal(metadata + result.Memory.Length, host.Reserved);
        Assert.Equal(metadata, host.Reserved);
        snapshot.Dispose();
        Assert.Equal(0, host.Reserved);
        Assert.False(host.Disposed);
    }

    [Fact]
    [Trait("Layer", "Contracts")]
    public async Task Cancelled_font_acquisition_drains_borrowers_before_refunding_discovery_storage()
    {
        (var catalog, var provider, _) = CreateExamplePack(CreateTriangleFont());
        using TestMemoryBudget host = new(2_000_000);
        using CancellationTokenSource cancellation = new();
        TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        HoldingFontProvider held = new(provider, entered, release);
        SvgRenderOptions options = SvgRenderOptions.Default.WithMemoryBudget(host);
        byte[] svg = "<svg xmlns='http://www.w3.org/2000/svg'><text font-family='Example'>A</text></svg>"u8.ToArray();
        Task<SvgFontSnapshot> acquisition = SvgFontSnapshot.CreateAsync(svg, catalog, held, options, cancellation.Token).AsTask();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(30));
        long active = host.Reserved;
        Assert.True(active > 0);
        cancellation.Cancel();
        OperationCanceledException error = await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await acquisition);
        Assert.Equal(cancellation.Token, error.CancellationToken);
        Assert.Equal(0, host.Reserved);
        using (SvgFontSnapshot retry = await SvgFontSnapshot.CreateAsync(svg, catalog, provider, options, CancellationToken.None))
        {
            Assert.Single(retry.Fonts);
            Assert.True(host.Reserved > 0);
            Assert.True(host.Reserved < active);
        }
        Assert.Equal(0, host.Reserved);
    }

    [Fact]
    [Trait("Layer", "Contracts")]
    public async Task Overlapping_async_output_keeps_its_owner_until_write_failure_finishes()
    {
        using TestMemoryBudget host = new(2_000_000);
        TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using HeldFailingStream output = new(entered, release);
        SvgRenderOptions options = SvgRenderOptions.Default.WithMemoryBudget(host);
        byte[] svg = "<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><rect width='8' height='8'/></svg>"u8.ToArray();
        Task<int> conversion = SvgConverter.ConvertToPngAsync(svg, output, options, CancellationToken.None);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(30));
        long active = host.Reserved;
        try
        {
            Assert.True(active > 0);
            host.Limit = active;
            Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgConverter.ConvertToOwnedPng(svg, options, CancellationToken.None)).ErrorCode);
            Assert.Equal(active, host.Reserved);
        }
        finally { release.TrySetResult(); }
        await Assert.ThrowsAsync<IOException>(async () => await conversion);
        Assert.Equal(0, host.Reserved);
        Assert.True(output.CanWrite);
        host.Limit = 2_000_000;
        using (SvgPngResult retry = SvgConverter.ConvertToOwnedPng(svg, options, CancellationToken.None)) Assert.Equal(retry.Memory.Length, host.Reserved);
        Assert.Equal(0, host.Reserved);
    }

    [Fact]
    [Trait("Layer", "Contracts")]
    public async Task Cancelled_font_byte_read_keeps_charges_until_read_completion()
    {
        byte[] program = CreateTriangleFont();
        (var catalog, _, _) = CreateExamplePack(program);
        using TestMemoryBudget host = new(2_000_000);
        using CancellationTokenSource cancellation = new();
        TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        CountingFontProvider provider = new(new HoldingBytesSource(program, entered, release));
        byte[] svg = "<svg xmlns='http://www.w3.org/2000/svg'><text font-family='Example'>A</text></svg>"u8.ToArray();
        Task<SvgFontSnapshot> acquisition = SvgFontSnapshot.CreateAsync(svg, catalog, provider,
            SvgRenderOptions.Default.WithMemoryBudget(host), cancellation.Token).AsTask();
        Exception? failure = null;
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(30));
            long live = host.Reserved;
            Assert.True(live > 0);
            cancellation.Cancel();
            Assert.False(acquisition.IsCompleted);
            Assert.Equal(live, host.Reserved);
        }
        finally
        {
            cancellation.Cancel();
            release.TrySetResult();
            try { using SvgFontSnapshot unexpected = await acquisition; }
            catch (Exception exception) { failure = exception; }
        }
        OperationCanceledException error = Assert.IsAssignableFrom<OperationCanceledException>(failure);
        Assert.Equal(cancellation.Token, error.CancellationToken);
        Assert.Equal(0, host.Reserved);
        Assert.False(host.Disposed);
    }

    private sealed class HoldingBytesSource(byte[] bytes, TaskCompletionSource entered, TaskCompletionSource release) : ISvgFontProgramSource
    {
        /// <summary>Gets a stable test identity without owning a cache or transferring program bytes.</summary>
        public string StableId => "held-test-read";

        /// <summary>Borrows immutable caller bytes until one controlled read completes, then observes cancellation. Serialized use; the test releases and drains the read before disposing owners.</summary>
        public async ValueTask<ReadOnlyMemory<byte>> GetBytesAsync(CancellationToken cancellationToken)
        {
            entered.TrySetResult();
            await release.Task;
            cancellationToken.ThrowIfCancellationRequested();
            return bytes;
        }
    }

    private sealed class HoldingFontProvider(ISvgFontProgramSourceProvider inner, TaskCompletionSource entered, TaskCompletionSource release) : ISvgFontProgramSourceProvider
    {
        /// <summary>Borrows provider/program storage, pauses one asynchronous request, and honors its cancellation before forwarding; one test owns all synchronization and finishes the borrower before disposal.</summary>
        public async ValueTask<ISvgFontProgramSource?> GetSourceAsync(SvgFontPackFile file, CancellationToken cancellationToken)
        {
            entered.TrySetResult();
            await release.Task.WaitAsync(cancellationToken);
            return await inner.GetSourceAsync(file, cancellationToken);
        }
    }

    private sealed class HeldFailingStream(TaskCompletionSource entered, TaskCompletionSource release) : MemoryStream
    {
        /// <summary>Borrows the caller's scratch until the awaited write ends, then faults; serialized use only, caller-owned stream and synchronization remain open.</summary>
        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
        {
            entered.TrySetResult();
            await release.Task.WaitAsync(cancellationToken);
            throw new IOException("The test output failed after awaiting its borrowed buffer.");
        }
    }
}
