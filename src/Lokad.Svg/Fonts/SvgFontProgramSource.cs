namespace Lokad.Svg.Fonts;

using System.Security.Cryptography;
using Lokad.Svg.Parsing;

/// <summary>
/// Supplies one immutable font program. The caller owns source lifetime, storage,
/// caching and thread safety; returned bytes must remain unchanged and valid until
/// the awaiting conversion releases them. Implementations must observe cancellation.
/// </summary>
public interface ISvgFontProgramSource
{
    /// <summary>Gets a stable, bounded diagnostic identity; it is not an integrity credential.</summary>
    string StableId { get; }

    /// <summary>Asynchronously gets the caller-owned font-program bytes.</summary>
    ValueTask<ReadOnlyMemory<byte>> GetBytesAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Locates caller-owned storage for one manifest-declared font program. The caller
/// owns the provider's lifetime, storage, cache policy, concurrency and retries.
/// Implementations must observe cancellation and must not return a source for a
/// different program. Returning <see langword="null"/> reports that the program
/// is unavailable; this library neither probes the file system nor performs I/O.
/// </summary>
public interface ISvgFontProgramSourceProvider
{
    /// <summary>Asynchronously locates storage for the exact manifest file.</summary>
    ValueTask<ISvgFontProgramSource?> GetSourceAsync(SvgFontPackFile file, CancellationToken cancellationToken);
}

/// <summary>Provides immutable caller-owned bytes without copying or retaining a cache.</summary>
public sealed class SvgMemoryFontProgramSource : ISvgFontProgramSource
{
    private readonly ReadOnlyMemory<byte> _bytes;

    /// <summary>Initializes a source whose caller retains the supplied bytes for every consumer.</summary>
    public SvgMemoryFontProgramSource(string stableId, ReadOnlyMemory<byte> bytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stableId);
        StableId = stableId;
        _bytes = bytes;
    }

    /// <inheritdoc />
    public string StableId { get; }

    /// <inheritdoc />
    public ValueTask<ReadOnlyMemory<byte>> GetBytesAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(_bytes);
    }
}

/// <summary>Represents one manifest-selected face bound to an integrity-verifying program source.</summary>
public sealed class SvgFontProgramResolution
{
    internal SvgFontProgramResolution(SvgFontPackResolution resolution, ISvgFontProgramSource source)
    {
        Resolution = resolution;
        Source = source;
    }

    /// <summary>Gets the metadata selection that led to this program source.</summary>
    public SvgFontPackResolution Resolution { get; }

    /// <summary>Gets a source that verifies declared size and hash on every returned byte sequence.</summary>
    public ISvgFontProgramSource Source { get; }
}

internal sealed class VerifiedSvgFontProgramSource(string packId, SvgFontPackFace face, ISvgFontProgramSource source) : ISvgFontProgramSource
{
    public string StableId => "lokad-svg-font-pack:" + packId + ":" + face.File.Sha256 + ":" + face.FaceIndex;

    public ValueTask<ReadOnlyMemory<byte>> GetBytesAsync(CancellationToken cancellationToken)
        => GetBytesAsync(null, cancellationToken);

    /// <summary>Gets and verifies declared bytes while charging a caller-owned preparation counter when one exists.</summary>
    internal ValueTask<ReadOnlyMemory<byte>> GetBytesAsync(ParserWorkCounter? workCounter, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValueTask<ReadOnlyMemory<byte>> pending = source.GetBytesAsync(cancellationToken);
        return pending.IsCompletedSuccessfully ? ValueTask.FromResult(Validate(pending.Result, workCounter, cancellationToken)) : AwaitAndValidate(pending, workCounter, cancellationToken);
    }

    private ValueTask<ReadOnlyMemory<byte>> AwaitAndValidate(ValueTask<ReadOnlyMemory<byte>> pending, ParserWorkCounter? workCounter, CancellationToken cancellationToken)
    {
        return new ValueTask<ReadOnlyMemory<byte>>(AwaitAsync(pending, workCounter, cancellationToken));
    }

    private async Task<ReadOnlyMemory<byte>> AwaitAsync(ValueTask<ReadOnlyMemory<byte>> pending, ParserWorkCounter? workCounter, CancellationToken cancellationToken)
    {
        ReadOnlyMemory<byte> bytes = await pending.ConfigureAwait(false);
        return Validate(bytes, workCounter, cancellationToken);
    }

    private ReadOnlyMemory<byte> Validate(ReadOnlyMemory<byte> bytes, ParserWorkCounter? workCounter, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (bytes.Length != face.File.ByteSize) throw new SvgException(SvgErrorCode.UnresolvedResource, "The selected font program has an unexpected byte size.");
        string hash = SvgFontProgramIntegrity.ComputeSha256(bytes, workCounter, cancellationToken);
        if (!hash.Equals(face.File.Sha256, StringComparison.OrdinalIgnoreCase)) throw new SvgException(SvgErrorCode.UnresolvedResource, "The selected font program SHA-256 does not match its manifest.");
        return bytes;
    }
}

/// <summary>Hashes caller-owned font bytes in bounded, cancellable checkpoints shared by source and snapshot integrity validation.</summary>
internal static class SvgFontProgramIntegrity
{
    internal static string ComputeSha256(ReadOnlyMemory<byte> bytes, ParserWorkCounter? workCounter, CancellationToken cancellationToken)
    {
        using IncrementalHash hashing = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        ReadOnlySpan<byte> programBytes = bytes.Span;
        for (int offset = 0; offset < programBytes.Length; offset += ParserWorkCounter.ByteCheckpointSize)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int length = Math.Min(ParserWorkCounter.ByteCheckpointSize, programBytes.Length - offset);
            workCounter?.Consume(cancellationToken);
            hashing.AppendData(programBytes.Slice(offset, length));
            FontIntegrityProbe.HashChunkCompleted();
        }
        return Convert.ToHexString(hashing.GetHashAndReset());
    }
}
