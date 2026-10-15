using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgLocalReferencePresentationResolverTests
{
    [Fact]
    public void Resolves_the_supported_use_instance_presentation_without_changing_inherited_paint()
    {
        IReadOnlyDictionary<string, SvgColor> namedColors = new Dictionary<string, SvgColor>(StringComparer.OrdinalIgnoreCase) { ["blue"] = new SvgColor(0, 0, 255, 255) };
        SvgInheritedPresentation inherited = new(new SvgPaint(new SvgColor(1, 2, 3, 255), true, new SvgPaintOpacity(128, 128 / 255f), new SvgColor(4, 5, 6, 255), false), true, false, SvgAffineTransform.Identity);
        SvgComputedStyle style = new(null, "blue", null, null, "none", "visible", "0.5", null);

        SvgResolvedLocalReferencePresentation presentation = SvgLocalReferencePresentationResolver.Resolve(style, inherited, namedColors, new BudgetedParserStringStore(new RenderBudget(10_000)));

        Assert.Equal((byte)128, presentation.Opacity);
        Assert.False(presentation.IsPainted);
        Assert.Equal(new SvgPaint(new SvgColor(0, 0, 255, 255), true, new SvgPaintOpacity(128, 128 / 255f), new SvgColor(4, 5, 6, 255), false), presentation.FillOverride);
    }

    [Fact]
    public void Retains_paint_server_fills_on_use_instances_without_resolving_paint()
    {
        SvgComputedStyle style = new(null, "url(#g)", null, null, null, null, null, null);

        SvgResolvedLocalReferencePresentation presentation = SvgLocalReferencePresentationResolver.Resolve(style, new SvgInheritedPresentation(SvgPaint.Default, true, true, SvgAffineTransform.Identity), new Dictionary<string, SvgColor>(), new BudgetedParserStringStore(new RenderBudget(10_000)));

        Assert.Null(presentation.FillOverride);
        Assert.Equal("g", presentation.PaintServerId);
    }

    [Fact]
    public void Resolves_use_color_and_fill_opacity_independently_of_stroke_opacity()
    {
        SvgComputedStyle style = new("#297fb3", "currentColor", ".375", "currentColor", null, null, null, null);
        SvgResolvedLocalReferencePresentation resolved = SvgLocalReferencePresentationResolver.Resolve(style, new(SvgPaint.Default, true, true, SvgAffineTransform.Identity), new Dictionary<string, SvgColor>(), new BudgetedParserStringStore(new RenderBudget(10_000)));
        Assert.Equal(new SvgColor(41, 127, 179, 255), resolved.Paint.CurrentColor);
        Assert.Equal(.375f, resolved.Paint.FillOpacity.Value);
        Assert.Equal(resolved.Paint, resolved.FillOverride);
        SvgUseStrokeOverride stroke = Assert.IsType<SvgUseStrokeOverride>(resolved.StrokeOverride);
        Assert.Equal(resolved.Paint.CurrentColor, stroke.Paint.CurrentColor);
        Assert.Equal(SvgPaintOpacity.Opaque, stroke.Paint.FillOpacity);
    }

    [Fact]
    public void Valid_instance_fill_rule_preserves_its_resolved_paint()
    {
        SvgComputedStyle style = new(null, null, null, null, null, null, null, "evenodd");
        RenderBudget budget = new(10_000);
        using BudgetedParserStringStore strings = new(budget);
        SvgInheritedPresentation inherited = new(SvgPaint.Default, true, true, SvgAffineTransform.Identity);
        Dictionary<string, SvgColor> colors = new();
        SvgResolvedLocalReferencePresentation expected = SvgLocalReferencePresentationResolver.Resolve(style with { FillRule = null }, inherited, colors, strings);
        SvgResolvedLocalReferencePresentation actual = SvgLocalReferencePresentationResolver.Resolve(style, inherited, colors, strings);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Resolves_solid_use_stroke_with_width_and_defaulted_mechanics()
    {
        SvgComputedStyle style = new(null, null, null, "#ff0000", null, null, null, null) { Width = "5.165", Linejoin = "round" };
        IReadOnlyDictionary<string, SvgColor> namedColors = new Dictionary<string, SvgColor>(StringComparer.OrdinalIgnoreCase);

        SvgResolvedLocalReferencePresentation presentation = SvgLocalReferencePresentationResolver.Resolve(style, new SvgInheritedPresentation(SvgPaint.Default, true, true, SvgAffineTransform.Identity), namedColors, new BudgetedParserStringStore(new RenderBudget(10_000)));

        Assert.Null(presentation.FillOverride);
        Assert.NotNull(presentation.StrokeOverride);
        Assert.Equal(new SvgColor(255, 0, 0, 255), presentation.StrokeOverride.Value.Paint.FillColor);
        Assert.Equal(5.165, presentation.StrokeOverride.Value.Width);
        Assert.Equal(SvgStrokeJoin.Round, presentation.StrokeOverride.Value.Join);
        Assert.Null(presentation.StrokeOverride.Value.MiterLimit);
    }

    [Fact]
    public void Retains_solid_stroke_paint_when_use_dash_mechanics_are_supplied()
    {
        SvgComputedStyle style = new(null, null, null, "#ff0000", null, null, null, null) { Dasharray = "4 4" };

        SvgResolvedLocalReferencePresentation resolved = SvgLocalReferencePresentationResolver.Resolve(style, new SvgInheritedPresentation(SvgPaint.Default, true, true, SvgAffineTransform.Identity), new Dictionary<string, SvgColor>(), new BudgetedParserStringStore(new RenderBudget(10_000)));
        Assert.Equal(new SvgColor(255, 0, 0, 255), Assert.IsType<SvgUseStrokeOverride>(resolved.StrokeOverride).Paint.FillColor);
    }

    [Fact]
    public void Defers_local_paint_server_use_strokes_to_retained_source_replay()
    {
        SvgComputedStyle painted = new(null, null, null, "url(#g)", null, null, null, null);
        RenderBudget budget = new(10_000);
        using BudgetedParserStringStore strings = new(budget);
        SvgResolvedLocalReferencePresentation resolved = SvgLocalReferencePresentationResolver.Resolve(painted,
            new(SvgPaint.Default, true, true, SvgAffineTransform.Identity), new Dictionary<string, SvgColor>(), strings);
        Assert.Null(resolved.StrokeOverride);
        Assert.False(resolved.AllowsSolidPaintAnalysis);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("#ff0000")]
    public void Resolves_use_stroke_opacity_without_requiring_a_stroke_color(string? strokeColor)
    {
        SvgComputedStyle translucent = new(null, null, ".625", strokeColor, null, null, null, null) { StrokeOpacity = ".375" };
        SvgResolvedLocalReferencePresentation resolved = SvgLocalReferencePresentationResolver.Resolve(translucent, new(SvgPaint.Default, true, true, SvgAffineTransform.Identity), new Dictionary<string, SvgColor>(), new BudgetedParserStringStore(new RenderBudget(10_000)));
        Assert.Equal(.375f, resolved.StrokeOpacity.Value);
        Assert.Equal(.625f, resolved.Paint.FillOpacity.Value);
        if (strokeColor is null) Assert.Null(resolved.StrokeOverride);
        else Assert.Equal(.375f, Assert.IsType<SvgUseStrokeOverride>(resolved.StrokeOverride).Paint.FillOpacity.Value);
    }
}
