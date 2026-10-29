using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgClipUnionTests
{
    [Fact]
    public void Cubic_union_preserves_analytic_crossing_and_coincident_subintervals()
    {
        // y = x^3 and y = (3-x)^3 cross at (1.5, 3.375).
        // Their union keeps the lower half of each cubic, with exact dyadic controls.
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(1_000_000));
        using (SvgAuthoredPath a = Source(false, false, budget))
        using (SvgAuthoredPath b = Source(true, false, budget))
        {
            using (SvgAuthoredPath result = SvgClipUnion.ResolveNativeUnion(a, false, b, false, budget, CancellationToken.None))
            {
                SvgAuthoredPathCommand[] curves = result.Commands.ToArray().Where(command => command.Verb == SvgAuthoredPathVerb.Cubic).ToArray();
                Assert.Equal(2, curves.Length);
                Assert.Contains(curves, command => command.First == new SvgDevicePoint(.5, 0)
                    && command.Second == new SvgDevicePoint(1, 0) && command.Third == new SvgDevicePoint(1.5, 3.375));
                Assert.Contains(curves, command => command.First == new SvgDevicePoint(2, 0)
                    && command.Second == new SvgDevicePoint(2.5, 0) && command.Third == new SvgDevicePoint(3, 0));
            }
            using (SvgAuthoredPath partial = Source(false, true, budget))
            using (SvgAuthoredPath result = SvgClipUnion.ResolveNativeUnion(a, false, partial, false, budget, CancellationToken.None))
            {
                SvgAuthoredPathCommand[] curves = result.Commands.ToArray().Where(command => command.Verb == SvgAuthoredPathVerb.Cubic).ToArray();
                Assert.Equal(2, curves.Length);
                Assert.Equal(new SvgDevicePoint(1.5, 3.375), curves[0].Third);
                Assert.Equal(new SvgDevicePoint(3, 27), curves[1].Third);
                Assert.Equal(SvgAuthoredPathVerb.Close, result.Commands[^1].Verb);
            }
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);

        static SvgAuthoredPath Source(bool reflected, bool partial, RenderBudget budget)
        {
            using BudgetedParserList<SvgAuthoredPathCommand> commands = new(budget, SvgMemoryCategory.Scene, CancellationToken.None);
            commands.Add(new(SvgAuthoredPathVerb.Move, SvgAuthoredArcFlags.None, new(reflected ? 3 : 0, 0), default, default));
            commands.Add(new(SvgAuthoredPathVerb.Cubic, SvgAuthoredArcFlags.None, new(partial ? .5 : reflected ? 2 : 1, 0), new(partial ? 1 : reflected ? 1 : 2, 0), new(partial ? 1.5 : reflected ? 0 : 3, partial ? 3.375 : 27)));
            if (partial) commands.Add(new(SvgAuthoredPathVerb.Line, SvgAuthoredArcFlags.None, new(1.5, 27), default, default));
            commands.Add(new(SvgAuthoredPathVerb.Line, SvgAuthoredArcFlags.None, new(reflected ? 3 : 0, 27), default, default));
            commands.Add(new(SvgAuthoredPathVerb.Close, SvgAuthoredArcFlags.None, default, default, default));
            OwnedParserArray<SvgAuthoredPathCommand> owned = commands.Detach();
            try { return SvgAuthoredPath.Create(owned, SvgAffineTransform.Identity, budget); }
            catch { owned.Dispose(); throw; }
        }
    }

    [Fact]
    public void Native_curve_union_preserves_conics_and_releases_owners_on_denial_and_retry()
    {
        RenderBudget sources = new RenderBudget(new HostMemoryBudget(1_000_000)), budget = new RenderBudget(new HostMemoryBudget(1_000_000));
        using (SvgAuthoredPath circle = Ellipse(82, 76, 52, 52, SvgAffineTransform.Identity, sources))
        using (SvgAuthoredPath oval = Ellipse(151, 83, 68, 43, SvgAffineTransform.FromRotationDegrees(11, 151, 83), sources))
        {
            SvgAuthoredPathCommand[] unchanged = circle.Commands.ToArray();
            using (SvgAuthoredPath result = SvgClipUnion.ResolveNativeUnion(circle, false, oval, false, budget, CancellationToken.None))
            {
                // Independent native boundary observation; the unchanged platform
                // PNG controls separately establish the complete raster result.
                Assert.Equal(new SvgDevicePoint(115.44391632080078, 115.81839752197266), result.Commands[0].First);
                Assert.Equal(new SvgDevicePoint(100.94046783447266, 128), result.Commands[1].First);
                Assert.Equal(0.9396111965179443, result.Commands[1].Third.X);
                Assert.Equal(10, result.Commands.ToArray().Count(command => command.Verb == SvgAuthoredPathVerb.Conic));
                Assert.Equal(SvgAuthoredPathVerb.Close, result.Commands[^1].Verb);
            }
            Assert.Equal(0, budget.Snapshot().CurrentBytes);
            foreach (long limit in new[] { 1, budget.Snapshot().PeakBytes / 2 })
            {
                RenderBudget denied = new RenderBudget(new HostMemoryBudget(limit));
                Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() =>
                    SvgClipUnion.ResolveNativeUnion(circle, false, oval, false, denied, CancellationToken.None)).ErrorCode);
                Assert.Equal(0, denied.Snapshot().CurrentBytes);
            }
            Assert.Equal(0, budget.Snapshot().CurrentBytes);
            using CancellationTokenSource cancellation = new(); cancellation.Cancel();
            Assert.Equal(cancellation.Token, Assert.ThrowsAny<OperationCanceledException>(() =>
                SvgClipUnion.ResolveNativeUnion(circle, false, oval, false, budget, cancellation.Token)).CancellationToken);
            Assert.Equal(0, budget.Snapshot().CurrentBytes);
            using (SvgAuthoredPath retry = SvgClipUnion.ResolveNativeUnion(circle, false, oval, false, budget, CancellationToken.None))
                Assert.Equal(new SvgDevicePoint(115.44391632080078, 115.81839752197266), retry.Commands[0].First);
            Assert.Equal(0, budget.Snapshot().CurrentBytes);
            Assert.Equal(unchanged, circle.Commands.ToArray());
        }
        Assert.Equal(0, sources.Snapshot().CurrentBytes);

        static SvgAuthoredPath Ellipse(double x, double y, double rx, double ry, SvgAffineTransform map, RenderBudget budget)
        {
            using BudgetedParserList<SvgAuthoredPathCommand> commands = new(budget, SvgMemoryCategory.Scene, CancellationToken.None);
            Assert.True(SvgOrthogonalGeometryLowering.AppendNativeEllipseCommands(commands, x, y, rx, ry));
            OwnedParserArray<SvgAuthoredPathCommand> owned = commands.Detach();
            try { return SvgAuthoredPath.CreateConvexShape(owned, map, budget); }
            catch { owned.Dispose(); throw; }
        }
    }

    [Fact]
    public void Unit_interval_roots_preserve_crossings_and_repeated_contacts()
    {
        Span<double> roots = stackalloc double[6];
        Assert.Equal(4, SvgClipCurve.Roots([.09375, -.78125, 2.1875, -2.5, 1], roots, CancellationToken.None));
        for (int index = 0; index < 4; index++) Assert.InRange(Math.Abs(roots[index] - (index + 1) * .25), 0, 1e-13);
        Assert.Equal(2, SvgClipCurve.Roots([.03515625, -.375, 1.375, -2, 1], roots, CancellationToken.None));
        Assert.InRange(Math.Abs(roots[0] - .25), 0, 1e-13);
        Assert.InRange(Math.Abs(roots[1] - .75), 0, 1e-13);
    }

    [Fact]
    public void Degree_nine_roots_preserve_every_distinct_dyadic_crossing()
    {
        double[] coefficients = [1];
        foreach (double root in new[] { 0, .125, .25, .375, .5, .625, .75, .875, 1 })
        {
            double[] next = new double[coefficients.Length + 1];
            for (int i = 0; i < coefficients.Length; i++) { next[i] -= root * coefficients[i]; next[i + 1] += coefficients[i]; }
            coefficients = next;
        }
        Span<double> roots = stackalloc double[9];
        Assert.Equal(9, SvgClipCurve.Roots(coefficients, roots, CancellationToken.None));
        for (int index = 0; index < 9; index++) Assert.InRange(Math.Abs(roots[index] - index * .125), 0, 1e-8);
    }

    [Theory]
    [InlineData("<path d=''/><path d='M1 1H5V5H1Z'/>")]
    [InlineData("<path d='M1 1H5V5H1Z'/><circle r='0'/>")]
    [InlineData("<path d='M1 1H3V3H5V5H1Z'/><rect x='3' y='1' width='2' height='2'/>")]
    [InlineData("<path d='M1 1C1 2 1 4 1 5L3 5C3 4 3 2 3 1Z'/><path d='M2 1C2 2 2 4 2 5L5 5C5 4 5 2 5 1Z'/>")]
    [InlineData("<path d='M1 1C1 2 1 4 1 5C1.5 5 2.5 5 3 5C3 4 3 2 3 1C2.5 1 1.5 1 1 1Z'/><path d='M2 1C2 2 2 4 2 5C3 5 4 5 5 5C5 4 5 2 5 1C4 1 3 1 2 1Z'/>")]
    public void Clip_child_unions_preserve_expected_square_and_release_owners(string children)
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><clipPath id='c'>" + children + "</clipPath></defs><rect width='8' height='8' fill='red' clip-path='url(#c)'/></svg>");
        RenderBudget sceneBudget = new RenderBudget(new HostMemoryBudget(1_000_000)), rasterBudget = new RenderBudget(new HostMemoryBudget(1_000_000));
        using (SvgRoot scene = SvgRootParser.Parse(input, SvgRenderLimits.Default, sceneBudget, CancellationToken.None))
        using (SvgConverter.RasterizedSurface raster = SvgConverter.Rasterize(scene, SvgRenderOptions.Default, rasterBudget, CancellationToken.None))
            for (int y = 0; y < 8; y++) for (int x = 0; x < 8; x++)
            {
                int offset = (y * 8 + x) * 4;
                byte red = x is >= 1 and < 5 && y is >= 1 and < 5 ? (byte)255 : (byte)0;
                Assert.Equal(red, raster.Pixels[offset]); Assert.Equal(0, raster.Pixels[offset + 1]);
                Assert.Equal(0, raster.Pixels[offset + 2]); Assert.Equal(red, raster.Pixels[offset + 3]);
            }
        Assert.Equal(0, sceneBudget.Snapshot().CurrentBytes); Assert.Equal(0, rasterBudget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Crossing_union_matches_native_boundary_and_releases_temporary_storage()
    {
        // Independent native path-ops observation for the frozen mixed rectangle/path
        // control. The platform visual compares rendered pixels against Chrome separately.
        SvgClipPolygon input = new(
            [new(16, 122), new(108, 122), new(108, 24), new(16, 24),
             new(90, 112), new(172, 136), new(212, 55), new(82, 38)], [4, 8]);
        SvgDevicePoint[] expected =
        [
            new(108, 122), new(16, 122), new(16, 24), new(108, 24),
            new(108, 41.400001525878906), new(212, 55), new(172, 136),
            new(108, 117.26829528808594), new(108, 122)
        ];
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(1_000_000));
        using (SvgAuthoredPath output = Assert.IsType<SvgAuthoredPath>(SvgClipUnion.ResolveCrossings(input, budget, CancellationToken.None)))
        {
            Assert.Equal(expected, output.Commands.ToArray().Where(command => command.Verb != SvgAuthoredPathVerb.Close).Select(command => command.First));
            Assert.Equal(SvgAuthoredPathVerb.Close, output.Commands[^1].Verb);
            Assert.True(budget.Snapshot().CurrentBytes > 0);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        foreach (long limit in new[] { 1, budget.Snapshot().PeakBytes / 2 })
        {
            RenderBudget denied = new RenderBudget(new HostMemoryBudget(limit));
            Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() =>
                SvgClipUnion.ResolveCrossings(input, denied, CancellationToken.None)).ErrorCode);
            Assert.Equal(0, denied.Snapshot().CurrentBytes);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        Assert.Equal(cancellation.Token, Assert.ThrowsAny<OperationCanceledException>(() =>
            SvgClipUnion.ResolveCrossings(input, budget, cancellation.Token)).CancellationToken);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        using (SvgAuthoredPath retry = Assert.IsType<SvgAuthoredPath>(SvgClipUnion.ResolveCrossings(input, budget, CancellationToken.None)))
            Assert.Equal(expected, retry.Commands.ToArray().Where(command => command.Verb != SvgAuthoredPathVerb.Close).Select(command => command.First));
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Disjoint_union_keeps_existing_normalization_and_releases_scratch()
    {
        SvgClipPolygon input = new([new(0, 0), new(0, 4), new(4, 0), new(8, 0), new(8, 4), new(12, 0)], [3, 6]);
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(1_000_000));
        Assert.Null(SvgClipUnion.ResolveCrossings(input, budget, CancellationToken.None));
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }
}
