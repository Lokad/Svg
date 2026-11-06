namespace Lokad.Svg.Fonts;

using Lokad.Svg.Parsing;

internal static partial class OpenTypeFont
{
    /// <summary>Tests previously validated input coverage against borrowed current glyphs; cancellable and allocation-free.</summary>
    private static bool CoverageIntersectsRun(ReadOnlySpan<byte> data, int coverage, ReadOnlySpan<SvgShapingGlyph> glyphs, CancellationToken token)
    {
        foreach (SvgShapingGlyph glyph in glyphs)
        {
            token.ThrowIfCancellationRequested();
            if (GetGlyphCoverageIndex(data, coverage, glyph.GlyphIndex, token) >= 0) return true;
        }
        return false;
    }

    /// <summary>Visits a validated feature union in LookupList order, once per selected index.</summary>
    /// <remarks>Borrowed feature offsets must already have bounded counts and valid lookup indices.
    /// Allocates no selection array; observes cancellation while scanning each small feature.</remarks>
    private static bool TryGetNextSelectedLookup(ReadOnlySpan<byte> data, OpenTypeTableRange table,
        int lookupList, ReadOnlySpan<int> features, ref int previous, out int offset, CancellationToken token)
    {
        int next = int.MaxValue;
        foreach (int feature in features)
        {
            if (feature == 0) continue;
            int count = ReadUInt16(data, feature + 2);
            for (int index = 0; index < count; index++)
            {
                token.ThrowIfCancellationRequested();
                int candidate = ReadUInt16(data, feature + 4 + index * 2);
                if (candidate > previous && candidate < next) next = candidate;
            }
        }
        offset = 0;
        if (next == int.MaxValue) return false;
        previous = next;
        offset = GetTableOffset(table, lookupList, ReadUInt16(data, lookupList + 2 + next * 2), 6, "OpenType selected lookup");
        return true;
    }

    /// <summary>Selects one feature through the default Latin language system, falling back to DFLT.</summary>
    /// <remarks>Feature tags can repeat across scripts. Only the selected language's feature indices
    /// select behavior; other language-specific features remain unused. Borrows validated table bytes,
    /// allocates no storage and observes cancellation during bounded traversal.</remarks>
    private static int SelectLatinFeature(ReadOnlySpan<byte> data, OpenTypeTableRange table, int scripts, int features,
        uint featureTag, string tableName, CancellationToken token)
    {
        int scriptCount = ReadUInt16(data, scripts);
        RequireTableRange(table, scripts, 2 + scriptCount * 6, tableName + " ScriptList");
        int latin = 0, fallback = 0;
        for (int index = 0; index < scriptCount; index++)
        {
            token.ThrowIfCancellationRequested();
            int record = scripts + 2 + index * 6;
            uint tag = ReadUInt32(data, record);
            if (tag != LatinScriptTag && tag != DefaultScriptTag) continue;
            int selected = GetTableOffset(table, scripts, ReadUInt16(data, record + 4), 4, tableName + " script");
            if (tag == LatinScriptTag)
            {
                if (latin != 0) throw InvalidFont("The " + tableName + " ScriptList repeats its Latin script.");
                latin = selected;
            }
            else
            {
                if (fallback != 0) throw InvalidFont("The " + tableName + " ScriptList repeats its default script.");
                fallback = selected;
            }
        }
        int script = latin != 0 ? latin : fallback;
        if (script == 0 || ReadUInt16(data, script) == 0) return 0;
        RequireTableRange(table, script, 4 + ReadUInt16(data, script + 2) * 6, tableName + " script languages");
        int language = GetTableOffset(table, script, ReadUInt16(data, script), 6, tableName + " default LangSys");
        if (ReadUInt16(data, language) != 0) throw InvalidFont("The " + tableName + " LangSys lookupOrder must be zero.");
        int featureCount = ReadUInt16(data, features);
        RequireTableRange(table, features, 2 + featureCount * 6, tableName + " FeatureList");
        int selectedCount = ReadUInt16(data, language + 4);
        RequireTableRange(table, language, 6 + selectedCount * 2, tableName + " LangSys");
        int required = ReadUInt16(data, language + 2);
        int feature = 0;
        for (int index = -1; index < selectedCount; index++)
        {
            token.ThrowIfCancellationRequested();
            int selected = index < 0 ? required : ReadUInt16(data, language + 6 + index * 2);
            if (selected == ushort.MaxValue && index < 0) continue;
            if (selected >= featureCount) throw InvalidFont("The " + tableName + " LangSys feature index is invalid.");
            int record = features + 2 + selected * 6;
            if (ReadUInt32(data, record) != featureTag) continue;
            int next = GetTableOffset(table, features, ReadUInt16(data, record + 4), 4, tableName + " selected feature");
            if (feature != 0 && feature != next) throw new SvgException(SvgErrorCode.UnsupportedFeature,
                "Multiple default Latin features with the same tag are not supported.");
            feature = next;
        }
        return feature;
    }
}
