using System.Xml;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Parsing;

/// <summary>Parses retained group/use content without rendering definition locations or retaining XML.</summary>
internal static class SvgSymbolContentParser
{
    internal static bool TryParse(XmlReader reader, SvgRootAttributes root, SvgDocumentState state, IReadOnlyDictionary<string, SvgColor> colors, ParserWorkCounter work, CancellationToken token, bool definitionRoot)
    {
        if (reader.LocalName is not ("g" or "use")) return false;
        SvgSymbolDefinitionBuilder builder = state.SymbolDefinition ?? throw new InvalidOperationException("Symbol content requires an open builder.");
        if (reader.LocalName == "g") SvgElementAttributePolicy.ValidateGroup(reader, work, token);
        else SvgElementAttributePolicy.ValidateUse(reader, work, token);
        if (reader.LocalName == "g" && SvgLocalUrl.ReadReference(reader, "mask") is not null)
            throw new SvgException(SvgErrorCode.UnsupportedFeature, "Masks within retained symbol containers are not implemented.");
        string? clipId = Copy(SvgClipPathSyntax.ResolveClipId(reader, state));
        string? id = reader.GetAttribute("id");
        if (id is not null && (id == state.SymbolId || state.Definitions.TryGetValue(id, out _) || state.GroupSnapshots.TryGetValue(id, out _) || state.RenderedGroupDefinitions.TryGetValue(id, out _)))
            throw new SvgException(SvgErrorCode.MalformedInput, "SVG definition ids must be unique.");
        id = id is null ? null : state.Strings.Copy(id);
        if (definitionRoot) state.SymbolId = id;
        InlineStyle sheet = SvgDocumentStyleResolver.Resolve(reader, state, work, token);
        InlineStyle inline = reader.LocalName == "use" ? SvgInlineStyleParser.ParseUse(reader.GetAttribute("style"), work, token)
            : SvgInlineStyleParser.Parse(reader.GetAttribute("style"), work, token, true);
        SvgComputedStyle style = SvgComputedStyle.Resolve(reader, inline, sheet);
        SvgPaintOpacity? strokeOpacity = style.StrokeOpacity is null ? null : SvgColorParser.ParsePaintOpacity(style.StrokeOpacity);
        SvgMarkerStyle markers = new SvgMarkerStyle(style.MarkerStart, style.MarkerMid, style.MarkerEnd).Retain(state, work, token);
        bool displayed = (style.GetSourceDisplay() != SvgDisplay.None);
        bool? visible = style.Visibility is null ? null : SvgPresentationResolver.ParseVisibility(style.Visibility, true);
        SvgSymbolPresentation inherited = builder.Presentation;
        SvgSymbolContent content = builder.EnableContent();
        state.HasRetainedSymbolContent = true;
        if (reader.LocalName == "g")
        {
            SvgShapePresentationResolver.ValidateFillRule(style.FillRule);
            bool shader = SvgPatternSyntax.IsPatternFill(style.Fill);
            string? shaderFill = shader ? Copy(style.Fill) : style.Fill is null ? inherited.PaintServerFill : null;
            string? shaderId = shaderFill is null ? null : shader ? Copy(SvgGradientSyntax.ParsePaintId(shaderFill)) : inherited.PaintServerId;
            SvgPaint paint = SvgPaintResolver.Resolve(shader ? "#ffffff" : style.Fill, style.FillOpacity, style.Color, inherited.Paint, colors);
            SvgInheritedStrokeStyle stroke = new(Copy(style.Linecap), Copy(style.Linejoin), Copy(style.Dasharray), Copy(style.Dashoffset), Copy(style.Width), Copy(style.Miterlimit), Copy(style.StrokeOpacity), Copy(style.Stroke));
            SvgPaintOpacity? opacity = style.FillOpacity is null ? null : SvgColorParser.ParsePaintOpacity(style.FillOpacity);
            SvgSolidPaint? color = style.Color is null ? null : SvgColorParser.ParsePaint(style.Color, colors);
            SvgPaintOpacity sourceOpacity = style.ResolveOpacity(inherited.SourceOpacity).GetOpacity();
            SvgSymbolPresentation presentation = new(paint, inherited.Stroke.ResolveChild(style with { Linecap = stroke.Linecap, Linejoin = stroke.Linejoin, Dasharray = stroke.Dasharray, Dashoffset = stroke.Dashoffset, Width = stroke.Width, Miterlimit = stroke.Miterlimit, StrokeOpacity = stroke.StrokeOpacity, Stroke = stroke.Stroke }),
                Copy(style.FillRule) ?? inherited.FillRule, style.Fill is not null || inherited.HasAuthoredFill, opacity ?? inherited.FillOpacity, color ?? inherited.CurrentColor, sourceOpacity) { InheritsOpacity = style.InheritsOpacity, ElementDisplay = style.GetSourceDisplay(), Markers = markers.InheritFrom(inherited.Markers), PaintOrder = style.PaintOrder ?? inherited.PaintOrder, PaintServerFill = shaderFill, PaintServerId = shaderId };
            content.BeginGroup(reader.Depth, definitionRoot ? null : id, presentation, new(paint, style.Fill is not null, opacity, color, sourceOpacity, displayed, visible)
                { TextStyleSource = SvgRetainedTextSource.CaptureElement(reader, SvgPaintableElementContext.Create(reader, root, state, colors, work, token), state,
                    state.TextReplay?.Limits ?? throw new InvalidOperationException("Group capture requires document limits.")),
                    Filter = Copy(style.Filter), ClipId = clipId, InheritsOpacity = style.InheritsOpacity, ElementDisplay = style.GetSourceDisplay(), Markers = markers, PaintOrder = style.PaintOrder, PaintServerFill = shader ? shaderFill : null, PaintServerId = shader ? shaderId : null, FillRule = Copy(style.FillRule), Stroke = stroke, StrokeOpacity = strokeOpacity }, SvgGeometrySyntax.ParseElementTransform(reader, work, token));
            if (!definitionRoot && id is not null) state.Definitions.TryAdd(id, new([], false, null, null) { SymbolContent = new(content, 0, 0), BorrowedFromSymbol = true });
            if (reader.IsEmptyElement) content.EndGroup(reader.Depth, state);
        }
        else
        {
            SvgPaintableElementContext context = new(new(inherited.Paint, true, true, SvgAffineTransform.Identity) { OwnOpacity = inherited.SourceOpacity, ElementDisplay = inherited.ElementDisplay == SvgDisplay.Inherit ? SvgDisplay.Inline : inherited.ElementDisplay, Markers = inherited.Markers, PaintOrder = inherited.PaintOrder ?? SvgPaintOrder.FillStrokeMarkers, PaintServerFill = inherited.PaintServerFill }, root.InheritedTextStyle, inherited.Stroke, sheet, colors, work, token, state.OutputMagnification, null) { RecordingScope = new(1) };
            double? width = state.SymbolViewBox?.Width ?? root.UserViewportWidth;
            double? height = state.SymbolViewBox?.Height ?? root.UserViewportHeight;
            context = context with { AuthoredStyle = style };
            if (SvgLocalReferenceParser.ParseUse(reader, context, inline, state.Strings, width, height, state) is not {} reference)
            {
                if (id is { Length: > 0 }) state.Definitions.TryAdd(state.Strings.Copy(id), new([], false, null, null));
                SvgStructuralAdmission.ConsumeLeafChildren(reader, state, work, token);
                return true;
            }
            SvgPaintOpacity? fillOpacity = style.FillOpacity is null ? null : SvgColorParser.ParsePaintOpacity(style.FillOpacity);
            SvgSolidPaint? color = style.Color is null ? null : SvgColorParser.ParsePaint(style.Color, colors);
            SvgInheritedStrokeStyle stroke = new(Copy(style.Linecap), Copy(style.Linejoin), Copy(style.Dasharray), Copy(style.Dashoffset), Copy(style.Width), Copy(style.Miterlimit), Copy(style.StrokeOpacity), Copy(style.Stroke));
            int start = content.AddUse(reference, fillOpacity, color, strokeOpacity, stroke, style.PaintOrder, markers,
                SvgRetainedTextSource.CaptureElement(reader, context, state, state.TextReplay?.Limits ?? throw new InvalidOperationException("Use capture requires document limits.")), clipId, style.Fill is not null, displayed, visible);
            if (id is not null) state.Definitions.TryAdd(id, new([], false, null, null) { SymbolContent = new(content, start, 1), BorrowedFromSymbol = true });
            SvgStructuralAdmission.ConsumeLeafChildren(reader, state, work, token);
        }
        return true;

        string? Copy(string? value)
        {
            if (value is null) return null;
            work.ConsumeText(value.Length, token);
            return state.Strings.Copy(value);
        }
    }
}
