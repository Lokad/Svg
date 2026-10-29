using System.Diagnostics.CodeAnalysis;

namespace Lokad.Svg;

/// <summary>Lets a host admit estimated live storage across cooperating operations.</summary>
/// <remarks>
/// The library borrows this budget and never disposes it. Implementations share one
/// root allowance across all scopes; they must not copy a remaining balance into a
/// private limit. Admission is thread-safe and immediate, without waiting, retries
/// or storage allocation. Estimates cover retained capacity and temporary peaks;
/// releasing an owner makes its allowance reusable without waiting for collection.
/// Scope disposal must be serialized with every operation using its subtree.
/// </remarks>
public interface IMemoryBudget
{
    /// <summary>Creates an empty, caller-owned scope sharing this budget's root allowance.</summary>
    /// <remarks>
    /// Creation reserves no bytes and introduces no independent cap. The creator
    /// keeps the scope alive until all borrowers and asynchronous cleanup finish.
    /// Throws <see cref="ObjectDisposedException"/> when this owner is disposed.
    /// </remarks>
    IMemoryScope CreateScope();

    /// <summary>Immediately reserves a nonnegative estimate, or returns false and null without changing accounting.</summary>
    /// <remarks>
    /// Zero is valid. Negative estimates throw <see cref="ArgumentOutOfRangeException"/>;
    /// a disposed owner throws <see cref="ObjectDisposedException"/>. A successful
    /// reservation belongs to the caller until disposal or ancestor disposal.
    /// Reservation itself does not allocate or cancel the associated storage.
    /// </remarks>
    bool TryReserve(long bytes, [NotNullWhen(true)] out IMemoryReservation? reservation);
}

/// <summary>Owns a subtree of reservations sharing the original root allowance.</summary>
/// <remarks>
/// Disposal is idempotent and non-throwing: it refunds the subtree and invalidates
/// descendants, without canceling work or disposing their runtime objects. The
/// owner must first finish all borrowers and cleanup. Scope disposal must not race
/// admission, resizing, disposal or other uses anywhere in its subtree.
/// </remarks>
public interface IMemoryScope : IMemoryBudget, IDisposable
{
}

/// <summary>Owns one live-storage estimate until disposal or ancestor disposal.</summary>
/// <remarks>
/// Disposal is idempotent and non-throwing and refunds this charge immediately.
/// It does not free or cancel runtime storage. The owner finishes all borrowers
/// before disposal and serializes resizing and disposal of this reservation.
/// </remarks>
public interface IMemoryReservation : IDisposable
{
    /// <summary>Gets the current estimate, or zero after this reservation or an ancestor is disposed.</summary>
    long Bytes { get; }

    /// <summary>Replaces the estimate immediately; refusal returns false and preserves the old charge.</summary>
    /// <remarks>
    /// Zero is valid. Negative estimates throw <see cref="ArgumentOutOfRangeException"/>;
    /// a disposed reservation or ancestor throws <see cref="ObjectDisposedException"/>.
    /// The caller accounts for old-plus-new capacity before replacing storage.
    /// Resizing neither allocates storage nor waits for allowance.
    /// </remarks>
    bool TryResize(long bytes);
}
