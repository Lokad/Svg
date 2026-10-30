using System.Xml;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Parsing;

/// <summary>Retains typed source nodes for identifier-bearing rendered groups and their descendants.</summary>
/// <remarks>The document owns node/attribute reservations; leaves retain no native geometry until an instance
/// lowers them. Capture preserves only authored styles and local transforms, excluding referenced ancestors.
/// All construction is parser-thread confined, budgeted and cancellation aware.</remarks>
internal static class SvgRenderedGroupSourceParser
{
    internal static void BeginGroup(XmlReader reader, SvgRootAttributes root, SvgDocumentState state, SvgRenderLimits limits,
        IReadOnlyDictionary<string, SvgColor> colors, CancellationToken token)
    {
        string? id = reader.LocalName == "g" ? reader.GetAttribute("id") : null;
        if (state.RenderedGroupContent is null && id is null) return;
        if (id is not null && (state.Definitions.TryGetValue(id, out _) || state.GroupSnapshots.TryGetValue(id, out _)
            || state.RenderedGroupDefinitions.TryGetValue(id, out _)))
            throw new SvgException(SvgErrorCode.MalformedInput, "SVG definition ids must be unique.");
        SvgPaintableElementContext context = SvgPaintableElementContext.Create(reader, root, state, colors, token);
        SvgComputedStyle style = SvgComputedStyleResolver.Resolve(reader, context.StyleSheet, token, true).ResolveOpacity(context.InheritedPresentation.ElementOpacity).ResolveDisplay(context.InheritedPresentation.ElementDisplay);
        _ = SvgPaintResolver.ResolveFallbacks(ref style, state.PaintResources, colors, reader.LocalName);
        SvgSymbolContent content;
        if (state.RenderedGroupContent is null)
        {
            content = SvgSymbolContent.Create(state.Budget, token, new(SvgPaint.Default, default, null, false, null, null, SvgPaintOpacity.Opaque));
            try { state.RetainStrokeContent(content); }
            catch { content.Dispose(); throw; }
            content.IsRenderedGroup = true;
            state.HasRetainedSymbolContent = true;
            state.RenderedGroupContent = content;
            state.RenderedGroupRootDepth = reader.Depth;
        }
        else content = state.RenderedGroupContent;
        id = id is null ? null : Copy(id);
        bool shader = SvgPatternSyntax.IsPatternFill(style.Fill);
        string? shaderFill = shader ? Copy(style.Fill) : null;
        string? shaderId = shaderFill is null ? null : Copy(SvgGradientSyntax.ParsePaintId(shaderFill));
        SvgPaint paint = SvgPaintResolver.Resolve(shader ? "#ffffff" : style.Fill, style.FillOpacity, style.Color, SvgPaint.Default, colors);
        SvgInheritedStrokeStyle stroke = new(Copy(style.Linecap), Copy(style.Linejoin), Copy(style.Dasharray), Copy(style.Dashoffset),
            Copy(style.Width), Copy(style.Miterlimit), Copy(style.StrokeOpacity), Copy(style.Stroke));
        SvgPaintOpacity? fillOpacity = style.FillOpacity is null ? null : SvgColorParser.ParsePaintOpacity(style.FillOpacity);
        SvgSolidPaint? color = style.Color is null ? null : SvgColorParser.ParsePaint(style.Color, colors);
        SvgPaintOpacity opacity = style.GetOpacity();
        string? fillRule = Copy(style.FillRule);
        SvgSymbolPresentation presentation = new(paint, stroke, fillRule, style.Fill is not null, fillOpacity, color, opacity) { InheritsOpacity = style.InheritsOpacity, ElementDisplay = style.GetSourceDisplay(), Markers = new SvgMarkerStyle(style.MarkerStart, style.MarkerMid, style.MarkerEnd).Retain(state, token), PaintOrder = style.PaintOrder, PaintServerFill = shaderFill, PaintServerId = shaderId };
        content.BeginGroup(reader.Depth, id, presentation,
            new(paint, style.Fill is not null, fillOpacity, color, opacity, (style.GetSourceDisplay() != SvgDisplay.None),
                style.Visibility is null ? null : SvgPresentationResolver.ParseVisibility(style.Visibility, true))
            { TextStyleSource = SvgRetainedTextSource.CaptureElement(reader, context, state, limits), InheritsOpacity = style.InheritsOpacity, ElementDisplay = style.GetSourceDisplay(), Markers = presentation.Markers, PaintOrder = style.PaintOrder, PaintServerFill = shaderFill, PaintServerId = shaderId, Stroke = stroke, FillRule = fillRule, StrokeOpacity = style.StrokeOpacity is null ? null : SvgColorParser.ParsePaintOpacity(style.StrokeOpacity) },
            SvgGeometrySyntax.ParseElementTransform(reader, token));
        if (id is not null) state.RenderedGroupDefinitions.TryAdd(id, new([], false, null, null)
            { SymbolContent = new(content, 0, 0), BorrowedFromSymbol = true, RenderedGroupId = id });
        if (SvgLocalUrl.ReadReference(reader, "clip-path") is not null || SvgLocalUrl.ReadReference(reader, "mask") is not null
            || style.Filter is string filter && filter.Trim() != "none") content.AddUnsupported();
        if (reader.IsEmptyElement) EndGroup(reader.Depth, state, token);

        string? Copy(string? value)
        {
            if (value is null) return null;
            token.ThrowIfCancellationRequested();
            return state.Strings.Copy(value);
        }
    }

    internal static void EndGroup(int depth, SvgDocumentState state, CancellationToken token)
    {
        SvgSymbolContent? content = state.RenderedGroupContent;
        if (content is null || !content.EndRenderedGroup(depth, state)) return;
        if (depth != state.RenderedGroupRootDepth) return;
        content.Seal(token, null);
        state.RenderedGroupContent = null;
        state.RenderedGroupRootDepth = -1;
    }

    internal static void CaptureLeaf(XmlReader reader, SvgPaintableElementContext context, SvgDocumentState state)
    {
        if (state.RenderedGroupContent is not {} content) return;
        if (!SvgStructuralVocabulary.IsDefinitionLeaf(reader.LocalName)) { content.AddUnsupported(); return; }
        SvgComputedStyle style = context.AuthoredStyle ?? SvgComputedStyleResolver.Resolve(reader, context.StyleSheet, context.CancellationToken);
        bool displayed = (style.GetSourceDisplay() != SvgDisplay.None);
        bool? visible = style.Visibility is null ? null : SvgPresentationResolver.ParseVisibility(style.Visibility, true);
        SvgDefinition source = new([], false, null, null) { LeafSource = SvgDefinitionLeafSource.CaptureUnconstructed(reader, context, state), SourceDisplayed = displayed, SourceDisplay = style.GetSourceDisplay(), SourceVisible = visible };
        content.AddLeaf(source, displayed, visible);
    }

    internal static void CaptureUse(XmlReader reader, SvgPaintableElementContext context, SvgUseReference reference, SvgDocumentState state)
    {
        if (state.RenderedGroupContent is not {} content) return;
        SvgComputedStyle style = context.AuthoredStyle ?? throw new InvalidOperationException("Use capture requires prepared declarations.");
        SvgInheritedStrokeStyle stroke = new(Copy(style.Linecap), Copy(style.Linejoin), Copy(style.Dasharray), Copy(style.Dashoffset),
            Copy(style.Width), Copy(style.Miterlimit), Copy(style.StrokeOpacity), Copy(style.Stroke));
        reference = reference with { Transform = SvgGeometrySyntax.ParseElementTransform(reader, context.CancellationToken),
            InheritedTransform = SvgAffineTransform.Identity, InheritedFillRule = Copy(style.FillRule), SourceMaskReference = Copy(SvgLocalUrl.ReadReference(reader, "mask")) };
        content.AddUse(reference, style.FillOpacity is null ? null : SvgColorParser.ParsePaintOpacity(style.FillOpacity),
            style.Color is null ? null : SvgColorParser.ParsePaint(style.Color, context.NamedColors),
            style.StrokeOpacity is null ? null : SvgColorParser.ParsePaintOpacity(style.StrokeOpacity), stroke, style.PaintOrder, new SvgMarkerStyle(style.MarkerStart, style.MarkerMid, style.MarkerEnd).Retain(state, context.CancellationToken),
            SvgRetainedTextSource.CaptureElement(reader, context, state, state.TextReplay?.Limits ?? throw new InvalidOperationException("Use capture requires document limits.")), null, style.Fill is not null,
            (style.GetSourceDisplay() != SvgDisplay.None), style.Visibility is null ? null : SvgPresentationResolver.ParseVisibility(style.Visibility, true));
        if (SvgLocalUrl.ReadReference(reader, "clip-path") is not null) content.AddUnsupported();

        string? Copy(string? value)
        {
            if (value is null) return null;
            context.CancellationToken.ThrowIfCancellationRequested();
            return state.Strings.Copy(value);
        }
    }
}
