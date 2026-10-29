using System.Xml;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Parsing;

/// <summary>Retains only the current XML branch and reusable selector scratch under the parser budget.</summary>
internal sealed class SvgCssAncestorContext : IDisposable
{
    private readonly BudgetedParserList<SvgCssElement> _elements;
    private readonly BudgetedParserList<byte> _matches;
    private readonly bool _enabled;
    private int _currentDepth;

    internal SvgCssAncestorContext(BudgetedParserList<CssRule> rules, RenderBudget budget, CancellationToken token)
    {
        _elements = new(budget, SvgMemoryCategory.Parser, token);
        _matches = new(budget, SvgMemoryCategory.Parser, token);
        for (int index = 0; index < rules.Count; index++)
        {
            if (rules[index].HasCombinator) { _enabled = true; break; }
        }
    }

    internal ReadOnlySpan<SvgCssElement> Ancestors => _elements.AsSpan()[..Math.Min(_currentDepth, _elements.Count)];

    /// <summary>Observes ordinary or subtree reader positions using an explicit absolute depth offset.
    /// The existing input-sized XML allowance covers borrowed immutable strings; only active ancestors survive.</summary>
    internal void Observe(XmlReader reader, int depthOffset)
    {
        if (!_enabled) return;
        if (reader.NodeType is not (XmlNodeType.Element or XmlNodeType.EndElement)) return;
        int depth = checked(reader.Depth + depthOffset);
        while (_elements.Count > depth) _elements.RemoveLast();
        _currentDepth = depth;
        if (reader.NodeType == XmlNodeType.Element)
        {
            if (_elements.Count != depth) throw new InvalidOperationException("CSS ancestor traversal omitted an open element.");
            _elements.Add(SvgCssElement.Read(reader));
        }
    }

    internal Span<byte> MatchRows(int rowLength)
    {
        int required = checked(rowLength * 2);
        while (_matches.Count < required) _matches.Add(0);
        Span<byte> rows = _matches.AsMutableSpan()[..required];
        rows.Clear();
        return rows;
    }

    public void Dispose()
    {
        _matches.Dispose();
        _elements.Dispose();
    }
}

/// <summary>Borrowed immutable names/attributes of one open XML element; no reader or DOM is retained.</summary>
internal readonly record struct SvgCssElement(string Name, string? Id, string? Classes, bool IsSvg)
{
    internal static SvgCssElement Read(XmlReader reader)
        => new(reader.LocalName, reader.GetAttribute("id"), reader.GetAttribute("class"), reader.NamespaceURI == "http://www.w3.org/2000/svg");
}
