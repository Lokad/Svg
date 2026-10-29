using Lokad.Svg.Fonts;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed partial class OpenTypeFontTests
{
    public static IEnumerable<object[]> EmptyUseSources()
    {
        foreach (string href in new[] { "", "href=''", "xlink:href=''", "href='' xlink:href='#source'" })
        foreach (string scope in new[] { "direct", "group", "group-instance", "symbol", "pattern", "clip", "mask", "marker" })
        foreach (bool balanced in new[] { false, true }) yield return [href, scope, balanced];
    }

    [Theory]
    [MemberData(nameof(EmptyUseSources))]
    public async Task Absent_use_sources_are_inert_before_bindings_replay_and_font_acquisition(string href, string scope, bool balanced)
    {
        string empty = "<use " + href + " font-family='Missing'" + (balanced ? "><title>caption</title><!--note--></use>" : "/>");
        byte[] input = Svg(empty), expectedInput = Svg("");
        byte[] expected = SvgConverter.ConvertToPng(expectedInput, SvgRenderOptions.Default, CancellationToken.None).ToArray();
        Assert.Equal(expected, SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None).ToArray());
        (SvgFontPackCatalog catalog, CountingFontProvider provider, CountingFontSource _) = CreateExamplePack(CreateTriangleFont());
        using SvgPreparedDocument prepared = await SvgPreparedDocument.CreateAsync(input, catalog, provider, SvgRenderOptions.Default, CancellationToken.None);
        Assert.Equal(expected, prepared.ConvertToPng(CancellationToken.None).ToArray());
        Assert.Equal(0, provider.RequestCount);

        byte[] Svg(string absent)
        {
            const string red = "<rect width='8' height='8' fill='red'/>";
            string body = scope switch
            {
                "group" => "<g>" + absent + red + "</g>",
                "group-instance" => "<defs><g id='q'>" + absent + red + "</g></defs><use href='#q'/>",
                "symbol" => "<defs><symbol id='q'>" + absent + red + "</symbol></defs><use href='#q'/>",
                "pattern" => "<defs><pattern id='q' width='8' height='8' patternUnits='userSpaceOnUse'>" + absent + red + "</pattern></defs><rect width='8' height='8' fill='url(#q)'/>",
                "clip" => "<defs><clipPath id='q'>" + absent + "<rect width='4' height='8'/></clipPath></defs><rect width='8' height='8' fill='red' clip-path='url(#q)'/>",
                "mask" => "<defs><mask id='q'>" + absent + "<rect width='8' height='8' fill='white'/></mask></defs><rect width='8' height='8' fill='red' mask='url(#q)'/>",
                "marker" => "<defs><marker id='q' markerUnits='userSpaceOnUse' markerWidth='8' markerHeight='8'>" + absent + red + "</marker></defs><path d='M0 0L8 8' marker-start='url(#q)'/>",
                _ => absent + red
            };
            return System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' xmlns:xlink='http://www.w3.org/1999/xlink' width='8' height='8'><defs><rect id='source' width='8' height='8' fill='blue'/></defs>" + body + "</svg>");
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Empty_use_content_overrides_a_pattern_template_without_adding_reference_placeholders(bool forward)
    {
        const string template = "<pattern id='base' width='8' height='8' patternUnits='userSpaceOnUse'><rect width='8' height='8' fill='red'/></pattern>";
        const string alias = "<pattern id='p' href='#base'><use/></pattern>";
        byte[] input = Document("<defs>" + (forward ? alias + template : template + alias) + "</defs><rect width='8' height='8' fill='url(#p)'/>");
        Assert.Equal(Render(Document("")), Render(input));
    }

    [Fact]
    public void Empty_symbol_use_ids_remain_available_without_an_empty_source_key()
    {
        byte[] input = Document("<defs><symbol id='s'><use id='empty'/><rect id='live' width='4' height='8' fill='red'/></symbol></defs><use href='#empty'/><use href='#live' x='4'/>");
        Assert.Equal(Render(Document("<rect x='4' width='4' height='8' fill='red'/>")), Render(input));
    }

    [Theory]
    [InlineData("clipPath", "clip-path")]
    [InlineData("mask", "mask")]
    public void An_empty_use_after_a_real_resource_use_does_not_change_resource_admission(string resource, string effect)
    {
        const string source = "<path id='contour' d='M0 0H4V8H0Z' fill='white'/>";
        byte[] Svg(string empty) => Document("<defs>" + source + "<" + resource + " id='q'><use href='#contour'/>" + empty + "</" + resource + "></defs><rect width='8' height='8' fill='red' " + effect + "='url(#q)'/>");
        Assert.Equal(Render(Svg("")), Render(Svg("<use/>")));
    }

    [Theory]
    [InlineData("<use href='#missing'/>", SvgErrorCode.UnsupportedFeature)]
    [InlineData("<use href='#'/>", SvgErrorCode.UnsupportedFeature)]
    [InlineData("<use width='-1'/>", SvgErrorCode.MalformedInput)]
    [InlineData("<use><rect width='8' height='8'/></use>", SvgErrorCode.UnsupportedFeature)]
    public void Inert_use_defaults_do_not_hide_reference_syntax_or_drawing_child_errors(string body, SvgErrorCode code)
        => Assert.Equal(code, Assert.Throws<SvgException>(() => Render(Document(body))).ErrorCode);

    [Fact]
    public void Empty_use_descriptions_keep_structural_work_and_storage_limits()
    {
        byte[] input = Document("<use><title>caption</title><metadata><item xmlns='urn:review'/></metadata></use>");
        RenderBudget budget = new(1_000_000);
        using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None)) Assert.False(root.HasUnsupportedChild);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        SvgRenderLimits d = SvgRenderLimits.Default;
        SvgRenderLimits limits = new(d.MaximumAccountedBytes, d.MaximumInputBytes, d.MaximumDimension, d.MaximumPixels,
            d.MaximumEncodedPngBytes, 3, d.MaximumPathCommands, d.MaximumXmlDepth, d.MaximumAttributesPerElement);
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, limits, budget, CancellationToken.None)).ErrorCode);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    private static byte[] Document(string body) => System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'>" + body + "</svg>");
    private static byte[] Render(byte[] input) => SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None).ToArray();
}
