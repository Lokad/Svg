using System.Xml;

namespace Lokad.Svg.Parsing;

/// <summary>Resolves policy-validated local-use presentation after bounded reference syntax is parsed.</summary>
internal static class SvgLocalReferenceParser
{
    internal static SvgUseReference ParseUse(XmlReader reader, SvgPaintableElementContext context, BudgetedParserStringStore strings, double? baseWidth, double? baseHeight, SvgDocumentState state)
    {
        SvgLocalReferenceSyntax syntax = SvgLocalReferenceSyntax.Parse(reader, strings, baseWidth, baseHeight);
        SvgComputedStyle style = context.ResolveStyle(reader);
        SvgMarkerStyle markers = SvgMarkerStyle.Resolve(style, context.InheritedPresentation.Markers);
        if (state.MarkerDefinition is not null && markers.HasMarkers)
            throw new SvgException(SvgErrorCode.UnsupportedFeature, "Markers on use instances inside marker content are not implemented.");
        SvgComputedStyle authored = SvgComputedStyleResolver.Resolve(reader, context.StyleSheet, context.WorkCounter, context.CancellationToken);
        SvgInheritedStrokeStyle inheritedStroke = context.InheritedStroke.ResolveChild(authored);
        inheritedStroke = new(Copy(inheritedStroke.Linecap), Copy(inheritedStroke.Linejoin), Copy(inheritedStroke.Dasharray), Copy(inheritedStroke.Dashoffset), Copy(inheritedStroke.Width), Copy(inheritedStroke.Miterlimit), Copy(inheritedStroke.StrokeOpacity), Copy(inheritedStroke.Stroke));
        SvgResolvedLocalReferencePresentation presentation = SvgLocalReferencePresentationResolver.Resolve(style, context.InheritedPresentation, context.NamedColors, strings);
        SvgRecordingScope scope = context.RecordingScope.Value == 0 ? new(1) : context.RecordingScope;
        if (syntax.X != 0 || syntax.Y != 0 || reader.GetAttribute("clip-path") is not null || context.ResolveTransform(reader) != context.InheritedPresentation.Transform)
            scope = state.NewRecordingScope();
        return new SvgUseReference(syntax.Id, syntax.X, syntax.Y, syntax.Width ?? baseWidth, syntax.Height ?? baseHeight, context.ResolveTransform(reader), context.InheritedPresentation.Transform, presentation.Opacity, presentation.IsPainted, presentation.FillOverride, presentation.PaintServerId, presentation.StrokeOverride) { InheritedMarkers = markers.Retain(state, context.WorkCounter, context.CancellationToken), InheritedPaintOrder = style.PaintOrder ?? context.InheritedPresentation.PaintOrder, AllowsSolidPaintAnalysis = presentation.AllowsSolidPaintAnalysis, RecordingScope = scope, SourceOpacity = style.GetOpacity(), InheritsOpacity = style.InheritsOpacity, InheritedFill = presentation.Paint, InheritedStrokeOpacity = presentation.StrokeOpacity, InheritedStroke = inheritedStroke, InheritedFillRule = Copy(style.FillRule ?? context.InheritedFillRule), PaintServerFill = presentation.PaintServerId is null ? null : Copy(style.Fill), IsDisplayed = context.InheritedPresentation.IsDisplayed && SvgPresentationResolver.ParseDisplay(style.Display), IsVisible = SvgPresentationResolver.ParseVisibility(style.Visibility, context.InheritedPresentation.IsVisible) };

        string? Copy(string? value)
        {
            if (value is null) return null;
            context.WorkCounter.ConsumeText(value.Length, context.CancellationToken);
            return strings.Copy(value);
        }
    }
}
