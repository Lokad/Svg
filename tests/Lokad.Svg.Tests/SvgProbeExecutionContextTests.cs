using Lokad.Svg.Fonts;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgProbeExecutionContextTests
{
    public enum ProbeKind { GlyphCoverage, PolygonFan, GlyphOutline }

    [Theory]
    [InlineData(ProbeKind.GlyphCoverage)]
    [InlineData(ProbeKind.PolygonFan)]
    [InlineData(ProbeKind.GlyphOutline)]
    public void Nested_scopes_restore_callbacks_and_keep_one_shot_behavior(ProbeKind kind)
    {
        int outer = 0, inner = 0;
        using (Arm(kind, () => outer++))
        {
            IDisposable nested = Arm(kind, () => inner++);
            Trigger(kind); Trigger(kind);
            Assert.Equal(kind == ProbeKind.PolygonFan ? 2 : 1, inner);
            Assert.Equal(0, outer);
            nested.Dispose(); nested.Dispose();
            Trigger(kind); Trigger(kind);
            Assert.Equal(kind == ProbeKind.PolygonFan ? 2 : 1, outer);
        }
        int completed = outer;
        Trigger(kind);
        Assert.Equal(completed, outer);
    }

    [Theory]
    [InlineData(ProbeKind.GlyphCoverage)]
    [InlineData(ProbeKind.PolygonFan)]
    [InlineData(ProbeKind.GlyphOutline)]
    public async Task Concurrent_scopes_remain_isolated_across_awaits(ProbeKind kind)
    {
        int[] calls = new int[2];
        int ready = 0;
        TaskCompletionSource start = new(TaskCreationOptions.RunContinuationsAsynchronously);
        await Task.WhenAll(Enumerable.Range(0, 2).Select(index => Task.Run(async () =>
        {
            using IDisposable scope = Arm(kind, () => calls[index]++);
            if (Interlocked.Increment(ref ready) == 2) start.SetResult();
            await start.Task;
            Trigger(kind);
            await Task.Yield();
            Trigger(kind);
        })));
        int expected = kind == ProbeKind.PolygonFan ? 2 : 1;
        Assert.Equal(new[] { expected, expected }, calls);
    }

    [Theory]
    [InlineData(ProbeKind.GlyphCoverage)]
    [InlineData(ProbeKind.PolygonFan)]
    [InlineData(ProbeKind.GlyphOutline)]
    public async Task Inherited_child_callback_survives_parent_scope_disposal(ProbeKind kind)
    {
        int calls = 0;
        TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Task child;
        using (Arm(kind, () => Interlocked.Increment(ref calls)))
        {
            child = Task.Run(async () =>
            {
                ready.SetResult();
                await release.Task;
                Trigger(kind);
            });
            await ready.Task;
        }
        Trigger(kind);
        Assert.Equal(0, calls);
        release.SetResult();
        await child;
        Assert.Equal(1, calls);
        Trigger(kind);
        Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData(ProbeKind.GlyphCoverage)]
    [InlineData(ProbeKind.PolygonFan)]
    [InlineData(ProbeKind.GlyphOutline)]
    public async Task Suppressed_flow_does_not_take_the_parent_callback(ProbeKind kind)
    {
        int calls = 0;
        using IDisposable scope = Arm(kind, () => calls++);
        Task child;
        using (ExecutionContext.SuppressFlow()) child = Task.Run(() => Trigger(kind));
        await child;
        Assert.Equal(0, calls);
        Trigger(kind);
        Assert.Equal(1, calls);
    }

    private static IDisposable Arm(ProbeKind kind, Action callback) => kind switch
    {
        ProbeKind.GlyphCoverage => GlyphCoverageProbe.Arm(callback),
        ProbeKind.PolygonFan => SvgPolygonCoverageProbe.Arm(callback),
        ProbeKind.GlyphOutline => GlyphOutlineProbe.Arm(callback),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private static void Trigger(ProbeKind kind)
    {
        switch (kind)
        {
            case ProbeKind.GlyphCoverage: GlyphCoverageProbe.Started(); break;
            case ProbeKind.PolygonFan: SvgPolygonCoverageProbe.FanEvaluated(); break;
            case ProbeKind.GlyphOutline: GlyphOutlineProbe.SegmentTransformed(); break;
            default: throw new ArgumentOutOfRangeException(nameof(kind));
        }
    }
}
