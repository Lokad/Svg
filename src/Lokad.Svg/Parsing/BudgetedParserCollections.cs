namespace Lokad.Svg.Parsing;

using Lokad.Svg.Rendering;
using System.Runtime.CompilerServices;

/// <summary>Tracks parser collection backing storage against a conversion budget.</summary>
internal static class ParserCollectionMemory
{
    private const int ArrayHeaderBytes = 32;

    internal static long GetArrayByteCount(int count, int entryBytes)
        => count == 0 ? 0 : checked(ArrayHeaderBytes + (long)count * entryBytes);

    /// <summary>Gets a conservative array charge using the runtime size of its value element type.</summary>
    internal static long GetArrayByteCount<T>(int count) where T : struct
        => GetArrayByteCount(count, Unsafe.SizeOf<T>());
}

/// <summary>Retains immutable parser strings under one aggregate lifetime allowance, copying only span-backed values.</summary>
internal sealed class BudgetedParserStringStore : IDisposable
{
    private const long StringHeaderBytes = 32;
    // Conservative aggregate allowances cover reference-set backing growth and
    // the store/set/reservation objects, without one tracking object per string.
    private const long PerStringTrackingBytes = 48;
    private const long StoreTrackingBytes = 192;
    private readonly RenderBudget _budget;
    private HashSet<string>? _retained;
    private RenderBudget.BudgetReservation? _reservation;

    internal BudgetedParserStringStore(RenderBudget budget) => _budget = budget;

    internal string Copy(ReadOnlySpan<char> value)
    {
        if (value.IsEmpty) return string.Empty;
        RetainAllowance(value.Length);
        string copy = value.ToString();
        HashSet<string> retained = _retained ?? throw new InvalidOperationException("String retention is unavailable.");
        retained.Add(copy);
        return copy;
    }

    /// <summary>Retains an existing immutable string once by reference identity; no reader, process-wide intern table or caller buffer is retained.</summary>
    internal string Copy(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Length == 0) return string.Empty;
        if (_retained is {} existing && existing.Contains(value)) return value;
        RetainAllowance(value.Length);
        HashSet<string> retained = _retained ?? throw new InvalidOperationException("String retention is unavailable.");
        retained.Add(value);
        return value;
    }

    private void RetainAllowance(int length)
    {
        long bytes = checked(StringHeaderBytes + (long)length * sizeof(char) + PerStringTrackingBytes);
        if (_reservation is null) _reservation = _budget.Reserve(StoreTrackingBytes + bytes, SvgMemoryCategory.Parser);
        else _reservation.Grow(bytes);
        _retained ??= new(ReferenceEqualityComparer.Instance);
    }

    public void Dispose()
    {
        _retained = null;
        Interlocked.Exchange(ref _reservation, null)?.Dispose();
    }
}

/// <summary>Builds one transient direct-text value with parser-budgeted backing storage and transfers its exact string reservation to the caller.</summary>
internal sealed class BudgetedTextBuffer : IDisposable, ISvgTextOutput
{
    private const long StringHeaderBytes = 32;
    private const int CopyCheckpointCharacters = 4_096;
    private readonly RenderBudget _budget;
    private char[] _characters = [];
    private RenderBudget.BudgetReservation? _reservation;
    private int _length;
    private bool _trailingCollapsedWhitespace;

    internal BudgetedTextBuffer(RenderBudget budget)
    {
        ArgumentNullException.ThrowIfNull(budget);
        _budget = budget;
    }

    internal int Length => _length;

    /// <summary>Copies text after reserving all live replacement storage and charges copying work to the current conversion.</summary>
    internal void Append(string value, int maximumLength, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(value);
        Append(value.AsSpan(), maximumLength, workCounter, cancellationToken);
    }

    /// <summary>Copies one reader-provided text chunk without first materializing it as a string.</summary>
    internal void Append(ReadOnlySpan<char> value, int maximumLength, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workCounter);
        if (value.Length > maximumLength - _length) throw new SvgException(SvgErrorCode.LimitExceeded, "The document direct text character count exceeds the admitted layout limit.");
        EnsureCapacity(checked(_length + value.Length), workCounter, cancellationToken);
        for (int offset = 0; offset < value.Length; offset += CopyCheckpointCharacters)
        {
            workCounter.Consume(cancellationToken);
            int count = Math.Min(CopyCheckpointCharacters, value.Length - offset);
            value.Slice(offset, count).CopyTo(_characters.AsSpan(_length + offset));
        }
        _length += value.Length;
        if (!value.IsEmpty) _trailingCollapsedWhitespace = false;
    }

    void ISvgTextOutput.Append(ReadOnlySpan<char> value, double letterSpacing, int scopeDepth, int maximumLength, ParserWorkCounter workCounter, CancellationToken cancellationToken)
        => Append(value, maximumLength, workCounter, cancellationToken);

    void ISvgTextOutput.AppendCollapsedWhitespace(double letterSpacing, int scopeDepth, int maximumLength, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        if (_length == 0 || _trailingCollapsedWhitespace) return;
        Append(" ", maximumLength, workCounter, cancellationToken);
        _trailingCollapsedWhitespace = true;
    }

    internal void TrimLastCharacter()
    {
        if (_length == 0) throw new InvalidOperationException("No retained character is available to trim.");
        _characters[--_length] = default;
    }

    void ISvgTextOutput.EnterPositioningSpan(SvgTextPositionOffset offset, int scopeDepth, ParserWorkCounter workCounter, CancellationToken cancellationToken) { }
    void ISvgTextOutput.EnterPositioningSpan(SvgTextPositionLists lists, int scopeDepth, ParserWorkCounter workCounter, CancellationToken cancellationToken) { }

    void ISvgTextOutput.ExitPositioningSpan(int scopeDepth) { }

    /// <summary>Transfers ownership of the exact final string reservation; the caller must dispose the returned value after layout.</summary>
    internal BudgetedText Detach()
    {
        if (_trailingCollapsedWhitespace) TrimLastCharacter();
        if (_length == 0)
        {
            Dispose();
            return new BudgetedText(string.Empty, null);
        }
        RenderBudget.BudgetReservation reservation = _budget.Reserve(checked(StringHeaderBytes + (long)_length * sizeof(char)), SvgMemoryCategory.Parser);
        try
        {
            string text = new(_characters, 0, _length);
            Dispose();
            return new BudgetedText(text, reservation);
        }
        catch
        {
            reservation.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        _characters = [];
        _length = 0;
        _trailingCollapsedWhitespace = false;
        _reservation?.Dispose();
        _reservation = null;
    }

    private void EnsureCapacity(int requiredLength, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        if (requiredLength <= _characters.Length) return;
        int capacity = _characters.Length == 0 ? Math.Max(256, requiredLength) : _characters.Length > int.MaxValue / 2 ? requiredLength : Math.Max(_characters.Length * 2, requiredLength);
        RenderBudget.BudgetReservation replacementReservation = _budget.Reserve(ParserCollectionMemory.GetArrayByteCount(capacity, sizeof(char)), SvgMemoryCategory.Parser);
        char[] replacement;
        try
        {
            replacement = new char[capacity];
        }
        catch
        {
            replacementReservation.Dispose();
            throw;
        }
        try
        {
            for (int offset = 0; offset < _length; offset += CopyCheckpointCharacters)
            {
                workCounter.Consume(cancellationToken);
                int count = Math.Min(CopyCheckpointCharacters, _length - offset);
                _characters.AsSpan(offset, count).CopyTo(replacement.AsSpan(offset));
            }
        }
        catch
        {
            replacementReservation.Dispose();
            throw;
        }
        _reservation?.Dispose();
        _reservation = replacementReservation;
        _characters = replacement;
    }
}

/// <summary>Owns the budget reservation for a transient direct-text string until its layout consumer disposes it.</summary>
internal readonly record struct BudgetedText(string Value, RenderBudget.BudgetReservation? Reservation) : IDisposable
{
    public void Dispose() => Reservation?.Dispose();
}

/// <summary>Builds normalized direct text and lazily retains spacing intervals and normalized positioning scopes that affect layout.</summary>
internal sealed class SvgTextLayoutBuffer : IDisposable, ISvgTextOutput
{
    private readonly BudgetedTextBuffer _text;
    private readonly RenderBudget _budget;
    private readonly ParserWorkCounter _workCounter;
    private readonly CancellationToken _cancellationToken;
    private readonly double _defaultLetterSpacing;
    private readonly double _defaultWordSpacing;
    private bool _trailingCollapsedWhitespace;
    private BudgetedParserList<SvgTextLetterSpacingSpan>? _letterSpacingSpans;
    private BudgetedParserList<SvgTextPositionScope>? _positioningScopes;
    private BudgetedParserList<SvgTextPositionSpan>? _positionSpans;

    internal SvgTextLayoutBuffer(RenderBudget budget, double defaultLetterSpacing, ParserWorkCounter workCounter, CancellationToken cancellationToken)
        : this(budget, defaultLetterSpacing, 0, workCounter, cancellationToken) { }

    internal SvgTextLayoutBuffer(RenderBudget budget, double defaultLetterSpacing, double defaultWordSpacing, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(budget);
        ArgumentNullException.ThrowIfNull(workCounter);
        if (!double.IsFinite(defaultLetterSpacing)) throw new ArgumentOutOfRangeException(nameof(defaultLetterSpacing));
        if (!double.IsFinite(defaultWordSpacing)) throw new ArgumentOutOfRangeException(nameof(defaultWordSpacing));
        _text = new BudgetedTextBuffer(budget);
        _budget = budget;
        _workCounter = workCounter;
        _cancellationToken = cancellationToken;
        _defaultLetterSpacing = defaultLetterSpacing;
        _defaultWordSpacing = defaultWordSpacing;
    }

    /// <summary>Appends one normalized interval and records eligible span starts before resolving whole Unicode scalars.</summary>
    public void Append(ReadOnlySpan<char> value, double letterSpacing, int scopeDepth, int maximumLength, ParserWorkCounter workCounter, CancellationToken cancellationToken)
        => Append(value, letterSpacing, _defaultWordSpacing, scopeDepth, maximumLength, workCounter, cancellationToken);

    /// <summary>Retains inherited letter/word spacing in the same bounded normalized intervals.</summary>
    internal void Append(ReadOnlySpan<char> value, double letterSpacing, double wordSpacing, int scopeDepth, int maximumLength, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        if (!double.IsFinite(letterSpacing)) throw new ArgumentOutOfRangeException(nameof(letterSpacing));
        if (!double.IsFinite(wordSpacing)) throw new ArgumentOutOfRangeException(nameof(wordSpacing));
        if (scopeDepth < 0) throw new ArgumentOutOfRangeException(nameof(scopeDepth));
        if (!ReferenceEquals(workCounter, _workCounter) || cancellationToken != _cancellationToken) throw new InvalidOperationException("Text layout buffer calls must retain their construction work context.");
        int start = _text.Length;
        _text.Append(value, maximumLength, workCounter, cancellationToken);
        if (!value.IsEmpty) _trailingCollapsedWhitespace = false;
        if (!value.IsEmpty && _positioningScopes is {} scopes)
        {
            for (int index = 0; index < scopes.Count; index++)
            {
                _workCounter.Consume(_cancellationToken);
                SvgTextPositionScope scope = scopes[index];
                if (scope.ScopeDepth > scopeDepth || scope.SpanIndex >= 0) continue;
                BudgetedParserList<SvgTextPositionSpan> positionSpans = _positionSpans ??= new(_budget, SvgMemoryCategory.Parser, _workCounter, _cancellationToken);
                scopes[index] = scope with { SpanIndex = positionSpans.Count };
                positionSpans.Add(new(start, 0, scope.Lists, scope.Offset));
            }
        }
        if (value.Length == 0 || _letterSpacingSpans is null && letterSpacing.Equals(_defaultLetterSpacing) && wordSpacing.Equals(_defaultWordSpacing)) return;
        BudgetedParserList<SvgTextLetterSpacingSpan> spans = _letterSpacingSpans ??= CreateLetterSpacingSpans(start);
        int end = checked(start + value.Length);
        if (spans.Count != 0)
        {
            SvgTextLetterSpacingSpan previous = spans[^1];
            if (previous.End == start && previous.LetterSpacing.Equals(letterSpacing) && previous.WordSpacing.Equals(wordSpacing))
            {
                spans[^1] = previous with { End = end };
                return;
            }
        }
        spans.Add(new SvgTextLetterSpacingSpan(start, end, letterSpacing) { WordSpacing = wordSpacing });
    }

    private bool _hasPriorText;
    internal void BeginTextChunk(bool hasPriorText) => _hasPriorText = hasPriorText;
    internal bool AcceptsCollapsedWhitespace => (_text.Length != 0 || _hasPriorText) && !_trailingCollapsedWhitespace;
    internal void MarkCollapsedWhitespace() => _trailingCollapsedWhitespace = true;

    public void AppendCollapsedWhitespace(double letterSpacing, int scopeDepth, int maximumLength, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        if (!AcceptsCollapsedWhitespace) return;
        Append(" ", letterSpacing, scopeDepth, maximumLength, workCounter, cancellationToken);
        MarkCollapsedWhitespace();
    }

    /// <summary>Begins an explicit scalar scope; normalized output determines when it is consumed.</summary>
    public void EnterPositioningSpan(SvgTextPositionOffset offset, int scopeDepth, ParserWorkCounter workCounter, CancellationToken cancellationToken)
        => EnterPositioningScope(new(scopeDepth, offset, default, -1), workCounter, cancellationToken);

    /// <summary>Begins validated lists over the span's normalized scalar subtree.</summary>
    public void EnterPositioningSpan(SvgTextPositionLists lists, int scopeDepth, ParserWorkCounter workCounter, CancellationToken cancellationToken)
        => EnterPositioningScope(new(scopeDepth, null, lists, -1), workCounter, cancellationToken);

    private void EnterPositioningScope(SvgTextPositionScope scope, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        if (scope.ScopeDepth <= 0) throw new ArgumentOutOfRangeException(nameof(scope));
        if (!ReferenceEquals(workCounter, _workCounter) || cancellationToken != _cancellationToken) throw new InvalidOperationException("Text layout buffer calls must retain their construction work context.");
        BudgetedParserList<SvgTextPositionScope> scopes = _positioningScopes ??= new(_budget, SvgMemoryCategory.Parser, _workCounter, _cancellationToken);
        if (scopes.Count != 0 && scopes[^1].ScopeDepth >= scope.ScopeDepth) throw new InvalidOperationException("Text positioning scopes must be entered in increasing span depth.");
        scopes.Add(scope);
    }

    /// <summary>Ends the current nested positioning scope at its last normalized character.</summary>
    public void ExitPositioningSpan(int scopeDepth)
    {
        if (scopeDepth <= 0) throw new ArgumentOutOfRangeException(nameof(scopeDepth));
        if (_positioningScopes is null || _positioningScopes.Count == 0 || _positioningScopes[^1].ScopeDepth != scopeDepth) throw new InvalidOperationException("Text positioning scopes must exit in nesting order.");
        SvgTextPositionScope scope = _positioningScopes[^1];
        if (scope.SpanIndex >= 0 && _positionSpans is {} spans) spans[scope.SpanIndex] = spans[scope.SpanIndex] with { End = _text.Length };
        _positioningScopes.RemoveLast();
    }

    /// <summary>Transfers normalized text and any exact spacing-span storage to the layout consumer.</summary>
    internal BudgetedTextLayout Detach() => Detach(false);

    internal BudgetedTextLayout Detach(bool preserveTrailingWhitespace)
    {
        if (_trailingCollapsedWhitespace && !preserveTrailingWhitespace)
        {
            _text.TrimLastCharacter();
            _trailingCollapsedWhitespace = false;
            if (_letterSpacingSpans is {} spacing)
            {
                if (spacing[^1].Start == _text.Length) spacing.RemoveLast();
                else spacing[^1] = spacing[^1] with { End = _text.Length };
            }
        }
        _trailingCollapsedWhitespace = false;
        _hasPriorText = false;
        if (_positioningScopes is { Count: not 0 }) throw new InvalidOperationException("Text positioning scopes must be closed before layout transfer.");
        _positioningScopes?.Dispose();
        _positioningScopes = null;
        BudgetedText text = _text.Detach();
        try
        {
            OwnedParserArray<SvgTextLetterSpacingSpan> spans = _letterSpacingSpans is null ? new OwnedParserArray<SvgTextLetterSpacingSpan>([], null) : _letterSpacingSpans.Detach();
            _letterSpacingSpans = null;
            try
            {
                OwnedParserArray<SvgTextPositionAdjustment> adjustments = ResolvePositions(text.Value, _positionSpans is {} intervals ? intervals.AsSpan() : [], _budget, _workCounter, _cancellationToken);
                return new BudgetedTextLayout(text.Value, spans.Items, adjustments.Items, text.Reservation, spans.Reservation, adjustments.Reservation);
            }
            catch
            {
                spans.Dispose();
                throw;
            }
        }
        catch
        {
            text.Dispose();
            throw;
        }
        finally { _positionSpans?.Dispose(); _positionSpans = null; }

        static OwnedParserArray<SvgTextPositionAdjustment> ResolvePositions(string value, ReadOnlySpan<SvgTextPositionSpan> spans,
            RenderBudget budget, ParserWorkCounter work, CancellationToken token)
        {
            if (spans.IsEmpty) return new([], null);
            using BudgetedParserList<SvgTextPositionAdjustment> positions = new(budget, SvgMemoryCategory.Parser, work, token);
            using BudgetedParserList<SvgTextPositionSpanCursor> active = new(budget, SvgMemoryCategory.Parser, work, token);
            int next = 0, source = 0;
            foreach (System.Text.Rune scalar in value.EnumerateRunes())
            {
                work.Consume(token);
                while (active.Count != 0 && active[^1].End <= source) active.RemoveLast();
                while (next < spans.Length && spans[next].Start <= source)
                {
                    SvgTextPositionSpan span = spans[next++];
                    if (span.End > source) active.Add(new(span));
                }
                double? x = null, y = null, dx = null, dy = null, rotation = null;
                bool overridesDx = false, overridesDy = false;
                for (int index = 0; index < active.Count; index++)
                {
                    work.Consume(token);
                    SvgTextPositionSpanCursor cursor = active[index];
                    SvgTextPositionValue position = cursor.Next();
                    active[index] = cursor;
                    x = position.X ?? x; y = position.Y ?? y;
                    if (position.Dx is not null) { dx = position.Dx; overridesDx = position.ExplicitRelativeAxes; }
                    if (position.Dy is not null) { dy = position.Dy; overridesDy = position.ExplicitRelativeAxes; }
                    rotation = position.Rotation ?? rotation;
                }
                if (x is not null || y is not null || dx is not null && dx != 0 || dy is not null && dy != 0 || rotation is not null || overridesDx || overridesDy)
                    positions.Add(new(source, x, y, dx ?? 0, dy ?? 0) { Rotation = rotation, OverridesDx = overridesDx, OverridesDy = overridesDy });
                source += scalar.Utf16SequenceLength;
            }
            return positions.Detach();
        }
    }

    public void Dispose()
    {
        _text.Dispose();
        _letterSpacingSpans?.Dispose();
        _letterSpacingSpans = null;
        _positioningScopes?.Dispose();
        _positioningScopes = null;
        _positionSpans?.Dispose();
        _positionSpans = null;
    }

    private BudgetedParserList<SvgTextLetterSpacingSpan> CreateLetterSpacingSpans(int start)
    {
        var spans = new BudgetedParserList<SvgTextLetterSpacingSpan>(_budget, SvgMemoryCategory.Parser, _workCounter, _cancellationToken);
        if (start != 0) spans.Add(new SvgTextLetterSpacingSpan(0, start, _defaultLetterSpacing) { WordSpacing = _defaultWordSpacing });
        return spans;
    }
}

/// <summary>Tracks one active positioning declaration and the interval first emitted by its subtree.</summary>
internal readonly record struct SvgTextPositionScope(int ScopeDepth, SvgTextPositionOffset? Offset, SvgTextPositionLists Lists, int SpanIndex);

/// <summary>Owns normalized text and optional spacing, coordinate and rotation storage until text layout completes.</summary>
internal readonly record struct BudgetedTextLayout(string Value, SvgTextLetterSpacingSpan[] LetterSpacingSpans, SvgTextPositionAdjustment[] PositionAdjustments, RenderBudget.BudgetReservation? TextReservation, RenderBudget.BudgetReservation? LetterSpacingReservation, RenderBudget.BudgetReservation? PositionAdjustmentReservation) : IDisposable
{
    public void Dispose()
    {
        TextReservation?.Dispose();
        LetterSpacingReservation?.Dispose();
        PositionAdjustmentReservation?.Dispose();
    }
}

/// <summary>Owns a growable parser array whose backing storage is reserved before allocation.</summary>
internal sealed class BudgetedParserList<T> : IDisposable where T : struct
{
    private const int CopyCheckpointItems = 4_096;
    private readonly RenderBudget _budget;
    private readonly SvgMemoryCategory _category;
    private readonly ParserWorkCounter? _workCounter;
    private readonly CancellationToken _cancellationToken;
    private T[] _items = [];
    private RenderBudget.BudgetReservation? _reservation;
    private int _count;

    internal BudgetedParserList(RenderBudget budget, SvgMemoryCategory category)
        : this(budget, category, null, CancellationToken.None)
    {
    }

    /// <summary>Creates a value-type list whose exact runtime element size is charged before allocation.</summary>
    internal BudgetedParserList(RenderBudget budget, SvgMemoryCategory category, ParserWorkCounter? workCounter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(budget);
        _budget = budget;
        _category = category;
        _workCounter = workCounter;
        _cancellationToken = cancellationToken;
    }

    internal int Count => _count;

    internal T this[int index]
    {
        get => _items[index];
        set => _items[index] = value;
    }

    internal T this[Index index]
    {
        get => _items[index.GetOffset(_count)];
        set => _items[index.GetOffset(_count)] = value;
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

    internal ReadOnlySpan<T> AsSpan() => _items.AsSpan(0, _count);

    /// <summary>Views retained slots only; growth invalidates the view. Callers charge mutation work,
    /// observe cancellation and preserve value ownership without changing the collection count.</summary>
    internal Span<T> AsMutableSpan() => _items.AsSpan(0, _count);

    internal void Clear()
    {
        _count = 0;
    }

    internal void Sort(ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        if (_count < 2) return;
        Comparer<T> comparer = Comparer<T>.Default;
        for (int root = _count / 2 - 1; root >= 0; root--) SiftDown(root, _count, comparer, workCounter, cancellationToken);
        for (int end = _count - 1; end > 0; end--)
        {
            (_items[0], _items[end]) = (_items[end], _items[0]);
            SiftDown(0, end, comparer, workCounter, cancellationToken);
        }
    }

    internal void RemoveLast()
    {
        if (_count == 0) throw new InvalidOperationException("The parser collection is empty.");
        _count--;
        _items[_count] = default;
    }

    internal bool Any(Func<T, bool> predicate)
    {
        for (int index = 0; index < _count; index++)
        {
            if (predicate(_items[index])) return true;
        }
        return false;
    }

    internal OwnedParserArray<T> Detach()
    {
        if (_count == 0)
        {
            Dispose();
            return new([], null);
        }

        RenderBudget.BudgetReservation reservation = _budget.Reserve(ParserCollectionMemory.GetArrayByteCount<T>(_count), _category);
        T[] exact;
        try
        {
            exact = new T[_count];
        }
        catch
        {
            reservation.Dispose();
            throw;
        }

        try { CopyWithCheckpoints(_items.AsSpan(0, _count), exact); }
        catch { reservation.Dispose(); throw; }
        Dispose();
        return new OwnedParserArray<T>(exact, reservation);
    }

    public void Dispose()
    {
        _items = [];
        _count = 0;
        _reservation?.Dispose();
        _reservation = null;
    }

    private void EnsureAdditionalCapacity(int additionalCount)
    {
        if (additionalCount == 0) return;
        int requiredCount;
        try
        {
            requiredCount = checked(_count + additionalCount);
        }
        catch (OverflowException)
        {
            throw new SvgException(SvgErrorCode.LimitExceeded, "The SVG parser collection exceeds the supported array capacity.");
        }

        if (requiredCount <= _items.Length) return;
        int capacity = _items.Length == 0 ? Math.Max(4, requiredCount) : _items.Length > int.MaxValue / 2 ? requiredCount : Math.Max(_items.Length * 2, requiredCount);
        RenderBudget.BudgetReservation replacementReservation = _budget.Reserve(ParserCollectionMemory.GetArrayByteCount<T>(capacity), _category);
        T[] replacement;
        try
        {
            replacement = new T[capacity];
        }
        catch
        {
            replacementReservation.Dispose();
            throw;
        }

        try { CopyWithCheckpoints(_items.AsSpan(0, _count), replacement); }
        catch { replacementReservation.Dispose(); throw; }
        _reservation?.Dispose();
        _reservation = replacementReservation;
        _items = replacement;
    }

    private void CopyWithCheckpoints(ReadOnlySpan<T> source, Span<T> destination)
    {
        if (_workCounter is null)
        {
            source.CopyTo(destination);
            return;
        }
        for (int offset = 0; offset < source.Length; offset += CopyCheckpointItems)
        {
            _workCounter.Consume(_cancellationToken);
            int count = Math.Min(CopyCheckpointItems, source.Length - offset);
            source.Slice(offset, count).CopyTo(destination.Slice(offset, count));
        }
    }

    private void SiftDown(int root, int length, Comparer<T> comparer, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        while (root < length / 2)
        {
            int child = root * 2 + 1;
            int largest = Compare(root, child, comparer, workCounter, cancellationToken) < 0 ? child : root;
            if (child + 1 < length && Compare(largest, child + 1, comparer, workCounter, cancellationToken) < 0) largest = child + 1;
            if (largest == root) return;
            (_items[root], _items[largest]) = (_items[largest], _items[root]);
            root = largest;
        }
    }

    private int Compare(int left, int right, Comparer<T> comparer, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        workCounter.Consume(cancellationToken);
        return comparer.Compare(_items[left], _items[right]);
    }
}

/// <summary>Owns a parser array that remains charged until its consumer releases it.</summary>
internal readonly record struct OwnedParserArray<T>(T[] Items, RenderBudget.BudgetReservation? Reservation) : IDisposable where T : struct
{
    public void Dispose() => Reservation?.Dispose();
}

/// <summary>Tracks dictionary growth before definition entries allocate parser-owned storage.</summary>
internal sealed class BudgetedParserDictionary<TKey, TValue> : IDisposable where TKey : notnull where TValue : struct
{
    private readonly RenderBudget _budget;
    private readonly SvgMemoryCategory _category;
    private readonly int _entryBytes;
    private Dictionary<TKey, TValue> _items = [];
    private RenderBudget.BudgetReservation? _reservation;
    private int _capacity;

    /// <summary>Uses a conservative per-entry capacity charge for the dictionary's bucket and entry arrays.</summary>
    internal BudgetedParserDictionary(RenderBudget budget, SvgMemoryCategory category, int entryBytes)
    {
        if (entryBytes <= 0) throw new ArgumentOutOfRangeException(nameof(entryBytes));
        _budget = budget;
        _category = category;
        _entryBytes = entryBytes;
    }

    internal bool TryAdd(TKey key, TValue value)
    {
        if (_items.ContainsKey(key)) return false;
        EnsureAdditionalCapacity(1);
        _items.Add(key, value);
        return true;
    }

    internal bool TryGetValue(TKey key, out TValue value) => _items.TryGetValue(key, out value);

    /// <summary>Replaces a known entry without allocation or growth; the caller charges work and owns any value resources.</summary>
    internal void Replace(TKey key, TValue value)
    {
        if (!_items.ContainsKey(key)) throw new InvalidOperationException("Only retained dictionary entries can be replaced.");
        _items[key] = value;
    }

    /// <summary>Enumerates retained values so owners can release value-owned resources on teardown.</summary>
    internal IEnumerable<TValue> Values => _items.Values;

    public void Dispose()
    {
        _items = [];
        _capacity = 0;
        _reservation?.Dispose();
        _reservation = null;
    }

    private void EnsureAdditionalCapacity(int additionalCount)
    {
        int requiredCount;
        try
        {
            requiredCount = checked(_items.Count + additionalCount);
        }
        catch (OverflowException)
        {
            throw new SvgException(SvgErrorCode.LimitExceeded, "The SVG definition table exceeds the supported capacity.");
        }

        if (requiredCount <= _capacity) return;
        int requestedCapacity = _capacity == 0 ? Math.Max(4, requiredCount) : _capacity > int.MaxValue / 2 ? requiredCount : Math.Max(_capacity * 2, requiredCount);
        int reservedCapacity;
        try
        {
            // Dictionary.EnsureCapacity selects the first prime at or above the request.
            // For requests of at least four, the next prime is below twice the request.
            reservedCapacity = checked(requestedCapacity * 2);
        }
        catch (OverflowException)
        {
            throw new SvgException(SvgErrorCode.LimitExceeded, "The SVG definition table exceeds the supported capacity.");
        }
        RenderBudget.BudgetReservation replacementReservation = _budget.Reserve(ParserCollectionMemory.GetArrayByteCount(reservedCapacity, _entryBytes), _category);
        int actualCapacity;
        try
        {
            actualCapacity = _items.EnsureCapacity(requestedCapacity);
        }
        catch
        {
            replacementReservation.Dispose();
            throw;
        }

        if (actualCapacity > reservedCapacity)
        {
            replacementReservation.Dispose();
            throw new SvgException(SvgErrorCode.LimitExceeded, "The XML definition table selected a capacity outside its pre-allocation budget.");
        }

        _reservation?.Dispose();
        _reservation = replacementReservation;
        _capacity = actualCapacity;
    }
}
