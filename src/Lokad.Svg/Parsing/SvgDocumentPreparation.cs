using System.Xml;
using Lokad.Svg.Fonts;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Parsing;

/// <summary>Owns one document's reader workspace and collected styles through sizing and scene lowering.</summary>
/// <remarks>Conversion-thread ownership only. Readers and parser state borrow these settings/rules;
/// they must finish before disposal. All traversal observes the caller's cancellation token.
/// Disposal releases preparation storage; a completed scene retains its own required resources.</remarks>
internal sealed class SvgDocumentPreparation : IDisposable
{
    private readonly ReadOnlyMemory<byte> _svg;
    private readonly SvgRenderLimits _limits;
    private readonly RenderBudget _budget;
    private readonly CancellationToken _token;
    private RenderBudget.BudgetReservation? _fontRequestsStorage;
    private readonly RenderBudget.BudgetReservation _workspace;
    private SvgRootSyntax? _root;
    private bool _structureInspected;
    private SvgInstanceFontDiscovery? _fontInstances;
    private SvgPaintResourceSelection? _paintResources;
    private bool _fontInstancesCollected;
    private bool _paintResourcesCollected;

    private SvgDocumentPreparation(ReadOnlyMemory<byte> svg, SvgRenderLimits limits, RenderBudget budget,
        CancellationToken token, RenderBudget.BudgetReservation workspace, XmlReaderSettings settings)
    {
        _svg = svg; _limits = limits; _budget = budget; _token = token;
        _workspace = workspace; Settings = settings;
        Rules = new(budget, SvgMemoryCategory.Parser, token);
        Strings = new(budget);
    }

    internal XmlReaderSettings Settings { get; }
    internal BudgetedParserList<CssRule> Rules { get; }
    internal BudgetedParserStringStore Strings { get; }

    /// <summary>Borrows the active preparation owner for font acquisition; callers keep this preparation alive until acquisition finishes.</summary>
    internal RenderBudget Budget => _budget;

    /// <summary>Borrows immutable requests within this single preparation; discovery/scalar collection may reuse them until disposal.</summary>
    internal SvgFontFaceRequest[]? FontRequests { get; private set; }

    /// <summary>Commits one completely constructed request array and its coarse owner; failures leave both with the caller.</summary>
    internal void RetainFontRequests(SvgFontFaceRequest[] requests, RenderBudget.BudgetReservation storage)
    {
        if (FontRequests is not null) throw new InvalidOperationException("This preparation already owns its font requirements.");
        FontRequests = requests;
        _fontRequestsStorage = storage;
    }

    /// <summary>Transfers request storage after acquisition; disposal no longer refunds the transferred live arrays.</summary>
    internal RenderBudget.BudgetReservation? DetachFontRequestsStorage()
    {
        RenderBudget.BudgetReservation? storage = _fontRequestsStorage;
        _fontRequestsStorage = null;
        return storage;
    }

    internal static SvgDocumentPreparation Create(ReadOnlyMemory<byte> svg, SvgRenderLimits limits,
        RenderBudget budget, CancellationToken token)
    {
        SvgPreparationProbe.Observe(SvgPreparationPhase.Workspace);
        if (svg.Length > limits.MaximumInputBytes) throw new SvgException(SvgErrorCode.LimitExceeded, "SVG input exceeds the configured input limit.");
        RenderBudget.BudgetReservation workspace = budget.Reserve(SvgRootParser.GetXmlReaderWorkspaceBytes(svg.Length, limits.MaximumAttributesPerElement), SvgMemoryCategory.Parser);
        SvgDocumentPreparation? result = null;
        try
        {
            XmlReaderSettings settings = SvgXmlReaderPolicy.Create(svg, limits, budget, token);
            result = new(svg, limits, budget, token, workspace, settings);
            SvgRootStyleSheetParser.Collect(svg, settings, limits, result.Rules, result.Strings, budget, token);
            return result;
        }
        catch (Exception exception)
        {
            if (result is null) workspace.Dispose();
            else result.Dispose();
            if (exception is XmlException xml) throw new SvgException(SvgErrorCode.MalformedInput, "SVG XML is malformed: " + xml.Message);
            throw;
        }
    }

    /// <summary>Discovers requests under this preparation's active owner, reusing its styles and reader workspace.</summary>
    internal SvgFontFaceRequest[] DiscoverFontRequests()
        => SvgFontRequestDiscovery.Discover(_svg, _limits, _budget, this, _token);

    /// <summary>Returns caller-owned scalar coverage charged to this preparation budget; keep it alive until acquisition finishes.</summary>
    internal SvgFontCoverageDiscovery DiscoverFontCoverage()
        => SvgFontRequestDiscovery.DiscoverWithCoverage(_svg, _limits, _budget, this, _token);

    /// <summary>Borrows the retained admitted dependency graph; this preparation owns it through acquisition and lowering.</summary>
    internal SvgInstanceFontDiscovery? GetFontInstances()
    {
        if (!_fontInstancesCollected)
        {
            _fontInstances = SvgInstanceFontDiscovery.Collect(_svg, Settings, _limits, Rules, Strings, _budget, _token);
            _fontInstancesCollected = true;
        }
        return _fontInstances;
    }

    /// <summary>Borrows the resolved resource selection shared by discovery and lowering; no caller may dispose it.</summary>
    internal SvgPaintResourceSelection? GetPaintResources()
    {
        if (!_paintResourcesCollected)
        {
            ReadOnlySpan<byte> bytes = _svg.Span;
            SvgInstanceFontDiscovery? consumers = _fontInstancesCollected ? _fontInstances
                : bytes.Contains((byte)0) || bytes.IndexOf("pattern"u8) >= 0 ? GetFontInstances() : null;
            _paintResources = SvgPaintResourceSelection.Create(_svg, Settings, _limits, Rules, Strings, _budget, _token, consumers);
            _paintResourcesCollected = true;
        }
        return _paintResources;
    }

    internal SvgDocumentDimensions InspectDimensions(SvgRenderOptions options)
    {
        SvgRootSyntax root = ReadRoot(true);
        return SvgDocumentSizing.Resolve(options.Viewport, root.Width, root.Height, root.ViewBox);
    }

    internal SvgRenderOptions ResolveRasterOptions(SvgRenderOptions options)
    {
        if (options.RasterBounds is null || options.ResolvedRasterSize is not null) return options;
        SvgDocumentDimensions dimensions = InspectDimensions(options);
        return options.WithRasterSize(dimensions.GetRasterSize(options.OutputScale, options.RasterBounds, options.Limits));
    }

    internal SvgAffineTransform GetOutputMap(SvgRenderOptions options)
    {
        SvgRootSyntax root = ReadRoot(false);
        SvgViewport viewport = SvgDocumentSizing.Resolve(options.Viewport, root.Width, root.Height, root.ViewBox).LogicalViewport;
        return SvgRootImageMap.Resolve(root.ViewBox, viewport, options.OutputScale).Drawing.Parameter;
    }

    private SvgRootSyntax ReadRoot(bool inspectStructure)
    {
        try { return ReadCore(); }
        catch (XmlException exception) { throw new SvgException(SvgErrorCode.MalformedInput, "SVG XML is malformed: " + exception.Message); }

        SvgRootSyntax ReadCore()
        {
            if (_root is SvgRootSyntax cached && (!inspectStructure || _structureInspected)) return cached;
            using ReadOnlyMemoryStream stream = new(_svg);
            using XmlReader reader = XmlReader.Create(stream, Settings);
            int nodes = 0;
            while (reader.Read())
            {
                _token.ThrowIfCancellationRequested();
                if (reader.NodeType != XmlNodeType.Element) continue;
                SvgStructuralAdmission.ValidateElementStructure(reader, _limits);
                if (nodes++ >= _limits.MaximumNodes) throw new SvgException(SvgErrorCode.LimitExceeded, "SVG node count exceeds the configured limit.");
                if (nodes != 1) continue;
                SvgStructuralAdmission.ThrowIfNotSvgRoot(reader);
                SvgElementAttributePolicy.ValidateRoot(reader, _token);
                InlineStyle sheet = SvgRootStyleSheetParser.ResolveRoot(reader.GetAttribute("id"), reader.GetAttribute("class"), Rules, _token);
                InlineStyle inline = SvgInlineStyleParser.Parse(reader.GetAttribute("style"), _token, true);
                _root = SvgRootSyntax.ParseComputed(reader, inline, sheet, _token);
                if (!inspectStructure) return _root.Value;
            }
            _structureInspected = true;
            return _root ?? throw new SvgException(SvgErrorCode.MalformedInput, "The document must contain an SVG root element.");
        }
    }

    public void Dispose()
    {
        _fontRequestsStorage?.Dispose();
        _fontRequestsStorage = null;
        FontRequests = null;
        _fontInstances?.Dispose();
        _paintResources?.Dispose();
        Rules.Dispose();
        Strings.Dispose();
        _workspace.Dispose();
    }
}
