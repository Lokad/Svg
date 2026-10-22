using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgGradientTemplateAttributeTests
{
    private const string Stops = "<stop stop-color='red'/><stop offset='1' stop-color='blue'/>";

    [Theory]
    [InlineData("linearGradient", "units")]
    [InlineData("linearGradient", "transform")]
    [InlineData("linearGradient", "spread")]
    [InlineData("radialGradient", "units")]
    [InlineData("radialGradient", "transform")]
    [InlineData("radialGradient", "spread")]
    public void Full_coordinates_keep_independent_template_attributes(string kind, string attribute)
    {
        foreach (bool forward in new[] { false, true })
        foreach (bool middle in new[] { false, true })
        foreach (bool ownStops in new[] { false, true })
        {
            var documents = Documents(kind, attribute, forward, middle, ownStops);
            Assert.Equal(Render(documents.Expected), Render(documents.Actual));
        }
    }

    [Theory]
    [InlineData("linearGradient", "matrix(1 .2 .1 1 2 0)")]
    [InlineData("radialGradient", "matrix(1 .2 .1 1 2 0)")]
    [InlineData("linearGradient", "translate(4 0) scale(1.25 .75)")]
    [InlineData("radialGradient", "translate(4 0) scale(1.25 .75)")]
    [InlineData("linearGradient", "rotate(17 16 16)")]
    [InlineData("radialGradient", "rotate(17 16 16)")]
    [InlineData("linearGradient", "matrix(-1 0 0 1 32 0)")]
    [InlineData("radialGradient", "matrix(-1 0 0 1 32 0)")]
    public void Full_vectors_reapply_the_original_inherited_transform(string kind, string transform)
    {
        string coordinates = kind == "linearGradient" ? "x1='0' y1='0' x2='16' y2='0'" : "cx='12' cy='16' r='8'";
        string parent = "<" + kind + " id='base' gradientUnits='userSpaceOnUse' " + coordinates + " gradientTransform='" + transform + "'>" + Stops + "</" + kind + ">";
        string child = "<" + kind + " id='g' href='#middle' " + coordinates + "/>";
        string intermediary = "<" + kind + " id='middle' href='#base'/>";
        byte[] expected = Render(Document(parent.Replace("id='base'", "id='g'", StringComparison.Ordinal)));
        Assert.Equal(expected, Render(Document(child + intermediary + parent)));
    }

    [Theory]
    [InlineData("linearGradient")]
    [InlineData("radialGradient")]
    public void Complete_explicit_attributes_and_stops_override_templates_without_a_dependency(string kind)
    {
        string coordinates = kind == "linearGradient" ? "x1='0' y1='0' x2='16' y2='0'" : "cx='12' cy='16' r='8'";
        string attributes = coordinates + " gradientUnits='userSpaceOnUse' gradientTransform='translate(4 0)' spreadMethod='reflect'";
        string explicitGradient = "<" + kind + " id='g' " + attributes + ">" + Stops + "</" + kind + ">";
        Assert.Equal(Render(Document(explicitGradient)), Render(Document(explicitGradient.Replace("id='g'", "id='g' href='#missing'", StringComparison.Ordinal))));
        string parentCoordinates = kind == "linearGradient" ? "x1='0' y1='0' x2='1' y2='0'" : "cx='.5' cy='.5' r='.5'";
        string parent = "<" + kind + " id='base' " + parentCoordinates + " gradientUnits='objectBoundingBox' gradientTransform='translate(.25 0)' spreadMethod='repeat'><stop stop-color='black'/></" + kind + ">";
        Assert.Equal(Render(Document(explicitGradient)), Render(Document(parent + explicitGradient.Replace("id='g'", "id='g' href='#base'", StringComparison.Ordinal))));
    }

    [Theory]
    [InlineData("linearGradient", "x1='0' y1='0' x2='16' y2='0'")]
    [InlineData("radialGradient", "cx='12' cy='16' r='8'")]
    public void Own_stops_and_full_coordinates_still_require_missing_template_attributes(string kind, string coordinates)
    {
        foreach (string target in new[] { "missing", "g" })
        {
            byte[] input = Document("<" + kind + " id='g' href='#" + target + "' " + coordinates + ">" + Stops + "</" + kind + ">");
            RenderBudget budget = new(SvgRenderLimits.Default.MaximumAccountedBytes);
            Assert.Equal(SvgErrorCode.UnsupportedFeature, Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None)).ErrorCode);
            Assert.Equal(0, budget.Snapshot().CurrentBytes);
        }
    }

    [Theory]
    [InlineData("linearGradient")]
    [InlineData("radialGradient")]
    public void Template_resolution_releases_owners_on_denial_and_cancellation(string kind)
    {
        var documents = Documents(kind, "transform", true, true, true);
        RenderBudget measured = new(SvgRenderLimits.Default.MaximumAccountedBytes);
        ParserWorkCounter work = new(SvgRenderLimits.Default.MaximumWorkUnits);
        using (SvgRoot root = SvgRootParser.Parse(documents.Actual, SvgRenderLimits.Default, measured, null, work, CancellationToken.None)) { }
        Assert.Equal(0, measured.Snapshot().CurrentBytes);
        RenderBudget denied = new(measured.Snapshot().PeakBytes - 1);
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgRootParser.Parse(documents.Actual, SvgRenderLimits.Default, denied, CancellationToken.None)).ErrorCode);
        Assert.Equal(0, denied.Snapshot().CurrentBytes);
        RenderBudget workDenied = new(SvgRenderLimits.Default.MaximumAccountedBytes);
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgRootParser.Parse(documents.Actual, SvgRenderLimits.Default, workDenied, null, new(work.Used - 1), CancellationToken.None)).ErrorCode);
        Assert.Equal(0, workDenied.Snapshot().CurrentBytes);
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        RenderBudget cancelled = new(SvgRenderLimits.Default.MaximumAccountedBytes);
        Assert.Equal(cancellation.Token, Assert.Throws<OperationCanceledException>(() => SvgRootParser.Parse(documents.Actual, SvgRenderLimits.Default, cancelled, cancellation.Token)).CancellationToken);
        Assert.Equal(0, cancelled.Snapshot().CurrentBytes);
        Assert.Equal(Render(documents.Expected), Render(documents.Actual));
    }

    private static (byte[] Actual, byte[] Expected) Documents(string kind, string attribute, bool forward, bool middle, bool ownStops)
    {
        string coordinates = kind == "linearGradient" ? "x1='0' y1='0' x2='16' y2='0'" : "cx='12' cy='16' r='8'";
        string attributes = "gradientUnits='userSpaceOnUse'" + (attribute == "transform" ? " gradientTransform='translate(4 0)'" : attribute == "spread" ? " spreadMethod='reflect'" : "");
        string parent = "<" + kind + " id='base' " + attributes + " " + coordinates + ">" + Stops + "</" + kind + ">";
        string intermediary = middle ? "<" + kind + " id='middle' href='#base'/>" : string.Empty;
        string localStops = ownStops ? "<stop stop-color='green'/><stop offset='1' stop-color='blue'/>" : string.Empty;
        string child = "<" + kind + " id='g' href='#" + (middle ? "middle" : "base") + "' " + (attribute == "units" ? "" : "gradientUnits='userSpaceOnUse'") + " " + coordinates + ">" + localStops + "</" + kind + ">";
        byte[] actual = Document(forward ? child + intermediary + parent : parent + intermediary + child);
        byte[] expected = Document("<" + kind + " id='g' " + attributes + " " + coordinates + ">" + (ownStops ? localStops : Stops) + "</" + kind + ">");
        return (actual, expected);
    }

    private static byte[] Document(string definitions)
        => System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'><defs>" + definitions + "</defs><rect width='32' height='32' fill='url(#g)'/></svg>");

    private static byte[] Render(byte[] input)
        => SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None).ToArray();
}
