using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgSymbolInstanceTests
{
    [Theory]
    [InlineData("<defs><symbol id='s'><circle id='a' r='3'/><path id='a' d='M0,0 Q4,0 4,4 Z'/></symbol></defs>")]
    [InlineData("<defs><symbol id='s'><circle id='s' r='3'/></symbol></defs>")]
    [InlineData("<defs><symbol id='s'/><symbol id='s'><circle r='3'/></symbol></defs>")]
    [InlineData("<defs><symbol id='s'><circle r='3'/></symbol><symbol id='s'/></defs>")]
    [InlineData("<defs><circle id='a' r='3'/><symbol id='s'><path id='a' d='M0,0 Q4,0 4,4 Z'/></symbol></defs>")]
    [InlineData("<defs><symbol id='s'><circle id='a' r='3'/></symbol><path id='a' d='M0,0 Q4,0 4,4 Z'/></defs>")]
    [InlineData("<g id='a'><circle r='3'/></g><defs><symbol id='s'><path id='a' d='M0,0 Q4,0 4,4 Z'/></symbol></defs>")]
    public void Duplicate_authored_ids_are_malformed_and_release_symbol_owners(string content)
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'>" + content + "</svg>");
        RenderBudget budget = new(1_000_000);
        SvgException exception = Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None));
        Assert.Equal(SvgErrorCode.MalformedInput, exception.ErrorCode);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Cancelled_symbol_assembly_preserves_children_for_retry_and_releases_owners()
    {
        RenderBudget budget = new(1_000_000);
        using (SvgDocumentState state = new(budget, CancellationToken.None))
        using (SvgSymbolDefinitionBuilder builder = SvgSymbolDefinitionBuilder.Create(budget, CancellationToken.None,
            new(SvgPaint.Default, SvgInheritedStrokeStyle.Default, null, false, null, null, SvgPaintOpacity.Opaque)))
        {
            for (int index = 0; index < 2; index++)
            {
                SvgPolygonFill fill = SvgPolygonFill.Create([new(0, 0), new(8, 0), new(0, 8)], [3], null, new(255, 0, 0, 255), true, false, SvgPolygonMapping.FiniteEdges, null, null, budget);
                try { builder.Add(new([SvgGeometry.FromPolygonFill(fill)], false, null, null) { SymbolFillBindings = [new(true, null, null)] }); }
                catch { fill.Dispose(); throw; }
            }
            long retained = budget.Snapshot().CurrentBytes;
            using CancellationTokenSource cancellation = new();
            int copied = 0;
            builder.AssemblyCheckpoint = count => { copied = count; cancellation.Cancel(); };
            OperationCanceledException cancelled = Assert.ThrowsAny<OperationCanceledException>(() => builder.Build(null, state, cancellation.Token));
            Assert.Equal(1, copied);
            Assert.Equal(cancellation.Token, cancelled.CancellationToken);
            Assert.Equal(retained, budget.Snapshot().CurrentBytes);
            builder.AssemblyCheckpoint = null;
            SvgDefinition definition = builder.Build(null, state, CancellationToken.None);
            try { Assert.Equal(2, definition.Geometries.Length); }
            finally { foreach (SvgGeometry geometry in definition.Geometries) geometry.Dispose(); }
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }
}
