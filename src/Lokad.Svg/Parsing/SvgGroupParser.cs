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
        SvgInheritedContainer resolved = SvgContainerInheritance.Resolve(reader, inline, style, context.StyleSheet, context.InheritedPresentation, context.InheritedTextStyle, root.Opacity != 255 || state.HasOpacityScope, namedColors);
        string? maskReference = reader.GetAttribute("mask");
        byte? mask = null;
        string? deferredMask = null;
        if (maskReference is not null)
        {
            string maskId = SvgMaskSyntax.ParseMaskId(maskReference);
            if (state.Masks.TryGetValue(maskId, out byte knownMask)) mask = knownMask;
            else if (reader.IsEmptyElement) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only defined local mask references are implemented.");
            else deferredMask = state.Strings.Copy(maskId);
        }
        if (mask is not null && SvgMaskSyntax.HasCustomRegion(reader, state)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Custom mask regions on masked groups are not implemented.");
        byte combined = mask is null ? resolved.Opacity : SvgReferenceResolver.MultiplyAlpha(resolved.Opacity, mask.Value);
        bool hasMaskScope = reader.IsEmptyElement ? false : PushMaskScope(reader, state);
        SvgGroupKind kind = reader.LocalName == "switch" ? SvgGroupKind.Switch : SvgStructuralVocabulary.IsGroupContainer(reader.LocalName) ? SvgGroupKind.Group : SvgGroupKind.Link;
        return new SvgParsedGroup(new SvgGroupScope(reader.Depth, kind, resolved.Presentation, resolved.TextStyle, combined != 255, hasMaskScope), combined, deferredMask);
    }

    /// <summary>Pushes the referenced mask content box for child coverage checks, reporting whether a scope was pushed.</summary>
    private static bool PushMaskScope(XmlReader reader, SvgDocumentState state)
    {
        string? reference = reader.GetAttribute("mask");
        if (reference is null) return false;
        if (!state.MaskContentBoxes.TryGetValue(SvgMaskSyntax.ParseMaskId(reference), out SvgClipBox scope)) return false;
        state.MaskScopes.Add(scope);
        return true;
    }
}

/// <summary>Couples a stored child context with the opacity required to enter its isolated surface.</summary>
internal readonly record struct SvgParsedGroup(SvgGroupScope Scope, byte Opacity, string? DeferredMask);
