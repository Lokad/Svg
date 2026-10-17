using Lokad.Svg.Rendering;

namespace Lokad.Svg.Parsing;

/// <summary>Owns the bounded mutable state for one SVG document while XML traversal remains streaming.</summary>
internal sealed class SvgDocumentState : IDisposable
{
    private int _nextPaintOperationIdentity;
    private int _nextRecordingScope = 1;
    internal SvgPaintOperationIdentity NewPaintOperationIdentity()
    {
        if (_nextPaintOperationIdentity == int.MaxValue) throw new SvgException(SvgErrorCode.LimitExceeded, "Paint identity storage exceeds the supported range.");
        return new(++_nextPaintOperationIdentity);
    }
    internal SvgRecordingScope NewRecordingScope()
    {
        if (_nextRecordingScope == int.MaxValue) throw new SvgException(SvgErrorCode.LimitExceeded, "Recording scope storage exceeds the supported range.");
        return new(++_nextRecordingScope);
    }
    private BudgetedParserList<SvgDefinition>? _anonymousSymbols;
    private BudgetedParserList<SvgSymbolContentRange>? _strokeContents;
    private RenderBudget.BudgetReservation? _strokeContentOwner;
    private RenderBudget.BudgetReservation? _anonymousSymbolOwner;
    private static readonly int DefinitionStorageBytes = checked(64 + System.Runtime.CompilerServices.Unsafe.SizeOf<SvgDefinition>());
    private const int ClipStorageBytes = 128;
    // The tile embeds nullable scene geometry. Include its actual value size,
    // plus conservative bucket/key/entry overhead, before dictionary growth.
    private static readonly int PatternStorageBytes = checked(64 + (int)ParserCollectionMemory.GetArrayByteCount<SvgPatternTile>(1));
    // Entry allowances exclude stop arrays, which reserve their actual lengths before allocation.
    private static readonly int GradientStorageBytes = checked(64 + (int)System.Runtime.CompilerServices.Unsafe.SizeOf<SvgLinearGradient>());

    // Radial entries retain their actual paint value, including the separate shader matrices.
    private static readonly int RadialGradientStorageBytes = checked(64 + (int)System.Runtime.CompilerServices.Unsafe.SizeOf<SvgRadialGradient>());

    private readonly bool _ownsStyleStorage;

    internal SvgDocumentState(RenderBudget budget, ParserWorkCounter workCounter, CancellationToken cancellationToken)
        : this(budget, workCounter, cancellationToken, new(budget, SvgMemoryCategory.Parser, workCounter, cancellationToken), new(budget), true)
    {
    }

    /// <summary>Uses collected style storage without copying it; borrowed stores outlive this state and are released by the caller.</summary>
    internal SvgDocumentState(RenderBudget budget, ParserWorkCounter workCounter, CancellationToken cancellationToken, BudgetedParserList<CssRule> styleRules, BudgetedParserStringStore strings, bool ownsStyleStorage)
    {
        Budget = budget;
        _documentScene = new SvgSceneBuilder(budget, workCounter, cancellationToken);
        Strings = strings;
        _ownsStyleStorage = ownsStyleStorage;
        Groups = new BudgetedParserList<SvgGroupScope>(budget, SvgMemoryCategory.Parser, workCounter, cancellationToken);
        Switches = new BudgetedParserList<SwitchScope>(budget, SvgMemoryCategory.Parser, workCounter, cancellationToken);
        ClipDepths = new BudgetedParserList<int>(budget, SvgMemoryCategory.Parser, workCounter, cancellationToken);
        Viewports = new BudgetedParserList<SvgViewportFrame>(budget, SvgMemoryCategory.Parser, workCounter, cancellationToken);
        StyleRules = styleRules;
        CssAncestors = new(styleRules, budget, workCounter, cancellationToken);
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
        PatternDefinitions = new(budget, SvgMemoryCategory.Parser, checked(64 + (int)ParserCollectionMemory.GetArrayByteCount<SvgPatternDefinition>(1)));
        Gradients = new BudgetedParserDictionary<string, SvgLinearGradient>(budget, SvgMemoryCategory.Parser, GradientStorageBytes);
        RadialGradients = new BudgetedParserDictionary<string, SvgRadialGradient>(budget, SvgMemoryCategory.Parser, RadialGradientStorageBytes);
        GradientStops = new BudgetedParserList<SvgGradientStop>(budget, SvgMemoryCategory.Parser, workCounter, cancellationToken);
        PendingUses = new BudgetedParserList<PendingUse>(budget, SvgMemoryCategory.Parser, workCounter, cancellationToken);
        PendingTextRuns = new(budget, SvgMemoryCategory.Parser, workCounter, cancellationToken);
        DeferredPaintLeaves = new(budget, SvgMemoryCategory.Parser, workCounter, cancellationToken);
        DeferredResourceUses = new(budget, SvgMemoryCategory.Parser, workCounter, cancellationToken);
        MaskReplayChildren = new(budget, SvgMemoryCategory.Parser, workCounter, cancellationToken);
        DeferredMaskContents = new(budget, SvgMemoryCategory.Parser, workCounter, cancellationToken);
        PatternUses = new(budget, SvgMemoryCategory.Parser, workCounter, cancellationToken);
        DeferredPatternContents = new(budget, SvgMemoryCategory.Parser, workCounter, cancellationToken);
        GroupSnapshots = new BudgetedParserDictionary<string, SvgGroupSnapshot>(budget, SvgMemoryCategory.Parser, DefinitionStorageBytes);
        RenderedGroupDefinitions = new(budget, SvgMemoryCategory.Parser, DefinitionStorageBytes);
        GroupSnapshotStarts = new BudgetedParserList<SvgGroupSnapshotStart>(budget, SvgMemoryCategory.Parser, workCounter, cancellationToken);
        PendingLeafClips = new BudgetedParserList<PendingLeafClip>(budget, SvgMemoryCategory.Parser, workCounter, cancellationToken);
        PendingGroupClips = new BudgetedParserList<PendingGroupClip>(budget, SvgMemoryCategory.Parser, workCounter, cancellationToken);
        LeafMaskOwners = new(budget, SvgMemoryCategory.Parser, workCounter, cancellationToken);
        PendingLeafMasks = new BudgetedParserList<PendingLeafMask>(budget, SvgMemoryCategory.Parser, workCounter, cancellationToken);
        PendingGroupMasks = new BudgetedParserList<PendingGroupMask>(budget, SvgMemoryCategory.Parser, workCounter, cancellationToken);
        PendingLinearGradientHrefs = new BudgetedParserList<PendingLinearGradientHref>(budget, SvgMemoryCategory.Parser, workCounter, cancellationToken);
        PendingRadialGradientHrefs = new BudgetedParserList<PendingRadialGradientHref>(budget, SvgMemoryCategory.Parser, workCounter, cancellationToken);
        MaskScopes = new BudgetedParserList<SvgClipBox>(budget, SvgMemoryCategory.Parser, workCounter, cancellationToken);
        MaskScopeSnapshots = new BudgetedParserList<SvgClipBox>(budget, SvgMemoryCategory.Parser, workCounter, cancellationToken);
    }

    internal RenderBudget Budget { get; }

    private readonly SvgSceneBuilder _documentScene;
    internal SvgSceneBuilder Scene => DefinitionReplayScene ?? _documentScene;

    /// <summary>Borrows an existing definition builder during synchronous lowering and deferred binding.</summary>
    /// <remarks>Callers restore it in finally; the conversion owns every borrowed builder. Resources remain
    /// document-owned, and the definition's local coordinates exclude enclosing rendered viewports.</remarks>
    internal SvgSceneBuilder? DefinitionReplayScene { get; set; }
    internal SvgTextReplayEnvironment? TextReplay { get; set; }
    internal bool DirectTextReplay { get; set; }
    internal SvgFilterArena DocumentFilters => _documentScene.Filters;

    internal SvgFilterDefinitionBuilder? FilterDefinition { get; set; }
    internal int FilterDepth { get; set; } = -1;

    internal BudgetedParserList<SvgDeferredPaintLeaf> DeferredPaintLeaves { get; }
    internal BudgetedParserList<SvgDeferredResourceUse> DeferredResourceUses { get; }
    internal BudgetedParserList<SvgMaskReplayChild> MaskReplayChildren { get; }
    internal BudgetedParserList<SvgDeferredMaskContent> DeferredMaskContents { get; }
    internal BudgetedParserList<SvgPatternContentUse> PatternUses { get; }
    internal BudgetedParserList<SvgDeferredPatternContent> DeferredPatternContents { get; }
    internal int MaskReplayChildStart { get; set; }
    internal int MaskReplayChildCount { get; set; }
    internal int MaskGroupChildIndex { get; set; } = -1;
    internal bool MaskHasReplayContent { get; set; }
    internal SvgResourceUse? MaskUse { get; set; }
    internal SvgResourceUse? ClipUse { get; set; }

    internal BudgetedParserStringStore Strings { get; }

    internal BudgetedParserList<SvgGroupScope> Groups { get; }

    /// <summary>Gets the open switch containers with their first-child consumption state.</summary>
    internal BudgetedParserList<SwitchScope> Switches { get; }

    /// <summary>Gets or sets the depth of the skipped switch subtree root, or -1 when nothing is skipped.</summary>
    internal int SkipDepth { get; set; } = -1;

    /// <summary>Gets or sets the root and output magnification applied after lowering, which scales flattening tolerance.</summary>
    internal SvgAffineTransform OutputMap { get; set; } = SvgAffineTransform.Identity;
    internal double OutputMagnification { get; set; } = 1.0;

    /// <summary>Logical picture font scale before the caller's raster-resolution transform.</summary>
    internal double OutputTextScale { get; set; } = 1.0;

    internal BudgetedParserList<SvgViewportFrame> Viewports { get; }

    /// <summary>Outer viewport clips apply after filter evaluation; inner source viewports retain their own clips.</summary>
    internal int FilterViewportDepth { get; set; } = -1;
    internal SvgViewportFrame? EffectiveFrame => DefinitionReplayScene is not null || Viewports.Count == 0 ? null
        : Viewports[^1].Depth < FilterViewportDepth ? Viewports[^1] with { Clip = SvgClipBox.Unbounded } : Viewports[^1];

    internal BudgetedParserList<CssRule> StyleRules { get; }

    internal SvgCssAncestorContext CssAncestors { get; }

    internal BudgetedParserDictionary<string, SvgDefinition> Definitions { get; }

    /// <summary>Holds definition-array reservations until teardown releases them on success, failure or cancellation.</summary>
    internal List<RenderBudget.BudgetReservation> DefinitionReservations { get; } = new();

    internal BudgetedParserDictionary<string, SvgClipBox> Clips { get; }

    internal BudgetedParserDictionary<string, SvgClipBox> ObjectClips { get; }

    internal BudgetedParserDictionary<string, SvgClipPolygon> ClipPolygons { get; }

    /// <summary>Owns original multi-child clip paths until registered mask scenes have copied their sources.</summary>
    internal BudgetedParserList<SvgClipChild>? ClipChildren { get; set; }

    internal BudgetedParserDictionary<string, byte> Masks { get; }

    internal BudgetedParserDictionary<string, SvgMaskContent> MaskContents { get; }

    internal SvgGeometry[]? MaskContentGeometries { get; set; }
    internal SvgSceneBuilder? MaskContentBuilder { get; set; }

    internal byte MaskContentLuminance { get; set; }

    internal bool MaskContentIsAlpha { get; set; }

    internal BudgetedParserDictionary<string, SvgClipBox> MaskRegions { get; }

    internal BudgetedParserDictionary<string, SvgClipBox> MaskFractionRegions { get; }

    internal BudgetedParserDictionary<string, SvgClipBox> MaskContentBoxes { get; }

    internal BudgetedParserDictionary<string, SvgPatternTile> Patterns { get; }
    internal BudgetedParserDictionary<string, SvgPatternDefinition> PatternDefinitions { get; }
    internal SvgPatternTemplate PatternTemplate { get; set; }
    internal bool HasPatternTemplateReferences { get; set; }

    internal BudgetedParserDictionary<string, SvgLinearGradient> Gradients { get; }

    internal BudgetedParserDictionary<string, SvgRadialGradient> RadialGradients { get; }

    internal BudgetedParserList<int> ClipDepths { get; }

    internal BudgetedParserList<PendingUse> PendingUses { get; }
    internal BudgetedParserList<SvgPendingTextRun> PendingTextRuns { get; }

    /// <summary>Gets closed source-group boundaries used to enforce instance composition gates.</summary>
    internal BudgetedParserDictionary<string, SvgGroupSnapshot> GroupSnapshots { get; }
    internal BudgetedParserDictionary<string, SvgDefinition> RenderedGroupDefinitions { get; }
    internal SvgSymbolContent? RenderedGroupContent { get; set; }
    internal int RenderedGroupRootDepth { get; set; } = -1;

    /// <summary>Gets open source-group boundaries, aligned with the group scope stack.</summary>
    internal BudgetedParserList<SvgGroupSnapshotStart> GroupSnapshotStarts { get; }

    internal BudgetedParserList<PendingLeafClip> PendingLeafClips { get; }



    internal BudgetedParserList<PendingGroupClip> PendingGroupClips { get; }

    internal BudgetedParserList<SvgLeafMaskOwner> LeafMaskOwners { get; }

    internal BudgetedParserList<PendingLeafMask> PendingLeafMasks { get; }

    internal BudgetedParserList<PendingGroupMask> PendingGroupMasks { get; }

    internal BudgetedParserList<PendingLinearGradientHref> PendingLinearGradientHrefs { get; }

    internal BudgetedParserList<PendingRadialGradientHref> PendingRadialGradientHrefs { get; }

    internal int GeometryElementCount { get; set; }

    internal bool DeferringLeafClip { get; set; }

    /// <summary>Defers the current filtered leaf's local clip to the filter output, keeping its source uncut.</summary>
    internal bool DeferringFilterClip { get; set; }
    internal bool DeferringFilterMask { get; set; }

    /// <summary>Gets or sets whether a gradient href drain is already running, guarding reentrant drains during nested registration.</summary>
    internal bool DrainingGradientHrefs { get; set; }

    /// <summary>Gets or sets whether missing gradient href targets throw at registration instead of deferring, used for the document-end leftover pass.</summary>
    internal bool ThrowOnMissingGradientHref { get; set; }


    internal int NodeCount { get; set; }

    internal int TextCharacterCount { get; set; }

    internal int GroupDepth { get; set; }
    internal int FilterGroupDepth { get; set; }


    internal bool HasUnsupportedContent { get; set; }
    internal SvgSourceDiagnostic? UnsupportedDiagnostic { get; set; }

    // Retain at most one already-created failure until earlier paint references resolve.
    // No text or glyph owners survive this failed run; document teardown releases prior scene owners.
    internal SvgException? DeferredTextFailure { get; set; }


    internal int DefinitionsDepth { get; set; } = -1;
    /// <summary>Borrows the current nonpainting definition container presentation until its matching end element.</summary>
    internal SvgInheritedContainer? DefinitionInheritance { get; set; }

    internal int SymbolDepth { get; set; } = -1;

    internal int DescriptionDepth { get; set; } = -1;

    internal int ClipPathDepth { get; set; } = -1;

    internal int MaskDepth { get; set; } = -1;


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

    internal int GradientDepth { get; set; } = -1;

    internal int RadialGradientDepth { get; set; } = -1;

    internal SvgPatternTile? PatternTile { get; set; }
    internal SvgSceneBuilder? PatternContentBuilder { get; set; }
    internal SvgDefinitionLeafSource? PatternRectangleSource { get; set; }
    internal SvgPaintableElementContext PatternRectangleContext { get; set; }
    internal int PatternUseStart { get; set; }
    internal int PatternUseCount { get; set; }
    internal int PatternScopeStart { get; set; }
    internal BudgetedParserList<SvgPatternContentScope>? PatternScopes { get; set; }

    internal string? MaskId { get; set; }

    internal bool MaskIsAlpha { get; set; }
    internal bool MaskObjectBoundingBoxContent { get; set; }
    internal SvgViewportLengthBases MaskReplayBases { get; set; }

    internal byte? MaskValue { get; set; }

    internal SvgClipBox? MaskCustomRegion { get; set; }
    internal bool MaskDefaultUserSpaceRegion { get; set; }

    internal SvgClipBox? MaskFractionRegion { get; set; }

    internal SvgClipBox? MaskContentBox { get; set; }

    internal string? ClipPathId { get; set; }

    internal SvgClipBox? ClipBox { get; set; }

    /// <summary>Retains allocation-free rectangle source placement until another clip child needs native union.</summary>
    internal SvgAffineViewportClip? ClipRectangleSource { get; set; }

    internal SvgClipPolygon? ClipPolygon { get; set; }

    internal bool ClipIsObjectBoundingBox { get; set; }
    internal bool ClipIsEvenOdd { get; set; }

    internal bool GroupUseExpanded { get; set; }

    internal SvgMarkerDefinition? MarkerDefinition { get; set; }
    internal string? SymbolId { get; set; }

    internal SvgViewBox? SymbolViewBox { get; set; }

    internal SvgSymbolDefinitionBuilder? SymbolDefinition { get; set; }

    /// <summary>Transfers an anonymous symbol's geometry only on success, keeping child-ID aliases alive through scene construction.
    /// Lazy storage and growth are budgeted; on failure the caller retains every geometry owner.</summary>
    internal void RetainAnonymousSymbol(SvgDefinition definition, ParserWorkCounter work, CancellationToken token)
    {
        if (_anonymousSymbols is null)
        {
            RenderBudget.BudgetReservation owner = Budget.Reserve(256, SvgMemoryCategory.Parser);
            try
            {
                _anonymousSymbols = new(Budget, SvgMemoryCategory.Parser, work, token);
                _anonymousSymbolOwner = owner;
            }
            catch { owner.Dispose(); throw; }
        }
        _anonymousSymbols.Add(definition);
    }

    private bool _definitionsReleased;

    /// <summary>Enables graph validation only when the document retains group/use source content.</summary>
    internal bool HasRetainedSymbolContent { get; set; }

    internal void ValidateSymbolReferences(ParserWorkCounter work, CancellationToken token)
    {
        if (!HasRetainedSymbolContent) return;
        using SvgSymbolReferenceValidator validator = SvgSymbolReferenceValidator.Create(this, work, token);
        foreach (SvgDefinition value in Definitions.Values) { work.Consume(token); validator.Validate(value); }
        foreach (SvgDefinition value in RenderedGroupDefinitions.Values) { work.Consume(token); validator.Validate(value); }
        if (_anonymousSymbols is not null)
            foreach (SvgDefinition value in _anonymousSymbols.AsSpan()) { work.Consume(token); validator.Validate(value); }
    }

    /// <summary>Releases definition-owned geometry reservations exactly once, covering success after scene build and failure or cancellation during teardown.</summary>
    internal void ReleaseDefinitions()
    {
        if (_definitionsReleased) return;
        _definitionsReleased = true;
        foreach (SvgDefinition value in Definitions.Values)
            if (!value.BorrowedFromSymbol)
            {
                foreach (SvgGeometry geometry in value.Geometries) geometry.Dispose();
                value.SymbolContent?.Content.Dispose();
            }
        if (_anonymousSymbols is not null)
            foreach (SvgDefinition value in _anonymousSymbols.AsSpan())
            {
                foreach (SvgGeometry geometry in value.Geometries) geometry.Dispose();
                value.SymbolContent?.Content.Dispose();
            }
    }

    internal void RetainStrokeContent(SvgSymbolContent content)
    {
        if (_strokeContents is null)
        {
            RenderBudget.BudgetReservation owner = Budget.Reserve(128, SvgMemoryCategory.Scene);
            try { _strokeContents = new(Budget, SvgMemoryCategory.Scene); _strokeContentOwner = owner; }
            catch { owner.Dispose(); throw; }
        }
        _strokeContents.Add(new(content, 0, 0));
    }

    public void Dispose()
    {
        FilterDefinition?.Dispose();
        SymbolDefinition?.Dispose();
        if (_strokeContents is not null) foreach (SvgSymbolContentRange content in _strokeContents.AsSpan()) content.Content.Dispose();
        _strokeContents?.Dispose();
        _strokeContentOwner?.Dispose();
        ReleaseDefinitions();
        _anonymousSymbols?.Dispose();
        _anonymousSymbolOwner?.Dispose();
        PendingUses.Dispose();
        foreach (SvgPendingTextRun pending in PendingTextRuns.AsSpan()) { pending.Run?.Dispose(); pending.Continuation?.Dispose(); pending.Owner.Dispose(); }
        PendingTextRuns.Dispose();
        DeferredPaintLeaves.Dispose();
        DeferredResourceUses.Dispose();
        MaskReplayChildren.Dispose();
        DeferredMaskContents.Dispose();
        GroupSnapshots.Dispose();
        RenderedGroupDefinitions.Dispose();
        GroupSnapshotStarts.Dispose();
        PendingLeafClips.Dispose();
        PendingGroupClips.Dispose();
        PendingLeafMasks.Dispose();
        foreach (SvgLeafMaskOwner owner in LeafMaskOwners.AsSpan()) owner.Owner.Dispose();
        LeafMaskOwners.Dispose();
        PendingGroupMasks.Dispose();
        PendingLinearGradientHrefs.Dispose();
        PendingRadialGradientHrefs.Dispose();
        Definitions.Dispose();
        foreach (RenderBudget.BudgetReservation reservation in DefinitionReservations) reservation.Dispose();
        DefinitionReservations.Clear();
        Clips.Dispose();
        ClipPolygon?.SourcePath?.Dispose();
        foreach (SvgClipPolygon clip in ClipPolygons.Values) clip.SourcePath?.Dispose();
        ClipPolygons.Dispose();
        if (ClipChildren is {} clipChildren)
            foreach (SvgClipChild child in clipChildren.AsSpan()) child.Path.Dispose();
        ClipChildren?.Dispose();
        ObjectClips.Dispose();
        Masks.Dispose();
        if (MaskContentGeometries is {} pendingMask) foreach (SvgGeometry geometry in pendingMask) geometry.Dispose();
        foreach (SvgMaskContent content in MaskContents.Values) foreach (SvgGeometry geometry in content.Geometries) geometry.Dispose();
        MaskContents.Dispose();
        MaskRegions.Dispose();
        MaskFractionRegions.Dispose();
        MaskContentBoxes.Dispose();
        MaskContentBuilder?.Dispose();
        PatternContentBuilder?.Dispose();
        foreach (SvgDeferredPatternContent content in DeferredPatternContents.AsSpan()) content.Builder.Dispose();
        DeferredPatternContents.Dispose();
        PatternScopes?.Dispose();
        PatternUses.Dispose();
        Patterns.Dispose();
        PatternDefinitions.Dispose();
        Gradients.Dispose();
        RadialGradients.Dispose();
        GradientStops.Dispose();
        MaskScopes.Dispose();
        MaskScopeSnapshots.Dispose();
        ClipDepths.Dispose();
        CssAncestors.Dispose();
        if (_ownsStyleStorage)
        {
            StyleRules.Dispose();
            Strings.Dispose();
        }
        Switches.Dispose();
        Groups.Dispose();
        Viewports.Dispose();
        Scene.Dispose();
    }
}

/// <summary>Represents one inherited group or link context while streaming the document.</summary>
internal readonly record struct SvgGroupScope(int Depth, SvgGroupKind Kind, SvgInheritedPresentation InheritedPresentation, SvgInheritedTextStyle InheritedTextStyle, bool IsolatesOpacity, bool HasMaskScope, SvgInheritedStrokeStyle InheritedStroke, string? FillRule)
{
    /// <summary>Retains an authored mask group's layer boundary before its deferred resource resolves; opacity inheritance stays independent.</summary>
    internal bool HasDeferredMask { get; init; }
    internal bool HasFilter { get; init; }
    internal bool HasFilterViewportScope { get; init; }
    internal int PreviousFilterViewportDepth { get; init; }
    internal SvgRecordingScope RecordingScope { get; init; }
    internal string? ClipRule { get; init; }
    /// <summary>Retains this container's computed local clip reference for explicitly inherited child declarations.</summary>
    internal string? ClipPathId { get; init; }
}

internal enum SvgGroupKind { Group, Link, Switch, Mask, Pattern, PatternGroup }

/// <summary>Retains closed source-group boundaries whose instance replay remains gated.</summary>
internal readonly record struct SvgGroupSnapshot(bool IsolatesOpacity, bool HasClip, bool HasMask, bool HasViewport);

/// <summary>Tracks one open group boundary, aligned with the group scope stack.</summary>
internal readonly record struct SvgGroupSnapshotStart(string? Id, bool HasClip, bool HasMask);

