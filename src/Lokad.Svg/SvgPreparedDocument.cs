namespace Lokad.Svg;

using Lokad.Svg.Fonts;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

/// <summary>Owns one prepared SVG/font operation and permits exactly one conversion under its shared work limit.</summary>
public sealed class SvgPreparedDocument : IDisposable
{
    private readonly SvgRenderOptions _options;
    private readonly SvgFontSnapshot _snapshot;
    private SvgRoot _root;
    private readonly RenderBudget _budget;
    private int _state;

    private SvgPreparedDocument(SvgRenderOptions options, SvgFontSnapshot snapshot, SvgRoot root, RenderBudget budget)
    {
        _options = options;
        _snapshot = snapshot;
        _root = root;
        _budget = budget;
    }

    /// <summary>Discovers required direct-text fonts and lowers the admitted scene, retaining verified font bytes until this document is disposed or converted once.</summary>
    public static async ValueTask<SvgPreparedDocument> CreateAsync(ReadOnlyMemory<byte> svg, SvgFontPackCatalog catalog, ISvgFontProgramSourceProvider provider, SvgRenderOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(options);
        if (options.FontSnapshot is not null) throw new ArgumentException("Prepared document options must not supply a separate font snapshot.", nameof(options));
        var workCounter = new ParserWorkCounter(options.Limits.MaximumWorkUnits);
        SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync(svg, catalog, provider, options.Limits, workCounter, cancellationToken).ConfigureAwait(false);
        try
        {
            SvgRenderOptions preparedOptions = new(options.Viewport, options.OutputScale, options.BackgroundColor, options.Limits, snapshot);
            RenderBudget budget = new(options.Limits.MaximumAccountedBytes);
            SvgAffineTransform outputMap = SvgConverter.TryResolveOutputMap(svg, options, budget, cancellationToken);
            SvgRoot root = SvgRootParser.Parse(svg, options.Limits, budget, snapshot, workCounter, cancellationToken, outputMap, options.Viewport);
            if (root.HasUnsupportedChild)
            {
                root.Dispose();
                throw new SvgException(SvgErrorCode.UnsupportedFeature, "This SVG child element is not implemented yet.");
            }
            return new SvgPreparedDocument(preparedOptions, snapshot, root, budget);
        }
        catch
        {
            snapshot.Dispose();
            throw;
        }
    }

    /// <summary>Converts this prepared document once and releases its retained font lease.</summary>
    public ReadOnlyMemory<byte> ConvertToPng(CancellationToken cancellationToken)
    {
        BeginConversion();
        try { return SvgConverter.ConvertToPng(_root, _options, _budget, cancellationToken); }
        finally { Release(); }
    }

    /// <summary>Schedules this prepared document's one conversion and releases its retained font lease when the task completes.</summary>
    public Task<ReadOnlyMemory<byte>> ConvertToPngAsync(CancellationToken cancellationToken)
    {
        BeginConversion();
        return ConvertAsync(cancellationToken);
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
    }
}
