namespace Lokad.Svg.Rendering;

/// <summary>
/// Reserves renderer-owned storage before allocation. This type is single-threaded
/// and belongs to exactly one conversion context.
/// </summary>
internal sealed class RenderBudget
{
    private readonly IMemoryBudget? _sharedBudget;
    private readonly long[] _bytesByCategory = new long[Enum.GetValues<SvgMemoryCategory>().Length];
    private long _currentBytes;
    private long _peakBytes;
    private long _deniedReservationCount;

    /// <summary>Records ungoverned storage ownership without introducing a memory allowance.</summary>
    internal RenderBudget() { }

    /// <summary>Borrows a host owner without introducing a cap or copying its available balance.</summary>
    internal RenderBudget(IMemoryBudget budget)
    {
        ArgumentNullException.ThrowIfNull(budget);
        _sharedBudget = budget;
    }

    internal BudgetReservation Reserve(long bytes, SvgMemoryCategory category)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bytes);
        IMemoryReservation? reservation = null;
        if (_sharedBudget is not null && !_sharedBudget.TryReserve(bytes, out reservation)) Deny();
        RecordCharge(bytes, category);
        return new BudgetReservation(this, bytes, category, reservation);
    }

    private void Deny()
    {
        _deniedReservationCount++;
        throw new SvgException(SvgErrorCode.LimitExceeded, "The memory budget refused renderer-owned storage.");
    }

    private void RecordCharge(long bytes, SvgMemoryCategory category)
    {
        _currentBytes += bytes;
        _bytesByCategory[(int)category] += bytes;
        if (_currentBytes > _peakBytes)
        {
            _peakBytes = _currentBytes;
        }
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

    /// <summary>Represents one storage allowance and releases it exactly once when disposed; growth is confined to the owning conversion thread.</summary>
    internal sealed class BudgetReservation : IDisposable
    {
        private RenderBudget? _owner;
        private long _bytes;
        private readonly SvgMemoryCategory _category;
        private readonly IMemoryReservation? _shared;

        internal BudgetReservation(RenderBudget owner, long bytes, SvgMemoryCategory category, IMemoryReservation? shared)
        {
            _owner = owner;
            _bytes = bytes;
            _category = category;
            _shared = shared;
        }

        /// <summary>Adds an aggregate allowance before material allocation; denial leaves both owner and reservation unchanged. Must not race disposal.</summary>
        internal void Grow(long bytes)
        {
            RenderBudget owner = _owner ?? throw new ObjectDisposedException(nameof(BudgetReservation));
            ArgumentOutOfRangeException.ThrowIfNegative(bytes);
            long total = checked(_bytes + bytes);
            if (_shared is not null && !_shared.TryResize(total)) owner.Deny();
            owner.RecordCharge(bytes, _category);
            _bytes += bytes;
        }

        public void Dispose()
        {
            RenderBudget? owner = Interlocked.Exchange(ref _owner, null);
            if (owner is not null)
            {
                _shared?.Dispose();
                owner.Release(_bytes, _category);
            }
        }
    }
}
