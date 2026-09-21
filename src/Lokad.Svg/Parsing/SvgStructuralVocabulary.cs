namespace Lokad.Svg.Parsing;

/// <summary>Shares the admitted structural container names between font-request discovery and document lowering.</summary>
internal static class SvgStructuralVocabulary
{
    /// <summary>Identifies the group and link containers that propagate text eligibility and group style scope.</summary>
    internal static bool IsGroupOrLinkContainer(string localName) => localName is "g" or "a";

    /// <summary>Identifies the group container that establishes group style scope.</summary>
    internal static bool IsGroupContainer(string localName) => localName == "g";

    /// <summary>Identifies the metadata containers whose subtrees never render.</summary>
    internal static bool IsInertContainer(string localName) => localName is "defs" or "title" or "desc" or "metadata";

    /// <summary>Identifies the definition container that admits local geometry references.</summary>
    internal static bool IsDefinitionsContainer(string localName) => localName == "defs";
}
