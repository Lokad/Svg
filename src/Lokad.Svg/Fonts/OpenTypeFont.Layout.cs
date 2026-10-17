namespace Lokad.Svg.Fonts;

using Lokad.Svg.Parsing;

internal static partial class OpenTypeFont
{
    /// <summary>Selects one feature through the default Latin language system, falling back to DFLT.</summary>
    /// <remarks>Feature tags can repeat across scripts. Only the selected language's feature indices
    /// select behavior; other language-specific features remain unused. Borrows validated table bytes,
    /// allocates no storage and meters every bounded traversal against work/cancellation.</remarks>
    private static int SelectLatinFeature(ReadOnlySpan<byte> data, OpenTypeTableRange table, int scripts, int features,
        uint featureTag, string tableName, ParserWorkCounter? work, CancellationToken token)
    {
        int scriptCount = ReadUInt16(data, scripts);
        RequireTableRange(table, scripts, 2 + scriptCount * 6, tableName + " ScriptList");
        int latin = 0, fallback = 0;
        for (int index = 0; index < scriptCount; index++)
        {
            Consume(work, token);
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
            Consume(work, token);
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
