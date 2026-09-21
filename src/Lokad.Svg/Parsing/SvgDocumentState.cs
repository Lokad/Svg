using Lokad.Svg.Rendering;

namespace Lokad.Svg.Parsing;

/// <summary>Owns the bounded mutable state for one SVG document while XML traversal remains streaming.</summary>
internal sealed class SvgDocumentState : IDisposable
{
    private const int DefinitionStorageBytes = 256;

    internal SvgDocumentState(RenderBudget budget, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        Scene = new SvgSceneBuilder(budget, workCounter, cancellationToken);
        Strings = new BudgetedParserStringStore(budget);
        Groups = new BudgetedParserList<SvgGroupScope>(budget, SvgMemoryCategory.Parser, workCounter, cancellationToken);
        StyleRules = new BudgetedParserList<CssRule>(budget, SvgMemoryCategory.Parser, workCounter, cancellationToken);
        Definitions = new BudgetedParserDictionary<string, SvgDefinition>(budget, SvgMemoryCategory.Parser, DefinitionStorageBytes);
        PendingUses = new BudgetedParserList<PendingUse>(budget, SvgMemoryCategory.Parser, workCounter, cancellationToken);
    }

    internal SvgSceneBuilder Scene { get; }

    internal BudgetedParserStringStore Strings { get; }

    internal BudgetedParserList<SvgGroupScope> Groups { get; }

    internal BudgetedParserList<CssRule> StyleRules { get; }

    internal BudgetedParserDictionary<string, SvgDefinition> Definitions { get; }

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

    internal string? SymbolId { get; set; }

    public void Dispose()
    {
        PendingUses.Dispose();
        Definitions.Dispose();
        StyleRules.Dispose();
        Strings.Dispose();
        Groups.Dispose();
        Scene.Dispose();
    }
}

/// <summary>Represents one inherited group or link context while streaming the document.</summary>
internal readonly record struct SvgGroupScope(int Depth, SvgGroupKind Kind, SvgInheritedPresentation InheritedPresentation, SvgInheritedTextStyle InheritedTextStyle, bool IsolatesOpacity);

internal enum SvgGroupKind { Group, Link }
