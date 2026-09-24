using Lokad.Svg.Rendering;

namespace Lokad.Svg.Parsing;

/// <summary>Owns the bounded mutable state for one SVG document while XML traversal remains streaming.</summary>
internal sealed class SvgDocumentState : IDisposable
{
    private const int DefinitionStorageBytes = 256;
    private const int ClipStorageBytes = 128;
    private const int PatternStorageBytes = 128;
    // Covers one gradient entry (128, as for patterns) plus one retained clamped stop array sized from its actual element type.
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
        ObjectClips = new BudgetedParserDictionary<string, SvgClipBox>(budget, SvgMemoryCategory.Parser, ClipStorageBytes);
        Masks = new BudgetedParserDictionary<string, byte>(budget, SvgMemoryCategory.Parser, ClipStorageBytes);
        MaskRegions = new BudgetedParserDictionary<string, SvgClipBox>(budget, SvgMemoryCategory.Parser, ClipStorageBytes);
        MaskContentBoxes = new BudgetedParserDictionary<string, SvgClipBox>(budget, SvgMemoryCategory.Parser, ClipStorageBytes);
        Patterns = new BudgetedParserDictionary<string, SvgPatternTile>(budget, SvgMemoryCategory.Parser, PatternStorageBytes);
        Gradients = new BudgetedParserDictionary<string, SvgLinearGradient>(budget, SvgMemoryCategory.Parser, GradientStorageBytes);
        RadialGradients = new BudgetedParserDictionary<string, SvgRadialGradient>(budget, SvgMemoryCategory.Parser, GradientStorageBytes);
        GradientStops = new BudgetedParserList<SvgGradientStop>(budget, SvgMemoryCategory.Parser, workCounter, cancellationToken);
        PendingUses = new BudgetedParserList<PendingUse>(budget, SvgMemoryCategory.Parser, workCounter, cancellationToken);
        MaskScopes = new BudgetedParserList<SvgClipBox>(budget, SvgMemoryCategory.Parser, workCounter, cancellationToken);
        MaskScopeSnapshots = new BudgetedParserList<SvgClipBox>(budget, SvgMemoryCategory.Parser, workCounter, cancellationToken);
    }

    internal RenderBudget Budget { get; }

    internal SvgSceneBuilder Scene { get; }

    internal BudgetedParserStringStore Strings { get; }

    internal BudgetedParserList<SvgGroupScope> Groups { get; }

    /// <summary>Gets the open switch containers with their first-child consumption state.</summary>
    internal BudgetedParserList<SwitchScope> Switches { get; }

    /// <summary>Gets or sets the depth of the skipped switch subtree root, or -1 when nothing is skipped.</summary>
    internal int SkipDepth { get; set; } = -1;

    internal BudgetedParserList<SvgViewportFrame> Viewports { get; }

    internal SvgViewportFrame? EffectiveFrame => Viewports.Count == 0 ? null : Viewports[^1];

    internal BudgetedParserList<CssRule> StyleRules { get; }

    internal BudgetedParserDictionary<string, SvgDefinition> Definitions { get; }

    internal BudgetedParserDictionary<string, SvgClipBox> Clips { get; }

    internal BudgetedParserDictionary<string, SvgClipBox> ObjectClips { get; }

    internal BudgetedParserDictionary<string, byte> Masks { get; }

    internal BudgetedParserDictionary<string, SvgClipBox> MaskRegions { get; }

    internal BudgetedParserDictionary<string, SvgClipBox> MaskContentBoxes { get; }

    internal BudgetedParserDictionary<string, SvgPatternTile> Patterns { get; }

    internal BudgetedParserDictionary<string, SvgLinearGradient> Gradients { get; }

    internal BudgetedParserDictionary<string, SvgRadialGradient> RadialGradients { get; }

    internal BudgetedParserList<int> ClipDepths { get; }

    internal BudgetedParserList<PendingUse> PendingUses { get; }

    internal int GeometryElementCount { get; set; }

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

    internal int GradientDepth { get; set; } = -1;

    internal int RadialGradientDepth { get; set; } = -1;

    internal SvgPatternTile? PatternTile { get; set; }

    internal string? MaskId { get; set; }

    internal bool MaskIsAlpha { get; set; }

    internal byte? MaskValue { get; set; }

    internal SvgClipBox? MaskCustomRegion { get; set; }

    internal SvgClipBox? MaskContentBox { get; set; }

    internal string? ClipPathId { get; set; }

    internal SvgClipBox? ClipBox { get; set; }

    internal bool ClipIsObjectBoundingBox { get; set; }

    internal string? SymbolId { get; set; }

    internal SvgViewBox? SymbolViewBox { get; set; }

    public void Dispose()
    {
        PendingUses.Dispose();
        Definitions.Dispose();
        Clips.Dispose();
        ObjectClips.Dispose();
        Masks.Dispose();
        MaskRegions.Dispose();
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
internal readonly record struct SvgGroupScope(int Depth, SvgGroupKind Kind, SvgInheritedPresentation InheritedPresentation, SvgInheritedTextStyle InheritedTextStyle, bool IsolatesOpacity, bool HasMaskScope);

internal enum SvgGroupKind { Group, Link, Switch }
