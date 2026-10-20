using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgProbeScopeTests
{
    [Fact]
    public void Nested_one_shot_probes_restore_outer_callback_only_once()
    {
        int outerCalls = 0, innerCalls = 0;
        using IDisposable outer = GlyphCoverageProbe.Arm(() => outerCalls++);
        IDisposable inner = GlyphCoverageProbe.Arm(() => innerCalls++);
        GlyphCoverageProbe.Started();
        GlyphCoverageProbe.Started();
        Assert.Equal(1, innerCalls);
        Assert.Equal(0, outerCalls);

        inner.Dispose();
        GlyphCoverageProbe.Started();
        inner.Dispose();
        GlyphCoverageProbe.Started();
        Assert.Equal(1, outerCalls);
    }

    [Fact]
    public async Task Inherited_probe_context_survives_parent_scope_disposal()
    {
        int calls = 0;
        var ready = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var proceed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using IDisposable scope = GlyphCoverageProbe.Arm(() => Interlocked.Increment(ref calls));
        Task child = Task.Run(async () =>
        {
            ready.SetResult(true);
            await proceed.Task;
            GlyphCoverageProbe.Started();
            GlyphCoverageProbe.Started();
        });
        await ready.Task;
        try
        {
            scope.Dispose();
            GlyphCoverageProbe.Started();
            Assert.Equal(0, calls);
        }
        finally { proceed.SetResult(true); }
        await child;
        GlyphCoverageProbe.Started();
        Assert.Equal(1, calls);
    }
}
