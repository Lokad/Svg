using System.Xml;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Parsing;

internal readonly record struct SvgPatternContentUse(SvgResourceUse Use, int CommandIndex);
internal readonly record struct SvgPatternContentClip(string Id, SvgAffineTransform Map, SvgRecordingScope Scope, bool HasOpacity);

/// <summary>The document owns the staging builder; the parent scene owns content borrowed by pattern paints.</summary>
internal readonly record struct SvgDeferredPatternContent(SvgSceneBuilder Builder, SvgVectorContent Content, int FirstUse, int UseCount, int FirstClip, int ClipCount);

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
            for (int index = pending.FirstClip; index < pending.FirstClip + pending.ClipCount; index++)
                ApplyClip(state.PatternClips?[index] ?? throw new InvalidOperationException("Retained pattern clips require their source registry."), pending.Builder, state, work, token);
            SvgLeafMaskComposition.Emit(state, pending.Builder, work, token);
            pending.Content.Complete(pending.Builder.Build());
        }

        // Scope identities survive use expansion, unlike absolute command offsets.
        // Bind through the ordinary resource clip machinery before the scene seals.
        static void ApplyClip(SvgPatternContentClip clip, SvgSceneBuilder builder, SvgDocumentState state, ParserWorkCounter work, CancellationToken token)
        {
            int depth = 0;
            int first = -1;
            SvgLeafMaskBinding? lastInner = null, lastBinding = null;
            for (int index = 0; index < builder.CommandCount; index++)
            {
                work.Consume(token);
                SvgDrawCommand command = builder.CommandAt(index);
                if (first < 0)
                {
                    if (command.Kind != SvgDrawCommandKind.BeginGroup || command.GeometryIndex != clip.Scope.Value) continue;
                    first = index;
                }
                if (command.Kind == SvgDrawCommandKind.BeginGroup) depth++;
                else if (command.Kind == SvgDrawCommandKind.EndGroup && --depth == 0)
                {
                    if (!clip.HasOpacity)
                    {
                        // Clip-only groups do not create an isolated color surface.
                        // Their stable markers were needed only until uses expanded.
                        BudgetedSceneList<SvgDrawCommand> commands = new(state.Budget, work, token);
                        bool transferred = false;
                        try
                        {
                            for (int copy = 0; copy < builder.CommandCount; copy++)
                            {
                                work.Consume(token);
                                if (copy != first && copy != index) commands.Add(builder.CommandAt(copy));
                            }
                            builder.AdoptCommands(commands);
                            transferred = true;
                        }
                        finally { if (!transferred) commands.Dispose(); }
                    }
                    return;
                }
                if (command.Kind != SvgDrawCommandKind.Geometry) continue;
                SvgLeafMaskBinding? inner = builder.GeometryAt(command.GeometryIndex).LeafMask;
                if (lastBinding is null || !ReferenceEquals(lastInner, inner))
                {
                    lastInner = inner;
                    lastBinding = SvgClipPathSyntax.BindResourceClip(clip.Id, clip.Map, inner, state, work, token);
                }
                builder.AssignLeafMask(command.GeometryIndex, lastBinding);
            }
            throw new InvalidOperationException("A retained pattern clip requires a balanced source group.");
        }
    }
}
