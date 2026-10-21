using System.Xml;
using Lokad.Svg.Rendering;
using Lokad.Svg.Fonts;

namespace Lokad.Svg.Parsing;

/// <summary>Borrows the document's ordinary text preparation context during source expansion.</summary>
internal sealed record SvgTextReplayEnvironment(SvgRootAttributes Root, SvgRenderLimits Limits, SvgFontSnapshot? Fonts);

/// <summary>Retains a bounded text subtree, including authored attributes, text order and source-matched CSS.</summary>
/// <remarks>The document owns arrays and immutable strings until source expansion finishes. Capture and
/// readers charge shared work and observe cancellation. Readers have independent cursors and never own
/// fonts or glyph geometry; this source feeds the ordinary text parser rather than a second layout path.</remarks>
internal sealed class SvgRetainedTextSource
{
    private readonly record struct Attribute(string Name, string LocalName, string Prefix, string Namespace, string Value, int Line, int Position);
    private readonly record struct Node(XmlNodeType Type, string Name, string LocalName, string Prefix, string Namespace,
        string Value, int Depth, bool Empty, int AttributeStart, int AttributeCount, int Style, int End, int Line, int Position);
    private readonly Node[] _nodes;
    private readonly Attribute[] _attributes;
    private readonly InlineStyle[] _styles;

    private SvgRetainedTextSource(Node[] nodes, Attribute[] attributes, InlineStyle[] styles, int characters, int childNodes)
    { _nodes = nodes; _attributes = attributes; _styles = styles; CharacterCount = characters; ChildNodeCount = childNodes; }

    internal int CharacterCount { get; }
    internal int ChildNodeCount { get; }

    internal static SvgRetainedTextSource Capture(XmlReader element, SvgPaintableElementContext context,
        SvgDocumentState state, SvgRenderLimits limits)
        => Capture(element, context, state, limits, true);

    internal static SvgRetainedTextSource CaptureElement(XmlReader element, SvgPaintableElementContext context,
        SvgDocumentState state, SvgRenderLimits limits)
        => Capture(element, context, state, limits, false);

    private static SvgRetainedTextSource Capture(XmlReader element, SvgPaintableElementContext context,
        SvgDocumentState state, SvgRenderLimits limits, bool descendants)
    {
        ParserWorkCounter work = context.WorkCounter;
        CancellationToken token = context.CancellationToken;
        using BudgetedParserList<Node> nodes = new(state.Budget, SvgMemoryCategory.Parser, work, token);
        using BudgetedParserList<Attribute> attributes = new(state.Budget, SvgMemoryCategory.Parser, work, token);
        using BudgetedParserList<InlineStyle> styles = new(state.Budget, SvgMemoryCategory.Parser, work, token);
        using BudgetedParserList<int> open = new(state.Budget, SvgMemoryCategory.Parser, work, token);
        int originalDepth = element.Depth, characters = 0, childNodes = 0;
        using XmlReader? nested = descendants ? element.ReadSubtree() : null;
        XmlReader subtree = nested ?? element;
        bool next = !descendants || subtree.Read();
        while (next)
        {
            work.Consume(token);
            if (descendants) state.CssAncestors.Observe(subtree, originalDepth);
            int depth = descendants ? subtree.Depth : 0;
            int firstAttribute = attributes.Count, styleIndex = -1;
            string value = string.Empty;
            bool empty = !descendants || subtree.IsEmptyElement;
            if (subtree.NodeType == XmlNodeType.Element)
            {
                if (depth != 0 && ++childNodes > limits.MaximumNodes - state.NodeCount)
                    throw new SvgException(SvgErrorCode.LimitExceeded, "Retained text exceeds the document node limit.");
                SvgStructuralAdmission.ValidateElementStructure(subtree, limits);
                if (depth + originalDepth >= limits.MaximumXmlDepth)
                    throw new SvgException(SvgErrorCode.LimitExceeded, "Retained text exceeds the document depth limit.");
                InlineStyle sheet = depth == 0 ? context.StyleSheet
                    : SvgStyleSheet.Resolve(subtree, state.StyleRules, state.CssAncestors, work, token);
                if (sheet != default) { styleIndex = styles.Count; styles.Add(sheet); }
                int count = subtree.AttributeCount;
                for (int index = 0; index < count; index++)
                {
                    work.Consume(token);
                    subtree.MoveToAttribute(index);
                    (int line, int position) = Location(subtree);
                    attributes.Add(new(Copy(subtree.Name), Copy(subtree.LocalName), Copy(subtree.Prefix), Copy(subtree.NamespaceURI), Copy(subtree.Value), line, position));
                }
                subtree.MoveToElement();
            }
            else if (subtree.NodeType is XmlNodeType.Text or XmlNodeType.CDATA or XmlNodeType.Whitespace or XmlNodeType.SignificantWhitespace)
            {
                value = subtree.Value;
                if (value.Length > SvgTextLimits.MaximumCharacters - state.TextCharacterCount - characters)
                    throw new SvgException(SvgErrorCode.LimitExceeded, "Retained text exceeds the document character limit.");
                characters += value.Length;
                value = Copy(value);
            }
            int current = nodes.Count;
            (int nodeLine, int nodePosition) = Location(subtree);
            nodes.Add(new(subtree.NodeType, Copy(subtree.Name), Copy(subtree.LocalName), Copy(subtree.Prefix), Copy(subtree.NamespaceURI),
                value, depth, empty, firstAttribute, attributes.Count - firstAttribute, styleIndex, current, nodeLine, nodePosition));
            if (subtree.NodeType == XmlNodeType.Element && !empty) open.Add(current);
            else if (subtree.NodeType == XmlNodeType.EndElement)
            {
                if (open.Count == 0) throw new XmlException("Retained text has an unmatched closing element.");
                int start = open[^1]; open.RemoveLast();
                nodes[start] = nodes[start] with { End = current };
            }
            next = descendants && subtree.Read();
        }
        if (nodes.Count == 0 || open.Count != 0) throw new XmlException("Retained text is incomplete.");
        RenderBudget.BudgetReservation owner = state.Budget.Reserve(128, SvgMemoryCategory.Parser);
        try
        {
            state.DefinitionReservations.Add(owner);
            return new(Adopt(nodes.Detach()), Adopt(attributes.Detach()), Adopt(styles.Detach()), characters, childNodes);
        }
        catch { owner.Dispose(); throw; }

        static (int Line, int Position) Location(XmlReader reader)
            => reader is IXmlLineInfo info && info.HasLineInfo() ? (info.LineNumber, info.LinePosition) : (0, 0);

        string Copy(string text)
        {
            work.ConsumeText(text.Length, token);
            return state.Strings.Copy(text);
        }

        T[] Adopt<T>(OwnedParserArray<T> array) where T : struct
        {
            try
            {
                if (array.Reservation is {} reservation) state.DefinitionReservations.Add(reservation);
                return array.Items;
            }
            catch { array.Dispose(); throw; }
        }
    }

    /// <summary>Creates a parser-local cursor borrowing this document-owned source; callers dispose it on every exit.</summary>
    internal Reader OpenReader(int depth, ParserWorkCounter work, CancellationToken token)
        => new(this, 0, _nodes.Length - 1, depth, null, work, token);

    /// <summary>Replays a text leaf with instance inheritance and the ordinary glyph pipeline.</summary>
    /// <remarks>The parent scene adopts the staging owner; returned glyphs borrow its immutable paints.
    /// Preparation is bounded and cancellable. Deferred paths and clip/mask scopes resolve in the source
    /// scene, then transfer through the existing leaf bindings. No pixels or intermediate bitmap are emitted.</remarks>
    internal SvgGeometry[] Lower(SvgUseReference reference, SvgViewportFrame? frame, SvgDocumentState state, ParserWorkCounter work, CancellationToken token)
    {
        SvgTextReplayEnvironment environment = state.TextReplay ?? throw new InvalidOperationException("Text expansion requires its document context.");
        SvgSceneBuilder parent = state.Scene;
        SvgSceneBuilder? previous = state.DefinitionReplayScene;
        bool previousDirect = state.DirectTextReplay;
        int previousNodes = state.NodeCount, previousCharacters = state.TextCharacterCount, previousPaths = state.PendingTextRuns.Count;
        int previousFilterDepth = state.FilterGroupDepth;
        int previousClips = state.PendingGroupClips.Count, previousMasks = state.PendingGroupMasks.Count;
        using SvgSceneBuilder staging = new(state.Budget, work, token);
        SvgSceneStorage? storage = null;
        try
        {
            state.DefinitionReplayScene = staging;
            state.DirectTextReplay = previous is null || previousDirect;
            if (reference.Filter is {} ownFilter && ownFilter.Trim() != "none") state.FilterGroupDepth++;
            // Source syntax was counted at capture. Expansion charges work and
            // geometry storage without counting those lexical nodes a second time.
            state.NodeCount = 1; state.TextCharacterCount = 0;
            SvgAffineTransform placement = SvgReferenceResolver.PlainInstancePlacement(reference, frame);
            using Reader reader = OpenReader(0, work, token); reader.MoveToContent();
            SvgPaintableElementContext context = new(new(reference.FillOverride ?? reference.InheritedFill, reference.IsDisplayed, reference.IsVisible, placement)
                { OwnOpacity = reference.ElementParentOpacity, PaintOrder = reference.InheritedPaintOrder, Markers = reference.InheritedMarkers, PaintServerFill = reference.PaintServerFill },
                reference.InheritedTextStyle, reference.InheritedStroke, reader.StyleSheet, SvgRootParser.CssNamedColors, work, token, state.OutputMagnification, reference.InheritedFillRule)
            { StrokeHostMap = state.OutputMap.ComposeChecked(placement), LengthBases = reference.LengthBases, RecordingScope = reference.RecordingScope };
            SvgRenderedLeafParser.ParseResolved(reader, context, environment.Root, state, environment.Limits, state.Budget, environment.Fonts, work, token);
            if (state.DeferredTextFailure is {} failure) throw failure;
            SvgTextPathLayout.Resolve(state, new(previousPaths, previousClips, previousMasks), environment.Limits, work, token);
            SvgClipPathSyntax.ResolvePendingGroupClips(state, previousClips, work, token);
            SvgMaskSyntax.ResolvePendingGroupMasks(state, previousMasks, work, token);
            SvgLeafMaskComposition.Emit(state, staging, work, token);
            storage = staging.Build();
        }
        finally
        {
            state.DefinitionReplayScene = previous; state.DirectTextReplay = previousDirect;
            state.FilterGroupDepth = previousFilterDepth;
            state.NodeCount = previousNodes; state.TextCharacterCount = previousCharacters;
            while (state.PendingTextRuns.Count > previousPaths)
            {
                SvgPendingTextRun path = state.PendingTextRuns[^1];
                path.Run?.Dispose(); path.Continuation?.Dispose(); path.Owner.Dispose();
                state.PendingTextRuns.RemoveLast();
            }
            while (state.PendingGroupClips.Count > previousClips) state.PendingGroupClips.RemoveLast();
            while (state.PendingGroupMasks.Count > previousMasks) state.PendingGroupMasks.RemoveLast();
        }
        SvgVectorContent owner = SvgVectorContent.Create(storage ?? throw new InvalidOperationException("Text replay did not produce a scene."), state.Budget);
        try { parent.AdoptVectorContent(owner); }
        catch { owner.Dispose(); throw; }
        using BudgetedParserList<SvgGeometry> output = new(state.Budget, SvgMemoryCategory.Parser, work, token);
        using BudgetedParserList<int> masks = new(state.Budget, SvgMemoryCategory.Parser, work, token);
        foreach (SvgMaskContent source in owner.Scene.MaskContents)
        {
            work.Consume(token);
            // Bounds clips always borrow an earlier mask with no further bounds clip.
            int bounds = source.BoundsClipContentIndex;
            if (bounds >= masks.Count) throw new InvalidOperationException("A transferred bounds clip must precede its content.");
            masks.Add(parent.AddMaskContent(source with { BoundsClipContentIndex = bounds < 0 ? -1 : masks[bounds] }));
        }
        SvgLeafMaskOperation? viewportClip = null;
        if (frame is {} viewport && !viewport.HasAffineClipScope && viewport.Clip != SvgClipBox.Unbounded)
        {
            viewportClip = SvgLeafMaskComposition.NewOperation(state, null, SvgAffineTransform.Identity);
            viewportClip.Resolved = true; viewportClip.Value = 255; viewportClip.Region = viewport.Clip; viewportClip.RegionKind = SvgLayerRegionKind.Viewport;
        }
        using BudgetedParserList<SvgLeafMaskOperationReference> scopes = new(state.Budget, SvgMemoryCategory.Parser, work, token);
        SvgLeafMaskBinding? binding = null;
        bool bindingReady = false;
        foreach (SvgDrawCommand command in owner.Scene.Commands)
        {
            work.Consume(token);
            if (command.Kind == SvgDrawCommandKind.BeginGroup)
            {
                SvgLeafMaskOperation operation = SvgLeafMaskComposition.NewOperation(state, null, SvgAffineTransform.Identity);
                operation.Resolved = true; operation.PreservesIsolation = true;
                operation.Value = command.MaskAlpha; operation.SourceOpacity = command.SourceOpacity;
                operation.Region = command.MaskRegion; operation.RegionKind = command.RegionKind;
                operation.RecordingScope = command.RecordingScope;
                if (command.RegionKind == SvgLayerRegionKind.Filter)
                {
                    SvgFilterArena filters = owner.Scene.Filters ?? throw new InvalidOperationException("Transferred filter scopes require their source arena.");
                    operation.Filter = new(filters.BindingAt(command.MaskContentIndex));
                }
                else operation.ContentIndex = command.MaskContentIndex < 0 ? -1 : masks[command.MaskContentIndex];
                scopes.Add(new(operation)); bindingReady = false;
                continue;
            }
            if (command.Kind == SvgDrawCommandKind.EndGroup)
            {
                if (scopes.Count == 0) throw new InvalidOperationException("Transferred text scopes must be balanced.");
                scopes.RemoveLast(); bindingReady = false;
                continue;
            }
            if (!bindingReady)
            {
                binding = null;
                for (int index = scopes.Count - 1; index >= 0; index--)
                {
                    work.Consume(token);
                    binding = SvgLeafMaskComposition.NewBinding(state, scopes[index].Operation, binding);
                }
                if (viewportClip is not null) binding = SvgLeafMaskComposition.NewBinding(state, viewportClip, binding);
                bindingReady = true;
            }
            SvgGeometry source = owner.Scene.Geometries[command.GeometryIndex];
            output.Add(source with { RecordedPaint = Rebind(source.RecordedPaint), RecordedStrokePaint = Rebind(source.RecordedStrokePaint), LeafMask = binding });
        }
        if (scopes.Count != 0) throw new InvalidOperationException("Transferred text scopes must be balanced.");
        return SvgReferenceResolver.RetainInstanceBatch(output.AsSpan(), state, work, token);

        SvgRecordedPaint Rebind(SvgRecordedPaint paint) => paint.Identity <= 0 ? paint
            : parent.RegisterRecordedPaint(owner.Scene.RecordedPaintValues[paint.Identity - 1]);
    }

    /// <summary>Replays immutable tokens with ordinary XmlReader subtree and chunk semantics.</summary>
    /// <remarks>All abstract reader contracts borrow source storage. Cursor work/cancellation use the supplied
    /// conversion context; no source arrays or fonts transfer. Independent cursors may read immutable tokens,
    /// but each cursor and its shared parser work counter remain confined to their owning parse.</remarks>
    internal sealed class Reader : XmlReader, IXmlLineInfo
    {
        private readonly SvgRetainedTextSource _source;
        private readonly int _first, _last, _depth;
        private readonly Reader? _parent;
        private readonly ParserWorkCounter _work;
        private readonly CancellationToken _token;
        private readonly XmlNameTable _names = new NameTable();
        private int _index, _attribute = -1, _valueOffset;
        private ReadState _state;

        internal Reader(SvgRetainedTextSource source, int first, int last, int depth, Reader? parent, ParserWorkCounter work, CancellationToken token)
        {
            _source = source; _first = first; _last = last; _depth = depth; _parent = parent;
            _work = work; _token = token; _index = first; _state = ReadState.Initial;
        }

        private Node Current => _source._nodes[_index];
        private Attribute CurrentAttribute => _source._attributes[Current.AttributeStart + _attribute];
        internal InlineStyle StyleSheet => Current.Style < 0 ? default : _source._styles[Current.Style];
        public bool HasLineInfo() => LineNumber > 0;
        public int LineNumber => _attribute < 0 ? Current.Line : CurrentAttribute.Line;
        public int LinePosition => _attribute < 0 ? Current.Position : CurrentAttribute.Position;
        public override int AttributeCount => Current.AttributeCount;
        public override string BaseURI => string.Empty;
        public override int Depth => Current.Depth - _source._nodes[_first].Depth + _depth + (_attribute < 0 ? 0 : 1);
        public override bool EOF => _state == ReadState.EndOfFile;
        public override bool HasValue => _attribute >= 0 || NodeType is XmlNodeType.Text or XmlNodeType.CDATA or XmlNodeType.Whitespace or XmlNodeType.SignificantWhitespace;
        public override bool IsEmptyElement => _attribute < 0 && Current.Empty;
        public override string LocalName => _attribute < 0 ? Current.LocalName : CurrentAttribute.LocalName;
        public override string Name => _attribute < 0 ? Current.Name : CurrentAttribute.Name;
        public override string NamespaceURI => _attribute < 0 ? Current.Namespace : CurrentAttribute.Namespace;
        public override XmlNameTable NameTable => _names;
        public override XmlNodeType NodeType => _state != ReadState.Interactive ? XmlNodeType.None : _attribute < 0 ? Current.Type : XmlNodeType.Attribute;
        public override string Prefix => _attribute < 0 ? Current.Prefix : CurrentAttribute.Prefix;
        public override ReadState ReadState => _state;
        public override string Value => _attribute < 0 ? Current.Value : CurrentAttribute.Value;
        public override bool CanReadValueChunk => true;

        public override bool Read()
        {
            _work.Consume(_token);
            if (_state is ReadState.Closed or ReadState.EndOfFile) return false;
            _attribute = -1; _valueOffset = 0;
            if (_state == ReadState.Initial) { _state = ReadState.Interactive; return true; }
            if (_index == _last) { _state = ReadState.EndOfFile; return false; }
            _index++; return true;
        }

        public override XmlReader ReadSubtree()
        {
            _work.Consume(_token);
            if (NodeType != XmlNodeType.Element) throw new InvalidOperationException("A subtree requires an element cursor.");
            return new Reader(_source, _index, Current.End, 0, this, _work, _token);
        }

        public override void Close()
        {
            if (_state == ReadState.Closed) return;
            if (_parent is {} parent) { parent._index = _last; parent._attribute = -1; parent._valueOffset = 0; }
            _state = ReadState.Closed;
        }

        public override int ReadValueChunk(char[] buffer, int index, int count)
        {
            ArgumentNullException.ThrowIfNull(buffer);
            if (index < 0 || count < 0 || index > buffer.Length - count) throw new ArgumentOutOfRangeException(nameof(index));
            _work.Consume(_token);
            if (!HasValue) return 0;
            int length = Math.Min(count, Value.Length - _valueOffset);
            Value.AsSpan(_valueOffset, length).CopyTo(buffer.AsSpan(index, length));
            _valueOffset += length; return length;
        }

        public override string GetAttribute(int index)
        {
            _work.Consume(_token);
            if ((uint)index >= (uint)AttributeCount) throw new ArgumentOutOfRangeException(nameof(index));
            return _source._attributes[Current.AttributeStart + index].Value;
        }
        public override string? GetAttribute(string name)
        {
            for (int index = 0; index < AttributeCount; index++)
            {
                _work.Consume(_token);
                Attribute attribute = _source._attributes[Current.AttributeStart + index];
                if (attribute.Name == name) return attribute.Value;
            }
            return null;
        }
        public override string? GetAttribute(string name, string? namespaceURI)
        {
            for (int index = 0; index < AttributeCount; index++)
            {
                _work.Consume(_token);
                Attribute attribute = _source._attributes[Current.AttributeStart + index];
                if (attribute.LocalName == name && attribute.Namespace == (namespaceURI ?? string.Empty)) return attribute.Value;
            }
            return null;
        }
        public override void MoveToAttribute(int index)
        {
            if ((uint)index >= (uint)AttributeCount) throw new ArgumentOutOfRangeException(nameof(index));
            _attribute = index; _valueOffset = 0;
        }
        public override bool MoveToAttribute(string name)
        {
            for (int index = 0; index < AttributeCount; index++)
            {
                _work.Consume(_token);
                if (_source._attributes[Current.AttributeStart + index].Name == name) { MoveToAttribute(index); return true; }
            }
            return false;
        }
        public override bool MoveToAttribute(string name, string? namespaceURI)
        {
            for (int index = 0; index < AttributeCount; index++)
            {
                _work.Consume(_token);
                Attribute attribute = _source._attributes[Current.AttributeStart + index];
                if (attribute.LocalName == name && attribute.Namespace == (namespaceURI ?? string.Empty)) { MoveToAttribute(index); return true; }
            }
            return false;
        }
        public override bool MoveToElement() { bool moved = _attribute >= 0; _attribute = -1; _valueOffset = 0; return moved; }
        public override bool MoveToFirstAttribute() { if (AttributeCount == 0) return false; MoveToAttribute(0); return true; }
        public override bool MoveToNextAttribute() { if (_attribute + 1 >= AttributeCount) return false; MoveToAttribute(_attribute + 1); return true; }
        public override bool ReadAttributeValue() => false;
        public override string? LookupNamespace(string prefix) => prefix switch
        {
            "" => "http://www.w3.org/2000/svg", "xml" => "http://www.w3.org/XML/1998/namespace",
            "xmlns" => "http://www.w3.org/2000/xmlns/", _ => GetAttribute("xmlns:" + prefix)
        };
        public override void ResolveEntity() => throw new InvalidOperationException("A retained text source contains no entity stream.");
    }
}
