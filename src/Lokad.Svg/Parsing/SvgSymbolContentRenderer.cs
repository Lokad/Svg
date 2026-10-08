using Lokad.Svg.Rendering;

namespace Lokad.Svg.Parsing;

/// <summary>Expands retained symbol scopes through the existing instance geometry and recorded-paint machinery.</summary>
internal static class SvgSymbolContentRenderer
{
    private readonly record struct Context(SvgPaint Paint, SvgAffineTransform Transform, SvgClipBox? Clip, bool Displayed, bool Visible);
    private readonly record struct Frame(SvgSymbolContentRange Range, int Next, Context Context, int OutputStart, SvgPaintOpacity Opacity, SvgRecordingScope Scope, SvgClipBox? CompositionClip);

    internal static SvgGeometry[] Apply(SvgDefinition definition, SvgUseReference reference, SvgViewportFrame? viewport, SvgDocumentState state, ParserWorkCounter work, CancellationToken token)
    {
        if (reference.PaintServerId is not null || reference.StrokeOverride is not null)
            throw new SvgException(SvgErrorCode.UnsupportedFeature, "Paint-server and stroke overrides on retained symbol content are not implemented.");
        SvgSymbolContentRange root = definition.SymbolContent ?? throw new InvalidOperationException("Retained content requires a source range.");
        using BudgetedParserList<SvgGeometry> output = new(state.Budget, SvgMemoryCategory.Parser, work, token);
        using BudgetedParserList<Frame> stack = new(state.Budget, SvgMemoryCategory.Parser, work, token);
        Context context = Place(definition, reference, viewport, reference.IsDisplayed, reference.IsVisible);
        stack.Add(new(root, root.Start, context, 0, SvgPaintOpacity.Opaque, state.NewRecordingScope(), null));
        try
        {
            while (stack.Count > 0)
            {
                work.Consume(token);
                Frame frame = stack[^1];
                if (frame.Next == frame.Range.Start + frame.Range.Count)
                {
                    SvgPaintRecording.WrapInstance(output.AsMutableSpan().Slice(frame.OutputStart), frame.Opacity, frame.Scope, frame.CompositionClip, state, work, token);
                    stack.RemoveLast(); continue;
                }
                SvgSymbolContentNode node = frame.Range.Content.Nodes[frame.Next];
                stack[^1] = frame with { Next = frame.Next + 1 };
                if (node.Kind == SvgSymbolContentKind.Group)
                {
                    Context child = frame.Context with { Paint = node.Style.Resolve(frame.Context.Paint), Transform = frame.Context.Transform.ComposeChecked(node.Transform), Displayed = frame.Context.Displayed && node.Style.IsDisplayed, Visible = node.Style.IsVisible ?? frame.Context.Visible };
                    stack[^1] = frame with { Next = node.End + 1 };
                    SvgSymbolContentRange contents = new(frame.Range.Content, frame.Next + 1, node.End - frame.Next - 1);
                    SvgClipBox? compositionClip = node.Style.Opacity.Value != 1 ? child.Clip : null;
                    // An inherited viewport clip surrounds the isolated container. Apply it
                    // once when restoring the group; children retain only their local clips.
                    if (compositionClip is not null) child = child with { Clip = null };
                    stack.Add(new(contents, contents.Start, child, output.Count, node.Style.Opacity, state.NewRecordingScope(), compositionClip));
                    continue;
                }
                if (node.Kind == SvgSymbolContentKind.EndGroup) throw new InvalidOperationException("Symbol range includes an unmatched end marker.");
                bool displayed = frame.Context.Displayed && node.Style.IsDisplayed;
                bool visible = node.Style.IsVisible ?? frame.Context.Visible;
                if (node.Kind == SvgSymbolContentKind.Leaf)
                {
                    SvgUseReference leaf = new(string.Empty, 0, 0, null, null, frame.Context.Transform, SvgAffineTransform.Identity, 255, displayed && visible, null, null, null)
                    { InheritedFill = frame.Context.Paint, RecordingScope = state.NewRecordingScope(), AllowsSolidPaintAnalysis = true, IsDisplayed = displayed, IsVisible = visible };
                    AddMapped(node.Definition, leaf, frame.Context.Clip);
                    continue;
                }
                if (!state.Definitions.TryGetValue(node.Reference.Id, out SvgDefinition target))
                    throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only defined local references in symbol content are implemented.");
                SvgPaint instancePaint = node.Style.Resolve(frame.Context.Paint);
                SvgUseReference use = node.Reference with { Transform = frame.Context.Transform.ComposeChecked(node.Reference.Transform), InheritedTransform = frame.Context.Transform, InheritedFill = instancePaint, FillOverride = node.Reference.FillOverride is null ? null : instancePaint, Opacity = 255, IsPainted = displayed && visible, IsDisplayed = displayed, IsVisible = visible, RecordingScope = state.NewRecordingScope() };
                if (target.SymbolContent is {} range)
                {
                    if (use.PaintServerId is not null || use.StrokeOverride is not null)
                        throw new SvgException(SvgErrorCode.UnsupportedFeature, "Paint-server and stroke overrides on retained symbol content are not implemented.");
                    Context child = Place(target, use, ClipFrame(frame.Context.Clip), displayed, visible);
                    stack.Add(new(range, range.Start, child, output.Count, node.Reference.SourceOpacity, use.RecordingScope, null));
                }
                else
                {
                    int start = output.Count;
                    AddMapped(target with { SymbolFillBindings = target.SourceFillBindings ?? target.SymbolFillBindings }, use, frame.Context.Clip);
                    SvgPaintRecording.WrapInstance(output.AsMutableSpan().Slice(start), node.Reference.SourceOpacity, use.RecordingScope, state, work, token);
                }
            }
            SvgGeometry[] result = SvgReferenceResolver.RetainInstanceBatch(output.AsSpan(), state, work, token);
            output.Clear();
            return result;
        }
        catch { foreach (SvgGeometry geometry in output.AsSpan()) geometry.Dispose(); throw; }

        void AddMapped(SvgDefinition source, SvgUseReference use, SvgClipBox? clip)
        {
            SvgGeometry[] mapped = SvgReferenceResolver.Apply(source, use, ClipFrame(clip), state, work, token);
            int transferred = 0;
            try { for (; transferred < mapped.Length; transferred++) output.Add(mapped[transferred]); }
            catch { for (int index = transferred; index < mapped.Length; index++) mapped[index].Dispose(); throw; }
        }
    }

    private static SvgViewportFrame? ClipFrame(SvgClipBox? clip)
        => clip is {} value ? new(0, 0, 1, 1, null, null, value, -1) : null;

    private static Context Place(SvgDefinition definition, SvgUseReference reference, SvgViewportFrame? frame, bool displayed, bool visible)
    {
        SvgAffineTransform map = definition.SymbolViewBox is {} box ? SvgReferenceResolver.SymbolInstancePlacement(box, reference, frame) : SvgReferenceResolver.PlainInstancePlacement(reference, frame);
        SvgClipBox? clip = frame?.Clip;
        if (definition.SymbolViewBox is not null)
        {
            if (reference.Width is null || reference.Height is null) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Symbol placement requires known instance dimensions.");
            SvgClipBox local = reference.Transform.Apply(new SvgClipBox(reference.X, reference.Y, reference.Width.Value, reference.Height.Value));
            if (frame is {} viewport) local = new(local.X * viewport.ScaleX + viewport.OffsetX, local.Y * viewport.ScaleY + viewport.OffsetY, local.Width * viewport.ScaleX, local.Height * viewport.ScaleY);
            clip = clip is {} parent ? local.Intersect(parent) : local;
        }
        return new(reference.FillOverride ?? reference.InheritedFill, map, clip, displayed, visible);
    }
}
