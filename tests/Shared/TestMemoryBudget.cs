using System.Diagnostics.CodeAnalysis;

namespace Lokad.Svg.Testing;

// A small host implementation for library ownership tests, not production governance.
internal sealed class TestMemoryBudget : IMemoryScope
{
    private sealed class State(long limit) { internal readonly object Gate = new(); internal long Limit = limit; internal long Reserved; }
    private readonly State _state;
    private readonly TestMemoryBudget? _parent;
    private readonly List<TestMemoryBudget> _children = [];
    private readonly List<Reservation> _reservations = [];
    internal bool Disposed { get; private set; }
    internal long Reserved { get { lock (_state.Gate) return _state.Reserved; } }
    internal long Limit { set { lock (_state.Gate) _state.Limit = value; } }
    internal TestMemoryBudget(long limit) { _state = new(limit); }
    private TestMemoryBudget(TestMemoryBudget parent) { _parent = parent; _state = parent._state; }
    private void CheckActive()
    {
        for (TestMemoryBudget? owner = this; owner is not null; owner = owner._parent)
            ObjectDisposedException.ThrowIf(owner.Disposed, owner);
    }
    /// <inheritdoc />
    public IMemoryScope CreateScope()
    {
        lock (_state.Gate) { CheckActive(); var child = new TestMemoryBudget(this); _children.Add(child); return child; }
    }
    /// <inheritdoc />
    public bool TryReserve(long bytes, [NotNullWhen(true)] out IMemoryReservation? reservation)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bytes);
        lock (_state.Gate)
        {
            CheckActive();
            if (bytes > _state.Limit - _state.Reserved) { reservation = null; return false; }
            var created = new Reservation(this, bytes); _reservations.Add(created); _state.Reserved += bytes; reservation = created; return true;
        }
    }
    public void Dispose()
    {
        lock (_state.Gate)
        {
            if (Disposed) return;
            foreach (TestMemoryBudget child in _children) child.Dispose();
            foreach (Reservation reservation in _reservations) reservation.Dispose();
            Disposed = true;
        }
    }
    private sealed class Reservation(TestMemoryBudget owner, long bytes) : IMemoryReservation
    {
        private bool _disposed;
        private long _bytes = bytes;
        /// <inheritdoc />
        public long Bytes { get { lock (owner._state.Gate) return _bytes; } }
        /// <inheritdoc />
        public bool TryResize(long value)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            lock (owner._state.Gate)
            {
                owner.CheckActive(); ObjectDisposedException.ThrowIf(_disposed, this);
                long increase = value - _bytes;
                if (increase > owner._state.Limit - owner._state.Reserved) return false;
                owner._state.Reserved += increase; _bytes = value; return true;
            }
        }
        public void Dispose()
        {
            lock (owner._state.Gate)
            {
                if (_disposed) return;
                owner._state.Reserved -= _bytes; _bytes = 0; _disposed = true;
            }
        }
    }
}
