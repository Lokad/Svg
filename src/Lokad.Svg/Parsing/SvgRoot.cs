namespace Lokad.Svg.Parsing;

using Lokad.Svg.Rendering;

/// <summary>Represents the parsed root viewport dimensions before scene construction.</summary>
internal readonly record struct SvgRoot(double? Width, double? Height, SvgViewBox? ViewBox, SvgRectangle[] Rectangles, SvgDrawCommand[] Commands, bool HasUnsupportedChild, long ParserWorkUnits, SvgSceneStorage? SceneStorage) : IDisposable
{
    public void Dispose() => SceneStorage?.Dispose();
}

/// <summary>Represents the supported root viewBox-to-viewport mapping.</summary>
internal readonly record struct SvgViewBox(double X, double Y, double Width, double Height, SvgViewBoxAlignment HorizontalAlignment, SvgViewBoxAlignment VerticalAlignment, SvgViewBoxFit Fit);

internal enum SvgViewBoxAlignment { Min, Mid, Max, None }

internal enum SvgViewBoxFit { Meet, Slice, None }

internal readonly record struct SvgRectangle(double X, double Y, double Width, double Height, SvgColor Color, bool HasFill, SvgStroke? Stroke);

internal readonly record struct SvgStroke(SvgColor Color, int Width);

internal enum SvgDrawCommandKind { Rectangle, BeginGroup, EndGroup }

internal readonly record struct SvgDrawCommand(SvgDrawCommandKind Kind, SvgRectangle Rectangle, byte Opacity);

internal readonly record struct SvgPoint(int X, int Y);

internal static class SvgSceneMemory
{
    private const int ArrayHeaderBytes = 32;
    private const int RectangleBytes = 56;
    private const int DrawCommandBytes = 72;

    internal static long GetArrayByteCount(SvgSceneArrayKind kind, int count)
        => count == 0 ? 0 : checked(ArrayHeaderBytes + (long)count * (kind == SvgSceneArrayKind.Rectangle ? RectangleBytes : DrawCommandBytes));
}

internal enum SvgSceneArrayKind { Rectangle, DrawCommand }

internal sealed class SvgSceneStorage : IDisposable
{
    private readonly RenderBudget.BudgetReservation? _rectangleReservation;
    private readonly RenderBudget.BudgetReservation? _commandReservation;

    private SvgSceneStorage(SvgRectangle[] rectangles, RenderBudget.BudgetReservation? rectangleReservation, SvgDrawCommand[] commands, RenderBudget.BudgetReservation? commandReservation)
    {
        Rectangles = rectangles;
        Commands = commands;
        _rectangleReservation = rectangleReservation;
        _commandReservation = commandReservation;
    }

    internal SvgRectangle[] Rectangles { get; }

    internal SvgDrawCommand[] Commands { get; }

    internal static SvgSceneStorage Create(BudgetedSceneList<SvgRectangle> rectangles, BudgetedSceneList<SvgDrawCommand> commands)
    {
        OwnedSceneArray<SvgRectangle> ownedRectangles = rectangles.Detach();
        try
        {
            OwnedSceneArray<SvgDrawCommand> ownedCommands = commands.Detach();
            return new SvgSceneStorage(ownedRectangles.Items, ownedRectangles.Reservation, ownedCommands.Items, ownedCommands.Reservation);
        }
        catch
        {
            ownedRectangles.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        _rectangleReservation?.Dispose();
        _commandReservation?.Dispose();
    }
}

/// <summary>Owns the mutable, budgeted scene construction phase until arrays transfer to scene storage.</summary>
internal sealed class SvgSceneBuilder : IDisposable
{
    private readonly BudgetedSceneList<SvgRectangle> _rectangles;
    private readonly BudgetedSceneList<SvgDrawCommand> _commands;
    private bool _built;

    internal SvgSceneBuilder(RenderBudget budget, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        _rectangles = new(budget, SvgSceneArrayKind.Rectangle, workCounter, cancellationToken);
        _commands = new(budget, SvgSceneArrayKind.DrawCommand, workCounter, cancellationToken);
    }

    internal void AddRectangle(SvgRectangle rectangle)
    {
        _rectangles.Add(rectangle);
        _commands.Add(new SvgDrawCommand(SvgDrawCommandKind.Rectangle, rectangle, 255));
    }

    internal void AddRectangles(ReadOnlySpan<SvgRectangle> rectangles)
    {
        _rectangles.AddRange(rectangles);
        foreach (SvgRectangle rectangle in rectangles) _commands.Add(new SvgDrawCommand(SvgDrawCommandKind.Rectangle, rectangle, 255));
    }

    internal SvgSceneSlot AddReferencePlaceholder()
    {
        SvgSceneSlot slot = new(_rectangles.Count, _commands.Count);
        _rectangles.Add(default);
        _commands.Add(new SvgDrawCommand(SvgDrawCommandKind.Rectangle, default, 255));
        return slot;
    }

    internal void Replace(SvgSceneSlot slot, SvgRectangle rectangle)
    {
        _rectangles[slot.RectangleIndex] = rectangle;
        _commands[slot.CommandIndex] = new SvgDrawCommand(SvgDrawCommandKind.Rectangle, rectangle, 255);
    }

    internal void BeginOpacityScope(byte opacity) => _commands.Add(new SvgDrawCommand(SvgDrawCommandKind.BeginGroup, default, opacity));

    internal void EndOpacityScope() => _commands.Add(new SvgDrawCommand(SvgDrawCommandKind.EndGroup, default, 255));

    internal SvgSceneStorage Build()
    {
        if (_built) throw new InvalidOperationException("The SVG scene has already been built.");
        _built = true;
        return SvgSceneStorage.Create(_rectangles, _commands);
    }

    public void Dispose()
    {
        _rectangles.Dispose();
        _commands.Dispose();
    }
}

/// <summary>Identifies the parallel retained-scene entries reserved for a deferred reference.</summary>
internal readonly record struct SvgSceneSlot(int RectangleIndex, int CommandIndex);

internal sealed class BudgetedSceneList<T> : IDisposable where T : struct
{
    private const int CopyCheckpointItems = 4_096;
    private readonly RenderBudget _budget;
    private readonly SvgSceneArrayKind _kind;
    private readonly ParserWorkCounter _workCounter;
    private readonly CancellationToken _cancellationToken;
    private T[] _items = [];
    private RenderBudget.BudgetReservation? _reservation;
    private int _count;

    internal BudgetedSceneList(RenderBudget budget, SvgSceneArrayKind kind, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        _budget = budget;
        _kind = kind;
        _workCounter = workCounter;
        _cancellationToken = cancellationToken;
    }

    internal int Count => _count;

    internal T this[int index]
    {
        get => _items[index];
        set => _items[index] = value;
    }

    internal void Add(T item)
    {
        EnsureAdditionalCapacity(1);
        _items[_count++] = item;
    }

    internal void AddRange(ReadOnlySpan<T> items)
    {
        EnsureAdditionalCapacity(items.Length);
        CopyWithCheckpoints(items, _items.AsSpan(_count));
        _count += items.Length;
    }

    internal OwnedSceneArray<T> Detach()
    {
        if (_count == 0)
        {
            Dispose();
            return new([], null);
        }
        long bytes = SvgSceneMemory.GetArrayByteCount(_kind, _count);
        RenderBudget.BudgetReservation reservation = _budget.Reserve(bytes, SvgMemoryCategory.Scene);
        T[] exact;
        try { exact = new T[_count]; }
        catch { reservation.Dispose(); throw; }
        CopyWithCheckpoints(_items.AsSpan(0, _count), exact);
        Dispose();
        return new OwnedSceneArray<T>(exact, reservation);
    }

    public void Dispose()
    {
        _reservation?.Dispose();
        _reservation = null;
        _items = [];
        _count = 0;
    }

    private void EnsureAdditionalCapacity(int additionalCount)
    {
        if (additionalCount == 0) return;
        int requiredCount;
        try { requiredCount = checked(_count + additionalCount); }
        catch (OverflowException) { throw new SvgException(SvgErrorCode.LimitExceeded, "The SVG scene exceeds the supported array capacity."); }
        if (requiredCount <= _items.Length) return;
        int capacity = _items.Length == 0 ? Math.Max(4, requiredCount) : _items.Length > int.MaxValue / 2 ? requiredCount : Math.Max(_items.Length * 2, requiredCount);
        long bytes = SvgSceneMemory.GetArrayByteCount(_kind, capacity);
        RenderBudget.BudgetReservation replacementReservation = _budget.Reserve(bytes, SvgMemoryCategory.Scene);
        T[] replacement;
        try { replacement = new T[capacity]; }
        catch { replacementReservation.Dispose(); throw; }
        CopyWithCheckpoints(_items.AsSpan(0, _count), replacement);
        _reservation?.Dispose();
        _reservation = replacementReservation;
        _items = replacement;
    }

    private void CopyWithCheckpoints(ReadOnlySpan<T> source, Span<T> destination)
    {
        for (int offset = 0; offset < source.Length; offset += CopyCheckpointItems)
        {
            _workCounter.Consume(_cancellationToken);
            int count = Math.Min(CopyCheckpointItems, source.Length - offset);
            source.Slice(offset, count).CopyTo(destination.Slice(offset, count));
        }
    }
}

internal readonly record struct OwnedSceneArray<T>(T[] Items, RenderBudget.BudgetReservation? Reservation) where T : struct
{
    internal void Dispose() => Reservation?.Dispose();
}
