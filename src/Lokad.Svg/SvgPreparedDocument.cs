namespace Lokad.Svg;

using Lokad.Svg.Fonts;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

/// <summary>Owns one prepared SVG/font operation and permits exactly one conversion with the retained memory owner and caller cancellation.</summary>
public sealed class SvgPreparedDocument : IDisposable
{
    private readonly SvgRenderOptions _options;
    private readonly SvgFontSnapshot _snapshot;
    private SvgRoot _root;
    private readonly RenderBudget _budget;
    private readonly IMemoryScope? _operationScope;
    private readonly SvgHttpFontPackResolver.Operation? _fontSources;
    private readonly IMemoryBudget? _memoryBudget;
    private int _state;

    private SvgPreparedDocument(SvgRenderOptions options, SvgFontSnapshot snapshot, SvgRoot root, RenderBudget budget,
        IMemoryScope? operationScope, SvgHttpFontPackResolver.Operation? fontSources, IMemoryBudget? memoryBudget)
    {
        _options = options;
        _snapshot = snapshot;
        _root = root;
        _budget = budget;
        _operationScope = operationScope;
        _fontSources = fontSources;
        _memoryBudget = memoryBudget;
    }

    /// <summary>Discovers required text fonts and lowers the admitted scene, retaining verified font bytes until this document is disposed or converted once.</summary>
    public static async ValueTask<SvgPreparedDocument> CreateAsync(ReadOnlyMemory<byte> svg, SvgFontPackCatalog catalog, ISvgFontProgramSourceProvider provider, SvgRenderOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(options);
        if (options.FontSnapshot is not null) throw new ArgumentException("Prepared document options must not supply a separate font snapshot.", nameof(options));
        IMemoryScope? execution = options.MemoryBudget?.CreateScope();
        SvgFontSnapshot? snapshot = null;
        SvgRoot root = default;
        try
        {
            RenderBudget budget = execution is null ? new() : new(execution);
            using SvgDocumentPreparation preparation = SvgDocumentPreparation.Create(svg, options.Limits, budget, cancellationToken);
            options = preparation.ResolveRasterOptions(options);
            snapshot = await SvgFontSnapshot.CreateAsync(preparation, catalog, provider, options.FontSelectionPolicy, cancellationToken).ConfigureAwait(false);
            SvgRenderOptions preparedOptions = options.WithFontSnapshot(snapshot);
            root = LowerPrepared(svg, preparedOptions, budget, preparation, cancellationToken);
            return new(preparedOptions, snapshot, root, budget, execution, null, options.MemoryBudget);
        }
        catch
        {
            root.Dispose();
            snapshot?.Dispose();
            execution?.Dispose();
            throw;
        }
    }

    /// <summary>Prepares once with explicit HTTP font acquisition and a host-governed document lifetime.</summary>
    /// <remarks>
    /// The resolver, client and root budget are borrowed. Requirements are discovered
    /// before catalog acquisition; font-free documents never wait for network work.
    /// Acquired programs stay pinned until conversion/disposal. Use owned PNG output,
    /// caller memory or a stream; governed bare-memory output cannot retain its charge.
    /// The caller keeps SVG input immutable until this asynchronous preparation ends.
    /// An options budget supplies the document owner and must share the resolver cache
    /// root; the host is responsible for that association, without identity inspection.
    /// </remarks>
    public static async ValueTask<SvgPreparedDocument> CreateAsync(ReadOnlyMemory<byte> svg,
        SvgHttpFontPackResolver resolver, SvgRenderOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentNullException.ThrowIfNull(options);
        if (options.FontSnapshot is not null) throw new ArgumentException("Prepared document options must not supply a separate font snapshot.", nameof(options));
        IMemoryBudget memoryBudget = options.MemoryBudget ?? resolver.Budget;
        SvgHttpFontPackResolver.Operation? sources = null;
        IMemoryScope? execution = null;
        SvgFontSnapshot? snapshot = null;
        SvgRoot root = default;
        try
        {
            sources = resolver.OpenOperation();
            execution = memoryBudget.CreateScope();
            RenderBudget budget = new(execution);
            using SvgDocumentPreparation preparation = SvgDocumentPreparation.Create(svg, options.Limits, budget, cancellationToken);
            options = preparation.ResolveRasterOptions(options);
            using SvgFontCoverageDiscovery coverage = preparation.DiscoverFontCoverage();
            SvgFontCoverageRequest[] requirements = coverage.Requests;
            if (requirements.Length == 0)
                snapshot = await SvgFontSnapshot.CreatePreparedAsync(Array.Empty<SvgFontProgramResolution>(), budget, cancellationToken).ConfigureAwait(false);
            else
            {
                SvgFontPackCatalog catalog;
                try { catalog = await sources.GetCatalogAsync(cancellationToken).ConfigureAwait(false); }
                catch (SvgException exception)
                {
                    SvgFontFaceRequest first = requirements[0].Request;
                    throw new SvgException(exception.ErrorCode, exception.Message,
                        new(exception.FontDiagnostic?.Stage ?? SvgFontFailureStage.Source, first.Family, string.Empty, string.Empty,
                            string.Empty, first.Weight, first.Italic, null, null, null), first.SourceContext?.Describe(string.Empty, string.Empty));
                }
                snapshot = await SvgFontSnapshot.CreateAsync(catalog, requirements, sources, options.FontSelectionPolicy, budget, cancellationToken).ConfigureAwait(false);
            }
            snapshot.AdoptDiscoveredRequests(preparation);
            SvgRenderOptions preparedOptions = options.WithFontSnapshot(snapshot);
            root = LowerPrepared(svg, preparedOptions, budget, preparation, cancellationToken);
            return new(preparedOptions, snapshot, root, budget, execution, sources, memoryBudget);
        }
        catch
        {
            root.Dispose();
            snapshot?.Dispose();
            execution?.Dispose();
            sources?.Dispose();
            throw;
        }
    }

    private static SvgRoot LowerPrepared(ReadOnlyMemory<byte> svg, SvgRenderOptions options, RenderBudget budget,
        SvgDocumentPreparation preparation, CancellationToken token)
    {
        SvgAffineTransform outputMap = preparation.GetOutputMap(options);
        SvgRoot root = SvgRootParser.ParsePrepared(svg, options.Limits, budget, options.FontSnapshot, token,
            outputMap, options.Viewport, options.OutputScale, preparation);
        if (!root.HasUnsupportedChild) return root;
        root.Dispose();
        throw new SvgException(SvgErrorCode.UnsupportedFeature, "This SVG child element is not implemented yet.", null, root.UnsupportedDiagnostic);
    }

    /// <summary>Converts this prepared document once and releases its retained font lease.</summary>
    public ReadOnlyMemory<byte> ConvertToPng(CancellationToken cancellationToken)
    {
        RequireUngovernedOwnedOutput();
        BeginConversion();
        try { return SvgConverter.ConvertToPng(_root, _options, _budget, cancellationToken); }
        finally { Release(); }
    }

    /// <summary>Schedules this prepared document's one conversion and releases its retained font lease when the task completes.</summary>
    public Task<ReadOnlyMemory<byte>> ConvertToPngAsync(CancellationToken cancellationToken)
    {
        RequireUngovernedOwnedOutput();
        BeginConversion();
        return ConvertAsync(cancellationToken);
    }

    /// <summary>Converts this document once; a governed PNG retains its own sibling scope until result disposal.</summary>
    public SvgPngResult ConvertToOwnedPng(CancellationToken cancellationToken)
    {
        BeginConversion();
        try { return EncodeOwned(cancellationToken); }
        finally { Release(); }
    }

    /// <summary>Schedules one owned-result conversion on the thread pool, retaining any output charge until result disposal.</summary>
    public Task<SvgPngResult> ConvertToOwnedPngAsync(CancellationToken cancellationToken)
    {
        BeginConversion();
        return ConvertOwnedAsync(cancellationToken);
    }

    private void RequireUngovernedOwnedOutput()
    {
        if (_memoryBudget is not null) throw new InvalidOperationException("A governed document requires ConvertToOwnedPng, caller memory or a stream.");
    }

    private SvgPngResult EncodeOwned(CancellationToken token)
    {
        IMemoryScope? owner = _memoryBudget?.CreateScope();
        try { return new(SvgConverter.ConvertToOwnedPng(_root, _options, _budget, owner, token), owner); }
        catch { owner?.Dispose(); throw; }
    }

    private async Task<SvgPngResult> ConvertOwnedAsync(CancellationToken token)
    {
        try { return await Task.Run(() => EncodeOwned(token), token).ConfigureAwait(false); }
        finally { Release(); }
    }

    /// <summary>Converts this prepared document once into caller-owned destination memory and releases its retained font lease.</summary>
    public int ConvertToPng(Memory<byte> destination, CancellationToken cancellationToken)
    {
        BeginConversion();
        try { return SvgConverter.ConvertToPng(_root, destination, _options, _budget, cancellationToken); }
        finally { Release(); }
    }

    /// <summary>Converts this prepared document once to a caller-owned writable stream and releases its retained font lease.</summary>
    public int ConvertToPng(Stream destination, CancellationToken cancellationToken)
    {
        BeginConversion();
        try { return SvgConverter.ConvertToPng(_root, destination, _options, _budget, cancellationToken); }
        finally { Release(); }
    }

    /// <summary>Schedules this prepared document's one destination-memory conversion and releases its retained font lease when the task completes.</summary>
    public Task<int> ConvertToPngAsync(Memory<byte> destination, CancellationToken cancellationToken)
    {
        BeginConversion();
        return ConvertAsync(destination, cancellationToken);
    }

    /// <summary>Schedules this prepared document's one stream conversion and releases its retained font lease when the task completes.</summary>
    public Task<int> ConvertToPngAsync(Stream destination, CancellationToken cancellationToken)
    {
        BeginConversion();
        return ConvertAsync(destination, cancellationToken);
    }

    /// <summary>Releases the retained snapshot when no conversion will occur.</summary>
    public void Dispose()
    {
        if (Interlocked.CompareExchange(ref _state, 2, 0) == 0) Release();
    }

    private void BeginConversion()
    {
        if (Interlocked.CompareExchange(ref _state, 1, 0) != 0) throw new ObjectDisposedException(nameof(SvgPreparedDocument));
    }

    private async Task<ReadOnlyMemory<byte>> ConvertAsync(CancellationToken cancellationToken)
    {
        try { return await SvgConverter.ConvertToPngAsync(_root, _options, _budget, cancellationToken).ConfigureAwait(false); }
        finally { Release(); }
    }

    private async Task<int> ConvertAsync(Memory<byte> destination, CancellationToken cancellationToken)
    {
        try { return await SvgConverter.ConvertToPngAsync(_root, destination, _options, _budget, cancellationToken).ConfigureAwait(false); }
        finally { Release(); }
    }

    private async Task<int> ConvertAsync(Stream destination, CancellationToken cancellationToken)
    {
        try { return await SvgConverter.ConvertToPngAsync(_root, destination, _options, _budget, cancellationToken).ConfigureAwait(false); }
        finally { Release(); }
    }

    /// <summary>Gets the retained scene geometries for lifetime characterization tests; null once the operation is released.</summary>
    internal SvgGeometry[]? RetainedSceneGeometries => _root.Geometries;

    private void Release()
    {
        _root.Dispose();
        // Detach the owned scene so a retained completed operation no longer keeps geometry on the heap.
        _root = default;
        _snapshot.Dispose();
        _operationScope?.Dispose();
        _fontSources?.Dispose();
    }
}
