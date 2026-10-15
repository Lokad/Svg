using Lokad.Svg.Rendering;

namespace Lokad.Svg.Parsing;

/// <summary>Resolves retained clip child boundaries before native path replay.</summary>
internal static partial class SvgClipUnion
{
    private readonly record struct Edge(SvgDevicePoint Start, SvgDevicePoint End, int Contour);
    private readonly record struct Cut(int Edge, double Parameter, SvgDevicePoint Point) : IComparable<Cut>
    {
        public int CompareTo(Cut other)
        {
            int edge = Edge.CompareTo(other.Edge);
            return edge != 0 ? edge : Parameter.CompareTo(other.Parameter);
        }
    }
    private readonly record struct Boundary(SvgDevicePoint Start, SvgDevicePoint End, bool Emitted);

    /// <summary>Resolves crossings of normalized straight convex children, retaining legacy disjoint replay.</summary>
    /// <remarks>Input contours borrow staging and have the same direction. Scratch reserves before growth;
    /// pair, winding and linking visits charge work/token. An owned result transfers to the caller;
    /// null preserves the preceding route. Failure releases scratch and leaves input owners untouched.</remarks>
    internal static SvgAuthoredPath? ResolveCrossings(SvgClipPolygon polygon, RenderBudget budget, ParserWorkCounter work, CancellationToken token)
    {
        using BudgetedParserList<Edge> edges = new(budget, SvgMemoryCategory.Geometry, work, token);
        using BudgetedParserList<Cut> cuts = new(budget, SvgMemoryCategory.Geometry, work, token);
        int start = 0;
        for (int contour = 0; contour < polygon.ContourEnds.Length; contour++)
        {
            int end = polygon.ContourEnds[contour];
            for (int index = start; index < end; index++)
            {
                work.Consume(token);
                SvgDevicePoint a = polygon.Points[index], b = polygon.Points[index + 1 < end ? index + 1 : start];
                if (a == b) continue;
                int edge = edges.Count;
                edges.Add(new(a, b, contour));
                cuts.Add(new(edge, 0, a)); cuts.Add(new(edge, 1, b));
            }
            start = end;
        }
        bool crosses = false;
        for (int left = 0; left < edges.Count; left++)
        {
            Edge a = edges[left];
            for (int right = left + 1; right < edges.Count; right++)
            {
                work.Consume(token);
                Edge b = edges[right];
                if (a.Contour == b.Contour) continue;
                double ax = a.End.X - a.Start.X, ay = a.End.Y - a.Start.Y;
                double bx = b.End.X - b.Start.X, by = b.End.Y - b.Start.Y;
                double denominator = ax * by - ay * bx;
                if (denominator == 0) continue;
                double dx = b.Start.X - a.Start.X, dy = b.Start.Y - a.Start.Y;
                double t = (dx * by - dy * bx) / denominator;
                double u = (dx * ay - dy * ax) / denominator;
                if (t <= 0 || t >= 1 || u <= 0 || u >= 1) continue;
                SvgDevicePoint point = new((float)(a.Start.X + t * ax), (float)(a.Start.Y + t * ay));
                cuts.Add(new(left, t, point)); cuts.Add(new(right, u, point));
                crosses = true;
            }
        }
        if (!crosses) return null;
        cuts.Sort(work, token);
        using BudgetedParserList<Boundary> boundary = new(budget, SvgMemoryCategory.Geometry, work, token);
        for (int index = 1; index < cuts.Count; index++)
        {
            work.Consume(token);
            Cut a = cuts[index - 1], b = cuts[index];
            if (a.Edge != b.Edge || a.Point == b.Point || a.Parameter == b.Parameter) continue;
            Edge source = edges[a.Edge];
            double middle = (a.Parameter + b.Parameter) / 2;
            SvgDevicePoint sample = new(source.Start.X + middle * (source.End.X - source.Start.X),
                source.Start.Y + middle * (source.End.Y - source.Start.Y));
            bool covered = false;
            for (int contour = 0; contour < polygon.ContourEnds.Length && !covered; contour++)
            {
                if (contour == source.Contour) continue;
                int first = contour == 0 ? 0 : polygon.ContourEnds[contour - 1];
                int end = polygon.ContourEnds[contour];
                int winding = 0;
                for (int vertex = first; vertex < end; vertex++)
                {
                    work.Consume(token);
                    SvgDevicePoint p = polygon.Points[vertex], q = polygon.Points[vertex + 1 < end ? vertex + 1 : first];
                    double cross = (q.X - p.X) * (sample.Y - p.Y) - (q.Y - p.Y) * (sample.X - p.X);
                    if (p.Y <= sample.Y && q.Y > sample.Y && cross > 0) winding++;
                    if (p.Y > sample.Y && q.Y <= sample.Y && cross < 0) winding--;
                }
                covered = winding != 0;
            }
            if (!covered) boundary.Add(new(a.Point, b.Point, false));
        }
        using BudgetedParserList<SvgAuthoredPathCommand> commands = new(budget, SvgMemoryCategory.Scene, work, token);
        int remaining = boundary.Count;
        while (remaining > 0)
        {
            int seed = -1;
            // Native winding normalization starts at the next endpoint of the
            // first retained contour and follows its reversed boundary direction.
            SvgDevicePoint preferred = polygon.Points[1];
            for (int index = 0; index < boundary.Count; index++)
            {
                work.Consume(token);
                Boundary candidate = boundary[index];
                if (candidate.Emitted) continue;
                if (seed < 0) seed = index;
                if (candidate.End == preferred) { seed = index; break; }
            }
            if (seed < 0) throw new InvalidOperationException("A clip union boundary is missing.");
            SvgDevicePoint first = boundary[seed].End;
            commands.Add(new(SvgAuthoredPathVerb.Move, SvgAuthoredArcFlags.None, first, default, default));
            int current = seed;
            while (true)
            {
                work.Consume(token);
                Boundary edge = boundary[current];
                boundary[current] = edge with { Emitted = true };
                remaining--;
                commands.Add(new(SvgAuthoredPathVerb.Line, SvgAuthoredArcFlags.None, edge.Start, default, default));
                if (edge.Start == first) break;
                int next = -1;
                for (int index = 0; index < boundary.Count; index++)
                {
                    work.Consume(token);
                    Boundary candidate = boundary[index];
                    if (!candidate.Emitted && candidate.End == edge.Start) { next = index; break; }
                }
                if (next < 0) throw new SvgException(SvgErrorCode.UnsupportedFeature, "The clip union boundary could not be linked.");
                current = next;
            }
            commands.Add(new(SvgAuthoredPathVerb.Close, SvgAuthoredArcFlags.None, default, default, default));
        }
        OwnedParserArray<SvgAuthoredPathCommand> owned = commands.Detach();
        try { return SvgAuthoredPath.Create(owned, SvgAffineTransform.Identity, budget); }
        catch { owned.Dispose(); throw; }
    }
}
