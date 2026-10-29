namespace Lokad.Svg.Fonts;

/// <summary>Defines explicit transport and retained-capacity policy for an HTTP font-pack resolver.</summary>
public sealed class SvgHttpFontAcquisitionOptions
{
    /// <summary>Initializes bounded acquisition policy; these limits do not create a memory allowance.</summary>
    /// <remarks>
    /// The deadline covers all response headers, body reads and attempts of one
    /// shared acquisition. Attempts retry transient transport failures only;
    /// caller cancellation, admission refusal and invalid data are never retried.
    /// Cache capacity is independent of the host's shared memory admission.
    /// </remarks>
    public SvgHttpFontAcquisitionOptions(TimeSpan acquisitionTimeout, int maximumAttempts, TimeSpan retryDelay,
        int maximumManifestBytes, int maximumProgramBytes, long maximumCachedProgramBytes)
    {
        if (acquisitionTimeout <= TimeSpan.Zero || acquisitionTimeout.TotalMilliseconds > uint.MaxValue - 1)
            throw new ArgumentOutOfRangeException(nameof(acquisitionTimeout));
        if (maximumAttempts <= 0) throw new ArgumentOutOfRangeException(nameof(maximumAttempts));
        if (retryDelay < TimeSpan.Zero || retryDelay.TotalMilliseconds > uint.MaxValue - 1)
            throw new ArgumentOutOfRangeException(nameof(retryDelay));
        if (maximumManifestBytes <= 0 || maximumManifestBytes > 1024 * 1024)
            throw new ArgumentOutOfRangeException(nameof(maximumManifestBytes));
        if (maximumProgramBytes <= 0 || maximumProgramBytes > 32 * 1024 * 1024)
            throw new ArgumentOutOfRangeException(nameof(maximumProgramBytes));
        if (maximumCachedProgramBytes < 0) throw new ArgumentOutOfRangeException(nameof(maximumCachedProgramBytes));
        AcquisitionTimeout = acquisitionTimeout;
        MaximumAttempts = maximumAttempts;
        RetryDelay = retryDelay;
        MaximumManifestBytes = maximumManifestBytes;
        MaximumProgramBytes = maximumProgramBytes;
        MaximumCachedProgramBytes = maximumCachedProgramBytes;
    }

    /// <summary>Gets a finite deadline for the entire shared acquisition, including retry delays.</summary>
    public TimeSpan AcquisitionTimeout { get; }
    /// <summary>Gets the total maximum number of transient transport attempts.</summary>
    public int MaximumAttempts { get; }
    /// <summary>Gets the delay between configured transient attempts.</summary>
    public TimeSpan RetryDelay { get; }
    /// <summary>Gets the maximum manifest body size, including bodies without a declared length.</summary>
    public int MaximumManifestBytes { get; }
    /// <summary>Gets the maximum declared and received size of an individual font program.</summary>
    public int MaximumProgramBytes { get; }
    /// <summary>Gets idle program capacity; zero disables idle retention. Active borrowers remain charged to the host budget and document limits.</summary>
    public long MaximumCachedProgramBytes { get; }
}
