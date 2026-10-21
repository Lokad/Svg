using System.Xml;

namespace Lokad.Svg.Parsing;

/// <summary>Resolves policy-validated local-use presentation after bounded reference syntax is parsed.</summary>
internal static class SvgLocalReferenceParser
{
    /// <summary>Consumes this element's parsed inline declarations and context-owned authored style without rescanning them.</summary>
    internal static SvgUseReference ParseUse(XmlReader reader, SvgPaintableElementContext context, InlineStyle inline, BudgetedParserStringStore strings, double? baseWidth, double? baseHeight, SvgDocumentState state)
    {
        SvgLocalReferenceSyntax syntax = SvgLocalReferenceSyntax.Parse(reader, strings, baseWidth, baseHeight, context.WorkCounter, context.CancellationToken);
        SvgComputedStyle authored = context.AuthoredStyle ?? throw new InvalidOperationException("Use declarations must be prepared before reference parsing.");
        SvgComputedStyle style = context.ResolveStyle(reader);
        SvgMarkerStyle markers = SvgMarkerStyle.Resolve(style, context.InheritedPresentation.Markers);
        if (state.MarkerDefinition is not null && markers.HasMarkers)
            throw new SvgException(SvgErrorCode.UnsupportedFeature, "Markers on use instances inside marker content are not implemented.");
        SvgInheritedStrokeStyle inheritedStroke = context.InheritedStroke.ResolveChild(authored);
        inheritedStroke = new(Copy(inheritedStroke.Linecap), Copy(inheritedStroke.Linejoin), Copy(inheritedStroke.Dasharray), Copy(inheritedStroke.Dashoffset), Copy(inheritedStroke.Width), Copy(inheritedStroke.Miterlimit), Copy(inheritedStroke.StrokeOpacity), Copy(inheritedStroke.Stroke));
        SvgResolvedLocalReferencePresentation presentation = SvgLocalReferencePresentationResolver.Resolve(style, context.InheritedPresentation, context.NamedColors, strings);
        SvgRecordingScope scope = context.RecordingScope.Value == 0 ? new(1) : context.RecordingScope;
        if (syntax.X != 0 || syntax.Y != 0 || reader.GetAttribute("clip-path") is not null || context.ResolveTransform(reader) != context.InheritedPresentation.Transform)
            scope = state.NewRecordingScope();
        SvgInheritedTextStyle textStyle = SvgContainerInheritance.Resolve(reader, inline, authored, context.StyleSheet,
            context.InheritedPresentation, context.InheritedFillRule, context.InheritedTextStyle, context.InheritedStroke,
            context.NamedColors, context.WorkCounter, context.CancellationToken).TextStyle;
        return new SvgUseReference(syntax.Id, syntax.X, syntax.Y, syntax.Width ?? baseWidth, syntax.Height ?? baseHeight, context.ResolveTransform(reader), context.InheritedPresentation.Transform, presentation.Opacity, presentation.IsPainted, presentation.FillOverride, presentation.PaintServerId, presentation.StrokeOverride) { SourceContext = SvgSourceContext.Capture(reader), Filter = Copy(style.Filter), SourceMaskReference = Copy(reader.GetAttribute("mask")), InheritedTextStyle = textStyle, LengthBases = context.LengthBases, NonScalingStroke = style.NonScalingStroke, InheritedMarkers = markers.Retain(state, context.WorkCounter, context.CancellationToken), InheritedPaintOrder = style.PaintOrder ?? context.InheritedPresentation.PaintOrder, AllowsSolidPaintAnalysis = presentation.AllowsSolidPaintAnalysis, RecordingScope = scope, SourceOpacity = style.GetOpacity(), InheritsOpacity = style.InheritsOpacity, InheritedFill = presentation.Paint, InheritedStrokeOpacity = presentation.StrokeOpacity, InheritedStroke = inheritedStroke, InheritedFillRule = Copy(style.FillRule ?? context.InheritedFillRule), PaintServerFill = presentation.PaintServerId is null ? null : Copy(style.Fill), IsDisplayed = context.InheritedPresentation.IsDisplayed && SvgPresentationResolver.ParseDisplay(style.Display), IsVisible = SvgPresentationResolver.ParseVisibility(style.Visibility, context.InheritedPresentation.IsVisible) };

        string? Copy(string? value)
        {
            if (value is null) return null;
            context.WorkCounter.ConsumeText(value.Length, context.CancellationToken);
            return strings.Copy(value);
        }
    }
}
