namespace Lokad.Svg.Rendering;

/// <summary>
/// Reserves renderer-owned storage before allocation. This type is single-threaded
/// and belongs to exactly one conversion context.
/// </summary>
internal sealed class RenderBudget
{
    private readonly long _maximumBytes;
    private readonly IMemoryBudget? _sharedBudget;
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

    /// <summary>Borrows a host owner without introducing a cap or copying its available balance.</summary>
    internal RenderBudget(IMemoryBudget budget)
    {
        ArgumentNullException.ThrowIfNull(budget);
        _sharedBudget = budget;
    }

    internal BudgetReservation Reserve(long bytes, SvgMemoryCategory category)
    {
        if (_sharedBudget is null)
        {
            Charge(bytes, category);
            return new BudgetReservation(this, bytes, category, null);
        }
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bytes);
        if (!_sharedBudget.TryReserve(bytes, out IMemoryReservation? reservation)) Deny();
        RecordCharge(bytes, category);
        return new BudgetReservation(this, bytes, category, reservation);
    }

    /// <summary>Legacy diagnostic allowance; host-governed operations do not expose or inspect a remaining balance.</summary>
    internal long AvailableBytes => _sharedBudget is null ? _maximumBytes - _currentBytes
        : throw new InvalidOperationException("A borrowed shared budget has no local available balance.");

    private void Charge(long bytes, SvgMemoryCategory category)
    {
        if (bytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(bytes));
        }

        if (bytes > _maximumBytes - _currentBytes)
        {
            Deny();
        }

        RecordCharge(bytes, category);
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
            if (_shared is null) owner.Charge(bytes, _category);
            else
            {
                ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bytes);
                if (!_shared.TryResize(checked(_bytes + bytes))) owner.Deny();
                owner.RecordCharge(bytes, _category);
            }
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
