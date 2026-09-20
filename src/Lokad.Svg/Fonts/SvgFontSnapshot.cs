namespace Lokad.Svg.Fonts;

/// <summary>
/// Represents one immutable, caller-byte-backed set of selected standalone
/// TrueType fonts. The caller keeps every returned byte region valid and unchanged
/// until this snapshot is disposed. Instances are safe for concurrent reads after
/// successful creation, but do not own a shared cache or perform I/O.
/// </summary>
public sealed class SvgFontSnapshot : IDisposable
{
    private const long MaximumBorrowedBytes = 32L * 1024 * 1024;
    private SvgPreparedFont[] _fonts;

    private SvgFontSnapshot(SvgPreparedFont[] fonts) => _fonts = fonts;

    /// <summary>Gets verified selected faces and their caller-owned program bytes.</summary>
    public IReadOnlyList<SvgPreparedFont> Fonts => _fonts;

    /// <summary>
    /// Asynchronously verifies and parses the selected caller-owned programs into
    /// one bounded snapshot. The input order is retained; duplicate source IDs are
    /// rejected to keep byte accounting unambiguous.
    /// </summary>
    public static async ValueTask<SvgFontSnapshot> CreateAsync(IReadOnlyList<SvgFontProgramResolution> resolutions, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(resolutions);
        cancellationToken.ThrowIfCancellationRequested();
        var sourceIds = new HashSet<string>(StringComparer.Ordinal);
        var prepared = new SvgPreparedFont[resolutions.Count];
        long totalBytes = 0;
        for (int index = 0; index < resolutions.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SvgFontProgramResolution resolution = resolutions[index] ?? throw new ArgumentException("The selected font list contains a null resolution.", nameof(resolutions));
            if (!sourceIds.Add(resolution.Source.StableId)) throw new ArgumentException("The selected font list contains duplicate program sources.", nameof(resolutions));
            ReadOnlyMemory<byte> bytes = await resolution.Source.GetBytesAsync(cancellationToken).ConfigureAwait(false);
            totalBytes = checked(totalBytes + bytes.Length);
            if (totalBytes > MaximumBorrowedBytes) throw new SvgException(SvgErrorCode.LimitExceeded, "The selected font programs exceed the snapshot byte limit.");
            OpenTypeFontMetrics metrics = OpenTypeFont.ParseMetrics(bytes, cancellationToken);
            prepared[index] = new SvgPreparedFont(resolution, bytes, metrics.UnitsPerEm, metrics.GlyphCount);
        }
        return new SvgFontSnapshot(prepared);
    }

    /// <summary>Releases references to caller-owned font byte regions retained by this snapshot.</summary>
    public void Dispose() => _fonts = [];
}

/// <summary>Represents one verified, caller-byte-backed font held by an immutable snapshot.</summary>
public sealed class SvgPreparedFont
{
    internal SvgPreparedFont(SvgFontProgramResolution resolution, ReadOnlyMemory<byte> bytes, ushort unitsPerEm, ushort glyphCount)
    {
        Resolution = resolution;
        Bytes = bytes;
        UnitsPerEm = unitsPerEm;
        GlyphCount = glyphCount;
    }

    /// <summary>Gets the catalog selection and verified source identity.</summary>
    public SvgFontProgramResolution Resolution { get; }
    /// <summary>Gets caller-owned, integrity-verified font bytes retained for the snapshot lifetime.</summary>
    public ReadOnlyMemory<byte> Bytes { get; }
    /// <summary>Gets the validated font-unit scale.</summary>
    public ushort UnitsPerEm { get; }
    /// <summary>Gets the validated glyph count.</summary>
    public ushort GlyphCount { get; }
}
