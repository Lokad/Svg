namespace Lokad.Svg.Parsing;

internal enum SvgClipDefinitionSourceKind { None, Geometry, Use, Text }

/// <summary>Borrows the existing retained text and its resource-local inherited presentation.</summary>
internal readonly record struct SvgClipTextSource(SvgRetainedTextSource Source, SvgInheritedContainer Inherited,
    SvgViewportLengthBases LengthBases, string? ClipId, bool Contributes);

/// <summary>Borrows ordered source slots until target-specific clip picture preparation finishes.</summary>
internal readonly record struct SvgClipSourceRange(string Id, int First, int Count);

/// <summary>Owns one direct clip path or borrows one deferred local reference.</summary>
/// <remarks>The kind guards the inactive value. Document staging disposes an unconsumed
/// geometry path; resolution clears its slot before transferring that owner to the union.
/// References borrow document-owned strings and style data. All access is conversion-local.</remarks>
internal readonly struct SvgClipDefinitionSource
{
    private readonly SvgClipContent _geometry;
    private readonly SvgResourceUse _use;
    private readonly SvgClipTextSource _text;

    internal SvgClipDefinitionSource(SvgClipContent geometry)
    {
        _geometry = geometry; _use = default; _text = default; Kind = SvgClipDefinitionSourceKind.Geometry;
    }

    internal SvgClipDefinitionSource(SvgResourceUse use)
    {
        _geometry = default; _use = use; _text = default; Kind = SvgClipDefinitionSourceKind.Use;
    }

    internal SvgClipDefinitionSource(SvgClipTextSource text)
    {
        _geometry = default; _use = default; _text = text; Kind = SvgClipDefinitionSourceKind.Text;
    }

    internal SvgClipDefinitionSourceKind Kind { get; }
    internal SvgClipContent Geometry => Kind == SvgClipDefinitionSourceKind.Geometry
        ? _geometry : throw new InvalidOperationException("The clip source is not direct geometry.");
    internal SvgResourceUse Use => Kind == SvgClipDefinitionSourceKind.Use
        ? _use : throw new InvalidOperationException("The clip source is not a local reference.");
    internal SvgClipTextSource Text => Kind == SvgClipDefinitionSourceKind.Text
        ? _text : throw new InvalidOperationException("The clip source is not retained text.");
}

/// <summary>Borrows one definition's ordered sources until all forward geometry is available.</summary>
internal readonly record struct SvgDeferredClipDefinition(string Id, int FirstSource, int SourceCount, bool IsObjectBoundingBox, string? ResourceClipId);

/// <summary>Borrows one clip definition id during dependency ordering.</summary>
internal readonly record struct SvgClipDependency(string Id);
