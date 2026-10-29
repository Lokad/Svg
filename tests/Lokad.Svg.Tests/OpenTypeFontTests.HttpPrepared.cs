using System.Net;
using Lokad.Svg.Fonts;
using Lokad.Svg.Testing;
using Lokad.Svg.Parsing;

namespace Lokad.Svg.Tests;

public sealed partial class OpenTypeFontTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Layer", "Contracts")]
    public async Task Http_preparation_failures_retain_acquisition_stage_font_request_and_source(bool manifestFailure)
    {
        (byte[] manifest, byte[] font) = HttpFontFixture("files/example.ttf");
        using var handler = new HttpFontHandler((request, _) =>
        {
            bool isManifest = request.RequestUri?.AbsolutePath.EndsWith("manifest.json", StringComparison.Ordinal) == true;
            if (manifestFailure) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            byte[] body = isManifest ? manifest : font.ToArray();
            if (!isManifest) body[0] ^= 1;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) });
        });
        using var client = new HttpClient(handler);
        using var budget = new TestMemoryBudget(8_000_000);
        await using var resolver = SvgHttpFontPackResolver.Create("test-pack", new("https://fonts.invalid/pack/"), client, HttpFontOptions(), budget);
        byte[] svg = "<svg xmlns='http://www.w3.org/2000/svg'><text id='label' font-family='Example'>A</text></svg>"u8.ToArray();
        SvgException failure = await Assert.ThrowsAsync<SvgException>(async () => await SvgPreparedDocument.CreateAsync(svg, resolver, SvgRenderOptions.Default, CancellationToken.None));
        Assert.Equal(manifestFailure ? SvgFontFailureStage.Transport : SvgFontFailureStage.Integrity, failure.FontDiagnostic?.Stage);
        Assert.Equal("Example", failure.FontDiagnostic?.RequestedFamily);
        Assert.Equal("label", failure.SourceDiagnostic?.ElementId);
        Assert.Equal(1, failure.SourceDiagnostic?.LineNumber);
        Assert.Equal(manifestFailure ? 1 : 2, handler.Requests);
        await resolver.DisposeAsync();
        Assert.Equal(0, budget.Reserved);
    }

    [Theory]
    [InlineData("sans-serif")]
    [InlineData("Absent, Example")]
    [InlineData("Absent")]
    [Trait("Layer", "Rendering")]
    public async Task Http_preparation_preserves_explicit_generic_ordered_family_and_fallback_policy(string family)
    {
        (byte[] manifest, byte[] font) = HttpFontFixture("files/example.ttf");
        using var handler = new HttpFontHandler((request, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(request.RequestUri?.AbsolutePath.EndsWith("manifest.json", StringComparison.Ordinal) == true ? manifest : font)
        }));
        using var client = new HttpClient(handler);
        using var budget = new TestMemoryBudget(8_000_000);
        await using var resolver = SvgHttpFontPackResolver.Create("test-pack", new("https://fonts.invalid/pack/"), client, HttpFontOptions(), budget);
        var policy = new SvgFontSelectionPolicy(new Dictionary<string, string> { ["sans-serif"] = "Example" }, ["Example"]);
        SvgRenderOptions options = SvgRenderOptions.Default.WithFontSelectionPolicy(policy);
        byte[] svg = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><text font-family='" + family + "' font-size='100' y='3'>A</text></svg>");
        (SvgFontPackCatalog catalog, CountingFontProvider provider, _) = CreateExamplePack(font);
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync(svg, catalog, provider, policy, options.Limits, CancellationToken.None);
        byte[] expected = SvgConverter.ConvertToPng(svg, new SvgRenderOptions(null, 1, SvgColor.Transparent, options.Limits, snapshot), CancellationToken.None).ToArray();
        using SvgPreparedDocument document = await SvgPreparedDocument.CreateAsync(svg, resolver, options, CancellationToken.None);
        using SvgPngResult result = document.ConvertToOwnedPng(CancellationToken.None);
        Assert.Equal(expected, result.Memory.ToArray());
        Assert.Equal(2, handler.Requests);
    }

    [Fact]
    [Trait("Layer", "Contracts")]
    public async Task Http_owned_async_cancellation_releases_execution_without_creating_a_result()
    {
        using var handler = new HttpFontHandler((_, _) => throw new InvalidOperationException("Font-free conversion must not acquire resources."));
        using var client = new HttpClient(handler);
        using var budget = new TestMemoryBudget(8_000_000);
        await using var resolver = SvgHttpFontPackResolver.Create("test-pack", new("https://fonts.invalid/pack/"), client, HttpFontOptions(), budget);
        byte[] svg = "<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><rect width='5' height='5'/></svg>"u8.ToArray();
        using SvgPreparedDocument document = await SvgPreparedDocument.CreateAsync(svg, resolver, SvgRenderOptions.Default, CancellationToken.None);
        Assert.True(budget.Reserved > 0);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => document.ConvertToOwnedPngAsync(cancellation.Token));
        Assert.Equal(0, budget.Reserved);
        Assert.Equal(0, handler.Requests);
    }

    [Fact]
    [Trait("Layer", "Contracts")]
    public async Task Http_governed_async_stream_keeps_scratch_until_the_write_and_its_failure_finish()
    {
        using var handler = new HttpFontHandler((_, _) => throw new InvalidOperationException("Font-free conversion must not acquire resources."));
        using var client = new HttpClient(handler);
        using var budget = new TestMemoryBudget(8_000_000);
        await using var resolver = SvgHttpFontPackResolver.Create("test-pack", new("https://fonts.invalid/pack/"), client, HttpFontOptions(), budget);
        byte[] svg = "<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><rect width='5' height='5'/></svg>"u8.ToArray();
        using SvgPreparedDocument document = await SvgPreparedDocument.CreateAsync(svg, resolver, SvgRenderOptions.Default, CancellationToken.None);
        using var stream = new HttpDelayedFailureStream();
        Task<int> conversion = document.ConvertToPngAsync(stream, CancellationToken.None);
        await stream.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(budget.Reserved > 0);
        stream.Finish.TrySetResult();
        await Assert.ThrowsAsync<IOException>(() => conversion);
        Assert.Equal(0, budget.Reserved);
        Assert.True(stream.CanWrite);
        Assert.Equal(0, handler.Requests);
    }

    private sealed class HttpDelayedFailureStream : MemoryStream
    {
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Finish { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        /// <summary>Borrows the buffer until the controlled write finishes, observing cancellation without disposing caller storage.</summary>
        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
        {
            Entered.TrySetResult();
            await Finish.Task.WaitAsync(cancellationToken);
            throw new IOException("Controlled destination failure.");
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Layer", "Contracts")]
    public async Task Http_prepared_documents_prepare_once_and_keep_each_owned_result_charged(bool asynchronous)
    {
        (byte[] manifest, byte[] font) = HttpFontFixture("files/example.ttf");
        using var handler = new HttpFontHandler((request, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(request.RequestUri?.AbsolutePath.EndsWith("manifest.json", StringComparison.Ordinal) == true ? manifest : font)
        }));
        using var client = new HttpClient(handler);
        using var budget = new TestMemoryBudget(8_000_000);
        await using var resolver = SvgHttpFontPackResolver.Create("test-pack", new("https://fonts.invalid/pack/"), client, HttpFontOptions(), budget);
        string shapes = string.Concat(Enumerable.Repeat("<rect width='1' height='1' fill='blue'/>", 200));
        byte[] svg = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'>" + shapes
            + "<defs><pattern id='tile' width='5' height='5' patternUnits='userSpaceOnUse'><text class='label' y='3'>A</text></pattern></defs>"
            + "<rect width='5' height='5' fill='url(#tile)'/><text class='label' y='3'>A</text><style>.label { font-family: Example; font-size: 100; }</style></svg>");
        (SvgFontPackCatalog catalog, CountingFontProvider provider, _) = CreateExamplePack(font);
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync(svg, catalog, provider, CancellationToken.None);
        byte[] expected = SvgConverter.ConvertToPng(svg, new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, snapshot), CancellationToken.None).ToArray();
        var phases = new List<SvgPreparationPhase>();
        using IDisposable observation = SvgPreparationProbe.Arm(phases.Add);
        using SvgPreparedDocument first = await SvgPreparedDocument.CreateAsync(svg, resolver, SvgRenderOptions.Default, CancellationToken.None);
        Assert.Throws<InvalidOperationException>(() => first.ConvertToPng(CancellationToken.None));
        using SvgPngResult firstResult = asynchronous ? await first.ConvertToOwnedPngAsync(CancellationToken.None) : first.ConvertToOwnedPng(CancellationToken.None);
        Assert.Equal(expected, firstResult.Memory.ToArray());
        using SvgPreparedDocument second = await SvgPreparedDocument.CreateAsync(svg, resolver, SvgRenderOptions.Default, CancellationToken.None);
        using SvgPngResult secondResult = second.ConvertToOwnedPng(CancellationToken.None);
        Assert.Equal(expected, secondResult.Memory.ToArray());
        Assert.Equal(2, handler.Requests);
        Assert.Equal(2, phases.Count(phase => phase == SvgPreparationPhase.Workspace));
        Assert.Equal(2, phases.Count(phase => phase == SvgPreparationPhase.StyleSheets));
        Assert.Equal(2, phases.Count(phase => phase == SvgPreparationPhase.FontIndex));
        Assert.Equal(2, phases.Count(phase => phase == SvgPreparationPhase.PaintIndex));
        Assert.Equal(2, phases.Count(phase => phase == SvgPreparationPhase.Scene));
        first.Dispose(); second.Dispose();
        await resolver.DisposeAsync();
        Assert.Equal(firstResult.Memory.Length + secondResult.Memory.Length, budget.Reserved);
        int secondBytes = secondResult.Memory.Length;
        firstResult.Dispose();
        Assert.Equal(secondBytes, budget.Reserved);
        Assert.Throws<ObjectDisposedException>(() => firstResult.Memory);
        secondResult.Dispose();
        Assert.Equal(0, budget.Reserved);
        Assert.False(budget.Disposed);
    }

    [Fact]
    [Trait("Layer", "Contracts")]
    public async Task Http_font_free_preparation_is_independent_of_cold_warm_and_stalled_initialization()
    {
        (byte[] manifest, byte[] font) = HttpFontFixture("files/example.ttf");
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new HttpFontHandler(async (request, token) =>
        {
            if (request.RequestUri?.AbsolutePath.EndsWith("manifest.json", StringComparison.Ordinal) == true)
            {
                entered.TrySetResult();
                await release.Task.WaitAsync(token);
                return new(HttpStatusCode.OK) { Content = new ByteArrayContent(manifest) };
            }
            return new(HttpStatusCode.OK) { Content = new ByteArrayContent(font) };
        });
        using var client = new HttpClient(handler);
        using var budget = new TestMemoryBudget(8_000_000);
        await using var resolver = SvgHttpFontPackResolver.Create("test-pack", new("https://fonts.invalid/pack/"), client, HttpFontOptions(), budget);
        byte[] shape = "<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><rect width='5' height='5' fill='blue'/></svg>"u8.ToArray();
        byte[] expected = SvgConverter.ConvertToPng(shape, SvgRenderOptions.Default, CancellationToken.None).ToArray();
        using (SvgPreparedDocument cold = await SvgPreparedDocument.CreateAsync(shape, resolver, SvgRenderOptions.Default, CancellationToken.None))
        using (SvgPngResult result = cold.ConvertToOwnedPng(CancellationToken.None)) Assert.Equal(expected, result.Memory.ToArray());
        Assert.Equal(0, handler.Requests);
        byte[] text = "<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><text font-family='Example' font-size='100' y='3'>A</text></svg>"u8.ToArray();
        using var cancellation = new CancellationTokenSource();
        Task<SvgPreparedDocument> pending = SvgPreparedDocument.CreateAsync(text, resolver, SvgRenderOptions.Default, cancellation.Token).AsTask();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        foreach (string inert in new[] { "", "<text display='none' font-family='Example'>A</text>", "<defs><text id='unused' font-family='Example'>A</text></defs>" })
        {
            byte[] input = System.Text.Encoding.UTF8.GetBytes(System.Text.Encoding.UTF8.GetString(shape).Replace("</svg>", inert + "</svg>", StringComparison.Ordinal));
            using SvgPreparedDocument free = await SvgPreparedDocument.CreateAsync(input, resolver, SvgRenderOptions.Default, CancellationToken.None).AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            using SvgPngResult result = free.ConvertToOwnedPng(CancellationToken.None);
            Assert.Equal(expected, result.Memory.ToArray());
            Assert.Equal(1, handler.Requests);
        }
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await pending);
        release.TrySetResult();
        using (SvgPreparedDocument live = await SvgPreparedDocument.CreateAsync(text, resolver, SvgRenderOptions.Default, CancellationToken.None))
        using (SvgPngResult result = live.ConvertToOwnedPng(CancellationToken.None)) Assert.False(result.Memory.IsEmpty);
        using (SvgPreparedDocument warm = await SvgPreparedDocument.CreateAsync(shape, resolver, SvgRenderOptions.Default, CancellationToken.None))
        using (SvgPngResult result = warm.ConvertToOwnedPng(CancellationToken.None)) Assert.Equal(expected, result.Memory.ToArray());
        Assert.Equal(2, handler.Requests);
        await resolver.DisposeAsync();
        Assert.Equal(0, budget.Reserved);
    }

    [Fact]
    [Trait("Layer", "Contracts")]
    public async Task Http_governed_conversion_refusal_releases_execution_and_allows_later_retry()
    {
        (byte[] manifest, byte[] font) = HttpFontFixture("files/example.ttf");
        using var handler = new HttpFontHandler((request, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(request.RequestUri?.AbsolutePath.EndsWith("manifest.json", StringComparison.Ordinal) == true ? manifest : font)
        }));
        using var client = new HttpClient(handler);
        using var budget = new TestMemoryBudget(8_000_000);
        await using var resolver = SvgHttpFontPackResolver.Create("test-pack", new("https://fonts.invalid/pack/"), client, HttpFontOptions(), budget);
        byte[] text = "<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><text font-family='Example' font-size='100' y='3'>A</text></svg>"u8.ToArray();
        using SvgPreparedDocument refused = await SvgPreparedDocument.CreateAsync(text, resolver, SvgRenderOptions.Default, CancellationToken.None);
        budget.Limit = budget.Reserved;
        SvgException failure = Assert.Throws<SvgException>(() => refused.ConvertToOwnedPng(CancellationToken.None));
        Assert.Equal(SvgErrorCode.LimitExceeded, failure.ErrorCode);
        budget.Limit = 8_000_000;
        using SvgPreparedDocument retry = await SvgPreparedDocument.CreateAsync(text, resolver, SvgRenderOptions.Default, CancellationToken.None);
        using SvgPngResult result = await retry.ConvertToOwnedPngAsync(CancellationToken.None);
        Assert.False(result.Memory.IsEmpty);
        Assert.Equal(2, handler.Requests);
        await resolver.DisposeAsync();
        Assert.Equal(result.Memory.Length, budget.Reserved);
        result.Dispose();
        Assert.Equal(0, budget.Reserved);
    }
}
