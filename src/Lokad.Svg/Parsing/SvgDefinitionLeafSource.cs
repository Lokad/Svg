using System.Xml;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Parsing;

/// <summary>Separates native fill-source retention from instance stroke construction.</summary>
[Flags]
internal enum SvgSourceContourRetention : byte { None = 0, Fill = 1, Stroke = 2 }

/// <summary>Retains one validated shape's immutable attributes and authored style for instance fill and stroke construction.</summary>
/// <remarks>The document string store retains immutable values; the document reservation list owns the reader and
/// attribute array. This is an attribute adapter for existing leaf lowerers, with no child tree or serialized XML.
/// Replay is confined to the parser thread. Attribute scans observe cancellation.</remarks>
internal sealed class SvgDefinitionLeafSource : XmlReader
{
    private readonly record struct Attribute(string Name, string LocalName, string Prefix, string Namespace, string Value);
    private readonly Attribute[] _attributes;
    private readonly string _element;
    private readonly SvgComputedStyle _authored;
    private readonly SvgInheritedStrokeStyle _constructedStroke;
    private readonly SvgPaintOrder _constructedPaintOrder;
    private readonly string? _constructedFillRule;
    private readonly bool _unconstructed;
    private readonly SvgSolidPaint _constructedCurrentColor;
    private readonly string? _constructedPaintServerFill;
    private readonly IReadOnlyDictionary<string, SvgColor> _colors;
    private readonly XmlNameTable _names = new NameTable();
    private readonly CancellationToken _token;
    private int _attribute = -1;

    internal SvgRenderLimits? ImageLimits { get; private set; }

    private SvgDefinitionLeafSource(string element, Attribute[] attributes, SvgComputedStyle authored,
        SvgInheritedStrokeStyle constructedStroke, IReadOnlyDictionary<string, SvgColor> colors,
CancellationToken token, string? constructedFillRule, bool unconstructed, SvgSolidPaint constructedCurrentColor, string? constructedPaintServerFill, SvgPaintOrder constructedPaintOrder)
    {
        _element = element; _attributes = attributes; _authored = authored;
        _constructedStroke = constructedStroke; _constructedPaintOrder = constructedPaintOrder; _colors = colors; _token = token;
        _constructedFillRule = constructedFillRule; _unconstructed = unconstructed; _constructedCurrentColor = constructedCurrentColor; _constructedPaintServerFill = constructedPaintServerFill;
    }

    internal static SvgDefinitionLeafSource Capture(XmlReader reader, SvgPaintableElementContext context, SvgDocumentState state)
        => Capture(reader, context, state, false);

    internal static SvgDefinitionLeafSource CaptureUnconstructed(XmlReader reader, SvgPaintableElementContext context, SvgDocumentState state)
        => Capture(reader, context, state, true);

    internal static SvgDefinitionLeafSource CaptureImage(XmlReader reader, SvgPaintableElementContext context, SvgDocumentState state, SvgRenderLimits limits)
    {
        SvgDefinitionLeafSource source = Capture(reader, context, state, true);
        source.ImageLimits = limits;
        return source;
    }

    private static SvgDefinitionLeafSource Capture(XmlReader reader, SvgPaintableElementContext context, SvgDocumentState state, bool unconstructed)
    {
        SvgComputedStyle raw = context.AuthoredStyle ?? SvgComputedStyleResolver.Resolve(reader, context.StyleSheet, context.CancellationToken);
        SvgComputedStyle authored = new(Copy(raw.Color), Copy(raw.Fill), Copy(raw.FillOpacity), Copy(raw.Stroke),
            Copy(raw.Display), Copy(raw.Visibility), Copy(raw.Opacity), Copy(raw.FillRule))
        {
            Width = Copy(raw.Width), Linecap = Copy(raw.Linecap), Linejoin = Copy(raw.Linejoin),
            Miterlimit = Copy(raw.Miterlimit), Dasharray = Copy(raw.Dasharray), Dashoffset = Copy(raw.Dashoffset),
            StrokeOpacity = Copy(raw.StrokeOpacity), PathLength = Copy(raw.PathLength), ClipRule = Copy(raw.ClipRule), PaintOrder = raw.PaintOrder,
            VectorEffect = Copy(raw.VectorEffect), MarkerStart = Copy(raw.MarkerStart), MarkerMid = Copy(raw.MarkerMid), MarkerEnd = Copy(raw.MarkerEnd), Filter = Copy(raw.Filter)
        };
        RenderBudget.BudgetReservation reservation = state.Budget.Reserve(512 + ParserCollectionMemory.GetArrayByteCount<Attribute>(reader.AttributeCount), SvgMemoryCategory.Scene);
        try
        {
            Attribute[] attributes = new Attribute[reader.AttributeCount];
            string element = state.Strings.Copy(reader.LocalName);
            for (int index = 0; index < attributes.Length; index++)
            {
                context.CancellationToken.ThrowIfCancellationRequested();
                reader.MoveToAttribute(index);
                string name = Copy(reader.Name) ?? throw new InvalidOperationException();
                string localName = reader.LocalName;
                attributes[index] = new(name, localName == name ? name : Copy(localName) ?? throw new InvalidOperationException(),
                    Copy(reader.Prefix) ?? throw new InvalidOperationException(), Copy(reader.NamespaceURI) ?? throw new InvalidOperationException(), Copy(reader.Value) ?? throw new InvalidOperationException());
            }
            reader.MoveToElement();
            SvgInheritedStrokeStyle constructed = context.InheritedStroke.ResolveChild(authored);
            SvgDefinitionLeafSource source = new(element, attributes, authored, constructed, context.NamedColors, context.CancellationToken, Copy(context.InheritedFillRule), unconstructed, context.InheritedPresentation.Paint.CurrentPaint, context.InheritedPresentation.PaintServerFill, context.InheritedPresentation.PaintOrder);
            state.DefinitionReservations.Add(reservation);
            return source;
        }
        catch { reader.MoveToElement(); reservation.Dispose(); throw; }

        string? Copy(string? value)
        {
            if (value is null) return null;
            context.CancellationToken.ThrowIfCancellationRequested();
            return state.Strings.Copy(value);
        }
    }

    internal SvgComputedStyle AuthoredStyle => _authored;

    internal bool RequiresConstruction(SvgInheritedStrokeStyle inherited, SvgPaint paint, string? fillRule, string? paintServerFill, SvgPaintOrder paintOrder, SvgMarkerStyle markers)
    {
        _token.ThrowIfCancellationRequested();
        SvgInheritedStrokeStyle resolved = inherited.ResolveChild(_authored);
        if (_authored.InheritsOpacity || _authored.InheritsDisplay) return true;
        if (_authored.Filter is string filter && filter.Trim() != "none") return true;
        if (SvgLocalUrl.GetReference(GetAttribute("clip-path")) is not null) return true;
        if (SvgLocalUrl.GetReference(GetAttribute("mask")) is not null) return true;
        // Numeric opacity binds through recorded paint. Shader fills and native
        // stroke colors require source lowering when their inherited values change.
        bool inheritedShader = _element != "line" && _authored.Fill is null && paintServerFill is not null;
        bool strokeColorChanged = _authored.Color is null && resolved.Stroke is not null
            && resolved.Stroke.Equals("currentColor", StringComparison.OrdinalIgnoreCase) && paint.CurrentPaint != _constructedCurrentColor;
        return SvgMarkerStyle.Resolve(_authored, markers).HasMarkers || (_authored.PaintOrder ?? paintOrder) != (_authored.PaintOrder ?? _constructedPaintOrder) || _unconstructed || inheritedShader || (_element != "line" && _authored.Fill is null && _constructedPaintServerFill is not null && paintServerFill is null) || strokeColorChanged || (_authored.FillRule ?? fillRule) != (_authored.FillRule ?? _constructedFillRule)
            || resolved with { StrokeOpacity = null } != _constructedStroke with { StrokeOpacity = null };
    }

    internal SvgDefinition Lower(SvgInheritedStrokeStyle inherited, SvgPaint paint, string? fillRule, string? paintServerFill, SvgPaintOrder paintOrder, SvgMarkerStyle markers, SvgPaintOpacity parentOpacity, SvgDisplay parentDisplay, SvgTextMetricScope textMetrics, bool parentNonScalingStroke, SvgAffineTransform instancePlacement, SvgViewportLengthBases lengthBases, SvgSourceContourRetention retention, bool isDisplayed, SvgDocumentState state)
    {
        _attribute = -1;
        // A use supplies fill only where the source did not author one. The original
        // budget-owned URL is borrowed during replay; opacity and color remain independent.
        SvgComputedStyle authored = _authored;
        if (_element != "line" && authored.Fill is null && paintServerFill is not null) authored = authored with { Fill = paintServerFill };
        SvgInheritedStrokeStyle resolved = inherited.ResolveChild(_authored);
        double instanceMagnification = Math.Max(Math.Sqrt(instancePlacement.A * instancePlacement.A + instancePlacement.B * instancePlacement.B),
            Math.Sqrt(instancePlacement.C * instancePlacement.C + instancePlacement.D * instancePlacement.D));
        if (instanceMagnification == 0) instanceMagnification = 1;
        SvgPaintableElementContext context = new(new(paint, true, true, SvgAffineTransform.Identity) { NonScalingStroke = parentNonScalingStroke, TextMetrics = textMetrics, OwnOpacity = parentOpacity, ElementDisplay = parentDisplay, PaintOrder = paintOrder, Markers = markers }, SvgInheritedTextStyle.Default,
            resolved, default, _colors, _token, state.OutputMagnification * instanceMagnification, fillRule)
        {
            // Inverse stroke-unit intermediates remain finite until complete draw placement.
            StrokeHostMap = state.OutputMap.Compose(instancePlacement),
            RecordingScope = new(1),
            AuthoredStyle = authored,
            LengthBases = lengthBases,
            RetainInstanceFillPath = (retention & SvgSourceContourRetention.Fill) != 0,
            RetainInstanceStrokePath = (retention & SvgSourceContourRetention.Stroke) != 0 || _element == "rect" && SvgGradientSyntax.IsGradientStrokeUrl(resolved.Stroke)
                && !SvgStrokeStyleSyntax.IsZeroStrokeWidth(resolved.Width)
        };
        // Raw construction stays independent of display; only live instances activate source effects.
        return SvgDefinitionParser.LowerInstanceSource(this, context, state, isDisplayed);
    }

    /// <summary>Lowers a referenced clip silhouette without constructing its ignored paint or stroke.</summary>
    /// <remarks>The result owns its geometry and borrows document strings. Visual bounds retain cascaded
    /// stroke presentation separately from opaque fill coverage; the caller applies visibility after geometry admission. Existing leaf lowerers share the document
    /// budget, cancellation and source operands; failure releases their partial owners. Replay is parser-thread confined.</remarks>
    internal (SvgDefinition Definition, float BoundsInflation) LowerClip(SvgUseReference reference, SvgDocumentState state)
    {
        _attribute = -1;
        SvgComputedStyle visualStyle = reference.InheritedStroke.ApplyTo(_authored);
        float inflation = SvgStrokeStyleSyntax.BoundsInflation(visualStyle, _element);
        SvgAffineTransform placement = SvgReferenceResolver.PlainInstancePlacement(reference, null);
        SvgPaintableElementContext context = new(new(SvgPaint.Default, true, true, SvgAffineTransform.Identity),
            SvgInheritedTextStyle.Default, SvgInheritedStrokeStyle.Default, default, _colors, _token, state.OutputMagnification, reference.InheritedFillRule)
        {
            StrokeHostMap = state.OutputMap.Compose(placement),
            RecordingScope = new(1), LengthBases = reference.LengthBases, RetainInstanceFillPath = true,
            AuthoredStyle = _authored with { Fill = "white", FillOpacity = "1", Stroke = "none", StrokeOpacity = "1",
                Opacity = "1", ComputedOpacity = null, Display = "inline", Visibility = "visible",
                Filter = "none", MarkerStart = "none", MarkerMid = "none", MarkerEnd = "none" }
        };
        return (SvgDefinitionParser.LowerInstanceSource(this, context, state, false), inflation);
    }

    /// <summary>Replays a referenced image through ordinary decoding/placement with the complete instance map.</summary>
    /// <remarks>Source attributes and original limits stay borrowed from this document; lengths use the live instance viewport. The caller owns
    /// returned image/path geometry; failed decode or binding releases partial owners under existing cancellation.</remarks>
    internal SvgDefinition LowerImage(SvgUseReference reference, SvgViewportFrame? frame, SvgDocumentState state)
    {
        _attribute = -1;
        SvgAffineTransform placement = SvgReferenceResolver.PlainInstancePlacement(reference, frame);
        SvgPaintableElementContext context = new(new(reference.InheritedFill, reference.IsDisplayed, reference.IsVisible, placement)
            { TextMetrics = reference.TextMetrics, OwnOpacity = reference.ElementParentOpacity, ElementDisplay = reference.ElementParentDisplay }, SvgInheritedTextStyle.Default, reference.InheritedStroke,
            default, _colors, _token, state.OutputMagnification, reference.InheritedFillRule)
        { AuthoredStyle = _authored, LengthBases = reference.LengthBases, RecordingScope = reference.RecordingScope };
        SvgDefinition result = SvgDefinitionParser.LowerInstanceSource(this, context, state);
        try
        {
            // The viewport clips completed source effects, preserving their paint bindings and full input bounds.
            if (frame is { HasAffineClipScope: false } viewport && viewport.Clip != SvgClipBox.Unbounded)
                SvgPaintRecording.WrapInstance(result.Geometries.AsSpan(), SvgPaintOpacity.Opaque, state.NewRecordingScope(), viewport.Clip, state, _token);
            return result;
        }
        catch { foreach (SvgGeometry geometry in result.Geometries) geometry.Dispose(); throw; }
    }

    public override int AttributeCount => _attributes.Length;
    public override string BaseURI => string.Empty;
    public override int Depth => _attribute < 0 ? 0 : 1;
    public override bool EOF => false;
    public override bool HasValue => _attribute >= 0;
    public override bool IsEmptyElement => _attribute < 0;
    public override string LocalName => _attribute < 0 ? _element : _attributes[_attribute].LocalName;
    public override string Name => _attribute < 0 ? _element : _attributes[_attribute].Name;
    public override string NamespaceURI => _attribute < 0 ? "http://www.w3.org/2000/svg" : _attributes[_attribute].Namespace;
    public override XmlNameTable NameTable => _names;
    public override XmlNodeType NodeType => _attribute < 0 ? XmlNodeType.Element : XmlNodeType.Attribute;
    public override string Prefix => _attribute < 0 ? string.Empty : _attributes[_attribute].Prefix;
    public override ReadState ReadState => ReadState.Interactive;
    public override string Value => _attribute < 0 ? string.Empty : _attributes[_attribute].Value;
    public override string GetAttribute(int index)
    {
        _token.ThrowIfCancellationRequested();
        if ((uint)index >= (uint)_attributes.Length) throw new ArgumentOutOfRangeException(nameof(index));
        return _attributes[index].Value;
    }
    public override string? GetAttribute(string name)
    {
        foreach (Attribute attribute in _attributes)
        {
            _token.ThrowIfCancellationRequested();
            if (attribute.Name == name) return attribute.Value;
        }
        return null;
    }
    public override string? GetAttribute(string name, string? namespaceURI)
    {
        foreach (Attribute attribute in _attributes)
        {
            _token.ThrowIfCancellationRequested();
            if (attribute.LocalName == name && attribute.Namespace == (namespaceURI ?? string.Empty)) return attribute.Value;
        }
        return null;
    }
    public override string? LookupNamespace(string prefix) => prefix switch
    {
        "" => "http://www.w3.org/2000/svg",
        "xml" => "http://www.w3.org/XML/1998/namespace",
        "xmlns" => "http://www.w3.org/2000/xmlns/",
        _ => GetAttribute("xmlns:" + prefix)
    };
    public override bool MoveToAttribute(string name)
    {
        for (int index = 0; index < _attributes.Length; index++)
        {
            _token.ThrowIfCancellationRequested();
            if (_attributes[index].Name == name) { _attribute = index; return true; }
        }
        return false;
    }
    public override bool MoveToAttribute(string name, string? namespaceURI)
    {
        for (int index = 0; index < _attributes.Length; index++)
        {
            _token.ThrowIfCancellationRequested();
            if (_attributes[index].LocalName == name && _attributes[index].Namespace == (namespaceURI ?? string.Empty)) { _attribute = index; return true; }
        }
        return false;
    }
    public override bool MoveToElement() { bool moved = _attribute >= 0; _attribute = -1; return moved; }
    public override bool MoveToFirstAttribute() { if (_attributes.Length == 0) return false; _attribute = 0; return true; }
    public override bool MoveToNextAttribute() { if (_attribute + 1 >= _attributes.Length) return false; _attribute++; return true; }
    public override bool Read() => false;
    public override bool ReadAttributeValue() => false;
    public override void ResolveEntity() => throw new InvalidOperationException("A retained leaf has no entity stream.");
}
