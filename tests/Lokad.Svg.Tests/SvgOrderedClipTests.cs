using System.Text;
using System.Text.Json;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

[Trait("Layer", "Mechanisms")]
public sealed class SvgOrderedClipTests
{
    [Theory]
    [InlineData(2, false)]
    [InlineData(3, false)]
    [InlineData(3, true)]
    public void Ordered_original_operands_preserve_native_cyclic_start_and_deferred_lines(int count, bool reorder)
    {
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(1_000_000));
        using (SvgAuthoredPath first = Rectangle(8, 20, budget))
        using (SvgAuthoredPath second = Rectangle(16, 24, budget))
        using (SvgAuthoredPath third = Rectangle(12, 20, budget))
        {
            SvgClipChild[] children = count == 2 ? [new(first, false, SvgClipPaintKind.Rectangle), new(second, false, SvgClipPaintKind.Rectangle)]
                : reorder ? [new(first, false, SvgClipPaintKind.Rectangle), new(third, false, SvgClipPaintKind.Rectangle), new(second, false, SvgClipPaintKind.Rectangle)]
                : [new(first, false, SvgClipPaintKind.Rectangle), new(second, false, SvgClipPaintKind.Rectangle), new(third, false, SvgClipPaintKind.Rectangle)];
            SvgClipUnionResult resolved = SvgClipUnion.ResolveOrdered(children, budget, CancellationToken.None);
            using SvgAuthoredPath result = resolved.Path;
            Assert.True(resolved.IsEvenOdd);
            using JsonDocument evidence = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData/ordered-clip-coverage/native-boundaries.json")));
            string id = count == 2 ? "clip-union-expansion-0120-candidate" : reorder ? "overlap-three-reordered" : "overlap-three-shear";
            JsonElement vector = evidence.RootElement.GetProperty("vectors").EnumerateArray().Single(row => row.GetProperty("id").GetString() == id);
            SvgDevicePoint[] expected = vector.GetProperty("points").EnumerateArray().Select(point => new SvgDevicePoint(point[0].GetDouble(), point[1].GetDouble())).ToArray();
            // The native boundary trace retains the midpoint start, even though
            // the union is geometrically a rectangle. Raster references test its coverage separately.
            Assert.Equal(expected, result.Commands[..^1].ToArray().Select(command => command.First));
            Assert.Equal(SvgAuthoredPathVerb.Move, result.Commands[0].Verb);
            Assert.All(result.Commands[1..^1].ToArray(), command => Assert.Equal(SvgAuthoredPathVerb.Line, command.Verb));
            Assert.Equal(SvgAuthoredPathVerb.Close, result.Commands[^1].Verb);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Theory]
    [InlineData(false, false, 1)]
    [InlineData(true, false, -1)]
    [InlineData(false, true, 0)]
    public void Raw_direction_requires_nondegenerate_convex_controls(bool reversed, bool flat, int expected)
    {
        SvgDevicePoint[] points = flat ? [new(8, 8), new(16, 8), new(24, 8)]
            : [new(8, 8), new(24, 8), new(24, 24), new(8, 24)];
        if (reversed) Array.Reverse(points);
        Assert.Equal((SvgConvexDirection)expected, SvgRawContourConvexity.Direction(points, [], [], CancellationToken.None));
    }

    [Fact]
    public void Straight_native_controls_are_reclassified_after_float_placement()
    {
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(1_000_000));
        using (SvgAuthoredPath path = Boundary(budget))
        using (SvgPreparedPathContours source = path.PrepareSource(SvgAffineTransform.Identity, false, budget, CancellationToken.None))
        {
            SvgAffineTransform map = new(1, .2, .3, 1, 0, 0);
            SvgDevicePoint[] placed = source.Points.Select(point => SvgAuthoredPath.MapPoint(map, point, new(0, 0))).ToArray();
            Assert.True(SvgRawContourConvexity.IsConvex(source.Points, [], [], CancellationToken.None));
            Assert.False(SvgRawContourConvexity.IsConvexAfterPlacement(source, map, new(0, 0), CancellationToken.None));
            SvgAnalyticContourPreparation preparation = new(placed, source.ContourEnds, [1], true, null, SvgPolygonMapping.NativeFillEdges,
                [], [], new(10, 9, 32, 29), 32, 32, source, map);
            using SvgAnalyticCoverage walker = SvgAnalyticCoverage.Prepare(ref preparation, 0, 32, 0, 32, new(0, 32),
                new(new(0, 0, 32, 32)), SvgAnalyticPaintClip.Binary, budget, out bool eligible, CancellationToken.None);
            Assert.True(eligible);
            Assert.Equal(true, preparation.SourceConvex);
            Assert.Equal(false, preparation.Convex);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);

        static SvgAuthoredPath Boundary(RenderBudget budget)
        {
            using BudgetedParserList<SvgAuthoredPathCommand> commands = new(budget, SvgMemoryCategory.Scene, CancellationToken.None);
            SvgDevicePoint[] points = [new(12, 24), new(8, 24), new(8, 8), new(24, 8), new(24, 24), new(12, 24)];
            for (int index = 0; index < points.Length; index++)
                commands.Add(new(index == 0 ? SvgAuthoredPathVerb.Move : SvgAuthoredPathVerb.Line, SvgAuthoredArcFlags.None, points[index], default, default));
            commands.Add(new(SvgAuthoredPathVerb.Close, SvgAuthoredArcFlags.None, default, default, default));
            OwnedParserArray<SvgAuthoredPathCommand> owned = commands.Detach();
            try { return SvgAuthoredPath.Create(owned, SvgAffineTransform.Identity, budget); }
            catch { owned.Dispose(); throw; }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Painted_clip_retains_enclosing_scope_and_authored_child_paint(bool outerPath)
    {
        StringBuilder input = new("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'><defs><clipPath id='c'>");
        for (int index = 0; index < 42; index++) input.Append("<rect x='12' y='12' width='4' height='4'/>");
        input.Append(outerPath ? "<path d='M8 8H24V24H8Z'/>" : "<rect x='8' y='8' width='16' height='16'/>");
        input.Append("</clipPath></defs><g clip-path='url(#c)' transform='translate(.5 .25)'><path d='M4 26Q16 -4 28 26Z'/></g></svg>");
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(2_000_000));
        using (SvgRoot root = SvgRootParser.Parse(System.Text.Encoding.UTF8.GetBytes(input.ToString()), SvgRenderLimits.Default, budget, null, CancellationToken.None))
        {
            SvgDrawCommand[] scopes = root.Commands.Where(command => command.Kind == SvgDrawCommandKind.BeginGroup).ToArray();
            int mask = Array.FindIndex(scopes, command => command.RegionKind == SvgLayerRegionKind.ClipMask);
            Assert.True(mask > 0);
            Assert.Equal(SvgLayerRegionKind.AffineViewport, scopes[mask - 1].RegionKind);
            SvgSceneStorage storage = Assert.IsType<SvgSceneStorage>(root.SceneStorage);
            SvgVectorContent content = Assert.IsType<SvgVectorContent>(storage.MaskContents[scopes[mask].MaskContentIndex].VectorContent);
            Assert.Equal(43, content.Scene.Geometries.Length);
            SvgAuthoredPath[] paints = content.Scene.Geometries.Select(geometry => Assert.IsType<SvgAuthoredPath>(geometry.AuthoredPath)).ToArray();
            Assert.All(paints[..42], paint => Assert.True(paint.IsRectangleFill));
            Assert.Equal(!outerPath, paints[42].IsRectangleFill);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    internal static SvgAuthoredPath Rectangle(double left, double right, RenderBudget budget)
    {
        using BudgetedParserList<SvgAuthoredPathCommand> commands = new(budget, SvgMemoryCategory.Scene, CancellationToken.None);
        SvgDevicePoint[] points = [new(left, 8), new(right, 8), new(right, 24), new(left, 24)];
        for (int index = 0; index < points.Length; index++)
            commands.Add(new(index == 0 ? SvgAuthoredPathVerb.Move : SvgAuthoredPathVerb.Line, SvgAuthoredArcFlags.None, points[index], default, default));
        commands.Add(new(SvgAuthoredPathVerb.Close, SvgAuthoredArcFlags.None, default, default, default));
        OwnedParserArray<SvgAuthoredPathCommand> owned = commands.Detach();
        try { return SvgAuthoredPath.Create(owned, SvgAffineTransform.Identity, budget); }
        catch { owned.Dispose(); throw; }
    }
}
