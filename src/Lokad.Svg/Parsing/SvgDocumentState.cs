using Lokad.Svg.Rendering;

namespace Lokad.Svg.Parsing;

/// <summary>Owns the bounded mutable state for one SVG document while XML traversal remains streaming.</summary>
internal sealed class SvgDocumentState : IDisposable
{
    private const int GroupScopeStorageBytes = 64;
    private const int CssRuleStorageBytes = 96;
    private const int DefinitionStorageBytes = 160;
    private const int PendingUseStorageBytes = 64;

    internal SvgDocumentState(RenderBudget budget, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        Scene = new SvgSceneBuilder(budget, workCounter, cancellationToken);
        Groups = new BudgetedParserList<SvgGroupScope>(budget, SvgMemoryCategory.Parser, GroupScopeStorageBytes, workCounter, cancellationToken);
        StyleRules = new BudgetedParserList<CssRule>(budget, SvgMemoryCategory.Parser, CssRuleStorageBytes, workCounter, cancellationToken);
        Definitions = new BudgetedParserDictionary<string, SvgRectangle>(budget, SvgMemoryCategory.Parser, DefinitionStorageBytes);
        PendingUses = new BudgetedParserList<PendingUse>(budget, SvgMemoryCategory.Parser, PendingUseStorageBytes, workCounter, cancellationToken);
    }

    internal SvgSceneBuilder Scene { get; }

    internal BudgetedParserList<SvgGroupScope> Groups { get; }

    internal BudgetedParserList<CssRule> StyleRules { get; }

    internal BudgetedParserDictionary<string, SvgRectangle> Definitions { get; }

    internal BudgetedParserList<PendingUse> PendingUses { get; }

    internal int GeometryElementCount { get; set; }

    internal int NodeCount { get; set; }

    internal bool HasUnsupportedContent { get; set; }

    internal bool SawStyleableNode { get; set; }

    internal bool HasStyleSheet { get; set; }

    internal int DefinitionsDepth { get; set; } = -1;

    internal int SymbolDepth { get; set; } = -1;

    internal string? SymbolId { get; set; }

    public void Dispose()
    {
        PendingUses.Dispose();
        Definitions.Dispose();
        StyleRules.Dispose();
        Groups.Dispose();
        Scene.Dispose();
    }
}

/// <summary>Represents one inherited group or link context while streaming the document.</summary>
internal readonly record struct SvgGroupScope(int Depth, string Name, SvgPaint Paint, bool IsDisplayed, bool IsVisible, AxisTransform Transform, bool IsolatesOpacity);
