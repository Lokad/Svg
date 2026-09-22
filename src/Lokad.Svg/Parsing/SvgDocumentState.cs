using Lokad.Svg.Rendering;

namespace Lokad.Svg.Parsing;

/// <summary>Owns the bounded mutable state for one SVG document while XML traversal remains streaming.</summary>
internal sealed class SvgDocumentState : IDisposable
{
    private const int DefinitionStorageBytes = 256;
    private const int ClipStorageBytes = 128;

    internal SvgDocumentState(RenderBudget budget, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        Scene = new SvgSceneBuilder(budget, workCounter, cancellationToken);
        Strings = new BudgetedParserStringStore(budget);
        Groups = new BudgetedParserList<SvgGroupScope>(budget, SvgMemoryCategory.Parser, workCounter, cancellationToken);
        ClipDepths = new BudgetedParserList<int>(budget, SvgMemoryCategory.Parser, workCounter, cancellationToken);
        Viewports = new BudgetedParserList<SvgViewportFrame>(budget, SvgMemoryCategory.Parser, workCounter, cancellationToken);
        StyleRules = new BudgetedParserList<CssRule>(budget, SvgMemoryCategory.Parser, workCounter, cancellationToken);
        Definitions = new BudgetedParserDictionary<string, SvgDefinition>(budget, SvgMemoryCategory.Parser, DefinitionStorageBytes);
        Clips = new BudgetedParserDictionary<string, SvgClipBox>(budget, SvgMemoryCategory.Parser, ClipStorageBytes);
        Masks = new BudgetedParserDictionary<string, byte>(budget, SvgMemoryCategory.Parser, ClipStorageBytes);
        PendingUses = new BudgetedParserList<PendingUse>(budget, SvgMemoryCategory.Parser, workCounter, cancellationToken);
    }

    internal SvgSceneBuilder Scene { get; }

    internal BudgetedParserStringStore Strings { get; }

    internal BudgetedParserList<SvgGroupScope> Groups { get; }

    internal BudgetedParserList<SvgViewportFrame> Viewports { get; }

    internal SvgViewportFrame? EffectiveFrame => Viewports.Count == 0 ? null : Viewports[^1];

    internal BudgetedParserList<CssRule> StyleRules { get; }

    internal BudgetedParserDictionary<string, SvgDefinition> Definitions { get; }

    internal BudgetedParserDictionary<string, SvgClipBox> Clips { get; }

    internal BudgetedParserDictionary<string, byte> Masks { get; }

    internal BudgetedParserList<int> ClipDepths { get; }

    internal BudgetedParserList<PendingUse> PendingUses { get; }

    internal int GeometryElementCount { get; set; }

    internal int NodeCount { get; set; }

    internal int TextCharacterCount { get; set; }

    internal int GroupDepth { get; set; }

    internal bool HasOpacityScope { get; set; }

    internal bool HasUnsupportedContent { get; set; }

    internal bool SawStyleableNode { get; set; }

    internal bool HasStyleSheet { get; set; }

    internal int DefinitionsDepth { get; set; } = -1;

    internal int SymbolDepth { get; set; } = -1;

    internal int ClipPathDepth { get; set; } = -1;

    internal int MaskDepth { get; set; } = -1;

    internal string? MaskId { get; set; }

    internal bool MaskIsAlpha { get; set; }

    internal byte? MaskValue { get; set; }

    internal string? ClipPathId { get; set; }

    internal SvgClipBox? ClipBox { get; set; }

    internal string? SymbolId { get; set; }

    internal SvgViewBox? SymbolViewBox { get; set; }

    public void Dispose()
    {
        PendingUses.Dispose();
        Definitions.Dispose();
        Clips.Dispose();
        Masks.Dispose();
        ClipDepths.Dispose();
        StyleRules.Dispose();
        Strings.Dispose();
        Groups.Dispose();
        Viewports.Dispose();
        Scene.Dispose();
    }
}

/// <summary>Represents one inherited group or link context while streaming the document.</summary>
internal readonly record struct SvgGroupScope(int Depth, SvgGroupKind Kind, SvgInheritedPresentation InheritedPresentation, SvgInheritedTextStyle InheritedTextStyle, bool IsolatesOpacity);

internal enum SvgGroupKind { Group, Link }
