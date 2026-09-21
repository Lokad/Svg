namespace Lokad.Svg.Fonts;

using System.Text.Json;

/// <summary>
/// Reads and resolves the public Lokad font-pack manifest format without acquiring
/// font bytes. Instances are immutable and safe for concurrent resolution.
/// </summary>
public sealed class SvgFontPackCatalog
{
    private const int MaximumManifestBytes = 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly SvgFontPackFace[] _faces;
    private readonly string[] _fallbackFamilies;

    private SvgFontPackCatalog(string packId, SvgFontPackFile[] files, SvgFontPackFace[] faces, string[] fallbackFamilies)
    {
        PackId = packId;
        Files = Array.AsReadOnly(files);
        Faces = Array.AsReadOnly(faces);
        _faces = faces;
        _fallbackFamilies = fallbackFamilies;
        FallbackFamilies = Array.AsReadOnly(fallbackFamilies);
    }

    /// <summary>Gets the pack identifier validated against the caller's requested identifier.</summary>
    public string PackId { get; }

    /// <summary>Gets the distinct font files declared by this pack.</summary>
    public IReadOnlyList<SvgFontPackFile> Files { get; }

    /// <summary>Gets declared family/style faces in manifest order.</summary>
    public IReadOnlyList<SvgFontPackFace> Faces { get; }

    /// <summary>Gets the configured fallback families after case-insensitive de-duplication.</summary>
    public IReadOnlyList<string> FallbackFamilies { get; }

    /// <summary>
    /// Parses a manifest compatible with the public OoxPdf 0.1.4 pack layout.
    /// The caller retains ownership of <paramref name="manifestBytes"/>.
    /// </summary>
    public static SvgFontPackCatalog Parse(ReadOnlyMemory<byte> manifestBytes, string requestedPackId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestedPackId);
        ValidatePackId(requestedPackId);
        if (manifestBytes.Length > MaximumManifestBytes)
        {
            throw new SvgException(SvgErrorCode.LimitExceeded, "The font pack manifest exceeds the supported size limit.");
        }

        WireManifest manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<WireManifest>(manifestBytes.Span, JsonOptions)
                ?? throw InvalidManifest("The font pack manifest is empty.");
        }
        catch (JsonException exception)
        {
            throw new SvgException(SvgErrorCode.MalformedInput, "The font pack manifest is not valid JSON: " + exception.Message);
        }

        if (!requestedPackId.Equals(manifest.PackId, StringComparison.Ordinal))
        {
            throw InvalidManifest("The font pack manifest packId does not match the requested pack identifier.");
        }

        SvgFontPackFile[] files = ParseFiles(manifest.Files);
        SvgFontPackFace[] faces = ParseFaces(manifest.Families, files);
        string[] fallbacks = ParseFallbacks(manifest.Fallbacks);
        return new SvgFontPackCatalog(requestedPackId, files, faces, fallbacks);
    }

    /// <summary>
    /// Resolves a requested family and style using the shared-pack selection order.
    /// The returned file remains metadata only; callers acquire and verify bytes separately.
    /// </summary>
    public SvgFontPackResolution Resolve(SvgFontFaceRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        SvgFontPackFace[] exact = _faces.Where(face => MatchesFamily(face, request.Family)).ToArray();
        if (exact.Length != 0) return new SvgFontPackResolution(request, SelectBest(exact, request), false);

        foreach (string fallback in _fallbackFamilies)
        {
            SvgFontPackFace[] candidates = _faces.Where(face => MatchesFamily(face, fallback)).ToArray();
            if (candidates.Length != 0) return new SvgFontPackResolution(request, SelectBest(candidates, request), true);
        }

        SvgFontPackFace[] textFaces = _faces.Where(static face => !face.HasMathTable).ToArray();
        return new SvgFontPackResolution(request, SelectBest(textFaces.Length == 0 ? _faces : textFaces, request), true);
    }

    /// <summary>
    /// Binds a catalog resolution to caller-owned program storage without fetching
    /// data. The returned source validates byte count and SHA-256 on every access.
    /// </summary>
    public SvgFontProgramResolution Bind(SvgFontPackResolution resolution, ISvgFontProgramSource source)
    {
        ArgumentNullException.ThrowIfNull(resolution);
        ArgumentNullException.ThrowIfNull(source);
        if (!_faces.Contains(resolution.Face)) throw new ArgumentException("The font-pack resolution does not belong to this catalog.", nameof(resolution));
        return new SvgFontProgramResolution(resolution, new VerifiedSvgFontProgramSource(PackId, resolution.Face, source));
    }

    /// <summary>
    /// Selectively locates and integrity-verifies the program selected by this
    /// catalog. No other manifest file is requested. The provider retains source
    /// ownership and cache policy; the result retains neither bytes nor a cache.
    /// </summary>
    public ValueTask<SvgFontProgramResolution> AcquireAsync(SvgFontPackResolution resolution, ISvgFontProgramSourceProvider provider, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(resolution);
        ArgumentNullException.ThrowIfNull(provider);
        if (!_faces.Contains(resolution.Face)) throw new ArgumentException("The font-pack resolution does not belong to this catalog.", nameof(resolution));
        cancellationToken.ThrowIfCancellationRequested();
        ValueTask<ISvgFontProgramSource?> pending;
        try { pending = provider.GetSourceAsync(resolution.Face.File, cancellationToken); }
        catch (SvgException exception)
        {
            throw SvgFontSourceFailures.Create(resolution, exception.ErrorCode);
        }
        return pending.IsCompletedSuccessfully
            ? AcquireResolved(resolution, pending.Result, cancellationToken)
            : AwaitAcquireAsync(resolution, pending, cancellationToken);
    }

    private ValueTask<SvgFontProgramResolution> AcquireResolved(SvgFontPackResolution resolution, ISvgFontProgramSource? source, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (source is null) throw SvgFontSourceFailures.Create(resolution, SvgErrorCode.UnresolvedResource);
        SvgFontProgramResolution bound = Bind(resolution, source);
        ValueTask<ReadOnlyMemory<byte>> verification;
        try { verification = ((VerifiedSvgFontProgramSource)bound.Source).GetBytesAsync(null, cancellationToken); }
        catch (SvgFontProgramSourceFailure exception)
        {
            throw SvgFontSourceFailures.Create(resolution, exception.SourceException.ErrorCode);
        }
        catch (SvgException exception)
        {
            throw CreateIntegrityFailure(resolution, exception);
        }
        return verification.IsCompletedSuccessfully
            ? ValueTask.FromResult(bound)
            : AwaitVerificationAsync(resolution, bound, verification);
    }

    private async ValueTask<SvgFontProgramResolution> AwaitAcquireAsync(SvgFontPackResolution resolution, ValueTask<ISvgFontProgramSource?> pending, CancellationToken cancellationToken)
    {
        ISvgFontProgramSource? source;
        try { source = await pending.ConfigureAwait(false); }
        catch (SvgException exception)
        {
            throw SvgFontSourceFailures.Create(resolution, exception.ErrorCode);
        }
        return await AcquireResolved(resolution, source, cancellationToken).ConfigureAwait(false);
    }

    private static async ValueTask<SvgFontProgramResolution> AwaitVerificationAsync(SvgFontPackResolution resolution, SvgFontProgramResolution bound, ValueTask<ReadOnlyMemory<byte>> verification)
    {
        try { await verification.ConfigureAwait(false); }
        catch (SvgFontProgramSourceFailure exception)
        {
            throw SvgFontSourceFailures.Create(resolution, exception.SourceException.ErrorCode);
        }
        catch (SvgException exception)
        {
            throw CreateIntegrityFailure(resolution, exception);
        }
        return bound;
    }

    private static SvgException CreateIntegrityFailure(SvgFontPackResolution resolution, SvgException exception)
        => new(exception.ErrorCode, exception.Message, new SvgFontDiagnostic(SvgFontFailureStage.Integrity, resolution.Request.Family, resolution.Face.ResolvedFamily, resolution.Face.File.RelativePath + ":" + resolution.Face.FaceIndex, string.Empty));

    private static SvgFontPackFile[] ParseFiles(WireFile[]? entries)
    {
        if (entries is null || entries.Length == 0) throw InvalidManifest("The font pack manifest does not list any font files.");
        var files = new Dictionary<string, SvgFontPackFile>(StringComparer.Ordinal);
        foreach (WireFile? entry in entries)
        {
            if (entry is null) throw InvalidManifest("The font pack manifest contains an empty font file entry.");
            string path = ValidateRelativePath(entry.RelativePath, "font file");
            if (entry.ByteSize is null or < 0) throw InvalidManifest("Font file '" + path + "' has an invalid byte size.");
            string hash = ValidateSha256(entry.Sha256, path);
            if (!files.TryAdd(path, new SvgFontPackFile(path, entry.ByteSize.Value, hash))) throw InvalidManifest("The font pack manifest contains duplicate font file paths.");
        }
        return files.Values.ToArray();
    }

    private static SvgFontPackFace[] ParseFaces(WireFamily[]? entries, SvgFontPackFile[] files)
    {
        if (entries is null || entries.Length == 0) throw InvalidManifest("The font pack manifest does not list any font families.");
        var filesByPath = files.ToDictionary(file => file.RelativePath, StringComparer.Ordinal);
        var faces = new SvgFontPackFace[entries.Length];
        for (int index = 0; index < entries.Length; index++)
        {
            WireFamily? entry = entries[index];
            if (entry is null) throw InvalidManifest("The font pack manifest contains an empty font family entry.");
            string requestedFamily = RequiredText(entry.RequestedFamily, "requested font family");
            string resolvedFamily = RequiredText(entry.ResolvedFamily, "resolved font family");
            string path = ValidateRelativePath(entry.RelativeFontFile, "font family file");
            if (!filesByPath.TryGetValue(path, out SvgFontPackFile? file)) throw InvalidManifest("A font family references a file absent from the manifest.");
            int weight = entry.Weight ?? 400;
            if (weight <= 0) throw InvalidManifest("A font family has an invalid weight.");
            int faceIndex = entry.FaceIndex ?? 0;
            if (faceIndex < 0) throw InvalidManifest("A font family has a negative face index.");
            faces[index] = new SvgFontPackFace(requestedFamily, resolvedFamily, file, weight, entry.Italic ?? false, faceIndex, entry.HasMathTable ?? false);
        }
        return faces;
    }

    private static string[] ParseFallbacks(WireFallback[]? entries)
    {
        if (entries is null) return [];
        var fallbacks = new List<string>(entries.Length);
        foreach (WireFallback? entry in entries)
        {
            if (entry is null) throw InvalidManifest("The font pack manifest contains an empty fallback entry.");
            string family = RequiredText(entry.Family, "fallback font family");
            if (!fallbacks.Contains(family, StringComparer.OrdinalIgnoreCase)) fallbacks.Add(family);
        }
        return fallbacks.ToArray();
    }

    private static SvgFontPackFace SelectBest(IReadOnlyList<SvgFontPackFace> candidates, SvgFontFaceRequest request)
    {
        return candidates
            .OrderBy(candidate => candidate.Italic == request.Italic ? 0 : 1000)
            .ThenBy(candidate => Math.Abs(candidate.Weight - request.Weight))
            .ThenBy(candidate => candidate.ResolvedFamily, StringComparer.OrdinalIgnoreCase)
            .ThenBy(candidate => candidate.File.RelativePath, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.FaceIndex)
            .First();
    }

    private static bool MatchesFamily(SvgFontPackFace face, string family)
        => face.RequestedFamily.Equals(family, StringComparison.OrdinalIgnoreCase) || face.ResolvedFamily.Equals(family, StringComparison.OrdinalIgnoreCase);

    private static string RequiredText(string? value, string fieldName)
        => string.IsNullOrWhiteSpace(value) ? throw InvalidManifest("The font pack manifest has an empty " + fieldName + ".") : value;

    private static string ValidateSha256(string? value, string path)
    {
        if (value is null || value.Length != 64 || value.Any(static character => !Uri.IsHexDigit(character))) throw InvalidManifest("Font file '" + path + "' has an invalid SHA-256 hash.");
        return value.ToUpperInvariant();
    }

    private static void ValidatePackId(string packId)
    {
        if (IsUnsafePathSegment(packId)) throw new ArgumentException("The font pack identifier must be one safe relative path segment.", nameof(packId));
    }

    private static string ValidateRelativePath(string? value, string fieldName)
    {
        string path = RequiredText(value, fieldName);
        if (path[0] == '/' || path.Contains('\\') || path.Contains(':') || path.Contains('?') || path.Contains('#') || path.Contains("//", StringComparison.Ordinal) || path.Split('/').Any(IsUnsafePathSegment)) throw InvalidManifest("The font pack manifest has an unsafe " + fieldName + " path.");
        return path;
    }

    private static bool IsUnsafePathSegment(string segment)
    {
        if (segment.Length == 0 || segment is "." or ".." || segment.Contains('/') || segment.Contains('\\') || segment.Contains(':') || segment.Contains('?') || segment.Contains('#')) return true;
        try
        {
            string unescaped = Uri.UnescapeDataString(segment);
            return unescaped is "." or ".." || unescaped.Contains('/') || unescaped.Contains('\\');
        }
        catch (UriFormatException) { return true; }
    }

    private static SvgException InvalidManifest(string message) => new(SvgErrorCode.MalformedInput, message);

    private sealed class WireManifest
    {
        public string? PackId { get; set; }
        public WireFile[]? Files { get; set; }
        public WireFamily[]? Families { get; set; }
        public WireFallback[]? Fallbacks { get; set; }
    }

    private sealed class WireFile
    {
        public string? RelativePath { get; set; }
        public long? ByteSize { get; set; }
        public string? Sha256 { get; set; }
    }

    private sealed class WireFamily
    {
        public string? RequestedFamily { get; set; }
        public string? ResolvedFamily { get; set; }
        public string? RelativeFontFile { get; set; }
        public int? Weight { get; set; }
        public bool? Italic { get; set; }
        public int? FaceIndex { get; set; }
        public bool? HasMathTable { get; set; }
    }

    private sealed class WireFallback { public string? Family { get; set; } }
}

/// <summary>Describes one family/style selection request to a font pack.</summary>
public sealed class SvgFontFaceRequest
{
    /// <summary>Initializes a requested family, positive CSS-like weight and italic flag.</summary>
    public SvgFontFaceRequest(string family, int weight, bool italic)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(family);
        if (weight <= 0) throw new ArgumentOutOfRangeException(nameof(weight));
        Family = family;
        Weight = weight;
        Italic = italic;
    }

    /// <summary>Gets the requested family.</summary>
    public string Family { get; }
    /// <summary>Gets the requested positive weight.</summary>
    public int Weight { get; }
    /// <summary>Gets whether an italic face was requested.</summary>
    public bool Italic { get; }
}

/// <summary>Describes one distinct font program declared in a pack.</summary>
public sealed class SvgFontPackFile
{
    internal SvgFontPackFile(string relativePath, long byteSize, string sha256)
    {
        RelativePath = relativePath;
        ByteSize = byteSize;
        Sha256 = sha256;
    }

    /// <summary>Gets the safe pack-relative font path.</summary>
    public string RelativePath { get; }
    /// <summary>Gets the declared program byte count.</summary>
    public long ByteSize { get; }
    /// <summary>Gets the normalized uppercase SHA-256 hash.</summary>
    public string Sha256 { get; }
}

/// <summary>Describes one selectable collection face declared in a pack.</summary>
public sealed class SvgFontPackFace
{
    internal SvgFontPackFace(string requestedFamily, string resolvedFamily, SvgFontPackFile file, int weight, bool italic, int faceIndex, bool hasMathTable)
    {
        RequestedFamily = requestedFamily;
        ResolvedFamily = resolvedFamily;
        File = file;
        Weight = weight;
        Italic = italic;
        FaceIndex = faceIndex;
        HasMathTable = hasMathTable;
    }

    /// <summary>Gets the requested family or alias declared by the pack.</summary>
    public string RequestedFamily { get; }
    /// <summary>Gets the resolved family declared by the pack.</summary>
    public string ResolvedFamily { get; }
    /// <summary>Gets the declared font program.</summary>
    public SvgFontPackFile File { get; }
    /// <summary>Gets the face weight.</summary>
    public int Weight { get; }
    /// <summary>Gets whether the face is italic.</summary>
    public bool Italic { get; }
    /// <summary>Gets the collection face index.</summary>
    public int FaceIndex { get; }
    /// <summary>Gets whether the face is marked as containing OpenType math data.</summary>
    public bool HasMathTable { get; }
}

/// <summary>Returns the selected pack face and whether selection used fallback behavior.</summary>
public sealed class SvgFontPackResolution
{
    internal SvgFontPackResolution(SvgFontFaceRequest request, SvgFontPackFace face, bool isFallback)
    {
        Request = request;
        Face = face;
        IsFallback = isFallback;
    }

    /// <summary>Gets the original request.</summary>
    public SvgFontFaceRequest Request { get; }
    /// <summary>Gets the selected face.</summary>
    public SvgFontPackFace Face { get; }
    /// <summary>Gets whether the selected face differs from a direct family match.</summary>
    public bool IsFallback { get; }
}
