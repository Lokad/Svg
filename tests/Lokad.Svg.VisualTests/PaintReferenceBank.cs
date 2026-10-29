using System.Collections.Frozen;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using TextEncoding = System.Text.Encoding;

namespace Lokad.Svg.VisualTests;

/// <summary>
/// Discovers only case identities and their routine/research partition. Full immutable
/// metadata is loaded once for a bank when one of its rendering cases executes.
/// No generated index duplicates the authoritative capture manifests.
/// </summary>
internal sealed class PaintReferenceBank
{
    private readonly Lazy<Selection> _selection;
    private readonly Lazy<FrozenDictionary<string, JsonElement>> _rows;

    public PaintReferenceBank(string name)
    {
        Name = name;
        _selection = new Lazy<Selection>(ReadSelection);
        _rows = new Lazy<FrozenDictionary<string, JsonElement>>(ReadRows);
    }

    public string Name { get; }
    public IReadOnlyList<string> CaseIds => _selection.Value.Ids;
    public bool IsResearch(string id) => _selection.Value.ResearchIds.Contains(id);
    public JsonElement GetCase(string id) => _rows.Value[id];

    private string Folder => Path.Combine(AppContext.BaseDirectory, "Research", Name);

    private Selection ReadSelection()
    {
        byte[] bytes = File.ReadAllBytes(Path.Combine(Folder, "capture.json"));
        CaptureIndex data = JsonSerializer.Deserialize<CaptureIndex>(bytes)
            ?? throw new InvalidDataException("Missing native capture index.");
        List<string> ordered = new();
        HashSet<string> ids = new(StringComparer.Ordinal);
        HashSet<string> research = new(StringComparer.Ordinal);
        foreach (CaseIdentity? row in data.Cases ?? throw new InvalidDataException("Missing native cases."))
            AddId(row?.Id ?? throw new InvalidDataException("Missing native case id."));

        if (Name == "filter-uniform-precision")
        {
            using JsonDocument status = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(Folder, "resolved-known-differences.json")));
            string captureText = TextEncoding.UTF8.GetString(bytes).Replace("\r\n", "\n", StringComparison.Ordinal);
            string captureHash = Convert.ToHexStringLower(SHA256.HashData(TextEncoding.UTF8.GetBytes(captureText)));
            if (status.RootElement.GetProperty("sourceCaptureSha256").GetString() != captureHash)
                throw new InvalidDataException("Historical filter capture changed after its status audit.");
            HashSet<string> resolved = ReadIds(status.RootElement.GetProperty("resolvedHistoricalIds"));
            HashSet<string> routine = ReadIds(status.RootElement.GetProperty("routineHistoricalIds"));
            foreach (CaseIdentity? row in data.KnownDifferences ?? throw new InvalidDataException("Missing historical filter cases."))
            {
                string id = row?.Id ?? throw new InvalidDataException("Missing historical filter id.");
                if (!resolved.Remove(id)) continue;
                AddId(id);
                if (!routine.Remove(id)) research.Add(id);
            }
            if (resolved.Count != 0 || routine.Count != 0)
                throw new InvalidDataException("Resolved filter status names an unknown historical input.");
        }

        if (Name is "gradient-color-alpha" or "filter-uniform-precision" or "filter-source-composition")
        {
            using JsonDocument selection = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(Folder, "research-cases.json")));
            foreach (JsonElement selected in selection.RootElement.GetProperty("cases").EnumerateArray())
            {
                string id = selected.GetString() ?? throw new InvalidDataException("Missing research case id.");
                if (!ids.Contains(id)) throw new InvalidDataException("Research selection names an unknown native case.");
                if (!research.Add(id)) throw new InvalidDataException("Research selection contains a duplicate case.");
            }
        }

        return new Selection(ordered.ToArray(), ids.ToFrozenSet(StringComparer.Ordinal), research.ToFrozenSet(StringComparer.Ordinal));

        void AddId(string id)
        {
            if (!ids.Add(id)) throw new InvalidDataException("Duplicate native case id: " + id);
            ordered.Add(id);
        }

        static HashSet<string> ReadIds(JsonElement array) => array.EnumerateArray()
            .Select(item => item.GetString() ?? throw new InvalidDataException("Missing historical filter id."))
            .ToHashSet(StringComparer.Ordinal);
    }

    private FrozenDictionary<string, JsonElement> ReadRows()
    {
        Selection selection = _selection.Value;
        using JsonDocument data = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(Folder, "capture.json")));
        // One shared immutable clone per bank, rather than a separate document per case.
        JsonElement root = data.RootElement.Clone();
        Dictionary<string, JsonElement> rows = new(StringComparer.Ordinal);
        foreach (JsonElement row in root.GetProperty("cases").EnumerateArray())
            rows.Add(RequiredId(row), row);
        if (Name == "filter-uniform-precision")
            foreach (JsonElement row in root.GetProperty("knownDifferences").EnumerateArray())
            {
                string id = RequiredId(row);
                if (selection.AllIds.Contains(id)) rows.Add(id, row);
            }
        if (rows.Count != selection.Ids.Length || rows.Keys.Any(id => !selection.AllIds.Contains(id)))
            throw new InvalidDataException("Native capture changed after case discovery.");
        return rows.ToFrozenDictionary(StringComparer.Ordinal);
    }

    private static string RequiredId(JsonElement row)
        => row.GetProperty("id").GetString() ?? throw new InvalidDataException("Missing native case id.");

    private sealed record Selection(string[] Ids, FrozenSet<string> AllIds, FrozenSet<string> ResearchIds);

    // All other capture fields are deliberately skipped during discovery, rather
    // than materializing rendering/provenance JSON for every unselected case.
    private sealed record CaptureIndex(
        [property: JsonPropertyName("cases")] CaseIdentity?[]? Cases,
        [property: JsonPropertyName("knownDifferences")] CaseIdentity?[]? KnownDifferences);

    private sealed record CaseIdentity([property: JsonPropertyName("id")] string? Id);
}
