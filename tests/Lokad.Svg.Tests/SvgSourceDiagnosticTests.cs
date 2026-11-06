using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgSourceDiagnosticTests
{
    [Theory]
    [Trait("Layer", "Mechanism")]
    [InlineData(0, 0, 2)]
    [InlineData(0, 1, 3)]
    [InlineData(254, 0, 256)]
    [InlineData(254, 1, 256)]
    [InlineData(255, 0, 255)]
    [InlineData(255, 1, 255)]
    [InlineData(256, 0, 256)]
    [InlineData(256, 1, 256)]
    public void Diagnostic_prefix_keeps_complete_surrogate_pairs(int prefixLength, int suffixLength, int expectedLength)
    {
        string value = new string('x', prefixLength) + "\U0001F680" + new string('y', suffixLength);
        SvgSourceDiagnostic source = new("", "", "", value, null, null);
        Assert.Equal(value[..expectedLength], source.AttributeValue);
    }

    [Fact]
    [Trait("Layer", "Contract")]
    public void Every_source_diagnostic_field_preserves_the_bounded_unicode_prefix()
    {
        string expected = new('x', 255);
        string value = expected + "\U0001F680suffix";
        SvgSourceDiagnostic source = new(value, value, value, value, null, null, value);
        Assert.Equal(expected, source.ElementName);
        Assert.Equal(expected, source.ElementId);
        Assert.Equal(expected, source.AttributeName);
        Assert.Equal(expected, source.AttributeValue);
        Assert.Equal(expected, source.InstanceElementId);
    }

    [Fact]
    [Trait("Layer", "Contract")]
    public void Every_font_diagnostic_field_preserves_the_bounded_unicode_prefix()
    {
        string expected = new('x', 255);
        string value = expected + "\U0001F680suffix";
        Fonts.SvgFontDiagnostic font = new(Fonts.SvgFontFailureStage.Selection, value, value, value, value);
        Assert.Equal(expected, font.RequestedFamily);
        Assert.Equal(expected, font.ResolvedFamily);
        Assert.Equal(expected, font.FaceIdentity);
        Assert.Equal(expected, font.SourceElementId);
    }

    [Theory]
    [Trait("Layer", "Mechanism")]
    [InlineData(false)]
    [InlineData(true)]
    public void Captured_source_ids_keep_complete_pairs_at_immediate_and_deferred_failures(bool deferred)
    {
        string expected = new('i', 255);
        string id = expected + "\U0001F680suffix";
        string content = deferred
            ? $"<defs><pattern id='{id}' width='1' height='1'/><pattern id='{id}' width='1' height='1'/></defs>"
            : $"<audio id='{id}'/>";
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'>" + content + "</svg>");
        SvgException error = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None));
        Assert.Equal(deferred ? SvgErrorCode.MalformedInput : SvgErrorCode.UnsupportedFeature, error.ErrorCode);
        SvgSourceDiagnostic source = Assert.IsType<SvgSourceDiagnostic>(error.SourceDiagnostic);
        Assert.Equal(expected, source.ElementId);
        Assert.Equal(deferred ? "pattern" : "audio", source.ElementName);
        if (deferred) Assert.Equal(expected, source.AttributeValue);
    }

    [Theory]
    [Trait("Layer", "Mechanism")]
    [InlineData("<g id='same'/>", "<g id='same'/>\n", "g", "definition")]
    [InlineData("<use id='same'/>", "<use id='same'/>", "use", "definition")]
    [InlineData("<path id='same' d='M0 0L1 1'/>", "<rect id='same' width='1' height='1'/>", "rect", "definition")]
    [InlineData("<defs><rect id='same'/></defs>", "<defs><text id='same'>A</text></defs>", "text", "definition")]
    [InlineData("<defs><g id='same'/></defs>", "<defs><symbol id='same'>\n<rect/>\n</symbol></defs>", "symbol", "definition")]
    [InlineData("<defs><linearGradient id='same'/></defs>", "<defs><radialGradient id='same'/></defs>", "radialGradient", "gradient")]
    [InlineData("<defs><pattern id='same' width='2' height='2'/></defs>", "<defs><pattern id='same' width='2' height='2'>\n<rect width='1' height='1'/>\n</pattern></defs>", "pattern", "pattern")]
    [InlineData("<defs><mask id='same'/></defs>", "<defs><mask id='same'/></defs>", "mask", "mask")]
    [InlineData("<defs><mask id='same'/></defs>", "<defs><mask id='same' style='display:none'>\n<rect/>\n</mask></defs>", "mask", "mask")]
    [InlineData("<defs><mask id='same'/></defs>", "<defs><mask id='same'>\n<path d='M0 0L8 0L0 8Z' fill='white'/>\n</mask></defs>", "mask", "mask")]
    [InlineData("<defs><clipPath id='same'/></defs>", "<defs><clipPath id='same'>\n<rect width='4' height='4'/>\n</clipPath></defs>", "clipPath", "clip-path")]
    [InlineData("<defs><clipPath id='same'/></defs>", "<defs><clipPath id='same' clipPathUnits='objectBoundingBox'>\n<path d='M0 0L1 0L0 1Z'/>\n</clipPath></defs>", "clipPath", "clip-path")]
    [InlineData("<defs><clipPath id='same'/><path id='target' d='M0 0L8 0L0 8Z'/></defs>", "<defs><clipPath id='same'>\n<use href='#target'/>\n</clipPath></defs>", "clipPath", "clip-path")]
    [InlineData("<defs><filter id='same'/></defs>", "<defs><filter id='same'>\n<feOffset dx='1'/>\n</filter></defs>", "filter", "filter")]
    public void Duplicate_ids_identify_the_second_declaration_including_deferred_registration(string first, string second, string element, string resource)
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'>\n"
            + first + "\n  " + second + "\n</svg>");
        SvgException error = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None));
        Assert.Equal(SvgErrorCode.MalformedInput, error.ErrorCode);
        Assert.Equal($"SVG {resource} ids must be unique.", error.Message);
        SvgSourceDiagnostic source = Assert.IsType<SvgSourceDiagnostic>(error.SourceDiagnostic);
        Assert.Equal(element, source.ElementName);
        Assert.Equal("same", source.ElementId);
        Assert.Equal("id", source.AttributeName);
        Assert.Equal("same", source.AttributeValue);
        Assert.Equal(3, source.LineNumber);
        Assert.Equal(second.IndexOf("id='same'", StringComparison.Ordinal) + 3, source.LinePosition);
    }

    [Fact]
    [Trait("Layer", "Mechanism")]
    public void Deferred_duplicate_diagnostics_bound_the_identifier_without_truncating_lookup()
    {
        string common = new('i', 300);
        string input = "<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><defs>"
            + $"<pattern id='{common}a' width='1' height='1'/><pattern id='{common}b' width='1' height='1'/>"
            + $"<pattern id='{common}b' width='1' height='1'/></defs></svg>";
        SvgException error = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(System.Text.Encoding.UTF8.GetBytes(input), SvgRenderOptions.Default, CancellationToken.None));
        Assert.Equal(SvgErrorCode.MalformedInput, error.ErrorCode);
        SvgSourceDiagnostic source = Assert.IsType<SvgSourceDiagnostic>(error.SourceDiagnostic);
        Assert.Equal(common[..256], source.ElementId);
        Assert.Equal(common[..256], source.AttributeValue);
        Assert.Equal(input.LastIndexOf("id='", StringComparison.Ordinal) + 1, source.LinePosition);
    }

    [Theory]
    [Trait("Layer", "Contract")]
    [InlineData("<pattern id='same' width='2' height='2'><path d='M0 0L1 0L0 1Z'/></pattern>")]
    [InlineData("<filter id='same'><feOffset dx='1'/></filter>")]
    public void Deferred_duplicate_rejection_releases_resources_and_preserves_borrowed_budget(string resource)
    {
        const string start = "<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'>";
        byte[] invalid = System.Text.Encoding.UTF8.GetBytes(start + "<defs>" + resource + resource + "</defs></svg>");
        RenderBudget budget = new(new HostMemoryBudget(1_000_000));
        using (RenderBudget.BudgetReservation borrowed = budget.Reserve(73, SvgMemoryCategory.Scene))
        {
            SvgException error = Assert.Throws<SvgException>(() => SvgRootParser.Parse(invalid, SvgRenderLimits.Default, budget, CancellationToken.None));
            Assert.Equal(SvgErrorCode.MalformedInput, error.ErrorCode);
            Assert.Equal("id", Assert.IsType<SvgSourceDiagnostic>(error.SourceDiagnostic).AttributeName);
            Assert.Equal(73, budget.Snapshot().CurrentBytes);
            using (SvgRoot retry = SvgRootParser.Parse(System.Text.Encoding.UTF8.GetBytes(start + "<rect width='4' height='4'/></svg>"), SvgRenderLimits.Default, budget, CancellationToken.None)) { }
            Assert.Equal(73, budget.Snapshot().CurrentBytes);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Theory]
    [Trait("Layer", "Mechanism")]
    [InlineData("<audio id='first'/>", "audio")]
    [InlineData("<animate id='first'/>", "animate")]
    [InlineData("<f:rect xmlns:f='urn:foreign' id='first'><f:child/></f:rect>", "rect")]
    public void Later_text_preserves_the_first_unsupported_source(string unsupported, string element)
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'>\n"
            + unsupported + "\n<text id='later' font-family='Missing'>A</text>\n</svg>");
        SvgException error = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None));
        Assert.Equal(SvgErrorCode.UnsupportedFeature, error.ErrorCode);
        SvgSourceDiagnostic source = Assert.IsType<SvgSourceDiagnostic>(error.SourceDiagnostic);
        Assert.Equal(element, source.ElementName);
        Assert.Equal("first", source.ElementId);
        Assert.Equal(2, source.LineNumber);
    }

    [Theory]
    [Trait("Layer", "Mechanism")]
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
    [Trait("Layer", "Contract")]
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
    [Trait("Layer", "Contract")]
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
    [Trait("Layer", "Contract")]
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
