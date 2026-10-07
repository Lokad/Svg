using Lokad.Svg.Rendering;

namespace Lokad.Svg.Parsing;

internal enum SvgAuthoredPathVerb { Move, Line, Quadratic, Cubic, Arc, Conic, End, Close }

/// <summary>Retains stroke width, its independent scene map and admitted cap semantics beside the centerline.</summary>
internal readonly record struct SvgAuthoredStrokeStyle(double Width, SvgAffineTransform WidthMap, SvgStrokeCap Cap);

[Flags]
internal enum SvgAuthoredArcFlags : byte { None = 0, Large = 1, Sweep = 2 }

/// <summary>Retains validated authored float operands, before any scene or output map.</summary>
/// <remarks>For a filtered conic, First is its control, Second its endpoint and Third.X its weight.</remarks>
internal readonly record struct SvgAuthoredPathCommand(SvgAuthoredPathVerb Verb, SvgAuthoredArcFlags ArcFlags, SvgDevicePoint First, SvgDevicePoint Second, SvgDevicePoint Third);

/// <summary>Owns immutable authored commands and their scene map until one geometry releases them.</summary>
/// <remarks>Creation, copying and disposal belong to one conversion's single-threaded budget.
/// Readers borrow immutable storage. Copies reserve and own distinct storage; no global state or shared mutable owner exists.</remarks>
internal sealed class SvgAuthoredPath : IDisposable
{
    // Covers the owner, geometry slot and transient old/new owner during transfer.
    private const long OwnerBytes = 384;
    private OwnedParserArray<SvgAuthoredPathCommand> _commands;
    private RenderBudget.BudgetReservation? _ownerReservation;

    private SvgAuthoredPath(OwnedParserArray<SvgAuthoredPathCommand> commands, SvgAffineTransform sceneMap, SvgAuthoredStrokeStyle? strokeStyle, RenderBudget.BudgetReservation ownerReservation)
    {
        _commands = commands;
        SceneMap = sceneMap;
        StrokeStyle = strokeStyle;
        _ownerReservation = ownerReservation;
    }

    internal ReadOnlySpan<SvgAuthoredPathCommand> Commands => _commands.Items;
    internal SvgAffineTransform SceneMap { get; }
    internal SvgAuthoredStrokeStyle? StrokeStyle { get; }

    /// <summary>Transfers storage to a replacement owner; later disposal of this dropped owner is harmless.</summary>
    internal SvgAuthoredPath Transfer()
    {
        RenderBudget.BudgetReservation owner = _ownerReservation ?? throw new InvalidOperationException("Authored path ownership has already transferred.");
        SvgAuthoredPath result = new(_commands, SceneMap, StrokeStyle, owner);
        _commands = new([], null);
        _ownerReservation = null;
        return result;
    }

    /// <summary>Replays authored commands and maps their vertices/controls once with the composed output matrix.</summary>
    /// <remarks>Reserves temporary builder storage before allocation. Every command and copied point consumes caller work;
    /// flattening uses its existing bounded, cancellable policy. The result exclusively owns its surface arrays.</remarks>
    internal SvgPreparedPathContours Prepare(SvgAffineTransform outputMap, bool isEvenOdd, RenderBudget budget, ParserWorkCounter workCounter, CancellationToken cancellationToken)
        => Replay(outputMap, isEvenOdd, true, budget, workCounter, cancellationToken);

    /// <summary>Replays and flattens source commands once, keeping original points for bitmap-local mapping.</summary>
    /// <remarks>The returned owner retains source coordinates; the caller maps them with outputMap composed with SceneMap. Reservations, work, cancellation and cleanup match Prepare.</remarks>
    internal SvgPreparedPathContours PrepareSource(SvgAffineTransform outputMap, bool isEvenOdd, RenderBudget budget, ParserWorkCounter workCounter, CancellationToken cancellationToken)
        => Replay(outputMap, isEvenOdd, false, budget, workCounter, cancellationToken);

    private SvgPreparedPathContours Replay(SvgAffineTransform outputMap, bool isEvenOdd, bool mapCoordinates, RenderBudget budget, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        if (StrokeStyle is not null) throw new InvalidOperationException("A retained stroke centerline cannot be replayed as a fill.");
        SvgAffineTransform map = outputMap.ComposeChecked(SceneMap);
        double magnification = Math.Max(Math.Sqrt(map.A * map.A + map.B * map.B), Math.Sqrt(map.C * map.C + map.D * map.D));
        SvgFlattenPolicy policy = new(SvgOrthogonalGeometryLowering.ScaledFlatness(magnification), SvgOrthogonalGeometryLowering.FlattenMaxDepth, SvgOrthogonalGeometryLowering.CurveSegmentsPerCommand);
        // Growing arrays temporarily retain old and new buffers. Span columns
        // round up from four slots; Get*Spans also materializes record arrays.
        int maximumSpans = Commands.Length;
        long maximumPoints = Commands.Length;
        foreach (SvgAuthoredPathCommand command in Commands)
        {
            workCounter.Consume(cancellationToken);
            if (command.Verb == SvgAuthoredPathVerb.Arc) maximumSpans = checked(maximumSpans + 2);
            if (command.Verb is SvgAuthoredPathVerb.Quadratic or SvgAuthoredPathVerb.Cubic or SvgAuthoredPathVerb.Conic or SvgAuthoredPathVerb.Arc)
                maximumPoints += SvgOrthogonalGeometryLowering.CurveSegmentsPerCommand;
        }
        int pointCapacity = (int)Math.Min(SvgOrthogonalGeometryLowering.MaximumFlattenedContourPoints, maximumPoints);
        int contourCapacity = Math.Min(pointCapacity, Commands.Length);
        int spanCapacity = Math.Max(4, Math.Min(pointCapacity, checked(2 * maximumSpans)));
        long scratchBytes = checked(512 + 2 * ParserCollectionMemory.GetArrayByteCount<SvgDevicePoint>(pointCapacity)
            + 4 * ParserCollectionMemory.GetArrayByteCount<int>(contourCapacity)
            + 3 * ParserCollectionMemory.GetArrayByteCount<SvgQuadSpan>(spanCapacity)
            + 3 * ParserCollectionMemory.GetArrayByteCount<SvgCubicSpan>(spanCapacity));
        using RenderBudget.BudgetReservation scratch = budget.Reserve(scratchBytes, SvgMemoryCategory.Geometry);
        SvgContourBuilder builder = SvgContourBuilder.CreateGrowing(pointCapacity, contourCapacity);
        builder.EnableQuadSpanRetention();
        builder.EnableCubicSpanRetention();
        SvgDevicePoint current = default;
        foreach (SvgAuthoredPathCommand command in Commands)
        {
            workCounter.Consume(cancellationToken);
            switch (command.Verb)
            {
                case SvgAuthoredPathVerb.Move: builder.BeginContour(command.First); current = command.First; break;
                case SvgAuthoredPathVerb.Line: builder.AddLine(command.First); current = command.First; break;
                case SvgAuthoredPathVerb.Quadratic: builder.AddQuadratic(command.First, command.Second, policy, cancellationToken); current = command.Second; break;
                case SvgAuthoredPathVerb.Conic: builder.AddConic(command.First, command.Second, (float)command.Third.X, 8, cancellationToken); current = command.Second; break;
                case SvgAuthoredPathVerb.Cubic: builder.AddCubic(command.First, command.Second, command.Third, policy, cancellationToken); current = command.Third; break;
                case SvgAuthoredPathVerb.Arc: AppendEndpointArc(builder, current, command, workCounter, cancellationToken); current = command.Third; break;
                case SvgAuthoredPathVerb.End:
                case SvgAuthoredPathVerb.Close: builder.EndContour(); break;
                default: throw new InvalidOperationException("Invalid retained path verb.");
            }
        }
        using BudgetedParserList<SvgDevicePoint> points = new(budget, SvgMemoryCategory.Surface, workCounter, cancellationToken);
        using BudgetedParserList<int> ends = new(budget, SvgMemoryCategory.Surface, workCounter, cancellationToken);
        using BudgetedParserList<SvgQuadSpan> quads = new(budget, SvgMemoryCategory.Surface, workCounter, cancellationToken);
        using BudgetedParserList<SvgCubicSpan> cubics = new(budget, SvgMemoryCategory.Surface, workCounter, cancellationToken);
        using BudgetedParserList<int> offsets = new(budget, SvgMemoryCategory.Geometry, workCounter, cancellationToken);
        using BudgetedParserList<int> contours = new(budget, SvgMemoryCategory.Geometry, workCounter, cancellationToken);
        for (int contour = 0; contour < builder.ContourCount; contour++)
        {
            ReadOnlySpan<SvgDevicePoint> vertices = builder.GetContour(contour);
            bool degenerate = vertices.Length < 3;
            offsets.Add(degenerate ? -1 : points.Count);
            contours.Add(degenerate ? -1 : ends.Count);
            if (degenerate) continue;
            foreach (SvgDevicePoint point in vertices) points.Add(mapCoordinates ? MapPoint(map, point, new(0, 0)) : point);
            ends.Add(points.Count);
        }
        foreach (SvgQuadSpan span in builder.GetQuadSpans())
            if (offsets[span.Contour] >= 0) quads.Add(span with { Contour = contours[span.Contour], StartIndex = span.StartIndex + offsets[span.Contour], EndIndex = span.EndIndex + offsets[span.Contour], Control = mapCoordinates ? MapPoint(map, span.Control, new(0, 0)) : span.Control });
        foreach (SvgCubicSpan span in builder.GetCubicSpans())
            if (offsets[span.Contour] >= 0) cubics.Add(span with { Contour = contours[span.Contour], StartIndex = span.StartIndex + offsets[span.Contour], EndIndex = span.EndIndex + offsets[span.Contour], FirstControl = mapCoordinates ? MapPoint(map, span.FirstControl, new(0, 0)) : span.FirstControl, SecondControl = mapCoordinates ? MapPoint(map, span.SecondControl, new(0, 0)) : span.SecondControl });
        return CreateContours(points, ends, quads, cubics, _commands.Items, isEvenOdd, budget, workCounter, cancellationToken);

        static void AppendEndpointArc(SvgContourBuilder builder, SvgDevicePoint start, SvgAuthoredPathCommand command, ParserWorkCounter workCounter, CancellationToken token)
        {
            // One normalization phase and at most three conic verbs, charged before numerical work.
            for (int phase = 0; phase <= SvgEndpointArc.MaximumConicCount; phase++) workCounter.Consume(token);
            Span<SvgEndpointConic> arcs = stackalloc SvgEndpointConic[SvgEndpointArc.MaximumConicCount];
            int count = SvgEndpointArc.Convert(start, command.First.X, command.First.Y, command.Second.X,
                (command.ArcFlags & SvgAuthoredArcFlags.Large) != 0, (command.ArcFlags & SvgAuthoredArcFlags.Sweep) != 0, command.Third, arcs, token);
            if (count == 0) builder.AddLine(command.Third);
            else for (int index = 0; index < count; index++)
                // Samples support fallback classification; the scanner consumes the retained conic.
                builder.AddConic(arcs[index].Control, arcs[index].End, arcs[index].Weight, 8, token);
        }

        static SvgPreparedPathContours CreateContours(BudgetedParserList<SvgDevicePoint> pointBuilder, BudgetedParserList<int> endBuilder, BudgetedParserList<SvgQuadSpan> quadBuilder, BudgetedParserList<SvgCubicSpan> cubicBuilder, SvgAuthoredPathCommand[] commands, bool isEvenOdd, RenderBudget budget, ParserWorkCounter workCounter, CancellationToken cancellationToken)
        {
            RenderBudget.BudgetReservation owner = budget.Reserve(128, SvgMemoryCategory.Surface);
            OwnedParserArray<SvgDevicePoint> points = new([], null);
            OwnedParserArray<int> ends = new([], null), signs = new([], null);
            OwnedParserArray<SvgQuadSpan> quads = new([], null);
            OwnedParserArray<SvgCubicSpan> cubics = new([], null);
            try
            {
                points = pointBuilder.Detach(); ends = endBuilder.Detach(); quads = quadBuilder.Detach(); cubics = cubicBuilder.Detach();
                if (isEvenOdd)
                {
                    using BudgetedParserList<int> signBuilder = new(budget, SvgMemoryCategory.Surface, workCounter, cancellationToken);
                    // The native walker applies parity to directed winding. Keep
                    // every contour, including zero-area self-crossing rings.
                    for (int contour = 0; contour < ends.Items.Length; contour++) signBuilder.Add(1);
                    signs = signBuilder.Detach();
                }
                return new(points, ends, quads, cubics, signs, commands, owner);
            }
            catch { points.Dispose(); ends.Dispose(); quads.Dispose(); cubics.Dispose(); signs.Dispose(); owner.Dispose(); throw; }
        }
    }

    /// <summary>Maps an authored float point after moving the translation into the software bitmap.</summary>
    /// <remarks>The caller charges the point mapping and keeps source storage alive. This bounded operation allocates nothing and preserves native float multiply/add order; returned coordinates are bitmap-local.</remarks>
    internal static SvgDevicePoint MapPoint(SvgAffineTransform map, SvgDevicePoint point, SvgCpuBitmapOrigin origin)
    {
        float x = (float)point.X, y = (float)point.Y;
        float tx = (float)map.E - origin.X, ty = (float)map.F - origin.Y;
        float mappedX = map.IsAxisAligned ? (float)map.A * x + tx : ((float)map.A * x + (float)map.C * y) + tx;
        float mappedY = map.IsAxisAligned ? (float)map.D * y + ty : ((float)map.B * x + (float)map.D * y) + ty;
        if (!float.IsFinite(mappedX) || !float.IsFinite(mappedY)) throw new SvgException(SvgErrorCode.LimitExceeded, "Authored path mapping exceeds the admitted numeric range.");
        return new(mappedX, mappedY);
    }

    /// <summary>Adopts command-array ownership only on success; the caller releases the array on failure.</summary>
    internal static SvgAuthoredPath Create(OwnedParserArray<SvgAuthoredPathCommand> commands, SvgAffineTransform sceneMap, RenderBudget budget)
        => Create(commands, sceneMap, null, budget);

    /// <summary>Adopts a source centerline only on success, with the same ownership rules as authored fills.</summary>
    internal static SvgAuthoredPath CreateStroke(OwnedParserArray<SvgAuthoredPathCommand> commands, SvgAffineTransform sceneMap, SvgAuthoredStrokeStyle strokeStyle, RenderBudget budget)
        => Create(commands, sceneMap, strokeStyle, budget);

    private static SvgAuthoredPath Create(OwnedParserArray<SvgAuthoredPathCommand> commands, SvgAffineTransform sceneMap, SvgAuthoredStrokeStyle? strokeStyle, RenderBudget budget)
    {
        RenderBudget.BudgetReservation owner = budget.Reserve(OwnerBytes, SvgMemoryCategory.Scene);
        try { return new SvgAuthoredPath(commands, sceneMap, strokeStyle, owner); }
        catch { owner.Dispose(); throw; }
    }

    /// <summary>Copies commands with bounded work and cancellation, composing an additional scene map.</summary>
    internal SvgAuthoredPath Copy(SvgAffineTransform placement, RenderBudget budget, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        using BudgetedParserList<SvgAuthoredPathCommand> commands = new(budget, SvgMemoryCategory.Scene, workCounter, cancellationToken);
        foreach (SvgAuthoredPathCommand command in Commands) commands.Add(command);
        OwnedParserArray<SvgAuthoredPathCommand> owned = commands.Detach();
        try
        {
            SvgAuthoredStrokeStyle? copiedStyle = StrokeStyle is SvgAuthoredStrokeStyle style
                ? style with { WidthMap = placement.ComposeChecked(style.WidthMap) } : null;
            return Create(owned, placement.ComposeChecked(SceneMap), copiedStyle, budget);
        }
        catch { owned.Dispose(); throw; }
    }

    public void Dispose()
    {
        _commands.Dispose();
        _commands = new([], null);
        _ownerReservation?.Dispose();
        _ownerReservation = null;
    }
}

/// <summary>Owns replayed surface arrays and borrows original immutable commands until disposal.</summary>
/// <remarks>Prepare returns output coordinates; PrepareSource retains source coordinates. Consumers
/// borrow immutable arrays and release this owner after all borrowed views. The scene command
/// owner must outlive this single-threaded prepared scope. The existing
/// 128-byte owner allowance covers the arrays' ownership slots, command borrow and object header.</remarks>
internal sealed class SvgPreparedPathContours : IDisposable
{
    private OwnedParserArray<SvgDevicePoint> _points;
    private OwnedParserArray<int> _ends;
    private OwnedParserArray<SvgQuadSpan> _quads;
    private OwnedParserArray<SvgCubicSpan> _cubics;
    private OwnedParserArray<int> _signs;
    private RenderBudget.BudgetReservation? _owner;
    private ReadOnlyMemory<SvgAuthoredPathCommand> _commands;

    internal SvgPreparedPathContours(OwnedParserArray<SvgDevicePoint> points, OwnedParserArray<int> ends, OwnedParserArray<SvgQuadSpan> quads, OwnedParserArray<SvgCubicSpan> cubics, OwnedParserArray<int> signs, SvgAuthoredPathCommand[] commands, RenderBudget.BudgetReservation owner)
    {
        _points = points; _ends = ends; _quads = quads; _cubics = cubics; _signs = signs; _owner = owner;
        _commands = commands;
    }

    internal SvgDevicePoint[] Points => _points.Items;
    internal int[] ContourEnds => _ends.Items;
    internal SvgQuadSpan[] QuadSpans => _quads.Items;
    internal SvgCubicSpan[] CubicSpans => _cubics.Items;
    internal int[]? Signs => _signs.Items.Length == 0 ? null : _signs.Items;
    /// <summary>Borrows original verbs from the scene owner, which must outlive this prepared value.</summary>
    internal ReadOnlySpan<SvgAuthoredPathCommand> Commands => _commands.Span;


    public void Dispose()
    {
        _points.Dispose(); _ends.Dispose(); _quads.Dispose(); _cubics.Dispose(); _signs.Dispose();
        _commands = default;
        _owner?.Dispose(); _owner = null;
    }
}
