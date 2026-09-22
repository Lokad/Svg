namespace Lokad.Svg.Parsing;

using Lokad.Svg.Fonts;
using Lokad.Svg.Rendering;
using System.Runtime.CompilerServices;

/// <summary>Represents the parsed root viewport dimensions before scene construction.</summary>
internal readonly record struct SvgRoot(double? Width, double? Height, SvgViewBox? ViewBox, SvgGeometry[] Geometries, SvgDrawCommand[] Commands, bool HasUnsupportedChild, long ParserWorkUnits, SvgSceneStorage? SceneStorage) : IDisposable
{
    /// <summary>Materializes rectangles only for parser characterization tests.</summary>
    internal SvgRectangle[] Rectangles => SvgGeometry.ToRectangles(Geometries);

    public void Dispose() => SceneStorage?.Dispose();
}

/// <summary>Represents the supported root viewBox-to-viewport mapping.</summary>
internal readonly record struct SvgViewBox(double X, double Y, double Width, double Height, SvgViewBoxAlignment HorizontalAlignment, SvgViewBoxAlignment VerticalAlignment, SvgViewBoxFit Fit);

internal enum SvgViewBoxAlignment { Min, Mid, Max, None }

internal enum SvgViewBoxFit { Meet, Slice, None }

internal readonly record struct SvgRectangle(double X, double Y, double Width, double Height, SvgColor Color, bool HasFill, SvgStroke? Stroke);

internal readonly record struct SvgStroke(SvgColor Color, int Width);

/// <summary>Represents the supported axis-aligned line before backend rasterization.</summary>
internal readonly record struct SvgAxisAlignedLine(double X1, double Y1, double X2, double Y2, SvgColor Color, bool IsPainted, int StrokeWidth)
{
    internal SvgRectangle ToStrokeBand()
    {
        double halfWidth = StrokeWidth / 2d;
        return X1 != X2
            ? new SvgRectangle(Math.Min(X1, X2), Y1 - halfWidth, Math.Abs(X2 - X1), StrokeWidth, Color, IsPainted, null)
            : new SvgRectangle(X1 - halfWidth, Math.Min(Y1, Y2), StrokeWidth, Math.Abs(Y2 - Y1), Color, IsPainted, null);
    }
}

internal enum SvgGeometryKind { Invalid, Rectangle, AxisAlignedLine, OrthogonalBand, GlyphContours, PatternFill, GradientFill }

/// <summary>Represents retained scene geometry independently from its raster fast path.</summary>
internal readonly struct SvgGeometry
{
    private readonly SvgGeometryKind _kind;
    private readonly SvgRectangle _rectangle;
    private readonly SvgAxisAlignedLine _axisAlignedLine;
    private readonly SvgGlyphGeometry? _glyph;
    private readonly SvgPatternGeometry? _pattern;
    private readonly SvgGradientGeometry? _gradient;

    private SvgGeometry(SvgGeometryKind kind, SvgRectangle rectangle)
    {
        _kind = kind;
        _rectangle = rectangle;
        _axisAlignedLine = default;
        _glyph = null;
        _pattern = null;
        _gradient = null;
    }

    private SvgGeometry(SvgAxisAlignedLine axisAlignedLine)
    {
        _kind = SvgGeometryKind.AxisAlignedLine;
        _rectangle = default;
        _axisAlignedLine = axisAlignedLine;
        _glyph = null;
        _pattern = null;
        _gradient = null;
    }

    private SvgGeometry(SvgGlyphGeometry glyph)
    {
        ArgumentNullException.ThrowIfNull(glyph);
        _kind = SvgGeometryKind.GlyphContours;
        _rectangle = default;
        _axisAlignedLine = default;
        _glyph = glyph;
        _pattern = null;
        _gradient = null;
    }

    private SvgGeometry(SvgPatternGeometry pattern)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        _kind = SvgGeometryKind.PatternFill;
        _rectangle = default;
        _axisAlignedLine = default;
        _glyph = null;
        _pattern = pattern;
    }

    private SvgGeometry(SvgGradientGeometry gradient)
    {
        ArgumentNullException.ThrowIfNull(gradient);
        _kind = SvgGeometryKind.GradientFill;
        _rectangle = default;
        _axisAlignedLine = default;
        _glyph = null;
        _pattern = null;
        _gradient = gradient;
    }

    internal SvgGeometryKind Kind => _kind;

    internal SvgRectangle Rectangle
        => _kind is SvgGeometryKind.Rectangle or SvgGeometryKind.OrthogonalBand
            ? _rectangle
            : throw new InvalidOperationException("This SVG geometry does not contain a rectangle.");

    internal SvgAxisAlignedLine AxisAlignedLine
        => _kind == SvgGeometryKind.AxisAlignedLine
            ? _axisAlignedLine
            : throw new InvalidOperationException("This SVG geometry does not contain an axis-aligned line.");

    internal SvgGlyphGeometry Glyph
        => _kind == SvgGeometryKind.GlyphContours
            ? _glyph ?? throw new InvalidOperationException("This SVG geometry does not contain glyph contours.")
            : throw new InvalidOperationException("This SVG geometry does not contain glyph contours.");

    internal static SvgGeometry FromRectangle(SvgRectangle rectangle) => new(SvgGeometryKind.Rectangle, rectangle);

    internal static SvgGeometry FromAxisAlignedLine(SvgAxisAlignedLine line) => new(line);

    internal static SvgGeometry FromOrthogonalBand(SvgRectangle band) => new(SvgGeometryKind.OrthogonalBand, band);

    internal static SvgGeometry FromGlyphContours(SvgGlyphGeometry glyph) => new(glyph);

    internal static SvgGeometry FromPatternFill(SvgPatternGeometry pattern) => new(pattern);

    internal static SvgGeometry FromGradientFill(SvgGradientGeometry gradient) => new(gradient);

    internal SvgGradientGeometry Gradient
        => _kind == SvgGeometryKind.GradientFill
            ? _gradient ?? throw new InvalidOperationException("This SVG geometry does not contain gradient stops.")
            : throw new InvalidOperationException("This SVG geometry does not contain gradient stops.");

    internal SvgPatternGeometry Pattern
        => _kind == SvgGeometryKind.PatternFill
            ? _pattern ?? throw new InvalidOperationException("This SVG geometry does not contain pattern tile content.")
            : throw new InvalidOperationException("This SVG geometry does not contain pattern tile content.");

    internal void Dispose()
    {
        if (_kind == SvgGeometryKind.GlyphContours) _glyph?.Dispose();
        if (_kind == SvgGeometryKind.PatternFill) _pattern?.Dispose();
        if (_kind == SvgGeometryKind.GradientFill) _gradient?.Dispose();
    }

    internal static SvgRectangle[] ToRectangles(ReadOnlySpan<SvgGeometry> geometries)
    {
        SvgRectangle[] rectangles = new SvgRectangle[geometries.Length];
        for (int index = 0; index < geometries.Length; index++)
        {
            rectangles[index] = geometries[index].Kind switch
            {
                SvgGeometryKind.Rectangle => geometries[index].Rectangle,
                SvgGeometryKind.AxisAlignedLine => geometries[index].AxisAlignedLine.ToStrokeBand(),
                SvgGeometryKind.OrthogonalBand => geometries[index].Rectangle,
                _ => throw new SvgException(SvgErrorCode.MalformedInput, "SVG scene contains unsupported geometry.")
            };
        }
        return rectangles;
    }
}

/// <summary>Owns retained glyph contour arrays and their scene-memory reservation until the scene is disposed.</summary>
internal sealed class SvgGlyphGeometry : IDisposable
{
    private RenderBudget.BudgetReservation? _reservation;

    internal SvgGlyphGeometry(SvgGlyphQuadraticContour[] contours, SvgColor color) : this(contours, color, null)
    {
        ArgumentNullException.ThrowIfNull(contours);
    }

    private SvgGlyphGeometry(SvgGlyphQuadraticContour[] contours, SvgColor color, RenderBudget.BudgetReservation? reservation)
    {
        Contours = contours;
        Color = color;
        _reservation = reservation;
    }

    internal SvgGlyphQuadraticContour[] Contours { get; }

    internal SvgColor Color { get; }

    internal static SvgGlyphGeometry Create(SvgGlyphContourStorage contours, SvgColor color)
    {
        ArgumentNullException.ThrowIfNull(contours);
        SvgGlyphContourTransfer transfer = contours.TransferOwnership();
        try { return new SvgGlyphGeometry(transfer.Contours, color, transfer.Reservation); }
        catch
        {
            transfer.Reservation?.Dispose();
            throw;
        }
    }

    /// <summary>Reserves scene ownership for contours supplied by a non-rendering caller.</summary>
    internal static SvgGlyphGeometry Create(SvgGlyphQuadraticContour[] contours, SvgColor color, RenderBudget budget)
    {
        ArgumentNullException.ThrowIfNull(contours);
        ArgumentNullException.ThrowIfNull(budget);
        int segmentCount = 0;
        try
        {
            foreach (SvgGlyphQuadraticContour contour in contours)
            {
                ArgumentNullException.ThrowIfNull(contour.Segments);
                segmentCount = checked(segmentCount + contour.Segments.Length);
            }
        }
        catch (OverflowException)
        {
            throw new SvgException(SvgErrorCode.LimitExceeded, "Glyph contour storage exceeds the supported accounting range.");
        }
        RenderBudget.BudgetReservation reservation = budget.Reserve(GetStorageByteCount(contours.Length, segmentCount), SvgMemoryCategory.Scene);
        return new SvgGlyphGeometry(contours, color, reservation);
    }

    internal static long GetStorageByteCount(int contourCount, int segmentCount)
    {
        try
        {
            return checked(32L + (long)contourCount * Unsafe.SizeOf<SvgGlyphQuadraticContour>() + (long)contourCount * 32L + (long)segmentCount * Unsafe.SizeOf<SvgGlyphQuadraticSegment>());
        }
        catch (OverflowException)
        {
            throw new SvgException(SvgErrorCode.LimitExceeded, "Glyph contour storage exceeds the supported accounting range.");
        }
    }

    public void Dispose()
    {
        _reservation?.Dispose();
        _reservation = null;
    }
}

/// <summary>Owns retained pattern tile content with its integer grid and fill region.</summary>
internal sealed class SvgPatternGeometry : IDisposable
{
    internal SvgPatternGeometry(SvgRectangle Content, int OriginX, int OriginY, int TileWidth, int TileHeight, SvgRectangle Fill, byte Opacity, SvgStroke? Stroke)
    {
        this.Content = Content;
        this.OriginX = OriginX;
        this.OriginY = OriginY;
        this.TileWidth = TileWidth;
        this.TileHeight = TileHeight;
        this.Fill = Fill;
        this.Opacity = Opacity;
        this.Stroke = Stroke;
    }

    internal SvgRectangle Content { get; }

    internal int OriginX { get; }

    internal int OriginY { get; }

    internal int TileWidth { get; }

    internal int TileHeight { get; }

    internal SvgRectangle Fill { get; }

    internal byte Opacity { get; }

    internal SvgStroke? Stroke { get; }

    internal SvgPatternGeometry ClipTo(SvgClipBox box)
        => new(Content, OriginX, OriginY, TileWidth, TileHeight, box.ClipRectangle(Fill), Opacity, Stroke);

    internal SvgPatternGeometry Stamped(byte value)
        => new(Content, OriginX, OriginY, TileWidth, TileHeight, Fill, SvgReferenceResolver.MultiplyAlpha(Opacity, value), Stroke is null ? null : new SvgStroke(new SvgColor(Stroke.Value.Color.Red, Stroke.Value.Color.Green, Stroke.Value.Color.Blue, SvgReferenceResolver.MultiplyAlpha(Stroke.Value.Color.Alpha, value)), Stroke.Value.Width));

    public void Dispose()
    {
    }
}

/// <summary>Owns retained linear-gradient stops with their bounding-box vector and fill region.</summary>
internal sealed class SvgGradientGeometry : IDisposable
{
    internal SvgGradientGeometry(SvgLinearGradient Gradient, SvgRectangle Box, SvgRectangle Fill, byte Opacity, SvgStroke? Stroke)
    {
        this.Gradient = Gradient;
        this.Box = Box;
        this.Fill = Fill;
        this.Opacity = Opacity;
        this.Stroke = Stroke;
    }

    internal SvgLinearGradient Gradient { get; }

    internal SvgRectangle Box { get; }

    internal SvgRectangle Fill { get; }

    internal byte Opacity { get; }

    internal SvgStroke? Stroke { get; }

    internal SvgGradientGeometry ClipTo(SvgClipBox box)
        => new(Gradient, Box, box.ClipRectangle(Fill), Opacity, Stroke);

    internal SvgGradientGeometry Stamped(byte value)
        => new(Gradient, Box, Fill, SvgReferenceResolver.MultiplyAlpha(Opacity, value), Stroke is null ? null : new SvgStroke(new SvgColor(Stroke.Value.Color.Red, Stroke.Value.Color.Green, Stroke.Value.Color.Blue, SvgReferenceResolver.MultiplyAlpha(Stroke.Value.Color.Alpha, value)), Stroke.Value.Width));

    public void Dispose()
    {
    }
}

/// <summary>Retains one local definition's typed geometry and the narrow instance-style override capability.</summary>
internal readonly record struct SvgDefinition(SvgGeometry Geometry, bool AllowsInstanceFillOverride, SvgViewBox? SymbolViewBox);

internal enum SvgDrawCommandKind { Geometry, BeginGroup, EndGroup }

/// <summary>References retained scene geometry or marks an opacity-scope boundary.</summary>
internal readonly record struct SvgDrawCommand(SvgDrawCommandKind Kind, int GeometryIndex, byte Opacity);

internal readonly record struct SvgPoint(int X, int Y);

internal sealed class SvgSceneStorage : IDisposable
{
    private readonly RenderBudget.BudgetReservation? _geometryReservation;
    private readonly RenderBudget.BudgetReservation? _commandReservation;

    private SvgSceneStorage(SvgGeometry[] geometries, RenderBudget.BudgetReservation? geometryReservation, SvgDrawCommand[] commands, RenderBudget.BudgetReservation? commandReservation)
    {
        Geometries = geometries;
        Commands = commands;
        _geometryReservation = geometryReservation;
        _commandReservation = commandReservation;
    }

    internal SvgGeometry[] Geometries { get; }

    /// <summary>Materializes rectangles only for parser characterization tests.</summary>
    internal SvgRectangle[] Rectangles => SvgGeometry.ToRectangles(Geometries);

    internal SvgDrawCommand[] Commands { get; }

    internal static SvgSceneStorage Create(BudgetedSceneList<SvgGeometry> geometries, BudgetedSceneList<SvgDrawCommand> commands)
    {
        OwnedSceneArray<SvgGeometry> ownedGeometries = geometries.Detach();
        try
        {
            OwnedSceneArray<SvgDrawCommand> ownedCommands = commands.Detach();
            return new SvgSceneStorage(ownedGeometries.Items, ownedGeometries.Reservation, ownedCommands.Items, ownedCommands.Reservation);
        }
        catch
        {
            foreach (SvgGeometry geometry in ownedGeometries.Items) geometry.Dispose();
            ownedGeometries.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        foreach (SvgGeometry geometry in Geometries) geometry.Dispose();
        _geometryReservation?.Dispose();
        _commandReservation?.Dispose();
    }
}

/// <summary>Owns the mutable, budgeted scene construction phase until arrays transfer to scene storage.</summary>
internal sealed class SvgSceneBuilder : IDisposable
{
    private readonly BudgetedSceneList<SvgGeometry> _geometries;
    private readonly BudgetedSceneList<SvgDrawCommand> _commands;
    private bool _built;

    internal SvgSceneBuilder(RenderBudget budget, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        _geometries = new(budget, workCounter, cancellationToken);
        _commands = new(budget, workCounter, cancellationToken);
    }

    internal void AddGeometry(SvgGeometry geometry)
    {
        int geometryIndex = _geometries.Count;
        _geometries.Add(geometry);
        _commands.Add(new SvgDrawCommand(SvgDrawCommandKind.Geometry, geometryIndex, 255));
    }

    internal void AddGeometries(ReadOnlySpan<SvgGeometry> geometries)
    {
        int firstGeometryIndex = _geometries.Count;
        _geometries.AddRange(geometries);
        for (int index = 0; index < geometries.Length; index++) _commands.Add(new SvgDrawCommand(SvgDrawCommandKind.Geometry, firstGeometryIndex + index, 255));
    }

    internal SvgSceneSlot AddReferencePlaceholder()
    {
        SvgSceneSlot slot = new(_geometries.Count, _commands.Count);
        _geometries.Add(default);
        _commands.Add(new SvgDrawCommand(SvgDrawCommandKind.Geometry, slot.GeometryIndex, 255));
        return slot;
    }

    internal void Replace(SvgSceneSlot slot, SvgGeometry geometry)
    {
        _geometries[slot.GeometryIndex] = geometry;
        _commands[slot.CommandIndex] = new SvgDrawCommand(SvgDrawCommandKind.Geometry, slot.GeometryIndex, 255);
    }

    internal void BeginOpacityScope(byte opacity) => _commands.Add(new SvgDrawCommand(SvgDrawCommandKind.BeginGroup, -1, opacity));

    internal void EndOpacityScope() => _commands.Add(new SvgDrawCommand(SvgDrawCommandKind.EndGroup, -1, 255));

    internal SvgSceneStorage Build()
    {
        if (_built) throw new InvalidOperationException("The SVG scene has already been built.");
        _built = true;
        return SvgSceneStorage.Create(_geometries, _commands);
    }

    public void Dispose()
    {
        foreach (SvgGeometry geometry in _geometries.Items) geometry.Dispose();
        _geometries.Dispose();
        _commands.Dispose();
    }
}

/// <summary>Identifies the parallel retained-scene entries reserved for a deferred reference.</summary>
internal readonly record struct SvgSceneSlot(int GeometryIndex, int CommandIndex);

internal sealed class BudgetedSceneList<T> : IDisposable where T : struct
{
    private const int CopyCheckpointItems = 4_096;
    private readonly RenderBudget _budget;
    private readonly ParserWorkCounter _workCounter;
    private readonly CancellationToken _cancellationToken;
    private T[] _items = [];
    private RenderBudget.BudgetReservation? _reservation;
    private int _count;

    internal BudgetedSceneList(RenderBudget budget, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        _budget = budget;
        _workCounter = workCounter;
        _cancellationToken = cancellationToken;
    }

    internal int Count => _count;

    internal ReadOnlySpan<T> Items => _items.AsSpan(0, _count);

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
        long bytes = ParserCollectionMemory.GetArrayByteCount<T>(_count);
        RenderBudget.BudgetReservation reservation = _budget.Reserve(bytes, SvgMemoryCategory.Scene);
        T[] exact;
        try { exact = new T[_count]; }
        catch { reservation.Dispose(); throw; }
        try { CopyWithCheckpoints(_items.AsSpan(0, _count), exact); }
        catch { reservation.Dispose(); throw; }
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
        long bytes = ParserCollectionMemory.GetArrayByteCount<T>(capacity);
        RenderBudget.BudgetReservation replacementReservation = _budget.Reserve(bytes, SvgMemoryCategory.Scene);
        T[] replacement;
        try { replacement = new T[capacity]; }
        catch { replacementReservation.Dispose(); throw; }
        try { CopyWithCheckpoints(_items.AsSpan(0, _count), replacement); }
        catch { replacementReservation.Dispose(); throw; }
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
