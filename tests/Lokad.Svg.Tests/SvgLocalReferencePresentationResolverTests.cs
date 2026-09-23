using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgLocalReferencePresentationResolverTests
{
    [Fact]
    public void Resolves_the_supported_use_instance_presentation_without_changing_inherited_paint()
    {
        IReadOnlyDictionary<string, SvgColor> namedColors = new Dictionary<string, SvgColor>(StringComparer.OrdinalIgnoreCase) { ["blue"] = new SvgColor(0, 0, 255, 255) };
        SvgInheritedPresentation inherited = new(new SvgPaint(new SvgColor(1, 2, 3, 255), true, 128, new SvgColor(4, 5, 6, 255), false), true, false, AxisTransform.Identity);
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

        SvgResolvedLocalReferencePresentation presentation = SvgLocalReferencePresentationResolver.Resolve(style, new SvgInheritedPresentation(SvgPaint.Default, true, true, AxisTransform.Identity), new Dictionary<string, SvgColor>(), new BudgetedParserStringStore(new RenderBudget(10_000)));

        Assert.Null(presentation.FillOverride);
        Assert.Equal("g", presentation.PaintServerId);
    }

    [Fact]
    public void Rejects_unsupported_use_instance_paint_properties()
    {
        SvgComputedStyle style = new(null, null, "0.5", null, null, null, null, null);

        SvgException exception = Assert.Throws<SvgException>(() => SvgLocalReferencePresentationResolver.Resolve(style, new SvgInheritedPresentation(SvgPaint.Default, true, true, AxisTransform.Identity), new Dictionary<string, SvgColor>(), new BudgetedParserStringStore(new RenderBudget(10_000))));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, exception.ErrorCode);
    }
}
