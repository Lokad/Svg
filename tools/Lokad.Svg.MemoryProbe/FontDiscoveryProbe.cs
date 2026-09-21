using System.Text;
using Lokad.Svg.Fonts;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.MemoryProbe;

/// <summary>Runs font-request discovery with an explicit preparation budget, then snapshot preparation against a counting synthetic pack.</summary>
internal static class FontDiscoveryProbe
{
    private const int StubProgramBytes = 1024;

    /// <summary>Discovers requests on an explicit ledger, then prepares a snapshot that serves every request from generated pack bytes.</summary>
    internal static async Task<FontDiscoveryMeasurement> MeasureAsync(ReadOnlyMemory<byte> svg, SvgRenderLimits limits, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(limits);
        ParserWorkCounter workCounter = new(limits.MaximumWorkUnits);
        RenderBudget budget = new(limits.MaximumAccountedBytes);
        SvgFontFaceRequest[]? requests = null;
        SvgErrorCode? discoveryError = null;
        try
        {
            requests = SvgFontRequestDiscovery.Discover(svg, limits, workCounter, budget, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            RenderBudgetSnapshot cancelled = budget.Snapshot();
            return new FontDiscoveryMeasurement(null, null, cancelled.CurrentBytes, cancelled.PeakBytes, cancelled.DeniedReservationCount, 0, null, true);
        }
        catch (SvgException exception)
        {
            discoveryError = exception.ErrorCode;
        }

        RenderBudgetSnapshot ledger = budget.Snapshot();
        if (requests is null)
        {
            return new FontDiscoveryMeasurement(null, discoveryError, ledger.CurrentBytes, ledger.PeakBytes, ledger.DeniedReservationCount, 0, null, false);
        }

        (SvgFontPackCatalog catalog, CountingFontProgramSourceProvider provider) = BuildSyntheticPack(requests);
        SvgErrorCode? snapshotError = null;
        try
        {
            using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync(svg, catalog, provider, limits, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            RenderBudgetSnapshot cancelled = budget.Snapshot();
            return new FontDiscoveryMeasurement(requests.Length, null, cancelled.CurrentBytes, cancelled.PeakBytes, cancelled.DeniedReservationCount, provider.Calls, null, true);
        }
        catch (SvgException exception)
        {
            snapshotError = exception.ErrorCode;
        }

        return new FontDiscoveryMeasurement(requests.Length, null, ledger.CurrentBytes, ledger.PeakBytes, ledger.DeniedReservationCount, provider.Calls, snapshotError, false);

        (SvgFontPackCatalog Catalog, CountingFontProgramSourceProvider Provider) BuildSyntheticPack(SvgFontFaceRequest[] discovered)
        {
            int entryCount = Math.Max(discovered.Length, 1);
            Dictionary<string, ReadOnlyMemory<byte>> programs = new(entryCount, StringComparer.Ordinal);
            StringBuilder files = new();
            StringBuilder families = new();
            for (int index = 0; index < entryCount; index++)
            {
                string family = index < discovered.Length ? EscapeJson(discovered[index].Family) : "Unused";
                string relativePath = "fonts/request-" + index + ".ttf";
                byte[] program = new byte[StubProgramBytes];
                for (int octet = 0; octet < program.Length; octet++)
                {
                    program[octet] = (byte)((octet * 31 + index * 131) & 0xFF);
                }

                programs.Add(relativePath, program);
                if (index != 0)
                {
                    files.Append(',');
                    families.Append(',');
                }

                files.Append("{\"relativePath\":\"").Append(relativePath).Append("\",\"byteSize\":").Append(program.Length).Append(",\"sha256\":\"").Append(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(program))).Append("\"}");
                families.Append("{\"requestedFamily\":\"").Append(family).Append("\",\"resolvedFamily\":\"").Append(family).Append("\",\"relativeFontFile\":\"").Append(relativePath).Append("\"}");
            }

            string manifest = "{\"packId\":\"font-discovery-probe\",\"files\":[" + files + "],\"families\":[" + families + "]}";
            SvgFontPackCatalog catalog = SvgFontPackCatalog.Parse(System.Text.Encoding.UTF8.GetBytes(manifest), "font-discovery-probe");
            return (catalog, new CountingFontProgramSourceProvider(programs));
        }

        string EscapeJson(string value)
        {
            return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }
    }

    /// <summary>Counts pack-file acquisitions without owning, caching, or transforming program bytes.</summary>
    private sealed class CountingFontProgramSourceProvider : ISvgFontProgramSourceProvider
    {
        private readonly Dictionary<string, ReadOnlyMemory<byte>> _programs;

        internal CountingFontProgramSourceProvider(Dictionary<string, ReadOnlyMemory<byte>> programs)
        {
            ArgumentNullException.ThrowIfNull(programs);
            _programs = programs;
        }

        internal int Calls { get; private set; }

        /// <summary>Returns the generated bytes for a known pack file while recording the call; unknown files resolve to no source.</summary>
        public ValueTask<ISvgFontProgramSource?> GetSourceAsync(SvgFontPackFile file, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(file);
            Calls++;
            if (!_programs.TryGetValue(file.RelativePath, out ReadOnlyMemory<byte> program))
            {
                return ValueTask.FromResult<ISvgFontProgramSource?>(null);
            }

            return ValueTask.FromResult<ISvgFontProgramSource?>(new SvgMemoryFontProgramSource(file.RelativePath, program));
        }
    }
}

/// <summary>Reports one fresh-process font-discovery ledger with its synthetic snapshot preparation.</summary>
internal readonly record struct FontDiscoveryMeasurement(
    int? DiscoveryRequestCount,
    SvgErrorCode? DiscoveryErrorCode,
    long DiscoveryCurrentBytes,
    long DiscoveryPeakBytes,
    long DiscoveryDeniedReservations,
    int ProviderCalls,
    SvgErrorCode? SnapshotErrorCode,
    bool Cancelled);
