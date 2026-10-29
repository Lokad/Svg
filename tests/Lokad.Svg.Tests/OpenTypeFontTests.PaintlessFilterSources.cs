using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed partial class OpenTypeFontTests
{
    [Theory]
    [InlineData("rotate(90 16 16)")]
    [InlineData("matrix(0 2 -1 0 32 0)")]
    public async Task Paintless_source_bounds_survive_affine_filter_replay(string transform)
    {
        byte[] input = Input("<defs><filter id='f' x='0' y='0' width='1' height='1'><feFlood flood-color='red'/></filter></defs><g transform='" + transform + "' filter='url(#f)'><rect x='4' y='4' width='4' height='4'/><path d='M28 28' fill='none'/></g>");
        byte[] expected = Render(Input("<rect x='4' y='4' width='24' height='24' fill='red' transform='" + transform + "'/>"));
        Assert.Equal(expected, Render(input));
        (var catalog, var provider, _) = CreateExamplePack(CreateTriangleFont());
        using SvgPreparedDocument prepared = await SvgPreparedDocument.CreateAsync(input, catalog, provider, SvgRenderOptions.Default, CancellationToken.None);
        Assert.Equal(expected, prepared.ConvertToPng(CancellationToken.None).ToArray());

        static byte[] Input(string content) => System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'>" + content + "</svg>");
    }

    public static IEnumerable<object?[]> SourceLessUseCompositions()
    {
        (string, string?)[] cases =
        [
            ("", ""), ("x='4' y='8'", "transform='translate(4 8)'"), ("transform='scale(.5)'", "transform='scale(.5)'"),
            ("clip-path='url(#c)'", "clip-path='url(#c)'"), ("mask='url(#m)'", "mask='url(#m)'"),
            ("opacity='.5'", "opacity='.5'"), ("visibility='hidden'", ""), ("display='none'", "display='none'"),
            ("x='4' y='8' opacity='.5' mask='url(#m)' clip-path='url(#c)'", "transform='translate(4 8)' opacity='.5' mask='url(#m)' clip-path='url(#c)'"),
            ("clip-path='url(#o)'", null)
        ];
        foreach ((string attributes, string? control) in cases)
            foreach (string context in new[] { "direct", "symbol", "pattern" })
                foreach (bool forward in new[] { false, true })
                    foreach (bool emptySymbol in new[] { false, true }) yield return [attributes, control, context, forward, emptySymbol];
    }

    [Theory]
    [MemberData(nameof(SourceLessUseCompositions))]
    public async Task Source_less_use_composes_filter_mask_clip_and_opacity(string attributes, string? controlAttributes, string context, bool forward, bool emptySymbol)
    {
        const string definitions = "<defs><filter id='f' filterUnits='userSpaceOnUse' x='0' y='0' width='16' height='16'><feFlood flood-color='red'/></filter><clipPath id='c'><rect width='8' height='16'/></clipPath><mask id='m' mask-type='alpha' maskUnits='userSpaceOnUse' x='0' y='0' width='32' height='32'><rect width='12' height='16' fill='white'/></mask><clipPath id='o' clipPathUnits='objectBoundingBox'><rect width='1' height='1'/></clipPath></defs>";
        string body = Wrap("<use filter='url(#f)' " + (emptySymbol ? "href='#empty' " : "") + attributes + "/>");
        string resources = definitions + "<defs><symbol id='empty'/></defs>";
        byte[] input = Input(forward ? body + resources : resources + body);
        byte[] expected = Render(Input(definitions + (controlAttributes is null ? "" : "<g " + controlAttributes + "><rect width='16' height='16' fill='red'/></g>")));
        Assert.Equal(expected, Render(input));
        (var catalog, var provider, _) = CreateExamplePack(CreateTriangleFont());
        using SvgPreparedDocument prepared = await SvgPreparedDocument.CreateAsync(input, catalog, provider, SvgRenderOptions.Default, CancellationToken.None);
        Assert.Equal(expected, prepared.ConvertToPng(CancellationToken.None).ToArray());

        string Wrap(string content) => context switch
        {
            "symbol" => "<defs><symbol id='s'>" + content + "</symbol></defs><use href='#s'/>",
            "pattern" => "<defs><pattern id='p' patternUnits='userSpaceOnUse' width='32' height='32'>" + content + "</pattern></defs><rect width='32' height='32' fill='url(#p)'/>",
            _ => content
        };
        static byte[] Input(string content) => System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'>" + content + "</svg>");
    }

    [Fact]
    public void Source_less_filtered_use_releases_parser_owners_after_cancellation_and_retry()
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'><use filter='url(#f)'><title>caption</title></use><defs><filter id='f' filterUnits='userSpaceOnUse' width='32' height='32'><feFlood flood-color='red'/></filter></defs></svg>");
        RenderBudget budget = new(10_000_000);
        using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None))
        {
            Assert.False(root.HasUnsupportedChild);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        using CancellationTokenSource cancelled = new();
        cancelled.Cancel();
        Assert.Throws<OperationCanceledException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, cancelled.Token));
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None)) Assert.False(root.HasUnsupportedChild);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        byte[] drawingChild = System.Text.Encoding.UTF8.GetBytes(System.Text.Encoding.UTF8.GetString(input).Replace("<title>caption</title>", "<rect width='4' height='4'/>", StringComparison.Ordinal));
        Assert.Equal(SvgErrorCode.UnsupportedFeature, Assert.Throws<SvgException>(() => Render(drawingChild)).ErrorCode);
    }

    public static IEnumerable<object[]> EmptyFilteredUses()
    {
        foreach (string href in new[] { "", "href=''", "href='' xlink:href='#missing'", "href='#empty'" })
            foreach (string context in new[] { "direct", "group", "symbol", "pattern", "mask", "marker" }) yield return [href, context];
    }

    [Theory]
    [MemberData(nameof(EmptyFilteredUses))]
    public async Task Source_less_and_empty_target_uses_keep_their_own_filter(string href, string context)
    {
        const string filter = "<filter id='f' filterUnits='userSpaceOnUse' x='0' y='0' width='32' height='32'><feFlood flood-color='red'/></filter><symbol id='empty'/>";
        string node = "<use " + href + " filter='url(#f)' font-family='Missing'/>";
        string body = context switch
        {
            "group" => "<g>" + node + "</g>",
            "symbol" => "<defs><symbol id='s'>" + node + "</symbol></defs><use href='#s'/>",
            "pattern" => "<defs><pattern id='s' patternUnits='userSpaceOnUse' width='32' height='32'>" + node + "</pattern></defs><rect width='32' height='32' fill='url(#s)'/>",
            "mask" => "<defs><mask id='s' mask-type='alpha'>" + node + "</mask></defs><rect width='32' height='32' fill='red' mask='url(#s)'/>",
            "marker" => "<defs><marker id='s' markerUnits='userSpaceOnUse' markerWidth='32' markerHeight='32'>" + node + "</marker></defs><path d='M0 0L0 0' marker-start='url(#s)'/>",
            _ => node
        };
        byte[] input = Input("<defs>" + filter + "</defs>" + body);
        byte[] expected = Render(Input("<rect width='32' height='32' fill='red'/>"));
        Assert.Equal(expected, Render(input));
        (var catalog, var provider, _) = CreateExamplePack(CreateTriangleFont());
        using SvgPreparedDocument prepared = await SvgPreparedDocument.CreateAsync(input, catalog, provider, SvgRenderOptions.Default, CancellationToken.None);
        Assert.Equal(expected, prepared.ConvertToPng(CancellationToken.None).ToArray());
        Assert.Equal(0, provider.RequestCount);

        static byte[] Input(string content) => System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' xmlns:xlink='http://www.w3.org/1999/xlink' width='32' height='32'>" + content + "</svg>");
    }

    public static IEnumerable<object[]> FilterSourceBounds()
    {
        (string, int)[] sources =
        {
            ("<rect x='28' y='28' width='0' height='0'/>", 4), ("<circle cx='28' cy='28' r='0'/>", 4),
            ("<ellipse cx='28' cy='28' rx='0' ry='4'/>", 4), ("<image x='28' y='28' width='0' height='0'/>", 4),
            ("<path d='M28 28' fill='none'/>", 24), ("<line x1='28' y1='28' x2='28' y2='28'/>", 24),
            ("<polyline points='28 28'/>", 24),
            ("<rect x='20' y='20' width='8' height='8' display='none'/>", 4),
            ("<circle cx='24' cy='24' r='4' display='none'/>", 4),
            ("<ellipse cx='24' cy='24' rx='4' ry='4' display='none'/>", 4),
            ("<rect x='20' y='20' width='8' height='8' fill='none'/>", 24),
            ("<circle cx='24' cy='24' r='4' fill='none'/>", 24),
            ("<ellipse cx='24' cy='24' rx='4' ry='4' fill='none'/>", 24)
        };
        foreach ((string source, int size) in sources)
            foreach (string context in new[] { "direct", "use", "symbol", "pattern" })
                foreach (double scale in new[] { .5, 1, 2 }) yield return [source, context, size, scale];
    }

    [Theory]
    [MemberData(nameof(FilterSourceBounds))]
    public async Task Ancestor_filter_bounds_distinguish_suppressed_leaves_from_paintless_geometry(string source, string context, int size, double scale)
    {
        string content = context switch
        {
            "use" => "<defs>" + source.Replace("/>", " id='s'/>", StringComparison.Ordinal) + "</defs><use href='#s'/>",
            "symbol" => "<defs><symbol id='s'>" + source + "</symbol></defs><use href='#s'/>",
            _ => source
        };
        string group = "<g filter='url(#outer)'><rect x='4' y='4' width='4' height='4'/>" + content + "</g>";
        if (context == "pattern") group = "<defs><pattern id='p' patternUnits='userSpaceOnUse' width='32' height='32'>" + group + "</pattern></defs><rect width='32' height='32' fill='url(#p)'/>";
        byte[] input = Input("<defs><filter id='outer' x='0' y='0' width='1' height='1'><feFlood flood-color='red'/></filter></defs>" + group);
        SvgRenderOptions options = new(null, scale, SvgColor.Transparent, SvgRenderLimits.Default, null);
        byte[] expected = SvgConverter.ConvertToPng(Input($"<rect x='4' y='4' width='{size}' height='{size}' fill='red'/>"), options, CancellationToken.None).ToArray();
        Assert.Equal(expected, SvgConverter.ConvertToPng(input, options, CancellationToken.None).ToArray());
        (var catalog, var provider, _) = CreateExamplePack(CreateTriangleFont());
        using SvgPreparedDocument prepared = await SvgPreparedDocument.CreateAsync(input, catalog, provider, options, CancellationToken.None);
        Assert.Equal(expected, prepared.ConvertToPng(CancellationToken.None).ToArray());

        static byte[] Input(string content) => System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'>" + content + "</svg>");
    }

    public static IEnumerable<object[]> PaintlessFilterSources()
    {
        foreach (string source in new[]
        {
            "<path d='M4 4H28V28H4Z' fill='none'", "<path d='M4 4H28V28H4Z' visibility='hidden'",
            "<line x1='4' y1='4' x2='28' y2='28'", "<line x1='4' y1='4' x2='28' y2='4'",
            "<line x1='4' y1='4' x2='4' y2='4'", "<path d='M4 4H28'", "<path d='M4 4'",
            "<path d='M4 4L4 4'", "<polyline points='4 4'", "<polygon points='4 4'"
        })
            foreach (string context in new[] { "direct", "use", "symbol", "pattern", "use-owner" }) yield return [source, context];
    }

    [Theory]
    [MemberData(nameof(PaintlessFilterSources))]
    public async Task Eligible_paintless_sources_keep_source_independent_filters(string source, string context)
    {
        (var catalog, var provider, _) = CreateExamplePack(CreateTriangleFont());
        const string filter = "<filter id='f' filterUnits='userSpaceOnUse' x='0' y='0' width='32' height='32'><feFlood flood-color='red'/></filter>";
        string effect = context == "use-owner" ? "" : " filter='url(#f)'";
        string node = source + effect;
        string body = context switch
        {
            "use" or "use-owner" => "<defs>" + node + " id='s'/></defs><use href='#s'" + (context == "use-owner" ? " filter='url(#f)'" : "") + "/>",
            "symbol" => "<defs><symbol id='s'>" + node + "/></symbol></defs><use href='#s'/>",
            "pattern" => "<defs><pattern id='s' patternUnits='userSpaceOnUse' width='32' height='32'>" + node + "/></pattern></defs><rect width='32' height='32' fill='url(#s)'/>",
            _ => node + "/>"
        };
        byte[] input = Input("<defs>" + filter + "</defs>" + body);
        byte[] expected = Render(Input("<rect width='32' height='32' fill='red'/>"));
        Assert.Equal(expected, Render(input));
        using SvgPreparedDocument prepared = await SvgPreparedDocument.CreateAsync(input, catalog, provider, SvgRenderOptions.Default, CancellationToken.None);
        Assert.Equal(expected, prepared.ConvertToPng(CancellationToken.None).ToArray());

        static byte[] Input(string content) => System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'>" + content + "</svg>");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Paintless_path_bounds_contribute_to_an_ancestor_filter(bool ownFilter)
    {
        string effects = "<defs><filter id='outer' x='0' y='0' width='1' height='1'><feFlood flood-color='red'/></filter><filter id='inner' filterUnits='userSpaceOnUse' x='0' y='0' width='32' height='32'><feFlood flood-color='blue'/></filter></defs>";
        string body = effects + "<g filter='url(#outer)'><rect x='4' y='4' width='4' height='4'/><path d='M20 20H28V28H20Z' fill='none'" + (ownFilter ? " filter='url(#inner)'" : "") + "/></g>";
        byte[] input = Input(body), control = Input("<rect x='4' y='4' width='24' height='24' fill='red'/>");
        Assert.Equal(Render(control), Render(input));
        (var catalog, var provider, _) = CreateExamplePack(CreateTriangleFont());
        using SvgPreparedDocument prepared = await SvgPreparedDocument.CreateAsync(input, catalog, provider, SvgRenderOptions.Default, CancellationToken.None);
        Assert.Equal(Render(control), prepared.ConvertToPng(CancellationToken.None).ToArray());

        static byte[] Input(string content) => System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'>" + content + "</svg>");
    }
}
