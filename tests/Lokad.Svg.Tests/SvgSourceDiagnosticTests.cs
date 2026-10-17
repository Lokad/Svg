namespace Lokad.Svg.Tests;

public sealed class SvgSourceDiagnosticTests
{
    [Theory]
    [InlineData("<foreignObject id='foreign'/>", "foreignObject", "foreign", "", "")]
    [InlineData("<rect id='shape' width='8' height='8' transform-origin='center'/>", "rect", "shape", "transform-origin", "center")]
    [InlineData("<path id='edge' d='M0 0L8 8' stroke='red' stroke-linejoin='arcs'/>", "path", "edge", "stroke-linejoin", "arcs")]
    public void Unsupported_input_identifies_its_element_attribute_value_and_position(string child, string element, string id, string attribute, string value)
    {
        string input = "<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'>\n  " + child + "\n</svg>";
        SvgException error = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(System.Text.Encoding.UTF8.GetBytes(input), SvgRenderOptions.Default, CancellationToken.None));
        Assert.Equal(SvgErrorCode.UnsupportedFeature, error.ErrorCode);
        SvgSourceDiagnostic context = Assert.IsType<SvgSourceDiagnostic>(error.SourceDiagnostic);
        Assert.Equal(element, context.ElementName); Assert.Equal(id, context.ElementId);
        Assert.Equal(attribute, context.AttributeName); Assert.Equal(value, context.AttributeValue);
        Assert.Equal(2, context.LineNumber);
        Assert.Equal(child.IndexOf(attribute.Length == 0 ? element : attribute, StringComparison.Ordinal) + 3, context.LinePosition);
    }

    [Fact]
    public void Inspection_and_conversion_share_actionable_sizing_failures()
    {
        byte[] input = "<svg xmlns='http://www.w3.org/2000/svg' id='root'\n width='12em' height='16'/>"u8.ToArray();
        SvgException inspection = Assert.Throws<SvgException>(() => SvgConverter.InspectDimensions(input, SvgRenderOptions.Default, CancellationToken.None));
        SvgException conversion = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None));
        Assert.Equal(inspection.ErrorCode, conversion.ErrorCode);
        foreach (SvgException error in new[] { inspection, conversion })
        {
            SvgSourceDiagnostic source = Assert.IsType<SvgSourceDiagnostic>(error.SourceDiagnostic);
            Assert.Equal("svg", source.ElementName); Assert.Equal("root", source.ElementId);
            Assert.Equal("width", source.AttributeName); Assert.Equal("12em", source.AttributeValue);
            Assert.Equal(2, source.LineNumber); Assert.Equal(2, source.LinePosition);
        }
    }

    [Fact]
    public async Task Source_enrichment_keeps_sink_integrity_and_first_failure()
    {
        byte[] input = "<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'>\n<rect id='first' transform-origin='center'/>\n<foreignObject id='later'/>\n</svg>"u8.ToArray();
        using MemoryStream stream = new();
        stream.WriteByte(42);
        SvgException error = await Assert.ThrowsAsync<SvgException>(async () => await SvgConverter.ConvertToPngAsync(input, stream, SvgRenderOptions.Default, CancellationToken.None));
        Assert.Equal("first", Assert.IsType<SvgSourceDiagnostic>(error.SourceDiagnostic).ElementId);
        Assert.Equal(new byte[] { 42 }, stream.ToArray());
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        Assert.Equal(cancellation.Token, Assert.ThrowsAny<OperationCanceledException>(() =>
            SvgConverter.ConvertToPng(input, stream, SvgRenderOptions.Default, cancellation.Token)).CancellationToken);
        Assert.Equal(new byte[] { 42 }, stream.ToArray());
    }

    [Fact]
    public void Source_context_is_bounded_and_old_exception_constructors_remain_available()
    {
        SvgSourceDiagnostic source = new(new string('x', 300), new string('i', 300), new string('a', 300), new string('v', 300), null, null, new string('u', 300));
        Assert.Equal(256, source.ElementName.Length); Assert.Equal(256, source.ElementId.Length);
        Assert.Equal(256, source.AttributeName.Length); Assert.Equal(256, source.AttributeValue.Length);
        Assert.Equal(256, source.InstanceElementId.Length);
        Assert.Null(new SvgException(SvgErrorCode.UnsupportedFeature, "old").SourceDiagnostic);
        Assert.Null(new SvgException(SvgErrorCode.UnsupportedFeature, "old", null).SourceDiagnostic);
        Assert.Equal("lineNumber", Assert.Throws<ArgumentOutOfRangeException>(() => new SvgSourceDiagnostic("", "", "", "", 0, null)).ParamName);
        Assert.Equal("unicodeScalar", Assert.Throws<ArgumentOutOfRangeException>(() =>
            new Fonts.SvgFontDiagnostic(Fonts.SvgFontFailureStage.Glyph, "", "", "", "", null, null, null, 0xD800, 0)).ParamName);
        Assert.Equal("scalarIndex", Assert.Throws<ArgumentOutOfRangeException>(() =>
            new Fonts.SvgFontDiagnostic(Fonts.SvgFontFailureStage.Glyph, "", "", "", "", null, null, null, 0xE9, -1)).ParamName);
    }
}
