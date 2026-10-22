using Lokad.Svg.Fonts;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgEmptyFilterGroupTests
{
    private const string Filter = "<defs><filter id='f' filterUnits='userSpaceOnUse' x='0' y='0' width='32' height='32'><feFlood flood-color='red'/></filter></defs>";
    private const string Red = "<rect width='32' height='32' fill='red'/>";

    [Theory]
    [InlineData("filter='url(#outer)'")]
    [InlineData("clip-path='url(#box)'")]
    [InlineData("mask='url(#boxmask)'")]
    public void Retained_empty_filter_scopes_do_not_enlarge_ancestor_object_bounds(string attributes)
    {
        const string effects = "<defs><filter id='outer'><feOffset dx='0'/></filter><clipPath id='box' clipPathUnits='objectBoundingBox'><rect width='1' height='1'/></clipPath><mask id='boxmask' mask-type='alpha' maskUnits='objectBoundingBox' x='0' y='0' width='1' height='1' maskContentUnits='objectBoundingBox'><rect width='1' height='1' fill='white'/></mask></defs>";
        foreach (string ending in new[] { "/>", "></g>" })
        {
            string content = "<g filter='url(#f)'" + ending + "<rect x='8' y='8' width='8' height='8' fill='blue'/>";
            byte[] expected = attributes.StartsWith("clip-path", StringComparison.Ordinal)
                ? Render("<rect x='8' y='8' width='8' height='8' fill='blue'/>")
                : Render(Filter + effects + "<g " + attributes + ">" + content + "</g>");
            Assert.Equal(expected, Render(Filter + effects + "<g " + attributes + ">" + content + "</g>"));
            Assert.Equal(expected,
                Render(Filter + effects + "<defs><g id='s'>" + content + "</g></defs><g " + attributes + "><use href='#s'/></g>"));
        }
    }

    [Theory]
    [InlineData("/>")]
    [InlineData("></g>")]
    [InlineData("><g/></g>")]
    [InlineData("><desc>description</desc></g>")]
    public async Task Empty_filtered_groups_generate_output_through_raw_and_prepared_apis(string ending)
    {
        byte[] expected = Render(Red);
        foreach (bool forward in new[] { false, true })
        {
            string group = "<g filter='url(#f)'" + ending;
            byte[] input = Input(forward ? group + Filter : Filter + group);
            SvgFontPackCatalog catalog = UnusedCatalog();
            foreach (bool prepared in new[] { false, true })
            foreach (string api in new[] { "owned", "memory", "stream", "owned-async", "memory-async", "stream-async" })
            {
                using SvgPreparedDocument document = await SvgPreparedDocument.CreateAsync(input, catalog, new NoFonts(), SvgRenderOptions.Default, CancellationToken.None);
                byte[] destination = new byte[expected.Length];
                using MemoryStream stream = new();
                byte[] actual;
                switch (api)
                {
                    case "owned": actual = (prepared ? document.ConvertToPng(CancellationToken.None) : SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None)).ToArray(); break;
                    case "owned-async": actual = (prepared ? await document.ConvertToPngAsync(CancellationToken.None) : await SvgConverter.ConvertToPngAsync(input, SvgRenderOptions.Default, CancellationToken.None)).ToArray(); break;
                    case "memory":
                        Assert.Equal(expected.Length, prepared ? document.ConvertToPng(destination, CancellationToken.None) : SvgConverter.ConvertToPng(input, destination, SvgRenderOptions.Default, CancellationToken.None));
                        actual = destination; break;
                    case "memory-async":
                        Assert.Equal(expected.Length, prepared ? await document.ConvertToPngAsync(destination, CancellationToken.None) : await SvgConverter.ConvertToPngAsync(input, destination, SvgRenderOptions.Default, CancellationToken.None));
                        actual = destination; break;
                    case "stream":
                        Assert.Equal(expected.Length, prepared ? document.ConvertToPng(stream, CancellationToken.None) : SvgConverter.ConvertToPng(input, stream, SvgRenderOptions.Default, CancellationToken.None));
                        actual = stream.ToArray(); break;
                    default:
                        Assert.Equal(expected.Length, prepared ? await document.ConvertToPngAsync(stream, CancellationToken.None) : await SvgConverter.ConvertToPngAsync(input, stream, SvgRenderOptions.Default, CancellationToken.None));
                        actual = stream.ToArray(); break;
                }
                Assert.Equal(expected, actual);
            }
        }
    }

    [Theory]
    [InlineData("display='none'", "")]
    [InlineData("visibility='hidden'", "")]
    [InlineData("opacity='0'", "opacity='0'")]
    [InlineData("opacity='.5'", "opacity='.5'")]
    [InlineData("style='filter:url(#f)'", "")]
    [InlineData("class='effect'", "")]
    public void Empty_group_effects_follow_cascade_and_outer_composition(string attributes, string opacity)
    {
        string expected = attributes.StartsWith("display", StringComparison.Ordinal) ? "" : "<rect width='32' height='32' fill='red' " + opacity + "/>";
        foreach (string ending in new[] { "/>", "></g>" })
            Assert.Equal(Render(expected), Render(Filter + "<style>.effect{filter:url(#f)}</style><g filter='url(#f)' " + attributes + ending));
    }

    [Theory]
    [InlineData("<g opacity='.5'>", "</g>")]
    [InlineData("<svg width='16' height='16'>", "</svg>")]
    [InlineData("<g transform='translate(2 3)'>", "</g>")]
    [InlineData("<g clip-path='url(#c)'>", "</g>")]
    public void Empty_group_filters_survive_enclosing_scopes(string before, string after)
    {
        const string clip = "<defs><clipPath id='c'><rect width='16' height='16'/></clipPath></defs>";
        foreach (string ending in new[] { "/>", "></g>" })
            Assert.Equal(Render(clip + before + Red + after), Render(Filter + clip + before + "<g filter='url(#f)'" + ending + after));
    }

    [Theory]
    [InlineData("clip-path='url(#c)'")]
    [InlineData("mask='url(#m)'")]
    [InlineData("clip-path='url(#c)' mask='url(#m)' opacity='.5'")]
    public void Empty_group_filter_output_is_clipped_and_masked(string attributes)
    {
        const string resources = "<defs><clipPath id='c'><rect width='16' height='32'/></clipPath><mask id='m' mask-type='alpha' maskUnits='userSpaceOnUse' x='0' y='0' width='32' height='32'><rect width='32' height='32' fill='white' opacity='.5'/></mask></defs>";
        foreach (string ending in new[] { "/>", "></g>" })
            Assert.Equal(Render(resources + "<g " + attributes + ">" + Red + "</g>"), Render(Filter + resources + "<g filter='url(#f)' " + attributes + ending));
    }

    [Theory]
    [InlineData("<feOffset dx='0'/>", "filterUnits='userSpaceOnUse' x='0' y='0' width='32' height='32'")]
    [InlineData("<feFlood flood-color='red'/>", "")]
    public void Empty_sources_keep_identity_and_zero_object_box_filters_transparent(string primitive, string region)
    {
        string filter = "<defs><filter id='f' " + region + ">" + primitive + "</filter></defs>";
        foreach (string ending in new[] { "/>", "></g>" }) Assert.Equal(Render(""), Render(filter + "<g filter='url(#f)'" + ending));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Empty_group_filters_generate_resource_content(bool pattern)
    {
        foreach (string ending in new[] { "/>", "></g>" })
        {
            string content = Filter + (pattern
                ? "<defs><pattern id='p' width='32' height='32' patternUnits='userSpaceOnUse'><g filter='url(#f)'" + ending + "</pattern></defs><rect width='32' height='32' fill='url(#p)'/>"
                : "<defs><g id='s'><g filter='url(#f)'" + ending + "</g></defs><use href='#s'/>");
            Assert.Equal(Render(Red), Render(content));
        }
    }

    [Theory]
    [InlineData("/>")]
    [InlineData("></g>")]
    public void Active_empty_groups_require_defined_filter_references(string ending)
    {
        using MemoryStream output = new();
        Assert.Equal(SvgErrorCode.UnsupportedFeature, Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(Input("<g filter='url(#missing)'" + ending), output, SvgRenderOptions.Default, CancellationToken.None)).ErrorCode);
        Assert.Equal(0, output.Length);
    }

    [Fact]
    public async Task Empty_filtered_pattern_overrides_do_not_acquire_inherited_template_fonts()
    {
        SvgFontPackCatalog catalog = UnusedCatalog();
        foreach (string attributes in new[] { "filter='url(#f)'", "style='filter:url(#f)'", "class='effect'" })
        {
            byte[] input = Input(Filter + "<style>.effect{filter:url(#f)}</style><defs><pattern id='base' width='0' height='0' patternUnits='userSpaceOnUse'><text font-family='Unused'>A</text></pattern><pattern id='p' href='#base' width='32' height='32'><g " + attributes + "/></pattern></defs><rect width='32' height='32' fill='url(#p)'/>");
            using SvgPreparedDocument document = await SvgPreparedDocument.CreateAsync(input, catalog, new NoFonts(), SvgRenderOptions.Default, CancellationToken.None);
            Assert.Equal(Render(Red), document.ConvertToPng(CancellationToken.None).ToArray());
        }
    }

    [Fact]
    public void Empty_filtered_group_owners_release_on_denial_cancellation_and_retry()
    {
        byte[] input = Input(Filter + "<g filter='url(#f)'/>");
        RenderBudget measured = new(1_000_000); ParserWorkCounter work = new(1_000_000);
        long parserPeak;
        using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, measured, null, work, CancellationToken.None))
        {
            parserPeak = measured.Snapshot().PeakBytes;
            Assert.Contains(root.Commands, command => command.RegionKind == SvgLayerRegionKind.Filter);
            long retained = measured.Snapshot().CurrentBytes;
            using CancellationTokenSource cancellation = new(); cancellation.Cancel();
            Assert.ThrowsAny<OperationCanceledException>(() => SvgConverter.Rasterize(root, SvgRenderOptions.Default, measured, cancellation.Token));
            Assert.Equal(retained, measured.Snapshot().CurrentBytes);
            using SvgConverter.RasterizedSurface retry = SvgConverter.Rasterize(root, SvgRenderOptions.Default, measured, CancellationToken.None);
            Assert.All(Enumerable.Range(0, 1024), index => Assert.Equal((byte)255, retry.Pixels[index * 4 + 3]));
        }
        Assert.Equal(0, measured.Snapshot().CurrentBytes);
        RenderBudget denied = new(parserPeak - 1);
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, denied, null, new(1_000_000), CancellationToken.None)).ErrorCode);
        Assert.Equal(0, denied.Snapshot().CurrentBytes);
        RenderBudget workDenied = new(1_000_000);
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, workDenied, null, new(work.Used - 1), CancellationToken.None)).ErrorCode);
        Assert.Equal(0, workDenied.Snapshot().CurrentBytes);
        Assert.Equal(Render(Red), SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None).ToArray());
    }

    private static SvgFontPackCatalog UnusedCatalog() => SvgFontPackCatalog.Parse(System.Text.Encoding.UTF8.GetBytes("{\"packId\":\"unused\",\"files\":[{\"relativePath\":\"unused.ttf\",\"byteSize\":1,\"sha256\":\"" + new string('0', 64) + "\"}],\"families\":[{\"requestedFamily\":\"Unused\",\"resolvedFamily\":\"Unused\",\"relativeFontFile\":\"unused.ttf\"}]}"), "unused");

    private static byte[] Input(string content) => System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'>" + content + "</svg>");
    private static byte[] Render(string content) => SvgConverter.ConvertToPng(Input(content), SvgRenderOptions.Default, CancellationToken.None).ToArray();

    /// <summary>Font-free test provider; owns no storage and rejects acquisition after checking cancellation.</summary>
    private sealed class NoFonts : ISvgFontProgramSourceProvider
    {
        public ValueTask<ISvgFontProgramSource?> GetSourceAsync(SvgFontPackFile file, CancellationToken cancellationToken)
        { cancellationToken.ThrowIfCancellationRequested(); throw new InvalidOperationException("No font acquisition expected."); }
    }
}
