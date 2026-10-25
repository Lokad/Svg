using System.Xml;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Parsing;

/// <summary>Identifies unavailable paint/filter resources and required pattern sources before glyph construction.</summary>
/// <remarks>One parsing/discovery operation owns this bounded metadata index. IDs borrow its string store;
/// scene parsing only borrows the index. Header collection and iterative graph resolution charge shared work
/// and observe cancellation. Disposal releases index storage; no geometry, font bytes or XML tree is retained.</remarks>
internal sealed class SvgPaintResourceSelection : IDisposable
{
    private enum Resolution { New, Visiting, Ready, Invalid }
    private enum Selection { NeededContent, DisabledResource }
    private enum EffectKind { Mask, Filter }
    private readonly record struct EffectReference(string Id, EffectKind Kind);
    private readonly record struct PatternEffect(int Pattern, EffectReference Reference);
    private readonly record struct Pattern(string Id, string? Target, double? Width, double? Height, bool IsDisplayed, bool HasChildElements, Resolution State, int Source);
    private readonly record struct Scope(int Depth, bool Drawing, bool Definitions, bool Switch, bool Consumed, int Pattern, int PatternOwner, bool Displayed, bool Visible, bool Inert, SvgDisplay ElementDisplay);
    private readonly BudgetedParserDictionary<string, Selection> _selection;
    private readonly BudgetedParserList<EffectReference> _unusedEffects;
    private readonly BudgetedParserDictionary<string, bool> _displayedPaints;
    private readonly BudgetedParserDictionary<string, bool> _inactiveFilters;
    private readonly ParserWorkCounter _work;
    private readonly CancellationToken _token;
    private readonly RenderBudget.BudgetReservation _owner;

    /// <summary>Returns true only for a defined paint whose own resource display prevents painting.</summary>
    internal bool IsInactivePaint(string? value)
    {
        if (value is null || !SvgLocalUrl.TryParseId(value, out string id)) return false;
        _work.Consume(_token);
        return _displayedPaints.TryGetValue(id, out bool displayed) && !displayed || !IsPatternDisplayed(id);
    }
    internal bool IsInactiveFilter(string? value)
    {
        if (value is null || !SvgLocalUrl.TryParseId(value, out string id)) return false;
        _work.Consume(_token);
        return _inactiveFilters.TryGetValue(id, out _);
    }
    internal void ValidateTextPaint(string? value)
    {
        if (value is null || !SvgLocalUrl.TryParseId(value, out string id)) return;
        _work.Consume(_token);
        if (!_displayedPaints.TryGetValue(id, out _))
            throw new SvgException(SvgErrorCode.UnsupportedFeature, "Text paint requires a defined local paint server.");
    }

    private SvgPaintResourceSelection(RenderBudget budget, ParserWorkCounter work, CancellationToken token, RenderBudget.BudgetReservation owner)
    {
        _owner = owner; _work = work; _token = token;
        _displayedPaints = new(budget, SvgMemoryCategory.Parser, 96);
        _inactiveFilters = new(budget, SvgMemoryCategory.Parser, 96);
        _selection = new(budget, SvgMemoryCategory.Parser, 96);
        _unusedEffects = new(budget, SvgMemoryCategory.Parser, work, token);
    }

    internal bool RequiresPatternContent(string? id) => id is not null && _selection.TryGetValue(id, out Selection value) && value == Selection.NeededContent;

    /// <summary>Reports conservatively selected pattern content that may need native recording transforms.</summary>
    internal bool HasPatternContent { get; private set; }

    internal bool IsPatternDisplayed(string? id) => id is null || !_selection.TryGetValue(id, out Selection value) || value != Selection.DisabledResource;

    /// <summary>Checks omitted sources' displayed effect references without allocating bindings or constructing mask glyphs.</summary>
    /// <remarks>Called before scene sealing; IDs borrow preparation strings. Traversal charges shared work/token and owns no resources.</remarks>
    internal void ValidateUnusedEffects(SvgDocumentState state, ParserWorkCounter work, CancellationToken token)
    {
        foreach (EffectReference reference in _unusedEffects.AsSpan())
        {
            work.Consume(token);
            if (reference.Kind == EffectKind.Filter) _ = state.DocumentFilters.ResolveReference(reference.Id);
            else if (!state.Masks.TryGetValue(reference.Id, out _) && !state.MaskContents.TryGetValue(reference.Id, out _))
                throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only defined local mask references are implemented.");
        }
    }

    internal static SvgPaintResourceSelection? Create(ReadOnlyMemory<byte> svg, XmlReaderSettings settings,
        SvgRenderLimits limits, BudgetedParserList<CssRule> rules, BudgetedParserStringStore strings, RenderBudget budget, ParserWorkCounter work, CancellationToken token, Lokad.Svg.Fonts.SvgInstanceFontDiscovery? consumers)
    {
        token.ThrowIfCancellationRequested();
        ReadOnlySpan<byte> bytes = svg.Span;
        if (!bytes.Contains((byte)0) && bytes.IndexOf("pattern"u8) < 0 && bytes.IndexOf("Gradient"u8) < 0 && bytes.IndexOf("filter"u8) < 0) return null;
        work.ConsumeText(bytes.Length, token);
        RenderBudget.BudgetReservation owner = budget.Reserve(512, SvgMemoryCategory.Parser);
        SvgPaintResourceSelection result;
        try { result = new(budget, work, token, owner); }
        catch { owner.Dispose(); throw; }
        if (consumers is not null) consumers.Resources = result;
        try
        {
            using BudgetedParserList<Pattern> patterns = new(budget, SvgMemoryCategory.Parser, work, token);
            using BudgetedParserDictionary<string, int> ids = new(budget, SvgMemoryCategory.Parser, 96);
            using BudgetedParserDictionary<string, bool> references = new(budget, SvgMemoryCategory.Parser, 96);
            using BudgetedParserList<PatternEffect> effects = new(budget, SvgMemoryCategory.Parser, work, token);
            using BudgetedParserList<Scope> scopes = new(budget, SvgMemoryCategory.Parser, work, token);
            using BudgetedParserList<int> path = new(budget, SvgMemoryCategory.Parser, work, token);
            using SvgCssAncestorContext ancestors = new(rules, budget, work, token);
            using ReadOnlyMemoryStream stream = new(svg);
            using XmlReader reader = XmlReader.Create(stream, settings);
            int nodes = 0, rootDepth = -1;
            while (reader.Read())
            {
                work.Consume(token);
                ancestors.Observe(reader, 0);
                if (reader.NodeType == XmlNodeType.EndElement)
                {
                    if (scopes.Count > 0 && scopes[^1].Depth == reader.Depth) scopes.RemoveLast();
                    continue;
                }
                if (reader.NodeType != XmlNodeType.Element) continue;
                SvgStructuralAdmission.ValidateElementStructure(reader, limits);
                if (nodes++ >= limits.MaximumNodes) throw new SvgException(SvgErrorCode.LimitExceeded, "SVG node count exceeds the configured limit.");
                bool root = rootDepth < 0;
                if (root) { SvgStructuralAdmission.ThrowIfNotSvgRoot(reader); rootDepth = reader.Depth; }
                bool svgElement = reader.NamespaceURI == "http://www.w3.org/2000/svg";
                Scope parent = scopes.Count > 0 ? scopes[^1] : default;
                bool selected = root || parent.Drawing || parent.Definitions;
                if (parent.Switch)
                {
                    selected = !parent.Consumed && reader.GetAttribute("systemLanguage") is null;
                    if (selected) scopes[^1] = parent with { Consumed = true };
                }
                // Any authored element child overrides template content, regardless
                // of namespace, display, geometry or whether a scene is allocated.
                if (parent.Pattern >= 0 && scopes.Count > 0)
                    patterns[parent.Pattern] = patterns[parent.Pattern] with { HasChildElements = true };
                SvgComputedStyle paint = default;
                bool inert = parent.Inert || !svgElement || SvgStructuralVocabulary.IsDescriptiveElement(reader.LocalName);
                bool paintable = !inert && (root || SvgStructuralVocabulary.IsGroupOrLinkContainer(reader.LocalName)
                    || SvgStructuralVocabulary.IsDefinitionLeaf(reader.LocalName)
                    || reader.LocalName is "svg" or "defs" or "pattern" or "symbol" or "mask" or "marker" or "text" or "tspan" or "use" or "image" or "linearGradient" or "radialGradient" or "filter");
                if (paintable)
                {
                    InlineStyle sheet = SvgStyleSheet.Resolve(reader, rules, ancestors, work, token);
                    paint = reader.LocalName == "use"
                        ? SvgComputedStyle.Resolve(reader, SvgInlineStyleParser.ParseUse(reader.GetAttribute("style"), work, token), sheet)
                        : SvgComputedStyleResolver.Resolve(reader, sheet, work, token, true);
                }
                paint = paint.ResolveDisplay(parent.ElementDisplay);
                if (svgElement && reader.LocalName is "linearGradient" or "radialGradient" or "pattern" or "filter" && reader.GetAttribute("id") is {} resourceId)
                {
                    resourceId = strings.Copy(resourceId);
                    bool available = paint.GetDisplay() != SvgDisplay.None && (!parent.Definitions || parent.ElementDisplay != SvgDisplay.None);
                    if (reader.LocalName == "filter")
                    {
                        if (!available) result._inactiveFilters.TryAdd(resourceId, true);
                    }
                    else result._displayedPaints.TryAdd(resourceId, available);
                }
                ObservePaint(paint, false); ObservePaint(paint, true);
                int pattern = -1;
                if (selected && svgElement && reader.LocalName == "pattern" && (reader.Depth == rootDepth + 1 || parent.Definitions)
                    && reader.GetAttribute("id") is {} id)
                {
                    id = strings.Copy(id);
                    string? href = reader.GetAttribute("href") ?? reader.GetAttribute("href", "http://www.w3.org/1999/xlink");
                    string? target = href is { Length: > 1 } && href[0] == '#' ? SvgLocalUrl.CopyFragment(href.AsSpan(1), strings) : null;
                    pattern = patterns.Count;
                    patterns.Add(new(id, target, Dimension("width"), Dimension("height"), (paint.GetDisplay() != SvgDisplay.None) && (!parent.Definitions || parent.ElementDisplay != SvgDisplay.None), false, Resolution.New, -1));
                    ids.TryAdd(id, pattern);
                }
                bool drawing = selected && !parent.Definitions && svgElement && (root || reader.LocalName is "svg" or "g" or "a" or "switch");
                bool definitions = selected && !parent.Definitions && svgElement && reader.LocalName == "defs";
                int patternOwner = pattern >= 0 ? pattern : root ? -1 : parent.PatternOwner;
                bool displayed = (root || pattern >= 0 || parent.Displayed) && (paint.GetDisplay() != SvgDisplay.None);
                bool visible = SvgPresentationResolver.ParseVisibility(paint.Visibility, root || parent.Visible);
                if (patternOwner >= 0 && displayed && paintable && (SvgStructuralVocabulary.IsGroupOrLinkContainer(reader.LocalName)
                    || SvgStructuralVocabulary.IsDefinitionLeaf(reader.LocalName) || reader.LocalName is "text" or "use" or "image"))
                {
                    if (SvgMaskSyntax.HasPaintableTarget(reader, visible, paint.Filter))
                        RetainEffect(SvgLocalUrl.ReadReference(reader, "mask"), EffectKind.Mask, patternOwner, effects, strings);
                    if (SvgMaskSyntax.HasPaintableTarget(reader, true, null)) RetainEffect(SvgLocalUrl.GetReference(paint.Filter), EffectKind.Filter, patternOwner, effects, strings);
                }
                if (!reader.IsEmptyElement) scopes.Add(new(reader.Depth, drawing, definitions, drawing && reader.LocalName == "switch", false, pattern, patternOwner, displayed, visible, inert, paint.GetDisplay()));
            }
            for (int index = 0; index < patterns.Count; index++)
            {
                work.Consume(token);
                int current = index;
                while (patterns[current].State == Resolution.New)
                {
                    work.Consume(token);
                    Pattern item = patterns[current];
                    patterns[current] = item with { State = Resolution.Visiting };
                    path.Add(current);
                    if (item.Target is null || !ids.TryGetValue(item.Target, out current)) break;
                }
                while (path.Count > 0)
                {
                    work.Consume(token);
                    int position = path[^1]; path.RemoveLast();
                    Pattern item = patterns[position];
                    Pattern? parent = item.Target is {} target && ids.TryGetValue(target, out int parentIndex) ? patterns[parentIndex] : null;
                    bool valid = item.Target is null || parent is { State: Resolution.Ready };
                    bool displayed = item.IsDisplayed;
                    if (parent?.IsDisplayed == false) parent = null;
                    item = item with { State = valid ? Resolution.Ready : Resolution.Invalid, IsDisplayed = displayed,
                        Width = item.Width ?? parent?.Width, Height = item.Height ?? parent?.Height,
                        Source = !displayed ? -1 : item.HasChildElements ? position : parent?.Source ?? -1 };
                    patterns[position] = item;
                    if (valid && !displayed) result._selection.TryAdd(item.Id, Selection.DisabledResource);
                }
            }
            using BudgetedParserDictionary<string, bool> actualReferences = new(budget, SvgMemoryCategory.Parser, 96);
            if (consumers is not null && patterns.Count != 0)
                consumers.SelectPaints(result, id => actualReferences.TryAdd(strings.Copy(id), true), id =>
                    ids.TryGetValue(id, out int index) && patterns[index] is { State: Resolution.Ready, Width: > 0, Height: > 0, Source: >= 0 } item
                        ? patterns[item.Source].Id : null);
            // Font-free inputs retain the conservative header-only path. Font-bearing
            // sources use actual consumers; a template href alone never selects
            // an overridden parent's glyphs or geometry.
            foreach (Pattern item in patterns.AsSpan())
            {
                work.Consume(token);
                if (item.State == Resolution.Ready && item.Width > 0 && item.Height > 0 && item.Source >= 0 && (consumers is not null ? actualReferences : references).TryGetValue(item.Id, out _))
                {
                    result._selection.TryAdd(patterns[item.Source].Id, Selection.NeededContent);
                    result.HasPatternContent = true;
                }
            }
            foreach (PatternEffect effect in effects.AsSpan())
            {
                work.Consume(token);
                if (patterns[effect.Pattern].IsDisplayed && !result.RequiresPatternContent(patterns[effect.Pattern].Id)) result._unusedEffects.Add(effect.Reference);
            }
            return result;

            double? Dimension(string name) => reader.GetAttribute(name) is {} value ? SvgGeometrySyntax.ParseRectangleCoordinate(value) : null;
            static void RetainEffect(string? value, EffectKind kind, int patternOwner, BudgetedParserList<PatternEffect> effects, BudgetedParserStringStore strings)
            {
                if (value is not null && SvgLocalUrl.TryParseId(value, out string id)) effects.Add(new(patternOwner, new(strings.Copy(id), kind)));
            }
            void ObservePaint(SvgComputedStyle paint, bool stroke)
            {
                string? value = stroke ? paint.Stroke : paint.Fill;
                // Declared empty geometry cannot sample its own paint, including through an instance.
                if (value is not null && SvgLocalUrl.TryParseId(value, out string id) && !references.TryGetValue(id, out _)
                    && (!SvgStructuralVocabulary.IsDefinitionLeaf(reader.LocalName) || SvgMaskSyntax.HasPaintableTarget(reader, true, null)))
                {
                    // A primitive's own channel declarations survive instance inheritance.
                    // Container paint stays conservative because children can override opacity or width.
                    if (SvgStructuralVocabulary.IsDefinitionLeaf(reader.LocalName))
                    {
                        if (paint.GetSourceDisplay() == SvgDisplay.None
                            || !paint.InheritsOpacity && paint.GetOpacity().Value == 0
                            || !SvgPresentationResolver.ParseVisibility(paint.Visibility, true)
                            || SvgColorParser.ParsePaintOpacity(stroke ? paint.StrokeOpacity : paint.FillOpacity).Value == 0
                            || stroke && SvgStrokeStyleSyntax.IsZeroStrokeWidth(paint.Width)
                            || (SvgGeometrySyntax.GetPaintChannels(reader, limits.MaximumPathCommands, budget, work, token) & (stroke ? SvgGeometryPaintChannels.Stroke : SvgGeometryPaintChannels.Fill)) == 0) return;
                    }
                    references.TryAdd(strings.Copy(id), true);
                }
            }

        }
        catch { result.Dispose(); throw; }
    }

    public void Dispose()
    {
        _inactiveFilters.Dispose(); _displayedPaints.Dispose(); _unusedEffects.Dispose();
        _selection.Dispose(); _owner.Dispose();
    }
}
