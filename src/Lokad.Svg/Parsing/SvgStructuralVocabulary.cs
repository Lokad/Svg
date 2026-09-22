namespace Lokad.Svg.Parsing;

/// <summary>Shares the admitted structural container names between font-request discovery and document lowering.</summary>
internal static class SvgStructuralVocabulary
{
    /// <summary>Identifies the group, link and switch containers that propagate text eligibility and group style scope.</summary>
    internal static bool IsGroupOrLinkContainer(string localName) => localName is "g" or "a" or "switch";

    /// <summary>Identifies the group container that establishes group style scope.</summary>
    internal static bool IsGroupContainer(string localName) => localName == "g";

    /// <summary>Identifies the metadata containers whose subtrees never render.</summary>
    internal static bool IsInertContainer(string localName) => localName is "defs" or "title" or "desc" or "metadata";

    /// <summary>Identifies the definition leaves that count as styleable content.</summary>
    internal static bool IsDefinitionLeaf(string localName) => localName is "rect" or "line";

    /// <summary>Identifies style, script and animation elements that unconditionally invalidate the operation.</summary>
    internal static bool IsStyleScriptOrAnimationElement(string localName) => localName is "style" or "script" or "animate" or "animateMotion" or "animateTransform" or "set";

    /// <summary>Identifies the definition container that admits local geometry references.</summary>
    internal static bool IsDefinitionsContainer(string localName) => localName == "defs";

}

/// <summary>Tracks one open switch container and whether its first element child was consumed.</summary>
internal readonly record struct SwitchScope(int Depth, bool FirstChildConsumed);
