using Lokad.Svg.Encoding;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgDefinitionDisplayTests
{
    public static IEnumerable<object[]> ResourceCases(bool text)
    {
        string[] kinds = text ? ["pattern", "mask", "marker"] : ["pattern", "mask", "clip", "marker", "linear", "radial", "filter", "symbol", "leaf", "image"];
        foreach (string kind in kinds)
        foreach (string scenario in new[] { "defs-none", "ancestor-none", "inherited-none", "initial", "unset", "inherited-inline" })
        foreach (string selector in new[] { "defs", "#d" })
        foreach (bool forward in new[] { false, true })
        {
            bool disabled = scenario is "defs-none" or "inherited-none" && kind is not ("leaf" or "image" or "symbol");
            string actual = Document(false);
            string expected = disabled ? Start + (kind is "mask" or "clip" ? "<rect width='32' height='32' fill='blue'/>" : kind == "marker" ? Line : "") + "</svg>" : Document(true);
            yield return [kind, scenario, selector, forward, actual, expected, disabled];

            string Document(bool control)
            {
                string display = control ? "inline" : scenario switch { "defs-none" => "none", "inherited-none" or "inherited-inline" => "inherit", "initial" => "initial", "unset" => "unset", _ => "inline" };
                string content = text ? "<text x='2' y='15' fill='blue'>A</text>" : Box;
                string resource;
                string target;
                switch (kind)
                {
                    case "pattern":
                        resource = "<pattern id='r' patternUnits='userSpaceOnUse' width='32' height='32'>" + content + "</pattern>";
                        target = "<rect width='32' height='32' fill='url(#r)'/>"; break;
                    case "mask":
                        resource = (text ? content.Replace("<text ", "<text id='t' ", StringComparison.Ordinal) : "")
                            + "<mask id='r' mask-type='alpha' maskUnits='userSpaceOnUse' x='0' y='0' width='32' height='32'>" + (text ? "<use href='#t'/>" : content) + "</mask>";
                        target = "<rect width='32' height='32' fill='blue' mask='url(#r)'/>"; break;
                    case "clip":
                        resource = "<clipPath id='r'>" + Box + "</clipPath>";
                        target = "<rect width='32' height='32' fill='blue' clip-path='url(#r)'/>"; break;
                    case "marker":
                        resource = "<marker id='r' markerUnits='userSpaceOnUse' markerWidth='" + (text ? "2ex" : "32") + "' markerHeight='" + (text ? "2ex" : "32") + "'>" + Box + "</marker>";
                        target = Line.Replace("/>", " marker-start='url(#r)'/>", StringComparison.Ordinal); break;
                    case "linear": case "radial":
                        string gradient = kind == "linear" ? "linearGradient" : "radialGradient";
                        resource = "<" + gradient + " id='r'><stop stop-color='blue'/><stop offset='1' stop-color='blue'/></" + gradient + ">";
                        target = "<rect width='32' height='32' fill='url(#r)'/>"; break;
                    case "filter":
                        resource = "<filter id='r' filterUnits='userSpaceOnUse' x='0' y='0' width='32' height='32'><feFlood flood-color='blue'/></filter>";
                        target = "<rect x='4' y='4' width='8' height='8' fill='red' filter='url(#r)'/>"; break;
                    case "symbol":
                        resource = "<symbol id='r' viewBox='0 0 32 32'>" + Box + "</symbol>";
                        target = "<use href='#r' width='32' height='32'/>"; break;
                    case "image":
                        resource = "<image id='r' x='4' y='4' width='8' height='8' href='data:image/png;base64," + Convert.ToBase64String(PngEncoder.Encode(1, 1, [0, 0, 255, 255], CancellationToken.None)) + "'/>";
                        target = "<use href='#r'/>"; break;
                    default:
                        resource = Box.Replace("<rect ", "<rect id='r' ", StringComparison.Ordinal);
                        target = "<use href='#r'/>"; break;
                }
                string definitions = "<defs id='d'>" + resource + "</defs>";
                if (scenario is not ("defs-none" or "inherited-inline")) definitions = "<g display='none'>" + definitions + "</g>";
                return Start + "<style>" + selector + "{display:" + display + "}</style>" + (forward ? target + definitions : definitions + target) + "</svg>";
            }
        }
    }

    [Theory]
    [MemberData(nameof(ResourceCases), false)]
    public void Defs_display_controls_resource_availability_without_suppressing_use_targets(string kind, string scenario, string selector, bool forward, string actual, string expected, bool disabled)
    {
        _ = (kind, scenario, selector, forward, disabled);
        Assert.Equal(Render(expected), Render(actual));
    }

    public static IEnumerable<object[]> TemplateCases()
    {
        foreach (string kind in new[] { "pattern", "linearGradient", "radialGradient" })
        foreach (string mode in new[] { "borrow", "replace", "complete", "complete-replace" })
        foreach (string hidden in new[] { "base", "alias", "both" })
        foreach (bool forward in new[] { false, true })
        foreach (bool ownDisplay in new[] { false, true })
        {
            bool visible = hidden == "base" && (kind == "pattern" ? mode == "complete-replace" : mode is "replace" or "complete-replace");
            yield return [kind, mode, hidden, forward, ownDisplay, Document(false), visible ? Document(true) : Start + "</svg>"];

            string Document(bool control)
            {
                string selector = hidden == "both" ? "#b,#a" : hidden == "base" ? "#b" : "#a";
                if (ownDisplay) selector = selector.Replace("#b", "#base", StringComparison.Ordinal).Replace("#a", "#r", StringComparison.Ordinal);
                string attributes = kind == "pattern" ? "patternUnits='userSpaceOnUse' width='32' height='32' patternTransform='matrix(1 0 0 1 0 0)'"
                    : "gradientUnits='objectBoundingBox' gradientTransform='matrix(1 0 0 1 0 0)' spreadMethod='pad' " + (kind == "linearGradient" ? "x1='0' y1='0' x2='1' y2='0'" : "cx='.5' cy='.5' r='.5' fx='.5' fy='.5' fr='0'");
                string content = kind == "pattern" ? Box : "<stop stop-color='blue'/><stop offset='1' stop-color='blue'/>";
                string replacement = kind == "pattern" ? "<rect x='12' y='12' width='8' height='8' fill='red'/>" : "<stop stop-color='red'/><stop offset='1' stop-color='red'/>";
                string definitions = "<defs id='b'><" + kind + " id='base' " + attributes + ">" + content + "</" + kind + "></defs><defs id='a'><" + kind + " id='r' href='#base' "
                    + (mode.StartsWith("complete", StringComparison.Ordinal) ? attributes : "") + ">" + (mode.EndsWith("replace", StringComparison.Ordinal) ? replacement : "") + "</" + kind + "></defs>";
                const string target = "<rect width='32' height='32' fill='url(#r)'/>";
                return Start + "<style>" + selector + "{display:" + (control ? "inline" : "none") + "}</style>" + (forward ? target + definitions : definitions + target) + "</svg>";
            }
        }
    }

    [Theory]
    [MemberData(nameof(TemplateCases))]
    public void Local_template_content_can_replace_unavailable_content_with_its_required_dimensions(string kind, string mode, string hidden, bool forward, bool ownDisplay, string actual, string expected)
    {
        _ = (kind, mode, hidden, forward, ownDisplay);
        Assert.Equal(Render(expected), Render(actual));
    }

    [Theory]
    [InlineData("linearGradient")]
    [InlineData("radialGradient")]
    public void An_unavailable_single_stop_gradient_keeps_the_shapes_independent_stroke(string kind)
    {
        const string shape = "<rect x='4' y='4' width='8' height='8' stroke='blue' stroke-width='2' fill='";
        string expected = Start + shape + "none'/></svg>";
        foreach (string selector in new[] { "defs", "#r" })
        {
            string actual = Start + "<style>" + selector + "{display:none}</style><defs><" + kind + " id='r'><stop stop-color='red'/></" + kind + "></defs>" + shape + "url(#r)'/></svg>";
            Assert.Equal(Render(expected), Render(actual));
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("rotate(15 16 16)")]
    [InlineData("skewX(20)")]
    public void An_unavailable_filter_does_not_paint_its_source(string transform)
    {
        string input = Start + "<style>#f{display:none}</style><defs><filter id='f'><feFlood flood-color='blue'/></filter></defs><rect width='32' height='32' fill='red' transform='" + transform + "' filter='url(#f)'/></svg>";
        Assert.Equal(Render(Start + "</svg>"), Render(input));
    }

    [Fact]
    public void Deferred_sources_and_unavailable_resources_release_on_denial_cancellation_and_retry()
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes(Start + "<style>#d{display:none}</style><defs id='d'>" + Box.Replace("<rect ", "<rect id='box' ", StringComparison.Ordinal)
            + "<text id='t' font-family='Unavailable'>A</text><mask id='m'><use href='#t'/></mask>"
            + "<marker id='mark' markerWidth='2ex' markerHeight='2ex'><rect width='8' height='8'/></marker>"
            + "<linearGradient id='g'><stop stop-color='red'/></linearGradient><filter id='f'><feFlood flood-color='red'/></filter></defs>"
            + "<path d='M0 0H28' stroke='black' fill='url(#g)' mask='url(#m)' marker-start='url(#mark)' filter='url(#f)'/><use href='#box'/></svg>");
        byte[] unchanged = input.ToArray();
        SvgRenderLimits limits = SvgRenderLimits.Default;
        RenderBudget parser = new(1_000_000);
        using (RenderBudget.BudgetReservation held = parser.Reserve(73, SvgMemoryCategory.Parser))
        {
            Assert.Equal(73, parser.Snapshot().CurrentBytes);
            using (SvgRoot scene = SvgRootParser.Parse(input, limits, parser, CancellationToken.None))
            {
                RenderBudget measured = new(1_000_000);
                byte[] reference;
                long work;
                using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(scene, SvgRenderOptions.Default, measured, CancellationToken.None))
                { reference = surface.Pixels.ToArray(); work = surface.WorkUnits; }
                Assert.Equal(0, measured.Snapshot().CurrentBytes);
                RenderBudget denied = new(measured.Snapshot().PeakBytes - 1);
                Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgConverter.Rasterize(scene, SvgRenderOptions.Default, denied, CancellationToken.None)).ErrorCode);
                Assert.Equal(0, denied.Snapshot().CurrentBytes);
                SvgRenderOptions shortWork = new(null, 1, SvgColor.Transparent,
                    new(limits.MaximumAccountedBytes, limits.MaximumInputBytes, limits.MaximumDimension, limits.MaximumPixels,
                        limits.MaximumEncodedPngBytes, limits.MaximumNodes, limits.MaximumPathCommands, checked((int)work - 1)));
                Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgConverter.Rasterize(scene, shortWork, measured, CancellationToken.None)).ErrorCode);
                Assert.Equal(0, measured.Snapshot().CurrentBytes);
                using CancellationTokenSource cancellation = new();
                cancellation.Cancel();
                Assert.Equal(cancellation.Token, Assert.ThrowsAny<OperationCanceledException>(() => SvgConverter.Rasterize(scene, SvgRenderOptions.Default, measured, cancellation.Token)).CancellationToken);
                Assert.Equal(0, measured.Snapshot().CurrentBytes);
                using SvgConverter.RasterizedSurface retry = SvgConverter.Rasterize(scene, SvgRenderOptions.Default, measured, CancellationToken.None);
                Assert.Equal(reference, retry.Pixels);
            }
            Assert.Equal(73, parser.Snapshot().CurrentBytes);
        }
        Assert.Equal(0, parser.Snapshot().CurrentBytes);
        Assert.Equal(unchanged, input);
    }

    private const string Start = "<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32' font-family='Example' font-size='12'>";
    private const string Box = "<rect x='4' y='4' width='8' height='8' fill='blue'/>";
    private const string Line = "<path d='M0 0H28' stroke='black'/>";
    private static byte[] Render(string input)
        => SvgConverter.ConvertToPng(System.Text.Encoding.UTF8.GetBytes(input), SvgRenderOptions.Default, CancellationToken.None).ToArray();
}
