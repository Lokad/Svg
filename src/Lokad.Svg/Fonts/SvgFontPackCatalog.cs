namespace Lokad.Svg.Fonts;

using Lokad.Svg.Parsing;
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
    /// Resolves authored families in order, then uses the existing shared-pack
    /// fallback and last-resort selection. Use ResolveFamilies to restrict fallback.
    /// The returned file remains metadata only; callers acquire and verify bytes separately.
    /// </summary>
    public SvgFontPackResolution Resolve(SvgFontFaceRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ResolveCore(request, null, CancellationToken.None);
    }

    /// <summary>Resolves one request while charging every bounded candidate scan to the calling preparation operation.</summary>
    internal SvgFontPackResolution Resolve(SvgFontFaceRequest request, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(workCounter);
        return ResolveCore(request, workCounter, cancellationToken);
    }

    /// <summary>Resolves one request while observing cancellation at every bounded candidate scan.</summary>
    internal SvgFontPackResolution Resolve(SvgFontFaceRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ResolveCore(request, null, cancellationToken);
    }

    private SvgFontPackResolution ResolveCore(SvgFontFaceRequest request, ParserWorkCounter? workCounter, CancellationToken cancellationToken)
        => ResolveFamilies(request, null, workCounter, cancellationToken);

    /// <summary>Resolves authored families in order, then only host-authorized fallbacks. No bytes are acquired.</summary>
    public SvgFontPackResolution ResolveFamilies(SvgFontFaceRequest request, SvgFontSelectionPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(policy);
        return ResolveFamilies(request, policy, null, CancellationToken.None);
    }

    internal SvgFontPackResolution ResolveFamilies(SvgFontFaceRequest request, SvgFontSelectionPolicy? policy, ParserWorkCounter? workCounter, CancellationToken cancellationToken)
    {
        foreach (SvgFontPackResolution candidate in EnumerateCandidates(request, policy, workCounter, cancellationToken)) return candidate;
        throw new SvgException(SvgErrorCode.UnresolvedResource, "No supplied font matches the requested family list and host fallback policy.",
            new SvgFontDiagnostic(SvgFontFailureStage.Selection, request.Family, string.Empty, string.Empty, string.Empty,
                request.Weight, request.Italic, null, null, null), request.SourceContext?.Describe(string.Empty, string.Empty));
    }

    /// <summary>Streams metadata candidates in authored/host order. A legacy last resort is offered only when no named candidate exists.</summary>
    internal IEnumerable<SvgFontPackResolution> EnumerateCandidates(SvgFontFaceRequest request, SvgFontSelectionPolicy? policy, ParserWorkCounter? workCounter, CancellationToken cancellationToken)
    {
        bool found = false;
        foreach (SvgFontFamily family in request.Families)
        {
            Consume(workCounter, cancellationToken);
            string name = family.Name;
            if (family.IsGeneric && policy is not null)
            {
                if (!policy.GenericFamilies.TryGetValue(name, out string? mapped)) continue;
                name = mapped;
            }
            SvgFontPackFace? exact = SelectFamily(name, request, workCounter, cancellationToken);
            if (exact is not null) { found = true; yield return new(request, exact, false); }
        }
        foreach (string fallback in policy?.FallbackFamilies ?? _fallbackFamilies)
        {
            Consume(workCounter, cancellationToken);
            SvgFontPackFace? candidate = SelectFamily(fallback, request, workCounter, cancellationToken);
            if (candidate is not null) { found = true; yield return new(request, candidate, true); }
        }

        if (found || policy is not null) yield break;
        SvgFontPackFace? textFace = SelectBest(request, true, workCounter, cancellationToken);
        yield return new(request, textFace ?? SelectBest(request, false, workCounter, cancellationToken) ?? throw new InvalidOperationException("A validated font catalog has no selectable faces."), true);
    }

    /// <summary>
    /// Binds a catalog resolution to caller-owned program storage without fetching
    /// data. The returned source validates byte count and SHA-256 on every access.
    /// </summary>
    public SvgFontProgramResolution Bind(SvgFontPackResolution resolution, ISvgFontProgramSource source)
    {
        ArgumentNullException.ThrowIfNull(resolution);
        ArgumentNullException.ThrowIfNull(source);
        return Bind(resolution, source, null, CancellationToken.None);
    }

    /// <summary>Binds a catalog resolution while metering the provenance scan against the calling preparation operation.</summary>
    internal SvgFontProgramResolution Bind(SvgFontPackResolution resolution, ISvgFontProgramSource source, ParserWorkCounter? workCounter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(resolution);
        ArgumentNullException.ThrowIfNull(source);
        if (!ContainsFace(resolution.Face, workCounter, cancellationToken)) throw new ArgumentException("The font-pack resolution does not belong to this catalog.", nameof(resolution));
        return new SvgFontProgramResolution(resolution, new VerifiedSvgFontProgramSource(PackId, resolution.Face, source));
    }

    /// <summary>
    /// Selectively locates and integrity-verifies the program selected by this
    /// catalog. No other manifest file is requested. The provider retains source
    /// ownership and cache policy; the result retains neither bytes nor a cache.
    /// Verification applies at acquisition: snapshot creation verifies size and hash again
    /// before use, so callers must not assume caller-owned bytes stay unchanged in between.
    /// </summary>
    public ValueTask<SvgFontProgramResolution> AcquireAsync(SvgFontPackResolution resolution, ISvgFontProgramSourceProvider provider, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(resolution);
        ArgumentNullException.ThrowIfNull(provider);
        if (!ContainsFace(resolution.Face, null, cancellationToken)) throw new ArgumentException("The font-pack resolution does not belong to this catalog.", nameof(resolution));
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
        SvgFontProgramResolution bound = Bind(resolution, source, null, cancellationToken);
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
        => new(exception.ErrorCode, exception.Message, new SvgFontDiagnostic(SvgFontFailureStage.Integrity, resolution.Request.Family, resolution.Face.ResolvedFamily, resolution.Face.File.RelativePath + ":" + resolution.Face.FaceIndex, string.Empty,
            resolution.Request.Weight, resolution.Request.Italic, resolution.IsFallback, null, null), resolution.Request.SourceContext?.Describe(string.Empty, string.Empty));

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
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (WireFallback? entry in entries)
        {
            if (entry is null) throw InvalidManifest("The font pack manifest contains an empty fallback entry.");
            string family = RequiredText(entry.Family, "fallback font family");
            if (seen.Add(family)) fallbacks.Add(family);
        }
        return fallbacks.ToArray();
    }

    /// <summary>Proves face provenance with a bounded reference-identity scan, metering each candidate like the selection scans.</summary>
    private bool ContainsFace(SvgFontPackFace face, ParserWorkCounter? workCounter, CancellationToken cancellationToken)
    {
        foreach (SvgFontPackFace candidate in _faces)
        {
            Consume(workCounter, cancellationToken);
            if (ReferenceEquals(candidate, face)) return true;
        }

        return false;
    }

    private SvgFontPackFace? SelectFamily(string family, SvgFontFaceRequest request, ParserWorkCounter? workCounter, CancellationToken cancellationToken)
    {
        SvgFontPackFace? selected = null;
        foreach (SvgFontPackFace candidate in _faces)
        {
            Consume(workCounter, cancellationToken);
            if (!MatchesFamily(candidate, family)) continue;
            if (selected is null || IsBetter(candidate, selected, request)) selected = candidate;
        }
        return selected;
    }

    private SvgFontPackFace? SelectBest(SvgFontFaceRequest request, bool excludesMath, ParserWorkCounter? workCounter, CancellationToken cancellationToken)
    {
        SvgFontPackFace? selected = null;
        foreach (SvgFontPackFace candidate in _faces)
        {
            Consume(workCounter, cancellationToken);
            if (excludesMath && candidate.HasMathTable) continue;
            if (selected is null || IsBetter(candidate, selected, request)) selected = candidate;
        }
        return selected;
    }

    private static bool IsBetter(SvgFontPackFace candidate, SvgFontPackFace selected, SvgFontFaceRequest request)
    {
        int candidateStyle = candidate.Italic == request.Italic ? 0 : 1000;
        int selectedStyle = selected.Italic == request.Italic ? 0 : 1000;
        int comparison = candidateStyle.CompareTo(selectedStyle);
        if (comparison != 0) return comparison < 0;
        comparison = Math.Abs(candidate.Weight - request.Weight).CompareTo(Math.Abs(selected.Weight - request.Weight));
        if (comparison != 0) return comparison < 0;
        comparison = StringComparer.OrdinalIgnoreCase.Compare(candidate.ResolvedFamily, selected.ResolvedFamily);
        if (comparison != 0) return comparison < 0;
        comparison = StringComparer.Ordinal.Compare(candidate.File.RelativePath, selected.File.RelativePath);
        if (comparison != 0) return comparison < 0;
        return candidate.FaceIndex < selected.FaceIndex;
    }

    private static void Consume(ParserWorkCounter? workCounter, CancellationToken cancellationToken)
    {
        if (workCounter is null) cancellationToken.ThrowIfCancellationRequested();
        else workCounter.Consume(cancellationToken);
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
    private readonly bool _cssRequest;
    internal SvgSourceContext? SourceContext { get; init; }
    /// <summary>Initializes a requested family, positive CSS-like weight and italic flag.</summary>
    public SvgFontFaceRequest(string family, int weight, bool italic)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(family);
        if (weight <= 0) throw new ArgumentOutOfRangeException(nameof(weight));
        Family = family;
        Families = Array.AsReadOnly<SvgFontFamily>([new(family, false)]);
        Weight = weight;
        Italic = italic;
    }

    /// <summary>Gets the original literal name or canonical CSS list. Families exposes its decoded ordered candidates.</summary>
    public string Family { get; }
    /// <summary>Gets ordered decoded family candidates; the original constructor denotes one literal name.</summary>
    public IReadOnlyList<SvgFontFamily> Families { get; }

    private SvgFontFaceRequest(SvgFontFamilyList families, int weight, bool italic)
    {
        if (weight <= 0) throw new ArgumentOutOfRangeException(nameof(weight));
        Family = families.Text; Families = Array.AsReadOnly(families.Families); Weight = weight; Italic = italic; _cssRequest = true;
    }

    /// <summary>Parses an ordered CSS family list, preserving generic versus quoted literal requests.</summary>
    public static SvgFontFaceRequest FromCss(string familyList, int weight, bool italic)
        => FromCss(familyList, weight, italic, null, null, CancellationToken.None);

    internal static SvgFontFaceRequest FromCss(string familyList, int weight, bool italic, SvgSourceContext? source, ParserWorkCounter? work, CancellationToken token)
        => new(SvgFontFamilySyntax.Parse(familyList, work, token), weight, italic) { SourceContext = source };

    internal bool Matches(SvgFontFaceRequest other)
    {
        if (Weight != other.Weight || Italic != other.Italic || Families.Count != other.Families.Count) return false;
        for (int index = 0; index < Families.Count; index++)
        {
            SvgFontFamily left = Families[index], right = other.Families[index];
            if (!left.Name.Equals(right.Name, StringComparison.OrdinalIgnoreCase) || _cssRequest && other._cssRequest && left.IsGeneric != right.IsGeneric) return false;
        }
        return true;
    }
    internal bool IsCssRequest => _cssRequest;
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
        Sha256Bytes = Convert.FromHexString(sha256);
    }

    /// <summary>Gets the safe pack-relative font path.</summary>
    public string RelativePath { get; }
    /// <summary>Gets the declared program byte count.</summary>
    public long ByteSize { get; }
    /// <summary>Gets the declared digest bytes parsed once from validated manifest hex.</summary>
    internal byte[] Sha256Bytes { get; }

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
