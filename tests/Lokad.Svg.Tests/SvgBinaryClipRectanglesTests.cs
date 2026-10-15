using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgBinaryClipRectanglesTests
{
    [Fact]
    public void Clipped_runs_preserve_full_bitmap_band_boundaries_without_overlapping()
    {
        byte[] alpha = [255, 255, 255, 0, 255, 255,
                        255, 255, 255, 0, 255, 255,
                        0, 255, 255, 0, 255, 255,
                        0, 255, 255, 255, 255, 255];
        byte[] pixels = new byte[alpha.Length * 4];
        for (int index = 0; index < alpha.Length; index++) pixels[index * 4 + 3] = alpha[index];
        byte[] unchanged = pixels.ToArray();
        SvgBinaryClipRectangles regions = new(pixels, 6, 4, new(0, 0, 6, 4), new(2, 0, 5, 4), new(1000), CancellationToken.None);
        List<SvgAnalyticClipBounds> actual = [];
        while (regions.MoveNext()) actual.Add(regions.Current);
        // Rows 0 and 2 agree inside the drawing area, but a change elsewhere in
        // the bitmap must end the region band before clipped coverage is built.
        Assert.Equal([new(2, 0, 3, 2), new(4, 0, 5, 2), new(2, 2, 3, 3), new(4, 2, 5, 3), new(2, 3, 5, 4)], actual);
        Assert.Equal(unchanged, pixels);
    }

    [Fact]
    public void Region_scans_observe_work_denial_and_cancellation_and_can_be_retried()
    {
        byte[] pixels = Enumerable.Repeat((byte)255, 6 * 4 * 4).ToArray();
        byte[] unchanged = pixels.ToArray();
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => Read(new(0), CancellationToken.None)).ErrorCode);
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => Read(new(10), CancellationToken.None)).ErrorCode);
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        Assert.Equal(cancellation.Token, Assert.ThrowsAny<OperationCanceledException>(() => Read(new(1000), cancellation.Token)).CancellationToken);
        Assert.Equal(new SvgAnalyticClipBounds(1, 0, 5, 4), Assert.Single(Read(new(1000), CancellationToken.None)));
        Assert.Equal(unchanged, pixels);

        List<SvgAnalyticClipBounds> Read(ParserWorkCounter work, CancellationToken token)
        {
            SvgBinaryClipRectangles regions = new(pixels, 6, 4, new(0, 0, 6, 4), new(1, 0, 5, 4), work, token);
            List<SvgAnalyticClipBounds> result = [];
            while (regions.MoveNext()) result.Add(regions.Current);
            return result;
        }
    }
}
