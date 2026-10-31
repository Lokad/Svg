namespace Lokad.Svg.Rendering;

/// <summary>Keeps a recorded drawing separate from its optional native image destination.</summary>
/// <remarks>Immutable values own no storage. A missing destination means that recording
/// already belongs to the owning device. Parameter calculations retain double precision;
/// drawing narrows the complete recorded scene before native destination concatenation.
/// Callers retain cancellation and may share these values between independent draws.</remarks>
internal readonly record struct SvgDrawingMap(SvgAffineTransform Recording, SvgAffineTransform? Destination)
{
    internal SvgAffineTransform Parameter => Destination is {} destination ? destination.Compose(Recording) : Recording;
    internal SvgAffineTransform Device => PlaceRecorded(Recording);

    internal SvgAffineTransform PlaceRecorded(SvgAffineTransform recorded)
        => Destination is {} destination ? destination.ComposeNative(recorded) : recorded;

    internal SvgAffineTransform MapScene(SvgAffineTransform source)
        => PlaceRecorded(Recording.ComposeChecked(source));

    internal SvgDrawingMap Compose(SvgAffineTransform source)
        => new(Recording.ComposeChecked(source), Destination);
}
