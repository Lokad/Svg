using Lokad.Svg.Fonts;

namespace Lokad.Svg.Testing;

/// <summary>Rejects unexpected acquisition in font-free test fixtures.</summary>
/// <remarks>Owns no sources, storage, I/O or budget reservations. Immutable instances permit concurrent
/// calls. The caller selects cancellation-first rejection or an unconditional synchronous failure,
/// which also detects unexpected acquisition during a pre-cancelled font-free operation.</remarks>
internal sealed class RejectFontProgramSourceProvider(string message, bool observeCancellation) : ISvgFontProgramSourceProvider
{
    /// <summary>Rejects synchronously with the fixture's message, observing the caller-selected cancellation policy.</summary>
    /// <remarks>File metadata and the token remain borrowed; no source or asynchronous work is created.</remarks>
    public ValueTask<ISvgFontProgramSource?> GetSourceAsync(SvgFontPackFile file, CancellationToken cancellationToken)
    {
        if (observeCancellation) cancellationToken.ThrowIfCancellationRequested();
        throw new InvalidOperationException(message);
    }
}
