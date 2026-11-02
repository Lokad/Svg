namespace Lokad.Svg.Parsing;

/// <summary>Shares the admitted structural container names between font-request discovery and document lowering.</summary>
internal static class SvgStructuralVocabulary
{
    /// <summary>Identifies the group, link and switch containers that propagate text eligibility and group style scope.</summary>
    internal static bool IsGroupOrLinkContainer(string localName) => localName is "g" or "a" or "switch";

    /// <summary>Identifies the group container that establishes group style scope.</summary>
    internal static bool IsGroupContainer(string localName) => localName == "g";

    /// <summary>Identifies the metadata containers whose subtrees never render.</summary>
    internal static bool IsInertContainer(string localName) => localName == "defs" || IsDescriptiveElement(localName);

    /// <summary>Identifies descriptive subtrees that do not paint or define reference targets at any admitted location.</summary>
    internal static bool IsDescriptiveElement(string localName) => localName is "title" or "desc" or "metadata";

    /// <summary>Identifies a non-rendering SVG script by namespace identity, independent of its XML prefix.</summary>
    internal static bool IsScriptElement(string namespaceUri, string localName)
        => namespaceUri == "http://www.w3.org/2000/svg" && localName == "script";

    /// <summary>Identifies foreign editor/test descriptions eligible for inert traversal.</summary>
    /// <remarks>Callers still reject SVG descendants; unknown nonempty foreign wrappers remain unsupported.</remarks>
    internal static bool IsForeignMetadata(string namespaceUri, string localName)
        => namespaceUri != "http://www.w3.org/2000/svg" && (localName == "namedview"
            || namespaceUri == "http://www.w3.org/2000/02/svg/testsuite/description/" && localName == "SVGTestCase");

    /// <summary>Identifies the definition leaves that count as styleable content.</summary>
    internal static bool IsDefinitionLeaf(string localName) => localName is "rect" or "line" or "polygon" or "polyline" or "path" or "circle" or "ellipse" or "image";

    /// <summary>Identifies style and animation elements outside inert script bodies that require explicit admission.</summary>
    internal static bool IsStyleOrAnimationElement(string localName) => localName is "style" or "animate" or "animateMotion" or "animateTransform" or "set";

    /// <summary>Identifies the definition container that admits local geometry references.</summary>
    internal static bool IsDefinitionsContainer(string localName) => localName == "defs";

    /// <summary>Shares pattern declaration locations between selection, font discovery and lowering.</summary>
    /// <remarks>Callers supply an eligible rendered-group child, excluding resource/symbol content and skipped branches.</remarks>
    internal static bool IsPatternDefinitionLocation(bool rootChild, bool definitionsChild, bool groupedChild)
        => rootChild || definitionsChild || groupedChild;

    /// <summary>Shares non-rendering symbol placement between lowering and font discovery.
    /// An open symbol admits no nested symbol; otherwise direct defs children and eligible root/group children are definitions.</summary>
    internal static bool IsSymbolDefinitionLocation(bool directChild, bool eligibleGroupChild, int definitionsDepth, int symbolDepth, int elementDepth)
        => symbolDepth < 0 && (definitionsDepth >= 0 ? elementDepth == definitionsDepth + 1 : directChild || eligibleGroupChild);

}

/// <summary>Tracks one open switch container and whether its first element child was consumed.</summary>
internal readonly record struct SwitchScope(int Depth, bool FirstChildConsumed);
