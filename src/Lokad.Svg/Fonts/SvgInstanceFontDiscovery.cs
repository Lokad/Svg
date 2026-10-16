using System.Xml;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Fonts;

/// <summary>Replays referenced font-bearing source trees through shared container inheritance and text extraction.</summary>
/// <remarks>Only referenced trees contribute requests; definition ancestors and unused programs are excluded.
/// This parser-local owner retains bounded token/graph storage, charges every scan/visit and observes cancellation.
/// It never opens a font program or constructs glyph geometry. Callers dispose it before acquiring programs.</remarks>
internal sealed class SvgInstanceFontDiscovery : IDisposable
{
    private enum Kind { Container, Definition, Opaque, Text, Use }
    private enum VisitKind { Enter, Children, Exit }
    private readonly record struct Node(Kind Kind, SvgRetainedTextSource? Source, string? Target, int End, bool Symbol, bool Switch, bool Language);
    private readonly record struct Open(int Depth, int Index, bool CanDefine);
    private readonly record struct Pending(string Target, SvgInheritedContainer Context);
    private readonly record struct Visit(int Index, SvgInheritedContainer Context, VisitKind Kind, int Next, bool Target);
    private readonly SvgDocumentState _state;
    private readonly SvgRenderLimits _limits;
    private readonly ParserWorkCounter _work;
    private readonly CancellationToken _token;
    private readonly BudgetedParserList<Node> _nodes;
    private readonly BudgetedParserDictionary<string, int> _ids;
    private readonly BudgetedParserList<Pending> _pending;

    private SvgInstanceFontDiscovery(SvgRenderLimits limits, BudgetedParserList<CssRule> rules,
        BudgetedParserStringStore strings, RenderBudget budget, ParserWorkCounter work, CancellationToken token)
    {
        _limits = limits; _work = work; _token = token;
        _state = new(budget, work, token, rules, strings, false);
        _nodes = new(budget, SvgMemoryCategory.Parser, work, token);
        _ids = new(budget, SvgMemoryCategory.Parser, 96);
        _pending = new(budget, SvgMemoryCategory.Parser, work, token);
    }

    internal static SvgInstanceFontDiscovery? Collect(ReadOnlyMemory<byte> svg, XmlReaderSettings settings, SvgRenderLimits limits,
        BudgetedParserList<CssRule> rules, BudgetedParserStringStore strings, RenderBudget budget, ParserWorkCounter work, CancellationToken token)
    {
        ReadOnlySpan<byte> bytes = svg.Span;
        if (!bytes.Contains((byte)0) && (bytes.IndexOf("text"u8) < 0 || bytes.IndexOf("use"u8) < 0)) return null;
        SvgInstanceFontDiscovery result = new(limits, rules, strings, budget, work, token);
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
                    "defs" or "symbol" => Kind.Definition,
                    "text" => Kind.Text, "use" => Kind.Use, _ => Kind.Opaque
                };
                bool switchChild = open.Count != 0 && owner._nodes[open[^1].Index].Switch && reader.Depth == open[^1].Depth + 1;
                if (kind == Kind.Opaque && reader.IsEmptyElement && !switchChild) continue;
                int index = owner._nodes.Count;
                bool symbol = svgElement && reader.LocalName == "symbol", isSwitch = svgElement && reader.LocalName == "switch";
                bool language = reader.GetAttribute("systemLanguage") is not null;
                bool canDefine = kind != Kind.Opaque && (open.Count == 0 || open[^1].CanDefine);
                string? id = canDefine && (kind != Kind.Definition || symbol) ? reader.GetAttribute("id") : null;
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

    internal void AddUse(XmlReader reader, SvgInheritedContainer context)
    {
        if (context.Presentation.IsDisplayed && Target(reader) is {} target) _pending.Add(new(target, context));
    }

    private string? Target(XmlReader reader)
    {
        _work.Consume(_token);
        string? href = reader.GetAttribute("href") ?? reader.GetAttribute("href", "http://www.w3.org/1999/xlink");
        if (href is null || href.Length < 2 || href[0] != '#') return null;
        if (href.Length - 1 > SvgSyntaxLimits.MaximumIdentifierCharacters) throw new SvgException(SvgErrorCode.LimitExceeded, "Local font-source identifiers exceed the syntax limit.");
        return _state.Strings.Copy(href.AsSpan(1));
    }

    internal void AddRequests(Action<SvgFontFaceRequest> addFace, Action<string> addStroke)
    {
        using BudgetedParserList<Visit> stack = new(_state.Budget, SvgMemoryCategory.Parser, _work, _token);
        using BudgetedParserDictionary<int, bool> active = new(_state.Budget, SvgMemoryCategory.Parser, 64);
        foreach (Pending pending in _pending.AsSpan())
        {
            _work.Consume(_token);
            if (!_ids.TryGetValue(pending.Target, out int root)) continue;
            stack.Add(new(root, pending.Context, VisitKind.Enter, 0, true));
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
                if (node.Kind == Kind.Opaque || node.Kind == Kind.Definition && (!node.Symbol || !visit.Target))
                { stack.RemoveLast(); continue; }
                if (active.TryGetValue(visit.Index, out bool visiting))
                {
                    if (visiting) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Cyclic local text source references are not implemented.");
                    active.Replace(visit.Index, true);
                }
                else active.TryAdd(visit.Index, true);
                SvgRetainedTextSource source = node.Source ?? throw new InvalidOperationException("A font source requires retained tokens.");
                using SvgRetainedTextSource.Reader reader = source.OpenReader(0, _work, _token); reader.MoveToContent();
                stack[^1] = visit with { Kind = VisitKind.Exit };
                if (node.Kind == Kind.Text) { AddText(reader, visit.Context, addFace, addStroke); continue; }
                InlineStyle inline = SvgInlineStyleParser.Parse(reader.GetAttribute("style"), _work, _token, true);
                SvgComputedStyle computed = SvgComputedStyle.Resolve(reader, inline, reader.StyleSheet);
                SvgInheritedContainer resolved = SvgContainerInheritance.Resolve(reader, inline, computed, reader.StyleSheet,
                    visit.Context.Presentation, visit.Context.FillRule, visit.Context.TextStyle, visit.Context.Stroke,
                    SvgRootParser.CssNamedColors, _work, _token);
                if (!resolved.Presentation.IsDisplayed) continue;
                if (node.Kind == Kind.Use)
                {
                    if (node.Target is {} target && _ids.TryGetValue(target, out int index)) stack.Add(new(index, resolved, VisitKind.Enter, 0, true));
                }
                else stack[^1] = visit with { Context = resolved, Kind = VisitKind.Children, Next = visit.Index + 1 };
            }
        }

        void AddText(XmlReader reader, SvgInheritedContainer inherited, Action<SvgFontFaceRequest> addFace, Action<string> addStroke)
        {
            SvgPaintableElementContext context = new(inherited.Presentation, inherited.TextStyle, inherited.Stroke,
                reader is SvgRetainedTextSource.Reader source ? source.StyleSheet : default, SvgRootParser.CssNamedColors, _work, _token, 1, inherited.FillRule);
            SvgResolvedDirectTextElement resolved = context.ResolveTextShape(reader);
            SvgTextStyle style = SvgTextSyntax.Parse(reader, resolved.InlineFont, inherited.TextStyle, resolved.LetterSpacing, resolved.WordSpacing);
            SvgTextWhitespaceMode whitespace = SvgTextSyntax.ParseWhitespaceMode(reader, resolved.WhiteSpace, inherited.TextStyle.WhitespaceMode);
            SvgTextPaintStyle paint = SvgTextPaintStyle.ResolveElement(resolved.PaintableElement.Style, inherited.Presentation.Paint,
                inherited.Presentation.IsDisplayed, inherited.Presentation.IsVisible, inherited.Stroke.Stroke);
            paint = paint with { StrokeStyle = inherited.Stroke.ResolveChild(resolved.PaintableElement.Style), PaintOrder = resolved.PaintableElement.Style.PaintOrder ?? inherited.Presentation.PaintOrder };
            paint = paint with { Decoration = SvgTextDecorationSyntax.Resolve(resolved.TextDecoration ?? reader.GetAttribute("text-decoration"), inherited.TextStyle.Decoration,
                (paint with { IsDisplayed = true, IsVisible = true }).GlyphPaint) };
            SvgInheritedTextStyle font = new(style.Font.Family, style.Font.Weight, style.Font.Italic, style.FontSize, whitespace, style.LetterSpacing, style.Anchor)
                { WordSpacing = style.WordSpacing, Decoration = paint.Decoration };
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
                    foreach (SvgTextPaintSpan span in run.PaintSpans.Items) { _work.Consume(_token); painted |= span.Paint.IsPainted; ObserveStroke(span.Paint); }
                }
                if (!painted) return;
                ObserveStroke(paint.GlyphPaint);
                foreach (SvgExtractedTextRun run in runs.AsSpan())
                {
                    _work.Consume(_token);
                    if (run.FontSpans.Items.Length == 0) addFace(style.Font);
                    else foreach (SvgTextFontSpan span in run.FontSpans.Items)
                    { _work.Consume(_token); addFace(new(span.Font.Family ?? "serif", span.Font.Weight, span.Font.Italic)); }
                }
            }
            finally { foreach (SvgExtractedTextRun run in runs.AsSpan()) run.Dispose(); }

            void ObserveStroke(SvgTextGlyphPaint glyph)
            {
                if (glyph.HasStroke && glyph.Stroke?.Style.Stroke is {} stroke && SvgLocalUrl.TryParseId(stroke, out string id)) addStroke(id);
            }
        }
    }

    public void Dispose() { _pending.Dispose(); _ids.Dispose(); _nodes.Dispose(); _state.Dispose(); }
}
