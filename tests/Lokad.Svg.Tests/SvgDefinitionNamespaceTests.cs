using Lokad.Svg.Fonts;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgDefinitionNamespaceTests
{
    [Theory]
    [InlineData("<defs xmlns='http://www.w3.org/2000/svg' id='definitions'>$</defs>")]
    [InlineData("<defs xmlns:metadata='urn:metadata' id='definitions'>$</defs>")]
    [InlineData("<defs xmlns:s='http://www.w3.org/2000/svg' id='definitions'>$</defs>")]
    [InlineData("<s:defs xmlns:s='http://www.w3.org/2000/svg' id='definitions'>$</s:defs>")]
    public void Namespace_declarations_preserve_definition_replay_and_stylesheet_lookup(string container)
    {
        const string children = "<rect id='shape' width='8' height='8'/><style>#shape{fill:red}</style>";
        const string use = "<use href='#shape'/>";
        byte[] input = Document(container.Replace("$", children, StringComparison.Ordinal) + use);
        byte[] expected = Document("<defs id='definitions'>" + children + "</defs>" + use);
        Assert.Equal(SvgConverter.ConvertToPng(expected, SvgRenderOptions.Default, CancellationToken.None).ToArray(),
            SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None).ToArray());
        Assert.Equal(new SvgViewport(16, 16), SvgConverter.InspectDimensions(input, SvgRenderOptions.Default, CancellationToken.None).LogicalViewport);
        RenderBudget budget = new(1_000_000);
        Assert.Empty(SvgFontRequestDiscovery.Discover(input, SvgRenderLimits.Default, budget, CancellationToken.None));
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Theory]
    [InlineData("class='extra'")]
    [InlineData("fill='red'")]
    [InlineData("metadata:label='extra'")]
    public void Other_definition_container_attributes_keep_their_existing_rejection(string attribute)
    {
        byte[] input = Document("<defs xmlns:metadata='urn:metadata' " + attribute + "/>");
        Assert.Equal(SvgErrorCode.UnsupportedFeature, Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None)).ErrorCode);
    }

    [Fact]
    public void Namespace_declarations_still_count_against_structural_attribute_limits()
    {
        SvgRenderLimits defaults = SvgRenderLimits.Default;
        SvgRenderLimits limits = new(defaults.MaximumAccountedBytes, defaults.MaximumInputBytes, defaults.MaximumDimension,
            defaults.MaximumPixels, defaults.MaximumEncodedPngBytes, defaults.MaximumNodes, defaults.MaximumPathCommands,
            defaults.MaximumWorkUnits, defaults.MaximumXmlDepth, 1);
        byte[] input = "<svg xmlns='http://www.w3.org/2000/svg'><defs xmlns:metadata='urn:metadata' id='definitions'/></svg>"u8.ToArray();
        RenderBudget budget = new(1_000_000);
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, limits, budget, CancellationToken.None)).ErrorCode);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Theory]
    [InlineData("<s:defs/>")]
    [InlineData("<defs xmlns:xml='urn:invalid'/>")]
    public void Malformed_namespace_bindings_remain_xml_errors(string content)
        => Assert.Equal(SvgErrorCode.MalformedInput, Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(Document(content), SvgRenderOptions.Default, CancellationToken.None)).ErrorCode);

    private static byte[] Document(string content)
        => System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'>" + content + "</svg>");
}
