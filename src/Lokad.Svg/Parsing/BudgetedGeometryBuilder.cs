using Lokad.Svg.Rendering;

namespace Lokad.Svg.Parsing;

/// <summary>Owns geometry values during lowering, including their paint/contour reservations.</summary>
/// <remarks>Single-threaded. Add adopts its input even on failure. Detach transfers every value and its array reservation on success; disposal releases all still-owned values. Work/cancellation and array accounting use the existing parser list.</remarks>
internal sealed class BudgetedGeometryBuilder : IDisposable
{
    private readonly BudgetedParserList<SvgGeometry> _items;

    internal BudgetedGeometryBuilder(RenderBudget budget, SvgMemoryCategory category, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        _items = new(budget, category, workCounter, cancellationToken);
    }

    internal int Count => _items.Count;

    internal SvgGeometry this[int index] => _items[index];

    internal void Add(SvgGeometry geometry)
    {
        try { _items.Add(geometry); }
        catch { geometry.Dispose(); throw; }
    }

    internal void AddRange(ReadOnlySpan<SvgGeometry> geometries)
    {
        try { _items.AddRange(geometries); }
        catch { foreach (SvgGeometry geometry in geometries) geometry.Dispose(); throw; }
    }

    internal ReadOnlySpan<SvgGeometry> AsSpan() => _items.AsSpan();

    internal OwnedParserArray<SvgGeometry> Detach() => _items.Detach();

    public void Dispose()
    {
        foreach (SvgGeometry geometry in _items.AsSpan()) geometry.Dispose();
        _items.Dispose();
    }
}
