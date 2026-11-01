using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

[Trait("Layer", "Mechanism")]
public sealed class SvgClipBoundaryTests
{
    [Theory]
    [InlineData(16, 24, 16, 20, 128)]
    [InlineData(24, 28, 0, 0, 0)]
    [InlineData(2, 28, 8, 20, 384)]
    public void Intersection_keeps_only_common_coverage_and_borrows_both_operands(int left, int right, int expectedLeft, int expectedRight, int twiceArea)
    {
        RenderBudget budget = new(new HostMemoryBudget(1_000_000));
        using (SvgAuthoredPath first = SvgOrderedClipTests.Rectangle(8, 20, budget))
        using (SvgAuthoredPath second = SvgOrderedClipTests.Rectangle(left, right, budget))
        {
            SvgAuthoredPathCommand[] unchanged = first.Commands.ToArray();
            SvgAuthoredPathCommand[] secondUnchanged = second.Commands.ToArray();
            using (SvgAuthoredPath result = SvgClipUnion.ResolveNativeIntersection(first, false, second, false, budget, CancellationToken.None))
            {
                if (twiceArea == 0) Assert.Empty(result.Commands.ToArray());
                else
                {
                    using SvgPreparedPathContours prepared = result.Prepare(SvgAffineTransform.Identity, false, budget, CancellationToken.None);
                    SvgDevicePoint[] points = prepared.Points.ToArray();
                    Assert.Equal(expectedLeft, points.Min(point => point.X));
                    Assert.Equal(expectedRight, points.Max(point => point.X));
                    Assert.Equal(8, points.Min(point => point.Y));
                    Assert.Equal(24, points.Max(point => point.Y));
                    double area = 0;
                    for (int index = 0; index < prepared.Points.Length; index++)
                    {
                        SvgDevicePoint a = prepared.Points[index], b = prepared.Points[(index + 1) % prepared.Points.Length];
                        area += a.X * b.Y - b.X * a.Y;
                    }
                    // The independently observed native intersection boundary
                    // has its filled face on the right. Traversal direction
                    // also preserves the fixed edge-rounding stage.
                    Assert.Equal(-twiceArea, area);
                }
            }
            Assert.Equal(unchanged, first.Commands.ToArray());
            Assert.Equal(secondUnchanged, second.Commands.ToArray());
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Theory]
    [InlineData(true, 192)]
    [InlineData(false, 256)]
    public void Intersection_preserves_each_operands_winding_rule(bool parity, int twiceArea)
    {
        RenderBudget budget = new(new HostMemoryBudget(1_000_000));
        using (SvgAuthoredPath source = NestedBoxes(budget))
        using (SvgAuthoredPath clip = SvgOrderedClipTests.Rectangle(16, 28, budget))
        using (SvgAuthoredPath result = SvgClipUnion.ResolveNativeIntersection(source, parity, clip, false, budget, CancellationToken.None))
        using (SvgPreparedPathContours prepared = result.Prepare(SvgAffineTransform.Identity, true, budget, CancellationToken.None))
        {
            double area = 0;
            int first = 0;
            foreach (int end in prepared.ContourEnds)
            {
                for (int index = first; index < end; index++)
                {
                    SvgDevicePoint a = prepared.Points[index], b = prepared.Points[index + 1 == end ? first : index + 1];
                    area += a.X * b.Y - b.X * a.Y;
                }
                first = end;
            }
            Assert.Equal(-twiceArea, area);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);

        static SvgAuthoredPath NestedBoxes(RenderBudget budget)
        {
            using BudgetedParserList<SvgAuthoredPathCommand> commands = new(budget, SvgMemoryCategory.Scene, CancellationToken.None);
            foreach ((int left, int top, int right, int bottom) in new[] { (8, 8, 24, 24), (12, 12, 20, 20) })
            {
                commands.Add(new(SvgAuthoredPathVerb.Move, SvgAuthoredArcFlags.None, new(left, top), default, default));
                commands.Add(new(SvgAuthoredPathVerb.Line, SvgAuthoredArcFlags.None, new(right, top), default, default));
                commands.Add(new(SvgAuthoredPathVerb.Line, SvgAuthoredArcFlags.None, new(right, bottom), default, default));
                commands.Add(new(SvgAuthoredPathVerb.Line, SvgAuthoredArcFlags.None, new(left, bottom), default, default));
                commands.Add(new(SvgAuthoredPathVerb.Close, SvgAuthoredArcFlags.None, default, default, default));
            }
            OwnedParserArray<SvgAuthoredPathCommand> owned = commands.Detach();
            try { return SvgAuthoredPath.Create(owned, SvgAffineTransform.Identity, budget); }
            catch { owned.Dispose(); throw; }
        }
    }
}
