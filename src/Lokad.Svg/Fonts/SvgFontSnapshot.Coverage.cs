namespace Lokad.Svg.Fonts;

using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

public sealed partial class SvgFontSnapshot
{
    private readonly record struct CoverageProgram(ISvgFontProgramSource Source, ReadOnlyMemory<byte> Bytes, OpenTypeFontMetrics Metrics);

    private static async ValueTask<SvgFontSnapshot> CreateWithCoverageAsync(SvgFontPackCatalog catalog, SvgFontCoverageRequest[] requests,
        ISvgFontProgramSourceProvider provider, SvgFontSelectionPolicy? policy, RenderBudget budget, CancellationToken token)
    {
        RenderBudget.BudgetReservation retained = budget.Reserve(0, SvgMemoryCategory.Resource);
        var programs = new Dictionary<string, CoverageProgram>(StringComparer.Ordinal);
        var prepared = new List<SvgPreparedFont>();
        SvgFontSnapshotLease lease = new();
        long totalBytes = 0;
        try
        {
            using RenderBudget.BudgetReservation scratch = budget.Reserve(512, SvgMemoryCategory.Temporary);
            foreach (SvgFontCoverageRequest item in requests)
            {
                token.ThrowIfCancellationRequested();
                using RenderBudget.BudgetReservation scalarScratch = budget.Reserve(ParserCollectionMemory.GetArrayByteCount<bool>(item.Scalars.Length), SvgMemoryCategory.Temporary);
                bool[] remaining = new bool[item.Scalars.Length];
                int needed = 0;
                for (int index = 0; index < remaining.Length; index++)
                {
                    token.ThrowIfCancellationRequested();
                    remaining[index] = SvgFontCoverageDiscovery.AllowsScalarFallback(item.Scalars[index]);
                    if (remaining[index]) needed++;
                }
                bool first = true;
                var visited = new HashSet<string>(StringComparer.Ordinal);
                foreach (SvgFontPackResolution selection in catalog.EnumerateCandidates(item.Request, policy, token))
                {
                    token.ThrowIfCancellationRequested();
                    string identity = selection.Face.File.Sha256;
                    ValidateSelections([selection], token);
                    bool known = programs.TryGetValue(identity, out CoverageProgram program);
                    if (known && program.Bytes.Length != selection.Face.File.ByteSize)
                        throw new SvgException(SvgErrorCode.UnresolvedResource, "Selected font declarations with the same SHA-256 have conflicting byte sizes.", CreateDiagnostic(selection, SvgFontFailureStage.Integrity));
                    if (!visited.Add(identity)) continue;
                    if (!known)
                    {
                        if (programs.Count == MaximumFaces || selection.Face.File.ByteSize > MaximumBorrowedBytes - totalBytes)
                            throw new SvgException(SvgErrorCode.LimitExceeded, "Examined font programs exceed the snapshot face or byte bound.");
                        scratch.Grow(2048);
                        ISvgFontProgramSource? source;
                        try { source = await provider.GetSourceAsync(selection.Face.File, token).ConfigureAwait(false); }
                        catch (SvgException exception) { throw SvgFontSourceFailures.Create(selection, exception); }
                        token.ThrowIfCancellationRequested();
                        if (source is null) throw SvgFontSourceFailures.Create(selection, SvgErrorCode.UnresolvedResource);
                        SvgFontProgramResolution bound = catalog.Bind(selection, source, token);
                        // Reuse the existing integrity/format loader. Its temporary lease
                        // owns no bytes; the final snapshot retains its own independent lease.
                        using SvgFontSnapshot loaded = await CreatePreparedAsync([bound], budget, token).ConfigureAwait(false);
                        SvgPreparedFont font = loaded.Fonts[0];
                        program = new(source, font.Bytes, font.Metrics);
                        programs.Add(identity, program);
                        totalBytes += program.Bytes.Length;
                    }
                    bool contributes = first;
                    for (int index = 0; index < remaining.Length; index++)
                    {
                        token.ThrowIfCancellationRequested();
                        if (!remaining[index] || OpenTypeFont.GetGlyphIndex(program.Bytes, item.Scalars[index], program.Metrics, token) == 0) continue;
                        remaining[index] = false; needed--; contributes = true;
                    }
                    if (contributes)
                    {
                        if (prepared.Count == MaximumFaces) throw new SvgException(SvgErrorCode.LimitExceeded, "Selected font faces exceed the snapshot bound.");
                        retained.Grow(GetSnapshotStorageBytes(prepared.Count + 1) - GetSnapshotStorageBytes(prepared.Count));
                        prepared.Add(new(catalog.Bind(selection, program.Source, token), program.Bytes, program.Metrics, lease));
                    }
                    first = false;
                    if (needed == 0) break;
                }
                if (first) _ = catalog.ResolveFamilies(item.Request, policy, token);
                // Missing scalars retain the primary face. Layout reports the
                // original element and normalized scalar index before any output.
            }
            return new(prepared.ToArray(), lease, retained);
        }
        catch { lease.Dispose(); retained.Dispose(); throw; }
    }

    /// <summary>Checks for distinct prepared programs of one request without consulting font bytes.</summary>
    internal bool HasCoverageAlternatives(SvgFontFaceRequest request, CancellationToken token)
    {
        ThrowIfDisposed();
        string? first = null;
        foreach (SvgPreparedFont font in _fonts)
        {
            token.ThrowIfCancellationRequested();
            if (!font.Resolution.Resolution.Request.Matches(request)) continue;
            string identity = font.Resolution.Resolution.Face.File.Sha256;
            if (first is null) first = identity;
            else if (identity != first) return true;
        }
        return false;
    }

    /// <summary>Selects the first prepared covering program while preserving primary ownership and unsupported shaping outcomes.</summary>
    internal SvgPreparedFont GetFontForScalar(SvgFontFaceRequest request, SvgPreparedFont primary, uint scalar, CancellationToken token)
    {
        ThrowIfDisposed();
        if (!SvgFontCoverageDiscovery.AllowsScalarFallback(scalar) || OpenTypeFont.GetGlyphIndex(primary.Bytes, scalar, primary.Metrics, token) != 0) return primary;
        for (int pass = 0; pass < 2; pass++)
        {
            foreach (SvgPreparedFont font in _fonts)
            {
                token.ThrowIfCancellationRequested();
                SvgFontFaceRequest selected = font.Resolution.Resolution.Request;
                if (ReferenceEquals(font, primary) || !selected.Matches(request) || (selected.IsCssRequest == request.IsCssRequest) != (pass == 0)) continue;
                if (OpenTypeFont.GetGlyphIndex(font.Bytes, scalar, font.Metrics, token) != 0) return font;
            }
        }
        return primary;
    }
}
