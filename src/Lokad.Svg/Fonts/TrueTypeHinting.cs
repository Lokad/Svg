namespace Lokad.Svg.Fonts;

using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

internal enum TrueTypeHintZone { Twilight, Glyph }
internal enum TrueTypeHintAxis { Y, X }
internal enum TrueTypeHintProgramKind { Font, Preparation, Glyph }
internal enum TrueTypeHintRound { Grid, HalfGrid, DoubleGrid, Down, Up, Off, Super }
internal enum TrueTypeHintControlScale { FullPrecision, FractionalFontUnits }
internal enum TrueTypeHintPreparation { Single, DefaultThenTarget }
internal enum TrueTypeHintGrid { Physical, ClearType }

/// <summary>Explicit native GETINFO result bits, not a claim that every queried feature is implemented.</summary>
[Flags]
internal enum TrueTypeHintQueryFlags
{
    None = 0,
    Rotated = 1 << 8,
    Stretched = 1 << 9,
    FontVariations = 1 << 10,
    VerticalPhantoms = 1 << 11,
    Grayscale = 1 << 12,
    ClearType = 1 << 13,
    CompatibleWidths = 1 << 14,
    HorizontalStripes = 1 << 15,
    Bgr = 1 << 16,
    SubpixelPositioned = 1 << 17,
    Symmetric = 1 << 18,
    ClearTypeGrayscale = 1 << 19,
    All = 0xFFF00
}

/// <summary>Declares bounded interpreter storage from a validated TrueType maxp table.</summary>
internal readonly record struct TrueTypeHintProfile(int Stack, int Storage, int Functions, int Twilight);

/// <summary>Retains independently rounded horizontal and vertical 16.16 font-to-device scales.</summary>
/// <remarks>Immutable and storage-free. Axial distances select their own scale; neither scale is derived from the rounded other axis.</remarks>
internal readonly record struct TrueTypeHintDeviceScale(int Vertical, int Horizontal);

/// <summary>Supplies explicit rasterizer queries without acquiring a native rasterizer.</summary>
internal readonly record struct TrueTypeHintTarget(int Version, TrueTypeHintQueryFlags QueryFlags, TrueTypeHintControlScale ControlScale, TrueTypeHintPreparation Preparation)
{
    /// <summary>Positive X/Y device-size ratio; immutable and storage-free, preserved across preparation and component calls.</summary>
    internal double HorizontalRatio { get; init; } = 1;
    /// <summary>Selects physical or ClearType horizontal instruction rounding independently of GETINFO queries.</summary>
    internal TrueTypeHintGrid Grid { get; init; }
    /// <summary>Selects Windows numerical rules independently of symmetric-smoothing GETINFO queries.</summary>
    internal bool WindowsArithmetic { get; init; }
    internal bool UsesWindowsArithmetic => WindowsArithmetic || (QueryFlags & TrueTypeHintQueryFlags.Symmetric) != 0;
}

internal struct TrueTypeHintPoint
{
    internal int X, Y, OriginalX, OriginalY, UnscaledX, UnscaledY;
    internal bool TouchedX, TouchedY, Unavailable, IsOnCurve;
    internal TrueTypeHintPoint(int x, int y, int unscaledX, int unscaledY) { X = OriginalX = x; Y = OriginalY = y; UnscaledX = unscaledX; UnscaledY = unscaledY; TouchedX = TouchedY = Unavailable = false; IsOnCurve = true; }
}

internal readonly record struct TrueTypeHintFunction(bool Defined, ReadOnlyMemory<byte> Program);

/// <summary>Retains explicit super-round distances in physical 26.6 device coordinates.</summary>
internal readonly record struct TrueTypeHintSuperRound(int Period, int Phase, int Threshold);

/// <summary>Executes the bounded TrueType subset needed by the selected public Latin fonts.</summary>
/// <remarks>Owns scratch and borrows programs, glyph points and contour ends for one conversion.
/// Every visited instruction, skipped operand and point consumes work and observes cancellation.
/// Malformed stacks/ranges are categorized; unsupported executed instructions remain explicit.
/// A machine and its caller-owned inputs belong to one thread and one glyph; disposal releases
/// its reservation and function-program borrows. It does not select fonts or draw pixels.
/// Fixed-point arithmetic and interpolation adapt FreeType 2.13.2; see THIRD_PARTY_NOTICES.txt.
/// Target queries and preparation lifecycle are explicit native policies. Per-glyph
/// geometry and mask acceptance belong to the independent visual controls.</remarks>
internal sealed partial class TrueTypeHinting : IDisposable
{
    private RenderBudget.BudgetReservation? _reservation;
    private readonly int[] _stack, _storage, _cvt, _initialCvt;
    private readonly TrueTypeHintFunction[] _functions;
    private readonly TrueTypeHintPoint[] _twilight;
    private TrueTypeHintPoint[] _glyph;
    private ushort[] _ends;
    private bool _compositeGlyph;
    private readonly ParserWorkCounter _work;
    private readonly CancellationToken _token;
    private TrueTypeHintTarget _target;
    private readonly int _ppem;
    private readonly TrueTypeHintDeviceScale _scale;
    private int AxisPpem => ScaleRatio(_ppem, ProjectionRatio);
    private int _count, _depth, _loop = 1, _rp0, _rp1, _rp2;
    private int _deltaBase = 9, _deltaShift = 3, _cutIn = 68, _instructionControl;
    private int _minimumDistance = 64, _singleWidthCutIn, _singleWidth;
    private int _scanControl, _scanType;
    private bool _scanConfigured, _autoFlip = true, _iupX, _iupY;
    private TrueTypeHintRound _round = TrueTypeHintRound.Grid;
    private TrueTypeHintSuperRound _superRound = new(64, 0, 32);
    private TrueTypeHintZone _zp0 = TrueTypeHintZone.Glyph, _zp1 = TrueTypeHintZone.Glyph, _zp2 = TrueTypeHintZone.Glyph;

    private TrueTypeHinting(TrueTypeHintProfile profile, int[] cvt, TrueTypeHintPoint[] glyph, ushort[] ends, int ppem, TrueTypeHintDeviceScale scale, TrueTypeHintTarget target, RenderBudget.BudgetReservation reservation, ParserWorkCounter work, CancellationToken token)
    {
        _stack = new int[profile.Stack]; _storage = new int[profile.Storage];
        _functions = new TrueTypeHintFunction[profile.Functions]; _twilight = new TrueTypeHintPoint[profile.Twilight];
        _cvt = new int[cvt.Length]; _initialCvt = new int[cvt.Length];
        for (int i = 0; i < cvt.Length; i++) { work.Consume(token); _cvt[i] = _initialCvt[i] = cvt[i]; }
        _glyph = glyph; _ends = ends; _ppem = ppem; _scale = scale; _target = target;
        _reservation = reservation; _work = work; _token = token;
    }

    internal static TrueTypeHinting Create(TrueTypeHintProfile profile, int[] cvt, TrueTypeHintPoint[] glyph, ushort[] ends, int ppem, TrueTypeHintDeviceScale scale, TrueTypeHintTarget target, RenderBudget budget, ParserWorkCounter work, CancellationToken token)
    {
        work.Consume(token);
        if (profile.Stack is < 0 or > 4096 || profile.Storage is < 0 or > 4096 || profile.Functions is < 0 or > 1024 || profile.Twilight is < 0 or > 4096 || cvt.Length > 4096 || glyph.Length > 32772)
            throw new SvgException(SvgErrorCode.LimitExceeded, "TrueType interpreter storage exceeds the supported bounds.");
        if (ppem is < 0 or > 32767 || scale.Vertical < 0 || scale.Horizontal < 0) throw Invalid("The TrueType device scale is invalid.");
        ValidateTarget(target);
        int previous = -1;
        foreach (ushort end in ends)
        {
            work.Consume(token);
            if (end <= previous || end >= glyph.Length) throw Invalid("TrueType contour ends are outside their ordered point range.");
            previous = end;
        }
        long bytes = 512 + ParserCollectionMemory.GetArrayByteCount<int>(profile.Stack)
            + ParserCollectionMemory.GetArrayByteCount<int>(profile.Storage) + 2 * ParserCollectionMemory.GetArrayByteCount<int>(cvt.Length)
            + ParserCollectionMemory.GetArrayByteCount<TrueTypeHintFunction>(profile.Functions) + ParserCollectionMemory.GetArrayByteCount<TrueTypeHintPoint>(profile.Twilight);
        RenderBudget.BudgetReservation reservation = budget.Reserve(bytes, SvgMemoryCategory.Geometry);
        try { return new(profile, cvt, glyph, ends, ppem, scale, target, reservation, work, token); }
        catch { reservation.Dispose(); throw; }
    }

    internal ReadOnlySpan<int> ControlValues => _reservation is not null ? _cvt : throw new InvalidOperationException("The TrueType interpreter is disposed.");
    internal ReadOnlySpan<int> Storage => _reservation is not null ? _storage : throw new InvalidOperationException("The TrueType interpreter is disposed.");
    internal int DefinedFunctions { get; private set; }

    /// <summary>Gets the completed glyph's enabled smart dropout policy, including stubs; contour consumers choose their own rasterizer.</summary>
    internal bool GlyphSmartDropouts { get; private set; }
    internal bool GlyphExcludeDropoutStubs { get; private set; }

    /// <summary>Fits a borrowed child or assembled composite, retaining font/preparation CVT, storage and functions between components.</summary>
    /// <remarks>Glyph graphics state is restored after each call. Points/endpoints remain caller-owned and are unbound on every exit. Composite originals are already device coordinates and must not be scaled again. This operation shares the machine's single-threaded work, cancellation and bounded storage contract.</remarks>
    internal void HintGlyph(ReadOnlyMemory<byte> program, TrueTypeHintPoint[] points, ushort[] ends, bool composite)
    {
        if (_reservation is null) throw new InvalidOperationException("The TrueType interpreter is disposed.");
        _work.Consume(_token);
        if (points.Length < 4) throw Invalid("TrueType glyph points omit their phantom slots.");
        if (points.Length > 32772) throw new SvgException(SvgErrorCode.LimitExceeded, "TrueType glyph points exceed the supported bound.");
        int previous = -1;
        foreach (ushort end in ends)
        {
            _work.Consume(_token);
            if (end <= previous || end >= points.Length - 4) throw Invalid("TrueType contour ends are outside their ordered outline point range.");
            previous = end;
        }
        if (previous + 1 != points.Length - 4) throw Invalid("TrueType contour ends do not cover the outline points.");
        TrueTypeHintPoint[] savedPoints = _glyph;
        ushort[] savedEnds = _ends;
        bool savedComposite = _compositeGlyph;
        int savedCutIn = _cutIn, savedDeltaBase = _deltaBase, savedDeltaShift = _deltaShift, savedControl = _instructionControl;
        int savedMinimum = _minimumDistance, savedSingleCutIn = _singleWidthCutIn, savedSingleWidth = _singleWidth;
        int savedScanControl = _scanControl, savedScanType = _scanType;
        bool savedScanConfigured = _scanConfigured;
        bool savedAutoFlip = _autoFlip;
        TrueTypeHintRound savedRound = _round;
        TrueTypeHintSuperRound savedSuperRound = _superRound;
        _glyph = points; _ends = ends; _compositeGlyph = composite;
        try { Run(program, TrueTypeHintProgramKind.Glyph); }
        finally
        {
            _glyph = savedPoints; _ends = savedEnds; _compositeGlyph = savedComposite;
            _cutIn = savedCutIn; _deltaBase = savedDeltaBase; _deltaShift = savedDeltaShift;
            _instructionControl = savedControl; _round = savedRound;
            _superRound = savedSuperRound;
            _minimumDistance = savedMinimum; _singleWidthCutIn = savedSingleCutIn; _singleWidth = savedSingleWidth;
            _scanControl = savedScanControl; _scanType = savedScanType; _scanConfigured = savedScanConfigured;
            _autoFlip = savedAutoFlip;
        }
    }

    /// <summary>Restores initial CVT values while retaining declared storage and functions for an explicit query-mode transition.</summary>
    /// <remarks>The creation scale, grid and preparation policy remain fixed; only rasterizer queries can change.</remarks>
    internal void Prepare(ReadOnlyMemory<byte> program, TrueTypeHintTarget target)
    {
        if (_reservation is null) throw new InvalidOperationException("The TrueType interpreter is disposed.");
        ValidateTarget(target);
        if (target.ControlScale != _target.ControlScale || target.Preparation != _target.Preparation || target.HorizontalRatio != _target.HorizontalRatio || target.Grid != _target.Grid || target.WindowsArithmetic != _target.WindowsArithmetic)
            throw Invalid("A TrueType mode transition cannot change its arithmetic, CVT scale, grid or preparation policy.");
        for (int i = 0; i < _cvt.Length; i++) { _work.Consume(_token); _cvt[i] = _initialCvt[i]; }
        _target = target;
        Run(program, TrueTypeHintProgramKind.Preparation);
    }

    internal void Run(ReadOnlyMemory<byte> program, TrueTypeHintProgramKind kind)
    {
        if (_reservation is null) throw new InvalidOperationException("The TrueType interpreter is disposed.");
        _work.Consume(_token);
        if (kind is not TrueTypeHintProgramKind.Font and not TrueTypeHintProgramKind.Preparation and not TrueTypeHintProgramKind.Glyph)
            throw Invalid("The TrueType program kind is invalid.");
        if (program.Length > ushort.MaxValue) throw new SvgException(SvgErrorCode.LimitExceeded, "A TrueType program exceeds its supported byte bound.");
        _count = 0;
        if (kind == TrueTypeHintProgramKind.Glyph)
        {
            if ((_instructionControl & 1) != 0) { SetDropouts(); return; }
            // Glyph-local reference/zone/vector state starts at the prescribed defaults.
            _rp0 = _rp1 = _rp2 = 0; _zp0 = _zp1 = _zp2 = TrueTypeHintZone.Glyph;
            _projection = _dualProjection = _freedom = HintVector.XAxis; _loop = 1;
            _iupX = _iupY = false;
            if ((_instructionControl & 2) != 0) { _round = TrueTypeHintRound.Grid; _cutIn = 68; _deltaBase = 9; _deltaShift = 3; _scanControl = _scanType = 0; _scanConfigured = false; _minimumDistance = 64; _singleWidthCutIn = _singleWidth = 0; _autoFlip = true; _instructionControl &= ~4; }
        }
        try
        {
            Execute(program, kind);
            if (kind == TrueTypeHintProgramKind.Glyph) SetDropouts();
        }
        catch (OverflowException) { throw Invalid("TrueType arithmetic exceeds its signed coordinate range."); }

        void SetDropouts()
        {
            GlyphSmartDropouts = SmartDropouts();
            GlyphExcludeDropoutStubs = GlyphSmartDropouts && _scanType == 5;
        }

        bool SmartDropouts()
        {
            // The native symmetric profile enables default tip dropouts through
            // rounded ppem 32. Authored scanner settings replace that default.
            if (!_scanConfigured && (_target.QueryFlags & TrueTypeHintQueryFlags.Symmetric) != 0 && _ppem <= 32) return true;
            if (_scanType is not 4 and not 5) return false;
            int threshold = _scanControl & 255;
            bool small = threshold == 255 || threshold != 0 && _ppem <= threshold;
            bool rotated = (_target.QueryFlags & TrueTypeHintQueryFlags.Rotated) != 0;
            bool stretched = (_target.QueryFlags & TrueTypeHintQueryFlags.Stretched) != 0;
            bool enabled = (_scanControl & 256) != 0 && small || (_scanControl & 512) != 0 && rotated || (_scanControl & 1024) != 0 && stretched;
            return enabled && ((_scanControl & 2048) == 0 || small) && ((_scanControl & 4096) == 0 || rotated) && ((_scanControl & 8192) == 0 || stretched);
        }
    }

    private void Execute(ReadOnlyMemory<byte> program, TrueTypeHintProgramKind kind)
    {
        int pc = 0, conditionalDepth = 0;
        while (pc < program.Length)
        {
            _work.Consume(_token);
            int start = pc; byte opcode = program.Span[pc++];
            if (opcode is 0x40 or 0x41 or >= 0xB0 and <= 0xBF)
            {
                bool words = opcode == 0x41 || opcode >= 0xB8;
                int count = opcode is 0x40 or 0x41 ? ReadByte() : (opcode & 7) + 1;
                for (int i = 0; i < count; i++) { _work.Consume(_token); Push(words ? (short)((ReadByte() << 8) | ReadByte()) : ReadByte()); }
                continue;
            }
            switch (opcode)
            {
                case >= 0x00 and <= 0x05:
                {
                    HintVector vector = (opcode & 1) == 0 ? HintVector.YAxis : HintVector.XAxis;
                    if (opcode < 4) _projection = _dualProjection = vector;
                    if ((opcode & 2) == 0) _freedom = vector;
                    break;
                }
                case >= 0x06 and <= 0x09: SetLineVector(opcode); break;
                case 0x0A:
                {
                    int y = unchecked((short)Pop()), x = unchecked((short)Pop());
                    if (x != 0 || y != 0) _projection = NormalizeVector(x, y);
                    _dualProjection = _projection;
                    break;
                }
                case 0x0C: Push(_projection.X); Push(_projection.Y); break;
                case 0x10: _rp0 = Pop(); break;
                case 0x11: _rp1 = Pop(); break;
                case 0x12: _rp2 = Pop(); break;
                case 0x13: _zp0 = Zone(Pop()); break;
                case 0x14: _zp1 = Zone(Pop()); break;
                case 0x15: _zp2 = Zone(Pop()); break;
                case 0x16: _zp0 = _zp1 = _zp2 = Zone(Pop()); break;
                case 0x17: _loop = Pop(); if (_loop < 0 || _loop > _stack.Length) throw Invalid("The TrueType point loop is outside its stack bound."); break;
                case 0x18: _round = TrueTypeHintRound.Grid; break;
                case 0x19: _round = TrueTypeHintRound.HalfGrid; break;
                case 0x1A: _minimumDistance = Pop(); break;
                case 0x1B:
                    if (conditionalDepth == 0) throw Invalid("ELSE occurred outside a TrueType conditional.");
                    Skip(false); conditionalDepth--; break;
                case 0x1D: _cutIn = Pop(); break;
                case 0x1E: _singleWidthCutIn = Pop(); break;
                case 0x1F: _singleWidth = Divide(Pop(), 65536, Math.Max(_scale.Horizontal, _scale.Vertical)); break;
                case 0x20: Push(Peek(1)); break;
                case 0x21: Pop(); break;
                case 0x22: _count = 0; break;
                case 0x23: { int a = Pop(), b = Pop(); Push(a); Push(b); break; }
                case 0x24: Push(_count); break;
                case 0x25: Push(Peek(Pop())); break;
                case 0x26: { int k = Pop(), value = Peek(k); int index = _count - k; for (int i = index; i < _count - 1; i++) { _work.Consume(_token); _stack[i] = _stack[i + 1]; } _stack[_count - 1] = value; break; }
                case 0x2A: { int function = Pop(), count = Pop(); if (count < 0) throw Invalid("A TrueType function loop has a negative count."); for (int i = 0; i < count; i++) Call(function); break; }
                case 0x2B: Call(Pop()); break;
                case 0x2C:
                {
                    if (kind == TrueTypeHintProgramKind.Glyph) throw Invalid("A glyph cannot define TrueType functions.");
                    int function = Index(Pop(), _functions.Length); int first = pc;
                    while (pc < program.Length && program.Span[pc] != 0x2D)
                    {
                        _work.Consume(_token);
                        if (program.Span[pc] == 0x2C) throw Invalid("A TrueType function definition is nested.");
                        SkipInstruction();
                    }
                    if (pc == program.Length) throw Invalid("A TrueType function has no ENDF.");
                    if (!_functions[function].Defined) DefinedFunctions++;
                    _functions[function] = new(true, program.Slice(first, pc - first)); pc++; break;
                }
                case 0x2D: throw Invalid("ENDF occurred outside a TrueType function definition.");
                case 0x2E: case 0x2F:
                {
                    int p = Pop(); ref TrueTypeHintPoint point = ref Point(_zp0, p);
                    int coordinate = Coordinate(point, false); Move(ref point, opcode == 0x2F ? checked(Round(coordinate, kind) - coordinate) : 0, true);
                    _rp0 = _rp1 = p; break;
                }
                case 0x30: case 0x31:
                    if (kind == TrueTypeHintProgramKind.Glyph && BackwardCompatible && _iupX && _iupY) break;
                    InterpolateUntouched((TrueTypeHintAxis)(opcode & 1));
                    if (opcode == 0x30) _iupY = true; else _iupX = true;
                    break;
                case 0x32: case 0x33: ShiftPoints(opcode); break;
                case 0x34: case 0x35: ShiftContour(opcode); break;
                case 0x38:
                {
                    int amount = Pop(); for (int i = 0; i < _loop; i++) { _work.Consume(_token); ref TrueTypeHintPoint p = ref Point(_zp2, Pop()); MoveAlongFreedom(ref p, amount); } _loop = 1; break;
                }
                case 0x39: InterpolatePoints(); break;
                case 0x3A: case 0x3B:
                {
                    int distance = Pop(), index = Pop();
                    ref TrueTypeHintPoint point = ref Point(_zp1, index);
                    TrueTypeHintPoint reference = Point(_zp0, _rp0);
                    if (_zp1 == TrueTypeHintZone.Twilight)
                    {
                        TrueTypeHintPoint original = new(reference.OriginalX, reference.OriginalY, 0, 0);
                        Move(ref original, distance, false);
                        point.X = point.OriginalX = original.X; point.Y = point.OriginalY = original.Y;
                        reference = Point(_zp0, _rp0);
                    }
                    Move(ref point, checked(distance - ProjectedDistance(point, reference, false)), true);
                    _rp1 = _rp0; _rp2 = index;
                    if ((opcode & 1) != 0) _rp0 = index;
                    break;
                }
                case 0x3C:
                {
                    TrueTypeHintPoint reference = Point(_zp0, _rp0);
                    for (int i = 0; i < _loop; i++) { _work.Consume(_token); ref TrueTypeHintPoint p = ref Point(_zp1, Pop()); Move(ref p, ProjectedDistance(reference, p, false), true); } _loop = 1; break;
                }
                case 0x3D: _round = TrueTypeHintRound.DoubleGrid; break;
                case 0x3E: case 0x3F:
                {
                    int value = ReadControl(Index(Pop(), _cvt.Length)), p = Pop(); ref TrueTypeHintPoint point = ref Point(_zp0, p);
                    if (_zp0 == TrueTypeHintZone.Twilight)
                    {
                        point.X = point.OriginalX = Divide(value, 16384, _freedom.X);
                        point.Y = point.OriginalY = Divide(value, 16384, _freedom.Y);
                    }
                    int original = Coordinate(point, false);
                    if (opcode == 0x3F) { if (Math.Abs((long)value - original) > ControlCutIn(kind)) value = original; value = Round(value, kind); }
                    Move(ref point, checked(value - original), true); _rp0 = _rp1 = p; break;
                }
                case 0x42: { int value = Pop(); _storage[Index(Pop(), _storage.Length)] = value; break; }
                case 0x43: Push(_storage[Index(Pop(), _storage.Length)]); break;
                case 0x44: { int value = Pop(); _cvt[Index(Pop(), _cvt.Length)] = StoreControl(value); break; }
                case 0x45: Push(ReadControl(Index(Pop(), _cvt.Length))); break;
                case 0x46: case 0x47: Push(Coordinate(Point(_zp2, Pop()), opcode == 0x47)); break;
                case 0x48:
                {
                    int value = Pop(); ref TrueTypeHintPoint point = ref Point(_zp2, Pop());
                    Move(ref point, checked(value - Coordinate(point, false)), true);
                    if (_zp2 == TrueTypeHintZone.Twilight) { point.OriginalX = point.X; point.OriginalY = point.Y; }
                    break;
                }
                case 0x49: case 0x4A:
                {
                    int a = Pop(), b = Pop(); TrueTypeHintPoint p = Point(_zp0, b), q = Point(_zp1, a);
                    // MD[1] (0x4A) measures the original distance; MD[0]
                    // (0x49) the fitted distance. Scale the font-unit difference once.
                    int distance = opcode == 0x4A && !_compositeGlyph && _zp0 == TrueTypeHintZone.Glyph && _zp1 == TrueTypeHintZone.Glyph
                        ? DesignDistance(p, q, true)
                        : ProjectedDistance(p, q, opcode == 0x4A);
                    Push(distance); break;
                }
                case 0x4B: Push(AxisPpem); break;
                case 0x4D: _autoFlip = true; break;
                case 0x4E: _autoFlip = false; break;
                case 0x50: case 0x51: case 0x52: case 0x53: case 0x54: case 0x55:
                case 0x5A: case 0x5B: case 0x60: case 0x61: case 0x62: case 0x63:
                {
                    int b = Pop(), a = Pop();
                    Push(opcode switch
                    {
                        0x50 => a < b ? 1 : 0, 0x51 => a <= b ? 1 : 0,
                        0x52 => a > b ? 1 : 0, 0x53 => a >= b ? 1 : 0,
                        0x54 => a == b ? 1 : 0, 0x55 => a != b ? 1 : 0,
                        0x5A => a != 0 && b != 0 ? 1 : 0, 0x5B => a != 0 || b != 0 ? 1 : 0,
                        0x60 => checked(a + b), 0x61 => checked(a - b),
                        0x62 => b == 0 ? throw Invalid("TrueType arithmetic divides by zero.") : checked((int)((long)a * 64 / b)),
                        _ => Divide(a, 64, b)
                    });
                    break;
                }
                case 0x56: case 0x57:
                {
                    int value = Pop();
                    // DirectWrite tests physical nearest-grid parity independently
                    // of graphics-state rounding. Classic FreeType uses that state;
                    // a fractional result can satisfy neither predicate.
                    long rounded = _target.UsesWindowsArithmetic
                        ? (Math.Abs((long)value) + 32) & ~63L
                        : RoundMagnitude(value, 64);
                    Push((rounded & 127) == (opcode == 0x56 ? 64 : 0) ? 1 : 0);
                    break;
                }
                case 0x58: if (Pop() != 0 || Skip(true)) conditionalDepth++; break;
                case 0x59:
                    if (conditionalDepth == 0) throw Invalid("EIF occurred outside a TrueType conditional.");
                    conditionalDepth--; break;
                case 0x5C: Push(Pop() == 0 ? 1 : 0); break;
                case 0x5D: case 0x71: case 0x72: Delta(opcode, false); break;
                case 0x73: case 0x74: case 0x75: Delta(opcode, true); break;
                case 0x5E: _deltaBase = Pop(); break;
                case 0x5F: _deltaShift = Pop(); if (_deltaShift is < 0 or > 6) throw Invalid("The TrueType delta shift is invalid."); break;
                case 0x64: Push(checked((int)Math.Abs((long)Pop()))); break;
                case 0x65: Push(checked(-Pop())); break;
                case 0x66: Push(Pop() & ~63); break;
                case 0x67: Push(checked((Pop() + 63) & ~63)); break;
                case >= 0x68 and <= 0x6B: Push(Round(Pop(), kind)); break;
                case 0x76:
                {
                    int selector = Pop() & 255;
                    int period = (selector & 192) switch
                    {
                        0 => 32, 64 => 64, 128 => 128,
                        _ => throw new SvgException(SvgErrorCode.UnsupportedFeature, "The reserved TrueType super-round period is not supported.")
                    };
                    int threshold = (selector & 15) == 0 ? period - 1 : ((selector & 15) - 4) * period / 8;
                    _superRound = new(period, period * ((selector >> 4) & 3) / 4, threshold);
                    _round = TrueTypeHintRound.Super;
                    break;
                }
                case 0x78: { int condition = Pop(), offset = Pop(); if (condition != 0) Jump(offset); break; }
                case 0x79: { int condition = Pop(), offset = Pop(); if (condition == 0) Jump(offset); break; }
                case 0x1C: Jump(Pop()); break;
                case 0x7A: _round = TrueTypeHintRound.Off; break;
                case 0x7C: _round = TrueTypeHintRound.Up; break;
                case 0x7D: _round = TrueTypeHintRound.Down; break;
                case 0x81: case 0x82:
                {
                    int last = Pop(), first = Pop();
                    if (BackwardCompatible && _iupX && _iupY) break;
                    Index(first, _glyph.Length); Index(last, _glyph.Length);
                    for (int index = first; index <= last; index++) { _work.Consume(_token); _glyph[index].IsOnCurve = opcode == 0x81; }
                    break;
                }
                case 0x85: _scanControl = Pop() & 0xFFFF; _scanConfigured = true; break;
                case 0x86: case 0x87: SetLineVector(opcode); break;
                case 0x88:
                {
                    int selector = Pop(), result = (selector & 1) != 0 ? _target.Version : 0;
                    // Selector bits 1..12 map to independent result bits 8..19.
                    // Subpixel positioning and symmetric rendering are distinct queries.
                    result |= ((selector & 0x1FFE) << 7) & (int)_target.QueryFlags;
                    Push(result); break;
                }
                case 0x8A: { int a = Pop(), b = Pop(), c = Pop(); Push(b); Push(a); Push(c); break; }
                case 0x8B: case 0x8C:
                {
                    int second = Pop(), first = Pop();
                    Push(opcode == 0x8B ? Math.Max(first, second) : Math.Min(first, second));
                    break;
                }
                case 0x8D: _scanType = Pop() & 0xFFFF; _scanConfigured = true; break;
                case 0x8E:
                {
                    int selector = Pop(), value = Pop(); if (selector is < 1 or > 3 || value != 0 && value != 1 << (selector - 1)) throw Invalid("The TrueType instruction-control value is invalid.");
                    if (kind != TrueTypeHintProgramKind.Glyph || selector == 3) _instructionControl = (_instructionControl & ~(1 << (selector - 1))) | value;
                    break;
                }
                case 0x70: { int value = Pop(); _cvt[Index(Pop(), _cvt.Length)] = _compositeGlyph ? StoreControl(value) : Divide(value, 65536, ProjectionScale); break; }
                case >= 0xC0: MoveRelative(opcode); break;
                default: throw new SvgException(SvgErrorCode.UnsupportedFeature, $"TrueType hint opcode 0x{opcode:X2} is not implemented.");
            }

            byte ReadByte() { if (pc >= program.Length) throw Invalid("TrueType push operands are truncated."); return program.Span[pc++]; }
            void Jump(int offset)
            {
                int destination = checked(start + offset);
                if (destination < 0 || destination > program.Length) throw Invalid("A TrueType jump leaves its program.");
                // Valid programs can leave or re-enter conditionals with a jump.
                // Reconcile only the crossed instructions, keeping bounded scans
                // and operand validation instead of treating IF as a runtime stack.
                int direction = destination >= pc ? 1 : -1;
                int end = Math.Max(pc, destination);
                pc = Math.Min(pc, destination);
                while (pc < end)
                {
                    _work.Consume(_token);
                    byte crossed = program.Span[pc];
                    if (crossed == 0x58) conditionalDepth += direction;
                    else if (crossed == 0x59) conditionalDepth -= direction;
                    SkipInstruction();
                }
                if (pc != end || conditionalDepth < 0) throw Invalid("A TrueType jump crosses invalid instruction or conditional boundaries.");
                pc = destination;
            }
            void SkipInstruction()
            {
                byte op = ReadByte(); int count = op is 0x40 or 0x41 ? ReadByte() : op is >= 0xB0 and <= 0xBF ? (op & 7) + 1 : 0;
                bool words = op == 0x41 || op >= 0xB8 && op <= 0xBF;
                int bytes = count * (words ? 2 : 1); if (bytes > program.Length - pc) throw Invalid("Skipped TrueType push operands are truncated.");
                for (int i = 0; i < bytes; i++) _work.Consume(_token); pc += bytes;
            }
            bool Skip(bool acceptsElse)
            {
                int nesting = 0;
                while (pc < program.Length)
                {
                    _work.Consume(_token); byte op = program.Span[pc];
                    if (op == 0x58) nesting++;
                    else if (op == 0x59) { if (nesting == 0) { pc++; return false; } nesting--; }
                    else if (op == 0x1B && nesting == 0 && acceptsElse) { pc++; return true; }
                    SkipInstruction();
                }
                throw Invalid("A TrueType conditional has no closing EIF.");
            }
            void Call(int index)
            {
                _work.Consume(_token); TrueTypeHintFunction function = _functions[Index(index, _functions.Length)];
                if (!function.Defined) throw Invalid("An undefined TrueType function was called.");
                if (_depth == 32) throw new SvgException(SvgErrorCode.LimitExceeded, "TrueType function nesting exceeds its limit.");
                _depth++; try { Execute(function.Program, kind); } finally { _depth--; }
            }
            void ShiftContour(byte op)
            {
                (TrueTypeHintZone referenceZone, int referenceIndex, int delta) = ReferenceShift(op);
                int contour = Pop(), first, end;
                if (_zp2 == TrueTypeHintZone.Twilight) { if (contour != 0) throw Invalid("A twilight contour index is invalid."); first = 0; end = _twilight.Length; }
                else { int index = Index(contour, _ends.Length); first = index == 0 ? 0 : _ends[index - 1] + 1; end = _ends[index] + 1; }
                for (int i = first; i < end; i++) { _work.Consume(_token); if (_zp2 == referenceZone && i == referenceIndex) continue; ref TrueTypeHintPoint p = ref Point(_zp2, i); Move(ref p, delta, false); }
            }
            (TrueTypeHintZone Zone, int Index, int Delta) ReferenceShift(byte op)
            {
                TrueTypeHintZone zone = (op & 1) == 0 ? _zp1 : _zp0;
                int index = (op & 1) == 0 ? _rp2 : _rp1;
                TrueTypeHintPoint point = Point(zone, index);
                return (zone, index, Project(_projection, checked(point.X - point.OriginalX), checked(point.Y - point.OriginalY)));
            }
            void ShiftPoints(byte op)
            {
                (_, _, int delta) = ReferenceShift(op);
                for (int i = 0; i < _loop; i++)
                {
                    _work.Consume(_token);
                    ref TrueTypeHintPoint point = ref Point(_zp2, Pop());
                    Move(ref point, delta, true);
                }
                _loop = 1;
            }
            void MoveRelative(byte op)
            {
                bool indirect = op >= 0xE0;
                int control = indirect ? ReadControl(Index(Pop(), _cvt.Length)) : 0;
                int index = Pop();
                ref TrueTypeHintPoint point = ref Point(_zp1, index);
                TrueTypeHintPoint reference = Point(_zp0, _rp0);
                if (indirect)
                {
                    if (Math.Abs((long)control - _singleWidth) < _singleWidthCutIn)
                        control = control >= 0 ? _singleWidth : checked(-_singleWidth);
                    if (_zp1 == TrueTypeHintZone.Twilight)
                    {
                        point.OriginalX = checked(reference.OriginalX + Divide(control, 16384, _freedom.X));
                        point.OriginalY = checked(reference.OriginalY + Divide(control, 16384, _freedom.Y));
                        point.X = point.OriginalX; point.Y = point.OriginalY;
                        reference = Point(_zp0, _rp0);
                    }
                }
                int original = !indirect && !_compositeGlyph && _zp0 == TrueTypeHintZone.Glyph && _zp1 == TrueTypeHintZone.Glyph
                    ? DesignDistance(point, reference, true)
                    : ProjectedDistance(point, reference, true);
                // Retain native signed single-width cut-in and round the distance,
                // rather than independently rounding either endpoint.
                if (!indirect && _singleWidthCutIn > 0 && original < (long)_singleWidth + _singleWidthCutIn && original > (long)_singleWidth - _singleWidthCutIn)
                    original = original >= 0 ? _singleWidth : checked(-_singleWidth);
                if (indirect && _autoFlip && (original ^ control) < 0) control = checked(-control);
                if (indirect && (op & 4) != 0 && _zp0 == _zp1 && Math.Abs((long)control - original) > ControlCutIn(kind)) control = original;
                int distance = indirect ? control : original;
                if ((op & 4) != 0) distance = Round(distance, kind);
                int minimum = UsesClearTypeGrid(kind) ? _minimumDistance / 2 : _minimumDistance;
                if ((op & 8) != 0) distance = original >= 0 ? Math.Max(distance, minimum) : Math.Min(distance, checked(-minimum));
                int current = ProjectedDistance(point, reference, false);
                Move(ref point, checked(distance - current), true);
                _rp1 = _rp0; _rp2 = index;
                if ((op & 16) != 0) _rp0 = index;
            }

            void InterpolatePoints()
            {
                TrueTypeHintPoint first = Point(_zp0, _rp1), last = Point(_zp1, _rp2);
                bool design = !_compositeGlyph && _zp0 == TrueTypeHintZone.Glyph && _zp1 == TrueTypeHintZone.Glyph && _zp2 == TrueTypeHintZone.Glyph;
                int originalRange = OriginalDistance(last);
                int currentRange = ProjectedDistance(last, first, false);
                for (int i = 0; i < _loop; i++)
                {
                    _work.Consume(_token);
                    ref TrueTypeHintPoint point = ref Point(_zp2, Pop());
                    int original = OriginalDistance(point);
                    int distance = originalRange != 0 ? Divide(original, originalRange, currentRange) : original;
                    int current = ProjectedDistance(point, first, false);
                    Move(ref point, checked(distance - current), true);
                }
                _loop = 1;

                int OriginalDistance(TrueTypeHintPoint point)
                {
                    return design ? DesignDistance(point, first, false) : ProjectedDistance(point, first, true);
                }
            }

            void Delta(byte op, bool control)
            {
                int count = Pop(); if (count < 0 || count > _count / 2) throw Invalid("TrueType delta operands exceed the stack.");
                for (int i = 0; i < count; i++)
                {
                    _work.Consume(_token); int p = Pop(), argument = Pop();
                    if (argument is < 0 or > 255) throw Invalid("A TrueType delta argument is invalid.");
                    int ppem = checked(_deltaBase + (op is 0x71 or 0x74 ? 16 : op is 0x72 or 0x75 ? 32 : 0) + (argument >> 4));
                    int step = (argument & 15) - 8; if (step >= 0) step++;
                    int amount = step * (64 >> _deltaShift);
                    if (control)
                    {
                        int index = Index(p, _cvt.Length);
                        if (ppem == AxisPpem) _cvt[index] = checked(_cvt[index] + StoreControl(amount));
                    }
                    else
                    {
                        ref TrueTypeHintPoint point = ref Point(_zp0, p);
                        if (ppem == AxisPpem && (kind != TrueTypeHintProgramKind.Glyph || !BackwardCompatible ||
                            !(_iupX && _iupY) && _freedom.X == 0 && _freedom.Y != 0 && (_compositeGlyph || point.TouchedY)))
                            Move(ref point, amount, true);
                    }
                }
            }
            void InterpolateUntouched(TrueTypeHintAxis axis)
            {
                int first = 0;
                foreach (ushort last in _ends)
                {
                    int firstTouched = -1, previous = -1;
                    for (int i = first; i <= last; i++)
                    {
                        _work.Consume(_token); if (!(axis == TrueTypeHintAxis.X ? _glyph[i].TouchedX : _glyph[i].TouchedY)) continue;
                        if (firstTouched < 0) firstTouched = i;
                        if (previous >= 0) Interpolate(previous + 1, i - 1, previous, i);
                        previous = i;
                    }
                    if (firstTouched >= 0) { Interpolate(previous + 1, last, previous, firstTouched); Interpolate(first, firstTouched - 1, previous, firstTouched); }
                    first = last + 1;
                }
                void Interpolate(int from, int to, int a, int b)
                {
                    int unscaledA = Unscaled(_glyph[a]), unscaledB = Unscaled(_glyph[b]);
                    int originalA = Original(_glyph[a]), originalB = Original(_glyph[b]);
                    int currentA = Current(_glyph[a]), currentB = Current(_glyph[b]);
                    if (unscaledA > unscaledB) { (unscaledA, unscaledB) = (unscaledB, unscaledA); (originalA, originalB) = (originalB, originalA); (currentA, currentB) = (currentB, currentA); }
                    int scale = unscaledA == unscaledB || currentA == currentB ? 0 : Divide(checked(currentB - currentA), checked(unscaledB - unscaledA), 65536);
                    for (int i = from; i <= to; i++)
                    {
                        _work.Consume(_token); int original = Original(_glyph[i]); int value;
                        if (original <= originalA) value = checked(original + currentA - originalA);
                        else if (original >= originalB) value = checked(original + currentB - originalB);
                        else if (unscaledA == unscaledB || currentA == currentB) value = currentA;
                        // Windows interpolates the design-unit ratio before rounding
                        // the resulting device coordinate. A 16.16 slope can lose
                        // an exact half-unit, especially in fitted accent contours.
                        else if (_target.UsesWindowsArithmetic)
                            value = checked(currentA + Divide(checked(Unscaled(_glyph[i]) - unscaledA), checked(unscaledB - unscaledA), checked(currentB - currentA)));
                        else value = checked(currentA + Divide(checked(Unscaled(_glyph[i]) - unscaledA), 65536, scale));
                        if (axis == TrueTypeHintAxis.X) _glyph[i].X = value; else _glyph[i].Y = value;
                    }
                }
                int Original(TrueTypeHintPoint p) => axis == TrueTypeHintAxis.X ? p.OriginalX : p.OriginalY;
                int Current(TrueTypeHintPoint p) => axis == TrueTypeHintAxis.X ? p.X : p.Y;
                int Unscaled(TrueTypeHintPoint p) => axis == TrueTypeHintAxis.X ? p.UnscaledX : p.UnscaledY;
            }
        }
        if (conditionalDepth != 0) throw Invalid("A TrueType conditional has no closing EIF.");
    }

    private static int Divide(int value, int denominator, int multiplier)
    {
        if (denominator == 0) throw Invalid("TrueType arithmetic divides by zero.");
        long product = (long)value * multiplier, magnitude = Math.Abs(product), divisor = Math.Abs((long)denominator);
        long result = (magnitude + divisor / 2) / divisor; if ((product < 0) != (denominator < 0)) result = -result;
        return checked((int)result);
    }
    private bool UsesClearTypeGrid(TrueTypeHintProgramKind kind) => kind == TrueTypeHintProgramKind.Glyph && _target.Grid == TrueTypeHintGrid.ClearType && _projection.X != 0;

    private int ControlCutIn(TrueTypeHintProgramKind kind) => UsesClearTypeGrid(kind) ? _cutIn / 16 : _cutIn;

    private int Round(int value, TrueTypeHintProgramKind kind)
    {
        int grid = UsesClearTypeGrid(kind) ? 4 : 64;
        long result = RoundMagnitude(value, grid);
        return checked((int)(value < 0 ? -result : result));
    }

    private long RoundMagnitude(int value, int grid)
    {
        long absolute = Math.Abs((long)value);
        long mask = -(long)grid, halfMask = -(long)(grid / 2);
        return _round switch
        {
            TrueTypeHintRound.Grid => (absolute + grid / 2) & mask,
            TrueTypeHintRound.HalfGrid => (absolute & mask) + grid / 2,
            TrueTypeHintRound.DoubleGrid => (absolute + grid / 4) & halfMask,
            TrueTypeHintRound.Down => absolute & mask,
            TrueTypeHintRound.Up => (absolute + grid - 1) & mask,
            // Explicit SROUND periods stay physical, including ClearType X.
            // Round each magnitude and clamp a sign crossing to the closest phase.
            TrueTypeHintRound.Super => Math.Max(_superRound.Phase,
                ((absolute + _superRound.Threshold - _superRound.Phase) & -(long)_superRound.Period) + _superRound.Phase),
            _ => absolute
        };
    }
    private ref TrueTypeHintPoint Point(TrueTypeHintZone zone, int index)
    {
        TrueTypeHintPoint[] points = zone == TrueTypeHintZone.Glyph ? _glyph : _twilight;
        ref TrueTypeHintPoint point = ref points[Index(index, points.Length)];
        if (point.Unavailable) throw new SvgException(SvgErrorCode.UnsupportedFeature, "TrueType vertical phantom metrics are not implemented.");
        return ref point;
    }
    internal static void ValidateTarget(TrueTypeHintTarget target)
    {
        if (target.Version is < 0 or > 255 || (target.QueryFlags & ~TrueTypeHintQueryFlags.All) != 0 || target.ControlScale is not TrueTypeHintControlScale.FullPrecision and not TrueTypeHintControlScale.FractionalFontUnits || target.Preparation is not TrueTypeHintPreparation.Single and not TrueTypeHintPreparation.DefaultThenTarget || target.Grid is not TrueTypeHintGrid.Physical and not TrueTypeHintGrid.ClearType || !double.IsFinite(target.HorizontalRatio) || target.HorizontalRatio <= 0)
            throw Invalid("The TrueType hint target is invalid.");
    }
    private int ReadControl(int index) => ScaleRatio(_cvt[index], ProjectionRatio);
    private int StoreControl(int value) => ScaleRatio(value, 1 / ProjectionRatio);
    private int ScaleHorizontal(int value) => ScaleRatio(value, _target.HorizontalRatio);
    private static int ScaleRatio(int value, double ratio)
    {
        if (ratio == 1) return value;
        double scaled = Math.Round(value * ratio, MidpointRounding.AwayFromZero);
        if (!double.IsFinite(scaled) || scaled < int.MinValue || scaled > int.MaxValue)
            throw new SvgException(SvgErrorCode.LimitExceeded, "An anisotropic hint coordinate exceeds its signed bound.");
        return (int)scaled;
    }
    private static TrueTypeHintZone Zone(int value) => value is 0 or 1 ? (TrueTypeHintZone)value : throw Invalid("A TrueType zone pointer is invalid.");
    private static int Index(int value, int length) => (uint)value < (uint)length ? value : throw Invalid("A TrueType index is outside its declared range.");
    private int Pop() { if (_count == 0) throw Invalid("The TrueType stack underflows."); return _stack[--_count]; }
    private int Peek(int offset) { if (offset <= 0 || offset > _count) throw Invalid("A TrueType stack index is invalid."); return _stack[_count - offset]; }
    private void Push(int value) { if (_count == _stack.Length) throw Invalid("The TrueType stack exceeds its declared capacity."); _stack[_count++] = value; }
    private static SvgException Invalid(string message) => new(SvgErrorCode.MalformedInput, message);

    public void Dispose() { Array.Clear(_functions); _reservation?.Dispose(); _reservation = null; }
}
