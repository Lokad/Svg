namespace Lokad.Svg.Fonts;

using System.Security.Cryptography;

/// <summary>
/// Represents one immutable, caller-byte-backed set of selected standalone
/// TrueType fonts. The caller keeps every returned byte region valid and unchanged
/// until this snapshot is disposed. Instances are safe for concurrent reads after
/// successful creation, but do not own a shared cache or perform I/O. Multiple
/// face records for one manifest program share one acquired byte region.
/// </summary>
public sealed class SvgFontSnapshot : IDisposable
{
    private const long MaximumBorrowedBytes = 32L * 1024 * 1024;
    private const int MaximumFaces = 256;
    private SvgPreparedFont[] _fonts;
    private IReadOnlyList<SvgPreparedFont> _fontView;

    private SvgFontSnapshot(SvgPreparedFont[] fonts)
    {
        _fonts = fonts;
        _fontView = Array.AsReadOnly(fonts);
    }

    /// <summary>Gets verified selected faces and their caller-owned program bytes.</summary>
    public IReadOnlyList<SvgPreparedFont> Fonts => _fontView;

    /// <summary>Gets the prepared face selected for one previously prepared family/style request.</summary>
    internal SvgPreparedFont GetFont(SvgFontFaceRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        foreach (SvgPreparedFont font in _fonts)
        {
            SvgFontFaceRequest selected = font.Resolution.Resolution.Request;
            if (selected.Weight == request.Weight && selected.Italic == request.Italic && selected.Family.Equals(request.Family, StringComparison.OrdinalIgnoreCase)) return font;
        }
        throw new SvgException(SvgErrorCode.UnresolvedResource, "The requested font face was not prepared in the supplied snapshot.", new SvgFontDiagnostic(SvgFontFailureStage.Source, request.Family, string.Empty, string.Empty, string.Empty));
    }

    /// <summary>
    /// Asynchronously verifies and parses the selected caller-owned programs into
    /// one bounded snapshot. The input order is retained; faces sharing a declared
    /// program SHA-256 acquire and account for that program only once.
    /// </summary>
    public static async ValueTask<SvgFontSnapshot> CreateAsync(IReadOnlyList<SvgFontProgramResolution> resolutions, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(resolutions);
        cancellationToken.ThrowIfCancellationRequested();
        if (resolutions.Count > MaximumFaces) throw new SvgException(SvgErrorCode.LimitExceeded, "The selected font face count exceeds the snapshot limit.");
        var programs = new Dictionary<string, (ReadOnlyMemory<byte> Bytes, OpenTypeFontMetrics Metrics)>(StringComparer.Ordinal);
        var prepared = new SvgPreparedFont[resolutions.Count];
        long totalBytes = 0;
        for (int index = 0; index < resolutions.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SvgFontProgramResolution resolution = resolutions[index] ?? throw new ArgumentException("The selected font list contains a null resolution.", nameof(resolutions));
            if (resolution.Resolution.Face.FaceIndex != 0) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Standalone TrueType font snapshots require face index zero.");
            string programKey = resolution.Resolution.Face.File.Sha256;
            if (!programs.TryGetValue(programKey, out (ReadOnlyMemory<byte> Bytes, OpenTypeFontMetrics Metrics) program))
            {
                ReadOnlyMemory<byte> loadedBytes = await resolution.Source.GetBytesAsync(cancellationToken).ConfigureAwait(false);
                totalBytes = checked(totalBytes + loadedBytes.Length);
                if (totalBytes > MaximumBorrowedBytes) throw new SvgException(SvgErrorCode.LimitExceeded, "The selected font programs exceed the snapshot byte limit.");
                program = (loadedBytes, OpenTypeFont.ParseMetrics(loadedBytes, cancellationToken));
                programs.Add(programKey, program);
            }
            ReadOnlyMemory<byte> bytes = program.Bytes;
            ValidateDeclaredProgram(resolution.Resolution.Face.File, bytes);
            OpenTypeFontMetrics metrics = program.Metrics;
            prepared[index] = new SvgPreparedFont(resolution, bytes, metrics.UnitsPerEm, metrics.GlyphCount);
        }
        return new SvgFontSnapshot(prepared);

        static void ValidateDeclaredProgram(SvgFontPackFile file, ReadOnlyMemory<byte> bytes)
        {
            if (bytes.Length != file.ByteSize) throw new SvgException(SvgErrorCode.UnresolvedResource, "The selected font program has an unexpected byte size.");
            string hash = Convert.ToHexString(SHA256.HashData(bytes.Span));
            if (!hash.Equals(file.Sha256, StringComparison.OrdinalIgnoreCase)) throw new SvgException(SvgErrorCode.UnresolvedResource, "The selected font program SHA-256 does not match its manifest.");
        }
    }

    /// <summary>
    /// Resolves explicit requests, selectively asks the caller-owned provider for
    /// the resulting distinct programs, and creates one verified snapshot. This
    /// does not parse SVG; SVG-driven request discovery remains a later phase.
    /// </summary>
    public static async ValueTask<SvgFontSnapshot> CreateAsync(SvgFontPackCatalog catalog, IReadOnlyList<SvgFontFaceRequest> requests, ISvgFontProgramSourceProvider provider, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(requests);
        ArgumentNullException.ThrowIfNull(provider);
        cancellationToken.ThrowIfCancellationRequested();
        if (requests.Count > MaximumFaces) throw new SvgException(SvgErrorCode.LimitExceeded, "The selected font face count exceeds the snapshot limit.");
        var sources = new Dictionary<string, ISvgFontProgramSource>(StringComparer.Ordinal);
        var resolutions = new SvgFontProgramResolution[requests.Count];
        for (int index = 0; index < requests.Count; index++)
        {
            SvgFontFaceRequest request = requests[index] ?? throw new ArgumentException("The font request list contains a null request.", nameof(requests));
            SvgFontPackResolution selection = catalog.Resolve(request);
            string programKey = selection.Face.File.Sha256;
            if (!sources.TryGetValue(programKey, out ISvgFontProgramSource? source))
            {
                source = await provider.GetSourceAsync(selection.Face.File, cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                if (source is null) throw new SvgException(SvgErrorCode.UnresolvedResource, "The selected font program is unavailable from the supplied provider.", new SvgFontDiagnostic(SvgFontFailureStage.Source, selection.Request.Family, selection.Face.ResolvedFamily, selection.Face.File.RelativePath + ":" + selection.Face.FaceIndex, string.Empty));
                sources.Add(programKey, source);
            }
            resolutions[index] = catalog.Bind(selection, source);
        }
        return await CreateAsync(resolutions, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Discovers the admitted explicit text font requests from SVG bytes, then
    /// selectively acquires their resolved programs. This preparation step does
    /// not retain text runs or render text.
    /// </summary>
    public static ValueTask<SvgFontSnapshot> CreateAsync(ReadOnlyMemory<byte> svg, SvgFontPackCatalog catalog, ISvgFontProgramSourceProvider provider, CancellationToken cancellationToken)
    {
        SvgFontFaceRequest[] requests = SvgFontRequestDiscovery.Discover(svg, cancellationToken);
        return CreateAsync(catalog, requests, provider, cancellationToken);
    }

    /// <summary>Releases references to caller-owned font byte regions retained by this snapshot.</summary>
    public void Dispose()
    {
        _fonts = [];
        _fontView = Array.AsReadOnly(_fonts);
    }
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
