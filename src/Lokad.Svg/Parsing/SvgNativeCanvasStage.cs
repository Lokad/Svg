using System.Runtime.CompilerServices;

namespace Lokad.Svg.Parsing;

/// <summary>Shares one native canvas boundary independently of viewport clipping and paint kind.</summary>
/// <remarks>The scene reserves and owns this immutable metadata before publication; paints, references
/// and contexts borrow it. It contains no output cache and permits concurrent immutable raster reads.
/// Mapping observes caller cancellation and validates the completed source-to-device map.</remarks>
internal sealed class SvgNativeCanvasStage
{
    // Includes the node header, reservation token and scene owner-list entry.
    internal static long RetainedStorageBytes => 64L + Unsafe.SizeOf<SvgCanvasDrawMap>();
    internal SvgNativeCanvasStage(SvgCanvasDrawMap map) { Map = map; }
    internal SvgCanvasDrawMap Map { get; }
}
