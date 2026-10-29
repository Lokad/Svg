using System.Xml;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Parsing;

internal enum SvgMarkerVertexRole { Start, Mid, End }
internal readonly record struct SvgMarkerPosition(SvgMarkerVertexRole Role, SvgDevicePoint Point, double Angle);

/// <summary>Reads authored path and point-list vertices before stroke dashing or fill closure.</summary>
/// <remarks>Command, corner and output lists charge caller-owned bounded parser storage. Every command,
/// derivative and vertex consumes work and observes cancellation; no curve flattening is required.</remarks>
internal static class SvgMarkerVertices
{
    private readonly record struct Corner(SvgDevicePoint Point, SvgDevicePoint Incoming, SvgDevicePoint Outgoing,
        bool StartsSubpath, bool EndsSubpath, bool Closed);

    internal static BudgetedParserList<SvgMarkerPosition> Read(XmlReader reader, int maximumCommands,
        SvgDocumentState state, CancellationToken token)
    {
        using BudgetedParserList<SvgAuthoredPathCommand> commands = new(state.Budget, SvgMemoryCategory.Parser, token);
        if (reader.LocalName is "polygon" or "polyline")
        {
            string values = reader.GetAttribute("points") ?? string.Empty;
            using BudgetedParserList<SvgFractionalPoint> points = SvgOrthogonalGeometryTokenizer.ParseFractionalPointList(values,
                reader.LocalName, maximumCommands, state.Budget, token, 0);
            for (int index = 0; index < points.Count; index++)
            {
                token.ThrowIfCancellationRequested();
                commands.Add(new(index == 0 ? SvgAuthoredPathVerb.Move : SvgAuthoredPathVerb.Line,
                    SvgAuthoredArcFlags.None, new(points[index].X, points[index].Y), default, default));
            }
            if (reader.LocalName == "polygon" && points.Count > 0) commands.Add(new(SvgAuthoredPathVerb.Close, SvgAuthoredArcFlags.None, default, default, default));
            commands.Add(new(SvgAuthoredPathVerb.End, SvgAuthoredArcFlags.None, default, default, default));
        }
        else
        {
            using SvgRenderablePathData data = SvgRenderablePathData.Read(reader.GetAttribute("d"), maximumCommands, state.Budget, token);
            SvgPathDataParser.ParseCommands(data.Value, maximumCommands, token, commands);
        }
        using BudgetedParserList<Corner> corners = new(state.Budget, SvgMemoryCategory.Parser, token);
        int subpathStart = -1;
        SvgDevicePoint current = default;
        Span<SvgEndpointConic> arcs = stackalloc SvgEndpointConic[SvgEndpointArc.MaximumConicCount];
        foreach (SvgAuthoredPathCommand command in commands.AsSpan())
        {
            token.ThrowIfCancellationRequested();
            switch (command.Verb)
            {
                case SvgAuthoredPathVerb.Move:
                    subpathStart = corners.Count;
                    corners.Add(new(command.First, Difference(command.First, current), default, true, false, false));
                    current = command.First;
                    break;
                case SvgAuthoredPathVerb.Line:
                    SvgDevicePoint direction = Difference(command.First, current);
                    Append(command.First, direction, direction);
                    break;
                case SvgAuthoredPathVerb.Quadratic:
                    SvgDevicePoint quadraticStart = Difference(command.First, current);
                    SvgDevicePoint quadraticEnd = Difference(command.Second, command.First);
                    if (quadraticStart == default) quadraticStart = quadraticEnd;
                    if (quadraticEnd == default) quadraticEnd = quadraticStart;
                    Append(command.Second, quadraticStart, quadraticEnd);
                    break;
                case SvgAuthoredPathVerb.Cubic:
                    SvgDevicePoint cubicStart = Difference(command.First, current);
                    SvgDevicePoint cubicEnd = Difference(command.Third, command.Second);
                    if (cubicStart == default)
                    {
                        cubicStart = Difference(command.Second, command.First);
                        if (cubicStart == default) cubicStart = cubicEnd;
                        if (cubicEnd == default) cubicEnd = cubicStart;
                    }
                    else if (cubicEnd == default)
                    {
                        cubicEnd = Difference(command.Second, command.First);
                        if (cubicEnd == default) cubicEnd = cubicStart;
                    }
                    Append(command.Third, cubicStart, cubicEnd);
                    break;
                case SvgAuthoredPathVerb.Close:
                    if (subpathStart < 0) throw new SvgException(SvgErrorCode.MalformedInput, "A marker closepath requires a subpath.");
                    Corner first = corners[subpathStart];
                    SvgDevicePoint closing = Difference(first.Point, current);
                    if (closing == default && !corners[^1].StartsSubpath) closing = first.Outgoing;
                    Append(first.Point, closing, closing);
                    corners[subpathStart] = first with { Incoming = closing, Closed = true };
                    corners[^1] = corners[^1] with { Outgoing = first.Outgoing, Closed = true };
                    break;
                case SvgAuthoredPathVerb.End:
                    if (corners.Count != 0) corners[^1] = corners[^1] with { EndsSubpath = true };
                    break;
                case SvgAuthoredPathVerb.Arc:
                    token.ThrowIfCancellationRequested();
                    int count = SvgEndpointArc.Convert(current, command.First.X, command.First.Y, command.Second.X,
                        (command.ArcFlags & SvgAuthoredArcFlags.Large) != 0, (command.ArcFlags & SvgAuthoredArcFlags.Sweep) != 0,
                        command.Third, arcs, token);
                    SvgDevicePoint arcStart = count == 0 ? Difference(command.Third, current) : Difference(arcs[0].Control, current);
                    SvgDevicePoint arcEnd = count == 0 ? arcStart : Difference(arcs[count - 1].End, arcs[count - 1].Control);
                    Append(command.Third, arcStart, arcEnd);
                    break;
                default:
                    throw new InvalidOperationException("Unexpected authored marker path command.");
            }
        }
        BudgetedParserList<SvgMarkerPosition> result = new(state.Budget, SvgMemoryCategory.Parser, token);
        try
        {
            for (int index = 0; index < corners.Count; index++)
            {
                token.ThrowIfCancellationRequested();
                Corner corner = corners[index];
                double incoming = Angle(corner.Incoming), outgoing = Angle(corner.Outgoing);
                double angle;
                if (corner.Closed || !corner.StartsSubpath && !corner.EndsSubpath)
                {
                    double difference = incoming - outgoing;
                    if (difference > 180 || difference <= -180) incoming += 360;
                    angle = (incoming + outgoing) / 2;
                }
                else angle = corner.EndsSubpath ? incoming : outgoing;
                SvgMarkerVertexRole role = index == corners.Count - 1 ? SvgMarkerVertexRole.End
                    : index == 0 ? SvgMarkerVertexRole.Start : SvgMarkerVertexRole.Mid;
                result.Add(new(role, corner.Point, (float)angle));
            }
            return result;
        }
        catch { result.Dispose(); throw; }

        void Append(SvgDevicePoint point, SvgDevicePoint start, SvgDevicePoint end)
        {
            if (corners.Count == 0) throw new SvgException(SvgErrorCode.MalformedInput, "A marker segment requires an initial moveto.");
            Corner previous = corners[^1];
            if (!previous.Closed) corners[^1] = previous with { Outgoing = start };
            corners.Add(new(point, end, default, false, false, false));
            current = point;
        }
        static SvgDevicePoint Difference(SvgDevicePoint end, SvgDevicePoint start)
            => new((float)end.X - (float)start.X, (float)end.Y - (float)start.Y);
        static double Angle(SvgDevicePoint direction)
        {
            // Exact axis angles keep the inclusive half-turn boundary stable.
            // A float pi converted through double degrees can exceed 180 and
            // flip a repeated vertex's bisector by a full half turn.
            if (direction.Y == 0) return direction.X < 0 ? 180 : 0;
            if (direction.X == 0) return direction.Y < 0 ? -90 : 90;
            return MathF.Atan2((float)direction.Y, (float)direction.X) * (180d / Math.PI);
        }
    }
}
