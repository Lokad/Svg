namespace Lokad.Svg;

/// <summary>Defines independent, per-conversion resource limits.</summary>
public sealed class SvgRenderLimits
{
    /// <summary>Initializes the provisional limits documented by the project plan.</summary>
    public SvgRenderLimits(
        long maximumAccountedBytes,
        int maximumInputBytes,
        int maximumDimension,
        int maximumPixels,
        int maximumEncodedPngBytes,
        int maximumNodes,
        int maximumPathCommands,
        int maximumWorkUnits)
    {
        if (maximumAccountedBytes <= 0) throw new ArgumentOutOfRangeException(nameof(maximumAccountedBytes));
        if (maximumInputBytes <= 0) throw new ArgumentOutOfRangeException(nameof(maximumInputBytes));
        if (maximumDimension <= 0) throw new ArgumentOutOfRangeException(nameof(maximumDimension));
        if (maximumPixels <= 0) throw new ArgumentOutOfRangeException(nameof(maximumPixels));
        if (maximumEncodedPngBytes <= 0) throw new ArgumentOutOfRangeException(nameof(maximumEncodedPngBytes));
        if (maximumNodes <= 0) throw new ArgumentOutOfRangeException(nameof(maximumNodes));
        if (maximumPathCommands <= 0) throw new ArgumentOutOfRangeException(nameof(maximumPathCommands));
        if (maximumWorkUnits <= 0) throw new ArgumentOutOfRangeException(nameof(maximumWorkUnits));

        MaximumAccountedBytes = maximumAccountedBytes;
        MaximumInputBytes = maximumInputBytes;
        MaximumDimension = maximumDimension;
        MaximumPixels = maximumPixels;
        MaximumEncodedPngBytes = maximumEncodedPngBytes;
        MaximumNodes = maximumNodes;
        MaximumPathCommands = maximumPathCommands;
        MaximumWorkUnits = maximumWorkUnits;
    }

    /// <summary>Gets the provisional default limits.</summary>
    public static SvgRenderLimits Default { get; } = new(268_435_456, 16_777_216, 16_384, 16_777_216, 134_217_728, 100_000, 1_000_000, 100_000_000);

    public long MaximumAccountedBytes { get; }
    public int MaximumInputBytes { get; }
    public int MaximumDimension { get; }
    public int MaximumPixels { get; }
    public int MaximumEncodedPngBytes { get; }
    public int MaximumNodes { get; }
    public int MaximumPathCommands { get; }
    public int MaximumWorkUnits { get; }
}
