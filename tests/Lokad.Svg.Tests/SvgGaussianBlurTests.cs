using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgGaussianBlurTests
{
    [Fact]
    public void Every_denied_work_and_memory_boundary_releases_owners_and_preserves_source_before_retry()
    {
        byte[] source = Source();
        byte[] before = (byte[])source.Clone();
        byte[] expected = new byte[source.Length];
        RenderBudget accepted = new(1_000_000);
        long work = SvgGaussianBlur.Apply(source, expected, 13, 9, new(3, 4), accepted, long.MaxValue, CancellationToken.None);
        long peak = accepted.Snapshot().PeakBytes;
        Assert.True(peak > 0);
        Assert.Equal(0, accepted.Snapshot().CurrentBytes);
        for (long available = 0; available < work; available++)
        {
            RenderBudget budget = new(1_000_000);
            byte[] target = new byte[source.Length];
            SvgException denied = Assert.Throws<SvgException>(() => SvgGaussianBlur.Apply(source, target, 13, 9, new(3, 4), budget, available, CancellationToken.None));
            Assert.Equal(SvgErrorCode.LimitExceeded, denied.ErrorCode);
            Assert.Equal(0, budget.Snapshot().CurrentBytes);
            Assert.Equal(0, budget.Snapshot().PeakBytes);
            Assert.All(target, value => Assert.Equal(0, value));
        }
        for (long limit = 1; limit < peak; limit++)
        {
            RenderBudget budget = new(limit);
            byte[] target = new byte[source.Length];
            SvgException denied = Assert.Throws<SvgException>(() => SvgGaussianBlur.Apply(source, target, 13, 9, new(3, 4), budget, work, CancellationToken.None));
            Assert.Equal(SvgErrorCode.LimitExceeded, denied.ErrorCode);
            Assert.Equal(0, budget.Snapshot().CurrentBytes);
            Assert.All(target, value => Assert.Equal(0, value));
        }
        byte[] retry = new byte[source.Length];
        Assert.Equal(work, SvgGaussianBlur.Apply(source, retry, 13, 9, new(3, 4), accepted, work, CancellationToken.None));
        Assert.Equal(expected, retry);
        Assert.Equal(before, source);
        Assert.Equal(0, accepted.Snapshot().CurrentBytes);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(10)]
    [InlineData(22)]
    public void Cancellation_during_either_axis_releases_temporary_owners_and_retries_identically(int cancelAfter)
    {
        byte[] source = Source(), before = (byte[])source.Clone(), target = new byte[source.Length];
        RenderBudget budget = new(1_000_000);
        using CancellationTokenSource cancellation = new();
        int observed = 0;
        OperationCanceledException stopped = Assert.Throws<OperationCanceledException>(() => SvgGaussianBlur.Apply(source, target, 13, 9, new(3, 4), budget, long.MaxValue, cancellation.Token, line =>
        {
            observed = line;
            Assert.True(budget.Snapshot().CurrentBytes > 0);
            if (line == cancelAfter) cancellation.Cancel();
        }));
        Assert.Equal(cancelAfter, observed);
        Assert.Equal(cancellation.Token, stopped.CancellationToken);
        Assert.Equal(before, source);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        byte[] expected = new byte[source.Length];
        SvgGaussianBlur.Apply(source, expected, 13, 9, new(3, 4), budget, long.MaxValue, CancellationToken.None);
        SvgGaussianBlur.Apply(source, target, 13, 9, new(3, 4), budget, long.MaxValue, CancellationToken.None);
        Assert.Equal(expected, target);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Theory]
    [InlineData(0f, 0f)]
    [InlineData(0.03f, 0.01f)]
    public void Identity_blur_copies_all_premultiplied_channels_without_temporary_owners(float horizontal, float vertical)
    {
        byte[] source = Source(), target = new byte[source.Length];
        RenderBudget budget = new(1);
        Assert.Equal(source.Length, SvgGaussianBlur.Apply(source, target, 13, 9, new(horizontal, vertical), budget, source.Length, CancellationToken.None));
        Assert.Equal(source, target);
        Assert.Equal(0, budget.Snapshot().PeakBytes);
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(3f)]
    public void Already_cancelled_identity_and_active_blurs_preserve_both_frames_without_allocating(float sigma)
    {
        byte[] source = Source(), before = (byte[])source.Clone(), target = new byte[source.Length];
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        RenderBudget budget = new(1_000_000);
        OperationCanceledException stopped = Assert.Throws<OperationCanceledException>(() => SvgGaussianBlur.Apply(source, target, 13, 9, new(sigma, sigma), budget, long.MaxValue, cancellation.Token));
        Assert.Equal(cancellation.Token, stopped.CancellationToken);
        Assert.Equal(before, source);
        Assert.All(target, value => Assert.Equal(0, value));
        Assert.Equal(0, budget.Snapshot().PeakBytes);
    }

    [Theory]
    [InlineData(0.5f)]
    [InlineData(1.999f)]
    [InlineData(135f)]
    [InlineData(float.MaxValue)]
    public void Pending_kernel_and_rescaling_admissions_reject_before_allocating(float sigma)
    {
        byte[] source = Source(), target = new byte[source.Length];
        RenderBudget budget = new(1_000_000);
        SvgException rejected = Assert.Throws<SvgException>(() => SvgGaussianBlur.Apply(source, target, 13, 9, new(sigma, 3), budget, long.MaxValue, CancellationToken.None));
        Assert.Equal(SvgErrorCode.UnsupportedFeature, rejected.ErrorCode);
        Assert.Equal(0, budget.Snapshot().PeakBytes);
        Assert.All(target, value => Assert.Equal(0, value));
    }

    [Fact]
    public void Invalid_deviations_frames_and_overlapping_storage_reject_before_mutation()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SvgGaussianDeviation(-1, 3));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SvgGaussianDeviation(3, float.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SvgGaussianDeviation(float.PositiveInfinity, 3));
        byte[] source = Source(), before = (byte[])source.Clone(), target = new byte[source.Length - 1];
        RenderBudget budget = new(1_000_000);
        Assert.Throws<ArgumentException>(() => SvgGaussianBlur.Apply(source, target, 13, 9, new(3, 4), budget, long.MaxValue, CancellationToken.None));
        Assert.Throws<ArgumentException>(() => SvgGaussianBlur.Apply(source, source, 13, 9, new(3, 4), budget, long.MaxValue, CancellationToken.None));
        SvgException huge = Assert.Throws<SvgException>(() => SvgGaussianBlur.Apply(source, target, int.MaxValue, int.MaxValue, new(3, 4), budget, long.MaxValue, CancellationToken.None));
        Assert.Equal(SvgErrorCode.LimitExceeded, huge.ErrorCode);
        Assert.Equal(before, source);
        Assert.Equal(0, budget.Snapshot().PeakBytes);
    }

    private static byte[] Source()
    {
        byte[] pixels = new byte[13 * 9 * 4];
        for (int y = 1; y < 7; y++)
            for (int x = 2; x < 11; x++)
            {
                int offset = (y * 13 + x) * 4;
                pixels[offset] = 100;
                pixels[offset + 1] = 75;
                pixels[offset + 2] = 30;
                pixels[offset + 3] = 128;
            }
        return pixels;
    }
}
