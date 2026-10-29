using System.Text;
using Lokad.Svg.Fonts;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgDoctypeTests
{
    private const string RedSvg = "<svg xmlns='http://www.w3.org/2000/svg' width='2' height='2' viewBox='0 0 1 1' preserveAspectRatio='none'><rect width='1' height='1' fill='red'/></svg>";

    [Theory]
    [InlineData("utf8")]
    [InlineData("utf8-bom")]
    [InlineData("utf16le-bom")]
    [InlineData("utf16be-bom")]
    [InlineData("utf16le")]
    [InlineData("utf16be")]
    [InlineData("utf32le-bom")]
    [InlineData("utf32be-bom")]
    [InlineData("latin1")]
    public void Padded_declarations_preserve_encoding_and_rendering(string encoding)
    {
        byte[] original = Encode("<!DOCTYPE svg>" + RedSvg, encoding, true);
        byte[] padded = Encode("<!DOCTYPE svg>" + RedSvg, encoding, true, new string(' ', 20_000) + "\t\r\n");
        Assert.Equal(SvgConverter.ConvertToPng(original, SvgRenderOptions.Default, CancellationToken.None).ToArray(),
            SvgConverter.ConvertToPng(padded, SvgRenderOptions.Default, CancellationToken.None).ToArray());
        Assert.Empty(SvgFontRequestDiscovery.Discover(padded, CancellationToken.None));
    }

    [Theory]
    [InlineData("<?xml {0}version='1.0' encoding='ASCII'{0}?>", true)]
    [InlineData("<?xml{0}version{0}={0}\"1.0\"{0}encoding='iso-8859-1'{0}standalone='yes'{0}?>", true)]
    [InlineData("<?xml{0}version='1.0' encoding='utf-{0}8'?>", false)]
    [InlineData("<?xml{0}version='1.{0}0'?>", false)]
    [InlineData("<?xml{0}version='1.0' standalone='y{0}es'?>", false)]
    [InlineData("<?xml{0}version='1.0'encoding='UTF-8'?>", false)]
    [InlineData("<?xml{0}encoding='UTF-8' version='1.0'?>", false)]
    [InlineData("<?xml{0}version='1.0' version='1.0'?>", false)]
    [InlineData("<?xml{0}version='1.0'", false)]
    public void Declaration_compaction_preserves_grammar_and_quoted_values(string declaration, bool admitted)
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes(string.Format(System.Globalization.CultureInfo.InvariantCulture,
            declaration, new string(' ', 4096)) + "<!DOCTYPE svg>" + RedSvg);
        if (admitted)
            Assert.Equal(SvgConverter.ConvertToPng(System.Text.Encoding.UTF8.GetBytes(RedSvg), SvgRenderOptions.Default, CancellationToken.None).ToArray(),
                SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None).ToArray());
        else
            Assert.Equal(SvgErrorCode.MalformedInput, Assert.Throws<SvgException>(() =>
                SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None)).ErrorCode);
    }

    [Fact]
    public void Long_declaration_tokens_reserve_storage_before_the_encoding_probe()
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<?xml version='1.0' encoding='" + new string('x', 10_000) + "'?>" + RedSvg);
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(32 * 1024));
        SvgException denied = Assert.Throws<SvgException>(() => SvgXmlReaderPolicy.Create(input,
            SvgRenderLimits.Default, budget, CancellationToken.None));
        Assert.Equal(SvgErrorCode.LimitExceeded, denied.ErrorCode);
        Assert.Equal(1, budget.Snapshot().DeniedReservationCount);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    public static IEnumerable<object[]> Documents()
    {
        (string Prolog, bool Admitted)[] cases =
        [
            ("<!DOCTYPE svg>", true),
            ("<!DOCTYPE svg SYSTEM 'https://example.invalid/svg.dtd'>", true),
            ("<!DOCTYPE svg SYSTEM \"file:///does-not-exist.dtd\">", true),
            ("<!DOCTYPE svg PUBLIC '-//W3C//DTD SVG 1.1//EN' 'https://example.invalid/svg.dtd'>", true),
            ("<!DOCTYPE svg SYSTEM 'https://example.invalid/[literal].dtd'>", true),
            ("<!DOCTYPE\nsvg\tSYSTEM\r\n'uri' >", true),
            ("<!-- <!DOCTYPE svg [fake]> -->", true),
            ("<?note <!DOCTYPE svg [fake]> ?>", true),
            ("<!DOCTYPE svg []>", false),
            ("<!DOCTYPE svg [<!ENTITY x 'red'>]>", false),
            ("<!DOCTYPE svg [<!ENTITY x SYSTEM 'file:///does-not-exist'>]>", false),
            ("<!DOCTYPE svg [<!ENTITY % x '<!ENTITY y \"red\">'>%x;]>", false),
            ("<!DOCTYPE svg><!DOCTYPE svg>", false),
            ("<!DOCTYPE 123>", false),
            ("<!DOCTYPE svg SYSTEM>", false),
            ("<!DOCTYPE svg SYSTEM 'uri>", false)
        ];
        foreach (string encoding in new[] { "utf8", "utf8-bom", "utf16le-bom", "utf16be-bom", "utf16le", "utf16be", "utf32le-bom", "utf32be-bom", "latin1" })
        {
            foreach ((string prolog, bool admitted) in cases) yield return [prolog + RedSvg, encoding, admitted];
            yield return [RedSvg + "<!DOCTYPE svg>", encoding, false];
            yield return ["<!DOCTYPE svg>" + RedSvg.Replace("fill='red'", "fill='&external;'", StringComparison.Ordinal), encoding, false];
        }
    }

    [Theory]
    [MemberData(nameof(Documents))]
    public void Root_discovery_render_and_scale_probe_share_the_encoding_and_declaration_policy(string document, string encoding, bool admitted)
    {
        byte[] input = Encode(document, encoding, true);
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(268_435_456L));
        if (admitted)
        {
            using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None)) Assert.Single(root.Rectangles);
            Assert.Empty(SvgFontRequestDiscovery.Discover(input, SvgRenderLimits.Default, budget, CancellationToken.None));
            SvgAffineTransform map = SvgPreparationTestHelper.GetOutputMap(input, new SvgRenderOptions(new SvgViewport(4, 6), 1, SvgColor.Transparent, SvgRenderLimits.Default), budget, CancellationToken.None);
            Assert.Equal((4.0, 6.0), (map.A, map.D));
            Assert.Equal(SvgConverter.ConvertToPng(System.Text.Encoding.UTF8.GetBytes(RedSvg), SvgRenderOptions.Default, CancellationToken.None).ToArray(), SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None).ToArray());
        }
        else
        {
            Assert.Equal(SvgErrorCode.MalformedInput, Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None)).ErrorCode);
            Assert.Equal(SvgErrorCode.MalformedInput, Assert.Throws<SvgException>(() => SvgFontRequestDiscovery.Discover(input, SvgRenderLimits.Default, budget, CancellationToken.None)).ErrorCode);
            Assert.Equal(SvgErrorCode.MalformedInput, Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None)).ErrorCode);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Theory]
    [InlineData("utf8")]
    [InlineData("utf8-bom")]
    [InlineData("utf16le-bom")]
    [InlineData("utf16be-bom")]
    [InlineData("utf32le-bom")]
    [InlineData("utf32be-bom")]
    public void Bom_and_default_encoding_do_not_require_an_xml_declaration(string encoding)
    {
        byte[] admitted = Encode("<!--lookalike <!DOCTYPE fake []> --><?note ???><!DOCTYPE svg SYSTEM '[literal]'>" + RedSvg, encoding, false);
        Assert.Equal(SvgConverter.ConvertToPng(System.Text.Encoding.UTF8.GetBytes(RedSvg), SvgRenderOptions.Default, CancellationToken.None).ToArray(), SvgConverter.ConvertToPng(admitted, SvgRenderOptions.Default, CancellationToken.None).ToArray());
        byte[] rejected = Encode("<!--before--><!DOCTYPE svg []>" + RedSvg, encoding, false);
        Assert.Equal(SvgErrorCode.MalformedInput, Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(rejected, SvgRenderOptions.Default, CancellationToken.None)).ErrorCode);
    }

    [Theory]
    [InlineData("input")]
    [InlineData("nodes")]
    [InlineData("depth")]
    [InlineData("attributes")]
    public void Inert_declaration_preserves_input_and_structure_limits(string limit)
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<!DOCTYPE svg><svg xmlns='http://www.w3.org/2000/svg' width='2' height='2'><g><rect width='2' height='2'/></g></svg>");
        SvgRenderLimits limits = new(limit == "input" ? input.Length - 1 : 1024, 16, 256, 1024, limit == "nodes" ? 1 : 16, 16, limit == "depth" ? 1 : 16, limit == "attributes" ? 1 : 16);
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(1_048_576));
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, limits, budget, CancellationToken.None)).ErrorCode);
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgFontRequestDiscovery.Discover(input, limits, budget, CancellationToken.None)).ErrorCode);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Theory]
    [InlineData("<!DOCTYPE svg><svg xmlns='http://www.w3.org/2000/svg'><!DOCTYPE svg></svg>")]
    [InlineData("<!DOCTYPE svg><!--bad---><!DOCTYPE svg><svg xmlns='http://www.w3.org/2000/svg'/>")]
    [InlineData("<!DOCTYPE svg><?note ???><!DOCTYPE svg><svg xmlns='http://www.w3.org/2000/svg'/>")]
    public void Late_duplicate_and_malformed_prolog_syntax_remains_malformed(string document)
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes(document);
        Assert.Equal(SvgErrorCode.MalformedInput, Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, CancellationToken.None)).ErrorCode);
        Assert.Equal(SvgErrorCode.MalformedInput, Assert.Throws<SvgException>(() => SvgFontRequestDiscovery.Discover(input, CancellationToken.None)).ErrorCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Invalid_utf32_prolog_bytes_keep_the_malformed_input_category(bool bigEndian)
    {
        UTF32Encoding encoding = new(bigEndian, true, true);
        byte[] input = [.. encoding.GetPreamble(), .. encoding.GetBytes("<!--x--><!DOCTYPE svg>" + RedSvg)];
        Span<byte> invalidScalar = input.AsSpan(encoding.GetPreamble().Length + 4 * 4, 4);
        if (bigEndian) System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(invalidScalar, 0x110000);
        else System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(invalidScalar, 0x110000);
        Assert.Equal(SvgErrorCode.MalformedInput, Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, CancellationToken.None)).ErrorCode);
        Assert.Equal(SvgErrorCode.MalformedInput, Assert.Throws<SvgException>(() => SvgFontRequestDiscovery.Discover(input, CancellationToken.None)).ErrorCode);
    }

    [Fact]
    public void External_dtd_contents_do_not_supply_entities_or_default_attributes()
    {
        string path = Path.Combine(Path.GetTempPath(), "lokad-svg-doctype-" + Guid.NewGuid().ToString("N") + ".dtd");
        File.WriteAllText(path, "<!ENTITY external 'red'><!ATTLIST rect fill CDATA 'blue'>");
        try
        {
            string prolog = "<!DOCTYPE svg SYSTEM '" + new Uri(path).AbsoluteUri + "'>";
            string document = RedSvg.Replace(" fill='red'", "", StringComparison.Ordinal);
            Assert.Equal(SvgConverter.ConvertToPng(System.Text.Encoding.UTF8.GetBytes(document), SvgRenderOptions.Default, CancellationToken.None).ToArray(), SvgConverter.ConvertToPng(System.Text.Encoding.UTF8.GetBytes(prolog + document), SvgRenderOptions.Default, CancellationToken.None).ToArray());
            byte[] entity = System.Text.Encoding.UTF8.GetBytes(prolog + RedSvg.Replace("fill='red'", "fill='&external;'", StringComparison.Ordinal));
            Assert.Equal(SvgErrorCode.MalformedInput, Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(entity, SvgRenderOptions.Default, CancellationToken.None)).ErrorCode);
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData("<!DOCTYPE svg>")]
    [InlineData("<!DOCTYPE svg SYSTEM 'file:///does-not-exist.dtd'>")]
    [InlineData("<!DOCTYPE svg PUBLIC 'identifier' 'https://example.invalid/svg.dtd'>")]
    public void Inert_declaration_preserves_font_requests(string prolog)
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes(prolog + "<svg xmlns='http://www.w3.org/2000/svg'><text font-family='Example'>x</text></svg>");
        Assert.Equal("Example", Assert.Single(SvgFontRequestDiscovery.Discover(input, CancellationToken.None)).Family);
    }

    [Fact]
    public async Task Inert_declaration_preserves_all_six_png_outputs()
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<!DOCTYPE svg SYSTEM 'file:///does-not-exist.dtd'>" + RedSvg);
        byte[] expected = SvgConverter.ConvertToPng(System.Text.Encoding.UTF8.GetBytes(RedSvg), SvgRenderOptions.Default, CancellationToken.None).ToArray();
        Assert.Equal(expected, SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None).ToArray());
        byte[] destination = new byte[expected.Length];
        Assert.Equal(expected.Length, SvgConverter.ConvertToPng(input, destination, SvgRenderOptions.Default, CancellationToken.None));
        Assert.Equal(expected, destination);
        using MemoryStream stream = new();
        Assert.Equal(expected.Length, SvgConverter.ConvertToPng(input, stream, SvgRenderOptions.Default, CancellationToken.None));
        Assert.Equal(expected, stream.ToArray());
        Assert.Equal(expected, (await SvgConverter.ConvertToPngAsync(input, SvgRenderOptions.Default, CancellationToken.None)).ToArray());
        Assert.Equal(expected.Length, await SvgConverter.ConvertToPngAsync(input, destination, SvgRenderOptions.Default, CancellationToken.None));
        Assert.Equal(expected, destination);
        using MemoryStream asyncStream = new();
        Assert.Equal(expected.Length, await SvgConverter.ConvertToPngAsync(input, asyncStream, SvgRenderOptions.Default, CancellationToken.None));
        Assert.Equal(expected, asyncStream.ToArray());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Long_prolog_work_denial_and_cancellation_release_all_owned_storage(bool discovery)
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<!--" + new string('x', 100_000) + "--><!DOCTYPE svg>" + RedSvg);
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(268_435_456L));
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        using CancellationTokenSource cancellation = new();
        int checkpoint = -1;
        using IDisposable observer = SvgXmlPrologProbe.Arm(position =>
        {
            if (position >= 4096)
            {
                checkpoint = position;
                Assert.True(budget.Snapshot().CurrentBytes > SvgRootParser.GetXmlReaderWorkspaceBytes(input.Length, SvgRenderLimits.Default.MaximumAttributesPerElement));
                cancellation.Cancel();
            }
        });
        OperationCanceledException cancelled = Assert.Throws<OperationCanceledException>(() => Run(cancellation.Token));
        Assert.Equal(cancellation.Token, cancelled.CancellationToken);
        Assert.Equal(4096, checkpoint);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);

        void Run(CancellationToken token)
        {
            if (discovery) SvgFontRequestDiscovery.Discover(input, SvgRenderLimits.Default, budget, token);
            else { using SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, null, token); }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Padded_declaration_work_denial_and_cancellation_release_storage(bool discovery)
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<?xml version='1.0'" + new string(' ', 100_000) + "?>" + RedSvg);
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(268_435_456L));
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        using CancellationTokenSource cancellation = new();
        int checkpoint = -1;
        using IDisposable observer = SvgXmlPrologProbe.Arm(position =>
        {
            if (position >= 4096)
            {
                checkpoint = position;
                cancellation.Cancel();
            }
        });
        Assert.Equal(cancellation.Token, Assert.Throws<OperationCanceledException>(() => Run(cancellation.Token)).CancellationToken);
        Assert.Equal(4096, checkpoint);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);

        void Run(CancellationToken token)
        {
            if (discovery) SvgFontRequestDiscovery.Discover(input, SvgRenderLimits.Default, budget, token);
            else { using SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, null, token); }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Prolog_workspace_is_reserved_before_allocation_and_released_on_denial(bool discovery)
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<!DOCTYPE svg>" + RedSvg);
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(SvgRootParser.GetXmlReaderWorkspaceBytes(input.Length, SvgRenderLimits.Default.MaximumAttributesPerElement)));
        SvgException denied = Assert.Throws<SvgException>(() =>
        {
            if (discovery) SvgFontRequestDiscovery.Discover(input, SvgRenderLimits.Default, budget, CancellationToken.None);
            else { using SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None); }
        });
        Assert.Equal(SvgErrorCode.LimitExceeded, denied.ErrorCode);
        Assert.Equal(1, budget.Snapshot().DeniedReservationCount);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Guard_allocation_is_bounded_for_long_prolog_padding(bool declaration)
    {
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(268_435_456L));
        byte[] small = System.Text.Encoding.UTF8.GetBytes((declaration ? "<?xml version='1.0' ?>" : "<!--x--><!DOCTYPE svg SYSTEM 'uri'>") + RedSvg);
        byte[] large = System.Text.Encoding.UTF8.GetBytes((declaration ? "<?xml version='1.0'" + new string(' ', 1_000_000) + "?>"
            : "<!--" + new string('x', 1_000_000) + "--><!DOCTYPE svg SYSTEM '" + new string('y', 1_000_000) + "'>") + RedSvg);
        _ = Measure(small);
        _ = Measure(large);
        long smallBytes = Measure(small);
        long largeBytes = Measure(large);
        Assert.InRange(largeBytes, 0, smallBytes + 16_384);
        Assert.InRange(largeBytes, 0, 32 * 1024);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);

        long Measure(byte[] input)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            SvgXmlReaderPolicy.Create(input, SvgRenderLimits.Default, budget, CancellationToken.None);
            return GC.GetAllocatedBytesForCurrentThread() - before;
        }
    }

    private static byte[] Encode(string document, string name, bool includeDeclaration) => Encode(document, name, includeDeclaration, " ");

    private static byte[] Encode(string document, string name, bool includeDeclaration, string padding)
    {
        (System.Text.Encoding encoding, bool bom, string declaration) = name switch
        {
            "utf8" => (new UTF8Encoding(false, true), false, "UTF-8"),
            "utf8-bom" => (new UTF8Encoding(true, true), true, "UTF-8"),
            "utf16le-bom" => (new UnicodeEncoding(false, true, true), true, "UTF-16"),
            "utf16be-bom" => (new UnicodeEncoding(true, true, true), true, "UTF-16"),
            "utf16le" => (new UnicodeEncoding(false, false, true), false, "UTF-16LE"),
            "utf16be" => (new UnicodeEncoding(true, false, true), false, "UTF-16BE"),
            "utf32le-bom" => (new UTF32Encoding(false, true, true), true, "UTF-32"),
            "utf32be-bom" => (new UTF32Encoding(true, true, true), true, "UTF-32BE"),
            "latin1" => (System.Text.Encoding.Latin1, false, "ISO-8859-1"),
            _ => throw new ArgumentException("Unknown encoding.", nameof(name))
        };
        byte[] bytes = encoding.GetBytes((includeDeclaration ? "<?xml" + padding + "version='1.0'" + padding + "encoding='" + declaration + "'" + padding + "?><!--é-->" : "") + document);
        return bom ? [.. encoding.GetPreamble(), .. bytes] : bytes;
    }
}
