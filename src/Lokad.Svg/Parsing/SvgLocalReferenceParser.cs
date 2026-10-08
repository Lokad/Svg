using System.Xml;

namespace Lokad.Svg.Parsing;

/// <summary>Resolves policy-validated local-use presentation after bounded reference syntax is parsed.</summary>
internal static class SvgLocalReferenceParser
{
    internal static SvgUseReference ParseUse(XmlReader reader, SvgPaintableElementContext context, BudgetedParserStringStore strings, double? baseWidth, double? baseHeight, SvgDocumentState state)
    {
        SvgLocalReferenceSyntax syntax = SvgLocalReferenceSyntax.Parse(reader, strings, baseWidth, baseHeight);
        SvgComputedStyle style = context.ResolveStyle(reader);
        SvgComputedStyle authored = SvgComputedStyleResolver.Resolve(reader, context.StyleSheet, context.WorkCounter, context.CancellationToken);
        SvgResolvedLocalReferencePresentation presentation = SvgLocalReferencePresentationResolver.Resolve(style, context.InheritedPresentation, context.NamedColors, strings);
        SvgRecordingScope scope = context.RecordingScope.Value == 0 ? new(1) : context.RecordingScope;
        if (syntax.X != 0 || syntax.Y != 0 || reader.GetAttribute("clip-path") is not null || context.ResolveTransform(reader) != context.InheritedPresentation.Transform)
            scope = state.NewRecordingScope();
        return new SvgUseReference(syntax.Id, syntax.X, syntax.Y, syntax.Width ?? baseWidth, syntax.Height ?? baseHeight, context.ResolveTransform(reader), context.InheritedPresentation.Transform, presentation.Opacity, presentation.IsPainted, presentation.FillOverride, presentation.PaintServerId, presentation.StrokeOverride) { AllowsSolidPaintAnalysis = presentation.AllowsSolidPaintAnalysis, RecordingScope = scope, SourceOpacity = SvgColorParser.ParsePaintOpacity(style.Opacity), InheritedFill = presentation.Paint, InheritedStrokeOpacity = presentation.StrokeOpacity, HasPaintContextOverride = style.Color is not null || style.FillOpacity is not null, HasFillOpacityOverride = style.FillOpacity is not null, HasStrokeOpacityOverride = authored.StrokeOpacity is not null, IsDisplayed = context.InheritedPresentation.IsDisplayed && SvgPresentationResolver.ParseDisplay(style.Display), IsVisible = SvgPresentationResolver.ParseVisibility(style.Visibility, context.InheritedPresentation.IsVisible) };
    }
}
