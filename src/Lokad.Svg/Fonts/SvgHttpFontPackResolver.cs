using System.Net;

namespace Lokad.Svg.Fonts;

/// <summary>Owns opt-in HTTP font acquisition and a verified, host-budgeted program cache.</summary>
/// <remarks>
/// Construction performs no I/O. The supplied HttpClient and memory budget are
/// borrowed. Independent operations may use this resolver concurrently. The host
/// configures its client's redirect/handler policy: disable automatic redirects or
/// allow only trusted destinations. Responses whose final URI leaves the pack
/// root are rejected; this cannot undo a redirect already followed by the handler.
/// Disposal closes acquisition, cancels shared I/O and defers refunds until all
/// active borrowers and I/O finish. DisposeAsync additionally awaits outstanding
/// I/O; already-prepared documents may continue borrowing cached bytes and keep
/// their charges until disposed. The host keeps the root budget alive throughout.
/// </remarks>
public sealed class SvgHttpFontPackResolver : IDisposable, IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly HttpClient _client;
    private readonly Uri _root;
    private readonly string _packId;
    private readonly SvgHttpFontAcquisitionOptions _options;
    private readonly IMemoryScope _scope;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Dictionary<string, ProgramEntry> _programs = new(StringComparer.Ordinal);
    private readonly TaskCompletionSource _ioFinished = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Task<SvgFontPackCatalog>? _catalog;
    private IMemoryReservation? _catalogStorage;
    private int _operations;
    private int _loads;
    private long _clock;
    private bool _closed;
    private bool _canceling;
    private bool _scopeDisposed;

    private SvgHttpFontPackResolver(string packId, Uri packRoot, HttpClient client,
        SvgHttpFontAcquisitionOptions options, IMemoryBudget budget)
    {
        _packId = packId; _root = packRoot; _client = client; _options = options;
        Budget = budget;
        _scope = budget.CreateScope();
    }

    /// <summary>Validates an HTTP(S) directory root and explicit policy without downloading or taking client ownership.</summary>
    public static SvgHttpFontPackResolver Create(string packId, Uri packRoot, HttpClient client,
        SvgHttpFontAcquisitionOptions options, IMemoryBudget budget)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packId);
        SvgFontPackCatalog.ValidatePackId(packId);
        ArgumentNullException.ThrowIfNull(packRoot);
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(budget);
        if (!packRoot.IsAbsoluteUri || packRoot.Scheme is not ("https" or "http")
            || packRoot.UserInfo.Length != 0 || packRoot.Query.Length != 0 || packRoot.Fragment.Length != 0
            || !packRoot.AbsolutePath.EndsWith('/'))
            throw new ArgumentException("The pack root must be an absolute HTTP(S) directory URI without user information, query or fragment.", nameof(packRoot));
        return new(packId, packRoot, client, options, budget);
    }

    internal IMemoryBudget Budget { get; }

    /// <summary>Starts one borrower; its owner disposes it only after all snapshot/rendering users finish.</summary>
    internal Operation OpenOperation()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_closed, this);
            _operations++;
            return new(this);
        }
    }

    private Task<SvgFontPackCatalog> GetCatalogAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        TaskCompletionSource<SvgFontPackCatalog>? start = null;
        Task<SvgFontPackCatalog> shared;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_closed, this);
            if (_catalog is null)
            {
                start = new(TaskCreationOptions.RunContinuationsAsynchronously);
                _catalog = start.Task;
                _loads++;
            }
            shared = _catalog;
        }
        if (start is not null) _ = LoadCatalogAsync(start);
        return shared.WaitAsync(token);
    }

    private async Task LoadCatalogAsync(TaskCompletionSource<SvgFontPackCatalog> completion)
    {
        IMemoryReservation? metadata = null;
        SvgFontPackCatalog? result = null;
        Exception? failure = null;
        try
        {
            using CancellationTokenSource deadline = CreateDeadline();
            using OwnedBytes body = await DownloadAsync(new(_root, "manifest.json"), _options.MaximumManifestBytes, null, deadline.Token).ConfigureAwait(false);
            // Keep staging and estimated deserialized/catalog capacity charged together.
            metadata = Reserve(checked((long)body.Memory.Length * 4 + 1024));
            SvgFontPackCatalog catalog = SvgFontPackCatalog.Parse(body.Memory, _packId);
            deadline.Token.ThrowIfCancellationRequested();
            lock (_gate) { _catalogStorage = metadata; metadata = null; }
            result = catalog;
        }
        catch (Exception exception)
        {
            metadata?.Dispose();
            lock (_gate) { if (ReferenceEquals(_catalog, completion.Task)) _catalog = null; }
            failure = Classify(exception);
        }
        finally { FinishLoad(); }
        if (failure is not null)
        {
            completion.TrySetException(failure);
            _ = completion.Task.Exception;
        }
        else completion.TrySetResult(result ?? throw new InvalidOperationException("Catalog acquisition did not complete."));
    }

    private async ValueTask<ISvgFontProgramSource?> GetSourceAsync(Operation operation, SvgFontPackFile file, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(file);
        token.ThrowIfCancellationRequested();
        SvgFontPackCatalog catalog = await GetCatalogAsync(token).ConfigureAwait(false);
        if (!catalog.Files.Contains(file)) throw new ArgumentException("The file does not belong to this resolver's catalog.", nameof(file));
        if (file.ByteSize > _options.MaximumProgramBytes)
            throw Failure(SvgFontFailureStage.Size, "The selected font exceeds the configured acquisition size.");
        ProgramEntry entry;
        bool start = false;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(operation.Closed || _closed, this);
            if (!_programs.TryGetValue(file.Sha256, out ProgramEntry? existing))
            {
                entry = new(file);
                _programs.Add(file.Sha256, entry);
                _loads++;
                start = true;
            }
            else
            {
                entry = existing;
                if (entry.File.ByteSize != file.ByteSize)
                    throw Failure(SvgFontFailureStage.Integrity, "The selected declarations disagree about program size.");
            }
            if (operation.Programs.Add(entry)) entry.Borrowers++;
            entry.LastUse = ++_clock;
        }
        if (start) _ = LoadProgramAsync(entry);
        await entry.Completion.Task.WaitAsync(token).ConfigureAwait(false);
        return new OperationSource(operation, entry);
    }

    private async Task LoadProgramAsync(ProgramEntry entry)
    {
        OwnedBytes? body = null;
        Exception? failure = null;
        try
        {
            using CancellationTokenSource deadline = CreateDeadline();
            Uri uri = ResolveProgram(entry.File.RelativePath, _root);
            body = await DownloadAsync(uri, checked((int)entry.File.ByteSize), entry.File.ByteSize, deadline.Token).ConfigureAwait(false);
            try { SvgFontProgramIntegrity.Verify(body.Memory, entry.File, deadline.Token); }
            catch (SvgException) { throw Failure(SvgFontFailureStage.Integrity, "The downloaded font does not match its manifest."); }
            lock (_gate) { entry.Bytes = body; body = null; TrimIdlePrograms(); }
        }
        catch (Exception exception)
        {
            body?.Dispose();
            lock (_gate)
            {
                if (_programs.TryGetValue(entry.File.Sha256, out ProgramEntry? current) && ReferenceEquals(current, entry))
                    _programs.Remove(entry.File.Sha256);
            }
            failure = Classify(exception);
        }
        finally { FinishLoad(); }
        if (failure is not null)
        {
            entry.Completion.TrySetException(failure);
            _ = entry.Completion.Task.Exception;
        }
        else entry.Completion.TrySetResult();


        static Uri ResolveProgram(string path, Uri root)
        {
            foreach (string segment in path.Split('/'))
            {
                string decoded = segment;
                while (true)
                {
                    if (decoded.Length == 0 || decoded is "." or ".." || decoded.IndexOfAny(['/', '\\']) >= 0)
                        throw Failure(SvgFontFailureStage.Format, "A font program path leaves its configured pack root.");
                    string next = Uri.UnescapeDataString(decoded);
                    if (next == decoded) break;
                    decoded = next;
                }
            }
            Uri result = new(root, path);
            if (!root.IsBaseOf(result)) throw Failure(SvgFontFailureStage.Format, "A font program path leaves its configured pack root.");
            return result;
        }

    }

    private CancellationTokenSource CreateDeadline()
    {
        CancellationTokenSource result = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        result.CancelAfter(_options.AcquisitionTimeout);
        return result;
    }

    private IMemoryReservation Reserve(long bytes)
        => _scope.TryReserve(bytes, out IMemoryReservation? reservation) ? reservation
            : throw new SvgException(SvgErrorCode.LimitExceeded, "The host refused storage for HTTP font acquisition.");

    private async Task<OwnedBytes> DownloadAsync(Uri uri, int capacity, long? expectedSize, CancellationToken token)
    {
        IMemoryReservation reservation = Reserve(capacity);
        try
        {
            byte[] bytes = new byte[capacity];
            for (int attempt = 0; attempt < _options.MaximumAttempts; attempt++)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    using HttpRequestMessage request = new(HttpMethod.Get, uri);
                    using HttpResponseMessage response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
                    if (response.RequestMessage?.RequestUri is {} final && !_root.IsBaseOf(final))
                        throw Failure(SvgFontFailureStage.Transport, "The response left the configured font-pack root.");
                    response.EnsureSuccessStatusCode();
                    long? length = response.Content.Headers.ContentLength;
                    if (length > capacity || expectedSize is {} expected && length is {} declared && declared != expected)
                        throw Failure(SvgFontFailureStage.Size, "The response declares an unexpected font-resource size.");
                    await using Stream stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
                    int count = 0;
                    while (count < capacity)
                    {
                        int read = await stream.ReadAsync(bytes.AsMemory(count), token).ConfigureAwait(false);
                        if (read == 0) break;
                        count += read;
                    }
                    if (count == capacity && await stream.ReadAsync(new byte[1], token).ConfigureAwait(false) != 0
                        || expectedSize is {} size && count != size)
                        throw Failure(SvgFontFailureStage.Size, "The received font-resource size does not match its bound.");
                    token.ThrowIfCancellationRequested();
                    return new(bytes, count, reservation);
                }
                catch (Exception exception) when (exception is HttpRequestException or IOException)
                {
                    bool transient = exception is IOException || exception is HttpRequestException http
                        && (http.StatusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests
                            or HttpStatusCode.InternalServerError or HttpStatusCode.BadGateway
                            or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout
                            || http.StatusCode is null && http.HttpRequestError is (HttpRequestError.Unknown
                                or HttpRequestError.ConnectionError or HttpRequestError.NameResolutionError or HttpRequestError.ResponseEnded));
                    if (!transient || attempt == _options.MaximumAttempts - 1)
                        throw Failure(SvgFontFailureStage.Transport, "HTTP font acquisition failed.");
                    await Task.Delay(_options.RetryDelay, token).ConfigureAwait(false);
                }
            }
            throw new InvalidOperationException("Acquisition attempts did not produce a result.");
        }
        catch { reservation.Dispose(); throw; }
    }

    private Exception Classify(Exception exception)
        => exception is OperationCanceledException && !_lifetime.IsCancellationRequested
            ? Failure(SvgFontFailureStage.Timeout, "The shared font acquisition deadline expired.")
            : exception is SvgException { ErrorCode: SvgErrorCode.MalformedInput, FontDiagnostic: null }
                ? Failure(SvgFontFailureStage.Format, "The font-pack manifest is invalid.") : exception;

    private static SvgException Failure(SvgFontFailureStage stage, string message)
        => new(stage == SvgFontFailureStage.Format ? SvgErrorCode.MalformedInput : SvgErrorCode.UnresolvedResource,
            message, new(stage, string.Empty, string.Empty, string.Empty, string.Empty));

    // Idle capacity policy is independent of admission pressure. Active programs
    // remain charged and pinned; releasing borrowers runs the same maintenance.
    private void TrimIdlePrograms()
    {
        long idleBytes = 0;
        foreach (ProgramEntry entry in _programs.Values)
            if (entry.Borrowers == 0 && entry.Bytes is {} bytes) idleBytes += bytes.Memory.Length;
        while (idleBytes > _options.MaximumCachedProgramBytes)
        {
            ProgramEntry? oldest = null;
            foreach (ProgramEntry entry in _programs.Values)
                if (entry.Borrowers == 0 && entry.Bytes is not null && (oldest is null || entry.LastUse < oldest.LastUse)) oldest = entry;
            if (oldest?.Bytes is not {} bytes) break;
            idleBytes -= bytes.Memory.Length;
            _programs.Remove(oldest.File.Sha256);
            bytes.Dispose();
        }
    }

    private void FinishLoad()
    {
        lock (_gate) { _loads--; ReleaseClosedOwner(); }
    }

    private void ReleaseClosedOwner()
    {
        if (!_closed) return;
        if (_loads == 0 && !_canceling) _ioFinished.TrySetResult();
        if (_operations != 0 || _loads != 0 || _canceling || _scopeDisposed) return;
        _scopeDisposed = true;
        foreach (ProgramEntry entry in _programs.Values) entry.Bytes?.Dispose();
        _programs.Clear();
        _catalog = null;
        _catalogStorage?.Dispose();
        _catalogStorage = null;
        _scope.Dispose();
        _lifetime.Dispose();
    }

    /// <summary>Closes new acquisition; live borrowers retain their storage and charges.</summary>
    public void Dispose()
    {
        lock (_gate) { if (_closed) return; _closed = true; _canceling = true; }
        // Cancellation callbacks may acquire _gate; never invoke them under it.
        try { _lifetime.Cancel(); }
        finally { lock (_gate) { _canceling = false; ReleaseClosedOwner(); } }
    }

    /// <summary>Closes acquisition and awaits all shared I/O cleanup; active documents retain their own borrow lifetime.</summary>
    public async ValueTask DisposeAsync()
    {
        Dispose();
        await _ioFinished.Task.ConfigureAwait(false);
    }

    internal sealed class ProgramEntry(SvgFontPackFile file)
    {
        internal SvgFontPackFile File { get; } = file;
        internal TaskCompletionSource Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal OwnedBytes? Bytes { get; set; }
        internal int Borrowers { get; set; }
        internal long LastUse { get; set; }
    }

    internal sealed class OwnedBytes(byte[] bytes, int length, IMemoryReservation reservation) : IDisposable
    {
        private byte[]? _bytes = bytes;
        internal ReadOnlyMemory<byte> Memory => (_bytes ?? throw new ObjectDisposedException(nameof(OwnedBytes))).AsMemory(0, length);
        public void Dispose() { _bytes = null; reservation.Dispose(); }
    }

    /// <summary>Owns pins for one document, without copying programs or introducing an allowance.</summary>
    internal sealed class Operation(SvgHttpFontPackResolver owner) : ISvgFontProgramSourceProvider, IDisposable
    {
        internal HashSet<ProgramEntry> Programs { get; } = [];
        internal bool Closed { get; private set; }
        internal Task<SvgFontPackCatalog> GetCatalogAsync(CancellationToken token)
        {
            lock (owner._gate) ObjectDisposedException.ThrowIf(Closed, this);
            return owner.GetCatalogAsync(token);
        }
        /// <summary>Returns an operation-borrowed immutable source; finish all users before disposing this operation.</summary>
        public ValueTask<ISvgFontProgramSource?> GetSourceAsync(SvgFontPackFile file, CancellationToken cancellationToken)
            => owner.GetSourceAsync(this, file, cancellationToken);
        internal ReadOnlyMemory<byte> Read(ProgramEntry entry)
        {
            lock (owner._gate)
            {
                ObjectDisposedException.ThrowIf(Closed, this);
                return (entry.Bytes ?? throw new ObjectDisposedException(nameof(ProgramEntry))).Memory;
            }
        }
        public void Dispose()
        {
            lock (owner._gate)
            {
                if (Closed) return;
                Closed = true;
                foreach (ProgramEntry entry in Programs) entry.Borrowers--;
                Programs.Clear();
                owner._operations--;
                owner.TrimIdlePrograms();
                owner.ReleaseClosedOwner();
            }
        }
    }

    private sealed class OperationSource(Operation operation, ProgramEntry entry) : ISvgFontProgramSource
    {
        /// <inheritdoc />
        public string StableId => entry.File.Sha256;
        /// <summary>Returns immutable bytes borrowed until the document operation finishes; observes caller cancellation.</summary>
        public ValueTask<ReadOnlyMemory<byte>> GetBytesAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(operation.Read(entry));
        }
    }
}
