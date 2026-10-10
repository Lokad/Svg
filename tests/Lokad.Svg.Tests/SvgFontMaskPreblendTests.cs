using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgFontMaskPreblendTests
{
    [Fact]
    public void Matches_pinned_native_samples_across_every_canonical_luminance()
    {
        byte[] table = new byte[256];
        int tested = 0;
        foreach (string line in File.ReadLines(Path.Combine(AppContext.BaseDirectory, "TestData", "font-preblend-linux-native.csv")))
        {
            if (line.StartsWith('#')) continue;
            byte[] row = line.Split(',').Select(byte.Parse).ToArray();
            SvgFontMaskPreblend.BuildLinuxGrayTable(table, new(row[0], row[0], row[0], 255), new(long.MaxValue), CancellationToken.None);
            Assert.Equal(row[2], table[row[1]]);
            tested++;
        }
        Assert.Equal(88, tested);
    }

    [Fact]
    public void Keeps_paint_alpha_separate_and_uses_canonical_color_luminance()
    {
        byte[] opaque = new byte[256], transparent = new byte[256], gray = new byte[256];
        SvgFontMaskPreblend.BuildLinuxGrayTable(opaque, new(128, 64, 32, 255), new(long.MaxValue), CancellationToken.None);
        SvgFontMaskPreblend.BuildLinuxGrayTable(transparent, new(128, 64, 32, 0), new(long.MaxValue), CancellationToken.None);
        SvgFontMaskPreblend.BuildLinuxGrayTable(gray, new(64, 64, 64, 255), new(long.MaxValue), CancellationToken.None);

        Assert.Equal(opaque, transparent);
        Assert.Equal(gray, opaque);
        Assert.Equal(0, opaque[0]);
        Assert.Equal(255, opaque[255]);
    }

    [Fact]
    public void Rejects_an_incomplete_caller_table()
    {
        Assert.Throws<ArgumentException>(() => SvgFontMaskPreblend.BuildLinuxGrayTable(new byte[255], new(0, 0, 0, 255), new(long.MaxValue), CancellationToken.None));
    }

    [Fact]
    public void Observes_work_expiry_and_cancellation_before_writing_a_table_entry()
    {
        byte[] table = Enumerable.Repeat((byte)0xCC, 256).ToArray();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => SvgFontMaskPreblend.BuildLinuxGrayTable(table, new(0, 0, 0, 255), new(long.MaxValue), cancellation.Token));
        Assert.All(table, value => Assert.Equal(0xCC, value));
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgFontMaskPreblend.BuildLinuxGrayTable(table, new(0, 0, 0, 255), new(0), CancellationToken.None)).ErrorCode);
        Assert.All(table, value => Assert.Equal(0xCC, value));
    }
}
