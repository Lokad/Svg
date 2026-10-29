namespace Lokad.Svg;

/// <summary>Owns encoded PNG storage and its host memory charge until disposal.</summary>
/// <remarks>
/// Returned memory is borrowed from this result. Finish all readers and async
/// writes before disposal; reading and disposal must not race. Disposal drops the
/// owned storage and refunds its scope, without disposing the host's root budget.
/// The host keeps that borrowed root alive for the complete result lifetime.
/// </remarks>
public sealed class SvgPngResult : IDisposable
{
    private byte[]? _png;
    private IMemoryScope? _owner;

    internal SvgPngResult(byte[] png, IMemoryScope? owner) { _png = png; _owner = owner; }

    /// <summary>Gets the PNG bytes borrowed until this result is disposed.</summary>
    public ReadOnlyMemory<byte> Memory => _png ?? throw new ObjectDisposedException(nameof(SvgPngResult));

    /// <summary>Releases this result's storage and charge exactly once after its borrowers finish.</summary>
    public void Dispose()
    {
        _png = null;
        Interlocked.Exchange(ref _owner, null)?.Dispose();
    }
}
