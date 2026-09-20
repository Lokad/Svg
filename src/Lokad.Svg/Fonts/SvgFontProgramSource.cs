namespace Lokad.Svg.Fonts;

using System.Security.Cryptography;

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
    {
        ValueTask<ReadOnlyMemory<byte>> pending = source.GetBytesAsync(cancellationToken);
        return pending.IsCompletedSuccessfully ? ValueTask.FromResult(Validate(pending.Result)) : AwaitAndValidate(pending);
    }

    private ValueTask<ReadOnlyMemory<byte>> AwaitAndValidate(ValueTask<ReadOnlyMemory<byte>> pending)
    {
        return new ValueTask<ReadOnlyMemory<byte>>(AwaitAsync(pending));
    }

    private async Task<ReadOnlyMemory<byte>> AwaitAsync(ValueTask<ReadOnlyMemory<byte>> pending)
    {
        return Validate(await pending.ConfigureAwait(false));
    }

    private ReadOnlyMemory<byte> Validate(ReadOnlyMemory<byte> bytes)
    {
        if (bytes.Length != face.File.ByteSize) throw new SvgException(SvgErrorCode.UnresolvedResource, "The selected font program has an unexpected byte size.");
        string hash = Convert.ToHexString(SHA256.HashData(bytes.Span));
        if (!hash.Equals(face.File.Sha256, StringComparison.OrdinalIgnoreCase)) throw new SvgException(SvgErrorCode.UnresolvedResource, "The selected font program SHA-256 does not match its manifest.");
        return bytes;
    }
}
