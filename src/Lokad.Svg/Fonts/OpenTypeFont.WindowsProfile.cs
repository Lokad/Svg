namespace Lokad.Svg.Fonts;

using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

/// <summary>Retains independent Windows smoothing and font-selected fitting policies without owning storage.</summary>
internal readonly record struct OpenTypeWindowsGlyphProfile(SvgGlyphLcdMode LcdMode, bool GridFit);

internal static partial class OpenTypeFont
{
    /// <summary>Selects the bounded Windows LCD profile from borrowed gasp recommendations and the actual device size.</summary>
    /// <remarks>Adapts the pinned Skia Windows scaler. Owns no storage and performs no native calls.
    /// Each visited range charges work and observes cancellation. Unknown versions use the native
    /// no-table fallback; malformed supported tables fail without reading outside their declared span.</remarks>
    internal static OpenTypeWindowsGlyphProfile GetWindowsGlyphProfile(ReadOnlyMemory<byte> bytes, OpenTypeFontMetrics metrics, double deviceSize, ParserWorkCounter work, CancellationToken token)
    {
        work.Consume(token);
        if (!double.IsFinite(deviceSize) || deviceSize <= 0 || deviceSize > 32767)
            throw new SvgException(SvgErrorCode.LimitExceeded, "The Windows font profile size exceeds its device bound.");
        if (metrics.Gasp is {} table)
        {
            RequireRange(bytes.Span, table.Offset, table.Length, "gasp table");
            RequireTableLength(table, 4, "gasp");
            ReadOnlySpan<byte> data = bytes.Span.Slice(table.Offset, table.Length);
            int version = ReadUInt16(data, 0);
            if (version is 0 or 1)
            {
                int count = ReadUInt16(data, 2);
                if (count > 1024) throw new SvgException(SvgErrorCode.LimitExceeded, "The gasp range count exceeds its supported bound.");
                RequireRange(data, 4, count * 4, "gasp ranges");
                float quantizedSize = MathF.Floor((float)deviceSize * 64 + .5f) / 64;
                int ppem = checked((int)MathF.Floor(quantizedSize + .5f));
                int previous = -1, selectedFlags = -1;
                for (int index = 0; index < count; index++)
                {
                    work.Consume(token);
                    int maximum = ReadUInt16(data, 4 + index * 4);
                    if (maximum <= previous) throw InvalidFont("The gasp ranges must have increasing ppem bounds.");
                    if (selectedFlags < 0 && ppem <= maximum) selectedFlags = ReadUInt16(data, 6 + index * 4);
                    previous = maximum;
                }
                if (version == 1 && selectedFlags >= 0)
                {
                    bool symmetric = (selectedFlags & 8) != 0;
                    // Native ClearType consults symmetric grid fitting only in
                    // a symmetric-smoothing range. Legacy ranges retain fitting.
                    return new(symmetric ? SvgGlyphLcdMode.Symmetric : SvgGlyphLcdMode.Natural, !symmetric || (selectedFlags & 4) != 0);
                }
            }
        }
        return new(deviceSize > 20 || !metrics.HasGlyphInstructions ? SvgGlyphLcdMode.Symmetric : SvgGlyphLcdMode.Natural, true);
    }
}
