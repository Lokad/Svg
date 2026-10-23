using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgGradientTemplateUnitTests
{
    private const string Stops = "<stop stop-color='red'/><stop offset='1' stop-color='blue'/>";

    [Theory]
    [InlineData("x1", false)]
    [InlineData("y1", false)]
    [InlineData("x2", false)]
    [InlineData("y2", false)]
    [InlineData("x1", true)]
    [InlineData("y1", true)]
    [InlineData("x2", true)]
    [InlineData("y2", true)]
    public void Inherited_user_space_rejects_percentages_on_each_linear_axis(string axis, bool complete)
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
            AssertPercentageRejected(Document(definitions));
            string explicitCoordinates = coordinates.Replace(attribute, axis + "='16'", StringComparison.Ordinal);
            Assert.Equal(Render(Document("<linearGradient id='g' gradientUnits='userSpaceOnUse' " + explicitCoordinates + ">" + Stops + "</linearGradient>")), Render(Document(definitions.Replace("50%", "16", StringComparison.Ordinal))));
            string objectBox = definitions.Replace("userSpaceOnUse", "objectBoundingBox", StringComparison.Ordinal);
            Assert.Equal(Render(Document(objectBox.Replace("50%", ".5", StringComparison.Ordinal))), Render(Document(objectBox)));
        }
    }

    [Theory]
    [InlineData("cx")]
    [InlineData("cy")]
    [InlineData("r")]
    [InlineData("fx")]
    [InlineData("fy")]
    [InlineData("fr")]
    public void Inherited_user_space_rejects_radial_center_radius_and_focal_percentages(string axis)
    {
        foreach (bool forward in new[] { false, true })
        foreach (bool ownStops in new[] { false, true })
        {
            const string coordinates = "cx='16' cy='16' r='8'";
            string parent = "<radialGradient id='base' gradientUnits='userSpaceOnUse' " + coordinates + ">" + Stops + "</radialGradient>";
            string attribute = axis + "='" + (axis == "r" ? "8" : "16") + "'";
            string replacement = axis is "cx" or "cy" or "r" ? coordinates.Replace(attribute, axis + "='50%'", StringComparison.Ordinal) : coordinates + " " + axis + "='50%'";
            string child = "<radialGradient id='g' href='#base' " + replacement + ">" + (ownStops ? Stops : string.Empty) + "</radialGradient>";
            AssertPercentageRejected(Document(forward ? child + parent : parent + child));
        }
    }

    [Theory]
    [InlineData("linearGradient", "x1='0' y1='0' x2='50%' y2='0'")]
    [InlineData("radialGradient", "cx='.5' cy='.5' r='50%'")]
    public void Complete_vectors_keep_the_boundary_through_cross_kind_templates(string kind, string coordinates)
    {
        string parentKind = kind == "linearGradient" ? "radialGradient" : "linearGradient";
        string parentCoordinates = parentKind == "linearGradient" ? "x1='0' y1='0' x2='32' y2='0'" : "cx='16' cy='16' r='8'";
        string parent = "<" + parentKind + " id='base' gradientUnits='userSpaceOnUse' " + parentCoordinates + ">" + Stops + "</" + parentKind + ">";
        string child = "<" + kind + " id='g' href='#base' " + coordinates + "/>";
        AssertPercentageRejected(Document(child + parent));
    }

    [Theory]
    [InlineData("fx", "40%", ".4")]
    [InlineData("fy", "45%", ".45")]
    [InlineData("fr", "5%", ".05")]
    public void Inherited_focal_percentages_keep_their_units_when_the_vector_is_replaced(string axis, string percentage, string number)
    {
        foreach (bool forward in new[] { false, true })
        foreach (bool ownStops in new[] { false, true })
        {
            const string coordinates = "cx='50%' cy='50%' r='45%' fx='.4' fy='.45' fr='.05'";
            string parent = "<radialGradient id='base' " + coordinates.Replace(axis + "='" + number + "'", axis + "='" + percentage + "'", StringComparison.Ordinal) + ">" + Stops + "</radialGradient>";
            const string middle = "<radialGradient id='middle' href='#base'/>";
            string child = "<radialGradient id='g' href='#middle' gradientUnits='userSpaceOnUse' cx='.5' cy='.5' r='.45' gradientTransform='translate(0 0)' spreadMethod='pad'>" + (ownStops ? Stops : string.Empty) + "</radialGradient>";
            AssertPercentageRejected(Document(forward ? child + middle + parent : parent + middle + child));
            string numericParent = parent.Replace(percentage, number, StringComparison.Ordinal);
            byte[] expected = Render(Document(numericParent + middle + child));
            string replacement = child.Replace("id='g'", "id='g' " + axis + "='" + number + "'", StringComparison.Ordinal);
            Assert.Equal(expected, Render(Document(forward ? replacement + middle + parent : parent + middle + replacement)));
        }
    }

    [Fact]
    public void Rejected_templates_leave_streams_empty_and_allow_an_absolute_retry()
    {
        const string definitions = "<linearGradient id='g' href='#base' x2='50%'/><linearGradient id='base' gradientUnits='userSpaceOnUse' x1='0' y1='0' x2='32' y2='0'><stop stop-color='red'/><stop offset='1' stop-color='blue'/></linearGradient>";
        byte[] input = Document(definitions);
        using MemoryStream output = new();
        Assert.Equal(SvgErrorCode.UnsupportedFeature, Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(input, output, SvgRenderOptions.Default, CancellationToken.None)).ErrorCode);
        Assert.Equal(0, output.Length);
        Assert.NotEmpty(Render(Document(definitions.Replace("50%", "16", StringComparison.Ordinal))));
    }

    private static void AssertPercentageRejected(byte[] input)
    {
        RenderBudget budget = new(SvgRenderLimits.Default.MaximumAccountedBytes);
        SvgException error = Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None));
        Assert.Equal(SvgErrorCode.UnsupportedFeature, error.ErrorCode);
        Assert.Contains("Percentage userSpaceOnUse", error.Message, StringComparison.Ordinal);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    private static byte[] Document(string definitions)
        => System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'><defs>" + definitions + "</defs><rect width='32' height='32' fill='url(#g)'/></svg>");

    private static byte[] Render(byte[] input)
        => SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None).ToArray();
}
