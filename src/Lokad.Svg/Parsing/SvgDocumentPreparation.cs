using System.Xml;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Parsing;

/// <summary>Owns one document's reader workspace and collected styles through sizing and scene lowering.</summary>
/// <remarks>Conversion-thread ownership only. Readers and parser state borrow these settings/rules;
/// they must finish before disposal. All traversal shares the caller's work counter and cancellation.
/// Disposal releases preparation storage; a completed scene retains its own required resources.</remarks>
internal sealed class SvgDocumentPreparation : IDisposable
{
    private readonly ReadOnlyMemory<byte> _svg;
    private readonly SvgRenderLimits _limits;
    private readonly ParserWorkCounter _work;
    private readonly CancellationToken _token;
    private readonly RenderBudget.BudgetReservation _workspace;
    private SvgRootSyntax? _root;
    private bool _structureInspected;

    private SvgDocumentPreparation(ReadOnlyMemory<byte> svg, SvgRenderLimits limits, RenderBudget budget,
        ParserWorkCounter work, CancellationToken token, RenderBudget.BudgetReservation workspace, XmlReaderSettings settings)
    {
        _svg = svg; _limits = limits; _work = work; _token = token;
        _workspace = workspace; Settings = settings;
        Rules = new(budget, SvgMemoryCategory.Parser, work, token);
        Strings = new(budget);
    }

    internal XmlReaderSettings Settings { get; }
    internal BudgetedParserList<CssRule> Rules { get; }
    internal BudgetedParserStringStore Strings { get; }

    internal static SvgDocumentPreparation Create(ReadOnlyMemory<byte> svg, SvgRenderLimits limits,
        RenderBudget budget, ParserWorkCounter work, CancellationToken token)
    {
        if (svg.Length > limits.MaximumInputBytes) throw new SvgException(SvgErrorCode.LimitExceeded, "SVG input exceeds the configured input limit.");
        RenderBudget.BudgetReservation workspace = budget.Reserve(SvgRootParser.GetXmlReaderWorkspaceBytes(svg.Length, limits.MaximumAttributesPerElement), SvgMemoryCategory.Parser);
        SvgDocumentPreparation? result = null;
        try
        {
            XmlReaderSettings settings = SvgXmlReaderPolicy.Create(svg, limits, budget, work, token);
            result = new(svg, limits, budget, work, token, workspace, settings);
            SvgRootStyleSheetParser.Collect(svg, settings, limits, result.Rules, result.Strings, budget, work, token);
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
        if (root.ViewBox is not SvgViewBox box) return SvgAffineTransform.FromScale(options.OutputScale, options.OutputScale);
        (double scaleX, double scaleY, double offsetX, double offsetY) = SvgGeometrySyntax.MapViewBox(box, viewport.Width, viewport.Height);
        return SvgAffineTransform.FromScaleOffset(scaleX * options.OutputScale, scaleY * options.OutputScale, offsetX * options.OutputScale, offsetY * options.OutputScale);
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
                _work.Consume(_token);
                if (reader.NodeType != XmlNodeType.Element) continue;
                SvgStructuralAdmission.ValidateElementStructure(reader, _limits);
                if (nodes++ >= _limits.MaximumNodes) throw new SvgException(SvgErrorCode.LimitExceeded, "SVG node count exceeds the configured limit.");
                if (nodes != 1) continue;
                SvgStructuralAdmission.ThrowIfNotSvgRoot(reader);
                SvgElementAttributePolicy.ValidateRoot(reader, _work, _token);
                InlineStyle sheet = SvgRootStyleSheetParser.ResolveRoot(reader.GetAttribute("id"), reader.GetAttribute("class"), Rules, _work, _token);
                InlineStyle inline = SvgInlineStyleParser.Parse(reader.GetAttribute("style"), _work, _token, true);
                _root = SvgRootSyntax.ParseComputed(reader, inline, sheet, _work, _token);
                if (!inspectStructure) return _root.Value;
            }
            _structureInspected = true;
            return _root ?? throw new SvgException(SvgErrorCode.MalformedInput, "The document must contain an SVG root element.");
        }
    }

    public void Dispose()
    {
        Rules.Dispose();
        Strings.Dispose();
        _workspace.Dispose();
    }
}
