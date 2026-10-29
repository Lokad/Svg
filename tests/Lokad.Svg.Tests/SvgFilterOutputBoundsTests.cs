using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgFilterOutputBoundsTests
{
    [Theory]
    [InlineData("rect")]
    [InlineData("circle")]
    [InlineData("path")]
    [InlineData("line")]
    [InlineData("group")]
    public void Filter_output_bounds_include_floods_independently_of_source_placement(string kind)
    {
        foreach ((int x, int y) in new[] { (0, 0), (64, 0), (-64, 0), (0, 64), (0, -64), (64, 64), (-64, -64) })
        foreach (string graph in new[] { "<feFlood flood-color='red'/>", "<feFlood flood-color='red'/><feOffset dx='1' dy='1'/>", "<feFlood flood-color='red'/><feComposite in2='SourceGraphic' operator='over'/>" })
        {
            string expected = graph.Contains("feOffset", StringComparison.Ordinal) ? "<rect x='1' y='1' width='31' height='31' fill='red'/>" : "<rect width='32' height='32' fill='red'/>";
            Assert.Equal(Render(expected, SvgRenderOptions.Default), Render(Filter(graph) + Shape(kind, x, y), SvgRenderOptions.Default));
        }
    }

    [Theory]
    [InlineData("<feOffset dx='0'/>")]
    [InlineData("<feFlood flood-color='red'/><feComposite in2='SourceGraphic' operator='in'/>")]
    public void Source_dependent_results_remain_transparent_for_offscreen_sources(string graph)
    {
        foreach (string kind in new[] { "rect", "circle", "path", "line", "group" })
        foreach (int position in new[] { -64, 64 })
            Assert.Equal(Render("", SvgRenderOptions.Default), Render(Filter(graph) + Shape(kind, position, position), SvgRenderOptions.Default));
    }

    [Theory]
    [InlineData(100_000)]
    [InlineData(-100_000)]
    public void Unused_remote_source_bounds_do_not_enlarge_the_filter_capture(int position)
    {
        SvgRenderLimits limits = new(16_384, 64, 4096, 16_384, 64, 64);
        SvgRenderOptions options = new(null, 1, SvgColor.Transparent, limits);
        const string red = "<rect width='32' height='32' fill='red'/>";
        Assert.Equal(Render(red, options), Render(Filter("<feFlood flood-color='red'/>") + Shape("rect", position, position), options));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void Offscreen_filter_output_keeps_viewport_transform_clip_and_composition(int scale)
    {
        SvgRenderOptions options = new(null, scale, SvgColor.Transparent, SvgRenderLimits.Default);
        const string resources = "<defs><clipPath id='c'><rect width='12' height='12'/></clipPath><mask id='m' mask-type='alpha' maskUnits='userSpaceOnUse' x='0' y='0' width='32' height='32'><rect width='32' height='32' fill='white' opacity='.5'/></mask></defs>";
        foreach (string before in new[] { "<g>", "<svg width='16' height='16'>", "<g transform='translate(2 3)'>", "<g clip-path='url(#c)' mask='url(#m)' opacity='.5'>" })
        {
            string after = before.StartsWith("<svg", StringComparison.Ordinal) ? "</svg>" : "</g>";
            Assert.Equal(Render(resources + before + "<rect width='32' height='32' fill='red'/>" + after, options),
                Render(resources + Filter("<feFlood flood-color='red'/>") + before + Shape("rect", 64, 64) + after, options));
        }
    }

    [Fact]
    public void Source_independent_filter_playback_releases_on_memory_refusal_and_retry()
    {
        byte[] input = Input(Filter("<feFlood flood-color='red'/><feOffset dx='1' dy='1'/>") + Shape("rect", 64, 64));
        RenderBudget parser = new RenderBudget(new HostMemoryBudget(1_000_000));
        using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, parser, CancellationToken.None))
        {
            byte[] expected;
            RenderBudget measured = new RenderBudget(new HostMemoryBudget(1_000_000));
            using (SvgConverter.RasterizedSurface result = SvgConverter.Rasterize(root, SvgRenderOptions.Default, measured, CancellationToken.None))
            { expected = result.Pixels.ToArray(); }
            Assert.Equal(0, measured.Snapshot().CurrentBytes);
            RenderBudget denied = new RenderBudget(new HostMemoryBudget(measured.Snapshot().PeakBytes - 1));
            Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgConverter.Rasterize(root, SvgRenderOptions.Default, denied, CancellationToken.None)).ErrorCode);
            Assert.Equal(0, denied.Snapshot().CurrentBytes);
            Assert.Equal(0, measured.Snapshot().CurrentBytes);
            using CancellationTokenSource cancellation = new(); cancellation.Cancel();
            Assert.Equal(cancellation.Token, Assert.ThrowsAny<OperationCanceledException>(() => SvgConverter.Rasterize(root, SvgRenderOptions.Default, measured, cancellation.Token)).CancellationToken);
            Assert.Equal(0, measured.Snapshot().CurrentBytes);
            using SvgConverter.RasterizedSurface retry = SvgConverter.Rasterize(root, SvgRenderOptions.Default, measured, CancellationToken.None);
            Assert.Equal(expected, retry.Pixels);
        }
        Assert.Equal(0, parser.Snapshot().CurrentBytes);
    }

    private static string Shape(string kind, int x, int y) => kind switch
    {
        "rect" => "<rect x='" + (4 + x) + "' y='" + (4 + y) + "' width='8' height='8' filter='url(#f)'/>",
        "circle" => "<circle cx='" + (8 + x) + "' cy='" + (8 + y) + "' r='4' filter='url(#f)'/>",
        "path" => "<path d='M" + (4 + x) + " " + (4 + y) + "h8v8h-8Z' filter='url(#f)'/>",
        "line" => "<line x1='" + (4 + x) + "' y1='" + (4 + y) + "' x2='" + (12 + x) + "' y2='" + (12 + y) + "' stroke='blue' filter='url(#f)'/>",
        "group" => "<g filter='url(#f)'><rect x='" + (4 + x) + "' y='" + (4 + y) + "' width='8' height='8'/></g>",
        _ => throw new ArgumentException("Unknown source kind.", nameof(kind))
    };
    private static string Filter(string graph) => "<defs><filter id='f' filterUnits='userSpaceOnUse' x='0' y='0' width='32' height='32'>" + graph + "</filter></defs>";
    private static byte[] Input(string content) => System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'>" + content + "</svg>");
    private static byte[] Render(string content, SvgRenderOptions options) => SvgConverter.ConvertToPng(Input(content), options, CancellationToken.None).ToArray();
}
