namespace Lokad.Svg;

/// <summary>Restores one diagnostic callback slot when its owning execution context exits a probe scope.</summary>
/// <remarks>Dispose on the execution context that armed the scope. Nested scopes restore their
/// prior value once; inherited child contexts retain their own callback value. This owner is
/// allocated only while a probe is armed and is absent from unarmed production hot paths.</remarks>
internal sealed class SvgProbeScope<T>(AsyncLocal<T?> slot, T? previous) : IDisposable where T : class
{
    private AsyncLocal<T?>? _slot = slot;
    private T? _previous = previous;

    public void Dispose()
    {
        AsyncLocal<T?>? owned = Interlocked.Exchange(ref _slot, null);
        if (owned is not null) owned.Value = Interlocked.Exchange(ref _previous, null);
    }
}
