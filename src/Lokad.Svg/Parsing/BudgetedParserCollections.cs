namespace Lokad.Svg.Parsing;

using Lokad.Svg.Rendering;

/// <summary>Tracks parser collection backing storage against a conversion budget.</summary>
internal static class ParserCollectionMemory
{
    private const int ArrayHeaderBytes = 32;

    internal static long GetArrayByteCount(int count, int entryBytes)
        => count == 0 ? 0 : checked(ArrayHeaderBytes + (long)count * entryBytes);
}

/// <summary>Owns exact parser-string reservations for copies retained by stylesheet rules or deferred references.</summary>
internal sealed class BudgetedParserStringStore : IDisposable
{
    private const long StringHeaderBytes = 32;
    // Covers the reservation object, the list reference, and the first/grown
    // reservation-pointer array. The first list entry is the worst amortized case.
    private const long PerStringTrackingBytes = 128;
    private readonly RenderBudget _budget;
    private List<RenderBudget.BudgetReservation>? _reservations;

    internal BudgetedParserStringStore(RenderBudget budget) => _budget = budget;

    internal string Copy(ReadOnlySpan<char> value)
    {
        if (value.IsEmpty) return string.Empty;
        long bytes = checked(StringHeaderBytes + (long)value.Length * sizeof(char) + PerStringTrackingBytes);
        RenderBudget.BudgetReservation reservation = _budget.Reserve(bytes, SvgMemoryCategory.Parser);
        try
        {
            string copy = value.ToString();
            (_reservations ??= []).Add(reservation);
            return copy;
        }
        catch
        {
            reservation.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        if (_reservations is null) return;
        foreach (RenderBudget.BudgetReservation reservation in _reservations) reservation.Dispose();
        _reservations = null;
    }
}

/// <summary>Builds one transient direct-text value with parser-budgeted backing storage and transfers its exact string reservation to the caller.</summary>
internal sealed class BudgetedTextBuffer : IDisposable
{
    private const long StringHeaderBytes = 32;
    private const int CopyCheckpointCharacters = 4_096;
    private readonly RenderBudget _budget;
    private char[] _characters = [];
    private RenderBudget.BudgetReservation? _reservation;
    private int _length;

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
        ArgumentNullException.ThrowIfNull(workCounter);
        if (value.Length > maximumLength - _length) throw new SvgException(SvgErrorCode.LimitExceeded, "The document direct text character count exceeds the admitted layout limit.");
        EnsureCapacity(checked(_length + value.Length), workCounter, cancellationToken);
        for (int offset = 0; offset < value.Length; offset += CopyCheckpointCharacters)
        {
            workCounter.Consume(cancellationToken);
            int count = Math.Min(CopyCheckpointCharacters, value.Length - offset);
            value.AsSpan(offset, count).CopyTo(_characters.AsSpan(_length + offset));
        }
        _length += value.Length;
    }

    /// <summary>Transfers ownership of the exact final string reservation; the caller must dispose the returned value after layout.</summary>
    internal BudgetedText Detach()
    {
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

/// <summary>Owns a growable parser array whose backing storage is reserved before allocation.</summary>
internal sealed class BudgetedParserList<T> : IDisposable where T : struct
{
    private const int CopyCheckpointItems = 4_096;
    private readonly RenderBudget _budget;
    private readonly SvgMemoryCategory _category;
    private readonly int _entryBytes;
    private readonly ParserWorkCounter? _workCounter;
    private readonly CancellationToken _cancellationToken;
    private T[] _items = [];
    private RenderBudget.BudgetReservation? _reservation;
    private int _count;

    internal BudgetedParserList(RenderBudget budget, SvgMemoryCategory category, int entryBytes, ParserWorkCounter? workCounter = null, CancellationToken cancellationToken = default)
    {
        if (entryBytes <= 0) throw new ArgumentOutOfRangeException(nameof(entryBytes));
        _budget = budget;
        _category = category;
        _entryBytes = entryBytes;
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

        RenderBudget.BudgetReservation reservation = _budget.Reserve(ParserCollectionMemory.GetArrayByteCount(_count, _entryBytes), _category);
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
        RenderBudget.BudgetReservation replacementReservation = _budget.Reserve(ParserCollectionMemory.GetArrayByteCount(capacity, _entryBytes), _category);
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
