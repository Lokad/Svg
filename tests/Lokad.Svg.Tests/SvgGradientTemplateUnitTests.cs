using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgGradientTemplateUnitTests
{
    private const string Stops = "<stop stop-color='red'/><stop offset='1' stop-color='blue'/>";
    private const string RadialMiddle = "<radialGradient id='middle' href='#base'/>";

    [Theory]
    [Trait("Layer", "Mechanism")]
    [InlineData("x1", false)]
    [InlineData("y1", false)]
    [InlineData("x2", false)]
    [InlineData("y2", false)]
    [InlineData("x1", true)]
    [InlineData("y1", true)]
    [InlineData("x2", true)]
    [InlineData("y2", true)]
    public void Inherited_user_space_resolves_percentages_on_each_linear_axis(string axis, bool complete)
    {
        foreach (var item in LinearCases(axis, complete))
        {
            using SvgRoot root = SvgRootParser.Parse(Document(item.Definitions), SvgRenderLimits.Default, CancellationToken.None);
            SvgLinearPaintMap source = Assert.IsType<SvgLinearPaintMap>(Assert.Single(root.Geometries).Gradient.Gradient.SourcePaint);
            Assert.Equal((axis == "x1" ? 16 : 0, axis == "y1" ? 16 : 0, axis == "x2" ? 16 : 32, axis == "y2" ? 16 : 0),
                (source.Data.X1, source.Data.Y1, source.Data.X2, source.Data.Y2));
        }
    }

    [Theory]
    [Trait("Layer", "Rendering")]
    [InlineData("x1", false)]
    [InlineData("y1", false)]
    [InlineData("x2", false)]
    [InlineData("y2", false)]
    [InlineData("x1", true)]
    [InlineData("y1", true)]
    [InlineData("x2", true)]
    [InlineData("y2", true)]
    public void Numeric_and_object_box_controls_preserve_each_linear_axis(string axis, bool complete)
    {
        foreach (var item in LinearCases(axis, complete))
        {
            SvgRasterTestHelper.AssertPixelsEqual(
                Document("<linearGradient id='g' gradientUnits='userSpaceOnUse' " + item.ExplicitCoordinates + ">" + Stops + "</linearGradient>"),
                Document(item.Definitions.Replace("50%", "16", StringComparison.Ordinal)), SvgRenderOptions.Default);
            string objectBox = item.Definitions.Replace("userSpaceOnUse", "objectBoundingBox", StringComparison.Ordinal);
            SvgRasterTestHelper.AssertPixelsEqual(Document(objectBox.Replace("50%", ".5", StringComparison.Ordinal)), Document(objectBox), SvgRenderOptions.Default);
        }
    }

    private static IEnumerable<(string Definitions, string ExplicitCoordinates)> LinearCases(string axis, bool complete)
    {
        foreach (bool forward in new[] { false, true })
        foreach (bool middle in new[] { false, true })
        foreach (bool ownStops in new[] { false, true })
        {
            const string coordinates = "x1='0' y1='0' x2='32' y2='0'";
            string parent = "<linearGradient id='base' gradientUnits='userSpaceOnUse' " + coordinates + ">" + Stops + "</linearGradient>";
            string intermediary = middle ? "<linearGradient id='middle' href='#base'/>" : string.Empty;
            string attribute = axis + "='" + (axis == "x2" ? "32" : "0") + "'";
            string replacement = complete ? coordinates.Replace(attribute, axis + "='50%'", StringComparison.Ordinal) : axis + "='50%'";
            string child = "<linearGradient id='g' href='#" + (middle ? "middle" : "base") + "' " + replacement + ">" + (ownStops ? Stops : string.Empty) + "</linearGradient>";
            string definitions = forward ? child + intermediary + parent : parent + intermediary + child;
            string explicitCoordinates = coordinates.Replace(attribute, axis + "='16'", StringComparison.Ordinal);
            yield return (definitions, explicitCoordinates);
        }
    }

    [Theory]
    [Trait("Layer", "Mechanism")]
    [InlineData("cx")]
    [InlineData("cy")]
    [InlineData("r")]
    [InlineData("fx")]
    [InlineData("fy")]
    [InlineData("fr")]
    public void Inherited_user_space_resolves_radial_lengths_and_still_rejects_invalid_focal_circles(string axis)
    {
        foreach (bool forward in new[] { false, true })
        foreach (bool ownStops in new[] { false, true })
        {
            const string coordinates = "cx='16' cy='16' r='8'";
            string parent = "<radialGradient id='base' gradientUnits='userSpaceOnUse' " + coordinates + ">" + Stops + "</radialGradient>";
            string attribute = axis + "='" + (axis == "r" ? "8" : "16") + "'";
            string replacement = axis is "cx" or "cy" or "r" ? coordinates.Replace(attribute, axis + "='50%'", StringComparison.Ordinal) : coordinates + " " + axis + "='50%'";
            string child = "<radialGradient id='g' href='#base' " + replacement + ">" + (ownStops ? Stops : string.Empty) + "</radialGradient>";
            byte[] input = Document(forward ? child + parent : parent + child);
            if (axis == "fr") { AssertInvalidCircle(input); continue; }
            using SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, CancellationToken.None);
            SvgRadialGradientSource source = Assert.IsType<SvgRadialGradientSource>(Assert.Single(root.Geometries).Radial.Gradient.SourceDefinition);
            Assert.Equal((16, 16, axis == "r" ? 16 : 8, 16, 16, 0),
                (source.CenterX, source.CenterY, source.Radius, source.FocalX, source.FocalY, source.FocalRadius));
        }
    }

    [Theory]
    [Trait("Layer", "Mechanism")]
    [InlineData("linearGradient", "x1='0' y1='0' x2='50%' y2='0'")]
    [InlineData("radialGradient", "cx='.5' cy='.5' r='50%'")]
    public void Complete_vectors_resolve_percentages_through_cross_kind_templates(string kind, string coordinates)
    {
        string parentKind = kind == "linearGradient" ? "radialGradient" : "linearGradient";
        string parentCoordinates = parentKind == "linearGradient" ? "x1='0' y1='0' x2='32' y2='0'" : "cx='16' cy='16' r='8'";
        string parent = "<" + parentKind + " id='base' gradientUnits='userSpaceOnUse' " + parentCoordinates + ">" + Stops + "</" + parentKind + ">";
        string child = "<" + kind + " id='g' href='#base' " + coordinates + "/>";
        using SvgRoot root = SvgRootParser.Parse(Document(child + parent), SvgRenderLimits.Default, CancellationToken.None);
        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(16, kind == "linearGradient" ? geometry.Gradient.Gradient.X2 : geometry.Radial.Gradient.Radius);
    }

    [Theory]
    [Trait("Layer", "Mechanism")]
    [InlineData("fx", "40%", ".4")]
    [InlineData("fy", "45%", ".45")]
    [InlineData("fr", "5%", ".05")]
    public void Inherited_focal_percentages_still_reject_an_invalid_replacement_circle(string axis, string percentage, string number)
    {
        foreach (var item in FocalCases(axis, percentage, number))
            AssertInvalidCircle(Document(item.Forward ? item.Child + RadialMiddle + item.Parent : item.Parent + RadialMiddle + item.Child));
    }

    [Theory]
    [Trait("Layer", "Rendering")]
    [InlineData("fx", "40%", ".4")]
    [InlineData("fy", "45%", ".45")]
    [InlineData("fr", "5%", ".05")]
    public void Numeric_focal_controls_preserve_replaced_vectors(string axis, string percentage, string number)
    {
        foreach (var item in FocalCases(axis, percentage, number))
        {
            string numericParent = item.Parent.Replace(percentage, number, StringComparison.Ordinal);
            string replacement = item.Child.Replace("id='g'", "id='g' " + axis + "='" + number + "'", StringComparison.Ordinal);
            SvgRasterTestHelper.AssertPixelsEqual(Document(numericParent + RadialMiddle + item.Child),
                Document(item.Forward ? replacement + RadialMiddle + item.Parent : item.Parent + RadialMiddle + replacement), SvgRenderOptions.Default);
        }
    }

    private static IEnumerable<(string Parent, string Child, bool Forward)> FocalCases(string axis, string percentage, string number)
    {
        foreach (bool forward in new[] { false, true })
        foreach (bool ownStops in new[] { false, true })
        {
            const string coordinates = "cx='50%' cy='50%' r='45%' fx='.4' fy='.45' fr='.05'";
            string parent = "<radialGradient id='base' " + coordinates.Replace(axis + "='" + number + "'", axis + "='" + percentage + "'", StringComparison.Ordinal) + ">" + Stops + "</radialGradient>";
            string child = "<radialGradient id='g' href='#middle' gradientUnits='userSpaceOnUse' cx='.5' cy='.5' r='.45' gradientTransform='translate(0 0)' spreadMethod='pad'>" + (ownStops ? Stops : string.Empty) + "</radialGradient>";
            yield return (parent, child, forward);
        }
    }

    [Fact]
    [Trait("Layer", "Contract")]
    public void Malformed_lengths_leave_streams_empty_and_allow_a_percentage_retry()
    {
        const string definitions = "<linearGradient id='g' href='#base' x2='50oops%'/><linearGradient id='base' gradientUnits='userSpaceOnUse' x1='0' y1='0' x2='32' y2='0'><stop stop-color='red'/><stop offset='1' stop-color='blue'/></linearGradient>";
        byte[] input = Document(definitions);
        using MemoryStream output = new();
        Assert.Equal(SvgErrorCode.UnsupportedFeature, Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(input, output, SvgRenderOptions.Default, CancellationToken.None)).ErrorCode);
        Assert.Equal(0, output.Length);
        Assert.NotEmpty(Render(Document(definitions.Replace("50oops%", "50%", StringComparison.Ordinal))));

        static byte[] Render(byte[] input)
            => SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None).ToArray();
    }

    private static void AssertInvalidCircle(byte[] input)
    {
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(268_435_456L));
        SvgException error = Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None));
        Assert.Equal(SvgErrorCode.UnsupportedFeature, error.ErrorCode);
        Assert.Contains("Offset radial foci", error.Message, StringComparison.Ordinal);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    private static byte[] Document(string definitions)
        => System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'><defs>" + definitions + "</defs><rect width='32' height='32' fill='url(#g)'/></svg>");

}
