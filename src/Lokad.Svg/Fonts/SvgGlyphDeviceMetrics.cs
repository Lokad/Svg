namespace Lokad.Svg.Fonts;

/// <summary>Selects the existing platform scaler's horizontal metric convention.</summary>
internal enum SvgGlyphMetricTarget { Windows, Linux }

/// <summary>Maps bounded font advances and SVG fragments through the output grid without rounding authored origins.</summary>
internal readonly record struct SvgGlyphDeviceMetrics(double Scale, SvgGlyphMetricTarget Target, bool IsTextPath)
{
    internal double GetAdvance(ushort advance, ushort unitsPerEm, double fontSize)
        => GetAdvance(advance, 0, unitsPerEm, fontSize);

    internal double GetAdvance(ushort advance, int pairAdjustment, ushort unitsPerEm, double fontSize)
    {
        float size = (float)(fontSize * Scale);
        if (!double.IsFinite(fontSize) || fontSize <= 0 || !float.IsFinite(size) || size < 0 || unitsPerEm == 0 || !double.IsFinite(Scale) || Scale <= 0)
            throw new SvgException(SvgErrorCode.LimitExceeded, "The text metrics exceed the device range.");
        double deviceAdvance;
        long adjustedAdvance = (long)advance + pairAdjustment;
        if (Target == SvgGlyphMetricTarget.Linux)
        {
            // FreeType retains its size and font-unit scale as 26.6 / 16.16,
            // then rounds the linear advance with FT_MulDiv(..., 64).
            double size26 = Math.Truncate(size * 64f);
            double scale16 = Math.Floor(size26 * 65536 / unitsPerEm + .5);
            double scaledAdvance = adjustedAdvance * scale16 / 64;
            deviceAdvance = Math.CopySign(Math.Floor(Math.Abs(scaledAdvance) + .5), scaledAdvance) / 65536;
        }
        else if (Target == SvgGlyphMetricTarget.Windows)
        {
            // DirectWrite design metrics use scalar multiplication before division.
            float width = size * adjustedAdvance / unitsPerEm;
            deviceAdvance = Math.Truncate(width * 65536f) / 65536;
        }
        else throw new ArgumentOutOfRangeException(nameof(Target));
        if (!double.IsFinite(deviceAdvance) || deviceAdvance * 65536 > int.MaxValue || deviceAdvance * 65536 < int.MinValue)
            throw new SvgException(SvgErrorCode.LimitExceeded, "The text advance exceeds the device range.");
        return (IsTextPath ? Math.Ceiling(deviceAdvance * 64) / 64 : deviceAdvance) / Scale;
    }

    internal double RoundOffset(double offset) => MathF.Floor((float)(offset * Scale) * 64) / (Scale * 64);

    internal double RoundWidth(double width) => MathF.Ceiling((float)(width * Scale) * 64) / (Scale * 64);
}
