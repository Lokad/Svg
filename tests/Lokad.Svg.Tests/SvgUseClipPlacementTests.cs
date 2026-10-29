using TextEncoding = System.Text.Encoding;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgUseClipPlacementTests
{
    public static IEnumerable<object[]> Placements()
    {
        foreach (string shape in new[] { "rect", "path", "curve", "circle", "polygon", "polyline" })
        foreach (string clip in new[] { "rect", "path", "object" })
        foreach (bool fractional in new[] { false, true })
        foreach (string map in new[] { "", "scale(.75)", "matrix(.75 0 0 1.25 0 -4)" })
        foreach (bool symbol in new[] { false, true })
        foreach (bool filter in new[] { false, true })
        foreach (bool forward in new[] { false, true }) yield return [shape, clip, fractional, map, symbol, filter, forward];
    }

    [Theory]
    [MemberData(nameof(Placements))]
    [Trait("Layer", "Rendering")]
    public void Owning_use_clip_keeps_the_same_placement_as_an_enclosing_translation(string shape, string clip, bool fractional, string map, bool symbol, bool filter, bool forward)
    {
        // Actual native SVG pairs independently establish this equivalence.
        // Filter/source-text fidelity remains separate from clip placement.
        var (control, input) = PlacementInputs(shape, clip, fractional, map, symbol, filter, forward);
        SvgRasterTestHelper.AssertPixelsEqual(control, input, SvgRenderOptions.Default);
    }

    [Fact]
    [Trait("Layer", "Mechanism")]
    public void Fractional_owning_clip_multiplies_native_edge_and_corner_coverage()
    {
        byte[] input = TextEncoding.UTF8.GetBytes(Document("rect", "rect", true, "", false, false, false, false));
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(1_000_000));
        using SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None);
        using SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, SvgRenderOptions.Default, budget, CancellationToken.None);
        Assert.Equal(16, surface.Pixels[(6 * 32 + 7) * 4 + 3]);
        Assert.Equal(64, surface.Pixels[(6 * 32 + 8) * 4 + 3]);
        Assert.Equal(64, surface.Pixels[(7 * 32 + 7) * 4 + 3]);
        Assert.Equal(255, surface.Pixels[(7 * 32 + 8) * 4 + 3]);
        Assert.Equal(32, surface.Pixels[(6 * 32 + 23) * 4 + 3]);
        Assert.Equal(128, surface.Pixels[(7 * 32 + 23) * 4 + 3]);
    }

    [Theory]
    [Trait("Layer", "Mechanism")]
    [InlineData("<rect x='4' y='4' width='16' height='16'/>")]
    [InlineData("<path d='M4 4H20V20H4Z'/>")]
    public void Nearly_integral_object_clip_rounds_to_native_binary_bounds_at_output_scale(string source)
    {
        byte[] input = TextEncoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'><use href='#s' width='24' height='24' x='4' y='4' fill='red' clip-path='url(#c)'/><defs><clipPath id='c' clipPathUnits='objectBoundingBox'><rect width='.75' height='.75'/></clipPath><symbol id='s' viewBox='0 0 32 32'>" + source + "</symbol></defs></svg>");
        SvgRenderOptions options = new(null, 1.125, SvgColor.Transparent, SvgRenderLimits.Default);
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(1_000_000));
        using SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None);
        using SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, options, budget, CancellationToken.None);
        Assert.Equal(36, surface.Width);
        Assert.Equal(36, surface.Height);
        for (int y = 0; y < 36; y++)
        for (int x = 0; x < 36; x++)
            Assert.Equal(x >= 8 && x < 18 && y >= 8 && y < 18 ? (byte)255 : (byte)0, surface.Pixels[(y * 36 + x) * 4 + 3]);
    }

    [Fact]
    [Trait("Layer", "Mechanism")]
    public void One_vector_paint_folds_opacity_through_its_clip_without_changing_color_rounding()
    {
        byte[] input = "<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'><defs><rect id='s' width='32' height='32' fill='#55a0dc'/><clipPath id='c'><rect x='4.5' y='5.5' width='10' height='10'/></clipPath></defs><use href='#s' x='3.5' y='2.5' opacity='.68' clip-path='url(#c)'/></svg>"u8.ToArray();
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(1_000_000));
        using SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None);
        using SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, SvgRenderOptions.Default, budget, CancellationToken.None);
        Assert.Equal(new byte[] { 85, 161, 220, 173 }, surface.Pixels.AsSpan((12 * 32 + 12) * 4, 4).ToArray());
    }

    [Fact]
    [Trait("Layer", "Mechanism")]
    public void Multiple_paints_keep_isolated_opacity_inside_an_owning_clip()
    {
        byte[] input = "<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'><defs><g id='s'><rect width='20' height='20' fill='red'/><rect x='8' width='20' height='20' fill='blue'/></g><clipPath id='c'><rect width='32' height='32'/></clipPath></defs><use href='#s' opacity='.68' clip-path='url(#c)'/></svg>"u8.ToArray();
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(1_000_000));
        using SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None);
        using SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, SvgRenderOptions.Default, budget, CancellationToken.None);
        Assert.Equal(new byte[] { 255, 0, 0, 173 }, surface.Pixels.AsSpan((12 * 32 + 4) * 4, 4).ToArray());
        Assert.Equal(new byte[] { 0, 0, 255, 173 }, surface.Pixels.AsSpan((12 * 32 + 12) * 4, 4).ToArray());
    }

    [Theory]
    [Trait("Layer", "Contract")]
    [InlineData("rect", false, false)]
    [InlineData("object", false, false)]
    [InlineData("path", false, false)]
    [InlineData("rect", true, false)]
    [InlineData("object", true, false)]
    [InlineData("path", true, false)]
    [InlineData("rect", false, true)]
    [InlineData("object", false, true)]
    [InlineData("path", false, true)]
    public void Deferred_clip_owners_preserve_borrowed_storage_on_failure_cancellation_and_retry(string clip, bool symbol, bool filter)
    {
        byte[] input = TextEncoding.UTF8.GetBytes(Document("curve", clip, true, "scale(.75)", symbol, filter, true, false));
        byte[] unchanged = input.ToArray();
        SvgParseOwnershipAssertions.PreservesBorrowedStorageOnDenialCancellationAndRetry(input);
        Assert.Equal(unchanged, input);
    }

    [Theory]
    [Trait("Layer", "Contract")]
    [InlineData("<g id='s'/>")]
    [InlineData("<path id='s' d=''/>")]
    [InlineData("<polygon id='s' points=''/>")]
    [InlineData("<rect id='s' width='0' height='8'/>")]
    public void Empty_instances_still_validate_their_owning_clip_before_writing_output(string source)
    {
        byte[] input = TextEncoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'><defs>" + source + "</defs><use href='#s' clip-path='url(#missing)'/></svg>");
        byte[] destination = Enumerable.Repeat((byte)173, 1024).ToArray();
        byte[] unchanged = destination.ToArray();
        Assert.Equal(SvgErrorCode.UnsupportedFeature, Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(input, destination.AsMemory(), SvgRenderOptions.Default, CancellationToken.None)).ErrorCode);
        Assert.Equal(unchanged, destination);
    }

    [Theory]
    [Trait("Layer", "Rendering")]
    [InlineData("rect", "rect", true, "", false, false, true)]
    [InlineData("curve", "object", false, "matrix(.75 0 0 1.25 0 -4)", true, true, true)]
    [InlineData("polygon", "path", true, "scale(.75)", false, true, false)]
    public void Complete_png_owning_clips_preserve_placement_and_effects(string shape, string clip, bool fractional, string map, bool symbol, bool filter, bool forward)
    {
        var (control, input) = PlacementInputs(shape, clip, fractional, map, symbol, filter, forward);
        SvgRasterTestHelper.AssertPngEqual(control, input, SvgRenderOptions.Default);
    }

    private static (byte[] Control, byte[] Input) PlacementInputs(string shape, string clip, bool fractional, string map, bool symbol, bool filter, bool forward)
        => (TextEncoding.UTF8.GetBytes(Document(shape, clip, fractional, map, symbol, filter, forward, true)),
            TextEncoding.UTF8.GetBytes(Document(shape, clip, fractional, map, symbol, filter, forward, false)));

    private static string Document(string shape, string clip, bool fractional, string map, bool symbol, bool filter, bool forward, bool control)
    {
        string geometry = shape switch
        {
            "rect" => "<rect x='4' y='4' width='16' height='16'/>",
            "path" => "<path d='M4 4H20V20H4Z'/>",
            "curve" => "<path d='M4 20Q12 -2 20 20Z'/>",
            "circle" => "<circle cx='12' cy='12' r='8'/>",
            _ => "<" + shape + " points='4 4 20 4 20 20 4 20'/>"
        };
        string definition = symbol ? "<symbol id='s'>" + geometry + "</symbol>" : geometry.Replace("/>", " id='s'/>", StringComparison.Ordinal);
        string clipping = clip switch
        {
            "object" => "<clipPath id='c' clipPathUnits='objectBoundingBox'><rect width='.75' height='.75'/></clipPath>",
            "path" => "<clipPath id='c'><path d='M4 4H24V24H4Z'/></clipPath>",
            _ => "<clipPath id='c'><rect x='4' y='4' width='20' height='20'/></clipPath>"
        };
        string definitions = "<defs>" + clipping + "<filter id='f' filterUnits='userSpaceOnUse' x='-32' y='-32' width='96' height='96' color-interpolation-filters='sRGB'><feOffset/></filter>" + definition + "</defs>";
        string x = fractional ? "3.5" : "4", y = fractional ? "2.5" : "4";
        string effect = filter ? " filter='url(#f)'" : "";
        string body = control
            ? "<g transform='" + map + "'><g transform='translate(" + x + " " + y + ")'><use href='#s' fill='red' clip-path='url(#c)'" + effect + "/></g></g>"
            : "<use href='#s' fill='red' clip-path='url(#c)' x='" + x + "' y='" + y + "' transform='" + map + "'" + effect + "/>";
        return "<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'>" + (forward ? body + definitions : definitions + body) + "</svg>";
    }
}
