namespace Lokad.Svg.Parsing;

using Lokad.Svg.Rendering;

/// <summary>Tracks parser collection backing storage against a conversion budget.</summary>
internal static class ParserCollectionMemory
{
    private const int ArrayHeaderBytes = 32;

    internal static long GetArrayByteCount(int count, int entryBytes)
        => count == 0 ? 0 : checked(ArrayHeaderBytes + (long)count * entryBytes);
}

/// <summary>Owns a growable parser array whose backing storage is reserved before allocation.</summary>
internal sealed class BudgetedParserList<T> : IDisposable where T : struct
{
    private readonly RenderBudget _budget;
    private readonly SvgMemoryCategory _category;
    private readonly int _entryBytes;
    private T[] _items = [];
    private RenderBudget.BudgetReservation? _reservation;
    private int _count;

    internal BudgetedParserList(RenderBudget budget, SvgMemoryCategory category, int entryBytes)
    {
        if (entryBytes <= 0) throw new ArgumentOutOfRangeException(nameof(entryBytes));
        _budget = budget;
        _category = category;
        _entryBytes = entryBytes;
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
        items.CopyTo(_items.AsSpan(_count));
        _count += items.Length;
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

        _items.AsSpan(0, _count).CopyTo(replacement);
        _reservation?.Dispose();
        _reservation = replacementReservation;
        _items = replacement;
    }
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
        RenderBudget.BudgetReservation replacementReservation = _budget.Reserve(ParserCollectionMemory.GetArrayByteCount(requestedCapacity, _entryBytes), _category);
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

        _reservation?.Dispose();
        _reservation = replacementReservation;
        _capacity = actualCapacity;
    }
}
