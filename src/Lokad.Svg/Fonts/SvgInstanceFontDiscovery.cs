using System.Xml;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Fonts;

/// <summary>Selects font-bearing source dependencies for request discovery and scene lowering.</summary>
/// <remarks>Mask and marker resources retain their own inherited context; local use targets inherit
/// the consuming instance. One parser-thread operation owns the bounded token/graph storage and
/// shares work/cancellation. Paint metadata is borrowed through all visits. Disposal releases only
/// owned storage. Document preparation retains the admitted graph through acquisition and lowering;
/// standalone discovery releases it on return. The index never opens programs or constructs render geometry.</remarks>
internal sealed class SvgInstanceFontDiscovery : IDisposable
{
    private enum Kind { Container, Definition, Opaque, Text, Use, Geometry, Mask, Marker }
    private enum VisitKind { Enter, Children, Exit }
    private readonly record struct Node(Kind Kind, SvgRetainedTextSource? Source, string? Target, int End, bool Symbol, bool Switch, bool Language);
    private readonly record struct Open(int Depth, int Index, bool CanDefine);
    private readonly record struct Pending(string Target, SvgInheritedContainer Context);
    private readonly record struct Visit(int Index, SvgInheritedContainer Context, VisitKind Kind, int Next, bool Target);
    private readonly record struct ResourceContext(SvgInheritedContainer Inherited, bool ScalesWithStrokeWidth);
    private readonly record struct ResourceDeclaration(int Index, SvgInheritedContainer Context, bool DefinitionsDisplayed);
    private readonly record struct SourceVisit(int Index, SvgInheritedContainer Context, VisitKind Kind, int Next, bool Target, bool HasObject, bool HasPaint);
    private readonly SvgDocumentState _state;
    private readonly SvgRenderLimits _limits;
    private readonly ParserWorkCounter _work;
    private readonly CancellationToken _token;
    private readonly bool _markerGeometry;
    private readonly bool _maskGeometry;
    private readonly bool _paintGeometry;
    private readonly BudgetedParserList<Node> _nodes;
    private readonly BudgetedParserDictionary<string, int> _ids;
    private readonly BudgetedParserList<Pending> _pending;
    private readonly BudgetedParserList<int> _pendingMasks;
    private readonly BudgetedParserDictionary<int, SvgInheritedContainer> _maskContexts;
    private readonly BudgetedParserDictionary<(int Line, int Position), int> _maskTargets;

    private SvgInstanceFontDiscovery(SvgRenderLimits limits, BudgetedParserList<CssRule> rules,
        BudgetedParserStringStore strings, RenderBudget budget, ParserWorkCounter work, CancellationToken token, bool markerGeometry, bool maskGeometry, bool paintGeometry)
    {
        _limits = limits; _work = work; _token = token; _markerGeometry = markerGeometry; _maskGeometry = maskGeometry; _paintGeometry = paintGeometry;
        _state = new(budget, work, token, rules, strings, false);
        _nodes = new(budget, SvgMemoryCategory.Parser, work, token);
        _ids = new(budget, SvgMemoryCategory.Parser, 96);
        _pending = new(budget, SvgMemoryCategory.Parser, work, token);
        _pendingMasks = new(budget, SvgMemoryCategory.Parser, work, token);
        _maskContexts = new(budget, SvgMemoryCategory.Parser, 320);
        _maskTargets = new(budget, SvgMemoryCategory.Parser, 96);
    }

    internal static SvgInstanceFontDiscovery? Collect(ReadOnlyMemory<byte> svg, XmlReaderSettings settings, SvgRenderLimits limits,
        BudgetedParserList<CssRule> rules, BudgetedParserStringStore strings, RenderBudget budget, ParserWorkCounter work, CancellationToken token)
    {
        ReadOnlySpan<byte> bytes = svg.Span;
        if (!bytes.Contains((byte)0) && (bytes.IndexOf("text"u8) < 0 || bytes.IndexOf("use"u8) < 0 && bytes.IndexOf("mask"u8) < 0)
            && (bytes.IndexOf("marker"u8) < 0 || bytes.IndexOf("ex"u8) < 0)
            && (bytes.IndexOf("pattern"u8) < 0 || bytes.IndexOf("text"u8) < 0 && (bytes.IndexOf("marker"u8) < 0 || bytes.IndexOf("ex"u8) < 0))) return null;
        SvgPreparationProbe.Observe(SvgPreparationPhase.FontIndex);
        SvgInstanceFontDiscovery result = new(limits, rules, strings, budget, work, token,
            bytes.Contains((byte)0) || bytes.IndexOf("marker"u8) >= 0,
            bytes.Contains((byte)0) || bytes.IndexOf("mask"u8) >= 0,
            bytes.Contains((byte)0) || bytes.IndexOf("pattern"u8) >= 0);
        try { Read(result, svg, settings); return result; }
        catch { result.Dispose(); throw; }

        static void Read(SvgInstanceFontDiscovery owner, ReadOnlyMemory<byte> svg, XmlReaderSettings settings)
        {
            using ReadOnlyMemoryStream stream = new(svg);
            using XmlReader reader = XmlReader.Create(stream, settings);
            using BudgetedParserList<Open> open = new(owner._state.Budget, SvgMemoryCategory.Parser, owner._work, owner._token);
            while (reader.Read())
            {
                owner._work.Consume(owner._token);
                owner._state.CssAncestors.Observe(reader, 0);
                if (reader.NodeType == XmlNodeType.EndElement)
                {
                    if (open.Count != 0 && open[^1].Depth == reader.Depth)
                    {
                        int start = open[^1].Index; open.RemoveLast();
                        owner._nodes[start] = owner._nodes[start] with { End = owner._nodes.Count };
                    }
                    continue;
                }
                if (reader.NodeType != XmlNodeType.Element) continue;
                SvgStructuralAdmission.ValidateElementStructure(reader, owner._limits);
                if (++owner._state.NodeCount > owner._limits.MaximumNodes) throw new SvgException(SvgErrorCode.LimitExceeded, "Font source collection exceeds the node limit.");
                bool svgElement = reader.NamespaceURI == "http://www.w3.org/2000/svg";
                Kind kind = !svgElement ? Kind.Opaque : reader.LocalName switch
                {
                    "g" or "a" or "svg" or "switch" => Kind.Container,
                    "defs" or "symbol" or "pattern" => Kind.Definition,
                    "mask" when owner._maskGeometry => Kind.Mask,
                    "marker" when owner._paintGeometry => Kind.Marker,
                    "text" => Kind.Text, "use" => Kind.Use,
                    "line" or "path" or "polygon" or "polyline" when owner._markerGeometry || owner._maskGeometry || owner._paintGeometry => Kind.Geometry,
                    "rect" or "circle" or "ellipse" or "image" when owner._maskGeometry || owner._paintGeometry => Kind.Geometry, _ => Kind.Opaque
                };
                bool switchChild = open.Count != 0 && owner._nodes[open[^1].Index].Switch && reader.Depth == open[^1].Depth + 1;
                if (reader.IsEmptyElement && !switchChild && (kind == Kind.Opaque || svgElement && reader.LocalName == "pattern")) continue;
                int index = owner._nodes.Count;
                if (kind == Kind.Container && SvgLocalUrl.ReadReference(reader, "mask") is not null && reader is IXmlLineInfo location && location.HasLineInfo())
                    owner._maskTargets.TryAdd((location.LineNumber, location.LinePosition), index);
                bool symbol = svgElement && reader.LocalName == "symbol", isSwitch = svgElement && reader.LocalName == "switch";
                bool language = reader.GetAttribute("systemLanguage") is not null;
                bool canDefine = kind != Kind.Opaque && (open.Count == 0 || open[^1].CanDefine);
                string? id = canDefine && (kind != Kind.Definition || symbol || reader.LocalName is "pattern" or "marker") ? reader.GetAttribute("id") : null;
                if (id is not null) owner._ids.TryAdd(owner._state.Strings.Copy(id), index);
                string? target = kind == Kind.Use ? owner.Target(reader) : null;
                SvgRetainedTextSource? source = null;
                bool empty = reader.IsEmptyElement;
                int depth = reader.Depth;
                if (kind != Kind.Opaque)
                {
                    InlineStyle sheet = SvgStyleSheet.Resolve(reader, owner._state.StyleRules, owner._state.CssAncestors, owner._work, owner._token);
                    SvgPaintableElementContext context = new(new(SvgPaint.Default, true, true, SvgAffineTransform.Identity), SvgInheritedTextStyle.Default,
                        SvgInheritedStrokeStyle.Default, sheet, SvgRootParser.CssNamedColors, owner._work, owner._token, 1, null);
                    source = kind == Kind.Text ? SvgRetainedTextSource.Capture(reader, context, owner._state, owner._limits)
                        : SvgRetainedTextSource.CaptureElement(reader, context, owner._state, owner._limits);
                    if (kind == Kind.Text)
                    { owner._state.NodeCount += source.ChildNodeCount; owner._state.TextCharacterCount += source.CharacterCount; }
                }
                owner._nodes.Add(new(kind, source, target, index + 1, symbol, isSwitch, language));
                if (!empty && kind != Kind.Text) open.Add(new(depth, index, canDefine));
            }
        }
    }


    /// <summary>Visits actual paint consumers on the existing retained source graph before acquisition.</summary>
    /// <remarks>Resources are borrowed; callbacks borrow IDs synchronously. All temporary state uses
    /// the caller's budget/work/token. This visit constructs neither glyphs nor geometry.</remarks>
    internal void SelectPaints(SvgPaintResourceSelection resources, Action<string> addPaint, Func<string, string?> patternSource)
    {
        _state.PaintResources = resources;
        if (_nodes.Count == 0) return;
        using BudgetedParserDictionary<int, ResourceContext> resourceContexts = new(_state.Budget, SvgMemoryCategory.Parser,
            checked(System.Runtime.CompilerServices.Unsafe.SizeOf<ResourceContext>() + 64));
        using BudgetedParserList<Visit> stack = new(_state.Budget, SvgMemoryCategory.Parser, _work, _token);
        using BudgetedParserDictionary<int, bool> active = new(_state.Budget, SvgMemoryCategory.Parser, 64);
        using BudgetedParserList<int> pending = new(_state.Budget, SvgMemoryCategory.Parser, _work, _token);
        using BudgetedParserDictionary<int, bool> selected = new(_state.Budget, SvgMemoryCategory.Parser, 64);
        SvgInheritedContainer initial = new(new(SvgPaint.Default, true, true, SvgAffineTransform.Identity),
            SvgInheritedTextStyle.Default, null, 255, SvgInheritedStrokeStyle.Default);
        // Register resources in their own lexical presentation, independently of consumers.
        // Local-use targets below will instead inherit from their consuming instance.
        using (BudgetedParserList<ResourceDeclaration> declarations = new(_state.Budget, SvgMemoryCategory.Parser, _work, _token))
        {
            declarations.Add(new(0, initial, true));
            while (declarations.Count != 0)
            {
                _work.Consume(_token);
                ResourceDeclaration declaration = declarations[^1]; declarations.RemoveLast(); Node node = _nodes[declaration.Index];
                if (node.Kind == Kind.Opaque) continue;
                using SvgRetainedTextSource.Reader reader = Open(node);
                SvgInheritedContainer inherited = declaration.Context;
                if (node.Kind is Kind.Mask or Kind.Marker)
                    inherited = inherited with { Presentation = inherited.Presentation with
                        { IsDisplayed = declaration.DefinitionsDisplayed, Transform = SvgAffineTransform.Identity, TextMetrics = default } };
                SvgInheritedContainer resolved = Resolve(reader, inherited, out SvgComputedStyle computed);
                bool strokeWidth = false;
                if (node.Kind == Kind.Marker)
                {
                    SvgMarkerDefinition marker = SvgMarkerSyntax.Parse(reader, computed, new(100, 100), inherited.TextStyle, reader.StyleSheet, _work, _token);
                    strokeWidth = marker.StrokeWidthUnits;
                    // Marker definitions ignore their own display; their containing defs and viewport still control availability.
                    resolved = resolved with { Presentation = resolved.Presentation with
                        { IsDisplayed = declaration.DefinitionsDisplayed && marker.Width != 0 && marker.Height != 0 } };
                }
                if (reader.LocalName is "pattern" or "mask" or "marker") resourceContexts.TryAdd(declaration.Index, new(resolved, strokeWidth));
                bool definitionsDisplayed = declaration.DefinitionsDisplayed && (reader.LocalName != "defs" || resolved.Presentation.ElementDisplay != SvgDisplay.None);
                for (int child = declaration.Index + 1; child < node.End; child = _nodes[child].End)
                    declarations.Add(new(child, resolved, definitionsDisplayed));
            }
        }
        Action<string> observePaint = id => QueueId(id, true);
        Action<XmlReader, SvgInheritedContainer, string?> observeTextMask = (element, context, filter) => Queue(SvgLocalUrl.ReadReference(element, "mask"), false);
        Read(0, initial, false);
        for (int next = 0; next < pending.Count; next++)
        {
            _work.Consume(_token);
            int index = pending[next];
            if (resourceContexts.TryGetValue(index, out ResourceContext context)) Read(index, context.Inherited, true);
        }

        SvgRetainedTextSource.Reader Open(Node node)
        {
            SvgRetainedTextSource source = node.Source ?? throw new InvalidOperationException("A paint consumer requires retained tokens.");
            SvgRetainedTextSource.Reader reader = source.OpenReader(0, _work, _token);
            reader.MoveToContent(); return reader;
        }
        SvgInheritedContainer Resolve(XmlReader reader, SvgInheritedContainer inherited, out SvgComputedStyle computed)
        {
            bool geometry = SvgStructuralVocabulary.IsDefinitionLeaf(reader.LocalName);
            InlineStyle inline = geometry ? SvgInlineStyleParser.ParseGeometry(reader.GetAttribute("style"), _work, _token)
                : SvgInlineStyleParser.Parse(reader.GetAttribute("style"), _work, _token, true);
            InlineStyle sheet = reader is SvgRetainedTextSource.Reader retained ? retained.StyleSheet : default;
            computed = SvgComputedStyle.Resolve(reader, inline, sheet);
            if (geometry)
                return SvgContainerInheritance.ResolvePaint(reader, computed, inherited, SvgRootParser.CssNamedColors, resources, _work, _token);
            return SvgContainerInheritance.Resolve(reader, inline, computed, sheet,
                inherited.Presentation, inherited.FillRule, inherited.TextStyle, inherited.Stroke,
                SvgRootParser.CssNamedColors, resources, _work, _token);
        }
        void Queue(string? value, bool paint)
        {
            if (value is null || !SvgLocalUrl.TryParseId(value, out string id)) return;
            QueueId(id, paint);
        }
        void QueueId(string id, bool paint)
        {
            _work.Consume(_token);
            if (paint)
            {
                addPaint(id);
                if (patternSource(id) is not {} sourceId) return;
                id = sourceId;
            }
            if (_ids.TryGetValue(id, out int index) && resourceContexts.TryGetValue(index, out _) && selected.TryAdd(index, true))
                pending.Add(index);
        }
        void QueueMarker(string? value, SvgInheritedStrokeStyle stroke)
        {
            if (value is null || !SvgLocalUrl.TryParseId(value, out string id)) return;
            _work.Consume(_token);
            if (!_ids.TryGetValue(id, out int index) || _nodes[index].Kind != Kind.Marker
                || !resourceContexts.TryGetValue(index, out ResourceContext context) || !context.Inherited.Presentation.IsDisplayed
                || context.ScalesWithStrokeWidth && SvgStrokeStyleSyntax.IsZeroStrokeWidth(stroke.Width)) return;
            QueueId(id, false);
        }
        void Read(int root, SvgInheritedContainer inherited, bool resource)
        {
            stack.Add(new(root, inherited, VisitKind.Enter, 0, resource));
            while (stack.Count != 0)
            {
                _work.Consume(_token);
                Visit visit = stack[^1]; Node node = _nodes[visit.Index];
                if (visit.Kind == VisitKind.Exit || visit.Kind == VisitKind.Children && visit.Next >= node.End)
                { active.Replace(visit.Index, false); stack.RemoveLast(); continue; }
                if (visit.Kind == VisitKind.Children)
                {
                    int child = visit.Next;
                    stack[^1] = visit with { Next = node.Switch && !_nodes[child].Language ? node.End : _nodes[child].End };
                    if (!node.Switch || !_nodes[child].Language) stack.Add(new(child, visit.Context, VisitKind.Enter, 0, false));
                    continue;
                }
                if (node.Kind == Kind.Opaque || node.Kind == Kind.Definition && !(node.Symbol && visit.Target || resource && visit.Index == root)
                    || node.Kind is Kind.Mask or Kind.Marker && !(resource && visit.Index == root))
                { stack.RemoveLast(); continue; }
                // Selection follows dependencies once; the existing lowering owner retains cycle admission/diagnostics.
                if (active.TryGetValue(visit.Index, out bool visiting) && visiting)
                { stack.RemoveLast(); continue; }
                if (!active.TryAdd(visit.Index, true)) active.Replace(visit.Index, true);
                stack[^1] = visit with { Kind = VisitKind.Exit };
                using SvgRetainedTextSource.Reader reader = Open(node);
                SvgInheritedContainer resolved;
                SvgComputedStyle computed;
                if (resource && visit.Index == root)
                {
                    resolved = visit.Context;
                    InlineStyle inline = SvgInlineStyleParser.Parse(reader.GetAttribute("style"), _work, _token, true);
                    computed = SvgComputedStyle.Resolve(reader, inline, reader.StyleSheet);
                }
                else resolved = Resolve(reader, visit.Context, out computed);
                if (!resolved.Presentation.IsDisplayed || resolved.Presentation.ElementOpacity.Value == 0 || resolved.Presentation.TextMetrics.CanSuppress) continue;
                if (reader.LocalName == "svg" && visit.Index != 0)
                {
                    SvgNestedViewportSyntax viewport = SvgNestedViewportSyntax.Parse(reader, 100, 100);
                    if (viewport.Width == 0 || viewport.Height == 0) continue;
                }
                if (node.Kind == Kind.Use && node.Target is {} target)
                {
                    if (!HasEmptyViewport(reader, target) || SvgLocalUrl.GetReference(computed.Filter) is not null)
                        Queue(SvgLocalUrl.ReadReference(reader, "mask"), false);
                    if (!HasEmptyViewport(reader, target) && _ids.TryGetValue(target, out int index))
                        stack.Add(new(index, resolved, VisitKind.Enter, 0, true));
                    continue;
                }
                if (node.Kind == Kind.Text)
                {
                    AddText(reader, visit.Context, static _ => { }, observePaint, static _ => { }, observeTextMask, null);
                    continue;
                }
                if (node.Kind == Kind.Geometry)
                {
                    if (!SvgMaskSyntax.HasPaintableTarget(reader, resolved.Presentation.IsVisible, computed.Filter)) continue;
                    bool fill = resolved.Presentation.PaintServerFill is not null && resolved.Presentation.Paint.HasFill && resolved.Presentation.Paint.FillOpacity.Value != 0;
                    bool stroke = SvgGradientSyntax.IsGradientStrokeUrl(resolved.Stroke.Stroke) && SvgColorParser.ParsePaintOpacity(resolved.Stroke.StrokeOpacity).Value != 0 && !SvgStrokeStyleSyntax.IsZeroStrokeWidth(resolved.Stroke.Width);
                    if (resolved.Presentation.IsVisible && (fill || stroke))
                    {
                        SvgGeometryPaintChannels channels = SvgGeometrySyntax.GetPaintChannels(reader, _limits.MaximumPathCommands, _state.Budget, _work, _token);
                        if (fill && (channels & SvgGeometryPaintChannels.Fill) != 0) Queue(resolved.Presentation.PaintServerFill, true);
                        if (stroke && (channels & SvgGeometryPaintChannels.Stroke) != 0) Queue(resolved.Stroke.Stroke, true);
                    }
                    if (resolved.Presentation.IsVisible && resolved.Presentation.Markers.HasMarkers)
                    {
                        SvgMarkerStyle markers = resolved.Presentation.Markers;
                        if (reader.LocalName == "line") { QueueMarker(markers.Start, resolved.Stroke); QueueMarker(markers.End, resolved.Stroke); }
                        else if (reader.LocalName is "path" or "polygon" or "polyline")
                        {
                            using BudgetedParserList<SvgMarkerPosition> positions = SvgMarkerVertices.Read(reader, _limits.MaximumPathCommands, _state, _work, _token);
                            foreach (SvgMarkerPosition position in positions.AsSpan())
                            {
                                _work.Consume(_token);
                                QueueMarker(position.Role == SvgMarkerVertexRole.Start ? markers.Start : position.Role == SvgMarkerVertexRole.Mid ? markers.Mid : markers.End, resolved.Stroke);
                            }
                        }
                    }
                    Queue(SvgLocalUrl.ReadReference(reader, "mask"), false);
                    continue;
                }
                Queue(SvgLocalUrl.ReadReference(reader, "mask"), false);
                stack[^1] = visit with { Context = resolved, Kind = VisitKind.Children, Next = visit.Index + 1 };
            }
        }

    }

    /// <summary>Borrows metadata owned by the enclosing discovery/preparation operation.</summary>
    internal SvgPaintResourceSelection Resources { set => _state.PaintResources = value; }

    internal void AddUse(XmlReader reader, SvgInheritedContainer context)
    {
        if (context.Presentation.IsDisplayed && Target(reader) is {} target && !HasEmptyViewport(reader, target)) _pending.Add(new(target, context));
    }

    private bool HasEmptyViewport(XmlReader reader, string target)
    {
        if (!_ids.TryGetValue(target, out int index) || !_nodes[index].Symbol) return false;
        _work.Consume(_token);
        // A 100-unit base preserves the authored percentage number; only an
        // exact zero is classified without needing the consumer's viewport size.
        SvgViewportLengthBases dimensions = SvgLocalReferenceSyntax.ParseDimensions(reader, 100, 100);
        return dimensions.Width == 0 || dimensions.Height == 0;
    }

    /// <summary>Retains an admitted mask's own inherited context, independently of its rendered consumers.</summary>
    internal void RegisterMask(XmlReader reader, SvgInheritedContainer inherited, InlineStyle sheet, bool definitionResourcesDisplayed, SvgViewportLengthBases bases)
    {
        SvgElementAttributePolicy.ValidateMask(reader, _work, _token);
        if (reader.GetAttribute("id") is not {} id || !_ids.TryGetValue(id, out int index) || _nodes[index].Kind != Kind.Mask) return;
        _work.Consume(_token);
        InlineStyle inline = SvgInlineStyleParser.Parse(reader.GetAttribute("style"), _work, _token, true);
        SvgComputedStyle style = SvgComputedStyle.Resolve(reader, inline, sheet);
        SvgInheritedContainer resolved = SvgContainerInheritance.Resolve(reader, inline, style, sheet,
            inherited.Presentation with { IsDisplayed = definitionResourcesDisplayed, Transform = SvgAffineTransform.Identity, TextMetrics = default }, inherited.FillRule,
            inherited.TextStyle, inherited.Stroke, SvgRootParser.CssNamedColors, _state.PaintResources, _work, _token);
        SvgClipBox? fractions = SvgMaskSyntax.ParseMaskFractionRegion(reader);
        SvgClipBox? region = fractions ?? SvgMaskSyntax.ParseMaskRegion(reader, bases);
        if (resolved.Presentation.IsDisplayed && region is not { Width: 0 } and not { Height: 0 })
            _maskContexts.TryAdd(index, resolved);
    }

    /// <summary>Queues a displayed target's mask, including filter output from an empty use source; font programs remain borrowed.</summary>
    internal void AddMask(XmlReader reader, SvgInheritedContainer context, string? ownFilter)
    {
        if (!context.Presentation.IsDisplayed || SvgLocalUrl.ReadReference(reader, "mask") is not {} value) return;
        if (SvgStructuralVocabulary.IsGroupOrLinkContainer(reader.LocalName) && SvgLocalUrl.GetReference(ownFilter) is null
            && reader is IXmlLineInfo location && location.HasLineInfo()
            && _maskTargets.TryGetValue((location.LineNumber, location.LinePosition), out int targetIndex)
            && !HasSource(this, targetIndex, context)) return;
        if (!SvgMaskSyntax.HasPaintableTarget(reader, context.Presentation.IsVisible, ownFilter)) return;
        if (SvgLocalUrl.GetReference(ownFilter) is null && reader.LocalName == "use" && Target(reader) is {} target && HasEmptyViewport(reader, target)) return;
        _work.Consume(_token);
        if (SvgLocalUrl.TryParseId(value, out string id) && _ids.TryGetValue(id, out int index) && _nodes[index].Kind == Kind.Mask)
            _pendingMasks.Add(index);

        // This visit borrows the already retained graph. No glyphs or programs are
        // constructed; object presence and possible paint stay independent so a
        // descendant's empty-source filter cannot make a masked ancestor paint.
        static bool HasSource(SvgInstanceFontDiscovery owner, int root, SvgInheritedContainer inherited)
        {
            if (owner._nodes[root].End == root + 1) return false;
            using BudgetedParserList<SourceVisit> stack = new(owner._state.Budget, SvgMemoryCategory.Parser, owner._work, owner._token);
            using BudgetedParserDictionary<int, bool> active = new(owner._state.Budget, SvgMemoryCategory.Parser, 64);
            stack.Add(new(root, inherited, VisitKind.Enter, 0, false, false, false));
            while (stack.Count != 0)
            {
                owner._work.Consume(owner._token);
                SourceVisit visit = stack[^1]; Node node = owner._nodes[visit.Index];
                if (visit.Kind == VisitKind.Exit || visit.Kind == VisitKind.Children && visit.Next >= node.End)
                {
                    if (active.TryGetValue(visit.Index, out _)) active.Replace(visit.Index, false);
                    stack.RemoveLast();
                    if (stack.Count == 0) return visit.HasObject && visit.HasPaint;
                    SourceVisit parent = stack[^1];
                    stack[^1] = parent with { HasObject = parent.HasObject || visit.HasObject, HasPaint = parent.HasPaint || visit.HasPaint };
                    continue;
                }
                if (visit.Kind == VisitKind.Children)
                {
                    int child = visit.Next;
                    stack[^1] = visit with { Next = node.Switch && !owner._nodes[child].Language ? node.End : owner._nodes[child].End };
                    if (!node.Switch || !owner._nodes[child].Language) stack.Add(new(child, visit.Context, VisitKind.Enter, 0, false, false, false));
                    continue;
                }
                stack[^1] = visit with { Kind = VisitKind.Exit };
                if (node.Kind is Kind.Opaque or Kind.Mask or Kind.Marker || node.Kind == Kind.Definition && (!node.Symbol || !visit.Target)) continue;
                if (active.TryGetValue(visit.Index, out bool visiting) && visiting)
                    throw new SvgException(SvgErrorCode.UnsupportedFeature, "Cyclic local text source references are not implemented.");
                if (!active.TryAdd(visit.Index, true)) active.Replace(visit.Index, true);
                SvgRetainedTextSource source = node.Source ?? throw new InvalidOperationException("A mask target requires retained tokens.");
                using SvgRetainedTextSource.Reader reader = source.OpenReader(0, owner._work, owner._token); reader.MoveToContent();
                InlineStyle inline = SvgInlineStyleParser.Parse(reader.GetAttribute("style"), owner._work, owner._token, true);
                SvgComputedStyle computed = SvgComputedStyle.Resolve(reader, inline, reader.StyleSheet);
                SvgInheritedContainer resolved = visit.Index == root ? visit.Context : SvgContainerInheritance.Resolve(reader, inline, computed, reader.StyleSheet,
                    visit.Context.Presentation, visit.Context.FillRule, visit.Context.TextStyle, visit.Context.Stroke,
                    SvgRootParser.CssNamedColors, owner._state.PaintResources, owner._work, owner._token);
                if (!resolved.Presentation.IsDisplayed || resolved.Presentation.TextMetrics.CanSuppress) continue;
                bool filter = SvgLocalUrl.GetReference(computed.Filter) is not null;
                if (node.Kind == Kind.Text)
                {
                    // Nonempty text may override visibility in spans; metrics stay
                    // with ordinary text preparation rather than this source test.
                    stack[^1] = stack[^1] with { HasObject = source.CharacterCount != 0, HasPaint = source.CharacterCount != 0 };
                    continue;
                }
                if (node.Kind == Kind.Geometry)
                {
                    bool geometry = SvgMaskSyntax.HasPaintableTarget(reader, true, null);
                    stack[^1] = stack[^1] with { HasObject = geometry, HasPaint = geometry && (resolved.Presentation.IsVisible || filter) };
                    continue;
                }
                stack[^1] = stack[^1] with { Context = resolved, HasPaint = filter };
                if (node.Kind == Kind.Use)
                {
                    if (node.Target is {} target && !owner.HasEmptyViewport(reader, target) && owner._ids.TryGetValue(target, out int index))
                        stack.Add(new(index, resolved, VisitKind.Enter, 0, true, false, false));
                }
                else stack[^1] = stack[^1] with { Kind = VisitKind.Children, Next = visit.Index + 1 };
            }
            return false;
        }
    }

    private string? Target(XmlReader reader)
    {
        _work.Consume(_token);
        string? href = SvgLocalReferenceSyntax.ReadHref(reader);
        if (href is null || href.Length < 2 || href[0] != '#') return null;
        if (href.Length - 1 > SvgSyntaxLimits.MaximumIdentifierCharacters) throw new SvgException(SvgErrorCode.LimitExceeded, "Local font-source identifiers exceed the syntax limit.");
        return SvgLocalUrl.CopyFragment(href.AsSpan(1), _state.Strings);
    }

    /// <summary>Emits referenced face/paint/filter requests before font acquisition.</summary>
    /// <remarks>Callbacks borrow strings for this synchronous visit and must copy retained values.
    /// Traversal shares the caller's bounded work, storage and cancellation; no font program is opened.</remarks>
    internal void AddRequests(Action<SvgFontFaceRequest> addFace, Action<string> addPaint, Action<string> addFilter, Action<SvgMarkerStyle> addMarkers, SvgFontCoverageDiscovery? coverage)
    {
        using BudgetedParserList<Visit> stack = new(_state.Budget, SvgMemoryCategory.Parser, _work, _token);
        using BudgetedParserDictionary<int, bool> active = new(_state.Budget, SvgMemoryCategory.Parser, 64);
        foreach (Pending pending in _pending.AsSpan())
        {
            _work.Consume(_token);
            if (!_ids.TryGetValue(pending.Target, out int root)) continue;
            Read(root, pending.Context, false);
        }
        using BudgetedParserDictionary<int, bool> selectedMasks = new(_state.Budget, SvgMemoryCategory.Parser, 64);
        for (int pending = 0; pending < _pendingMasks.Count; pending++)
        {
            _work.Consume(_token);
            int root = _pendingMasks[pending];
            if (selectedMasks.TryAdd(root, true) && _maskContexts.TryGetValue(root, out SvgInheritedContainer context)) Read(root, context, true);
        }

        void Read(int root, SvgInheritedContainer inherited, bool mask)
        {
            stack.Add(new(root, inherited, VisitKind.Enter, 0, true));
            while (stack.Count != 0)
            {
                _work.Consume(_token);
                Visit visit = stack[^1]; Node node = _nodes[visit.Index];
                if (visit.Kind == VisitKind.Exit || visit.Kind == VisitKind.Children && visit.Next >= node.End)
                { active.Replace(visit.Index, false); stack.RemoveLast(); continue; }
                if (visit.Kind == VisitKind.Children)
                {
                    int child = visit.Next;
                    stack[^1] = visit with { Next = node.Switch && !_nodes[child].Language ? node.End : _nodes[child].End };
                    if ((!node.Switch || !_nodes[child].Language) && (node.Kind != Kind.Mask || _nodes[child].Kind != Kind.Text))
                        stack.Add(new(child, visit.Context, VisitKind.Enter, 0, false));
                    continue;
                }
                if (node.Kind is Kind.Opaque or Kind.Marker || node.Kind == Kind.Definition && (!node.Symbol || !visit.Target)
                    || node.Kind == Kind.Mask && (!mask || visit.Index != root))
                { stack.RemoveLast(); continue; }
                if (active.TryGetValue(visit.Index, out bool visiting))
                {
                    if (visiting) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Cyclic local text source references are not implemented.");
                    active.Replace(visit.Index, true);
                }
                else active.TryAdd(visit.Index, true);
                if (node.Kind == Kind.Mask)
                {
                    stack[^1] = visit with { Kind = VisitKind.Children, Next = visit.Index + 1 };
                    continue;
                }
                SvgRetainedTextSource source = node.Source ?? throw new InvalidOperationException("A font source requires retained tokens.");
                using SvgRetainedTextSource.Reader reader = source.OpenReader(0, _work, _token); reader.MoveToContent();
                stack[^1] = visit with { Kind = VisitKind.Exit };
                if (node.Kind == Kind.Text) { AddText(reader, visit.Context, addFace, addPaint, addFilter, AddMask, coverage); continue; }
                InlineStyle inline = SvgInlineStyleParser.Parse(reader.GetAttribute("style"), _work, _token, true);
                SvgComputedStyle computed = SvgComputedStyle.Resolve(reader, inline, reader.StyleSheet);
                SvgInheritedContainer resolved = SvgContainerInheritance.Resolve(reader, inline, computed, reader.StyleSheet,
                    visit.Context.Presentation, visit.Context.FillRule, visit.Context.TextStyle, visit.Context.Stroke,
                    SvgRootParser.CssNamedColors, _state.PaintResources, _work, _token);
                if (!resolved.Presentation.IsDisplayed || resolved.Presentation.TextMetrics.CanSuppress) continue;
                if (node.Kind == Kind.Use && node.Target is {} emptyTarget && HasEmptyViewport(reader, emptyTarget))
                {
                    AddMask(reader, resolved, computed.Filter);
                    continue;
                }
                if (node.Kind == Kind.Geometry)
                {
                    AddMask(reader, resolved, computed.Filter);
                    if (resolved.Presentation.IsVisible && (reader.LocalName != "path" || !string.IsNullOrWhiteSpace(reader.GetAttribute("d"))))
                    {
                        if (reader.LocalName is "line" or "path" or "polygon" or "polyline") addMarkers(resolved.Presentation.Markers);
                    }
                    continue;
                }
                AddMask(reader, resolved, computed.Filter);
                if (node.Kind == Kind.Use)
                {
                    if (node.Target is {} target && _ids.TryGetValue(target, out int index)) stack.Add(new(index, resolved, VisitKind.Enter, 0, true));
                }
                else stack[^1] = visit with { Context = resolved, Kind = VisitKind.Children, Next = visit.Index + 1 };
            }
        }

    }

    /// <summary>Shares normalized text paint and font dependency extraction without opening programs.</summary>
    /// <remarks>Callbacks borrow the reader/contexts/IDs synchronously. Temporary normalized runs
    /// are owned here and released on every exit; work and cancellation use the caller's operation.</remarks>
    private void AddText(XmlReader reader, SvgInheritedContainer inherited, Action<SvgFontFaceRequest> addFace,
        Action<string> addPaint, Action<string> addFilter,
        Action<XmlReader, SvgInheritedContainer, string?> addMask, SvgFontCoverageDiscovery? coverage)
    {
        SvgPaintableElementContext context = new(inherited.Presentation, inherited.TextStyle, inherited.Stroke,
            reader is SvgRetainedTextSource.Reader source ? source.StyleSheet : default, SvgRootParser.CssNamedColors, _work, _token, 1, inherited.FillRule);
        // Font discovery borrows text paint but leaves effects to scene lowering.
        SvgComputedStyle authoredStyle = context.ResolveStyle(reader, true);
        context = context with { AuthoredStyle = authoredStyle with { Filter = null } };
        SvgResolvedDirectTextElement resolved = context.ResolveTextShape(reader);
        SvgTextStyle style = SvgTextSyntax.Parse(reader, resolved.InlineFont, inherited.TextStyle, resolved.LetterSpacing, resolved.WordSpacing);
        SvgTextWhitespaceMode whitespace = SvgTextSyntax.ParseWhitespaceMode(reader, resolved.WhiteSpace, inherited.TextStyle.WhitespaceMode);
        SvgTextPaintStyle paint = SvgTextPaintStyle.ResolveElement(resolved.PaintableElement.Style with { Filter = null }, inherited.Presentation.Paint,
            inherited.Presentation.IsDisplayed, inherited.Presentation.IsVisible, inherited.Stroke.Stroke);
        paint = paint with { StrokeStyle = inherited.Stroke.ResolveChild(resolved.PaintableElement.Style), PaintOrder = resolved.PaintableElement.Style.PaintOrder ?? inherited.Presentation.PaintOrder };
        paint = paint with { Decoration = SvgTextDecorationSyntax.Resolve(resolved.TextDecoration ?? reader.GetAttribute("text-decoration"), inherited.TextStyle.Decoration,
            (paint with { IsDisplayed = true, IsVisible = true }).GlyphPaint) };
        SvgInheritedTextStyle font = new(style.Font.Family, style.Font.Weight, style.Font.Italic, style.FontSize, whitespace, style.LetterSpacing, style.Anchor)
            { WordSpacing = style.WordSpacing, Decoration = paint.Decoration };
        SvgTextMetricScope metrics = inherited.Presentation.TextMetrics.Enter(authoredStyle.Filter, _state.PaintResources);
        paint = paint.ResolveResources(metrics.NeedsBounds ? null : _state.PaintResources, metrics.CanSuppress);
        using BudgetedParserList<SvgExtractedTextRun> runs = new(_state.Budget, SvgMemoryCategory.Parser, _work, _token);
        try
        {
            SvgRenderedLeafParser.ReadDirectTextContent(reader, _state, whitespace, style.LetterSpacing, paint, font,
                SvgTextLimits.MaximumCharacters, _limits.MaximumNodes, _limits.MaximumXmlDepth, _limits.MaximumAttributesPerElement,
                _state.Budget, _work, _token, runs, out _, out _);
            bool painted = false;
            foreach (SvgExtractedTextRun run in runs.AsSpan())
            {
                _work.Consume(_token);
                if (run.PaintSpans.Items.Length == 0) painted |= paint.GlyphPaint.IsPainted;
                foreach (SvgTextPaintSpan span in run.PaintSpans.Items) { _work.Consume(_token); painted |= span.Paint.IsPainted; ObservePaint(span.Paint); }
            }
            if (!painted) return;
            addMask(reader, inherited with { Presentation = inherited.Presentation with { IsDisplayed = true, IsVisible = paint.IsVisible } }, authoredStyle.Filter);
            if (authoredStyle.Filter is {} filter) addFilter(filter);
            ObservePaint(paint.GlyphPaint);
            SvgFontFaceRequest request = SvgFontFaceRequest.FromCss(style.Font.Family, style.Font.Weight, style.Font.Italic, style.SourceContext, _work, _token);
            foreach (SvgExtractedTextRun run in runs.AsSpan())
            {
                _work.Consume(_token);
                if (run.FontSpans.Items.Length == 0)
                {
                    addFace(request);
                    coverage?.ObserveNormalized(request, run.Layout.Value.AsSpan());
                }
                else foreach (SvgTextFontSpan span in run.FontSpans.Items)
                {
                    _work.Consume(_token);
                    SvgFontFaceRequest spanRequest = SvgFontFaceRequest.FromCss(span.Font.Family ?? "serif", span.Font.Weight, span.Font.Italic, style.SourceContext, _work, _token);
                    addFace(spanRequest);
                    coverage?.ObserveNormalized(spanRequest, run.Layout.Value.AsSpan(span.Start, span.End - span.Start));
                }
            }
        }
        finally { foreach (SvgExtractedTextRun run in runs.AsSpan()) run.Dispose(); }

        void ObservePaint(SvgTextGlyphPaint glyph)
        {
            if (glyph.FillServer is {} fill && SvgLocalUrl.TryParseId(fill, out string fillId)) addPaint(fillId);
            if (glyph.HasStroke && glyph.Stroke?.Style.Stroke is {} stroke && SvgLocalUrl.TryParseId(stroke, out string id)) addPaint(id);
        }
    }

    public void Dispose() { _maskTargets.Dispose(); _maskContexts.Dispose(); _pendingMasks.Dispose(); _pending.Dispose(); _ids.Dispose(); _nodes.Dispose(); _state.Dispose(); }
}
