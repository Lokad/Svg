namespace Lokad.Svg;

using Lokad.Svg.Fonts;
using Lokad.Svg.Parsing;

/// <summary>Owns one prepared SVG/font operation and permits exactly one conversion under its shared work limit.</summary>
public sealed class SvgPreparedDocument : IDisposable
{
    private readonly ReadOnlyMemory<byte> _svg;
    private readonly SvgRenderOptions _options;
    private readonly SvgFontSnapshot _snapshot;
    private readonly ParserWorkCounter _workCounter;
    private int _state;

    private SvgPreparedDocument(ReadOnlyMemory<byte> svg, SvgRenderOptions options, SvgFontSnapshot snapshot, ParserWorkCounter workCounter)
    {
        _svg = svg;
        _options = options;
        _snapshot = snapshot;
        _workCounter = workCounter;
    }

    /// <summary>Discovers and prepares required direct-text fonts, retaining caller SVG/font bytes until this document is disposed or converted once.</summary>
    public static async ValueTask<SvgPreparedDocument> CreateAsync(ReadOnlyMemory<byte> svg, SvgFontPackCatalog catalog, ISvgFontProgramSourceProvider provider, SvgRenderOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(options);
        if (options.FontSnapshot is not null) throw new ArgumentException("Prepared document options must not supply a separate font snapshot.", nameof(options));
        var workCounter = new ParserWorkCounter(options.Limits.MaximumWorkUnits);
        SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync(svg, catalog, provider, options.Limits, workCounter, cancellationToken).ConfigureAwait(false);
        SvgRenderOptions preparedOptions = new(options.Viewport, options.OutputScale, options.BackgroundColor, options.Limits, snapshot);
        return new SvgPreparedDocument(svg, preparedOptions, snapshot, workCounter);
    }

    /// <summary>Converts this prepared document once and releases its retained font lease.</summary>
    public ReadOnlyMemory<byte> ConvertToPng(CancellationToken cancellationToken)
    {
        BeginConversion();
        try { return SvgConverter.ConvertToPng(_svg, _options, _workCounter, cancellationToken); }
        finally { _snapshot.Dispose(); }
    }

    /// <summary>Schedules this prepared document's one conversion and releases its retained font lease when the task completes.</summary>
    public Task<ReadOnlyMemory<byte>> ConvertToPngAsync(CancellationToken cancellationToken)
    {
        BeginConversion();
        return ConvertAsync(cancellationToken);
    }

    /// <summary>Releases the retained snapshot when no conversion will occur.</summary>
    public void Dispose()
    {
        if (Interlocked.CompareExchange(ref _state, 2, 0) == 0) _snapshot.Dispose();
    }

    private void BeginConversion()
    {
        if (Interlocked.CompareExchange(ref _state, 1, 0) != 0) throw new ObjectDisposedException(nameof(SvgPreparedDocument));
    }

    private async Task<ReadOnlyMemory<byte>> ConvertAsync(CancellationToken cancellationToken)
    {
        try { return await Task.Run(() => SvgConverter.ConvertToPng(_svg, _options, _workCounter, cancellationToken), cancellationToken).ConfigureAwait(false); }
        finally { _snapshot.Dispose(); }
    }
}
