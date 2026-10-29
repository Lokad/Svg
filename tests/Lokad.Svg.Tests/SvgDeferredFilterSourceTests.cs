using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgDeferredFilterSourceTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Paintless_sources_extend_filter_object_bounds_without_extending_painted_bounds(bool instance, bool mapped)
    {
        byte[] input = Build(true);
        RenderBudget budget = new(2_000_000);
        using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, null, CancellationToken.None))
        {
            SvgFilterArena filters = root.FilterResources ?? throw new InvalidDataException("Missing filter resources.");
            SvgDrawCommand source = root.Commands.First(command => command.Kind == SvgDrawCommandKind.BeginGroup && command.RegionKind == SvgLayerRegionKind.Filter);
            SvgFilterBinding binding = filters.BindingAt(source.MaskContentIndex);
            Assert.Equal(mapped ? new SvgPaintBounds(4, 12, 40, 44) : new SvgPaintBounds(4, 4, 40, 12), binding.ObjectBounds);
            Assert.Equal(mapped ? new SvgPaintBounds(4, 12, 12, 24) : new SvgPaintBounds(4, 8, 12, 12), binding.VisualBounds);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        Assert.Equal(SvgConverter.ConvertToPng(Build(false), SvgRenderOptions.Default, CancellationToken.None).ToArray(),
            SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None).ToArray());

        byte[] Build(bool withLine)
        {
            const string line = "<line id='line' x1='20' y1='4' x2='40' y2='4'/>";
            string source = withLine ? instance ? "<use href='#line'/>" : line : "";
            return System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='48' height='48'><defs><filter id='f' filterUnits='userSpaceOnUse' x='0' y='0' width='48' height='48'><feOffset dx='1' dy='1'/></filter></defs><g filter='url(#f)'"
                + (mapped ? " transform='matrix(1 1 0 1 0 0)'" : "") + ">" + source + "<rect x='4' y='8' width='8' height='4' fill='blue'/></g><defs>" + (instance ? line : "") + "</defs></svg>");
        }
    }

    public static TheoryData<string, string> DeferredFilterMarkerCases()
    {
        TheoryData<string, string> cases = new();
        foreach (string shape in new[] { "<line x1='4' y1='8' x2='12' y2='8'", "<polyline points='4 8 12 8 12 16'", "<path d='M4 8H12V16'" })
        foreach (string role in new[] { "start", "mid", "end" }) cases.Add(shape, role);
        return cases;
    }

    [Theory]
    [MemberData(nameof(DeferredFilterMarkerCases))]
    public void Deferred_filter_effect_anchors_accept_affine_marker_instances(string shape, string role)
    {
        byte[] expected = SvgConverter.ConvertToPng(Build(false), SvgRenderOptions.Default, CancellationToken.None).ToArray();
        byte[] actual = SvgConverter.ConvertToPng(Build(true), SvgRenderOptions.Default, CancellationToken.None).ToArray();
        Assert.Equal(expected, actual);

        byte[] Build(bool forward)
        {
            const string filter = "<filter id='f' filterUnits='userSpaceOnUse' x='0' y='0' width='32' height='32'><feOffset dx='1' dy='1'/></filter>";
            const string marker = "<defs><marker id='m' markerUnits='userSpaceOnUse' markerWidth='8' markerHeight='8' refX='4' refY='4' orient='0'><rect width='4' height='8' fill='blue'/><rect x='4' width='4' height='8' fill='green'/></marker></defs>";
            string source = "<g filter='url(#f)' transform='matrix(1 .125 .125 1 1 1)'><defs>" + shape + " id='s' marker-" + role + "='url(#m)'/></defs><use href='#s'/></g>";
            return System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'><defs>" + filter + "</defs>" + (forward ? source + marker : marker + source) + "</svg>");
        }
    }

    public static TheoryData<string, bool, double> DeferredFilterShapeCases()
    {
        TheoryData<string, bool, double> cases = new();
        foreach (string shape in new[] { "<rect x='4' y='4' width='12' height='10'/>", "<circle cx='10' cy='10' r='6'/>", "<path d='M4 4L16 4L10 14Z'/>", "<line x1='4' y1='4' x2='16' y2='14' stroke='red' stroke-width='2'/>" })
        foreach (bool symbol in new[] { false, true })
        foreach (double scale in new[] { 1d, 1.5 }) cases.Add(shape, symbol, scale);
        return cases;
    }

    [Theory]
    [MemberData(nameof(DeferredFilterShapeCases))]
    public void Enclosing_filters_keep_shape_source_bounds_across_forward_instance_replay(string shape, bool symbol, double scale)
    {
        SvgRenderOptions options = new(null, scale, SvgColor.Transparent, SvgRenderLimits.Default);
        byte[] expected = SvgConverter.ConvertToPng(Build(false), options, CancellationToken.None).ToArray();
        byte[] actual = SvgConverter.ConvertToPng(Build(true), options, CancellationToken.None).ToArray();
        Assert.Equal(expected, actual);

        byte[] Build(bool forward)
        {
            string definition = symbol ? "<symbol id='s' viewBox='0 0 32 32'>" + shape + "</symbol>" : "<g id='s'>" + shape + "</g>";
            string filter = "<filter id='f' filterUnits='userSpaceOnUse' x='0' y='0' width='32' height='32'><feOffset dx='2' dy='1'/></filter>";
            string source = "<g filter='url(#f)' fill='blue'><use href='#s' width='32' height='32'/></g><rect x='24' y='24' width='4' height='4' fill='red'/>";
            string defs = "<defs>" + filter + definition + "</defs>";
            return System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'>" + (forward ? source + defs : defs + source) + "</svg>");
        }
    }
}
