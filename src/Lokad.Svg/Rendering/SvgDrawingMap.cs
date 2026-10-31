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

    /// <summary>Moves destination placement into an admitted bitmap without folding it into the recorded scene.</summary>
    /// <remarks>Pure values own no storage. The caller retains the bitmap and checks cancellation;
    /// integer origin translation follows native float destination concatenation.</remarks>
    internal SvgDrawingMap InBitmap(SvgCpuBitmapOrigin origin)
        => new(Recording, SvgAffineTransform.FromTranslation(-(double)origin.X, -(double)origin.Y)
            .ComposeNative(Destination ?? SvgAffineTransform.Identity));
}
