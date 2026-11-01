namespace Lokad.Svg.Parsing;

internal enum SvgClipDefinitionSourceKind { None, Geometry, Use }

/// <summary>Owns one direct clip path or borrows one deferred local reference.</summary>
/// <remarks>The kind guards the inactive value. Document staging disposes an unconsumed
/// geometry path; resolution clears its slot before transferring that owner to the union.
/// References borrow document-owned strings and style data. All access is conversion-local.</remarks>
internal readonly struct SvgClipDefinitionSource
{
    private readonly SvgClipPolygon _geometry;
    private readonly SvgResourceUse _use;

    internal SvgClipDefinitionSource(SvgClipPolygon geometry)
    {
        _geometry = geometry; _use = default; Kind = SvgClipDefinitionSourceKind.Geometry;
    }

    internal SvgClipDefinitionSource(SvgResourceUse use)
    {
        _geometry = default; _use = use; Kind = SvgClipDefinitionSourceKind.Use;
    }

    internal SvgClipDefinitionSourceKind Kind { get; }
    internal SvgClipPolygon Geometry => Kind == SvgClipDefinitionSourceKind.Geometry
        ? _geometry : throw new InvalidOperationException("The clip source is not direct geometry.");
    internal SvgResourceUse Use => Kind == SvgClipDefinitionSourceKind.Use
        ? _use : throw new InvalidOperationException("The clip source is not a local reference.");
}

/// <summary>Borrows one definition's ordered sources until all forward geometry is available.</summary>
internal readonly record struct SvgDeferredClipDefinition(string Id, int FirstSource, int SourceCount);
