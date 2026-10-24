namespace Lokad.Svg.Parsing;

using Lokad.Svg.Rendering;

/// <summary>Carries one nested viewport map, user-space percentage bases, absolute clip box and XML depth.</summary>
internal readonly record struct SvgViewportFrame(double OffsetX, double OffsetY, double ScaleX, double ScaleY, double? ViewportWidth, double? ViewportHeight, SvgClipBox Clip, int Depth)
{
    internal SvgPaintOpacity? OwnOpacity { get; init; }
    /// <summary>Reports a physical affine clip emitted around this viewport's original scene commands.</summary>
    internal bool HasAffineClipScope { get; init; }
    /// <summary>Borrows bounded presentation/text state without copying it into every deferred instance frame.</summary>
    internal SvgViewportTextContext? TextContext { get; init; }

    /// <summary>Composes a source geometry/paint map with its scalar viewport without folding tile-local coordinates into it.</summary>
    internal static SvgAffineTransform MapGeometry(SvgAffineTransform source, SvgViewportFrame? frame)
        => frame is {} viewport
            ? SvgAffineTransform.FromScaleOffset(viewport.ScaleX, viewport.ScaleY, viewport.OffsetX, viewport.OffsetY).ComposeChecked(source)
            : source;

    /// <summary>Places text after the viewport map while preserving intervening source-group transforms.</summary>
    /// <remarks>Immutable bounded arithmetic; callers charge the mapping and observe cancellation. Singular source ancestry retains an explicit unsupported outcome.</remarks>
    internal SvgAffineTransform MapText(SvgAffineTransform source)
    {
        if (TextContext?.Inheritance is not {} inherited) return SvgAffineTransform.FromScaleOffset(ScaleX, ScaleY, OffsetX, OffsetY).ComposeChecked(source);
        if (!inherited.Presentation.Transform.TryInvert(out SvgAffineTransform inverse))
            throw new SvgException(SvgErrorCode.UnsupportedFeature, "Singular text viewport ancestry is not implemented.");
        return TextContext.Placement.ComposeChecked(inverse.ComposeChecked(source));
    }

    /// <summary>Returns registry-owned viewport clips shared across glyph paints and retained text paths.</summary>
    /// <remarks>Frames and metadata belong to one conversion. Each ancestor consumes work and observes cancellation; registry teardown releases partial reservations on failure. The returned chain is borrowed until final command emission.</remarks>
    internal static SvgLeafMaskBinding? ResolveGlyphClip(SvgDocumentState state, ParserWorkCounter work, CancellationToken token)
    {
        if (state.DefinitionReplayScene is not null) return null;
        SvgLeafMaskBinding? parent = null;
        for (int index = 0; index < state.Viewports.Count; index++)
        {
            work.Consume(token);
            SvgViewportFrame frame = state.Viewports[index];
            if (frame.Depth < state.IsolatedViewportDepth) continue;
            // A forward group clip contributes a map-only passthrough frame until
            // its real clip binds; the unbounded sentinel is not a native contour.
            if (frame.HasAffineClipScope || frame.Clip == SvgClipBox.Unbounded && frame.TextContext is null) continue;
            SvgViewportTextContext context = frame.TextContext ?? SvgViewportTextContext.Create(state, null,
                SvgAffineTransform.FromScaleOffset(frame.ScaleX, frame.ScaleY, frame.OffsetX, frame.OffsetY), new(frame.Clip, SvgAffineTransform.Identity));
            if (frame.TextContext is null) state.Viewports[index] = frame with { TextContext = context };
            if (context.GlyphClip is null)
            {
                SvgLeafMaskOperation operation = SvgLeafMaskComposition.NewOperation(state, null, SvgAffineTransform.Identity);
                operation.Resolved = true; operation.Value = 255;
                if (context.Viewport is {} viewport)
                {
                    operation.AffineViewport = viewport;
                    operation.RegionKind = SvgLayerRegionKind.AffineViewport;
                }
                else
                {
                    operation.Region = frame.Clip;
                    operation.RegionKind = SvgLayerRegionKind.Viewport;
                }
                parent = SvgLeafMaskComposition.NewBinding(state, operation, parent);
                context.GlyphClip = parent;
            }
            else parent = context.GlyphClip;
        }
        return parent;
    }
    internal SvgPaintOpacity ElementOpacity => OwnOpacity ?? SvgPaintOpacity.Opaque;
    internal bool IsScaled => ScaleX != 1 || ScaleY != 1;

    internal bool IsTrivial => OffsetX == 0 && OffsetY == 0 && !IsScaled;

    /// <summary>Shifts one fill box into viewport-parent space without clipping.</summary>
    internal SvgRectangle Shift(SvgRectangle rectangle)
        => rectangle with { X = rectangle.X * ScaleX + OffsetX, Y = rectangle.Y * ScaleY + OffsetY, Width = rectangle.Width * ScaleX, Height = rectangle.Height * ScaleY, Clip = rectangle.Clip is {} clip ? new SvgClipBox(clip.X * ScaleX + OffsetX, clip.Y * ScaleY + OffsetY, clip.Width * ScaleX, clip.Height * ScaleY) { Antialiasing = clip.Antialiasing } : null };

    /// <summary>Shifts one axis-aligned line into viewport-parent space without clipping.</summary>
    internal SvgAxisAlignedLine Shift(SvgAxisAlignedLine line)
        => line with { X1 = line.X1 * ScaleX + OffsetX, X2 = line.X2 * ScaleX + OffsetX, Y1 = line.Y1 * ScaleY + OffsetY, Y2 = line.Y2 * ScaleY + OffsetY };

    /// <summary>Shifts one fill box into viewport-parent space, then clips it to the viewport.</summary>
    internal SvgRectangle ClipShifted(SvgRectangle rectangle) => Clip.ClipRectangle(Shift(rectangle));

    /// <summary>Shifts one axis-aligned line into viewport-parent space and clips its painted stroke band.</summary>
    internal SvgGeometry ClipShifted(SvgAxisAlignedLine line) => Clip.ClipLine(Shift(line));
}


/// <summary>Owns one aggregate allowance for borrowed viewport style/maps and a lazily shared glyph clip chain.</summary>
/// <remarks>The document registry owns this conversion-thread state. Fonts/strings/clip nodes remain borrowed;
/// disposal only releases the material metadata allowance. Creation reserves before allocation and registry
/// insertion, and failures release partial ownership. Callers charge traversal and observe cancellation.</remarks>
internal sealed class SvgViewportTextContext : IDisposable
{
    private readonly RenderBudget.BudgetReservation _owner;
    internal SvgInheritedContainer? Inheritance { get; }
    internal SvgAffineTransform Placement { get; }
    internal SvgAffineViewportClip? Viewport { get; }
    internal SvgLeafMaskBinding? GlyphClip { get; set; }
    private SvgViewportTextContext(SvgInheritedContainer? inheritance, SvgAffineTransform placement, SvgAffineViewportClip viewport, RenderBudget.BudgetReservation owner)
    { Inheritance = inheritance; Placement = placement; Viewport = viewport; _owner = owner; }

    internal static SvgViewportTextContext Create(SvgDocumentState state, SvgInheritedContainer? inheritance, SvgAffineTransform placement, SvgAffineViewportClip viewport)
    {
        RenderBudget.BudgetReservation owner = state.Budget.Reserve(320, SvgMemoryCategory.Parser);
        try
        {
            SvgViewportTextContext context = new(inheritance, placement, viewport, owner);
            state.LeafMaskOwners.Add(new(context));
            return context;
        }
        catch { owner.Dispose(); throw; }
    }
    public void Dispose() => _owner.Dispose();
}
