using System.Xml;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgStyleSheetTests
{
    [Fact]
    public void Cascades_each_presentation_property_by_specificity_then_source_order()
    {
        SvgStyleCascade cascade = new();
        cascade.Apply(new InlineStyle("red", "red", "0.25", null, "none", null, "0.5", null), 1);
        cascade.Apply(new InlineStyle(null, "blue", null, "none", "inline", "hidden", "0.75", "evenodd"), 1);
        cascade.Apply(new InlineStyle(null, "green", "0.5", null, null, null, null, null), 0);
        cascade.Apply(new InlineStyle(null, null, "0.5", null, null, null, null, null), 2);

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

    [Fact]
    public void Cascades_css_whitespace_by_specificity_then_source_order()
    {
        SvgStyleCascade cascade = new();
        cascade.Apply(new InlineStyle(null, null, null, null, null, null, null, null) { WhiteSpace = "pre" }, 1);
        cascade.Apply(new InlineStyle(null, null, null, null, null, null, null, null) { WhiteSpace = "normal" }, 0);
        cascade.Apply(new InlineStyle(null, null, null, null, null, null, null, null) { WhiteSpace = "normal" }, 1);

        Assert.Equal("normal", cascade.ToInlineStyle().WhiteSpace);
    }

    [Fact]
    public void Important_stylesheet_declarations_beat_higher_specificity_normal_declarations()
    {
        SvgStyleCascade cascade = new();
        cascade.Apply(new InlineStyle(null, "red", null, null, null, null, null, null), 100);
        cascade.Apply(new InlineStyle(null, "blue", null, null, null, null, null, null) { Important = InlineImportantProperties.Fill }, 1);

        InlineStyle style = cascade.ToInlineStyle();

        Assert.Equal("blue", style.Fill);
        Assert.Equal(InlineImportantProperties.Fill, style.Important);
    }

    [Fact]
    public void Later_important_stylesheet_declarations_win_source_order_ties()
    {
        SvgStyleCascade cascade = new();
        cascade.Apply(new InlineStyle(null, "red", null, null, null, null, null, null) { Important = InlineImportantProperties.Fill }, 1);
        cascade.Apply(new InlineStyle(null, "blue", null, null, null, null, null, null) { Important = InlineImportantProperties.Fill }, 1);

        Assert.Equal("blue", cascade.ToInlineStyle().Fill);
    }

    [Fact]
    public void Normal_stylesheet_declarations_do_not_override_earlier_important_ones()
    {
        SvgStyleCascade cascade = new();
        cascade.Apply(new InlineStyle(null, "red", null, null, null, null, null, null) { Important = InlineImportantProperties.Fill }, 1);
        cascade.Apply(new InlineStyle(null, "blue", null, null, null, null, null, null), 101);

        Assert.Equal("red", cascade.ToInlineStyle().Fill);
    }

    [Theory]
    [InlineData("fill: red !important", "red", true)]
    [InlineData("fill: red!important", "red", true)]
    [InlineData("fill: red !IMPORTANT", "red", true)]
    [InlineData("fill: red ! important", "red", true)]
    [InlineData("fill: red", "red", false)]
    public void Parses_important_suffix_variants(string declaration, string expectedValue, bool expectedImportant)
    {
        InlineStyle style = SvgInlineStyleParser.Parse(declaration, new ParserWorkCounter(100), CancellationToken.None);

        Assert.Equal(expectedValue, style.Fill);
        Assert.Equal(expectedImportant, (style.Important & InlineImportantProperties.Fill) != 0);
    }

    [Fact]
    public void Important_beats_normal_within_one_declaration_block_regardless_of_order()
    {
        InlineStyle importantFirst = SvgInlineStyleParser.Parse("fill: red !important; fill: blue", new ParserWorkCounter(100), CancellationToken.None);
        InlineStyle importantLast = SvgInlineStyleParser.Parse("fill: red; fill: blue !important", new ParserWorkCounter(100), CancellationToken.None);

        Assert.Equal("red", importantFirst.Fill);
        Assert.Equal("blue", importantLast.Fill);
    }

    [Theory]
    [InlineData("fill", "inherit", null)]
    [InlineData("fill", "initial", "black")]
    [InlineData("fill", "unset", null)]
    [InlineData("color", "INHERIT", null)]
    [InlineData("fill-opacity", "initial", "1")]
    [InlineData("fill-opacity", "unset", null)]
    [InlineData("stroke", "initial", "none")]
    [InlineData("stroke", "unset", "none")]
    [InlineData("display", "initial", "inline")]
    [InlineData("visibility", "unset", null)]
    [InlineData("opacity", "initial", "1")]
    [InlineData("fill-rule", "unset", "nonzero")]
    public void Maps_paint_keywords_to_specified_values(string property, string value, string? expected)
    {
        InlineStyle style = SvgInlineStyleParser.Parse(property + ": " + value, new ParserWorkCounter(100), CancellationToken.None);

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

    [Theory]
    [InlineData("stroke: inherit")]
    [InlineData("display: inherit")]
    [InlineData("opacity: inherit")]
    [InlineData("fill-rule: inherit")]
    public void Rejects_unplumbed_inherit_declarations(string declaration)
    {
        SvgException exception = Assert.Throws<SvgException>(() => SvgInlineStyleParser.Parse(declaration, new ParserWorkCounter(100), CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, exception.ErrorCode);
    }

    [Fact]
    public void Rejects_empty_important_declarations()
    {
        SvgException exception = Assert.Throws<SvgException>(() => SvgInlineStyleParser.Parse("fill: !important", new ParserWorkCounter(100), CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, exception.ErrorCode);
    }

    [Fact]
    public void Checks_stylesheet_read_work_before_allocating_text_storage()
    {
        using XmlReader reader = XmlReader.Create(new StringReader("<style>x</style>"));
        Assert.True(reader.Read());
        RenderBudget budget = new(1);

        SvgException exception = Assert.Throws<SvgException>(() => SvgStyleSheet.ReadElementText(reader, budget, new ParserWorkCounter(0), CancellationToken.None));

        Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode);
        Assert.Equal(0, budget.Snapshot().BytesByCategory[(int)SvgMemoryCategory.Parser]);
    }

    [Fact]
    public void Reserves_stylesheet_text_storage_before_incremental_reading()
    {
        using XmlReader reader = XmlReader.Create(new StringReader("<style>" + new string(' ', 4_096) + "</style>"));
        Assert.True(reader.Read());

        SvgException exception = Assert.Throws<SvgException>(() => SvgStyleSheet.ReadElementText(reader, new RenderBudget(8_223), new ParserWorkCounter(100), CancellationToken.None));

        Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode);
    }
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void Rejects_a_late_root_stylesheet(bool sawStyleableNode, bool throws)
    {
        if (throws)
        {
            SvgException exception = Assert.Throws<SvgException>(() => SvgRootStyleSheetParser.ThrowIfLate(sawStyleableNode));

            Assert.Equal(SvgErrorCode.UnsupportedFeature, exception.ErrorCode);
        }
        else
        {
            SvgRootStyleSheetParser.ThrowIfLate(sawStyleableNode);
        }
    }

    [Theory]
    [InlineData("<style/>", false)]
    [InlineData("<style media='screen'/>", true)]
    [InlineData("<style type='text/css'/>", false)]
    [InlineData("<style id='sheet'/>", false)]
    [InlineData("<style type='text/css' id='sheet'/>", false)]
    [InlineData("<style type='text/plain'/>", true)]
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
        }
    }

    [Fact]
    public void Defs_stylesheet_class_applies_to_polygon()
    {
        // S9: definition stylesheets accumulate exactly like root stylesheets.
        byte[] pixels = RenderPixels(
            "<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'>" +
            "<defs><style>.a{fill:#00ff00;}</style></defs>" +
            "<polygon points='0,0 8,0 8,8 0,8' class='a'/></svg>",
            out int width, out int height);
        Assert.Equal(8, width);
        Assert.Equal(8, height);
        Assert.Equal(new SvgColor(0, 255, 0, 255), Pixel(pixels, width, 4, 4));
    }

    [Fact]
    public void Defs_stylesheet_wins_source_order_over_root_stylesheet()
    {
        // S9: defs stylesheets parse after the root stylesheet, so ties fall to defs.
        byte[] blue = RenderPixels(
            "<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'>" +
            "<style>.a{fill:#ff0000;}</style><defs><style>.a{fill:#0000ff;}</style></defs>" +
            "<polygon points='0,0 8,0 8,8 0,8' class='a'/></svg>",
            out _, out _);
        Assert.Equal(new SvgColor(0, 0, 255, 255), Pixel(blue, 8, 4, 4));
    }

    [Fact]
    public void Late_defs_stylesheet_stays_unsupported()
    {
        // S9: streaming cannot apply rules parsed after content, so late stylesheets reject.
        byte[] input = System.Text.Encoding.UTF8.GetBytes(
            "<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'>" +
            "<polygon points='0,0 8,0 8,8 0,8' fill='red'/>" +
            "<defs><style>.a{fill:#00ff00;}</style></defs></svg>");
        SvgException rejected = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None));
        Assert.Equal(SvgErrorCode.UnsupportedFeature, rejected.ErrorCode);
    }

    private static byte[] RenderPixels(string svg, out int width, out int height)
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes(svg);
        ReadOnlyMemory<byte> png = SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None);
        return DecodeRgba(png, out width, out height);
    }

    private static byte[] DecodeRgba(ReadOnlyMemory<byte> png, out int width, out int height)
    {
        width = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.Span.Slice(16, 4));
        height = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.Span.Slice(20, 4));
        using System.IO.MemoryStream compressed = new();
        for (int offset = 8; offset < png.Length;)
        {
            int length = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.Span.Slice(offset, 4));
            if (png.Span.Slice(offset + 4, 4).SequenceEqual("IDAT"u8)) compressed.Write(png.Span.Slice(offset + 8, length));
            offset += length + 12;
        }
        compressed.Position = 0;
        using System.IO.Compression.ZLibStream zlib = new(compressed, System.IO.Compression.CompressionMode.Decompress);
        byte[] raw = new byte[checked((width * 4 + 1) * height)];
        zlib.ReadExactly(raw);
        byte[] pixels = new byte[checked(width * 4 * height)];
        for (int y = 0; y < height; y++)
        {
            if (raw[y * (width * 4 + 1)] != 0) throw new InvalidDataException("Test PNG decoder expects an unfiltered scanline.");
            raw.AsSpan(y * (width * 4 + 1) + 1, width * 4).CopyTo(pixels.AsSpan(y * width * 4, width * 4));
        }
        return pixels;
    }

    private static SvgColor Pixel(byte[] pixels, int width, int x, int y)
    {
        int offset = (y * width + x) * 4;
        return new SvgColor(pixels[offset], pixels[offset + 1], pixels[offset + 2], pixels[offset + 3]);
    }
}
