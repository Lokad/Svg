namespace Lokad.Svg.Fonts;

using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;
using System.Runtime.CompilerServices;

/// <summary>
/// Represents one immutable, caller-byte-backed set of selected standalone
/// TrueType fonts. The caller keeps every returned byte region valid and unchanged
/// until this snapshot is disposed. Instances are safe for concurrent reads after
/// successful creation when disposal does not overlap those reads, but do not own a shared cache or perform I/O. Multiple
/// face records for one manifest program share one acquired byte region.
/// </summary>
public sealed partial class SvgFontSnapshot : IDisposable
{
    private const long MaximumBorrowedBytes = 32L * 1024 * 1024;
    private const int MaximumFaces = 256;
    private SvgPreparedFont[] _fonts;
    private IReadOnlyList<SvgPreparedFont> _fontView;
    private readonly SvgFontSnapshotLease _lease;
    private bool _disposed;
    private readonly RenderBudget.BudgetReservation _storage;
    private RenderBudget.BudgetReservation? _requestStorage;
    private IMemoryScope? _scope;

    private SvgFontSnapshot(SvgPreparedFont[] fonts, SvgFontSnapshotLease lease, RenderBudget.BudgetReservation storage)
    {
        _fonts = fonts;
        _fontView = Array.AsReadOnly(fonts);
        _lease = lease;
        _storage = storage;
    }

    /// <summary>Gets verified selected faces and their caller-owned program bytes.</summary>
    public IReadOnlyList<SvgPreparedFont> Fonts
    {
        get
        {
            ThrowIfDisposed();
            return _fontView;
        }
    }

    /// <summary>Gets the prepared face selected for one previously prepared family/style request.</summary>
    internal SvgPreparedFont GetFont(SvgFontFaceRequest request)
        => GetFontCore(request, string.Empty, CancellationToken.None);

    /// <summary>Gets the prepared face selected for one previously prepared family/style request, retaining bounded rendering context on a missing face.</summary>
    internal SvgPreparedFont GetFont(SvgFontFaceRequest request, string sourceElementId)
        => GetFontCore(request, sourceElementId, CancellationToken.None);

    /// <summary>Gets the prepared face while observing cancellation during the bounded face scan.</summary>
    internal SvgPreparedFont GetFont(SvgFontFaceRequest request, string sourceElementId, CancellationToken cancellationToken)
    {
        return GetFontCore(request, sourceElementId, cancellationToken);
    }

    private SvgPreparedFont GetFontCore(SvgFontFaceRequest request, string sourceElementId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(sourceElementId);
        ThrowIfDisposed();
        SvgPreparedFont? compatible = null;
        foreach (SvgPreparedFont font in _fonts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SvgFontFaceRequest selected = font.Resolution.Resolution.Request;
            if (selected.Matches(request))
            {
                if (selected.IsCssRequest == request.IsCssRequest) return font;
                compatible ??= font;
            }
        }
        if (compatible is not null) return compatible;
        throw new SvgException(SvgErrorCode.UnresolvedResource, "The requested font face was not prepared in the supplied snapshot.", new SvgFontDiagnostic(SvgFontFailureStage.Source, request.Family, string.Empty, string.Empty, sourceElementId, request.Weight, request.Italic, null, null, null));
    }

    /// <summary>
    /// Asynchronously verifies and parses the selected caller-owned programs into
    /// one bounded snapshot. The input order is retained; faces sharing a declared
    /// program SHA-256 acquire and account for that program only once.
    /// </summary>
    public static ValueTask<SvgFontSnapshot> CreateAsync(IReadOnlyList<SvgFontProgramResolution> resolutions, CancellationToken cancellationToken)
        => CreatePreparedAsync(resolutions, new RenderBudget(), cancellationToken);

    /// <summary>Prepares borrowed font programs while owning snapshot metadata under the supplied host budget until disposal.</summary>
    public static async ValueTask<SvgFontSnapshot> CreateAsync(IReadOnlyList<SvgFontProgramResolution> resolutions, IMemoryBudget budget, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(resolutions);
        ArgumentNullException.ThrowIfNull(budget);
        cancellationToken.ThrowIfCancellationRequested();
        IMemoryScope scope = budget.CreateScope();
        try
        {
            SvgFontSnapshot snapshot = await CreatePreparedAsync(resolutions, new RenderBudget(scope), cancellationToken).ConfigureAwait(false);
            snapshot._scope = scope;
            return snapshot;
        }
        catch { scope.Dispose(); throw; }
    }

    /// <summary>Builds snapshot-owned face metadata under a borrowed active owner, without charging caller-owned font bytes.</summary>
    internal static async ValueTask<SvgFontSnapshot> CreatePreparedAsync(IReadOnlyList<SvgFontProgramResolution> resolutions, RenderBudget budget, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(resolutions);
        cancellationToken.ThrowIfCancellationRequested();
        if (resolutions.Count > MaximumFaces) throw new SvgException(SvgErrorCode.LimitExceeded, "The selected font face count exceeds the snapshot limit.");
        RenderBudget.BudgetReservation retained = budget.Reserve(GetSnapshotStorageBytes(resolutions.Count), SvgMemoryCategory.Resource);
        var lease = new SvgFontSnapshotLease();
        try
        {
            using RenderBudget.BudgetReservation scratch = budget.Reserve(resolutions.Count == 0 ? 0 : 512L + 2048L * resolutions.Count, SvgMemoryCategory.Temporary);
            SvgFontPackResolution[] selections = GetSelections(resolutions, nameof(resolutions), cancellationToken);
            ValidateSelections(selections, cancellationToken);
            var programs = new Dictionary<string, (ReadOnlyMemory<byte> Bytes, OpenTypeFontMetrics Metrics)>(StringComparer.Ordinal);
            var prepared = new SvgPreparedFont[resolutions.Count];
            long totalBytes = 0;
            for (int index = 0; index < resolutions.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                SvgFontProgramResolution resolution = resolutions[index] ?? throw new ArgumentException("The selected font list contains a null resolution.", nameof(resolutions));
                string programKey = resolution.Resolution.Face.File.Sha256;
                if (!programs.TryGetValue(programKey, out (ReadOnlyMemory<byte> Bytes, OpenTypeFontMetrics Metrics) program))
                {
                    ReadOnlyMemory<byte> loadedBytes;
                    try { loadedBytes = await GetVerifiedBytesAsync(resolution.Source, cancellationToken).ConfigureAwait(false); }
                    catch (SvgFontProgramSourceFailure exception)
                    {
                        throw SvgFontSourceFailures.Create(resolution.Resolution, exception.SourceException);
                    }
                    catch (SvgException exception) when (exception.FontDiagnostic is null)
                    {
                        SvgFontFailureStage stage = exception.ErrorCode == SvgErrorCode.UnresolvedResource ? SvgFontFailureStage.Integrity : SvgFontFailureStage.Format;
                        throw new SvgException(exception.ErrorCode, exception.Message, CreateDiagnostic(resolution.Resolution, stage));
                    }
                    totalBytes = checked(totalBytes + loadedBytes.Length);
                    if (totalBytes > MaximumBorrowedBytes) throw new SvgException(SvgErrorCode.LimitExceeded, "The selected font programs exceed the snapshot byte limit.");
                    try { program = (loadedBytes, OpenTypeFont.ParseMetrics(loadedBytes, cancellationToken)); }
                    catch (SvgException exception)
                    {
                        throw new SvgException(exception.ErrorCode, exception.Message, CreateDiagnostic(resolution.Resolution, SvgFontFailureStage.Format));
                    }
                    programs.Add(programKey, program);
                }
                ReadOnlyMemory<byte> bytes = program.Bytes;
                if (resolution.Source is not VerifiedSvgFontProgramSource)
                {
                    try { ValidateDeclaredProgram(resolution, bytes, cancellationToken); }
                    catch (SvgException exception) when (exception.FontDiagnostic is null)
                    {
                        throw new SvgException(exception.ErrorCode, exception.Message, CreateDiagnostic(resolution.Resolution, SvgFontFailureStage.Integrity));
                    }
                }
                OpenTypeFontMetrics metrics = program.Metrics;
                prepared[index] = new SvgPreparedFont(resolution, bytes, metrics, lease);
            }
            return new SvgFontSnapshot(prepared, lease, retained);
        }
        catch { lease.Dispose(); retained.Dispose(); throw; }

        static void ValidateDeclaredProgram(SvgFontProgramResolution resolution, ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken)
        {
            SvgFontPackFile file = resolution.Resolution.Face.File;
            if (bytes.Length != file.ByteSize) throw new SvgException(SvgErrorCode.UnresolvedResource, "The selected font program has an unexpected byte size.", CreateDiagnostic(resolution.Resolution, SvgFontFailureStage.Integrity));
            byte[] hash = SvgFontProgramIntegrity.ComputeSha256(bytes, cancellationToken);
            if (!hash.SequenceEqual(file.Sha256Bytes)) throw new SvgException(SvgErrorCode.UnresolvedResource, "The selected font program SHA-256 does not match its manifest.", CreateDiagnostic(resolution.Resolution, SvgFontFailureStage.Integrity));
        }

        static ValueTask<ReadOnlyMemory<byte>> GetVerifiedBytesAsync(ISvgFontProgramSource source, CancellationToken cancellationToken)
            => source is VerifiedSvgFontProgramSource verified ? verified.GetBytesForPreparationAsync(cancellationToken) : source.GetBytesAsync(cancellationToken);
    }

    /// <summary>
    /// Resolves explicit requests, selectively asks the caller-owned provider for
    /// the resulting distinct programs, and creates one verified snapshot. Use
    /// the SVG overload to discover requests from the supported direct-text subset.
    /// </summary>
    public static async ValueTask<SvgFontSnapshot> CreateAsync(SvgFontPackCatalog catalog, IReadOnlyList<SvgFontFaceRequest> requests, ISvgFontProgramSourceProvider provider, CancellationToken cancellationToken)
        => await CreateRequestedAsync(catalog, requests, provider, null, new RenderBudget(), cancellationToken).ConfigureAwait(false);

    /// <summary>Prepares ordered requests using copied host mappings/fallbacks, sharing verified programs and borrowing caller-owned bytes until disposal.</summary>
    public static ValueTask<SvgFontSnapshot> CreateAsync(SvgFontPackCatalog catalog, IReadOnlyList<SvgFontFaceRequest> requests, ISvgFontProgramSourceProvider provider, SvgFontSelectionPolicy policy, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(policy);
        return CreateRequestedAsync(catalog, requests, provider, policy, new RenderBudget(), cancellationToken);
    }

    /// <summary>Acquires selected caller-owned programs, retaining snapshot metadata under a borrowed host budget.</summary>
    public static ValueTask<SvgFontSnapshot> CreateAsync(SvgFontPackCatalog catalog, IReadOnlyList<SvgFontFaceRequest> requests,
        ISvgFontProgramSourceProvider provider, IMemoryBudget budget, CancellationToken cancellationToken)
        => CreateRequestedWithBudgetAsync(catalog, requests, provider, null, budget, cancellationToken);

    /// <summary>Applies host family selection while retaining snapshot metadata under the supplied budget until disposal.</summary>
    public static ValueTask<SvgFontSnapshot> CreateAsync(SvgFontPackCatalog catalog, IReadOnlyList<SvgFontFaceRequest> requests,
        ISvgFontProgramSourceProvider provider, SvgFontSelectionPolicy policy, IMemoryBudget budget, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(policy);
        return CreateRequestedWithBudgetAsync(catalog, requests, provider, policy, budget, cancellationToken);
    }

    private static async ValueTask<SvgFontSnapshot> CreateRequestedWithBudgetAsync(SvgFontPackCatalog catalog, IReadOnlyList<SvgFontFaceRequest> requests,
        ISvgFontProgramSourceProvider provider, SvgFontSelectionPolicy? policy, IMemoryBudget budget, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(requests);
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(budget);
        token.ThrowIfCancellationRequested();
        IMemoryScope scope = budget.CreateScope();
        try
        {
            SvgFontSnapshot snapshot = await CreateRequestedAsync(catalog, requests, provider, policy, new RenderBudget(scope), token).ConfigureAwait(false);
            snapshot._scope = scope;
            return snapshot;
        }
        catch { scope.Dispose(); throw; }
    }

    private static async ValueTask<SvgFontSnapshot> CreateRequestedAsync(SvgFontPackCatalog catalog, IReadOnlyList<SvgFontFaceRequest> requests, ISvgFontProgramSourceProvider provider, SvgFontSelectionPolicy? policy, RenderBudget budget, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(requests);
        ArgumentNullException.ThrowIfNull(provider);
        cancellationToken.ThrowIfCancellationRequested();
        if (requests.Count > MaximumFaces) throw new SvgException(SvgErrorCode.LimitExceeded, "The selected font face count exceeds the snapshot limit.");
        using RenderBudget.BudgetReservation scratch = budget.Reserve(requests.Count == 0 ? 0 : 512L + 256L * requests.Count, SvgMemoryCategory.Temporary);
        var selections = new SvgFontPackResolution[requests.Count];
        for (int index = 0; index < requests.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SvgFontFaceRequest request = requests[index] ?? throw new ArgumentException("The font request list contains a null request.", nameof(requests));
            selections[index] = catalog.ResolveFamilies(request, policy, cancellationToken);
        }
        ValidateSelections(selections, cancellationToken);
        var sources = new Dictionary<string, ISvgFontProgramSource>(StringComparer.Ordinal);
        var resolutions = new SvgFontProgramResolution[requests.Count];
        for (int index = 0; index < requests.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SvgFontPackResolution selection = selections[index];
            string programKey = selection.Face.File.Sha256;
            if (!sources.TryGetValue(programKey, out ISvgFontProgramSource? source))
            {
                try { source = await provider.GetSourceAsync(selection.Face.File, cancellationToken).ConfigureAwait(false); }
                catch (SvgException exception)
                {
                    throw SvgFontSourceFailures.Create(selection, exception);
                }
                cancellationToken.ThrowIfCancellationRequested();
                if (source is null) throw SvgFontSourceFailures.Create(selection, SvgErrorCode.UnresolvedResource);
                sources.Add(programKey, source);
            }
            resolutions[index] = catalog.Bind(selection, source, cancellationToken);
        }
        return await CreatePreparedAsync(resolutions, budget, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Discovers the admitted explicit text font requests from SVG bytes, then
    /// selectively acquires their resolved programs. This preparation step does
    /// not retain text runs or render text.
    /// </summary>
    public static ValueTask<SvgFontSnapshot> CreateAsync(ReadOnlyMemory<byte> svg, SvgFontPackCatalog catalog, ISvgFontProgramSourceProvider provider, CancellationToken cancellationToken)
        => CreateAsync(svg, catalog, provider, SvgRenderLimits.Default, cancellationToken);

    /// <summary>Discovers CSS family lists without fonts, then selectively verifies host-authorized programs under the supplied SVG limits.</summary>
    public static ValueTask<SvgFontSnapshot> CreateAsync(ReadOnlyMemory<byte> svg, SvgFontPackCatalog catalog, ISvgFontProgramSourceProvider provider, SvgFontSelectionPolicy policy, SvgRenderLimits limits, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(limits);
        return CreateAsync(svg, catalog, provider, limits, policy, cancellationToken);
    }

    /// <summary>Discovers and acquires admitted text fonts under caller-selected SVG limits before producing an immutable snapshot.</summary>
    public static ValueTask<SvgFontSnapshot> CreateAsync(ReadOnlyMemory<byte> svg, SvgFontPackCatalog catalog, ISvgFontProgramSourceProvider provider, SvgRenderLimits limits, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(limits);
        return CreateAsync(svg, catalog, provider, limits, null, cancellationToken);
    }

    /// <summary>Discovers requirements once and retains library-owned font metadata under the options budget until disposal.</summary>
    /// <remarks>Font programs remain caller-owned. Viewport, scale and background do not alter requirement discovery; limits, selection policy and memory budget apply.</remarks>
    public static async ValueTask<SvgFontSnapshot> CreateAsync(ReadOnlyMemory<byte> svg, SvgFontPackCatalog catalog,
        ISvgFontProgramSourceProvider provider, SvgRenderOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(provider);
        cancellationToken.ThrowIfCancellationRequested();
        IMemoryScope? scope = options.MemoryBudget?.CreateScope();
        try
        {
            RenderBudget budget = scope is null ? new() : new(scope);
            using SvgDocumentPreparation preparation = SvgDocumentPreparation.Create(svg, options.Limits, budget, cancellationToken);
            SvgFontSnapshot snapshot = await CreateAsync(preparation, catalog, provider, options.FontSelectionPolicy, cancellationToken).ConfigureAwait(false);
            snapshot._scope = scope;
            return snapshot;
        }
        catch { scope?.Dispose(); throw; }
    }

    internal static ValueTask<SvgFontSnapshot> CreateAsync(ReadOnlyMemory<byte> svg, SvgFontPackCatalog catalog, ISvgFontProgramSourceProvider provider, SvgRenderLimits limits, SvgFontSelectionPolicy? policy, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(limits);
        SvgRenderOptions options = new(null, 1, SvgColor.Transparent, limits);
        if (policy is not null) options = options.WithFontSelectionPolicy(policy);
        return CreateAsync(svg, catalog, provider, options, cancellationToken);
    }

    /// <summary>Acquires fonts from one retained document preparation without creating another budget or stylesheet collection.</summary>
    internal static async ValueTask<SvgFontSnapshot> CreateAsync(SvgDocumentPreparation preparation, SvgFontPackCatalog catalog,
        ISvgFontProgramSourceProvider provider, SvgFontSelectionPolicy? policy, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(preparation);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(provider);
        using SvgFontCoverageDiscovery? owner = catalog.Files.Count > 1 ? preparation.DiscoverFontCoverage() : null;
        SvgFontCoverageRequest[]? coverage = owner?.Requests;
        SvgFontFaceRequest[] requests = coverage is null ? preparation.DiscoverFontRequests()
            : coverage.Select(static item => item.Request).ToArray();
        SvgFontSnapshot snapshot = await CreateDiscoveredAsync(catalog, provider, policy, requests, coverage, preparation.Budget, cancellationToken).ConfigureAwait(false);
        snapshot.AdoptDiscoveredRequests(preparation);
        return snapshot;
    }

    /// <summary>Acquires a previously discovered requirement set without revisiting document structure.</summary>
    internal static async ValueTask<SvgFontSnapshot> CreateAsync(SvgFontPackCatalog catalog, SvgFontCoverageRequest[] coverage,
        ISvgFontProgramSourceProvider provider, SvgFontSelectionPolicy? policy, RenderBudget budget, CancellationToken cancellationToken)
    {
        using RenderBudget.BudgetReservation scratch = budget.Reserve(checked(32L + (long)IntPtr.Size * coverage.Length), SvgMemoryCategory.Temporary);
        SvgFontFaceRequest[] requests = coverage.Select(static item => item.Request).ToArray();
        return await CreateDiscoveredAsync(catalog, provider, policy, requests, coverage, budget, cancellationToken).ConfigureAwait(false);
    }

    private static async ValueTask<SvgFontSnapshot> CreateDiscoveredAsync(SvgFontPackCatalog catalog,
        ISvgFontProgramSourceProvider provider, SvgFontSelectionPolicy? policy, SvgFontFaceRequest[] requests, SvgFontCoverageRequest[]? coverage, RenderBudget budget, CancellationToken cancellationToken)
    {
        try
        {
            return coverage is null
                ? await CreateRequestedAsync(catalog, requests, provider, policy, budget, cancellationToken).ConfigureAwait(false)
                : await CreateWithCoverageAsync(catalog, coverage, provider, policy, budget, cancellationToken).ConfigureAwait(false);
        }
        catch (SvgException exception) when (exception.FontDiagnostic is {} diagnostic)
        {
            foreach (SvgFontFaceRequest request in requests)
                if (request.Family.Equals(diagnostic.RequestedFamily, StringComparison.OrdinalIgnoreCase)
                    && request.Weight == diagnostic.RequestedWeight && request.Italic == diagnostic.RequestedItalic
                    && request.SourceContext is {} source)
                {
                    exception.AttachSource(source.Describe(string.Empty, string.Empty));
                    break;
                }
            throw;
        }
    }

    /// <summary>Adopts discovered request arrays/family copies after all acquisition borrowers finish; the preparation stops refunding them.</summary>
    internal void AdoptDiscoveredRequests(SvgDocumentPreparation preparation)
    {
        if (_requestStorage is not null) throw new InvalidOperationException("Discovered requests already have a snapshot owner.");
        _requestStorage = preparation.DetachFontRequestsStorage();
    }

    private static long GetSnapshotStorageBytes(int count)
        => count == 0 ? 0 : checked(32L + (long)IntPtr.Size * count
            + (long)count * (Unsafe.SizeOf<OpenTypeFontMetrics>() + 128));

    /// <summary>Releases references to caller-owned font byte regions retained by this snapshot.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _lease.Dispose();
        _fonts = [];
        _fontView = Array.AsReadOnly(_fonts);
        _requestStorage?.Dispose();
        _requestStorage = null;
        _storage.Dispose();
        _scope?.Dispose();
        _scope = null;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(SvgFontSnapshot));
    }

    private static SvgFontPackResolution[] GetSelections(IReadOnlyList<SvgFontProgramResolution> resolutions, string parameterName, CancellationToken cancellationToken)
    {
        if (resolutions.Count > MaximumFaces) throw new SvgException(SvgErrorCode.LimitExceeded, "The selected font face count exceeds the snapshot limit.");
        var selections = new SvgFontPackResolution[resolutions.Count];
        for (int index = 0; index < resolutions.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SvgFontProgramResolution resolution = resolutions[index] ?? throw new ArgumentException("The selected font list contains a null resolution.", parameterName);
            selections[index] = resolution.Resolution;
        }
        return selections;
    }

    private static void ValidateSelections(IReadOnlyList<SvgFontPackResolution> selections, CancellationToken cancellationToken)
    {
        var declaredSizes = new Dictionary<string, long>(StringComparer.Ordinal);
        long totalBytes = 0;
        foreach (SvgFontPackResolution selection in selections)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (selection.Face.FaceIndex != 0) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Standalone TrueType font snapshots require face index zero.", CreateDiagnostic(selection, SvgFontFailureStage.Format));
            string programKey = selection.Face.File.Sha256;
            long declaredSize = selection.Face.File.ByteSize;
            if (declaredSizes.TryGetValue(programKey, out long previousSize))
            {
                if (previousSize != declaredSize) throw new SvgException(SvgErrorCode.UnresolvedResource, "Selected font declarations with the same SHA-256 have conflicting byte sizes.", CreateDiagnostic(selection, SvgFontFailureStage.Integrity));
                continue;
            }
            if (declaredSize > MaximumBorrowedBytes - totalBytes) throw new SvgException(SvgErrorCode.LimitExceeded, "The selected font programs exceed the snapshot byte limit.");
            declaredSizes.Add(programKey, declaredSize);
            totalBytes += declaredSize;
        }
    }

    private static SvgFontDiagnostic CreateDiagnostic(SvgFontPackResolution selection, SvgFontFailureStage stage)
        => new(stage, selection.Request.Family, selection.Face.ResolvedFamily, selection.Face.File.RelativePath + ":" + selection.Face.FaceIndex, string.Empty,
            selection.Request.Weight, selection.Request.Italic, selection.IsFallback, null, null);
}

/// <summary>Represents one verified, caller-byte-backed font held by an immutable snapshot.</summary>
public sealed class SvgPreparedFont
{
    private readonly SvgFontProgramResolution _resolution;
    private readonly ReadOnlyMemory<byte> _bytes;
    private readonly OpenTypeFontMetrics _metrics;
    private readonly SvgFontSnapshotLease _lease;

    internal SvgPreparedFont(SvgFontProgramResolution resolution, ReadOnlyMemory<byte> bytes, OpenTypeFontMetrics metrics, SvgFontSnapshotLease lease)
    {
        _resolution = resolution;
        _bytes = bytes;
        _metrics = metrics;
        _lease = lease;
    }

    /// <summary>Gets the catalog selection and verified source identity.</summary>
    public SvgFontProgramResolution Resolution
    {
        get
        {
            _lease.ThrowIfDisposed();
            return _resolution;
        }
    }
    /// <summary>
    /// Gets the caller-owned, integrity-verified program view retained for the snapshot lifetime.
    /// This is not a defensive copy: callers must not mutate the underlying storage while this
    /// snapshot or an escaped prepared-font handle remains usable. Metadata remains immutable.
    /// </summary>
    public ReadOnlyMemory<byte> Bytes
    {
        get
        {
            _lease.ThrowIfDisposed();
            return _bytes;
        }
    }
    /// <summary>Gets the validated font-unit scale.</summary>
    public ushort UnitsPerEm
    {
        get
        {
            _lease.ThrowIfDisposed();
            return _metrics.UnitsPerEm;
        }
    }
    /// <summary>Gets the validated glyph count.</summary>
    public ushort GlyphCount
    {
        get
        {
            _lease.ThrowIfDisposed();
            return _metrics.GlyphCount;
        }
    }

    /// <summary>Reads the validated x-height, falling back to the existing x-glyph header.</summary>
    /// <remarks>Font bytes stay borrowed under the snapshot lease. Header lookup shares caller
    /// cancellation and allocates no glyph contours or retained storage.</remarks>
    internal int GetXHeight(CancellationToken token)
    {
        int height = Metrics.LineMetrics.XHeight;
        if (height == 0)
        {
            ushort glyph = OpenTypeFont.GetGlyphIndex(Bytes, 'x', Metrics, token);
            if (glyph != 0) height = OpenTypeFont.GetGlyphHeader(Bytes, glyph, Metrics, token)?.YMaximum ?? 0;
        }
        return height;
    }

    internal OpenTypeFontMetrics Metrics
    {
        get
        {
            _lease.ThrowIfDisposed();
            return _metrics;
        }
    }
}

internal sealed class SvgFontSnapshotLease
{
    private volatile bool _disposed;

    internal void Dispose() => _disposed = true;

    internal void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(SvgFontSnapshot));
    }
}
