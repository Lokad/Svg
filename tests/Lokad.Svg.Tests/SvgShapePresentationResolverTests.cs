using Lokad.Svg.Parsing;

namespace Lokad.Svg.Tests;

public sealed class SvgShapePresentationResolverTests
{
    [Fact]
    public void Resolves_fill_and_stroke_paint_once_from_the_computed_style()
    {
        IReadOnlyDictionary<string, SvgColor> namedColors = new Dictionary<string, SvgColor>(StringComparer.OrdinalIgnoreCase)
        {
            ["blue"] = new SvgColor(0, 0, 255, 255)
        };
        SvgComputedStyle style = new("blue", "currentColor", "0.5", "currentColor", null, null, "0.5", null);

        SvgResolvedShapePresentation presentation = SvgShapePresentationResolver.Resolve(style, SvgPaint.Default, true, true, true, namedColors);

        Assert.Equal(new SvgColor(0, 0, 255, 64), presentation.Color);
        Assert.Equal(new SvgColor(0, 0, 255, 255), presentation.StrokeColor);
        Assert.Equal((byte)128, presentation.Opacity);
    }

    [Fact]
    public void Retains_absent_stroke_as_typed_no_stroke_presentation()
    {
        SvgResolvedShapePresentation presentation = SvgShapePresentationResolver.Resolve(default, SvgPaint.Default, true, true, true, new Dictionary<string, SvgColor>());

        Assert.Null(presentation.StrokeColor);
    }
}
