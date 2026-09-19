namespace Lokad.Svg.VisualDiff;

/// <summary>Reports exact, canonical RGBA8 comparison diagnostics.</summary>
public readonly record struct PixelComparison(
    bool DimensionsMatch,
    long ChangedPixelCount,
    byte MaximumChannelError,
    double MeanAbsoluteError,
    double RootMeanSquareError);

/// <summary>Compares canonical RGBA8 pixels without alignment or tolerance.</summary>
public static class PixelComparer
{
    public static PixelComparison Compare(RgbaImage reference, RgbaImage candidate)
    {
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(candidate);
        if (reference.Width != candidate.Width || reference.Height != candidate.Height)
        {
            return new PixelComparison(false, 0, 0, 0, 0);
        }

        long changed = 0;
        long sum = 0;
        double squaredSum = 0;
        byte maximum = 0;
        for (int pixel = 0; pixel < reference.Width * reference.Height; pixel++)
        {
            int offset = pixel * 4;
            bool pixelChanged = false;
            bool transparentReference = reference.Pixels[offset + 3] == 0;
            bool transparentCandidate = candidate.Pixels[offset + 3] == 0;
            for (int channel = 0; channel < 4; channel++)
            {
                int referenceValue = channel < 3 && transparentReference ? 0 : reference.Pixels[offset + channel];
                int candidateValue = channel < 3 && transparentCandidate ? 0 : candidate.Pixels[offset + channel];
                int delta = Math.Abs(referenceValue - candidateValue);
                pixelChanged |= delta != 0;
                sum += delta;
                squaredSum += (double)delta * delta;
                maximum = Math.Max(maximum, (byte)delta);
            }

            if (pixelChanged) changed++;
        }

        long channels = (long)reference.Width * reference.Height * 4;
        return new PixelComparison(true, changed, maximum, (double)sum / channels, Math.Sqrt(squaredSum / channels));
    }
}
