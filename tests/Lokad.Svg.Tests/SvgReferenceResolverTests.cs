using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgReferenceResolverTests
{
    [Fact]
    public void Resolves_available_geometry_without_a_placeholder()
    {
        using SvgDocumentState state = new(new RenderBudget(10_000), new ParserWorkCounter(100), CancellationToken.None);
        SvgDefinition definition = CreateRectangleDefinition();
        Assert.True(state.Definitions.TryAdd("tile", definition));

        SvgReferenceResolver.ResolveOrDefer(CreateReference(), state.Definitions, state.PendingUses, state.Scene, null, null, null, null, state, new ParserWorkCounter(100), CancellationToken.None);

        using SvgSceneStorage scene = state.Scene.Build();
        Assert.Equal(0, state.PendingUses.Count);
        Assert.Equal(definition.Geometries, scene.Geometries);
    }

    [Fact]
    public void Resolves_forward_geometry_from_its_typed_placeholder()
    {
        using SvgDocumentState state = new(new RenderBudget(10_000), new ParserWorkCounter(100), CancellationToken.None);
        SvgDefinition definition = CreateRectangleDefinition();

        SvgReferenceResolver.ResolveOrDefer(CreateReference(), state.Definitions, state.PendingUses, state.Scene, null, null, null, null, state, new ParserWorkCounter(100), CancellationToken.None);
        Assert.True(state.Definitions.TryAdd("tile", definition));
        SvgReferenceResolver.ResolvePendingUses(state.Definitions, state.PendingUses, state.Scene, state, new ParserWorkCounter(100), CancellationToken.None);

        using SvgSceneStorage scene = state.Scene.Build();
        Assert.Equal(1, state.PendingUses.Count);
        Assert.Equal(definition.Geometries, scene.Geometries);
    }

    [Fact]
    public void Applies_integral_and_rejects_fractional_line_use_offsets()
    {
        using SvgDocumentState state = new(new RenderBudget(10_000), new ParserWorkCounter(100), CancellationToken.None);
        SvgDefinition definition = new([SvgGeometry.FromAxisAlignedLine(new SvgAxisAlignedLine(0, 0, 2, 0, new SvgColor(255, 0, 0, 255), true, 2))], false, null);

        SvgGeometry applied = Assert.Single(SvgReferenceResolver.Apply(definition, new SvgUseReference("tile", 1, 0, 0, 0, AxisTransform.Identity, AxisTransform.Identity, 255, true, null, null), null, state));
        SvgException exception = Assert.Throws<SvgException>(() => SvgReferenceResolver.Apply(definition, new SvgUseReference("tile", 1.5, 0, 0, 0, AxisTransform.Identity, AxisTransform.Identity, 255, true, null, null), null, state));

        Assert.Equal(new SvgAxisAlignedLine(1, 0, 3, 0, new SvgColor(255, 0, 0, 255), true, 2), applied.AxisAlignedLine);
        Assert.Equal(SvgErrorCode.UnsupportedFeature, exception.ErrorCode);
    }

    private static SvgDefinition CreateRectangleDefinition()
        => new([SvgGeometry.FromRectangle(new SvgRectangle(1, 2, 3, 4, new SvgColor(5, 6, 7, 255), true, null))], false, null);

    private static SvgUseReference CreateReference()
        => new("tile", 0, 0, 0, 0, AxisTransform.Identity, AxisTransform.Identity, 255, true, null, null);
}
