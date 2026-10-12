using System.Xml;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Parsing;

internal readonly record struct SvgPatternContentUse(SvgResourceUse Use, int CommandIndex);

/// <summary>The document owns the staging builder; the parent scene owns content borrowed by pattern paints.</summary>
internal readonly record struct SvgDeferredPatternContent(SvgSceneBuilder Builder, SvgVectorContent Content, int FirstUse, int UseCount);

/// <summary>Resolves ordered local tile instances through existing reference and premultiplied scene machinery.</summary>
/// <remarks>References borrow document-owned strings and definitions. The staging builder retains all mapped
/// owners until sealing or failure cleanup. Replay is parser-thread confined, charged to shared work and
/// cancellation aware. Content becomes immutable before the root scene is returned.</remarks>
internal static class SvgPatternUseReplay
{
    internal static void Retain(XmlReader reader, SvgRootAttributes root, SvgDocumentState state,
        IReadOnlyDictionary<string, SvgColor> colors, SvgSceneBuilder builder, ParserWorkCounter work, CancellationToken token)
    {
        if (reader.GetAttribute("clip-path") is not null || reader.GetAttribute("mask") is not null || reader.GetAttribute("filter") is not null)
            throw new SvgException(SvgErrorCode.UnsupportedFeature, "Clips, masks and filters on pattern uses are not implemented.");
        SvgResourceUse reference = SvgResourceUseReplay.Retain(reader, root, state, colors, work, token);
        if (state.PatternUseCount == 0) state.PatternUseStart = state.PatternUses.Count;
        state.PatternUses.Add(new(reference, builder.AddPaintPlaceholder()));
        state.PatternUseCount++;
    }

    internal static void Resolve(SvgDocumentState state, ParserWorkCounter work, CancellationToken token)
    {
        foreach (SvgDeferredPatternContent pending in state.DeferredPatternContents.AsSpan())
        {
            work.Consume(token);
            // Descending positions preserve every unresolved slot when a use expands to several draws.
            for (int index = pending.FirstUse + pending.UseCount - 1; index >= pending.FirstUse; index--)
            {
                work.Consume(token);
                SvgPatternContentUse use = state.PatternUses[index];
                int tail = pending.Builder.CommandCount;
                SvgResourceUseReplay.AppendPatternUse(use.Use, pending.Builder, state, work, token);
                pending.Builder.ResolvePaintPlaceholder(use.CommandIndex, tail);
            }
            SvgLeafMaskComposition.Emit(state, pending.Builder, work, token);
            pending.Content.Complete(pending.Builder.Build());
        }
    }
}
