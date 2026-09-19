namespace Lokad.Svg.Rendering;

/// <summary>
/// Reserves renderer-owned storage before allocation. This type is single-threaded
/// and belongs to exactly one conversion context.
/// </summary>
internal sealed class RenderBudget
{
    private readonly long _maximumBytes;
    private readonly long[] _bytesByCategory = new long[Enum.GetValues<SvgMemoryCategory>().Length];
    private long _currentBytes;
    private long _peakBytes;
    private long _deniedReservationCount;

    internal RenderBudget(long maximumBytes)
    {
        if (maximumBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        }

        _maximumBytes = maximumBytes;
    }

    internal BudgetReservation Reserve(long bytes, SvgMemoryCategory category)
    {
        if (bytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(bytes));
        }

        if (bytes > _maximumBytes - _currentBytes)
        {
            _deniedReservationCount++;
            throw new SvgException(SvgErrorCode.LimitExceeded, "The conversion would exceed its renderer-owned memory budget.");
        }

        _currentBytes += bytes;
        _bytesByCategory[(int)category] += bytes;
        if (_currentBytes > _peakBytes)
        {
            _peakBytes = _currentBytes;
        }

        return new BudgetReservation(this, bytes, category);
    }

    internal RenderBudgetSnapshot Snapshot()
    {
        return new RenderBudgetSnapshot(_currentBytes, _peakBytes, _deniedReservationCount, (long[])_bytesByCategory.Clone());
    }

    private void Release(long bytes, SvgMemoryCategory category)
    {
        _currentBytes -= bytes;
        _bytesByCategory[(int)category] -= bytes;
    }

    /// <summary>Represents one reservation and releases it exactly once when disposed.</summary>
    internal sealed class BudgetReservation : IDisposable
    {
        private RenderBudget? _owner;
        private readonly long _bytes;
        private readonly SvgMemoryCategory _category;

        internal BudgetReservation(RenderBudget owner, long bytes, SvgMemoryCategory category)
        {
            _owner = owner;
            _bytes = bytes;
            _category = category;
        }

        public void Dispose()
        {
            RenderBudget? owner = Interlocked.Exchange(ref _owner, null);
            if (owner is not null)
            {
                owner.Release(_bytes, _category);
            }
        }
    }
}
