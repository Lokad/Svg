namespace Lokad.Svg;

/// <summary>Defines independent, per-conversion resource limits.</summary>
public sealed class SvgRenderLimits
{
    /// <summary>Initializes per-conversion resource limits with default XML depth and attribute bounds.</summary>
    public SvgRenderLimits(
        long maximumAccountedBytes,
        int maximumInputBytes,
        int maximumDimension,
        int maximumPixels,
        int maximumEncodedPngBytes,
        int maximumNodes,
        int maximumPathCommands)
        : this(maximumAccountedBytes, maximumInputBytes, maximumDimension, maximumPixels, maximumEncodedPngBytes, maximumNodes, maximumPathCommands, 128, 256)
    {
    }

    /// <summary>Initializes per-conversion resource limits with an explicit XML depth and the default attribute bound.</summary>
    public SvgRenderLimits(
        long maximumAccountedBytes,
        int maximumInputBytes,
        int maximumDimension,
        int maximumPixels,
        int maximumEncodedPngBytes,
        int maximumNodes,
        int maximumPathCommands,
        int maximumXmlDepth)
        : this(maximumAccountedBytes, maximumInputBytes, maximumDimension, maximumPixels, maximumEncodedPngBytes, maximumNodes, maximumPathCommands, maximumXmlDepth, 256)
    {
    }

    /// <summary>Initializes per-conversion resource limits, including XML nesting and per-element attributes.</summary>
    public SvgRenderLimits(
        long maximumAccountedBytes,
        int maximumInputBytes,
        int maximumDimension,
        int maximumPixels,
        int maximumEncodedPngBytes,
        int maximumNodes,
        int maximumPathCommands,
        int maximumXmlDepth,
        int maximumAttributesPerElement)
    {
        if (maximumAccountedBytes <= 0) throw new ArgumentOutOfRangeException(nameof(maximumAccountedBytes));
        if (maximumInputBytes <= 0) throw new ArgumentOutOfRangeException(nameof(maximumInputBytes));
        if (maximumDimension <= 0) throw new ArgumentOutOfRangeException(nameof(maximumDimension));
        if (maximumPixels <= 0) throw new ArgumentOutOfRangeException(nameof(maximumPixels));
        if (maximumEncodedPngBytes <= 0) throw new ArgumentOutOfRangeException(nameof(maximumEncodedPngBytes));
        if (maximumNodes <= 0) throw new ArgumentOutOfRangeException(nameof(maximumNodes));
        if (maximumPathCommands <= 0) throw new ArgumentOutOfRangeException(nameof(maximumPathCommands));
        if (maximumXmlDepth <= 0) throw new ArgumentOutOfRangeException(nameof(maximumXmlDepth));
        if (maximumAttributesPerElement <= 0) throw new ArgumentOutOfRangeException(nameof(maximumAttributesPerElement));

        MaximumAccountedBytes = maximumAccountedBytes;
        MaximumInputBytes = maximumInputBytes;
        MaximumDimension = maximumDimension;
        MaximumPixels = maximumPixels;
        MaximumEncodedPngBytes = maximumEncodedPngBytes;
        MaximumNodes = maximumNodes;
        MaximumPathCommands = maximumPathCommands;
        MaximumXmlDepth = maximumXmlDepth;
        MaximumAttributesPerElement = maximumAttributesPerElement;
    }

    /// <summary>Gets the default per-conversion resource limits.</summary>
    public static SvgRenderLimits Default { get; } = new(268_435_456, 16_777_216, 16_384, 16_777_216, 134_217_728, 100_000, 1_000_000, 128, 256);

    public long MaximumAccountedBytes { get; }
    public int MaximumInputBytes { get; }
    public int MaximumDimension { get; }
    public int MaximumPixels { get; }
    public int MaximumEncodedPngBytes { get; }
    public int MaximumNodes { get; }
    public int MaximumPathCommands { get; }
    public int MaximumXmlDepth { get; }
    public int MaximumAttributesPerElement { get; }
}
