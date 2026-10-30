using System.Net;

internal sealed class HttpFontSmokeHandler(byte[] manifest, byte[] font) : HttpMessageHandler
{
    private int _requests;
    internal int Requests => Volatile.Read(ref _requests);
    internal Task ManifestGate { get; init; } = Task.CompletedTask;
    internal Task ProgramGate { get; init; } = Task.CompletedTask;
    internal Task CancellationCleanupGate { get; init; } = Task.CompletedTask;
    internal TaskCompletionSource ManifestRequested { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal TaskCompletionSource ProgramRequested { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal TaskCompletionSource CancellationObserved { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Borrows each request through its controlled asynchronous response and transfers response ownership to the client.</summary>
    /// <remarks>Concurrent requests share immutable bytes and atomic counters. Response gates honor acquisition cancellation;
    /// a separate cleanup gate must finish before canceled I/O returns. No network I/O occurs.</remarks>
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        bool catalog = request.RequestUri?.AbsolutePath.EndsWith("manifest.json", StringComparison.Ordinal) == true;
        if (!catalog && request.RequestUri?.AbsolutePath.EndsWith("/Primary.ttf", StringComparison.Ordinal) != true)
            throw new InvalidOperationException("The installed resolver requested an unselected program.");
        Interlocked.Increment(ref _requests);
        (catalog ? ManifestRequested : ProgramRequested).TrySetResult();
        try { await (catalog ? ManifestGate : ProgramGate).WaitAsync(cancellationToken); }
        catch (OperationCanceledException)
        {
            CancellationObserved.TrySetResult();
            await CancellationCleanupGate;
            throw;
        }
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            RequestMessage = request,
            Content = new ByteArrayContent(catalog ? manifest : font)
        };
    }
}
