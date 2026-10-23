using System.Xml;

namespace Lokad.Svg.Parsing;

/// <summary>Parses one bounded group or link context before its children are traversed.</summary>
internal static class SvgGroupParser
{
    internal static SvgParsedGroup Parse(XmlReader reader, SvgRootAttributes root, SvgDocumentState state, IReadOnlyDictionary<string, SvgColor> namedColors, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        if (reader.LocalName == "switch") SvgElementAttributePolicy.ValidateSwitch(reader, workCounter, cancellationToken);
        else SvgElementAttributePolicy.ValidateGroup(reader, workCounter, cancellationToken);
        SvgPaintableElementContext context = SvgPaintableElementContext.Create(reader, root, state, namedColors, workCounter, cancellationToken);
        InlineStyle inline = SvgInlineStyleParser.Parse(reader.GetAttribute("style"), workCounter, cancellationToken, true);
        SvgComputedStyle style = SvgComputedStyle.Resolve(reader, inline, context.StyleSheet).ResolveOpacity(context.InheritedPresentation.ElementOpacity);
        SvgInheritedContainer resolved = SvgContainerInheritance.Resolve(reader, inline, style, context.StyleSheet, context.InheritedPresentation, context.InheritedFillRule, context.InheritedTextStyle, context.InheritedStroke, namedColors, workCounter, cancellationToken);
        if (style.Fill is not null && resolved.Presentation.PaintServerFill is string fill)
        {
            workCounter.ConsumeText(fill.Length, cancellationToken);
            resolved = resolved with { Presentation = resolved.Presentation with { PaintServerFill = state.Strings.Copy(fill) } };
        }
        resolved = resolved with { Presentation = resolved.Presentation with { Markers = resolved.Presentation.Markers.Retain(state, workCounter, cancellationToken) } };
        string? filterId = null;
        if (style.Filter is string filter && filter.Trim() != "none")
        {
            if (!SvgLocalUrl.TryParseId(filter, out string id)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only local filter url references are implemented.");
            if (resolved.Presentation.IsDisplayed) filterId = state.Strings.Copy(id);
        }
        bool hasScope = !reader.IsEmptyElement || filterId is not null;
        string? maskReference = SvgLocalUrl.ReadReference(reader, "mask");
        if (maskReference is not null && !resolved.Presentation.IsDisplayed)
        {
            _ = SvgMaskSyntax.ParseMaskId(maskReference);
            maskReference = null;
        }
        string? deferredMask = null;
        if (maskReference is not null)
        {
            string maskId = SvgMaskSyntax.ParseMaskId(maskReference);
            if (!hasScope)
            {
                state.EmptyGroupMaskReferences.Add(new(state.Strings.Copy(maskId)));
                maskReference = null;
            }
            else if (!SvgMaskSyntax.IsDisabled(maskId, state))
            {
                // Every live mask, including uniform masks, applies after child
                // composition on the group's own isolation scope.
                deferredMask = state.Strings.Copy(maskId);
            }
        }
        bool hasMaskScope = hasScope && maskReference is not null && PushMaskScope(reader, state);
        SvgGroupKind kind = reader.LocalName == "switch" ? SvgGroupKind.Switch : SvgStructuralVocabulary.IsGroupContainer(reader.LocalName) ? SvgGroupKind.Group : SvgGroupKind.Link;
        SvgPaintOpacity sourceOpacity = style.GetOpacity();
        SvgRecordingScope opacityScope = context.RecordingScope.Value == 0 ? new(1) : context.RecordingScope;
        if (SvgLocalUrl.ReadReference(reader, "clip-path") is not null || resolved.Presentation.Transform != context.InheritedPresentation.Transform)
            opacityScope = state.NewRecordingScope();
        SvgRecordingScope contentScope = maskReference is null ? opacityScope : state.NewRecordingScope();
        string? clipId = SvgClipPathSyntax.ResolveClipId(reader, state);
        return new SvgParsedGroup(new SvgGroupScope(reader.Depth, kind, resolved.Presentation, resolved.TextStyle, resolved.Opacity != 255 || sourceOpacity.Value != 1, hasMaskScope, resolved.Stroke, resolved.FillRule) { HasFilter = filterId is not null, HasDeferredMask = deferredMask is not null, RecordingScope = contentScope, ClipRule = style.ClipRule ?? context.InheritedClipRule, ClipPathId = clipId is null ? null : state.Strings.Copy(clipId) }, deferredMask) { FilterId = filterId, LengthBases = context.LengthBases, SourceOpacity = sourceOpacity, RecordingScope = opacityScope };
    }

    /// <summary>Pushes the referenced mask content box for child coverage checks, reporting whether a scope was pushed.</summary>
    private static bool PushMaskScope(XmlReader reader, SvgDocumentState state)
    {
        string? reference = SvgLocalUrl.ReadReference(reader, "mask");
        if (reference is null) return false;
        if (state.MaskContents.TryGetValue(SvgMaskSyntax.ParseMaskId(reference), out _)) return false;
        if (!state.MaskContentBoxes.TryGetValue(SvgMaskSyntax.ParseMaskId(reference), out SvgClipBox scope)) return false;
        state.MaskScopes.Add(scope);
        return true;
    }
}

/// <summary>Couples a stored child context with deferred effects and source opacity for its isolated surface.</summary>
internal readonly record struct SvgParsedGroup(SvgGroupScope Scope, string? DeferredMask)
{
    internal string? FilterId { get; init; }
    internal SvgViewportLengthBases LengthBases { get; init; }
    internal SvgPaintOpacity SourceOpacity { get; init; }
    internal SvgRecordingScope RecordingScope { get; init; }
}

/// <summary>A document-owned mask ID whose empty target needs validation without constructing mask content.</summary>
internal readonly record struct SvgEmptyGroupMaskReference(string Id);

