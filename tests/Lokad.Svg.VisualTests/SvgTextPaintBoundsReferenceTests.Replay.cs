using System.Collections.Frozen;
using System.Security.Cryptography;
using System.Text.Json;
using Lokad.Svg.Fonts;

namespace Lokad.Svg.VisualTests;

public sealed partial class SvgTextPaintBoundsReferenceTests
{
    private static readonly Lazy<FrozenSet<string>> RoutinePreparedReplayIds = new(static () =>
    {
        using JsonDocument selection = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(Folder(), "prepared-replay.json")));
        HashSet<string> ids = new(StringComparer.Ordinal);
        foreach (JsonElement group in selection.RootElement.GetProperty("groups").EnumerateArray())
        foreach (JsonElement row in group.GetProperty("cases").EnumerateArray())
            if (!ids.Add(row.GetString() ?? throw new InvalidDataException("Missing prepared replay identity.")))
                throw new InvalidDataException("Duplicate prepared replay identity.");
        return ids.ToFrozenSet(StringComparer.Ordinal);
    });

    public static IEnumerable<object[]> PreparedReplayCases()
    {
        foreach (string id in Banks["image-capture.json"].CaseIds)
            if (RoutinePreparedReplayIds.Value.Contains(id)) yield return [id];
    }

    public static IEnumerable<object[]> ResearchPreparedReplayCases()
    {
        foreach (string id in Banks["image-capture.json"].CaseIds)
            if (!RoutinePreparedReplayIds.Value.Contains(id)) yield return [id];
    }

    [Theory]
    [MemberData(nameof(PreparedReplayCases))]
    [Trait("Layer", "Contract")]
    public Task Prepared_paint_replay_matches_canonical_output(string id)
        => AssertPreparedReplay(id);

    [Theory]
    [MemberData(nameof(ResearchPreparedReplayCases))]
    [Trait("Layer", "Contract")]
    [Trait("Category", "Research")]
    public Task Extended_prepared_paint_replay_matches_canonical_output(string id)
        => AssertPreparedReplay(id);

    [Fact]
    [Trait("Layer", "Integrity")]
    public void Prepared_replay_partition_preserves_every_original_and_declares_each_routine_interaction()
    {
        using JsonDocument selection = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(Folder(), "prepared-replay.json")));
        JsonElement root = selection.RootElement;
        string capture = File.ReadAllText(Path.Combine(Folder(), Required(root, "capture"))).Replace("\r\n", "\n", StringComparison.Ordinal);
        Assert.Equal(Required(root, "captureLfSha256"), Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(capture))));
        string[] originals = Banks["image-capture.json"].CaseIds.Order(StringComparer.Ordinal).ToArray();
        string[] routine = PreparedReplayCases().Select(row => (string)row[0]).ToArray();
        string[] research = ResearchPreparedReplayCases().Select(row => (string)row[0]).ToArray();
        Assert.Equal(root.GetProperty("originalCases").GetInt32(), originals.Length);
        Assert.Equal(root.GetProperty("routinePreparedCases").GetInt32(), routine.Length);
        Assert.Equal(root.GetProperty("extendedPreparedCases").GetInt32(), research.Length);
        Assert.Equal(RoutinePreparedReplayIds.Value.Order(StringComparer.Ordinal), routine.Order(StringComparer.Ordinal));
        Assert.Equal(originals, routine.Concat(research).Order(StringComparer.Ordinal));
        foreach (JsonElement group in root.GetProperty("groups").EnumerateArray())
        {
            Assert.False(string.IsNullOrWhiteSpace(Required(group, "duty")));
            Assert.False(string.IsNullOrWhiteSpace(Required(group, "reason")));
            Assert.NotEmpty(group.GetProperty("cases").EnumerateArray());
        }
    }

    private static async Task AssertPreparedReplay(string id)
    {
        JsonElement row = Banks["image-capture.json"].GetCase(id);
        byte[] input = File.ReadAllBytes(Path.Combine(Folder(), Required(row, "input")));
        string expectedSourceHash = Required(row, "sourceSha256");
        Assert.Equal(expectedSourceHash, Convert.ToHexStringLower(SHA256.HashData(input)));
        SvgFontPackCatalog catalog = NotoPaintPrograms.Catalog;
        NotoPaintPrograms provider = new();
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync(input, catalog, provider, CancellationToken.None);
        SvgRenderOptions options = new(null, row.GetProperty("outputScale").GetDouble(), SvgColor.Transparent, SvgRenderLimits.Default, snapshot);
        byte[] expected = SvgConverter.ConvertToPng(input, options, CancellationToken.None).ToArray();
        using SvgPreparedDocument document = await SvgPreparedDocument.CreateAsync(input, catalog, provider,
            new(null, options.OutputScale, SvgColor.Transparent, SvgRenderLimits.Default), CancellationToken.None);
        Assert.Equal(expected, document.ConvertToPng(CancellationToken.None).ToArray());
        Assert.Equal(expectedSourceHash, Convert.ToHexStringLower(SHA256.HashData(input)));
    }
}
