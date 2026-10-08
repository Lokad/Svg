using System.Xml;

namespace Lokad.Svg.Parsing;

/// <summary>Resolves policy-validated local-use presentation after bounded reference syntax is parsed.</summary>
internal static class SvgLocalReferenceParser
{
    internal static SvgUseReference ParseUse(XmlReader reader, SvgPaintableElementContext context, BudgetedParserStringStore strings, double? baseWidth, double? baseHeight, SvgDocumentState state)
    {
        SvgLocalReferenceSyntax syntax = SvgLocalReferenceSyntax.Parse(reader, strings, baseWidth, baseHeight);
        SvgComputedStyle style = context.ResolveStyle(reader);
        SvgResolvedLocalReferencePresentation presentation = SvgLocalReferencePresentationResolver.Resolve(style, context.InheritedPresentation, context.NamedColors, strings);
        SvgRecordingScope scope = context.RecordingScope.Value == 0 ? new(1) : context.RecordingScope;
        if (syntax.X != 0 || syntax.Y != 0 || reader.GetAttribute("clip-path") is not null || context.ResolveTransform(reader) != context.InheritedPresentation.Transform)
            scope = state.NewRecordingScope();
        return new SvgUseReference(syntax.Id, syntax.X, syntax.Y, syntax.Width ?? baseWidth, syntax.Height ?? baseHeight, context.ResolveTransform(reader), context.InheritedPresentation.Transform, presentation.Opacity, presentation.IsPainted, presentation.FillOverride, presentation.PaintServerId, presentation.StrokeOverride) { AllowsSolidPaintAnalysis = presentation.AllowsSolidPaintAnalysis, RecordingScope = scope, SourceOpacity = SvgColorParser.ParsePaintOpacity(style.Opacity) };
    }
}
