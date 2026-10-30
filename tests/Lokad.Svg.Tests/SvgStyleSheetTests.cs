using static Lokad.Svg.Tests.SvgRasterTestHelper;
using System.Xml;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgStyleSheetTests
{
    [Trait("Layer", "Rendering")]
    [Theory]
    [InlineData("<style xmlns='http://www.w3.org/2000/svg'>$</style>")]
    [InlineData("<s:style xmlns:s='http://www.w3.org/2000/svg'>$</s:style>")]
    [InlineData("<style xmlns:metadata='urn:metadata' type='text/css' id='sheet'>$</style>")]
    public void Stylesheet_namespace_declarations_preserve_cascade_sizing_and_discovery(string sheet)
    {
        const string shape = "<rect width='8' height='8'/>";
        sheet = sheet.Replace("$", "svg{width:8px;height:8px} rect{fill:red}", StringComparison.Ordinal);
        byte[] expected = "<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><rect width='8' height='8' fill='red'/></svg>"u8.ToArray();
        foreach (string content in new[] { sheet + shape, shape + sheet, "<defs>" + sheet + "</defs>" + shape })
        {
            byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='4' height='4'>" + content + "</svg>");
            Assert.Equal(SvgConverter.ConvertToPng(expected, SvgRenderOptions.Default, CancellationToken.None).ToArray(),
                SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None).ToArray());
            Assert.Equal(new SvgViewport(8, 8), SvgConverter.InspectDimensions(input, SvgRenderOptions.Default, CancellationToken.None).LogicalViewport);
            RenderBudget budget = new RenderBudget(new HostMemoryBudget(1_000_000));
            Assert.Empty(Lokad.Svg.Fonts.SvgFontRequestDiscovery.Discover(input, SvgRenderLimits.Default, budget, CancellationToken.None));
            Assert.Equal(0, budget.Snapshot().CurrentBytes);
        }
    }

    [Trait("Layer", "Rendering")]
    [Theory]
    [InlineData("<style/>")]
    [InlineData("<style xmlns='http://www.w3.org/2000/svg'/>")]
    [InlineData("<s:style xmlns:s='http://www.w3.org/2000/svg'/>")]
    [InlineData("<style></style>")]
    [InlineData("<style> \t\r\n </style>")]
    [InlineData("<style>/*empty*/</style>")]
    [InlineData("<style><![CDATA[ /*empty*/ ]]></style>")]
    [InlineData("<style><!--empty--></style>")]
    public void Empty_sheets_are_no_ops_in_any_source_order(string empty)
    {
        const string rule = "<style>rect{fill:red}</style>";
        foreach (string styles in new[] { empty, empty + rule, rule + empty, "<defs>" + empty + "</defs>" + rule })
        {
            byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='4' height='4'>"
                + styles + "<rect width='4' height='4' fill='red'/></svg>");
            byte[] control = "<svg xmlns='http://www.w3.org/2000/svg' width='4' height='4'><rect width='4' height='4' fill='red'/></svg>"u8.ToArray();
            Assert.Equal(SvgConverter.ConvertToPng(control, SvgRenderOptions.Default, CancellationToken.None).ToArray(),
                SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None).ToArray());
            Assert.Equal(new SvgViewport(4, 4), SvgConverter.InspectDimensions(input, SvgRenderOptions.Default, CancellationToken.None).LogicalViewport);
            RenderBudget budget = new RenderBudget(new HostMemoryBudget(1_000_000));
            Assert.Empty(Lokad.Svg.Fonts.SvgFontRequestDiscovery.Discover(input, SvgRenderLimits.Default, budget, CancellationToken.None));
            Assert.Equal(0, budget.Snapshot().CurrentBytes);
        }
    }

    [Trait("Layer", "Rendering")]
    [Fact]
    public void Removing_a_selector_comment_does_not_introduce_a_descendant_combinator()
    {
        const string prefix = "<svg xmlns='http://www.w3.org/2000/svg' width='4' height='4'><style>";
        const string suffix = "{fill:red}</style><rect class='label' width='4' height='4'/></svg>";
        Assert.Equal(RenderPixels(prefix + "rect.label" + suffix, out _, out _), RenderPixels(prefix + "rect/**/.label" + suffix, out _, out _));
    }

    [Trait("Layer", "Mechanism")]
    [Fact]
    public void Overflow_and_decoration_keep_independent_priority()
    {
        SvgStyleCascade cascade = new();
        cascade.Apply(SvgInlineStyleParser.Parse("text-decoration:underline!important;overflow:visible", CancellationToken.None, true), new(0, 1, 0));
        cascade.Apply(SvgInlineStyleParser.Parse("overflow:hidden", CancellationToken.None), new(1, 0, 0));
        InlineStyle style = cascade.ToInlineStyle();
        Assert.Equal("hidden", style.Overflow);
        Assert.Equal("underline", style.TextDecoration);
        Assert.Equal(InlineImportantProperties.TextDecoration, style.Important);
    }

    [Trait("Layer", "Mechanism")]
    [Theory]
    [InlineData("overflow:visible!important;overflow:hidden", "visible")]
    [InlineData("overflow:visible;overflow:hidden", "hidden")]
    [InlineData("overflow:hidden!important;overflow:visible", "hidden")]
    public void Overflow_duplicates_follow_declaration_priority(string declarations, string expected)
        => Assert.Equal(expected, SvgInlineStyleParser.Parse(declarations, CancellationToken.None).Overflow);

    [Trait("Layer", "Mechanism")]
    [Fact]
    public void Cascades_each_presentation_property_by_specificity_then_source_order()
    {
        SvgStyleCascade cascade = new();
        cascade.Apply(new InlineStyle("red", "red", "0.25", null, "none", null, "0.5", null), new(0, 0, 1));
        cascade.Apply(new InlineStyle(null, "blue", null, "none", "inline", "hidden", "0.75", "evenodd"), new(0, 0, 1));
        cascade.Apply(new InlineStyle(null, "green", "0.5", null, null, null, null, null), default);
        cascade.Apply(new InlineStyle(null, null, "0.5", null, null, null, null, null), new(0, 0, 2));

        InlineStyle style = cascade.ToInlineStyle();

        Assert.Equal("red", style.Color);
        Assert.Equal("blue", style.Fill);
        Assert.Equal("0.5", style.FillOpacity);
        Assert.Equal("none", style.Stroke);
        Assert.Equal("inline", style.Display);
        Assert.Equal("hidden", style.Visibility);
        Assert.Equal("0.75", style.Opacity);
        Assert.Equal("evenodd", style.FillRule);
    }

    [Trait("Layer", "Mechanism")]
    [Fact]
    public void Cascades_css_whitespace_by_specificity_then_source_order()
    {
        SvgStyleCascade cascade = new();
        cascade.Apply(new InlineStyle(null, null, null, null, null, null, null, null) { WhiteSpace = "pre" }, new(0, 0, 1));
        cascade.Apply(new InlineStyle(null, null, null, null, null, null, null, null) { WhiteSpace = "normal" }, default);
        cascade.Apply(new InlineStyle(null, null, null, null, null, null, null, null) { WhiteSpace = "normal" }, new(0, 0, 1));

        Assert.Equal("normal", cascade.ToInlineStyle().WhiteSpace);
    }

    [Trait("Layer", "Mechanism")]
    [Fact]
    public void Important_stylesheet_declarations_beat_higher_specificity_normal_declarations()
    {
        SvgStyleCascade cascade = new();
        cascade.Apply(new InlineStyle(null, "red", null, null, null, null, null, null), new(1, 0, 0));
        cascade.Apply(new InlineStyle(null, "blue", null, null, null, null, null, null) { Important = InlineImportantProperties.Fill }, new(0, 0, 1));

        InlineStyle style = cascade.ToInlineStyle();

        Assert.Equal("blue", style.Fill);
        Assert.Equal(InlineImportantProperties.Fill, style.Important);
    }

    [Trait("Layer", "Mechanism")]
    [Fact]
    public void Later_important_stylesheet_declarations_win_source_order_ties()
    {
        SvgStyleCascade cascade = new();
        cascade.Apply(new InlineStyle(null, "red", null, null, null, null, null, null) { Important = InlineImportantProperties.Fill }, new(0, 0, 1));
        cascade.Apply(new InlineStyle(null, "blue", null, null, null, null, null, null) { Important = InlineImportantProperties.Fill }, new(0, 0, 1));

        Assert.Equal("blue", cascade.ToInlineStyle().Fill);
    }

    [Trait("Layer", "Mechanism")]
    [Fact]
    public void Normal_stylesheet_declarations_do_not_override_earlier_important_ones()
    {
        SvgStyleCascade cascade = new();
        cascade.Apply(new InlineStyle(null, "red", null, null, null, null, null, null) { Important = InlineImportantProperties.Fill }, new(0, 0, 1));
        cascade.Apply(new InlineStyle(null, "blue", null, null, null, null, null, null), new(1, 0, 1));

        Assert.Equal("red", cascade.ToInlineStyle().Fill);
    }

    [Trait("Layer", "Mechanism")]
    [Theory]
    [InlineData("fill: red !important", "red", true)]
    [InlineData("fill: red!important", "red", true)]
    [InlineData("fill: red !IMPORTANT", "red", true)]
    [InlineData("fill: red ! important", "red", true)]
    [InlineData("fill: red", "red", false)]
    public void Parses_important_suffix_variants(string declaration, string expectedValue, bool expectedImportant)
    {
        InlineStyle style = SvgInlineStyleParser.Parse(declaration, CancellationToken.None);

        Assert.Equal(expectedValue, style.Fill);
        Assert.Equal(expectedImportant, (style.Important & InlineImportantProperties.Fill) != 0);
    }

    [Trait("Layer", "Mechanism")]
    [Fact]
    public void Important_beats_normal_within_one_declaration_block_regardless_of_order()
    {
        InlineStyle importantFirst = SvgInlineStyleParser.Parse("fill: red !important; fill: blue", CancellationToken.None);
        InlineStyle importantLast = SvgInlineStyleParser.Parse("fill: red; fill: blue !important", CancellationToken.None);

        Assert.Equal("red", importantFirst.Fill);
        Assert.Equal("blue", importantLast.Fill);
    }

    [Trait("Layer", "Mechanism")]
    [Theory]
    [InlineData("fill", "inherit", "inherit")]
    [InlineData("fill", "initial", "black")]
    [InlineData("fill", "unset", "inherit")]
    [InlineData("color", "INHERIT", "inherit")]
    [InlineData("fill-opacity", "initial", "1")]
    [InlineData("fill-opacity", "unset", "inherit")]
    [InlineData("stroke", "initial", "none")]
    [InlineData("stroke", "unset", "inherit")]
    [InlineData("display", "initial", "inline")]
    [InlineData("visibility", "unset", "inherit")]
    [InlineData("opacity", "initial", "1")]
    [InlineData("opacity", "inherit", "inherit")]
    [InlineData("opacity", "unset", "1")]
    [InlineData("fill-rule", "unset", "inherit")]
    [InlineData("stroke", "inherit", "inherit")]
    [InlineData("fill-rule", "inherit", "inherit")]
    public void Maps_paint_keywords_to_specified_values(string property, string value, string? expected)
    {
        InlineStyle style = SvgInlineStyleParser.Parse(property + ": " + value, CancellationToken.None);

        Assert.Equal(expected, GetPaintValue(style, property));

        static string? GetPaintValue(InlineStyle style, string property) => property switch
        {
            "color" => style.Color,
            "fill" => style.Fill,
            "fill-opacity" => style.FillOpacity,
            "stroke" => style.Stroke,
            "display" => style.Display,
            "visibility" => style.Visibility,
            "opacity" => style.Opacity,
            "fill-rule" => style.FillRule,
            _ => throw new InvalidOperationException("Unexpected paint property.")
        };
    }

    [Trait("Layer", "Mechanism")]
    [Theory]
    [InlineData("display: inherit")]
    [InlineData("display: INHERIT!important")]
    public void Retains_display_inheritance_for_parent_resolution(string declaration)
    {
        InlineStyle style = SvgInlineStyleParser.Parse(declaration, CancellationToken.None);
        Assert.Equal(SvgInlineStyleParser.InheritedValue, style.Display);
    }

    [Trait("Layer", "Rendering")]
    [Theory]
    [InlineData("fill", "red", "inherit", "green")]
    [InlineData("fill", "red", "unset", "green")]
    [InlineData("fill-opacity", "0.8", "inherit", "0.4")]
    [InlineData("color", "blue", "inherit", "green")]
    [InlineData("visibility", "visible", "inherit", "hidden")]
    [InlineData("stroke", "red", "inherit", "green")]
    [InlineData("stroke-width", "1", "inherit", "4")]
    [InlineData("stroke-linecap", "butt", "inherit", "round")]
    [InlineData("stroke-linejoin", "miter", "inherit", "round")]
    [InlineData("stroke-miterlimit", "4", "inherit", "2")]
    [InlineData("stroke-dasharray", "none", "inherit", "4 2")]
    [InlineData("stroke-dashoffset", "0", "inherit", "2")]
    [InlineData("stroke-opacity", "1", "inherit", "0.4")]
    [InlineData("fill-rule", "nonzero", "inherit", "evenodd")]
    public void Winning_inheritance_binds_to_parent_after_cascade(string property, string lower, string keyword, string parent)
    {
        string geometry = "d='M8 8H32V32H8Z M12 12H28V28H12Z M4 4L20 4L8 12' "
            + (property == "fill" ? "" : "fill='currentColor' ")
            + (property == "stroke" ? "" : "stroke='black' ")
            + (property == "stroke-width" ? "" : "stroke-width='4' ");
        string shape = "<path id='box' class='selected' " + geometry + property + "='" + lower + "'/>";
        string prefix = "<svg xmlns='http://www.w3.org/2000/svg' width='40' height='40'><g " + property + "='" + parent + "'>";
        string expected = prefix + "<path " + geometry + property + "='" + parent + "'/></g></svg>";
        string actual = prefix + shape + "</g><style>.selected{" + property + ":" + lower + "}#box{" + property + ":" + keyword + "!important}</style></svg>";
        // The declaration must defeat both the presentation attribute and a normal stylesheet rule.
        Assert.Equal(RenderPixels(expected, out _, out _), RenderPixels(actual, out _, out _));
    }

    [Trait("Layer", "Rendering")]
    [Theory]
    [InlineData("fill: blue; fill: inherit !important; fill: red", "fill: inherit !important")]
    [InlineData("fill: blue !important; fill: inherit !important", "fill: inherit !important")]
    [InlineData("fill: inherit; fill: blue", "fill: blue")]
    [InlineData("fill: inherit !important; fill: blue", "fill: inherit !important")]
    public void Inline_inheritance_keeps_its_declaration_priority(string declarations, string expectedStyle)
    {
        string prefix = "<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><g fill='green'><rect width='8' height='8' fill='red' style='";
        string suffix = "'/></g></svg>";
        Assert.Equal(RenderPixels(prefix + expectedStyle + suffix, out _, out _), RenderPixels(prefix + declarations + suffix, out _, out _));
    }

    [Trait("Layer", "Rendering")]
    [Theory]
    [InlineData("fill", "blue", "green")]
    [InlineData("stroke", "blue", "green")]
    [InlineData("stroke-width", "1", "4")]
    [InlineData("stroke-dasharray", "none", "4 2")]
    [InlineData("stroke-opacity", "1", "0.4")]
    public void Winning_inheritance_in_symbol_source_binds_to_instance_parent(string property, string lower, string parent)
    {
        string geometry = "d='M8 8H32V32H8Z M12 12H28V28H12Z' "
            + (property == "fill" ? "" : "fill='red' ")
            + (property == "stroke" ? "" : "stroke='black' ");
        string prefix = "<svg xmlns='http://www.w3.org/2000/svg' width='40' height='40'>";
        string actual = prefix + "<defs><symbol id='item' viewBox='0 0 40 40'><path id='box' " + geometry + property + "='" + lower + "'/></symbol></defs>"
            + "<use href='#item' width='40' height='40' style='" + property + ":" + parent + "'/><style>#box{" + property + ":inherit!important}</style></svg>";
        string expected = prefix + "<path " + geometry + property + "='" + parent + "'/></svg>";
        Assert.Equal(RenderPixels(expected, out _, out _), RenderPixels(actual, out _, out _));
    }

    [Trait("Layer", "Rendering")]
    [Theory]
    [InlineData("clip-rule='inherit'")]
    [InlineData("clip-rule='nonzero' style='clip-rule:inherit!important'")]
    [InlineData("clip-rule='nonzero' style='clip-rule:unset!important'")]
    public void Clip_rule_inheritance_preserves_the_parent_parity_boundary(string declaration)
    {
        string prefix = "<svg xmlns='http://www.w3.org/2000/svg' width='40' height='40' clip-rule='evenodd'><defs><clipPath id='cut'><path d='M8 8H32V32H8Z M12 12H28V28H12Z' ";
        string suffix = "/></clipPath></defs><rect width='40' height='40' fill='red' clip-path='url(#cut)'/></svg>";
        byte[] expected = RenderPixels(prefix + "clip-rule='evenodd'" + suffix, out _, out _);
        byte[] actual = RenderPixels(prefix + declaration + suffix, out _, out _);
        Assert.Equal(expected, actual);
        Assert.Equal((byte)0, actual[(20 * 40 + 20) * 4 + 3]);
        Assert.Equal((byte)255, actual[(10 * 40 + 10) * 4 + 3]);
    }

    [Trait("Layer", "Rendering")]
    [Theory]
    [InlineData("stroke-linecap", "round", "inherit")]
    [InlineData("stroke-linejoin", "round", "inherit")]
    [InlineData("stroke-dasharray", "4 2", "unset")]
    [InlineData("stroke-dashoffset", "2", "inherit")]
    public void Stroke_attribute_keywords_resolve_before_geometry_validation(string property, string parent, string keyword)
    {
        string prefix = "<svg xmlns='http://www.w3.org/2000/svg' width='40' height='40'><g " + property + "='" + parent + "'><path d='M8 8L32 8L32 32' fill='none' stroke='black' stroke-width='4' ";
        string suffix = "/></g></svg>";
        Assert.Equal(RenderPixels(prefix + property + "='" + parent + "'" + suffix, out _, out _), RenderPixels(prefix + property + "='" + keyword + "'" + suffix, out _, out _));
    }

    [Trait("Layer", "Rendering")]
    [Theory]
    [InlineData("stroke", "green", "none")]
    [InlineData("stroke-width", "4", "1")]
    [InlineData("stroke-linecap", "round", "butt")]
    [InlineData("stroke-linejoin", "round", "miter")]
    [InlineData("stroke-miterlimit", "2", "4")]
    [InlineData("stroke-dasharray", "4 2", "none")]
    [InlineData("stroke-dashoffset", "2", "0")]
    [InlineData("stroke-opacity", "0.4", "1")]
    public void Initial_stroke_values_override_parent_and_lower_declarations(string property, string parent, string initial)
    {
        string geometry = "<path d='M4 4L20 4L8 12' fill='none' "
            + (property == "stroke" ? "" : "stroke='black' ")
            + (property == "stroke-width" ? "" : "stroke-width='4' ")
            + (property == "stroke-dasharray" ? "" : "stroke-dasharray='4 2' ");
        string prefix = "<svg xmlns='http://www.w3.org/2000/svg' width='40' height='40'><g " + property + "='" + parent + "'>" + geometry;
        string suffix = "/></g></svg>";
        Assert.Equal(RenderPixels(prefix + property + "='" + initial + "'" + suffix, out _, out _),
            RenderPixels(prefix + property + "='" + parent + "' style='" + property + ":initial!important'" + suffix, out _, out _));
    }

    [Trait("Layer", "Mechanism")]
    [Fact]
    public void Rejects_empty_important_declarations()
    {
        SvgException exception = Assert.Throws<SvgException>(() => SvgInlineStyleParser.Parse("fill: !important", CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, exception.ErrorCode);
    }

    [Trait("Layer", "Contract")]
    [Fact]
    public void Reserves_stylesheet_text_storage_before_incremental_reading()
    {
        using XmlReader reader = XmlReader.Create(new StringReader("<style>" + new string(' ', 4_096) + "</style>"));
        Assert.True(reader.Read());

        SvgException exception = Assert.Throws<SvgException>(() => SvgStyleSheet.ReadElementText(reader, new RenderBudget(new HostMemoryBudget(8_223)), CancellationToken.None));

        Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode);
    }
    [Trait("Layer", "Rendering")]
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Late_stylesheets_apply_to_earlier_content(bool inDefinitions)
    {
        string sheet = "<style>#box { fill: #025bc7; }</style>";
        if (inDefinitions) sheet = "<defs>" + sheet + "</defs>";
        byte[] actual = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><rect id='box' width='8' height='8'/>" + sheet + "</svg>", out _, out _);
        byte[] expected = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><rect width='8' height='8' fill='#025bc7'/></svg>", out _, out _);
        Assert.Equal(expected, actual);
    }

    [Trait("Layer", "Rendering")]
    [Theory]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(2, true)]
    [InlineData(4, false)]
    [InlineData(4, true)]
    public void Late_prefixed_stylesheets_use_the_xml_input_encoding(int stride, bool bigEndian)
    {
        System.Text.Encoding encoding = stride switch { 2 => bigEndian ? System.Text.Encoding.BigEndianUnicode : System.Text.Encoding.Unicode, 4 => new System.Text.UTF32Encoding(bigEndian, true, true), _ => System.Text.Encoding.UTF8 };
        string xml = "<s:svg xmlns:s='http://www.w3.org/2000/svg' width='8' height='8'><s:rect id='box' width='8' height='8'/><s:style>#box{fill:blue}</s:style></s:svg>";
        byte[] input = encoding.GetPreamble().Concat(encoding.GetBytes(xml)).ToArray();
        byte[] expected = SvgConverter.ConvertToPng("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><rect width='8' height='8' fill='blue'/></svg>"u8.ToArray(), SvgRenderOptions.Default, CancellationToken.None).ToArray();
        Assert.Equal(expected, SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None).ToArray());
    }

    [Trait("Layer", "Contract")]
    [Fact]
    public void Partial_late_stylesheet_rejection_releases_parser_storage()
    {
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(1_000_000));
        byte[] input = "<svg xmlns='http://www.w3.org/2000/svg'><rect id='box' width='8' height='8'/><style>#box{fill:blue}.box:hover{fill:red}</style></svg>"u8.ToArray();
        SvgException error = Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None));
        Assert.Equal(SvgErrorCode.UnsupportedFeature, error.ErrorCode);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Trait("Layer", "Mechanism")]
    [Theory]
    [InlineData("<style/>", false)]
    [InlineData("<style media='screen'/>", true)]
    [InlineData("<style type='text/css'/>", false)]
    [InlineData("<style id='sheet'/>", false)]
    [InlineData("<style type='text/css' id='sheet'/>", false)]
    [InlineData("<style type='text/plain'/>", true)]
    [InlineData("<style xmlns='http://www.w3.org/2000/svg'/>", false)]
    [InlineData("<s:style xmlns:s='http://www.w3.org/2000/svg' type='text/css'/>", false)]
    [InlineData("<style xmlns:metadata='urn:metadata' id='sheet'/>", false)]
    [InlineData("<style xmlns='http://www.w3.org/2000/svg' media='screen'/>", true)]
    [InlineData("<style xmlns='http://www.w3.org/2000/svg' type='text/plain'/>", true)]
    public void Rejects_stylesheet_element_attributes(string element, bool throws)
    {
        using XmlReader reader = XmlReader.Create(new StringReader(element));
        Assert.True(reader.Read());
        if (throws)
        {
            SvgException exception = Assert.Throws<SvgException>(() => SvgRootStyleSheetParser.ThrowIfAttributed(reader));

            Assert.Equal(SvgErrorCode.UnsupportedFeature, exception.ErrorCode);
        }
        else
        {
            SvgRootStyleSheetParser.ThrowIfAttributed(reader);
            Assert.Equal(XmlNodeType.Element, reader.NodeType);
            Assert.Equal("style", reader.LocalName);
        }
    }

    [Trait("Layer", "Rendering")]
    [Fact]
    public void Defs_stylesheet_class_applies_to_polygon()
    {
        // Definition stylesheets accumulate exactly like root stylesheets.
        byte[] pixels = RenderPixels(
            "<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'>" +
            "<defs><style>.a{fill:#00ff00;}</style></defs>" +
            "<polygon points='0,0 8,0 8,8 0,8' class='a'/></svg>",
            out int width, out int height);
        Assert.Equal(8, width);
        Assert.Equal(8, height);
        Assert.Equal(new SvgColor(0, 255, 0, 255), Pixel(pixels, width, 4, 4));
    }

    [Trait("Layer", "Rendering")]
    [Fact]
    public void Defs_stylesheet_wins_source_order_over_root_stylesheet()
    {
        // Defs stylesheets parse after the root stylesheet, so ties fall to defs.
        byte[] blue = RenderPixels(
            "<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'>" +
            "<style>.a{fill:#ff0000;}</style><defs><style>.a{fill:#0000ff;}</style></defs>" +
            "<polygon points='0,0 8,0 8,8 0,8' class='a'/></svg>",
            out _, out _);
        Assert.Equal(new SvgColor(0, 0, 255, 255), Pixel(blue, 8, 4, 4));
    }

    [Trait("Layer", "Rendering")]
    [Fact]
    public void Root_and_late_definition_stylesheets_keep_source_order()
    {
        byte[] actual = RenderPixels(
            "<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'>" +
            "<style>.a{fill:red !important}</style><polygon class='a' points='0,0 8,0 8,8 0,8'/>" +
            "<defs><style>.a{fill:#00ff00 !important}</style></defs><style>.a{fill:blue !important}</style></svg>", out _, out _);
        byte[] expected = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><polygon points='0,0 8,0 8,8 0,8' fill='blue'/></svg>", out _, out _);
        Assert.Equal(expected, actual);
    }

}
