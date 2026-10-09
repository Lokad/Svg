using System.Xml;

namespace Lokad.Svg.Parsing;

/// <summary>Parses one bounded group or link context before its children are traversed.</summary>
internal static class SvgGroupParser
{
    internal static SvgParsedGroup Parse(XmlReader reader, bool directChild, SvgRootAttributes root, SvgDocumentState state, IReadOnlyDictionary<string, SvgColor> namedColors, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        if (reader.LocalName == "switch") SvgElementAttributePolicy.ValidateSwitch(reader, workCounter, cancellationToken);
        else SvgElementAttributePolicy.ValidateGroup(reader, workCounter, cancellationToken);
        SvgPaintableElementContext context = SvgPaintableElementContext.Create(reader, directChild, root, state, namedColors, workCounter, cancellationToken);
        InlineStyle inline = SvgInlineStyleParser.Parse(reader.GetAttribute("style"), workCounter, cancellationToken, true);
        SvgComputedStyle style = SvgComputedStyle.Resolve(reader, inline, context.StyleSheet);
        SvgInheritedContainer resolved = SvgContainerInheritance.Resolve(reader, inline, style, context.StyleSheet, context.InheritedPresentation, context.InheritedFillRule, context.InheritedTextStyle, context.InheritedStroke, root.Opacity != 255 || state.HasOpacityScope, namedColors, workCounter, cancellationToken);
        if (style.Fill is not null && resolved.Presentation.PaintServerFill is string fill)
        {
            workCounter.ConsumeText(fill.Length, cancellationToken);
            resolved = resolved with { Presentation = resolved.Presentation with { PaintServerFill = state.Strings.Copy(fill) } };
        }
        string? maskReference = reader.GetAttribute("mask");
        byte? mask = null;
        string? deferredMask = null;
        if (maskReference is not null)
        {
            string maskId = SvgMaskSyntax.ParseMaskId(maskReference);
            if (state.Masks.TryGetValue(maskId, out byte knownMask)) mask = knownMask;
            else if (reader.IsEmptyElement) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only defined local mask references are implemented.");
            else deferredMask = state.Strings.Copy(maskId);
            if (mask is not null && SvgMaskSyntax.HasCustomRegion(reader, state))
            {
                // Custom regions validate at document-end resolution (vacuous check), so a backward mask with a custom region takes the deferred path.
                deferredMask = state.Strings.Copy(maskId);
                mask = null;
            }
            if (mask is not null && !reader.IsEmptyElement)
            {
                // Preserve mask multiplication before outer opacity. Deferred resolution
                // retains both factors on an isolated group, including uniform masks
                // whose value must apply after all children have composed.
                deferredMask = state.Strings.Copy(maskId);
                mask = null;
            }
        }
        byte combined = mask is null ? resolved.Opacity : SvgReferenceResolver.MultiplyAlpha(resolved.Opacity, mask.Value);
        bool hasMaskScope = reader.IsEmptyElement ? false : PushMaskScope(reader, state);
        SvgGroupKind kind = reader.LocalName == "switch" ? SvgGroupKind.Switch : SvgStructuralVocabulary.IsGroupContainer(reader.LocalName) ? SvgGroupKind.Group : SvgGroupKind.Link;
        SvgPaintOpacity sourceOpacity = SvgColorParser.ParsePaintOpacity(style.Opacity);
        SvgRecordingScope opacityScope = context.RecordingScope.Value == 0 ? new(1) : context.RecordingScope;
        if (reader.GetAttribute("clip-path") is not null || resolved.Presentation.Transform != context.InheritedPresentation.Transform)
            opacityScope = state.NewRecordingScope();
        SvgRecordingScope contentScope = maskReference is null ? opacityScope : state.NewRecordingScope();
        return new SvgParsedGroup(new SvgGroupScope(reader.Depth, kind, resolved.Presentation, resolved.TextStyle, combined != 255 || sourceOpacity.Value != 1, hasMaskScope, resolved.Stroke, resolved.FillRule) { HasDeferredMask = deferredMask is not null, RecordingScope = contentScope, ClipRule = style.ClipRule ?? context.InheritedClipRule }, combined, deferredMask) { SourceOpacity = sourceOpacity, RecordingScope = opacityScope };
    }

    /// <summary>Pushes the referenced mask content box for child coverage checks, reporting whether a scope was pushed.</summary>
    private static bool PushMaskScope(XmlReader reader, SvgDocumentState state)
    {
        string? reference = reader.GetAttribute("mask");
        if (reference is null) return false;
        if (state.MaskContents.TryGetValue(SvgMaskSyntax.ParseMaskId(reference), out _)) return false;
        if (!state.MaskContentBoxes.TryGetValue(SvgMaskSyntax.ParseMaskId(reference), out SvgClipBox scope)) return false;
        state.MaskScopes.Add(scope);
        return true;
    }
}

/// <summary>Couples a stored child context with the opacity required to enter its isolated surface.</summary>
internal readonly record struct SvgParsedGroup(SvgGroupScope Scope, byte Opacity, string? DeferredMask)
{
    internal SvgPaintOpacity SourceOpacity { get; init; }
    internal SvgRecordingScope RecordingScope { get; init; }
}

