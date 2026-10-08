using System.Xml;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Parsing;

/// <summary>Retains typed source nodes for identifier-bearing rendered groups and their descendants.</summary>
/// <remarks>The document owns node/attribute reservations; leaves retain no native geometry until an instance
/// lowers them. Capture preserves only authored styles and local transforms, excluding referenced ancestors.
/// All construction is parser-thread confined, budgeted, work charged and cancellation aware.</remarks>
internal static class SvgRenderedGroupSourceParser
{
    internal static void BeginGroup(XmlReader reader, bool directChild, SvgRootAttributes root, SvgDocumentState state,
        IReadOnlyDictionary<string, SvgColor> colors, ParserWorkCounter work, CancellationToken token)
    {
        string? id = reader.LocalName == "g" ? reader.GetAttribute("id") : null;
        if (state.RenderedGroupContent is null && id is null) return;
        if (id is not null && (state.Definitions.TryGetValue(id, out _) || state.GroupSnapshots.TryGetValue(id, out _)
            || state.RenderedGroupDefinitions.TryGetValue(id, out _)))
            throw new SvgException(SvgErrorCode.MalformedInput, "SVG definition ids must be unique.");
        SvgPaintableElementContext context = SvgPaintableElementContext.Create(reader, directChild, root, state, colors, work, token);
        SvgComputedStyle style = SvgComputedStyleResolver.Resolve(reader, context.StyleSheet, work, token);
        SvgSymbolContent content;
        if (state.RenderedGroupContent is null)
        {
            content = SvgSymbolContent.Create(state.Budget, work, token, new(SvgPaint.Default, default, null, false, null, null, SvgPaintOpacity.Opaque));
            try { state.RetainStrokeContent(content); }
            catch { content.Dispose(); throw; }
            content.IsRenderedGroup = true;
            state.HasRetainedSymbolContent = true;
            state.RenderedGroupContent = content;
            state.RenderedGroupRootDepth = reader.Depth;
        }
        else content = state.RenderedGroupContent;
        id = id is null ? null : Copy(id);
        SvgPaint paint = SvgPaintResolver.Resolve(style.Fill, style.FillOpacity, style.Color, SvgPaint.Default, colors);
        SvgInheritedStrokeStyle stroke = new(Copy(style.Linecap), Copy(style.Linejoin), Copy(style.Dasharray), Copy(style.Dashoffset),
            Copy(style.Width), Copy(style.Miterlimit), Copy(style.StrokeOpacity), Copy(style.Stroke));
        SvgPaintOpacity? fillOpacity = style.FillOpacity is null ? null : SvgColorParser.ParsePaintOpacity(style.FillOpacity);
        SvgColor? color = style.Color is null ? null : SvgColorParser.Parse(style.Color, colors);
        SvgPaintOpacity opacity = SvgColorParser.ParsePaintOpacity(style.Opacity);
        string? fillRule = Copy(style.FillRule);
        SvgSymbolPresentation presentation = new(paint, stroke, fillRule, style.Fill is not null, fillOpacity, color, opacity);
        content.BeginGroup(reader.Depth, id, presentation,
            new(paint, style.Fill is not null, fillOpacity, color, opacity, SvgPresentationResolver.ParseDisplay(style.Display),
                style.Visibility is null ? null : SvgPresentationResolver.ParseVisibility(style.Visibility, true))
            { Stroke = stroke, FillRule = fillRule, StrokeOpacity = style.StrokeOpacity is null ? null : SvgColorParser.ParsePaintOpacity(style.StrokeOpacity) },
            SvgGeometrySyntax.ParseTransform(reader.GetAttribute("transform"), work, token));
        if (id is not null) state.RenderedGroupDefinitions.TryAdd(id, new([], false, null, null)
            { SymbolContent = new(content, 0, 0), BorrowedFromSymbol = true, RenderedGroupId = id });
        if (reader.GetAttribute("clip-path") is not null || reader.GetAttribute("mask") is not null) content.AddUnsupported();
        if (reader.IsEmptyElement) EndGroup(reader.Depth, state, work, token);

        string? Copy(string? value)
        {
            if (value is null) return null;
            work.ConsumeText(value.Length, token);
            return state.Strings.Copy(value);
        }
    }

    internal static void EndGroup(int depth, SvgDocumentState state, ParserWorkCounter work, CancellationToken token)
    {
        SvgSymbolContent? content = state.RenderedGroupContent;
        if (content is null || !content.EndRenderedGroup(depth, state)) return;
        if (depth != state.RenderedGroupRootDepth) return;
        content.Seal(work, token, null);
        state.RenderedGroupContent = null;
        state.RenderedGroupRootDepth = -1;
    }

    internal static void CaptureLeaf(XmlReader reader, SvgPaintableElementContext context, SvgDocumentState state)
    {
        if (state.RenderedGroupContent is not {} content) return;
        if (!SvgStructuralVocabulary.IsDefinitionLeaf(reader.LocalName)) { content.AddUnsupported(); return; }
        SvgComputedStyle style = SvgComputedStyleResolver.Resolve(reader, context.StyleSheet, context.WorkCounter, context.CancellationToken);
        bool displayed = SvgPresentationResolver.ParseDisplay(style.Display);
        bool? visible = style.Visibility is null ? null : SvgPresentationResolver.ParseVisibility(style.Visibility, true);
        SvgDefinition source = new([], false, null, null) { LeafSource = SvgDefinitionLeafSource.CaptureUnconstructed(reader, context, state), SourceDisplayed = displayed, SourceVisible = visible };
        content.AddLeaf(source, displayed, visible);
    }

    internal static void CaptureUse(XmlReader reader, SvgPaintableElementContext context, SvgUseReference reference, SvgDocumentState state)
    {
        if (state.RenderedGroupContent is not {} content) return;
        SvgComputedStyle style = SvgComputedStyleResolver.Resolve(reader, context.StyleSheet, context.WorkCounter, context.CancellationToken);
        SvgInheritedStrokeStyle stroke = new(Copy(style.Linecap), Copy(style.Linejoin), Copy(style.Dasharray), Copy(style.Dashoffset),
            Copy(style.Width), Copy(style.Miterlimit), Copy(style.StrokeOpacity), Copy(style.Stroke));
        reference = reference with { Transform = SvgGeometrySyntax.ParseTransform(reader.GetAttribute("transform"), context.WorkCounter, context.CancellationToken),
            InheritedTransform = SvgAffineTransform.Identity, InheritedFillRule = Copy(style.FillRule), SourceMaskReference = Copy(reader.GetAttribute("mask")) };
        content.AddUse(reference, style.FillOpacity is null ? null : SvgColorParser.ParsePaintOpacity(style.FillOpacity),
            style.Color is null ? null : SvgColorParser.Parse(style.Color, context.NamedColors),
            style.StrokeOpacity is null ? null : SvgColorParser.ParsePaintOpacity(style.StrokeOpacity), stroke,
            SvgPresentationResolver.ParseDisplay(style.Display), style.Visibility is null ? null : SvgPresentationResolver.ParseVisibility(style.Visibility, true));
        if (reader.GetAttribute("clip-path") is not null) content.AddUnsupported();

        string? Copy(string? value)
        {
            if (value is null) return null;
            context.WorkCounter.ConsumeText(value.Length, context.CancellationToken);
            return state.Strings.Copy(value);
        }
    }
}
