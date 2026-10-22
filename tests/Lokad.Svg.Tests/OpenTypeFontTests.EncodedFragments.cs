using Lokad.Svg.Fonts;

namespace Lokad.Svg.Tests;

public sealed partial class OpenTypeFontTests
{
    [Theory]
    [InlineData("\u00e9", "%c3%a9")]
    [InlineData("\ud83d\ude80", "%F0%9F%9A%80")]
    public async Task Utf8_fragments_select_literal_unicode_ids_without_selecting_encoded_collision_ids(string id, string encoded)
    {
        (var catalog, var provider, _) = CreateExamplePack(CreateTriangleFont());
        byte[] expected = Render(Document("<rect width='8' height='8' fill='red'/>"));
        foreach (string consumer in new[] { "use", "gradient", "pattern-href" })
        {
            string resources = Resource(id, "red") + Resource(encoded, "blue");
            if (consumer == "pattern-href") resources += "<pattern id='alias' href='#" + encoded + "'/>";
            string node = consumer == "use" ? "<use href='#" + encoded + "'/>"
                : "<rect width='8' height='8' fill='url(#" + (consumer == "pattern-href" ? "alias" : encoded) + ")'/>";
            byte[] input = Document("<defs>" + resources + "</defs>" + node);
            Assert.Equal(expected, Render(input));
            using SvgPreparedDocument prepared = await SvgPreparedDocument.CreateAsync(input, catalog, provider, SvgRenderOptions.Default, CancellationToken.None);
            Assert.Equal(expected, prepared.ConvertToPng(CancellationToken.None).ToArray());

            string Resource(string name, string color) => consumer switch
            {
                "use" => "<rect id='" + name + "' width='8' height='8' fill='" + color + "'/>",
                "gradient" => "<linearGradient id='" + name + "'><stop stop-color='" + color + "'/></linearGradient>",
                _ => "<pattern id='" + name + "' patternUnits='userSpaceOnUse' width='8' height='8'><rect width='8' height='8' fill='" + color + "'/></pattern>"
            };
        }
        Assert.Equal(0, provider.RequestCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Encoded_targets_use_the_same_resource_selection_before_font_acquisition(bool pattern)
    {
        string definitions = pattern
            ? "<pattern id='p' patternUnits='userSpaceOnUse' width='0' height='0'><rect width='8' height='8' fill='red'/></pattern><pattern id='%70' patternUnits='userSpaceOnUse' width='0' height='0'><text y='7' font-family='Missing' font-size='8'>A</text></pattern><pattern id='alias' href='#%70' width='8' height='8'/>"
            : "<rect id='s' width='8' height='8' fill='red'/><text id='%73' y='7' font-family='Missing' font-size='8'>A</text>";
        byte[] input = Document("<defs>" + definitions + "</defs>" + (pattern ? "<rect width='8' height='8' fill='url(#alias)'/>" : "<use href='#%73'/>"));
        byte[] expected = Render(Document("<rect width='8' height='8' fill='red'/>"));
        Assert.Equal(expected, Render(input));
        (var catalog, var provider, _) = CreateExamplePack(CreateTriangleFont());
        using SvgPreparedDocument prepared = await SvgPreparedDocument.CreateAsync(input, catalog, provider, SvgRenderOptions.Default, CancellationToken.None);
        Assert.Equal(expected, prepared.ConvertToPng(CancellationToken.None).ToArray());
        Assert.Equal(0, provider.RequestCount);
    }

    [Fact]
    public async Task Encoded_text_paths_resolve_the_decoded_path_in_raw_and_prepared_layout()
    {
        byte[] input = Document("<defs><path id='p' d='M0 7H8'/><path id='%70' d='M0 3H8'/></defs><text font-family='serif' font-size='8' fill='red'><textPath href='#%70'>A</textPath></text>");
        byte[] control = System.Text.Encoding.UTF8.GetBytes(System.Text.Encoding.UTF8.GetString(input).Replace("href='#%70'", "href='#p'", StringComparison.Ordinal));
        (var catalog, var provider, _) = CreateExamplePack(CreateTriangleFont());
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync(input, catalog, provider, CancellationToken.None);
        SvgRenderOptions options = new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, snapshot);
        byte[] expected = SvgConverter.ConvertToPng(control, options, CancellationToken.None).ToArray();
        Assert.Equal(expected, SvgConverter.ConvertToPng(input, options, CancellationToken.None).ToArray());
        using SvgPreparedDocument prepared = await SvgPreparedDocument.CreateAsync(input, catalog, provider, SvgRenderOptions.Default, CancellationToken.None);
        Assert.Equal(expected, prepared.ConvertToPng(CancellationToken.None).ToArray());
    }

    [Theory]
    [InlineData("%")]
    [InlineData("%7")]
    [InlineData("%GG")]
    [InlineData("%C3%28")]
    [InlineData("%ED%A0%80")]
    [InlineData("%C0%AF")]
    [InlineData("%F4%90%80%80")]
    [InlineData("%20s")]
    public async Task Invalid_fragment_encodings_do_not_bind_literal_encoded_ids(string spelling)
    {
        (var catalog, var provider, _) = CreateExamplePack(CreateTriangleFont());
        foreach (bool use in new[] { false, true })
        {
            byte[] input = Document(use ? "<defs><rect id='" + spelling + "' width='8' height='8'/></defs><use href='#" + spelling + "'/>"
                : "<defs><linearGradient id='" + spelling + "'><stop stop-color='red'/></linearGradient></defs><rect width='8' height='8' fill='url(#" + spelling + ")'/>");
            Assert.Equal(SvgErrorCode.UnsupportedFeature, Assert.Throws<SvgException>(() => Render(input)).ErrorCode);
            SvgException error = await Assert.ThrowsAsync<SvgException>(async () =>
            {
                using SvgPreparedDocument prepared = await SvgPreparedDocument.CreateAsync(input, catalog, provider, SvgRenderOptions.Default, CancellationToken.None);
                _ = prepared.ConvertToPng(CancellationToken.None);
            });
            Assert.Equal(SvgErrorCode.UnsupportedFeature, error.ErrorCode);
        }
        Assert.Equal(0, provider.RequestCount);
    }

    public static IEnumerable<object[]> EncodedLocalReferences()
    {
        foreach (string consumer in new[] { "use", "gradient", "pattern", "clip", "mask", "marker", "filter", "gradient-href", "pattern-href" })
        foreach (string spelling in new[] { "s", "%73", "%2573" })
        foreach (string context in new[] { "direct", "group", "symbol" })
        foreach (bool forward in new[] { false, true }) yield return [consumer, spelling, context, forward];
    }

    [Theory]
    [MemberData(nameof(EncodedLocalReferences))]
    public async Task Local_fragments_decode_once_before_selecting_a_resource(string consumer, string spelling, string context, bool forward)
    {
        string resources = Resource("s", "red") + Resource("%73", "blue") + Resource("%2573", "green");
        string color = spelling == "%2573" ? "blue" : "red";
        string node;
        if (consumer is "gradient-href" or "pattern-href")
        {
            resources += consumer == "gradient-href" ? "<linearGradient id='alias' href='#" + spelling + "'/>" : "<pattern id='alias' href='#" + spelling + "'/>";
            node = "<rect width='32' height='32' fill='url(#alias)'/>";
        }
        else node = consumer switch
        {
            "use" => "<use href='#" + spelling + "'/>",
            "clip" => "<rect width='32' height='32' fill='red' clip-path='url(#" + spelling + ")'/>",
            "mask" => "<rect width='32' height='32' fill='red' mask='url(#" + spelling + ")'/>",
            "marker" => "<path d='M8 8H24' marker-start='url(#" + spelling + ")'/>",
            "filter" => "<rect width='16' height='16' filter='url(#" + spelling + ")'/>",
            _ => "<rect width='32' height='32' fill='url(#" + spelling + ")'/>"
        };
        string control = consumer switch
        {
            "use" => "<rect x='" + (color == "red" ? "0" : "16") + "' width='16' height='32' fill='" + color + "'/>",
            "clip" or "mask" => "<rect x='" + (color == "red" ? "0" : "16") + "' width='16' height='32' fill='red'/>",
            "marker" => "<rect x='8' y='8' width='8' height='8' fill='" + color + "'/>",
            _ => "<rect width='32' height='32' fill='" + color + "'/>"
        };
        string definitions = "<defs>" + resources + "</defs>";
        byte[] input = Input(forward ? Wrap(node) + definitions : definitions + Wrap(node));
        (var catalog, var provider, _) = CreateExamplePack(CreateTriangleFont());
        if (consumer == "marker" && context == "symbol" && forward)
        {
            // Forward marker resources on retained symbol paths already have an
            // explicit admission boundary, independent of fragment spelling.
            Assert.Equal(SvgErrorCode.UnsupportedFeature, Assert.Throws<SvgException>(() => Render(input)).ErrorCode);
            SvgException error = await Assert.ThrowsAsync<SvgException>(async () =>
            {
                using SvgPreparedDocument rejected = await SvgPreparedDocument.CreateAsync(input, catalog, provider, SvgRenderOptions.Default, CancellationToken.None);
                _ = rejected.ConvertToPng(CancellationToken.None);
            });
            Assert.Equal(SvgErrorCode.UnsupportedFeature, error.ErrorCode);
            return;
        }
        byte[] expected = Render(Input(control));
        Assert.Equal(expected, Render(input));
        using SvgPreparedDocument prepared = await SvgPreparedDocument.CreateAsync(input, catalog, provider, SvgRenderOptions.Default, CancellationToken.None);
        Assert.Equal(expected, prepared.ConvertToPng(CancellationToken.None).ToArray());
        Assert.Equal(0, provider.RequestCount);

        string Resource(string id, string paint)
        {
            string rectangle = "<rect x='" + (paint == "red" ? "0" : "16") + "' width='16' height='32' fill='" + paint + "'/>";
            return consumer switch
            {
                "use" => rectangle.Replace("<rect", "<rect id='" + id + "'", StringComparison.Ordinal),
                "gradient" or "gradient-href" => "<linearGradient id='" + id + "'><stop stop-color='" + paint + "'/></linearGradient>",
                "pattern" or "pattern-href" => "<pattern id='" + id + "' patternUnits='userSpaceOnUse' width='4' height='4'><rect width='4' height='4' fill='" + paint + "'/></pattern>",
                "clip" => "<clipPath id='" + id + "'>" + rectangle + "</clipPath>",
                "mask" => "<mask id='" + id + "' mask-type='alpha'>" + rectangle + "</mask>",
                "marker" => "<marker id='" + id + "' markerUnits='userSpaceOnUse' markerWidth='8' markerHeight='8'><rect width='8' height='8' fill='" + paint + "'/></marker>",
                _ => "<filter id='" + id + "' filterUnits='userSpaceOnUse' x='0' y='0' width='32' height='32'><feFlood flood-color='" + paint + "'/></filter>"
            };
        }
        string Wrap(string content) => context switch
        {
            "group" => "<g>" + content + "</g>",
            "symbol" => "<defs><symbol id='host'>" + content + "</symbol></defs><use href='#host'/>",
            _ => content
        };
        static byte[] Input(string content) => System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'>" + content + "</svg>");
    }
}
