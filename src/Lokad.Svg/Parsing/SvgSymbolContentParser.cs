using System.Xml;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Parsing;

/// <summary>Parses retained group/use content without rendering definition locations or retaining XML.</summary>
internal static class SvgSymbolContentParser
{
    internal static bool TryParse(XmlReader reader, SvgRootAttributes root, SvgDocumentState state, IReadOnlyDictionary<string, SvgColor> colors, ParserWorkCounter work, CancellationToken token)
    {
        if (reader.LocalName is not ("g" or "use")) return false;
        SvgSymbolDefinitionBuilder builder = state.SymbolDefinition ?? throw new InvalidOperationException("Symbol content requires an open builder.");
        if (reader.LocalName == "g") SvgElementAttributePolicy.ValidateGroup(reader, work, token);
        else SvgElementAttributePolicy.ValidateUse(reader, work, token);
        if (reader.GetAttribute("clip-path") is not null || reader.GetAttribute("mask") is not null)
            throw new SvgException(SvgErrorCode.UnsupportedFeature, "Clips and masks within retained symbol containers are not implemented.");
        string? id = reader.GetAttribute("id");
        if (id is not null && (id == state.SymbolId || state.Definitions.TryGetValue(id, out _) || state.GroupSnapshots.TryGetValue(id, out _)))
            throw new SvgException(SvgErrorCode.MalformedInput, "SVG definition ids must be unique.");
        id = id is null ? null : state.Strings.Copy(id);
        InlineStyle sheet = SvgDocumentStyleResolver.Resolve(reader, false, state, work, token);
        SvgComputedStyle style = SvgComputedStyleResolver.Resolve(reader, sheet, work, token);
        bool displayed = SvgPresentationResolver.ParseDisplay(style.Display);
        bool? visible = style.Visibility is null ? null : SvgPresentationResolver.ParseVisibility(style.Visibility, true);
        SvgSymbolPresentation inherited = builder.Presentation;
        SvgSymbolContent content = builder.EnableContent();
        state.HasRetainedSymbolContent = true;
        if (reader.LocalName == "g")
        {
            SvgShapePresentationResolver.ValidateFillRule(style.FillRule);
            SvgPaint paint = SvgPaintResolver.Resolve(style.Fill, style.FillOpacity, style.Color, inherited.Paint, colors);
            SvgInheritedStrokeStyle stroke = new(Copy(style.Linecap), Copy(style.Linejoin), Copy(style.Dasharray), Copy(style.Dashoffset), Copy(style.Width), Copy(style.Miterlimit), Copy(style.StrokeOpacity), Copy(style.Stroke));
            SvgPaintOpacity? opacity = style.FillOpacity is null ? null : SvgColorParser.ParsePaintOpacity(style.FillOpacity);
            SvgColor? color = style.Color is null ? null : SvgColorParser.Parse(style.Color, colors);
            SvgPaintOpacity sourceOpacity = SvgColorParser.ParsePaintOpacity(style.Opacity);
            SvgSymbolPresentation presentation = new(paint, inherited.Stroke.ResolveChild(style with { Linecap = stroke.Linecap, Linejoin = stroke.Linejoin, Dasharray = stroke.Dasharray, Dashoffset = stroke.Dashoffset, Width = stroke.Width, Miterlimit = stroke.Miterlimit, StrokeOpacity = stroke.StrokeOpacity, Stroke = stroke.Stroke }),
                Copy(style.FillRule) ?? inherited.FillRule, style.Fill is not null || inherited.HasAuthoredFill, opacity ?? inherited.FillOpacity, color ?? inherited.CurrentColor, sourceOpacity);
            content.BeginGroup(reader.Depth, id, presentation, new(paint, style.Fill is not null, opacity, color, sourceOpacity, displayed, visible), SvgGeometrySyntax.ParseTransform(reader.GetAttribute("transform"), work, token));
            if (id is not null) state.Definitions.TryAdd(id, new([], false, null, null) { SymbolContent = new(content, 0, 0), BorrowedFromSymbol = true });
            if (reader.IsEmptyElement) content.EndGroup(reader.Depth, state);
        }
        else
        {
            SvgPaintableElementContext context = new(new(inherited.Paint, true, true, SvgAffineTransform.Identity), root.InheritedTextStyle, inherited.Stroke, sheet, colors, work, token, state.OutputMagnification, null) { RecordingScope = new(1) };
            double? width = state.SymbolViewBox?.Width ?? root.Width?.ToAbsoluteOrNull();
            double? height = state.SymbolViewBox?.Height ?? root.Height?.ToAbsoluteOrNull();
            SvgUseReference reference = SvgLocalReferenceParser.ParseUse(reader, context, state.Strings, width, height, state);
            SvgPaintOpacity? fillOpacity = style.FillOpacity is null ? null : SvgColorParser.ParsePaintOpacity(style.FillOpacity);
            SvgColor? color = style.Color is null ? null : SvgColorParser.Parse(style.Color, colors);
            int start = content.AddUse(reference, fillOpacity, color, displayed, visible);
            if (id is not null) state.Definitions.TryAdd(id, new([], false, null, null) { SymbolContent = new(content, start, 1), BorrowedFromSymbol = true });
            state.HasUnsupportedContent |= !reader.IsEmptyElement;
        }
        state.SawStyleableNode = true;
        return true;

        string? Copy(string? value) => value is null ? null : state.Strings.Copy(value);
    }
}
