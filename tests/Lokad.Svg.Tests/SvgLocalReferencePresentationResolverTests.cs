using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgLocalReferencePresentationResolverTests
{
    [Fact]
    public void Resolves_the_supported_use_instance_presentation_without_changing_inherited_paint()
    {
        IReadOnlyDictionary<string, SvgColor> namedColors = new Dictionary<string, SvgColor>(StringComparer.OrdinalIgnoreCase) { ["blue"] = new SvgColor(0, 0, 255, 255) };
        SvgInheritedPresentation inherited = new(new SvgPaint(new SvgColor(1, 2, 3, 255), true, 128, new SvgColor(4, 5, 6, 255), false), true, false, SvgAffineTransform.Identity);
        SvgComputedStyle style = new(null, "blue", null, null, "none", "visible", "0.5", null);

        SvgResolvedLocalReferencePresentation presentation = SvgLocalReferencePresentationResolver.Resolve(style, inherited, namedColors, new BudgetedParserStringStore(new RenderBudget(10_000)));

        Assert.Equal((byte)128, presentation.Opacity);
        Assert.False(presentation.IsPainted);
        Assert.Equal(new SvgPaint(new SvgColor(0, 0, 255, 255), true, 128, new SvgColor(4, 5, 6, 255), false), presentation.FillOverride);
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
    public void Rejects_unsupported_use_instance_paint_properties()
    {
        SvgComputedStyle style = new(null, null, "0.5", null, null, null, null, null);

        SvgException exception = Assert.Throws<SvgException>(() => SvgLocalReferencePresentationResolver.Resolve(style, new SvgInheritedPresentation(SvgPaint.Default, true, true, SvgAffineTransform.Identity), new Dictionary<string, SvgColor>(), new BudgetedParserStringStore(new RenderBudget(10_000))));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, exception.ErrorCode);
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
    public void Rejects_dashed_use_strokes_with_an_honest_message()
    {
        SvgComputedStyle style = new(null, null, null, "#ff0000", null, null, null, null) { Dasharray = "4 4" };

        SvgException exception = Assert.Throws<SvgException>(() => SvgLocalReferencePresentationResolver.Resolve(style, new SvgInheritedPresentation(SvgPaint.Default, true, true, SvgAffineTransform.Identity), new Dictionary<string, SvgColor>(), new BudgetedParserStringStore(new RenderBudget(10_000))));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, exception.ErrorCode);
        Assert.Contains("Dashed strokes on use elements", exception.Message);
    }

    [Fact]
    public void Rejects_paint_server_and_opacity_use_strokes()
    {
        SvgComputedStyle painted = new(null, null, null, "url(#g)", null, null, null, null);
        SvgException server = Assert.Throws<SvgException>(() => SvgLocalReferencePresentationResolver.Resolve(painted, new SvgInheritedPresentation(SvgPaint.Default, true, true, SvgAffineTransform.Identity), new Dictionary<string, SvgColor>(), new BudgetedParserStringStore(new RenderBudget(10_000))));
        Assert.Equal(SvgErrorCode.UnsupportedFeature, server.ErrorCode);

        SvgComputedStyle translucent = new(null, null, null, "#ff0000", null, null, null, null) { StrokeOpacity = "0.5" };
        SvgException opacity = Assert.Throws<SvgException>(() => SvgLocalReferencePresentationResolver.Resolve(translucent, new SvgInheritedPresentation(SvgPaint.Default, true, true, SvgAffineTransform.Identity), new Dictionary<string, SvgColor>(), new BudgetedParserStringStore(new RenderBudget(10_000))));
        Assert.Equal(SvgErrorCode.UnsupportedFeature, opacity.ErrorCode);
        Assert.Contains("Stroke opacity on use elements", opacity.Message);
    }
}
