using System.Xml;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Parsing;

/// <summary>Identifies pattern sources needed by nonzero resolved templates before content and font construction.</summary>
/// <remarks>One parsing/discovery operation owns this bounded metadata index. IDs borrow its string store;
/// scene parsing only borrows the index. Header collection and iterative graph resolution charge shared work
/// and observe cancellation. Disposal releases index storage; no geometry, font bytes or XML tree is retained.</remarks>
internal sealed class SvgPatternContentSelection : IDisposable
{
    private enum Resolution { New, Visiting, Ready, Invalid }
    private enum Selection { NeededContent, DisabledResource }
    private readonly record struct Pattern(string Id, string? Target, double? Width, double? Height, bool IsDisplayed, bool HasContent, Resolution State, int Source);
    private readonly record struct Scope(int Depth, bool Drawing, bool Definitions, bool Switch, bool Consumed, int Pattern);
    private readonly BudgetedParserDictionary<string, Selection> _selection;

    private SvgPatternContentSelection(RenderBudget budget)
        => _selection = new(budget, SvgMemoryCategory.Parser, 96);

    internal bool RequiresContent(string? id) => id is not null && _selection.TryGetValue(id, out Selection value) && value == Selection.NeededContent;

    internal bool IsDisplayed(string? id) => id is null || !_selection.TryGetValue(id, out Selection value) || value != Selection.DisabledResource;

    internal static SvgPatternContentSelection? Create(ReadOnlyMemory<byte> svg, XmlReaderSettings settings,
        SvgRenderLimits limits, BudgetedParserList<CssRule> rules, BudgetedParserStringStore strings, RenderBudget budget, ParserWorkCounter work, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        ReadOnlySpan<byte> bytes = svg.Span;
        if (!bytes.Contains((byte)0) && (bytes.IndexOf("pattern"u8) < 0 || bytes.IndexOf("href"u8) < 0)) return null;
        work.ConsumeText(bytes.Length, token);
        SvgPatternContentSelection result = new(budget);
        try
        {
            using BudgetedParserList<Pattern> patterns = new(budget, SvgMemoryCategory.Parser, work, token);
            using BudgetedParserDictionary<string, int> ids = new(budget, SvgMemoryCategory.Parser, 96);
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
                // An empty group creates a tile payload only when its active filter can produce output.
                if (parent.Pattern >= 0 && scopes.Count > 0 && svgElement && !SvgStructuralVocabulary.IsDescriptiveElement(reader.LocalName)
                    && (reader.LocalName != "g" || !reader.IsEmptyElement || HasFilter()))
                    patterns[parent.Pattern] = patterns[parent.Pattern] with { HasContent = true };
                int pattern = -1;
                if (selected && svgElement && reader.LocalName == "pattern" && (reader.Depth == rootDepth + 1 || parent.Definitions)
                    && reader.GetAttribute("id") is {} id)
                {
                    id = strings.Copy(id);
                    string? href = reader.GetAttribute("href") ?? reader.GetAttribute("href", "http://www.w3.org/1999/xlink");
                    string? target = href is { Length: > 1 } && href[0] == '#' ? strings.Copy(href.AsSpan(1)) : null;
                    pattern = patterns.Count;
                    SvgComputedStyle style = SvgComputedStyleResolver.Resolve(reader, SvgStyleSheet.Resolve(reader, rules, ancestors, work, token), work, token, true);
                    patterns.Add(new(id, target, Dimension("width"), Dimension("height"), SvgPresentationResolver.ParseDisplay(style.Display), false, Resolution.New, -1));
                    ids.TryAdd(id, pattern);
                }
                bool drawing = selected && !parent.Definitions && svgElement && (root || reader.LocalName is "svg" or "g" or "a" or "switch");
                bool definitions = selected && !parent.Definitions && svgElement && reader.LocalName == "defs";
                if (!reader.IsEmptyElement) scopes.Add(new(reader.Depth, drawing, definitions, drawing && reader.LocalName == "switch", false, pattern));
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
                    bool displayed = item.IsDisplayed && parent?.IsDisplayed != false;
                    item = item with { State = valid ? Resolution.Ready : Resolution.Invalid, IsDisplayed = displayed,
                        Width = item.Width ?? parent?.Width, Height = item.Height ?? parent?.Height,
                        Source = !displayed ? -1 : item.HasContent ? position : parent?.Source ?? -1 };
                    patterns[position] = item;
                    if (valid && !displayed) result._selection.TryAdd(item.Id, Selection.DisabledResource);
                    if (valid && item.Width > 0 && item.Height > 0 && item.Source >= 0) result._selection.TryAdd(patterns[item.Source].Id, Selection.NeededContent);
                }
            }
            return result;

            double? Dimension(string name) => reader.GetAttribute(name) is {} value ? SvgGeometrySyntax.ParseRectangleCoordinate(value) : null;
            bool HasFilter()
            {
                SvgComputedStyle style = SvgComputedStyleResolver.Resolve(reader, SvgStyleSheet.Resolve(reader, rules, ancestors, work, token), work, token, true);
                return style.Filter is {} filter && filter.Trim() != "none" && SvgPresentationResolver.ParseDisplay(style.Display);
            }
        }
        catch { result.Dispose(); throw; }
    }

    public void Dispose() => _selection.Dispose();
}
