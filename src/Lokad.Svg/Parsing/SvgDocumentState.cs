using Lokad.Svg.Rendering;

namespace Lokad.Svg.Parsing;

/// <summary>Owns the bounded mutable state for one SVG document while XML traversal remains streaming.</summary>
internal sealed class SvgDocumentState : IDisposable
{
    private const int DefinitionStorageBytes = 256;
    private const int ClipStorageBytes = 128;
    // The tile embeds nullable scene geometry. Include its actual value size,
    // plus conservative bucket/key/entry overhead, before dictionary growth.
    private static readonly int PatternStorageBytes = checked(64 + (int)ParserCollectionMemory.GetArrayByteCount<SvgPatternTile>(1));
    // Covers one gradient entry plus one retained clamped stop array sized from its actual element type.
    private static readonly int GradientStorageBytes = checked(128 + (int)ParserCollectionMemory.GetArrayByteCount<SvgGradientStop>(SvgGradientSyntax.MaximumGradientStops));

    internal SvgDocumentState(RenderBudget budget, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        Budget = budget;
        Scene = new SvgSceneBuilder(budget, workCounter, cancellationToken);
        Strings = new BudgetedParserStringStore(budget);
        Groups = new BudgetedParserList<SvgGroupScope>(budget, SvgMemoryCategory.Parser, workCounter, cancellationToken);
        Switches = new BudgetedParserList<SwitchScope>(budget, SvgMemoryCategory.Parser, workCounter, cancellationToken);
        ClipDepths = new BudgetedParserList<int>(budget, SvgMemoryCategory.Parser, workCounter, cancellationToken);
        Viewports = new BudgetedParserList<SvgViewportFrame>(budget, SvgMemoryCategory.Parser, workCounter, cancellationToken);
        StyleRules = new BudgetedParserList<CssRule>(budget, SvgMemoryCategory.Parser, workCounter, cancellationToken);
        Definitions = new BudgetedParserDictionary<string, SvgDefinition>(budget, SvgMemoryCategory.Parser, DefinitionStorageBytes);
        Clips = new BudgetedParserDictionary<string, SvgClipBox>(budget, SvgMemoryCategory.Parser, ClipStorageBytes);
        ClipPolygons = new BudgetedParserDictionary<string, SvgClipPolygon>(budget, SvgMemoryCategory.Parser, ClipStorageBytes);
        ObjectClips = new BudgetedParserDictionary<string, SvgClipBox>(budget, SvgMemoryCategory.Parser, ClipStorageBytes);
        Masks = new BudgetedParserDictionary<string, byte>(budget, SvgMemoryCategory.Parser, ClipStorageBytes);
        MaskContents = new BudgetedParserDictionary<string, SvgMaskContent>(budget, SvgMemoryCategory.Parser, ClipStorageBytes);
        MaskRegions = new BudgetedParserDictionary<string, SvgClipBox>(budget, SvgMemoryCategory.Parser, ClipStorageBytes);
        MaskFractionRegions = new BudgetedParserDictionary<string, SvgClipBox>(budget, SvgMemoryCategory.Parser, ClipStorageBytes);
        MaskContentBoxes = new BudgetedParserDictionary<string, SvgClipBox>(budget, SvgMemoryCategory.Parser, ClipStorageBytes);
        Patterns = new BudgetedParserDictionary<string, SvgPatternTile>(budget, SvgMemoryCategory.Parser, PatternStorageBytes);
        Gradients = new BudgetedParserDictionary<string, SvgLinearGradient>(budget, SvgMemoryCategory.Parser, GradientStorageBytes);
        RadialGradients = new BudgetedParserDictionary<string, SvgRadialGradient>(budget, SvgMemoryCategory.Parser, GradientStorageBytes);
        GradientStops = new BudgetedParserList<SvgGradientStop>(budget, SvgMemoryCategory.Parser, workCounter, cancellationToken);
        PendingUses = new BudgetedParserList<PendingUse>(budget, SvgMemoryCategory.Parser, workCounter, cancellationToken);
        GroupSnapshots = new BudgetedParserDictionary<string, SvgGroupSnapshot>(budget, SvgMemoryCategory.Parser, DefinitionStorageBytes);
        GroupSnapshotStarts = new BudgetedParserList<SvgGroupSnapshotStart>(budget, SvgMemoryCategory.Parser, workCounter, cancellationToken);
        PendingLeafClips = new BudgetedParserList<PendingLeafClip>(budget, SvgMemoryCategory.Parser, workCounter, cancellationToken);
        PendingGroupClips = new BudgetedParserList<PendingGroupClip>(budget, SvgMemoryCategory.Parser, workCounter, cancellationToken);
        PendingLeafMasks = new BudgetedParserList<PendingLeafMask>(budget, SvgMemoryCategory.Parser, workCounter, cancellationToken);
        PendingGroupMasks = new BudgetedParserList<PendingGroupMask>(budget, SvgMemoryCategory.Parser, workCounter, cancellationToken);
        PendingLinearGradientHrefs = new BudgetedParserList<PendingLinearGradientHref>(budget, SvgMemoryCategory.Parser, workCounter, cancellationToken);
        PendingRadialGradientHrefs = new BudgetedParserList<PendingRadialGradientHref>(budget, SvgMemoryCategory.Parser, workCounter, cancellationToken);
        MaskScopes = new BudgetedParserList<SvgClipBox>(budget, SvgMemoryCategory.Parser, workCounter, cancellationToken);
        MaskScopeSnapshots = new BudgetedParserList<SvgClipBox>(budget, SvgMemoryCategory.Parser, workCounter, cancellationToken);
        GroupUseExtras = new System.Collections.Generic.Dictionary<int, System.Collections.Generic.List<int>>();
    }

    internal RenderBudget Budget { get; }

    internal SvgSceneBuilder Scene { get; }

    internal BudgetedParserStringStore Strings { get; }

    internal BudgetedParserList<SvgGroupScope> Groups { get; }

    /// <summary>Gets the open switch containers with their first-child consumption state.</summary>
    internal BudgetedParserList<SwitchScope> Switches { get; }

    /// <summary>Gets or sets the depth of the skipped switch subtree root, or -1 when nothing is skipped.</summary>
    internal int SkipDepth { get; set; } = -1;

    /// <summary>Gets or sets the root and output magnification applied after lowering, which scales flattening tolerance.</summary>
    internal double OutputMagnification { get; set; } = 1.0;

    internal BudgetedParserList<SvgViewportFrame> Viewports { get; }

    internal SvgViewportFrame? EffectiveFrame => Viewports.Count == 0 ? null : Viewports[^1];

    internal BudgetedParserList<CssRule> StyleRules { get; }

    internal BudgetedParserDictionary<string, SvgDefinition> Definitions { get; }

    /// <summary>Holds definition-array reservations until teardown releases them on success, failure or cancellation.</summary>
    internal List<RenderBudget.BudgetReservation> DefinitionReservations { get; } = new();

    internal BudgetedParserDictionary<string, SvgClipBox> Clips { get; }

    internal BudgetedParserDictionary<string, SvgClipBox> ObjectClips { get; }

    internal BudgetedParserDictionary<string, SvgClipPolygon> ClipPolygons { get; }

    internal BudgetedParserDictionary<string, byte> Masks { get; }

    internal BudgetedParserDictionary<string, SvgMaskContent> MaskContents { get; }

    internal SvgGeometry[]? MaskContentGeometries { get; set; }

    internal byte MaskContentLuminance { get; set; }

    internal bool MaskContentIsAlpha { get; set; }

    internal BudgetedParserDictionary<string, SvgClipBox> MaskRegions { get; }

    internal BudgetedParserDictionary<string, SvgClipBox> MaskFractionRegions { get; }

    internal BudgetedParserDictionary<string, SvgClipBox> MaskContentBoxes { get; }

    internal BudgetedParserDictionary<string, SvgPatternTile> Patterns { get; }

    internal BudgetedParserDictionary<string, SvgLinearGradient> Gradients { get; }

    internal BudgetedParserDictionary<string, SvgRadialGradient> RadialGradients { get; }

    internal BudgetedParserList<int> ClipDepths { get; }

    internal BudgetedParserList<PendingUse> PendingUses { get; }

    /// <summary>Gets closed groups with identifiers by scene geometry range for use-instance expansion.</summary>
    internal BudgetedParserDictionary<string, SvgGroupSnapshot> GroupSnapshots { get; }

    /// <summary>Gets the open group stack for snapshot ranges, aligned with the group scope stack.</summary>
    internal BudgetedParserList<SvgGroupSnapshotStart> GroupSnapshotStarts { get; }

    internal BudgetedParserList<PendingLeafClip> PendingLeafClips { get; }



    internal BudgetedParserList<PendingGroupClip> PendingGroupClips { get; }

    internal BudgetedParserList<PendingLeafMask> PendingLeafMasks { get; }

    internal BudgetedParserList<PendingGroupMask> PendingGroupMasks { get; }

    internal BudgetedParserList<PendingLinearGradientHref> PendingLinearGradientHrefs { get; }

    internal BudgetedParserList<PendingRadialGradientHref> PendingRadialGradientHrefs { get; }

    internal int GeometryElementCount { get; set; }

    internal bool DeferringLeafClip { get; set; }

    internal bool DeferringLeafMask { get; set; }

    /// <summary>Gets or sets whether a gradient href drain is already running, guarding reentrant drains during nested registration.</summary>
    internal bool DrainingGradientHrefs { get; set; }

    /// <summary>Gets or sets whether missing gradient href targets throw at registration instead of deferring, used for the document-end leftover pass.</summary>
    internal bool ThrowOnMissingGradientHref { get; set; }

    internal int ScaledViewportPushes { get; set; }

    internal int NodeCount { get; set; }

    internal int TextCharacterCount { get; set; }

    internal int GroupDepth { get; set; }

    internal bool HasOpacityScope { get; set; }

    internal bool HasUnsupportedContent { get; set; }

    internal bool SawStyleableNode { get; set; }

    internal int DefinitionsDepth { get; set; } = -1;

    internal int SymbolDepth { get; set; } = -1;

    internal int ClipPathDepth { get; set; } = -1;

    internal int MaskDepth { get; set; } = -1;

    internal int ViewportOpacityDepth { get; set; } = -1;

    internal int PatternDepth { get; set; } = -1;

    internal string? PatternId { get; set; }

    internal string? GradientId { get; set; }

    internal string? RadialGradientId { get; set; }

    internal SvgGradientTemplate? Gradient { get; set; }

    internal SvgRadialTemplate? RadialGradient { get; set; }

    internal BudgetedParserList<SvgGradientStop> GradientStops { get; }

    /// <summary>Gets the content boxes of the currently open masked-group scopes for child coverage checks.</summary>
    internal BudgetedParserList<SvgClipBox> MaskScopes { get; }

    /// <summary>Gets the append-only retained mask scope boxes snapshotted by deferred use references.</summary>
    internal BudgetedParserList<SvgClipBox> MaskScopeSnapshots { get; }

    /// <summary>Maps expanded use placeholder slots to their appended extra scene geometry indices, so group snapshots read evacuated content in place order.</summary>
    internal System.Collections.Generic.Dictionary<int, System.Collections.Generic.List<int>> GroupUseExtras { get; }

    internal int GradientDepth { get; set; } = -1;

    internal int RadialGradientDepth { get; set; } = -1;

    internal SvgPatternTile? PatternTile { get; set; }

    internal string? MaskId { get; set; }

    internal bool MaskIsAlpha { get; set; }

    internal byte? MaskValue { get; set; }

    internal SvgClipBox? MaskCustomRegion { get; set; }

    internal SvgClipBox? MaskFractionRegion { get; set; }

    internal SvgClipBox? MaskContentBox { get; set; }

    internal string? ClipPathId { get; set; }

    internal SvgClipBox? ClipBox { get; set; }

    internal SvgClipPolygon? ClipPolygon { get; set; }

    internal bool ClipIsObjectBoundingBox { get; set; }

    internal bool GroupUseExpanded { get; set; }

    internal string? SymbolId { get; set; }

    internal SvgViewBox? SymbolViewBox { get; set; }

    private bool _definitionsReleased;

    /// <summary>Releases definition-owned geometry reservations exactly once, covering success after scene build and failure or cancellation during teardown.</summary>
    internal void ReleaseDefinitions()
    {
        if (_definitionsReleased) return;
        _definitionsReleased = true;
        foreach (SvgDefinition value in Definitions.Values)
            foreach (SvgGeometry geometry in value.Geometries)
                geometry.Dispose();
    }

    public void Dispose()
    {
        ReleaseDefinitions();
        PendingUses.Dispose();
        GroupSnapshots.Dispose();
        GroupSnapshotStarts.Dispose();
        PendingLeafClips.Dispose();
        PendingGroupClips.Dispose();
        PendingLeafMasks.Dispose();
        PendingGroupMasks.Dispose();
        PendingLinearGradientHrefs.Dispose();
        PendingRadialGradientHrefs.Dispose();
        Definitions.Dispose();
        foreach (RenderBudget.BudgetReservation reservation in DefinitionReservations) reservation.Dispose();
        DefinitionReservations.Clear();
        Clips.Dispose();
        ClipPolygons.Dispose();
        ObjectClips.Dispose();
        Masks.Dispose();
        foreach (SvgMaskContent content in MaskContents.Values) foreach (SvgGeometry geometry in content.Geometries) geometry.Dispose();
        MaskContents.Dispose();
        MaskRegions.Dispose();
        MaskFractionRegions.Dispose();
        MaskContentBoxes.Dispose();
        Patterns.Dispose();
        Gradients.Dispose();
        RadialGradients.Dispose();
        GradientStops.Dispose();
        MaskScopes.Dispose();
        MaskScopeSnapshots.Dispose();
        ClipDepths.Dispose();
        StyleRules.Dispose();
        Strings.Dispose();
        Switches.Dispose();
        Groups.Dispose();
        Viewports.Dispose();
        Scene.Dispose();
    }
}

/// <summary>Represents one inherited group or link context while streaming the document.</summary>
internal readonly record struct SvgGroupScope(int Depth, SvgGroupKind Kind, SvgInheritedPresentation InheritedPresentation, SvgInheritedTextStyle InheritedTextStyle, bool IsolatesOpacity, bool HasMaskScope, SvgInheritedStrokeStyle InheritedStroke, string? FillRule);

internal enum SvgGroupKind { Group, Link, Switch }

/// <summary>Records one closed identifier-bearing group as a scene geometry range for use-instance expansion.</summary>
internal readonly record struct SvgGroupSnapshot(int GeometryStart, int GeometryCount, SvgGroupKind Kind, bool IsolatesOpacity, bool HasClip, bool HasMask, SvgAffineTransform AncestorTransform, bool HasViewport);

/// <summary>Tracks one open group for snapshot ranges, aligned with the group scope stack.</summary>
internal readonly record struct SvgGroupSnapshotStart(string? Id, int GeometryStart, bool HasClip, bool HasMask, SvgAffineTransform AncestorTransform);

