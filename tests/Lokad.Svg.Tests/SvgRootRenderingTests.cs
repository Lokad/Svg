using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

using static Lokad.Svg.Tests.SvgPatternContentAssertions;

namespace Lokad.Svg.Tests;

[Trait("Layer", "Rendering")]
public sealed class SvgRootRenderingTests
{
    [Fact]
    public void Group_wrapped_definitions_convert_without_painting_their_content()
    {
        byte[] svg = "<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><g><defs><g><rect width='5' height='5' fill='red'/></g></defs></g></svg>"u8.ToArray();

        byte[] empty = "<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'/>"u8.ToArray();
        Assert.Equal(SvgConverter.ConvertToPng(empty, SvgRenderOptions.Default, CancellationToken.None).ToArray(), SvgConverter.ConvertToPng(svg, SvgRenderOptions.Default, CancellationToken.None).ToArray());
    }

    [Fact]
    public void Switch_fallback_renders_byte_identical_to_direct_content()
    {
        byte[] direct = "<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><rect x='1' y='1' width='2' height='2' fill='red'/></svg>"u8.ToArray();
        byte[] switched = "<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><switch><rect systemLanguage='en' x='9' y='9' width='2' height='2' fill='blue'/><rect x='1' y='1' width='2' height='2' fill='red'/></switch></svg>"u8.ToArray();

        ReadOnlyMemory<byte> directPng = SvgConverter.ConvertToPng(direct, SvgRenderOptions.Default, CancellationToken.None);
        ReadOnlyMemory<byte> switchedPng = SvgConverter.ConvertToPng(switched, SvgRenderOptions.Default, CancellationToken.None);

        Assert.True(directPng.Span.SequenceEqual(switchedPng.Span));
    }

    [Fact]
    public void Renders_switch_content_byte_identical_to_direct_content()
    {
        byte[] direct = "<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><rect x='1' y='1' width='2' height='2' fill='red'/></svg>"u8.ToArray();
        byte[] switched = "<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><switch><rect x='1' y='1' width='2' height='2' fill='red'/></switch></svg>"u8.ToArray();

        ReadOnlyMemory<byte> directPng = SvgConverter.ConvertToPng(direct, SvgRenderOptions.Default, CancellationToken.None);
        ReadOnlyMemory<byte> switchedPng = SvgConverter.ConvertToPng(switched, SvgRenderOptions.Default, CancellationToken.None);

        Assert.True(directPng.Span.SequenceEqual(switchedPng.Span));
    }

    [Theory]
    [InlineData("<defs><clipPath id='c'><rect x='1' y='1' width='3' height='3'/></clipPath></defs><rect width='5' height='5' fill='red' clip-path='url(#c)'/>", "<defs><clipPath id='c'><rect x='1' y='1' width='3' height='3'/></clipPath></defs><rect width='5' height='5' fill='red' clip-path='url(\"#c\")'/>")]
    [InlineData("<defs><mask id='m'><rect x='0' y='0' width='5' height='5' fill='#ffffff'/></mask></defs><rect width='5' height='5' fill='red' mask='url(#m)'/>", "<defs><mask id='m'><rect x='0' y='0' width='5' height='5' fill='#ffffff'/></mask></defs><rect width='5' height='5' fill='red' mask='url(\"#m\")'/>")]
    [InlineData("<defs><pattern id='p' width='2' height='2' patternUnits='userSpaceOnUse'><rect width='1' height='1' fill='red'/></pattern></defs><rect width='5' height='5' fill='url(#p)'/>", "<defs><pattern id='p' width='2' height='2' patternUnits='userSpaceOnUse'><rect width='1' height='1' fill='red'/></pattern></defs><rect width='5' height='5' fill='url(\"#p\")'/>")]
    [InlineData("<defs><linearGradient id='g'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><rect width='5' height='5' fill='url(#g)'/>", "<defs><linearGradient id='g'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><rect width='5' height='5' fill='url(\"#g\")'/>")]
    public void Quoted_local_references_render_byte_identical_to_unquoted_ones(string plainBody, string quotedBody)
    {
        byte[] plain = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'>" + plainBody + "</svg>");
        byte[] quoted = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'>" + quotedBody + "</svg>");

        ReadOnlyMemory<byte> plainPng = SvgConverter.ConvertToPng(plain, SvgRenderOptions.Default, CancellationToken.None);
        ReadOnlyMemory<byte> quotedPng = SvgConverter.ConvertToPng(quoted, SvgRenderOptions.Default, CancellationToken.None);

        Assert.True(plainPng.Span.SequenceEqual(quotedPng.Span));
    }

    [Fact]
    public void Renders_palette_images_byte_identical_to_direct_fills()
    {
        byte[] image = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='2' height='2'><image href='data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAMAAABFaP0WAAAAA1BMVEX/AAAZ4gk3AAAAC0lEQVR4nGNgAAEAAAYAAf6MZ8gAAAAASUVORK5CYII=' x='0' y='0' width='2' height='2'/></svg>");
        byte[] direct = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='2' height='2'><rect width='2' height='2' fill='red'/></svg>");

        ReadOnlyMemory<byte> imagePng = SvgConverter.ConvertToPng(image, SvgRenderOptions.Default, CancellationToken.None);
        ReadOnlyMemory<byte> directPng = SvgConverter.ConvertToPng(direct, SvgRenderOptions.Default, CancellationToken.None);

        Assert.True(imagePng.Span.SequenceEqual(directPng.Span));
    }

    [Fact]
    public void Renders_pattern_fills_on_circles_in_scaled_viewports()
    {
        byte[] original = "<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><defs><pattern id='p' width='8' height='8' patternUnits='userSpaceOnUse'><rect width='8' height='8' fill='red'/></pattern></defs><svg x='0' y='0' width='16' height='16' viewBox='0 0 8 8'><circle cx='4' cy='4' r='4' fill='url(#p)'/></svg></svg>"u8.ToArray();
        using SvgRoot root = SvgRootParser.Parse(original, SvgRenderLimits.Default, CancellationToken.None);
        Assert.Equal(SvgGeometryKind.PatternPolygon, Assert.Single(root.Geometries).Kind);
        byte[] solid = System.Text.Encoding.UTF8.GetBytes(System.Text.Encoding.UTF8.GetString(original).Replace("fill='url(#p)'", "fill='red'", StringComparison.Ordinal));
        Assert.Equal(SvgConverter.ConvertToPng(solid, SvgRenderOptions.Default, CancellationToken.None).ToArray(),
            SvgConverter.ConvertToPng(original, SvgRenderOptions.Default, CancellationToken.None).ToArray());
    }

    [Fact]
    public void Renders_reflected_pattern_fills_on_circles()
    {
        byte[] original = "<svg xmlns='http://www.w3.org/2000/svg'><defs><pattern id='p' width='8' height='8' patternUnits='userSpaceOnUse'><rect width='8' height='8' fill='red'/></pattern></defs><circle cx='4' cy='4' r='4' fill='url(#p)' transform='scale(-1 1)'/></svg>"u8.ToArray();
        using SvgRoot root = SvgRootParser.Parse(original, SvgRenderLimits.Default, CancellationToken.None);
        Assert.Equal(SvgGeometryKind.PatternPolygon, Assert.Single(root.Geometries).Kind);
        byte[] solid = System.Text.Encoding.UTF8.GetBytes(System.Text.Encoding.UTF8.GetString(original).Replace("fill='url(#p)'", "fill='red'", StringComparison.Ordinal));
        Assert.Equal(SvgConverter.ConvertToPng(solid, SvgRenderOptions.Default, CancellationToken.None).ToArray(),
            SvgConverter.ConvertToPng(original, SvgRenderOptions.Default, CancellationToken.None).ToArray());
    }

    [Fact]
    public void Dashed_circle_offsets_shift_run_bands()
    {
        byte[] plain = System.Text.Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"16\"><circle cx=\"8\" cy=\"8\" r=\"6\" fill=\"red\" stroke=\"blue\" stroke-width=\"2\" stroke-dasharray=\"4 2\"/></svg>");
        byte[] shifted = System.Text.Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"16\"><circle cx=\"8\" cy=\"8\" r=\"6\" fill=\"red\" stroke=\"blue\" stroke-width=\"2\" stroke-dasharray=\"4 2\" stroke-dashoffset=\"2\"/></svg>");

        ReadOnlyMemory<byte> plainPng = SvgConverter.ConvertToPng(plain, SvgRenderOptions.Default, CancellationToken.None);
        ReadOnlyMemory<byte> shiftedPng = SvgConverter.ConvertToPng(shifted, SvgRenderOptions.Default, CancellationToken.None);

        Assert.False(shiftedPng.Span.SequenceEqual(plainPng.Span));
    }

    [Fact]
    public void PathLength_scales_dashed_circle_runs()
    {
        byte[] plain = System.Text.Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"16\"><circle cx=\"8\" cy=\"8\" r=\"6\" fill=\"red\" stroke=\"blue\" stroke-width=\"2\" stroke-dasharray=\"4 2\"/></svg>");
        byte[] scaled = System.Text.Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"16\"><circle cx=\"8\" cy=\"8\" r=\"6\" fill=\"red\" stroke=\"blue\" stroke-width=\"2\" stroke-dasharray=\"4 2\" pathLength=\"10\"/></svg>");

        ReadOnlyMemory<byte> plainPng = SvgConverter.ConvertToPng(plain, SvgRenderOptions.Default, CancellationToken.None);
        ReadOnlyMemory<byte> scaledPng = SvgConverter.ConvertToPng(scaled, SvgRenderOptions.Default, CancellationToken.None);

        Assert.False(scaledPng.Span.SequenceEqual(plainPng.Span));
    }

    [Fact]
    public void OutputScale_dashed_circle_matches_enlarged_direct_rendering()
    {
        byte[] small = System.Text.Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"64\" height=\"64\"><circle cx=\"32\" cy=\"32\" r=\"16\" fill=\"none\" stroke=\"red\" stroke-width=\"8\" stroke-dasharray=\"8 8\"/></svg>");
        byte[] large = System.Text.Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"128\" height=\"128\"><circle cx=\"64\" cy=\"64\" r=\"32\" fill=\"none\" stroke=\"red\" stroke-width=\"16\" stroke-dasharray=\"16 16\"/></svg>");
        SvgRenderOptions scaled = new(null, 2, SvgColor.Transparent, SvgRenderLimits.Default);
        SvgRenderOptions direct = new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default);
        ReadOnlyMemory<byte> scaledPng = SvgConverter.ConvertToPng(small, scaled, CancellationToken.None);
        ReadOnlyMemory<byte> directPng = SvgConverter.ConvertToPng(large, direct, CancellationToken.None);

        Assert.True(scaledPng.Span.SequenceEqual(directPng.Span));
    }

    [Fact]
    public void OutputScale_dashed_circle_offset_matches_enlarged_direct_rendering()
    {
        byte[] small = System.Text.Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"64\" height=\"64\"><circle cx=\"32\" cy=\"32\" r=\"16\" fill=\"none\" stroke=\"red\" stroke-width=\"8\" stroke-dasharray=\"8 8\" stroke-dashoffset=\"4\"/></svg>");
        byte[] large = System.Text.Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"128\" height=\"128\"><circle cx=\"64\" cy=\"64\" r=\"32\" fill=\"none\" stroke=\"red\" stroke-width=\"16\" stroke-dasharray=\"16 16\" stroke-dashoffset=\"8\"/></svg>");
        SvgRenderOptions scaled = new(null, 2, SvgColor.Transparent, SvgRenderLimits.Default);
        SvgRenderOptions direct = new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default);
        ReadOnlyMemory<byte> scaledPng = SvgConverter.ConvertToPng(small, scaled, CancellationToken.None);
        ReadOnlyMemory<byte> directPng = SvgConverter.ConvertToPng(large, direct, CancellationToken.None);

        Assert.True(scaledPng.Span.SequenceEqual(directPng.Span));
    }

    [Fact]
    public void OutputScale_dashed_ellipse_matches_enlarged_direct_rendering()
    {
        byte[] small = System.Text.Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"64\" height=\"64\"><ellipse cx=\"32\" cy=\"32\" rx=\"20\" ry=\"12\" fill=\"none\" stroke=\"red\" stroke-width=\"8\" stroke-dasharray=\"8 8\"/></svg>");
        byte[] large = System.Text.Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"128\" height=\"128\"><ellipse cx=\"64\" cy=\"64\" rx=\"40\" ry=\"24\" fill=\"none\" stroke=\"red\" stroke-width=\"16\" stroke-dasharray=\"16 16\"/></svg>");
        SvgRenderOptions scaled = new(null, 2, SvgColor.Transparent, SvgRenderLimits.Default);
        SvgRenderOptions direct = new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default);
        ReadOnlyMemory<byte> scaledPng = SvgConverter.ConvertToPng(small, scaled, CancellationToken.None);
        ReadOnlyMemory<byte> directPng = SvgConverter.ConvertToPng(large, direct, CancellationToken.None);

        Assert.True(scaledPng.Span.SequenceEqual(directPng.Span));
    }

    [Fact]
    public void OutputScale_dashed_ellipse_offset_matches_enlarged_direct_rendering()
    {
        byte[] small = System.Text.Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"64\" height=\"64\"><ellipse cx=\"32\" cy=\"32\" rx=\"20\" ry=\"12\" fill=\"none\" stroke=\"red\" stroke-width=\"8\" stroke-dasharray=\"8 8\" stroke-dashoffset=\"4\"/></svg>");
        byte[] large = System.Text.Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"128\" height=\"128\"><ellipse cx=\"64\" cy=\"64\" rx=\"40\" ry=\"24\" fill=\"none\" stroke=\"red\" stroke-width=\"16\" stroke-dasharray=\"16 16\" stroke-dashoffset=\"8\"/></svg>");
        SvgRenderOptions scaled = new(null, 2, SvgColor.Transparent, SvgRenderLimits.Default);
        SvgRenderOptions direct = new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default);
        ReadOnlyMemory<byte> scaledPng = SvgConverter.ConvertToPng(small, scaled, CancellationToken.None);
        ReadOnlyMemory<byte> directPng = SvgConverter.ConvertToPng(large, direct, CancellationToken.None);

        Assert.True(scaledPng.Span.SequenceEqual(directPng.Span));
    }

    [Fact]
    public void Dashed_ellipse_offsets_shift_run_bands()
    {
        byte[] plain = System.Text.Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"16\"><ellipse cx=\"8\" cy=\"8\" rx=\"6\" ry=\"4\" fill=\"red\" stroke=\"blue\" stroke-width=\"2\" stroke-dasharray=\"4 2\"/></svg>");
        byte[] shifted = System.Text.Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"16\"><ellipse cx=\"8\" cy=\"8\" rx=\"6\" ry=\"4\" fill=\"red\" stroke=\"blue\" stroke-width=\"2\" stroke-dasharray=\"4 2\" stroke-dashoffset=\"2\"/></svg>");

        ReadOnlyMemory<byte> plainPng = SvgConverter.ConvertToPng(plain, SvgRenderOptions.Default, CancellationToken.None);
        ReadOnlyMemory<byte> shiftedPng = SvgConverter.ConvertToPng(shifted, SvgRenderOptions.Default, CancellationToken.None);

        Assert.False(shiftedPng.Span.SequenceEqual(plainPng.Span));
    }

    [Fact]
    public void PathLength_scales_dashed_ellipse_runs()
    {
        byte[] plain = System.Text.Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"16\"><ellipse cx=\"8\" cy=\"8\" rx=\"6\" ry=\"4\" fill=\"red\" stroke=\"blue\" stroke-width=\"2\" stroke-dasharray=\"4 2\"/></svg>");
        byte[] scaled = System.Text.Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"16\"><ellipse cx=\"8\" cy=\"8\" rx=\"6\" ry=\"4\" fill=\"red\" stroke=\"blue\" stroke-width=\"2\" stroke-dasharray=\"4 2\" pathLength=\"10\"/></svg>");

        ReadOnlyMemory<byte> plainPng = SvgConverter.ConvertToPng(plain, SvgRenderOptions.Default, CancellationToken.None);
        ReadOnlyMemory<byte> scaledPng = SvgConverter.ConvertToPng(scaled, SvgRenderOptions.Default, CancellationToken.None);

        Assert.False(scaledPng.Span.SequenceEqual(plainPng.Span));
    }

    [Fact]
    public void Renders_pattern_ellipse_strokes_through_the_distinct_shader_material_route()
    {
        byte[] painted = System.Text.Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"64\" height=\"64\"><defs><pattern id=\"p\" width=\"8\" height=\"8\" patternUnits=\"userSpaceOnUse\"><rect width=\"8\" height=\"8\" fill=\"#0000ff\"/></pattern></defs><ellipse cx=\"32\" cy=\"32\" rx=\"20\" ry=\"12\" fill=\"red\" stroke=\"url(#p)\" stroke-width=\"8\"/></svg>");
        byte[] solid = System.Text.Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"64\" height=\"64\"><ellipse cx=\"32\" cy=\"32\" rx=\"20\" ry=\"12\" fill=\"red\" stroke=\"blue\" stroke-width=\"8\"/></svg>");

        ReadOnlyMemory<byte> paintedPng = SvgConverter.ConvertToPng(painted, SvgRenderOptions.Default, CancellationToken.None);
        ReadOnlyMemory<byte> solidPng = SvgConverter.ConvertToPng(solid, SvgRenderOptions.Default, CancellationToken.None);

        // Independent recipe-3 captures differ on 27 decoded pixels, including
        // 18 alpha lanes. Uniform tile samples do not erase shader operation identity.
        Assert.False(paintedPng.Span.SequenceEqual(solidPng.Span));
    }

    [Theory]
    [InlineData("linearGradient")]
    [InlineData("radialGradient")]
    public void Shader_filled_circles_keep_their_independent_solid_stroke(string paint)
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='12' height='12'><defs><" + paint + " id='g'><stop stop-color='red'/><stop offset='1' stop-color='blue'/></" + paint + "></defs><circle cx='6' cy='6' r='4' fill='url(#g)' stroke='#00ff00' stroke-width='2'/></svg>");
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(1_000_000));
        using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None))
        using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, SvgRenderOptions.Default, budget, CancellationToken.None))
        {
            Assert.Equal(new byte[] { 0, 255, 0, 255 }, surface.Pixels.AsSpan((6 * 12 + 2) * 4, 4).ToArray());
            ReadOnlySpan<byte> center = surface.Pixels.AsSpan((6 * 12 + 6) * 4, 4);
            Assert.InRange(center[0], (byte)1, (byte)254);
            Assert.Equal(0, center[1]);
            Assert.InRange(center[2], (byte)1, (byte)254);
            Assert.Equal(255, center[3]);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Renders_pattern_fills_on_rounded_rectangles_in_scaled_viewports()
    {
        byte[] original = "<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><defs><pattern id='p' width='8' height='8' patternUnits='userSpaceOnUse'><rect width='8' height='8' fill='red'/></pattern></defs><svg x='0' y='0' width='16' height='16' viewBox='0 0 8 8'><rect x='0' y='0' width='8' height='8' rx='2' fill='url(#p)'/></svg></svg>"u8.ToArray();
        using SvgRoot root = SvgRootParser.Parse(original, SvgRenderLimits.Default, CancellationToken.None);
        Assert.Equal(SvgGeometryKind.PatternPolygon, Assert.Single(root.Geometries).Kind);
        byte[] solid = System.Text.Encoding.UTF8.GetBytes(System.Text.Encoding.UTF8.GetString(original).Replace("fill='url(#p)'", "fill='red'", StringComparison.Ordinal));
        Assert.Equal(SvgConverter.ConvertToPng(solid, SvgRenderOptions.Default, CancellationToken.None).ToArray(),
            SvgConverter.ConvertToPng(original, SvgRenderOptions.Default, CancellationToken.None).ToArray());
    }

    [Fact]
    public void Renders_reflected_pattern_fills_on_rounded_rectangles()
    {
        byte[] original = "<svg xmlns='http://www.w3.org/2000/svg'><defs><pattern id='p' width='8' height='8' patternUnits='userSpaceOnUse'><rect width='8' height='8' fill='red'/></pattern></defs><rect x='0' y='0' width='8' height='8' rx='2' fill='url(#p)' transform='scale(-1 1)'/></svg>"u8.ToArray();
        using SvgRoot root = SvgRootParser.Parse(original, SvgRenderLimits.Default, CancellationToken.None);
        Assert.Equal(SvgGeometryKind.PatternPolygon, Assert.Single(root.Geometries).Kind);
        byte[] solid = System.Text.Encoding.UTF8.GetBytes(System.Text.Encoding.UTF8.GetString(original).Replace("fill='url(#p)'", "fill='red'", StringComparison.Ordinal));
        Assert.Equal(SvgConverter.ConvertToPng(solid, SvgRenderOptions.Default, CancellationToken.None).ToArray(),
            SvgConverter.ConvertToPng(original, SvgRenderOptions.Default, CancellationToken.None).ToArray());
    }

    [Fact]
    public void Round_and_square_caps_render_identical_to_butt_on_solid_rectangles()
    {
        byte[] butt = System.Text.Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"8\" height=\"8\"><rect x=\"1\" y=\"1\" width=\"6\" height=\"6\" fill=\"red\" stroke=\"blue\" stroke-width=\"2\"/></svg>");
        byte[] round = System.Text.Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"8\" height=\"8\"><rect x=\"1\" y=\"1\" width=\"6\" height=\"6\" fill=\"red\" stroke=\"blue\" stroke-width=\"2\" stroke-linecap=\"round\"/></svg>");
        byte[] square = System.Text.Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"8\" height=\"8\"><rect x=\"1\" y=\"1\" width=\"6\" height=\"6\" fill=\"red\" stroke=\"blue\" stroke-width=\"2\" stroke-linecap=\"square\"/></svg>");

        ReadOnlyMemory<byte> buttPng = SvgConverter.ConvertToPng(butt, SvgRenderOptions.Default, CancellationToken.None);
        ReadOnlyMemory<byte> roundPng = SvgConverter.ConvertToPng(round, SvgRenderOptions.Default, CancellationToken.None);
        ReadOnlyMemory<byte> squarePng = SvgConverter.ConvertToPng(square, SvgRenderOptions.Default, CancellationToken.None);

        Assert.True(roundPng.Span.SequenceEqual(buttPng.Span));
        Assert.True(squarePng.Span.SequenceEqual(buttPng.Span));
    }

    [Fact]
    public void Round_caps_render_identical_to_butt_on_solid_rounded_rectangles()
    {
        byte[] butt = System.Text.Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"8\" height=\"8\"><rect x=\"0\" y=\"0\" width=\"8\" height=\"8\" rx=\"2\" fill=\"red\" stroke=\"blue\" stroke-width=\"2\"/></svg>");
        byte[] round = System.Text.Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"8\" height=\"8\"><rect x=\"0\" y=\"0\" width=\"8\" height=\"8\" rx=\"2\" fill=\"red\" stroke=\"blue\" stroke-width=\"2\" stroke-linecap=\"round\"/></svg>");

        ReadOnlyMemory<byte> buttPng = SvgConverter.ConvertToPng(butt, SvgRenderOptions.Default, CancellationToken.None);
        ReadOnlyMemory<byte> roundPng = SvgConverter.ConvertToPng(round, SvgRenderOptions.Default, CancellationToken.None);

        Assert.True(roundPng.Span.SequenceEqual(buttPng.Span));
    }

    [Fact]
    public void Smooth_rounded_rectangle_corners_preserve_the_same_pixels_for_bevel_and_miter_joins()
    {
        byte[] miter = System.Text.Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"8\" height=\"8\"><rect x=\"0\" y=\"0\" width=\"8\" height=\"8\" rx=\"2\" fill=\"red\" stroke=\"blue\" stroke-width=\"2\"/></svg>");
        byte[] bevel = System.Text.Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"8\" height=\"8\"><rect x=\"0\" y=\"0\" width=\"8\" height=\"8\" rx=\"2\" fill=\"red\" stroke=\"blue\" stroke-width=\"2\" stroke-linejoin=\"bevel\"/></svg>");

        ReadOnlyMemory<byte> miterPng = SvgConverter.ConvertToPng(miter, SvgRenderOptions.Default, CancellationToken.None);
        ReadOnlyMemory<byte> bevelPng = SvgConverter.ConvertToPng(bevel, SvgRenderOptions.Default, CancellationToken.None);

        Assert.True(bevelPng.Span.SequenceEqual(miterPng.Span));
    }

    [Fact]
    public void Renders_stroke_styles_byte_identical_to_plain_strokes()
    {
        byte[] plain = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='6' height='6'><rect x='1' y='1' width='4' height='4' fill='red' stroke='blue' stroke-width='2'/></svg>");
        byte[] styled = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='6' height='6'><rect x='1' y='1' width='4' height='4' fill='red' stroke='blue' stroke-width='2' stroke-linecap='butt' stroke-linejoin='miter' stroke-dasharray='none'/></svg>");

        ReadOnlyMemory<byte> plainPng = SvgConverter.ConvertToPng(plain, SvgRenderOptions.Default, CancellationToken.None);
        ReadOnlyMemory<byte> styledPng = SvgConverter.ConvertToPng(styled, SvgRenderOptions.Default, CancellationToken.None);

        Assert.True(plainPng.Span.SequenceEqual(styledPng.Span));
    }

    [Fact]
    public void Clips_embedded_images_to_their_clip_path_box()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><clipPath id='c'><rect x='2' y='2' width='4' height='4'/></clipPath></defs><image href='data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGP4z8DwHwAFAAH/iZk9HQAAAABJRU5ErkJggg==' x='0' y='0' width='8' height='8' clip-path='url(#c)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgImageGeometry placedImage = Assert.Single(root.Geometries).Image;
        Assert.Equal((0d, 0d, 8d, 8d), (placedImage.Box.X, placedImage.Box.Y, placedImage.Box.Width, placedImage.Box.Height));
        Assert.Equal(new SvgConstantShaderPaint(255, 0, 0, 255), placedImage.UniformSample);
        SvgPathFillAssertions.MatchesIntegerBands(root, [new(2, 2, 4, 4, new(255, 0, 0, 255), true, null)]);
    }

    [Fact]
    public void Clips_forward_clip_path_definitions_on_embedded_images()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><image href='data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGP4z8DwHwAFAAH/iZk9HQAAAABJRU5ErkJggg==' x='0' y='0' width='8' height='8' clip-path='url(#later)'/><defs><clipPath id='later'><rect x='2' y='2' width='4' height='4'/></clipPath></defs></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgImageGeometry placedImage = Assert.Single(root.Geometries).Image;
        Assert.Equal((0d, 0d, 8d, 8d), (placedImage.Box.X, placedImage.Box.Y, placedImage.Box.Width, placedImage.Box.Height));
        Assert.Equal(new SvgConstantShaderPaint(255, 0, 0, 255), placedImage.UniformSample);
        SvgPathFillAssertions.MatchesIntegerBands(root, [new(2, 2, 4, 4, new(255, 0, 0, 255), true, null)]);
    }

    [Fact]
    public void Clips_forward_bounding_box_clips_on_embedded_images()
    {
        AssertSolidRectanglePixels("<svg xmlns='http://www.w3.org/2000/svg'><image href='data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGP4z8DwHwAFAAH/iZk9HQAAAABJRU5ErkJggg==' x='0' y='0' width='8' height='8' clip-path='url(#later)'/><defs><clipPath id='later' clipPathUnits='objectBoundingBox'><rect x='0' y='0' width='1' height='1'/></clipPath></defs></svg>"u8.ToArray(), new(0, 0, 8, 8, new(255, 0, 0, 255), true, null), 300, 150);
    }

    [Fact]
    public void Applies_masks_to_embedded_images_without_changing_solid_content()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><mask id='m'><rect x='0' y='0' width='8' height='8' fill='#ffffff'/></mask></defs><image href='data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGP4z8DwHwAFAAH/iZk9HQAAAABJRU5ErkJggg==' x='0' y='0' width='8' height='8' mask='url(#m)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgImageGeometry placedImage = Assert.Single(root.Geometries).Image;
        Assert.Equal((0d, 0d, 8d, 8d), (placedImage.Box.X, placedImage.Box.Y, placedImage.Box.Width, placedImage.Box.Height));
        Assert.Equal(new SvgConstantShaderPaint(255, 0, 0, 255), placedImage.UniformSample);
        SvgPathFillAssertions.MatchesIntegerBands(root, [new(0, 0, 8, 8, new(255, 0, 0, 255), true, null)]);
    }

    [Fact]
    public void Applies_element_opacity_to_embedded_images()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><image href='data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGP4z8DwHwAFAAH/iZk9HQAAAABJRU5ErkJggg==' x='0' y='0' width='8' height='8' opacity='0.5'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(SvgGeometryKind.EmbeddedImage, Assert.Single(root.Geometries).Kind);
        using SvgConverter.RasterizedSurface output = SvgConverter.Rasterize(root,
            new(new SvgViewport(8, 8), 1, SvgColor.Transparent, SvgRenderLimits.Default), new RenderBudget(new HostMemoryBudget(1_000_000)), CancellationToken.None);
        for (int offset = 0; offset < output.Pixels.Length; offset += 4)
            Assert.Equal(new byte[] { 255, 0, 0, 128 }, output.Pixels.AsSpan(offset, 4).ToArray());
    }

    [Fact]
    public void Transforms_embedded_image_placement_through_the_admitted_subset()
    {
        using SvgRoot translated = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><image href='data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGP4z8DwHwAFAAH/iZk9HQAAAABJRU5ErkJggg==' x='0' y='0' width='4' height='4' transform='translate(4,0)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);
        using SvgRoot reflected = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><image href='data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGP4z8DwHwAFAAH/iZk9HQAAAABJRU5ErkJggg==' x='0' y='0' width='4' height='4' transform='translate(8,0) scale(-1,1)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgImageGeometry translatedImage = Assert.Single(translated.Geometries).Image;
        Assert.Equal((4d, 0d, 4d, 4d), (translatedImage.Box.X, translatedImage.Box.Y, translatedImage.Box.Width, translatedImage.Box.Height));
        Assert.Equal(new SvgConstantShaderPaint(255, 0, 0, 255), translatedImage.UniformSample);
        SvgImageGeometry reflectedImage = Assert.Single(reflected.Geometries).Image;
        Assert.Equal((4d, 0d, 4d, 4d), (reflectedImage.Box.X, reflectedImage.Box.Y, reflectedImage.Box.Width, reflectedImage.Box.Height));
        Assert.Equal(new SvgConstantShaderPaint(255, 0, 0, 255), reflectedImage.UniformSample);
        SvgPathFillAssertions.MatchesIntegerBands(translated, [new(4, 0, 4, 4, new(255, 0, 0, 255), true, null)]);
        SvgPathFillAssertions.MatchesIntegerBands(reflected, [new(4, 0, 4, 4, new(255, 0, 0, 255), true, null)]);
    }


    [Fact]
    public void Renders_explicit_none_stretch_despite_aspect_mismatch()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg' width='4' height='2'><image href='data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGP4z8DwHwAFAAH/iZk9HQAAAABJRU5ErkJggg==' x='0' y='0' width='4' height='2' preserveAspectRatio='none'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgImageGeometry placed = Assert.Single(root.Geometries).Image;
        Assert.Equal((0d, 0d, 4d, 2d), (placed.Box.X, placed.Box.Y, placed.Box.Width, placed.Box.Height));
        Assert.Equal(new SvgConstantShaderPaint(255, 0, 0, 255), placed.UniformSample);
        SvgPathFillAssertions.MatchesIntegerBands(root, [new(0, 0, 4, 2, new(255, 0, 0, 255), true, null)]);
        Assert.False(root.HasUnsupportedChild);
    }

    [Fact]
    public void Maps_default_meet_placement_for_mismatched_aspects()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg' width='4' height='2'><image href='data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGP4z8DwHwAFAAH/iZk9HQAAAABJRU5ErkJggg==' x='0' y='0' width='4' height='2'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgImageGeometry placedImage = Assert.Single(root.Geometries).Image;
        Assert.Equal((1d, 0d, 2d, 2d), (placedImage.Box.X, placedImage.Box.Y, placedImage.Box.Width, placedImage.Box.Height));
        Assert.Equal(new SvgConstantShaderPaint(255, 0, 0, 255), placedImage.UniformSample);
        SvgPathFillAssertions.MatchesIntegerBands(root, [new(1, 0, 2, 2, new(255, 0, 0, 255), true, null)]);
    }

    [Fact]
    public void Maps_explicit_meet_placement_for_mismatched_aspects()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg' width='4' height='2'><image href='data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGP4z8DwHwAFAAH/iZk9HQAAAABJRU5ErkJggg==' x='0' y='0' width='4' height='2' preserveAspectRatio='xMidYMid meet'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgImageGeometry placedImage = Assert.Single(root.Geometries).Image;
        Assert.Equal((1d, 0d, 2d, 2d), (placedImage.Box.X, placedImage.Box.Y, placedImage.Box.Width, placedImage.Box.Height));
        Assert.Equal(new SvgConstantShaderPaint(255, 0, 0, 255), placedImage.UniformSample);
        SvgPathFillAssertions.MatchesIntegerBands(root, [new(1, 0, 2, 2, new(255, 0, 0, 255), true, null)]);
    }

    [Theory]
    [InlineData("<image href='data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAIAAAABCAYAAAD0In+KAAAADklEQVR4nGP4z8DwH4QBEfcD/ePF9e8AAAAASUVORK5CYII=' x='0' y='0' width='4' height='4' preserveAspectRatio='xMinYMin meet'/>", 0, 0, 4, 2)]
    [InlineData("<image href='data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAIAAAABCAYAAAD0In+KAAAADklEQVR4nGP4z8DwH4QBEfcD/ePF9e8AAAAASUVORK5CYII=' x='0' y='0' width='4' height='4' preserveAspectRatio='xMaxYMax meet'/>", 0, 2, 4, 2)]
    [InlineData("<image href='data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAACCAYAAACZgbYnAAAAEElEQVR4nGP4z8DwnwFEAAAT9gP9uKbxygAAAABJRU5ErkJggg==' x='0' y='0' width='4' height='4'/>", 1, 0, 2, 4)]
    [InlineData("<image href='data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAMAAAABCAYAAAAb4BS0AAAADklEQVR4nGP4z8DwH4YBJuYF+2EsXwgAAAAASUVORK5CYII=' x='0' y='0' width='6' height='6'/>", 0, 2, 6, 2)]
    [InlineData("<image href='data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAIAAAABCAYAAAD0In+KAAAADklEQVR4nGP4z8DwH4QBEfcD/ePF9e8AAAAASUVORK5CYII=' x='0' y='0' width='4' height='4' preserveAspectRatio='xMidYMid slice'/>", 0, 0, 4, 4)]
    public void Maps_meet_and_slice_alignments_to_integral_placement(string image, double x, double y, double width, double height)
    {
        byte[] svg = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'>" + image + "</svg>");

        using SvgRoot root = SvgRootParser.Parse(svg, SvgRenderLimits.Default, CancellationToken.None);

        SvgImageGeometry placedImage = Assert.Single(root.Geometries).Image;
        Assert.Equal((x, y, width, height), (placedImage.Box.X, placedImage.Box.Y, placedImage.Box.Width, placedImage.Box.Height));
        Assert.Equal(new SvgConstantShaderPaint(255, 0, 0, 255), placedImage.UniformSample);
        SvgPathFillAssertions.MatchesIntegerBands(root, [new(x, y, width, height, new(255, 0, 0, 255), true, null)]);
    }

    [Fact]
    public void Prefers_presentation_attributes_over_inline_aspect()
    {
        byte[] attribute = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><image href='data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAIAAAABCAYAAAD0In+KAAAADklEQVR4nGP4z8DwH4QBEfcD/ePF9e8AAAAASUVORK5CYII=' x='0' y='0' width='4' height='4' preserveAspectRatio='none' style='preserveAspectRatio: xMinYMin meet'/></svg>");
        byte[] baseline = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><image href='data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAIAAAABCAYAAAD0In+KAAAADklEQVR4nGP4z8DwH4QBEfcD/ePF9e8AAAAASUVORK5CYII=' x='0' y='0' width='4' height='4' preserveAspectRatio='none'/></svg>");

        ReadOnlyMemory<byte> attributePng = SvgConverter.ConvertToPng(attribute, SvgRenderOptions.Default, CancellationToken.None);
        ReadOnlyMemory<byte> baselinePng = SvgConverter.ConvertToPng(baseline, SvgRenderOptions.Default, CancellationToken.None);

        Assert.True(attributePng.Span.SequenceEqual(baselinePng.Span));
    }

    [Fact]
    public void Ignores_inline_aspect_without_attributes()
    {
        byte[] inline = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><image href='data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAIAAAABCAYAAAD0In+KAAAADklEQVR4nGP4z8DwH4QBEfcD/ePF9e8AAAAASUVORK5CYII=' x='0' y='0' width='4' height='4' style='preserveAspectRatio: xMinYMin meet'/></svg>");
        byte[] baseline = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><image href='data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAIAAAABCAYAAAD0In+KAAAADklEQVR4nGP4z8DwH4QBEfcD/ePF9e8AAAAASUVORK5CYII=' x='0' y='0' width='4' height='4'/></svg>");

        ReadOnlyMemory<byte> inlinePng = SvgConverter.ConvertToPng(inline, SvgRenderOptions.Default, CancellationToken.None);
        ReadOnlyMemory<byte> baselinePng = SvgConverter.ConvertToPng(baseline, SvgRenderOptions.Default, CancellationToken.None);

        Assert.True(inlinePng.Span.SequenceEqual(baselinePng.Span));
    }

    [Fact]
    public void Renders_group_nested_definitions_byte_identical_to_direct_definitions()
    {
        byte[] direct = "<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><defs><rect id='r' width='2' height='2' fill='red'/></defs><use href='#r'/></svg>"u8.ToArray();
        byte[] nested = "<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><g><defs><rect id='r' width='2' height='2' fill='red'/></defs></g><use href='#r'/></svg>"u8.ToArray();

        ReadOnlyMemory<byte> directPng = SvgConverter.ConvertToPng(direct, SvgRenderOptions.Default, CancellationToken.None);
        ReadOnlyMemory<byte> nestedPng = SvgConverter.ConvertToPng(nested, SvgRenderOptions.Default, CancellationToken.None);

        Assert.True(directPng.Span.SequenceEqual(nestedPng.Span));
    }

    [Fact]
    public void Fractional_line_use_offsets_match_direct_native_placement()
    {
        byte[] instance = "<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><line id='line' x2='2' y1='3' y2='3' stroke='red' stroke-width='2'/></defs><use href='#line' x='0.25'/></svg>"u8.ToArray();
        byte[] direct = "<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><line x2='2' y1='3' y2='3' stroke='red' stroke-width='2' transform='translate(.25)'/></svg>"u8.ToArray();
        Assert.Equal(SvgConverter.ConvertToPng(direct, SvgRenderOptions.Default, CancellationToken.None).ToArray(), SvgConverter.ConvertToPng(instance, SvgRenderOptions.Default, CancellationToken.None).ToArray());
    }

    [Fact]
    public void Use_fill_does_not_replace_an_authored_line_stroke()
    {
        byte[] input = "<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><line id='line' x1='2' y1='4' x2='6' y2='4' stroke='blue' stroke-width='2'/></defs><use href='#line' fill='red'/></svg>"u8.ToArray();
        byte[] direct = "<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><line x1='2' y1='4' x2='6' y2='4' stroke='blue' stroke-width='2'/></svg>"u8.ToArray();
        Assert.Equal(SvgConverter.ConvertToPng(direct, SvgRenderOptions.Default, CancellationToken.None).ToArray(), SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None).ToArray());
    }

    [Fact]
    public void Applies_id_stylesheet_rules_to_supported_paths_and_polygons()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><style>#path { fill: red; } #polygon { fill: blue; }</style><path id='path' d='M 0 0 H 1 V 1 H 0 Z'/><polygon id='polygon' points='1,0 2,0 2,1 1,1'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgPathFillAssertions.MatchesIntegerBands(root, [
            new(0, 0, 1, 1, new(255, 0, 0, 255), true, null),
            new(1, 0, 1, 1, new(0, 0, 255, 255), true, null),
        ]);
    }

    [Fact]
    public void Applies_class_stylesheet_rules_to_supported_paths_and_point_lists()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><style>.path { fill: red; } .polygon { fill: blue; } .polyline { fill: lime; }</style><path class='path' d='M 0 0 H 1 V 1 H 0 Z'/><polygon class='polygon' points='1,0 2,0 2,1 1,1'/><polyline class='polyline' points='2,0 3,0 3,1 2,1'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgPathFillAssertions.MatchesIntegerBands(root, [
            new(0, 0, 1, 1, new(255, 0, 0, 255), true, null),
            new(1, 0, 1, 1, new(0, 0, 255, 255), true, null),
            new(2, 0, 1, 1, new(0, 255, 0, 255), true, null),
        ]);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("45")]
    public void Admits_plain_rotated_rectangles(string angle)
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg'><rect width='1' height='1' fill='#ff0000' transform='rotate(" + angle + ")'/></svg>");
        using SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, CancellationToken.None);
        Assert.False(root.HasUnsupportedChild);
        Assert.Equal(SvgGeometryKind.PolygonFill, Assert.Single(root.Geometries).Kind);
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(1_000_000));
        using SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, new(new SvgViewport(8, 8), 1, SvgColor.Transparent, SvgRenderLimits.Default), budget, CancellationToken.None);
        Assert.Equal(255, surface.Pixels[0]);
        Assert.True(surface.Pixels[3] > 0);
    }

    [Theory]
    [InlineData("<defs><linearGradient id='g'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><line x1='0' y1='4' x2='8' y2='4' stroke='url(#g)' stroke-width='2'/>")]
    [InlineData("<defs><linearGradient id='g' x1='0' y1='0' x2='0' y2='1'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><line x1='4' y1='0' x2='4' y2='8' stroke='url(#g)' stroke-width='2'/>")]
    public void Bounding_box_gradients_do_not_paint_zero_area_line_boxes(string body)
    {
        using SvgRoot root = SvgRootParser.Parse(System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg'>" + body + "</svg>"), SvgRenderLimits.Default, CancellationToken.None);

        Assert.False(root.HasUnsupportedChild);
        Assert.False(Assert.Single(root.Geometries).GradientPolygon.HasFill);
        RenderBudget rasterBudget = new RenderBudget(new HostMemoryBudget(1_000_000));
        using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root,
            new(new SvgViewport(8, 8), 1, SvgColor.Transparent, SvgRenderLimits.Default), rasterBudget, CancellationToken.None))
            Assert.All(surface.Pixels, component => Assert.Equal((byte)0, component));
        Assert.Equal(0, rasterBudget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Admits_nonuniform_userspace_gradient_transforms()
    {
        byte[] png = Lokad.Svg.SvgConverter.ConvertToPng("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='16'><defs><linearGradient id='g' gradientUnits='userSpaceOnUse' x1='0' y1='0' x2='16' y2='16'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><g transform='scale(2, 1)'><rect x='0' y='0' width='16' height='16' fill='url(#g)'/></g></svg>"u8.ToArray(), Lokad.Svg.SvgRenderOptions.Default, CancellationToken.None).ToArray();

        Assert.Equal(32, System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16, 4)));
        Assert.Equal(16, System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20, 4)));
    }

    [Fact]
    public void Admits_integer_mapped_pattern_tiles_under_viewBox_scaling()
    {
        byte[] svg = "<svg xmlns='http://www.w3.org/2000/svg' width='64' height='64' viewBox='0 0 32 32'><defs><pattern id='p' width='8' height='8' patternUnits='userSpaceOnUse'><rect width='4' height='8' fill='#ff0000'/></pattern></defs><rect x='0' y='0' width='32' height='32' fill='url(#p)'/></svg>"u8.ToArray();
        byte[] png = Lokad.Svg.SvgConverter.ConvertToPng(svg, Lokad.Svg.SvgRenderOptions.Default, CancellationToken.None).ToArray();

        Assert.Equal(64, System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16, 4)));
        Assert.Equal(64, System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20, 4)));
    }

    [Fact]
    public void Renders_fractional_pattern_phase_under_viewBox_scaling()
    {
        byte[] svg = "<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8' viewBox='0 0 16 16'><defs><pattern id='p' x='1' width='8' height='8' patternUnits='userSpaceOnUse'><rect width='4' height='4' fill='#ff0000'/></pattern></defs><rect x='0' y='0' width='16' height='16' fill='url(#p)'/></svg>"u8.ToArray();

        ReadOnlyMemory<byte> png = SvgConverter.ConvertToPng(svg, SvgRenderOptions.Default, CancellationToken.None);
        Assert.False(png.IsEmpty);
    }

    [Fact]
    public void Renders_pattern_content_strokes_under_viewBox_scaling()
    {
        byte[] svg = "<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8' viewBox='0 0 4 4'><defs><pattern id='p' width='8' height='8' patternUnits='userSpaceOnUse'><rect x='2' y='2' width='4' height='4' fill='red' stroke='blue' stroke-width='2'/></pattern></defs><rect x='0' y='0' width='4' height='4' fill='url(#p)'/></svg>"u8.ToArray();

        ReadOnlyMemory<byte> png = Lokad.Svg.SvgConverter.ConvertToPng(svg, Lokad.Svg.SvgRenderOptions.Default, CancellationToken.None);

        Assert.False(png.IsEmpty);
    }

    [Fact]
    public void Scaled_pattern_output_scale_matches_doubled_rendering()
    {
        byte[] small = "<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8' ><defs><pattern id='p' width='4' height='4' patternUnits='userSpaceOnUse'><rect width='2' height='2' fill='red'/></pattern></defs><rect x='0' y='0' width='8' height='8' fill='url(#p)'/></svg>"u8.ToArray();
        byte[] big = "<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><defs><pattern id='p' width='8' height='8' patternUnits='userSpaceOnUse'><rect width='4' height='4' fill='red'/></pattern></defs><rect x='0' y='0' width='16' height='16' fill='url(#p)'/></svg>"u8.ToArray();

        Lokad.Svg.SvgRenderOptions scaled = new(null, 2, SvgColor.Transparent, SvgRenderLimits.Default);
        byte[] doubled = Lokad.Svg.SvgConverter.ConvertToPng(small, scaled, CancellationToken.None).ToArray();
        byte[] reference = Lokad.Svg.SvgConverter.ConvertToPng(big, Lokad.Svg.SvgRenderOptions.Default, CancellationToken.None).ToArray();

        Assert.Equal(reference, doubled);
    }

    [Fact]
    public void Offscreen_reflected_pattern_rectangles_paint_nothing()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><pattern id='p' width='2' height='2' patternUnits='userSpaceOnUse'><rect width='1' height='1' fill='red'/></pattern></defs><rect width='4' height='4' fill='url(#p)' transform='scale(-1,1)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.False(root.HasUnsupportedChild);
        RenderBudget rasterBudget = new RenderBudget(new HostMemoryBudget(1_000_000));
        using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root,
            new(new SvgViewport(8, 8), 1, SvgColor.Transparent, SvgRenderLimits.Default), rasterBudget, CancellationToken.None))
            Assert.All(surface.Pixels, component => Assert.Equal((byte)0, component));
        Assert.Equal(0, rasterBudget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Renders_reflected_paths_identical_to_hand_mirrored_baselines()
    {
        byte[] reflected = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><path d='M1,1 H4 V6 H1 Z' fill='red' transform='translate(8,0) scale(-1,1)'/></svg>");
        byte[] baseline = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><path d='M7,1 H4 V6 H7 Z' fill='red'/></svg>");

        ReadOnlyMemory<byte> reflectedPng = SvgConverter.ConvertToPng(reflected, SvgRenderOptions.Default, CancellationToken.None);
        ReadOnlyMemory<byte> baselinePng = SvgConverter.ConvertToPng(baseline, SvgRenderOptions.Default, CancellationToken.None);

        Assert.True(reflectedPng.Span.SequenceEqual(baselinePng.Span));
    }

    [Fact]
    public void Lowers_symbol_gradient_stroke_instances_through_viewBox_mapping()
    {
        byte[] input = "<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='g' gradientUnits='userSpaceOnUse' x1='0' y1='0' x2='4' y2='0'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><defs><symbol id='s' viewBox='0 0 4 4'><rect x='0' y='0' width='4' height='4' fill='#ff0000' stroke='url(#g)' stroke-width='2'/></symbol></defs><use href='#s' width='8' height='8' opacity='0.5'/></svg>"u8.ToArray();
        byte[] expected = "<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='g' gradientUnits='userSpaceOnUse' x1='0' y1='0' x2='4' y2='0'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><g opacity='0.5'><svg width='8' height='8' viewBox='0 0 4 4'><rect width='4' height='4' fill='#ff0000' stroke='url(#g)' stroke-width='2'/></svg></g></svg>"u8.ToArray();
        Assert.Equal(SvgConverter.ConvertToPng(expected, SvgRenderOptions.Default, CancellationToken.None).ToArray(), SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None).ToArray());
        using SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, CancellationToken.None);
        SvgDrawCommand opacityScope = Assert.Single(root.Commands, command => command.Kind == SvgDrawCommandKind.BeginGroup && command.SourceOpacity.Value != 1);
        SvgRecordedPaintAssertions.OpacityScope(opacityScope, new(128, 0.5f));
    }
    [Fact]
    public void Lowers_gradient_stroke_rect_instances_through_placement()
    {
        byte[] input = "<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='g' gradientUnits='userSpaceOnUse' x1='0' y1='0' x2='8' y2='0'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><defs><rect id='r' width='8' height='8' fill='#ff0000' stroke='url(#g)' stroke-width='2'/></defs><use href='#r' x='8'/></svg>"u8.ToArray();
        byte[] expected = "<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='g' gradientUnits='userSpaceOnUse' x1='0' y1='0' x2='8' y2='0'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><g transform='translate(8)'><rect width='8' height='8' fill='#ff0000' stroke='url(#g)' stroke-width='2'/></g></svg>"u8.ToArray();
        Assert.Equal(SvgConverter.ConvertToPng(expected, SvgRenderOptions.Default, CancellationToken.None).ToArray(), SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None).ToArray());
    }

    [Fact]
    public void Renders_reflected_gradient_paths_identical_to_hand_mirrored_baselines()
    {
        byte[] reflected = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><linearGradient id='g'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><path d='M1,1 H7 V3 H3 V7 H1 Z' fill='url(#g)' transform='translate(8,0) scale(-1,1)'/></svg>");
        byte[] baseline = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><linearGradient id='g' x1='1' y1='0' x2='0' y2='0'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><path d='M7,1 H1 V3 H5 V7 H7 Z' fill='url(#g)'/></svg>");

        ReadOnlyMemory<byte> reflectedPng = SvgConverter.ConvertToPng(reflected, SvgRenderOptions.Default, CancellationToken.None);
        ReadOnlyMemory<byte> baselinePng = SvgConverter.ConvertToPng(baseline, SvgRenderOptions.Default, CancellationToken.None);

        Assert.True(reflectedPng.Span.SequenceEqual(baselinePng.Span));
    }

    [Fact]
    public void Renders_degenerate_pad_gradient_paths_as_end_stop_solids()
    {
        byte[] gradient = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><linearGradient id='g' x1='1' y1='1' x2='1' y2='1'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><path d='M1,1 H7 V3 H3 V7 H1 Z' fill='url(#g)'/></svg>");
        byte[] baseline = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><path d='M1,1 H7 V3 H3 V7 H1 Z' fill='blue'/></svg>");

        ReadOnlyMemory<byte> gradientPng = SvgConverter.ConvertToPng(gradient, SvgRenderOptions.Default, CancellationToken.None);
        ReadOnlyMemory<byte> baselinePng = SvgConverter.ConvertToPng(baseline, SvgRenderOptions.Default, CancellationToken.None);

        Assert.True(gradientPng.Span.SequenceEqual(baselinePng.Span));
    }

    [Fact]
    public void Renders_single_stop_gradients_identical_to_solid_fills()
    {
        byte[] gradient = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><linearGradient id='g'><stop offset='0.3' stop-color='#ff0000'/></linearGradient></defs><rect width='8' height='8' fill='url(#g)'/></svg>");
        byte[] baseline = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><rect width='8' height='8' fill='red'/></svg>");

        ReadOnlyMemory<byte> gradientPng = SvgConverter.ConvertToPng(gradient, SvgRenderOptions.Default, CancellationToken.None);
        ReadOnlyMemory<byte> baselinePng = SvgConverter.ConvertToPng(baseline, SvgRenderOptions.Default, CancellationToken.None);

        Assert.True(gradientPng.Span.SequenceEqual(baselinePng.Span));
    }
    [Fact]
    public void Renders_empty_transforms_identical_to_untransformed_content()
    {
        byte[] plain = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><rect x='1' y='1' width='6' height='6' fill='red'/></svg>");
        byte[] empty = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><rect x='1' y='1' width='6' height='6' fill='red' transform=''/></svg>");
        byte[] blank = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><rect x='1' y='1' width='6' height='6' fill='red' transform='   '/></svg>");

        ReadOnlyMemory<byte> plainPng = SvgConverter.ConvertToPng(plain, SvgRenderOptions.Default, CancellationToken.None);
        ReadOnlyMemory<byte> emptyPng = SvgConverter.ConvertToPng(empty, SvgRenderOptions.Default, CancellationToken.None);
        ReadOnlyMemory<byte> blankPng = SvgConverter.ConvertToPng(blank, SvgRenderOptions.Default, CancellationToken.None);

        Assert.True(emptyPng.Span.SequenceEqual(plainPng.Span));
        Assert.True(blankPng.Span.SequenceEqual(plainPng.Span));
    }

    [Fact]
    public void Renders_empty_gradient_transforms_identical_to_untransformed_vectors()
    {
        byte[] plain = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><linearGradient id='g'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><rect width='8' height='8' fill='url(#g)'/></svg>");
        byte[] empty = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><linearGradient id='g' gradientTransform=''><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><rect width='8' height='8' fill='url(#g)'/></svg>");

        ReadOnlyMemory<byte> plainPng = SvgConverter.ConvertToPng(plain, SvgRenderOptions.Default, CancellationToken.None);
        ReadOnlyMemory<byte> emptyPng = SvgConverter.ConvertToPng(empty, SvgRenderOptions.Default, CancellationToken.None);

        Assert.True(emptyPng.Span.SequenceEqual(plainPng.Span));
    }

    [Fact]
    public void Renders_empty_pattern_transforms_identical_to_unshifted_tiles()
    {
        byte[] plain = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><pattern id='p' width='4' height='4' patternUnits='userSpaceOnUse'><rect width='4' height='4' fill='red'/></pattern></defs><rect width='8' height='8' fill='url(#p)'/></svg>");
        byte[] empty = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><pattern id='p' width='4' height='4' patternUnits='userSpaceOnUse' patternTransform=''><rect width='4' height='4' fill='red'/></pattern></defs><rect width='8' height='8' fill='url(#p)'/></svg>");

        ReadOnlyMemory<byte> plainPng = SvgConverter.ConvertToPng(plain, SvgRenderOptions.Default, CancellationToken.None);
        ReadOnlyMemory<byte> emptyPng = SvgConverter.ConvertToPng(empty, SvgRenderOptions.Default, CancellationToken.None);

        Assert.True(emptyPng.Span.SequenceEqual(plainPng.Span));
    }

    [Fact]
    public void Renders_single_stop_use_instances_as_solid_bands()
    {
        byte[] instanced = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='8'><defs><linearGradient id='g'><stop offset='0.3' stop-color='#ff0000'/></linearGradient></defs><defs><polygon id='p' points='0,0 8,0 8,4 4,4 4,8 0,8' fill='url(#g)'/></defs><use href='#p' x='8'/></svg>");
        byte[] baseline = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='8'><polygon points='8,0 16,0 16,4 12,4 12,8 8,8' fill='red'/></svg>");

        ReadOnlyMemory<byte> instancedPng = SvgConverter.ConvertToPng(instanced, SvgRenderOptions.Default, CancellationToken.None);
        ReadOnlyMemory<byte> baselinePng = SvgConverter.ConvertToPng(baseline, SvgRenderOptions.Default, CancellationToken.None);

        Assert.True(instancedPng.Span.SequenceEqual(baselinePng.Span));
    }
    [Fact]
    public void Retains_gradient_fill_opacity_separately_from_element_opacity()
    {
        byte[] faded = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><linearGradient id='g'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><rect width='8' height='8' fill='url(#g)' fill-opacity='0.5'/></svg>");
        byte[] baseline = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><linearGradient id='g'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><rect width='8' height='8' fill='url(#g)' opacity='0.5'/></svg>");

        ReadOnlyMemory<byte> fadedPng = SvgConverter.ConvertToPng(faded, SvgRenderOptions.Default, CancellationToken.None);
        ReadOnlyMemory<byte> baselinePng = SvgConverter.ConvertToPng(baseline, SvgRenderOptions.Default, CancellationToken.None);

        // Fresh browser captures differ for these unchanged inputs.
        Assert.False(fadedPng.Span.SequenceEqual(baselinePng.Span));
        using SvgRoot first = SvgRootParser.Parse(faded, SvgRenderLimits.Default, CancellationToken.None);
        using SvgRoot second = SvgRootParser.Parse(baseline, SvgRenderLimits.Default, CancellationToken.None);
        SvgRecordedPaintAssertions.ShaderAlpha(first, 128 / 255f);
        Assert.DoesNotContain(first.Commands, command => command.Kind == SvgDrawCommandKind.BeginGroup);
        SvgRecordedPaintAssertions.ShaderAlpha(second, 1);
        SvgRecordedPaintAssertions.ElementOpacity(second, new(128, 0.5f));
    }

    [Fact]
    public void Preserves_shader_byte_and_element_float_opacity_when_combined()
    {
        byte[] layered = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><linearGradient id='g'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><rect width='8' height='8' fill='url(#g)' opacity='0.5' fill-opacity='0.5'/></svg>");
        byte[] baseline = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><linearGradient id='g'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><rect width='8' height='8' fill='url(#g)' opacity='0.25'/></svg>");

        ReadOnlyMemory<byte> layeredPng = SvgConverter.ConvertToPng(layered, SvgRenderOptions.Default, CancellationToken.None);
        ReadOnlyMemory<byte> baselinePng = SvgConverter.ConvertToPng(baseline, SvgRenderOptions.Default, CancellationToken.None);

        // Fresh browser captures differ for these unchanged inputs.
        Assert.False(layeredPng.Span.SequenceEqual(baselinePng.Span));
        using SvgRoot first = SvgRootParser.Parse(layered, SvgRenderLimits.Default, CancellationToken.None);
        using SvgRoot second = SvgRootParser.Parse(baseline, SvgRenderLimits.Default, CancellationToken.None);
        SvgRecordedPaintAssertions.ShaderAlpha(first, 128 / 255f);
        SvgRecordedPaintAssertions.ElementOpacity(first, new(128, 0.5f));
        SvgRecordedPaintAssertions.ShaderAlpha(second, 1);
        SvgRecordedPaintAssertions.ElementOpacity(second, new(64, 0.25f));
    }

    [Fact]
    public void Retains_gradient_path_fill_opacity_separately_from_element_opacity()
    {
        byte[] faded = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><linearGradient id='g'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><path d='M1,1 H7 V3 H3 V7 H1 Z' fill='url(#g)' fill-opacity='0.5'/></svg>");
        byte[] baseline = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><linearGradient id='g'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><path d='M1,1 H7 V3 H3 V7 H1 Z' fill='url(#g)' opacity='0.5'/></svg>");

        ReadOnlyMemory<byte> fadedPng = SvgConverter.ConvertToPng(faded, SvgRenderOptions.Default, CancellationToken.None);
        ReadOnlyMemory<byte> baselinePng = SvgConverter.ConvertToPng(baseline, SvgRenderOptions.Default, CancellationToken.None);

        // Fresh browser captures differ for these unchanged inputs.
        Assert.False(fadedPng.Span.SequenceEqual(baselinePng.Span));
        using SvgRoot first = SvgRootParser.Parse(faded, SvgRenderLimits.Default, CancellationToken.None);
        using SvgRoot second = SvgRootParser.Parse(baseline, SvgRenderLimits.Default, CancellationToken.None);
        SvgRecordedPaintAssertions.ShaderAlpha(first, 128 / 255f);
        Assert.DoesNotContain(first.Commands, command => command.Kind == SvgDrawCommandKind.BeginGroup);
        SvgRecordedPaintAssertions.ShaderAlpha(second, 1);
        SvgRecordedPaintAssertions.ElementOpacity(second, new(128, 0.5f));
    }

    [Fact]
    public void Renders_pattern_path_fill_opacity_identical_to_opacity()
    {
        byte[] faded = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><pattern id='p' width='4' height='4' patternUnits='userSpaceOnUse'><rect width='2' height='2' fill='red'/></pattern></defs><path d='M1,1 H7 V3 H3 V7 H1 Z' fill='url(#p)' fill-opacity='0.5'/></svg>");
        byte[] baseline = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><pattern id='p' width='4' height='4' patternUnits='userSpaceOnUse'><rect width='2' height='2' fill='red'/></pattern></defs><path d='M1,1 H7 V3 H3 V7 H1 Z' fill='url(#p)' opacity='0.5'/></svg>");

        ReadOnlyMemory<byte> fadedPng = SvgConverter.ConvertToPng(faded, SvgRenderOptions.Default, CancellationToken.None);
        ReadOnlyMemory<byte> baselinePng = SvgConverter.ConvertToPng(baseline, SvgRenderOptions.Default, CancellationToken.None);

        Assert.True(fadedPng.Span.SequenceEqual(baselinePng.Span));
    }

    [Fact]
    public void Renders_pattern_rect_fill_opacity_identical_to_opacity()
    {
        byte[] faded = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><pattern id='p' width='4' height='4' patternUnits='userSpaceOnUse'><rect width='2' height='2' fill='red'/></pattern></defs><rect width='8' height='8' fill='url(#p)' fill-opacity='0.5'/></svg>");
        byte[] baseline = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><pattern id='p' width='4' height='4' patternUnits='userSpaceOnUse'><rect width='2' height='2' fill='red'/></pattern></defs><rect width='8' height='8' fill='url(#p)' opacity='0.5'/></svg>");

        ReadOnlyMemory<byte> fadedPng = SvgConverter.ConvertToPng(faded, SvgRenderOptions.Default, CancellationToken.None);
        ReadOnlyMemory<byte> baselinePng = SvgConverter.ConvertToPng(baseline, SvgRenderOptions.Default, CancellationToken.None);

        Assert.True(fadedPng.Span.SequenceEqual(baselinePng.Span));
    }

    [Fact]
    public void Clips_use_instances_to_their_clip_path_box()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><rect id='r' x='0' y='0' width='8' height='8' fill='red'/><clipPath id='c'><rect x='2' y='2' width='4' height='4'/></clipPath></defs><use href='#r' clip-path='url(#c)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(new SvgRectangle(0, 0, 8, 8, new SvgColor(255, 0, 0, 255), true, null), Assert.Single(root.Rectangles));
        SvgPathFillAssertions.MatchesIntegerBands(root, [new SvgRectangle(2, 2, 4, 4, new SvgColor(255, 0, 0, 255), true, null)]);
    }

    [Fact]
    public void Direct_child_clip_path_matches_defs_wrapped_clip()
    {
        ReadOnlyMemory<byte> direct = SvgConverter.ConvertToPng(System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><clipPath id='c'><rect x='2' y='0' width='4' height='8'/></clipPath><path d='M0,0 H8 V8 H0 Z' fill='red' clip-path='url(#c)'/></svg>"), SvgRenderOptions.Default, CancellationToken.None);
        ReadOnlyMemory<byte> wrapped = SvgConverter.ConvertToPng(System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><clipPath id='c'><rect x='2' y='0' width='4' height='8'/></clipPath></defs><path d='M0,0 H8 V8 H0 Z' fill='red' clip-path='url(#c)'/></svg>"), SvgRenderOptions.Default, CancellationToken.None);

        Assert.True(direct.Span.SequenceEqual(wrapped.Span));
    }

    [Fact]
    public void Authored_rectangular_path_accepts_a_multi_contour_clip()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><clipPath id='c'><path d='M0,0 H4 V4 H0 Z M5,5 H8 V8 H5 Z'/></clipPath><path d='M0,0 H8 V8 H0 Z' fill='red' clip-path='url(#c)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);
        SvgPathFillAssertions.MatchesIntegerBands(root, [
            new(0, 0, 4, 4, new(255, 0, 0, 255), true, null),
            new(5, 5, 3, 3, new(255, 0, 0, 255), true, null),
        ]);
    }

    [Fact]
    public void Clips_symbol_instances_to_their_clip_path_box()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><symbol id='s' viewBox='0 0 8 8'><rect width='8' height='8' fill='red'/></symbol><clipPath id='c'><rect x='2' y='0' width='4' height='8'/></clipPath></defs><use href='#s' x='0' y='0' width='8' height='8' clip-path='url(#c)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(new SvgRectangle(0, 0, 8, 8, new SvgColor(255, 0, 0, 255), true, null), Assert.Single(root.Rectangles));
        SvgPathFillAssertions.MatchesIntegerBands(root, [new SvgRectangle(2, 0, 4, 8, new SvgColor(255, 0, 0, 255), true, null)]);
    }

    [Fact]
    public void Crops_partially_covering_mask_content()
    {
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(1_000_000));
        using (SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><mask id='m'><rect x='0' y='0' width='2' height='2' fill='#ffffff'/></mask></defs><rect x='0' y='0' width='8' height='8' fill='red' mask='url(#m)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, budget, CancellationToken.None))
        using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, new(new SvgViewport(8, 8), 1, SvgColor.Transparent, SvgRenderLimits.Default), budget, CancellationToken.None))
        {
            Assert.Equal(new byte[] { 255, 0, 0, 255 }, surface.Pixels.AsSpan((0 * 8 + 0) * 4, 4).ToArray());
            Assert.Equal(new byte[] { 0, 0, 0, 0 }, surface.Pixels.AsSpan((4 * 8 + 4) * 4, 4).ToArray());
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Crops_transformed_mask_content()
    {
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(1_000_000));
        using (SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><mask id='m'><rect x='0' y='0' width='8' height='8' fill='#ffffff' transform='translate(2,0) scale(2,1)'/></mask></defs><rect x='0' y='0' width='8' height='8' fill='red' mask='url(#m)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, budget, CancellationToken.None))
        using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, new(new SvgViewport(8, 8), 1, SvgColor.Transparent, SvgRenderLimits.Default), budget, CancellationToken.None))
        {
            Assert.Equal(new byte[] { 255, 0, 0, 255 }, surface.Pixels.AsSpan((3 * 8 + 3) * 4, 4).ToArray());
            Assert.Equal(new byte[] { 0, 0, 0, 0 }, surface.Pixels.AsSpan((3 * 8 + 0) * 4, 4).ToArray());
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Crops_partially_covering_mask_content_on_bands()
    {
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(1_000_000));
        using (SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><mask id='m'><rect x='0' y='0' width='2' height='2' fill='#ffffff'/></mask></defs><path d='M0,0 H8 V8 H0 Z' fill='red' mask='url(#m)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, budget, CancellationToken.None))
        using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, new(new SvgViewport(8, 8), 1, SvgColor.Transparent, SvgRenderLimits.Default), budget, CancellationToken.None))
        {
            Assert.Equal(new byte[] { 255, 0, 0, 255 }, surface.Pixels.AsSpan((0 * 8 + 0) * 4, 4).ToArray());
            Assert.Equal(new byte[] { 0, 0, 0, 0 }, surface.Pixels.AsSpan((4 * 8 + 4) * 4, 4).ToArray());
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Partially_covering_use_masks_preserve_forward_binding_and_coverage()
    {
        byte[] backward = "<svg xmlns='http://www.w3.org/2000/svg'><defs><rect id='r' x='0' y='0' width='8' height='8' fill='red'/><mask id='m'><rect x='0' y='0' width='2' height='2' fill='#ffffff'/></mask></defs><use href='#r' mask='url(#m)'/></svg>"u8.ToArray();
        byte[] forward = "<svg xmlns='http://www.w3.org/2000/svg'><use href='#r' mask='url(#m)'/><defs><rect id='r' x='0' y='0' width='8' height='8' fill='red'/><mask id='m'><rect x='0' y='0' width='2' height='2' fill='#ffffff'/></mask></defs></svg>"u8.ToArray();
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(1_000_000));
        byte[] expected;
        SvgRenderOptions options = new(new SvgViewport(8, 8), 1, SvgColor.Transparent, SvgRenderLimits.Default);
        using (SvgRoot root = SvgRootParser.Parse(backward, SvgRenderLimits.Default, budget, CancellationToken.None))
        using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, options, budget, CancellationToken.None))
        {
            expected = surface.Pixels.ToArray();
            Assert.Equal(new byte[] { 255, 0, 0, 255 }, expected.AsSpan(0, 4).ToArray());
            Assert.Equal(new byte[] { 0, 0, 0, 0 }, expected.AsSpan((4 * 8 + 4) * 4, 4).ToArray());
        }
        using (SvgRoot root = SvgRootParser.Parse(forward, SvgRenderLimits.Default, budget, CancellationToken.None))
        using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, options, budget, CancellationToken.None))
            Assert.Equal(expected, surface.Pixels);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Crops_mask_content_inside_its_custom_region()
    {
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(1_000_000));
        using (SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><mask id='m' maskUnits='userSpaceOnUse' x='0' y='0' width='8' height='8'><rect x='0' y='0' width='2' height='2' fill='#ffffff'/></mask></defs><rect x='0' y='0' width='8' height='8' fill='red' mask='url(#m)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, budget, CancellationToken.None))
        using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, new(new SvgViewport(8, 8), 1, SvgColor.Transparent, SvgRenderLimits.Default), budget, CancellationToken.None))
        {
            Assert.Equal(new byte[] { 255, 0, 0, 255 }, surface.Pixels.AsSpan((0 * 8 + 0) * 4, 4).ToArray());
            Assert.Equal(new byte[] { 0, 0, 0, 0 }, surface.Pixels.AsSpan((4 * 8 + 4) * 4, 4).ToArray());
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Renders_nested_viewport_opacity_identical_to_group_opacity()
    {
        byte[] viewport = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><svg x='0' y='0' width='8' height='8' opacity='0.5'><rect x='0' y='0' width='8' height='8' fill='red'/><rect x='2' y='2' width='4' height='4' fill='blue'/></svg></svg>");
        byte[] group = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><g opacity='0.5'><rect x='0' y='0' width='8' height='8' fill='red'/><rect x='2' y='2' width='4' height='4' fill='blue'/></g></svg>");

        ReadOnlyMemory<byte> viewportPng = SvgConverter.ConvertToPng(viewport, SvgRenderOptions.Default, CancellationToken.None);
        ReadOnlyMemory<byte> groupPng = SvgConverter.ConvertToPng(group, SvgRenderOptions.Default, CancellationToken.None);

        Assert.True(viewportPng.Span.SequenceEqual(groupPng.Span));
    }

    [Fact]
    public void Original_nested_viewport_and_group_opacity_compose_separate_layers()
    {
        AssertSolidRectanglePixels("<svg xmlns='http://www.w3.org/2000/svg' opacity='0.5'><svg x='0' y='0' width='8' height='8' opacity='0.5'><rect width='8' height='8' fill='red'/></svg></svg>"u8.ToArray(), new(0, 0, 8, 8, new(255, 0, 0, 64), true, null), 300, 150);
        AssertSolidRectanglePixels("<svg xmlns='http://www.w3.org/2000/svg'><svg x='0' y='0' width='8' height='8' opacity='0.5'><g opacity='0.5'><rect width='8' height='8' fill='red'/></g></svg></svg>"u8.ToArray(), new(0, 0, 8, 8, new(255, 0, 0, 64), true, null), 300, 150);
    }

    [Fact]
    public void Nested_percentage_viewport_resolves_against_explicit_viewport()
    {
        // The explicit viewport sizes percentage roots, so omitted nested dimensions match the absolute twin.
        SvgRenderOptions options = new(new SvgViewport(64, 32), 1, new SvgColor(0, 0, 0, 0), SvgRenderLimits.Default);
        byte[] percent = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='100%' height='100%'><svg><rect x='4' y='4' width='8' height='8' fill='red'/></svg></svg>");
        byte[] absolute = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='64' height='32'><svg x='0' y='0' width='64' height='32'><rect x='4' y='4' width='8' height='8' fill='red'/></svg></svg>");

        ReadOnlyMemory<byte> percentPng = SvgConverter.ConvertToPng(percent, options, CancellationToken.None);
        ReadOnlyMemory<byte> absolutePng = SvgConverter.ConvertToPng(absolute, options, CancellationToken.None);

        Assert.True(percentPng.Span.SequenceEqual(absolutePng.Span));
    }

    [Fact]
    public void Lowers_intersecting_contours_with_nonzero_winding()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><path d='M1,1 H6 V6 H1 Z M3,0 H5 V8 H3 Z' fill='red'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgPathFillAssertions.MatchesIntegerBands(root, [
            new SvgRectangle(3, 0, 2, 1, new SvgColor(255, 0, 0, 255), true, null),
            new SvgRectangle(1, 1, 2, 5, new SvgColor(255, 0, 0, 255), true, null),
            new SvgRectangle(3, 1, 2, 5, new SvgColor(255, 0, 0, 255), true, null),
            new SvgRectangle(5, 1, 1, 5, new SvgColor(255, 0, 0, 255), true, null),
            new SvgRectangle(3, 6, 2, 2, new SvgColor(255, 0, 0, 255), true, null),
        ]);
    }

    [Fact]
    public void Lowers_intersecting_contours_with_evenodd_pairing()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><path d='M1,1 H6 V6 H1 Z M3,0 H5 V8 H3 Z' fill='red' fill-rule='evenodd'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgPathFillAssertions.MatchesIntegerBands(root, [
            new SvgRectangle(3, 0, 2, 1, new SvgColor(255, 0, 0, 255), true, null),
            new SvgRectangle(1, 1, 2, 5, new SvgColor(255, 0, 0, 255), true, null),
            new SvgRectangle(5, 1, 1, 5, new SvgColor(255, 0, 0, 255), true, null),
            new SvgRectangle(3, 6, 2, 2, new SvgColor(255, 0, 0, 255), true, null),
        ]);
    }

    [Fact]
    public void Lowers_corner_touching_contours_independently()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><path d='M0,0 H4 V4 H0 Z M4,4 H8 V8 H4 Z' fill='red'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgPathFillAssertions.MatchesIntegerBands(root, [
            new SvgRectangle(0, 0, 4, 4, new SvgColor(255, 0, 0, 255), true, null),
            new SvgRectangle(4, 4, 4, 4, new SvgColor(255, 0, 0, 255), true, null),
        ]);
    }

    [Fact]
    public void Lowers_self_crossing_contours()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><path d='M0,4 H8 V5 H5 V8 H3 V0 H2 V5 H0 Z' fill='red'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgPathFillAssertions.MatchesIntegerBands(root, [
            new SvgRectangle(2, 0, 1, 4, new SvgColor(255, 0, 0, 255), true, null),
            new SvgRectangle(0, 4, 2, 1, new SvgColor(255, 0, 0, 255), true, null),
            new SvgRectangle(3, 4, 5, 1, new SvgColor(255, 0, 0, 255), true, null),
            new SvgRectangle(3, 5, 2, 3, new SvgColor(255, 0, 0, 255), true, null),
        ]);
    }

    [Fact]
    public void Lowers_abutting_rect_contours_to_adjacent_bands()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><path d='M0,0 H4 V4 H0 Z M4,0 H8 V4 H4 Z' fill='red'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgPathFillAssertions.MatchesIntegerBands(root, [
            new SvgRectangle(0, 0, 4, 4, new SvgColor(255, 0, 0, 255), true, null),
            new SvgRectangle(4, 0, 4, 4, new SvgColor(255, 0, 0, 255), true, null),
        ]);
    }

    [Fact]
    public void Lowers_abutting_rect_contours_with_evenodd_pairing()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><path d='M0,0 H4 V4 H0 Z M4,0 H8 V4 H4 Z' fill='red' fill-rule='evenodd'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgPathFillAssertions.MatchesIntegerBands(root, [
            new SvgRectangle(0, 0, 4, 4, new SvgColor(255, 0, 0, 255), true, null),
            new SvgRectangle(4, 0, 4, 4, new SvgColor(255, 0, 0, 255), true, null),
        ]);
    }

    [Fact]
    public void Lowers_opposite_orientation_abut_to_adjacent_bands()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><path d='M0,0 H4 V4 H0 Z M8,0 H4 V4 H8 Z' fill='red'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgPathFillAssertions.MatchesIntegerBands(root, [
            new SvgRectangle(0, 0, 4, 4, new SvgColor(255, 0, 0, 255), true, null),
            new SvgRectangle(4, 0, 4, 4, new SvgColor(255, 0, 0, 255), true, null),
        ]);
    }

    [Fact]
    public void Lowers_retraced_edge_contour_to_a_single_band()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><path d='M0,0 H4 V4 H0 V0 H4 Z' fill='red'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgPathFillAssertions.MatchesIntegerBands(root, [
            new SvgRectangle(0, 0, 4, 4, new SvgColor(255, 0, 0, 255), true, null),
        ]);
    }

    [Fact]
    public void Lowers_vertical_spike_contour_by_cancellation()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><path d='M0,0 H2 V4 V0 H4 V4 H0 Z' fill='red' fill-rule='evenodd'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgPathFillAssertions.MatchesIntegerBands(root, [
            new SvgRectangle(0, 0, 2, 4, new SvgColor(255, 0, 0, 255), true, null),
            new SvgRectangle(2, 0, 2, 4, new SvgColor(255, 0, 0, 255), true, null),
        ]);
    }

    [Fact]
    public void Lowers_vertical_spike_contour_with_nonzero_sweep()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><path d='M0,0 H2 V4 V0 H4 V4 H0 Z' fill='red'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgPathFillAssertions.MatchesIntegerBands(root, [
            new SvgRectangle(0, 0, 2, 4, new SvgColor(255, 0, 0, 255), true, null),
            new SvgRectangle(2, 0, 2, 4, new SvgColor(255, 0, 0, 255), true, null),
        ]);
    }

    [Fact]
    public void Lowers_nested_touching_hole_with_evenodd_pairing()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><path d='M0,0 H10 V10 H0 Z M6,4 H10 V6 H6 Z' fill='red' fill-rule='evenodd'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgPathFillAssertions.MatchesIntegerBands(root, [
            new SvgRectangle(0, 0, 10, 4, new SvgColor(255, 0, 0, 255), true, null),
            new SvgRectangle(0, 4, 6, 2, new SvgColor(255, 0, 0, 255), true, null),
            new SvgRectangle(0, 6, 10, 4, new SvgColor(255, 0, 0, 255), true, null),
        ]);
    }

    [Fact]
    public void Lowers_stacked_identical_contours_to_empty_with_evenodd()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><path d='M0,0 H4 V4 H0 Z M0,0 H4 V4 H0 Z' fill='red' fill-rule='evenodd'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgPathFillAssertions.MatchesIntegerBands(root, []);
    }

    [Fact]
    public void Lowers_triple_covered_bands_with_nonzero_sweep()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><path d='M0,0 H2 V4 H0 Z M0,0 H2 V4 H0 Z M0,0 H2 V4 H0 Z' fill='red'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgPathFillAssertions.MatchesIntegerBands(root, [
            new SvgRectangle(0, 0, 2, 4, new SvgColor(255, 0, 0, 255), true, null),
        ]);
    }

    [Fact]
    public void Lowers_closed_axis_aligned_rectangle_paths()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><path d='M 1,2 H 4 V 6 H 1 Z' fill='red'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgPathFillAssertions.MatchesIntegerBands(root, [new SvgRectangle(1, 2, 3, 4, new SvgColor(255, 0, 0, 255), true, null)]);
    }

    [Fact]
    public void Lowers_paths_with_adjacent_commands_without_materializing_tokens()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><path d='M0,0H3V1H1V3H0Z' fill='red'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgPathFillAssertions.MatchesIntegerBands(root, [
            new SvgRectangle(0, 0, 3, 1, new SvgColor(255, 0, 0, 255), true, null),
            new SvgRectangle(0, 1, 1, 2, new SvgColor(255, 0, 0, 255), true, null),
        ]);
    }

    [Fact]
    public void Lowers_closed_axis_aligned_line_rectangle_paths()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><path d='M 1,2 L 4,2 L 4,6 L 1,6 z' fill='red'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgPathFillAssertions.MatchesIntegerBands(root, [new SvgRectangle(1, 2, 3, 4, new SvgColor(255, 0, 0, 255), true, null)]);
    }

    [Fact]
    public void Lowers_closed_axis_aligned_line_paths_to_non_overlapping_rectangle_bands()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><path d='M 0,0 H 3 V 1 H 1 V 3 H 0 Z' fill='red'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgPathFillAssertions.MatchesIntegerBands(root, [
            new SvgRectangle(0, 0, 3, 1, new SvgColor(255, 0, 0, 255), true, null),
            new SvgRectangle(0, 1, 1, 2, new SvgColor(255, 0, 0, 255), true, null),
        ]);
    }

    [Fact]
    public void Retains_orthogonal_path_commands_as_one_native_fill()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><path d='M 0,0 H 2 V 1 H 0 Z' fill='red'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.PolygonFill, geometry.Kind);
        Assert.NotNull(geometry.AuthoredPath);
        SvgPathFillAssertions.MatchesIntegerBands(root, [new(0, 0, 2, 1, new(255, 0, 0, 255), true, null)]);
    }

    [Fact]
    public void Retains_closed_axis_aligned_polygon_coverage()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><polygon points='0,0 3,0 3,1 1,1 1,3 0,3' fill='red'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.PolygonFill, geometry.Kind);
        SvgAuthoredPath source = Assert.IsType<SvgAuthoredPath>(geometry.AuthoredPath);
        Assert.Equal(7, source.Commands.Length);
        Assert.Equal(SvgAuthoredPathVerb.Close, source.Commands[^1].Verb);
        SvgPathFillAssertions.MatchesIntegerBands(root,
            [new(0, 0, 3, 1, new(255, 0, 0, 255), true, null), new(0, 1, 1, 2, new(255, 0, 0, 255), true, null)]);
    }

    [Fact]
    public void Retains_open_polyline_source_with_closed_fill_coverage()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><polyline points='0,0 3,0 3,1 1,1 1,3 0,3' fill='red'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.PolygonFill, geometry.Kind);
        SvgAuthoredPath source = Assert.IsType<SvgAuthoredPath>(geometry.AuthoredPath);
        Assert.Equal(7, source.Commands.Length);
        Assert.Equal(SvgAuthoredPathVerb.End, source.Commands[^1].Verb);
        SvgPathFillAssertions.MatchesIntegerBands(root,
            [new(0, 0, 3, 1, new(255, 0, 0, 255), true, null), new(0, 1, 1, 2, new(255, 0, 0, 255), true, null)]);
    }

    [Fact]
    public void Lowers_self_intersecting_orthogonal_contours()
    {
        using SvgRoot polygon = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><polygon points='0,0 3,0 3,3 1,3 1,1 4,1 4,4 0,4'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);
        using SvgRoot path = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><path d='M 0,0 H 3 V 3 H 1 V 1 H 4 V 4 H 0 Z'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgRectangle[] expected =
        [
            new SvgRectangle(0, 0, 3, 1, new SvgColor(0, 0, 0, 255), true, null),
            new SvgRectangle(0, 1, 1, 2, new SvgColor(0, 0, 0, 255), true, null),
            new SvgRectangle(1, 1, 2, 2, new SvgColor(0, 0, 0, 255), true, null),
            new SvgRectangle(3, 1, 1, 2, new SvgColor(0, 0, 0, 255), true, null),
            new SvgRectangle(0, 3, 4, 1, new SvgColor(0, 0, 0, 255), true, null),
        ];
        SvgPathFillAssertions.MatchesIntegerBands(polygon, expected);
        SvgPathFillAssertions.MatchesIntegerBands(path, expected);
    }

    [Fact]
    public void Lowers_evenodd_counter_path_to_bands_with_hole()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><path d='M8,8 H56 V56 H8 Z M20,20 V44 H44 V20 Z' fill-rule='evenodd' fill='red'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgPathFillAssertions.MatchesIntegerBands(root, [
            new SvgRectangle(8, 8, 48, 12, new SvgColor(255, 0, 0, 255), true, null),
            new SvgRectangle(8, 20, 12, 24, new SvgColor(255, 0, 0, 255), true, null),
            new SvgRectangle(44, 20, 12, 24, new SvgColor(255, 0, 0, 255), true, null),
            new SvgRectangle(8, 44, 48, 12, new SvgColor(255, 0, 0, 255), true, null),
        ]);
    }

    [Fact]
    public void Lowers_same_winding_nonzero_counter_path_to_solid_bands()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><path d='M8,8 H56 V56 H8 Z M20,20 H44 V44 H20 Z' fill-rule='nonzero' fill='red'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgPathFillAssertions.MatchesIntegerBands(root, [
            new SvgRectangle(8, 8, 48, 12, new SvgColor(255, 0, 0, 255), true, null),
            new SvgRectangle(8, 20, 12, 24, new SvgColor(255, 0, 0, 255), true, null),
            new SvgRectangle(20, 20, 24, 24, new SvgColor(255, 0, 0, 255), true, null),
            new SvgRectangle(44, 20, 12, 24, new SvgColor(255, 0, 0, 255), true, null),
            new SvgRectangle(8, 44, 48, 12, new SvgColor(255, 0, 0, 255), true, null),
        ]);
    }

    [Fact]
    public void Lowers_overlapping_contours_with_nonzero_winding()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><path d='M0,0 H4 V4 H0 Z M2,2 H6 V6 H2 Z' fill='red'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgPathFillAssertions.MatchesIntegerBands(root, [
            new SvgRectangle(0, 0, 4, 2, new SvgColor(255, 0, 0, 255), true, null),
            new SvgRectangle(0, 2, 2, 2, new SvgColor(255, 0, 0, 255), true, null),
            new SvgRectangle(2, 2, 2, 2, new SvgColor(255, 0, 0, 255), true, null),
            new SvgRectangle(4, 2, 2, 2, new SvgColor(255, 0, 0, 255), true, null),
            new SvgRectangle(2, 4, 4, 2, new SvgColor(255, 0, 0, 255), true, null),
        ]);
    }

    [Fact]
    public void Admits_trailing_unclosed_path_subpath()
    {
        using SvgRoot closed = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><path d='M0,0 H2 V2 H0 Z M4,4 H6 V6 H4 Z'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);
        using SvgRoot open = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><path d='M0,0 H2 V2 H0 Z M4,4 H6 V6 H4'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgPathFillAssertions.Equivalent(closed, open);
    }

    [Fact]
    public void Admits_nonfinal_unclosed_path_subpath()
    {
        using SvgRoot closed = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><path d='M0,0 H2 V2 H0 Z M4,4 H6 V6 H4 Z'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);
        using SvgRoot open = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><path d='M0,0 H2 V2 H0 M4,4 H6 V6 H4 Z'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgPathFillAssertions.Equivalent(closed, open);
    }

    [Fact]
    public void Admits_explicit_return_to_start_path_subpath()
    {
        using SvgRoot closed = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><path d='M0,0 H2 V2 H0 Z M4,4 H6 V6 H4 Z'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);
        using SvgRoot returned = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><path d='M0,0 H2 V2 H0 V0 M4,4 H6 V6 H4 Z'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgPathFillAssertions.Equivalent(closed, returned);
    }

    [Fact]
    public void Lowers_relative_rectangle_path_to_same_bands_as_absolute()
    {
        using SvgRoot relative = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><path d='m 1,2 h 3 v 4 h -3 z' fill='red'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);
        using SvgRoot absolute = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><path d='M 1,2 H 4 V 6 H 1 Z' fill='red'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgPathFillAssertions.Equivalent(absolute, relative);
        SvgPathFillAssertions.MatchesIntegerBands(relative, [
            new SvgRectangle(1, 2, 3, 4, new SvgColor(255, 0, 0, 255), true, null),
        ]);
    }

    [Fact]
    public void Lowers_implicit_lineto_path_to_same_bands_as_explicit()
    {
        using SvgRoot implicitPath = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><path d='M0,0 3,0 3,1 1,1 1,3 0,3Z' fill='red'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgPathFillAssertions.MatchesIntegerBands(implicitPath, [
            new SvgRectangle(0, 0, 3, 1, new SvgColor(255, 0, 0, 255), true, null),
            new SvgRectangle(0, 1, 1, 2, new SvgColor(255, 0, 0, 255), true, null),
        ]);
    }

    [Fact]
    public void Lowers_compact_repeated_commands_to_same_bands()
    {
        using SvgRoot compact = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><path d='M0,0H2 3V1H0Z' fill='red'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgPathFillAssertions.MatchesIntegerBands(compact, [
            new SvgRectangle(0, 0, 3, 1, new SvgColor(255, 0, 0, 255), true, null),
        ]);
    }

    [Fact]
    public void Lowers_relative_counter_subpath_to_the_same_hole()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><path d='M8,8H56V56H8Z m12,12 v24 h24 v-24 Z' fill-rule='evenodd' fill='red'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgPathFillAssertions.MatchesIntegerBands(root, [
            new SvgRectangle(8, 8, 48, 12, new SvgColor(255, 0, 0, 255), true, null),
            new SvgRectangle(8, 20, 12, 24, new SvgColor(255, 0, 0, 255), true, null),
            new SvgRectangle(44, 20, 12, 24, new SvgColor(255, 0, 0, 255), true, null),
            new SvgRectangle(8, 44, 48, 12, new SvgColor(255, 0, 0, 255), true, null),
        ]);
    }

    [Fact]
    public void Original_nested_group_opacity_composes_separate_layers()
    {
        AssertSolidRectanglePixels("<svg xmlns='http://www.w3.org/2000/svg'><g opacity='0.5'><g opacity='0.5'><rect width='1' height='1'/></g></g></svg>"u8.ToArray(), new(0, 0, 1, 1, new(0, 0, 0, 64), true, null), 300, 150);
    }
    [Fact]
    public void Keeps_a_stroked_rect_use_fully_inside_its_nested_viewport()
    {
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(1_000_000));
        using (SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><rect id='r' x='10' y='10' width='4' height='4' fill='red' stroke='blue' stroke-width='2'/></defs><svg x='8' y='8' width='32' height='32'><use href='#r'/></svg></svg>"u8.ToArray(), SvgRenderLimits.Default, budget, CancellationToken.None))
        using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, new(new SvgViewport(32, 32), 1, SvgColor.Transparent, SvgRenderLimits.Default), budget, CancellationToken.None))
        {
            for (int y = 0; y < 32; y++)
                for (int x = 0; x < 32; x++)
                {
                    byte[] expected = x >= 19 && x < 21 && y >= 19 && y < 21
                        ? [255, 0, 0, 255]
                        : x >= 17 && x < 23 && y >= 17 && y < 23 ? [0, 0, 255, 255] : [0, 0, 0, 0];
                    Assert.Equal(expected, surface.Pixels.AsSpan((y * 32 + x) * 4, 4).ToArray());
                }
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Clips_a_stroked_rect_use_crossing_a_nested_viewport_boundary()
    {
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(1_000_000));
        using (SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><rect id='r' x='0' y='0' width='8' height='8' fill='red' stroke='blue' stroke-width='2'/></defs><svg x='8' y='8' width='4' height='4'><use href='#r'/></svg></svg>"u8.ToArray(), SvgRenderLimits.Default, budget, CancellationToken.None))
        using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, new(new SvgViewport(16, 16), 1, SvgColor.Transparent, SvgRenderLimits.Default), budget, CancellationToken.None))
        {
            for (int y = 0; y < 16; y++)
                for (int x = 0; x < 16; x++)
                {
                    byte[] expected = x >= 8 && x < 12 && y >= 8 && y < 12
                        ? x == 8 || y == 8 ? [0, 0, 255, 255] : [255, 0, 0, 255]
                        : [0, 0, 0, 0];
                    Assert.Equal(expected, surface.Pixels.AsSpan((y * 16 + x) * 4, 4).ToArray());
                }
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Original_stroke_crossing_viewport_preserves_source_and_physical_clip()
    {
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(8_000_000));
        using (SvgRoot scene = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><svg x='8' y='8' width='32' height='32'><rect width='64' height='64' fill='red' stroke='blue' stroke-width='2'/></svg></svg>"u8.ToArray(), SvgRenderLimits.Default, budget, CancellationToken.None))
        using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(scene, SvgRenderOptions.Default, budget, CancellationToken.None))
        {
            Assert.Equal(300, surface.Width);
            Assert.Equal(150, surface.Height);
            for (int y = 0; y < surface.Height; y++)
            for (int x = 0; x < surface.Width; x++)
            {
                SvgColor expected = x >= 8 && x < 40 && y >= 8 && y < 40
                    ? x == 8 || y == 8 ? new(0, 0, 255, 255) : new(255, 0, 0, 255) : SvgColor.Transparent;
                int offset = (y * surface.Width + x) * 4;
                Assert.Equal(expected, new SvgColor(surface.Pixels[offset], surface.Pixels[offset + 1], surface.Pixels[offset + 2], surface.Pixels[offset + 3]));
            }
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Applies_fill_override_to_retained_polygon_source()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><polygon id='p' points='0,0 4,0 4,4 0,4'/></defs><use href='#p' fill='blue'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.PolygonFill, geometry.Kind);
        Assert.Equal(new SvgColor(0, 0, 255, 255), geometry.Polygon.Color);
        Assert.NotNull(geometry.AuthoredPath);
        SvgPathFillAssertions.MatchesIntegerBands(root, [new(0, 0, 4, 4, new(0, 0, 255, 255), true, null)]);
    }

    [Fact]
    public void Use_fill_preserves_the_authored_fill_and_stroke_of_a_polygon_pair()
    {
        byte[] input = "<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><polygon id='p' points='0,0 8,0 0,8' fill='red' stroke='blue' stroke-width='2'/></defs><use href='#p' fill='green'/></svg>"u8.ToArray();
        byte[] direct = "<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><polygon points='0,0 8,0 0,8' fill='red' stroke='blue' stroke-width='2'/></svg>"u8.ToArray();
        Assert.Equal(SvgConverter.ConvertToPng(direct, SvgRenderOptions.Default, CancellationToken.None).ToArray(), SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None).ToArray());
    }

    [Fact]
    public void Forward_group_masks_compose_after_nested_opacity()
    {
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(1_000_000));
        using (SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><g mask='url(#later)'><g opacity='0.5'><rect x='0' y='0' width='8' height='8' fill='red'/></g></g><defs><mask id='later'><rect x='0' y='0' width='8' height='8' fill='#808080'/></mask></defs></svg>"u8.ToArray(), SvgRenderLimits.Default, budget, CancellationToken.None))
        using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, new(new SvgViewport(8, 8), 1, SvgColor.Transparent, SvgRenderLimits.Default), budget, CancellationToken.None))
        {
            Assert.Equal(5, root.Commands.Length);
            Assert.Equal(255, root.Commands[0].Opacity);
            Assert.Equal(128, root.Commands[0].MaskAlpha);
            Assert.Equal(128, root.Commands[1].Opacity);
            Assert.Equal(255, root.Commands[1].MaskAlpha);
            Assert.NotEqual(root.Commands[0].RecordingScope, root.Commands[1].RecordingScope);
            for (int pixel = 0; pixel < 64; pixel++)
                Assert.Equal(new byte[] { 255, 0, 0, 64 }, surface.Pixels.AsSpan(pixel * 4, 4).ToArray());
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Original_forward_mask_composes_nested_group_opacity()
    {
        AssertSolidRectanglePixels("<svg xmlns='http://www.w3.org/2000/svg'><g opacity='0.5' mask='url(#later)'><g opacity='0.5'><rect x='0' y='0' width='8' height='8' fill='red'/></g></g><defs><mask id='later'><rect x='0' y='0' width='8' height='8' fill='#808080'/></mask></defs></svg>"u8.ToArray(), new(0, 0, 8, 8, new(255, 0, 0, 32), true, null), 300, 150);
    }

    [Fact]
    public void Crops_forward_masks_on_groups_with_uncovered_children()
    {
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(1_000_000));
        using (SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><g mask='url(#later)'><rect x='0' y='0' width='8' height='8' fill='red'/></g><defs><mask id='later'><rect x='0' y='0' width='2' height='2' fill='#ffffff'/></mask></defs></svg>"u8.ToArray(), SvgRenderLimits.Default, budget, CancellationToken.None))
        using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, new(new SvgViewport(8, 8), 1, SvgColor.Transparent, SvgRenderLimits.Default), budget, CancellationToken.None))
        {
            Assert.Equal(new byte[] { 255, 0, 0, 255 }, surface.Pixels.AsSpan((0 * 8 + 0) * 4, 4).ToArray());
            Assert.Equal(new byte[] { 0, 0, 0, 0 }, surface.Pixels.AsSpan((4 * 8 + 4) * 4, 4).ToArray());
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }
    [Fact]
    public void Clips_forward_bounding_box_clips_on_groups()
    {
        AssertSolidRectanglePixels("<svg xmlns='http://www.w3.org/2000/svg'><g clip-path='url(#later)'><rect width='16' height='16' fill='red'/></g><defs><clipPath id='later' clipPathUnits='objectBoundingBox'><rect x='0' y='0' width='1' height='1'/></clipPath></defs></svg>"u8.ToArray(), new(0, 0, 16, 16, new(255, 0, 0, 255), true, null), 300, 150);
    }

    [Fact]
    public void Clips_forward_clips_on_masked_groups()
    {
        AssertSolidRectanglePixels("<svg xmlns='http://www.w3.org/2000/svg'><defs><mask id='fade'><rect x='0' y='0' width='16' height='16' fill='#ffffff'/></mask></defs><g mask='url(#fade)' clip-path='url(#later)'><rect width='16' height='16' fill='red'/></g><defs><clipPath id='later'><rect x='4' y='4' width='8' height='8'/></clipPath></defs></svg>"u8.ToArray(), new(4, 4, 8, 8, new(255, 0, 0, 255), true, null), 300, 150);
    }

    [Fact]
    public void Original_forward_group_clip_maps_scaled_viewport()
    {
        AssertSolidRectanglePixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><svg x='0' y='0' width='16' height='16' viewBox='0 0 8 8'><g clip-path='url(#later)'><rect width='8' height='8' fill='red'/></g></svg><defs><clipPath id='later'><rect x='2' y='2' width='4' height='4'/></clipPath></defs></svg>"u8.ToArray(), new(4, 4, 8, 8, new(255, 0, 0, 255), true, null), 16, 16);
    }

    [Fact]
    public void Original_enclosing_group_clip_stays_outside_child_viewport_map()
    {
        AssertSolidRectanglePixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><g clip-path='url(#later)'><svg x='0' y='0' width='16' height='16' viewBox='0 0 8 8'><rect width='8' height='8' fill='red'/></svg></g><defs><clipPath id='later'><rect x='2' y='2' width='4' height='4'/></clipPath></defs></svg>"u8.ToArray(), new(2, 2, 4, 4, new(255, 0, 0, 255), true, null), 16, 16);
    }

    [Fact]
    public void Clips_forward_clip_paths_on_groups_with_use_instances()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><g clip-path='url(#c)'><use href='#p'/></g><rect x='0' y='0' width='2' height='2' fill='blue'/><defs><polygon id='p' points='0,0 8,0 8,4 4,4 4,8 0,8' fill='red'/><clipPath id='c'><rect x='2' y='2' width='4' height='4'/></clipPath></defs></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(2, root.Geometries.Length);
        Assert.Equal(SvgGeometryKind.PolygonFill, root.Geometries[0].Kind);
        Assert.Equal(new SvgClipBox(2, 2, 4, 4), root.Geometries[0].Polygon.Clip);
        Assert.Equal(new SvgRectangle(0, 0, 2, 2, new(0, 0, 255, 255), true, null), root.Geometries[1].Rectangle);
        SvgPathFillAssertions.MatchesIntegerBands(root,
            [new(2, 2, 4, 2, new(255, 0, 0, 255), true, null), new(2, 4, 2, 2, new(255, 0, 0, 255), true, null),
             new(0, 0, 2, 2, new(0, 0, 255, 255), true, null)]);
    }

    [Fact]
    public void Original_scaled_leaf_clip_preserves_empty_intersection()
    {
        AssertSolidRectanglePixels("<svg xmlns='http://www.w3.org/2000/svg' width='64' height='64'><defs><clipPath id='window'><rect x='8' y='8' width='8' height='8'/></clipPath></defs><svg x='8' y='8' width='48' height='48' viewBox='0 0 24 24'><rect x='0' y='0' width='8' height='8' fill='red' clip-path='url(#window)'/></svg></svg>"u8.ToArray(), new(0, 0, 0, 0, new(255, 0, 0, 255), true, null), 64, 64);
    }

    [Fact]
    public void Original_scaled_viewport_mask_maps_region_and_coverage()
    {
        AssertSolidRectanglePixels("<svg xmlns='http://www.w3.org/2000/svg' width='64' height='64'><defs><mask id='window' maskUnits='userSpaceOnUse' x='1' y='1' width='2' height='2'><rect x='0' y='0' width='8' height='8' fill='#808080'/></mask></defs><svg x='8' y='8' width='48' height='48' viewBox='0 0 24 24'><rect x='0' y='0' width='8' height='8' fill='red' mask='url(#window)'/></svg></svg>"u8.ToArray(), new(10, 10, 4, 4, new(255, 0, 0, 128), true, null), 64, 64);
    }

    [Fact]
    public void Renders_forward_clips_identical_to_backward_clips_on_rounded_rectangles()
    {
        byte[] forward = System.Text.Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"64\" height=\"64\"><rect x=\"0\" y=\"0\" width=\"64\" height=\"64\" rx=\"12\" fill=\"red\" clip-path=\"url(#later)\"/><defs><clipPath id=\"later\"><rect x=\"8\" y=\"8\" width=\"48\" height=\"48\"/></clipPath></defs></svg>");
        byte[] backward = System.Text.Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"64\" height=\"64\"><defs><clipPath id=\"later\"><rect x=\"8\" y=\"8\" width=\"48\" height=\"48\"/></clipPath></defs><rect x=\"0\" y=\"0\" width=\"64\" height=\"64\" rx=\"12\" fill=\"red\" clip-path=\"url(#later)\"/></svg>");

        ReadOnlyMemory<byte> forwardPng = SvgConverter.ConvertToPng(forward, SvgRenderOptions.Default, CancellationToken.None);
        ReadOnlyMemory<byte> backwardPng = SvgConverter.ConvertToPng(backward, SvgRenderOptions.Default, CancellationToken.None);

        Assert.True(forwardPng.Span.SequenceEqual(backwardPng.Span));
    }

    [Fact]
    public void Clips_a_rounded_rectangle_to_its_object_bounding_box_clip()
    {
        AssertSolidRectanglePixels("<svg xmlns=\"http://www.w3.org/2000/svg\"><defs><clipPath id=\"c\" clipPathUnits=\"objectBoundingBox\"><rect x=\"0.25\" y=\"0.25\" width=\"0.5\" height=\"0.5\"/></clipPath></defs><rect x=\"0\" y=\"0\" width=\"64\" height=\"64\" rx=\"12\" fill=\"red\" clip-path=\"url(#c)\"/></svg>"u8.ToArray(), new(16, 16, 32, 32, new(255, 0, 0, 255), true, null), 300, 150);
    }

    [Fact]
    public void Clips_a_rectangle_to_its_object_bounding_box_clip()
    {
        AssertSolidRectanglePixels("<svg xmlns='http://www.w3.org/2000/svg'><defs><clipPath id='window' clipPathUnits='objectBoundingBox'><rect x='0.25' y='0.25' width='0.5' height='0.5'/></clipPath></defs><rect x='8' y='8' width='48' height='48' fill='red' clip-path='url(#window)'/></svg>"u8.ToArray(), new(20, 20, 24, 24, new(255, 0, 0, 255), true, null), 300, 150);
    }

    [Fact]
    public void Preserves_full_fraction_clips_on_orthogonal_paths()
    {
        AssertSolidRectanglePixels("<svg xmlns='http://www.w3.org/2000/svg'><defs><clipPath id='window' clipPathUnits='objectBoundingBox'><rect x='0' y='0' width='1' height='1'/></clipPath></defs><path d='M 0,0 H 4 V 4 H 0 Z' fill='red' clip-path='url(#window)'/></svg>"u8.ToArray(), new(0, 0, 4, 4, new(255, 0, 0, 255), true, null), 300, 150);
    }
    [Fact]
    public void Clips_a_polygon_to_its_object_bounding_box_clip()
    {
        AssertEquivalentPixels("<svg xmlns='http://www.w3.org/2000/svg'><defs><clipPath id='window' clipPathUnits='objectBoundingBox'><rect x='0.25' y='0.25' width='0.5' height='0.5'/></clipPath></defs><polygon points='0,0 8,0 8,4 4,4 4,8 0,8' fill='red' clip-path='url(#window)'/></svg>"u8.ToArray(),
            "<svg xmlns='http://www.w3.org/2000/svg'><defs><clipPath id='c'><rect x='2' y='2' width='4' height='4'/></clipPath></defs><polygon points='0,0 8,0 8,4 4,4 4,8 0,8' fill='red' clip-path='url(#c)'/></svg>"u8.ToArray());
    }

    [Fact]
    public void Clips_a_diagonal_path_to_its_object_bounding_box_clip()
    {
        AssertEquivalentPixels("<svg xmlns='http://www.w3.org/2000/svg'><defs><clipPath id='window' clipPathUnits='objectBoundingBox'><rect x='0.25' y='0.25' width='0.5' height='0.5'/></clipPath></defs><path d='M0,0 L8,0 L0,8 Z' fill='red' clip-path='url(#window)'/></svg>"u8.ToArray(),
            "<svg xmlns='http://www.w3.org/2000/svg'><defs><clipPath id='c'><rect x='2' y='2' width='4' height='4'/></clipPath></defs><path d='M0,0 L8,0 L0,8 Z' fill='red' clip-path='url(#c)'/></svg>"u8.ToArray());
    }

    [Fact]
    public void Passes_full_fraction_clips_on_curved_paths_without_clipping()
    {
        AssertEquivalentPixels("<svg xmlns='http://www.w3.org/2000/svg'><defs><clipPath id='window' clipPathUnits='objectBoundingBox'><rect x='0' y='0' width='1' height='1'/></clipPath></defs><path d='M0,0 C8,0 8,8 0,8 Z' fill='red' clip-path='url(#window)'/></svg>"u8.ToArray(),
            "<svg xmlns='http://www.w3.org/2000/svg'><defs><clipPath id='c'><rect x='0' y='0' width='6' height='8'/></clipPath></defs><path d='M0,0 C8,0 8,8 0,8 Z' fill='red' clip-path='url(#c)'/></svg>"u8.ToArray());
    }

    [Fact]
    public void Clips_a_curved_path_to_its_object_bounding_box_clip()
    {
        AssertEquivalentPixels("<svg xmlns='http://www.w3.org/2000/svg'><defs><clipPath id='window' clipPathUnits='objectBoundingBox'><rect x='0.25' y='0.25' width='0.5' height='0.5'/></clipPath></defs><path d='M0,0 C8,0 8,8 0,8 Z' fill='red' clip-path='url(#window)'/></svg>"u8.ToArray(),
            "<svg xmlns='http://www.w3.org/2000/svg'><defs><clipPath id='c'><rect x='1.5' y='2' width='3' height='4'/></clipPath></defs><path d='M0,0 C8,0 8,8 0,8 Z' fill='red' clip-path='url(#c)'/></svg>"u8.ToArray());
    }

    [Fact]
    public void Clips_a_fractional_polygon_to_its_object_bounding_box_clip()
    {
        AssertEquivalentPixels("<svg xmlns='http://www.w3.org/2000/svg'><defs><clipPath id='window' clipPathUnits='objectBoundingBox'><rect x='0.25' y='0.25' width='0.5' height='0.5'/></clipPath></defs><polygon points='0.5,0 8,0 0,8' fill='red' clip-path='url(#window)'/></svg>"u8.ToArray(),
            "<svg xmlns='http://www.w3.org/2000/svg'><defs><clipPath id='c'><rect x='2' y='2' width='4' height='4'/></clipPath></defs><polygon points='0.5,0 8,0 0,8' fill='red' clip-path='url(#c)'/></svg>"u8.ToArray());
    }

    [Fact]
    public void Clips_a_fractional_curve_to_its_object_bounding_box_clip()
    {
        AssertEquivalentPixels("<svg xmlns='http://www.w3.org/2000/svg'><defs><clipPath id='window' clipPathUnits='objectBoundingBox'><rect x='0' y='0' width='1' height='1'/></clipPath></defs><path d='M0.5,8 Q4,-3.5 8,8' fill='red' clip-path='url(#window)'/></svg>"u8.ToArray(),
            "<svg xmlns='http://www.w3.org/2000/svg'><defs><clipPath id='c'><rect x='0.5' y='2.25' width='7.5' height='5.75'/></clipPath></defs><path d='M0.5,8 Q4,-3.5 8,8' fill='red' clip-path='url(#c)'/></svg>"u8.ToArray());
    }

    [Fact]
    public void Clips_polygon_use_instances_to_a_fractional_object_bounding_box()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><polygon id='p' points='0,0 8,0 0,8' fill='red'/></defs><defs><clipPath id='window' clipPathUnits='objectBoundingBox'><rect x='0.25' y='0.25' width='0.5' height='0.5'/></clipPath></defs><use href='#p' x='8' clip-path='url(#window)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.PolygonFill, geometry.Kind);
        Assert.NotNull(geometry.AuthoredPath);
        Assert.Contains(root.Commands.ToArray(), command => command.Kind == SvgDrawCommandKind.BeginGroup && command.MaskContentIndex >= 0);
        using SvgRoot expected = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><clipPath id='c'><rect x='10' y='2' width='4' height='4'/></clipPath></defs><polygon points='8,0 16,0 8,8' fill='red' clip-path='url(#c)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);
        SvgPathFillAssertions.Equivalent(expected, root);
    }

    [Fact]
    public void Clips_forward_use_instances_to_their_object_bounding_box()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><clipPath id='window' clipPathUnits='objectBoundingBox'><rect x='0.25' y='0.25' width='0.5' height='0.5'/></clipPath></defs><use href='#p' x='8' clip-path='url(#window)'/><defs><polygon id='p' points='0,0 8,0 0,8' fill='red'/></defs></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.PolygonFill, geometry.Kind);
        Assert.NotNull(geometry.AuthoredPath);
        Assert.Contains(root.Commands.ToArray(), command => command.Kind == SvgDrawCommandKind.BeginGroup && command.MaskContentIndex >= 0);
        using SvgRoot expected = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><clipPath id='c'><rect x='10' y='2' width='4' height='4'/></clipPath></defs><polygon points='8,0 16,0 8,8' fill='red' clip-path='url(#c)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);
        SvgPathFillAssertions.Equivalent(expected, root);
    }

    [Fact]
    public void Clips_forward_symbol_use_instances_to_their_object_bounding_box()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><clipPath id='window' clipPathUnits='objectBoundingBox'><rect x='0.25' y='0.25' width='0.5' height='0.5'/></clipPath></defs><use href='#s' width='8' height='8' clip-path='url(#window)'/><defs><symbol id='s' viewBox='0 0 8 8'><polygon points='0,0 8,0 0,8' fill='red'/></symbol></defs></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.PolygonFill, geometry.Kind);
        Assert.NotNull(geometry.AuthoredPath);
        Assert.Contains(root.Commands.ToArray(), command => command.Kind == SvgDrawCommandKind.BeginGroup && command.MaskContentIndex >= 0);
        using SvgRoot expected = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><clipPath id='c'><rect x='2' y='2' width='4' height='4'/></clipPath></defs><polygon points='0,0 8,0 0,8' fill='red' clip-path='url(#c)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);
        SvgPathFillAssertions.Equivalent(expected, root);
    }

    [Fact]
    public void Clips_forward_clip_path_use_instances_to_their_user_space_box()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><rect id='r' x='0' y='0' width='8' height='8' fill='red'/></defs><use href='#r' clip-path='url(#c)'/><defs><clipPath id='c'><rect x='2' y='2' width='4' height='4'/></clipPath></defs></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(new SvgRectangle(0, 0, 8, 8, new SvgColor(255, 0, 0, 255), true, null), Assert.Single(root.Rectangles));
        SvgPathFillAssertions.MatchesIntegerBands(root, [new SvgRectangle(2, 2, 4, 4, new SvgColor(255, 0, 0, 255), true, null)]);
    }

    [Fact]
    public void Clips_forward_clip_path_symbol_instances_to_their_user_space_box()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><symbol id='s' viewBox='0 0 8 8'><polygon points='0,0 8,0 0,8' fill='red'/></symbol></defs><use href='#s' width='8' height='8' clip-path='url(#c)'/><defs><clipPath id='c'><rect x='2' y='2' width='4' height='4'/></clipPath></defs></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.PolygonFill, geometry.Kind);
        Assert.NotNull(geometry.AuthoredPath);
        Assert.Contains(root.Commands.ToArray(), command => command.Kind == SvgDrawCommandKind.BeginGroup && command.MaskContentIndex >= 0);
        using SvgRoot expected = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><clipPath id='c'><rect x='2' y='2' width='4' height='4'/></clipPath></defs><polygon points='0,0 8,0 0,8' fill='red' clip-path='url(#c)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);
        SvgPathFillAssertions.Equivalent(expected, root);
    }

    [Fact]
    public void Clips_forward_clip_path_definitions_on_direct_paths()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><path d='M 0,0 H 8 V 8 H 0 Z' fill='red' clip-path='url(#later)'/><defs><clipPath id='later'><rect x='2' y='2' width='4' height='4'/></clipPath></defs></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(new SvgClipBox(2, 2, 4, 4), Assert.Single(root.Geometries).Polygon.Clip);
        SvgPathFillAssertions.MatchesIntegerBands(root, [new(2, 2, 4, 4, new(255, 0, 0, 255), true, null)]);
    }

    [Fact]
    public void Clips_forward_bounding_box_clips_on_direct_rects()
    {
        AssertSolidRectanglePixels("<svg xmlns='http://www.w3.org/2000/svg'><rect x='0' y='0' width='8' height='8' fill='red' clip-path='url(#later)'/><defs><clipPath id='later' clipPathUnits='objectBoundingBox'><rect x='0.25' y='0.25' width='0.5' height='0.5'/></clipPath></defs></svg>"u8.ToArray(), new(2, 2, 4, 4, new(255, 0, 0, 255), true, null), 300, 150);
    }

    [Fact]
    public void Clips_forward_bounding_box_clips_on_direct_paths()
    {
        AssertSolidRectanglePixels("<svg xmlns='http://www.w3.org/2000/svg'><path d='M 0,0 H 8 V 8 H 0 Z' fill='red' clip-path='url(#later)'/><defs><clipPath id='later' clipPathUnits='objectBoundingBox'><rect x='0.25' y='0.25' width='0.5' height='0.5'/></clipPath></defs></svg>"u8.ToArray(), new(2, 2, 4, 4, new(255, 0, 0, 255), true, null), 300, 150);
    }
    [Fact]
    public void Zero_height_line_object_box_clip_is_nonpainting()
    {
        AssertSolidRectanglePixels("<svg xmlns='http://www.w3.org/2000/svg'><line x1='0' y1='0' x2='8' y2='0' stroke='red' stroke-width='2' clip-path='url(#later)'/><defs><clipPath id='later' clipPathUnits='objectBoundingBox'><rect x='0' y='0' width='1' height='1'/></clipPath></defs></svg>"u8.ToArray(), new(0, 0, 0, 0, new(255, 0, 0, 255), true, null), 300, 150);
    }

    [Fact]
    public void Clips_forward_bounding_box_clips_on_direct_circles()
    {
        AssertEquivalentPixels("<svg xmlns='http://www.w3.org/2000/svg'><circle cx='4' cy='4' r='4' fill='red' clip-path='url(#later)'/><defs><clipPath id='later' clipPathUnits='objectBoundingBox'><rect x='0' y='0' width='1' height='1'/></clipPath></defs></svg>"u8.ToArray(),
            "<svg xmlns='http://www.w3.org/2000/svg'><circle cx='4' cy='4' r='4' fill='red'/></svg>"u8.ToArray());
    }

    [Fact]
    public void Original_forward_viewport_mask_maps_its_region()
    {
        AssertSolidRectanglePixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='8'><svg x='4' y='0' width='8' height='8'><rect x='0' y='0' width='8' height='8' fill='red' mask='url(#later)'/></svg><defs><mask id='later' maskUnits='userSpaceOnUse' x='2' y='2' width='4' height='4'><rect x='0' y='0' width='8' height='8' fill='#ffffff'/></mask></defs></svg>"u8.ToArray(), new(6, 2, 4, 4, new(255, 0, 0, 255), true, null), 16, 8);
    }

    [Fact]
    public void Clips_symbol_use_instances_to_their_object_bounding_box()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><symbol id='s' viewBox='0 0 8 8'><polygon points='0,0 8,0 0,8' fill='red'/></symbol></defs><defs><clipPath id='window' clipPathUnits='objectBoundingBox'><rect x='0.25' y='0.25' width='0.5' height='0.5'/></clipPath></defs><use href='#s' width='8' height='8' clip-path='url(#window)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.PolygonFill, geometry.Kind);
        Assert.NotNull(geometry.AuthoredPath);
        Assert.Contains(root.Commands.ToArray(), command => command.Kind == SvgDrawCommandKind.BeginGroup && command.MaskContentIndex >= 0);
        using SvgRoot expected = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><clipPath id='c'><rect x='2' y='2' width='4' height='4'/></clipPath></defs><polygon points='0,0 8,0 0,8' fill='red' clip-path='url(#c)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);
        SvgPathFillAssertions.Equivalent(expected, root);
    }

    [Fact]
    public void Clips_plain_symbol_use_instances_to_their_object_bounding_box()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><symbol id='s'><polygon points='0,0 8,0 0,8' fill='red'/></symbol></defs><defs><clipPath id='window' clipPathUnits='objectBoundingBox'><rect x='0.25' y='0.25' width='0.5' height='0.5'/></clipPath></defs><use href='#s' clip-path='url(#window)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.PolygonFill, geometry.Kind);
        Assert.NotNull(geometry.AuthoredPath);
        Assert.Contains(root.Commands.ToArray(), command => command.Kind == SvgDrawCommandKind.BeginGroup && command.MaskContentIndex >= 0);
        using SvgRoot expected = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><clipPath id='c'><rect x='2' y='2' width='4' height='4'/></clipPath></defs><polygon points='0,0 8,0 0,8' fill='red' clip-path='url(#c)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);
        SvgPathFillAssertions.Equivalent(expected, root);
    }

    [Fact]
    public void Root_fallback_viewport_supplies_sizeless_symbol_instance_clips()
    {
        byte[] svg = "<svg xmlns='http://www.w3.org/2000/svg'><defs><symbol id='s' viewBox='0 0 8 8'><polygon points='0,0 8,0 0,8' fill='red'/></symbol></defs><defs><clipPath id='window' clipPathUnits='objectBoundingBox'><rect x='0.25' y='0.25' width='0.5' height='0.5'/></clipPath></defs><use href='#s' clip-path='url(#window)'/></svg>"u8.ToArray();
        string source = System.Text.Encoding.UTF8.GetString(svg);
        byte[] explicitViewport = System.Text.Encoding.UTF8.GetBytes(source.Insert(source.IndexOf('>'), " width='300' height='150'"));
        Assert.Equal(SvgConverter.ConvertToPng(explicitViewport, SvgRenderOptions.Default, CancellationToken.None).ToArray(),
            SvgConverter.ConvertToPng(svg, SvgRenderOptions.Default, CancellationToken.None).ToArray());
    }

    [Fact]
    public void Transformed_object_box_clip_outside_target_is_nonpainting()
    {
        AssertSolidRectanglePixels("<svg xmlns='http://www.w3.org/2000/svg'><defs><clipPath id='window' clipPathUnits='objectBoundingBox'><rect x='0' y='0' width='1' height='1' transform='translate(1)'/></clipPath></defs><rect width='8' height='8' fill='red' clip-path='url(#window)'/></svg>"u8.ToArray(), new(0, 0, 0, 0, new(255, 0, 0, 255), true, null), 300, 150);
    }

    [Fact]
    public void Renders_multi_rectangle_clip_paths()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><clipPath id='window'><rect x='4' y='4' width='4' height='4'/><rect x='8' y='8' width='4' height='4'/></clipPath></defs><g clip-path='url(#window)'><rect width='8' height='8' fill='red'/></g></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);
        Assert.False(root.HasUnsupportedChild);
        using SvgConverter.RasterizedSurface raster = SvgConverter.Rasterize(root,
            new(new SvgViewport(8, 8), 1, SvgColor.Transparent, SvgRenderLimits.Default), new RenderBudget(new HostMemoryBudget(1_000_000)), CancellationToken.None);
        Assert.Equal(8, raster.Width);
        Assert.Equal(8, raster.Height);
        for (int y = 0; y < 8; y++)
            for (int x = 0; x < 8; x++)
            {
                byte red = x >= 4 && y >= 4 ? (byte)255 : (byte)0;
                int offset = (y * 8 + x) * 4;
                Assert.Equal(red, raster.Pixels[offset]);
                Assert.Equal(0, raster.Pixels[offset + 1]);
                Assert.Equal(0, raster.Pixels[offset + 2]);
                Assert.Equal(red, raster.Pixels[offset + 3]);
            }
    }

    [Theory]
    [InlineData("translate(0.5, 0)", "0.5", "0", "1.5", "0")]
    [InlineData("scale(0.5)", "0", "0", "0.5", "0")]
    [InlineData("translate(0.5, 0) scale(2)", "0.5", "0", "2.5", "0")]
    public void Gradient_fractional_transforms_match_explicit_vectors(string transform, string x1, string y1, string x2, string y2)
    {
        byte[] transformed = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><defs><linearGradient id='g' gradientTransform='" + transform + "'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><rect width='5' height='5' fill='url(#g)'/></svg>");
        byte[] baseline = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><defs><linearGradient id='g' x1='" + x1 + "' y1='" + y1 + "' x2='" + x2 + "' y2='" + y2 + "'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><rect width='5' height='5' fill='url(#g)'/></svg>");

        ReadOnlyMemory<byte> transformedPng = SvgConverter.ConvertToPng(transformed, SvgRenderOptions.Default, CancellationToken.None);
        ReadOnlyMemory<byte> baselinePng = SvgConverter.ConvertToPng(baseline, SvgRenderOptions.Default, CancellationToken.None);

        Assert.True(transformedPng.Span.SequenceEqual(baselinePng.Span));
    }

    [Theory]
    [InlineData("skewX(30)", "matrix(1 0 .5773502691896257 1 0 0)")]
    [InlineData("skewY(15)", "matrix(1 .2679491924311227 0 1 0 0)")]
    public void Original_gradient_skews_match_their_affine_matrix_and_release_storage(string transform, string matrix)
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='g' gradientTransform='" + transform + "'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><rect width='8' height='8' fill='url(#g)'/></svg>");
        byte[] equivalent = System.Text.Encoding.UTF8.GetBytes(System.Text.Encoding.UTF8.GetString(input).Replace(transform, matrix, StringComparison.Ordinal));
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(1_000_000));
        using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None))
            Assert.True(Assert.Single(root.Geometries).Gradient.Gradient.RequiresSourceMap);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        SvgRenderOptions options = new(new SvgViewport(8, 8), 1, SvgColor.Transparent, SvgRenderLimits.Default);
        Assert.Equal(SvgConverter.ConvertToPng(equivalent, options, CancellationToken.None).ToArray(), SvgConverter.ConvertToPng(input, options, CancellationToken.None).ToArray());
    }

    [Fact]
    public void Inherits_transparency_through_gradient_href()
    {
        byte[] input = "<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='b'/><linearGradient id='c' href='#b'/></defs><rect x='8' y='8' width='48' height='48' fill='url(#c)'/></svg>"u8.ToArray();
        using SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, CancellationToken.None);
        SvgRectangle bounds = SvgMaskSyntax.BoundingBox(Assert.Single(root.Geometries));
        Assert.Equal((8d, 8d, 48d, 48d), (bounds.X, bounds.Y, bounds.Width, bounds.Height));
        Assert.Equal(SvgConverter.ConvertToPng("<svg xmlns='http://www.w3.org/2000/svg'/>"u8.ToArray(), SvgRenderOptions.Default, CancellationToken.None).ToArray(),
            SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None).ToArray());
    }

    [Fact]
    public void Gradient_href_partial_vector_override_matches_explicit_vector()
    {
        byte[] child = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><defs><linearGradient id='b'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient><linearGradient id='c' href='#b' x1='0.25'/></defs><rect width='5' height='5' fill='url(#c)'/></svg>");
        byte[] baseline = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><defs><linearGradient id='e' x1='0.25' y1='0' x2='1' y2='0'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><rect width='5' height='5' fill='url(#e)'/></svg>");

        ReadOnlyMemory<byte> childPng = SvgConverter.ConvertToPng(child, SvgRenderOptions.Default, CancellationToken.None);
        ReadOnlyMemory<byte> baselinePng = SvgConverter.ConvertToPng(baseline, SvgRenderOptions.Default, CancellationToken.None);

        Assert.True(childPng.Span.SequenceEqual(baselinePng.Span));
    }

    [Fact]
    public void Gradient_href_partial_vector_override_matches_explicit_vector_in_user_space()
    {
        byte[] child = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><defs><linearGradient id='b' gradientUnits='userSpaceOnUse' x1='1' y1='1' x2='4' y2='1'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient><linearGradient id='c' href='#b' x2='3' y2='2'/></defs><rect width='5' height='5' fill='url(#c)'/></svg>");
        byte[] baseline = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><defs><linearGradient id='e' gradientUnits='userSpaceOnUse' x1='1' y1='1' x2='3' y2='2'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><rect width='5' height='5' fill='url(#e)'/></svg>");

        ReadOnlyMemory<byte> childPng = SvgConverter.ConvertToPng(child, SvgRenderOptions.Default, CancellationToken.None);
        ReadOnlyMemory<byte> baselinePng = SvgConverter.ConvertToPng(baseline, SvgRenderOptions.Default, CancellationToken.None);

        Assert.True(childPng.Span.SequenceEqual(baselinePng.Span));
    }

    [Fact]
    public void Renders_pattern_round_capped_lines_identical_to_solid()
    {
        byte[] painted = System.Text.Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"8\"><defs><pattern id=\"p\" width=\"8\" height=\"8\" patternUnits=\"userSpaceOnUse\"><rect width=\"8\" height=\"8\" fill=\"red\"/></pattern></defs><line x1=\"0\" y1=\"4\" x2=\"16\" y2=\"4\" stroke=\"url(#p)\" stroke-width=\"2\" stroke-linecap=\"round\"/></svg>");
        byte[] solid = System.Text.Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"8\"><line x1=\"0\" y1=\"4\" x2=\"16\" y2=\"4\" stroke=\"red\" stroke-width=\"2\" stroke-linecap=\"round\"/></svg>");

        ReadOnlyMemory<byte> paintedPng = SvgConverter.ConvertToPng(painted, SvgRenderOptions.Default, CancellationToken.None);
        ReadOnlyMemory<byte> solidPng = SvgConverter.ConvertToPng(solid, SvgRenderOptions.Default, CancellationToken.None);

        Assert.True(paintedPng.Span.SequenceEqual(solidPng.Span));
    }

    [Fact]
    public void Instances_gradient_fill_with_pattern_stroke_definitions()
    {
        byte[] input = "<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='g'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient><pattern id='p' width='8' height='8' patternUnits='userSpaceOnUse'><rect width='8' height='8' fill='red'/></pattern><rect id='r' width='8' height='8' fill='url(#g)' stroke='url(#p)' stroke-width='2'/></defs><use href='#r'/></svg>"u8.ToArray();
        byte[] expected = "<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='g'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient><pattern id='p' width='8' height='8' patternUnits='userSpaceOnUse'><rect width='8' height='8' fill='red'/></pattern></defs><rect width='8' height='8' fill='url(#g)' stroke='url(#p)' stroke-width='2'/></svg>"u8.ToArray();
        Assert.Equal(SvgConverter.ConvertToPng(expected, SvgRenderOptions.Default, CancellationToken.None).ToArray(), SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None).ToArray());
    }

    [Fact]
    public void White_masks_leave_rounded_strokes_unchanged()
    {
        byte[] masked = System.Text.Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"64\" height=\"64\"><defs><mask id=\"m\"><rect x=\"0\" y=\"0\" width=\"64\" height=\"64\" fill=\"white\"/></mask></defs><rect x=\"8\" y=\"8\" width=\"48\" height=\"48\" rx=\"12\" fill=\"red\" stroke=\"blue\" stroke-width=\"8\" mask=\"url(#m)\"/></svg>");
        byte[] baseline = System.Text.Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"64\" height=\"64\"><rect x=\"8\" y=\"8\" width=\"48\" height=\"48\" rx=\"12\" fill=\"red\" stroke=\"blue\" stroke-width=\"8\"/></svg>");

        ReadOnlyMemory<byte> maskedPng = SvgConverter.ConvertToPng(masked, SvgRenderOptions.Default, CancellationToken.None);
        ReadOnlyMemory<byte> baselinePng = SvgConverter.ConvertToPng(baseline, SvgRenderOptions.Default, CancellationToken.None);

        Assert.True(maskedPng.Span.SequenceEqual(baselinePng.Span));
    }

    [Fact]
    public void White_masks_leave_rounded_rectangles_unchanged()
    {
        byte[] masked = System.Text.Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"64\" height=\"64\"><defs><mask id=\"m\"><rect x=\"0\" y=\"0\" width=\"64\" height=\"64\" fill=\"white\"/></mask></defs><rect x=\"8\" y=\"8\" width=\"48\" height=\"48\" rx=\"12\" fill=\"red\" mask=\"url(#m)\"/></svg>");
        byte[] baseline = System.Text.Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"64\" height=\"64\"><rect x=\"8\" y=\"8\" width=\"48\" height=\"48\" rx=\"12\" fill=\"red\"/></svg>");

        ReadOnlyMemory<byte> maskedPng = SvgConverter.ConvertToPng(masked, SvgRenderOptions.Default, CancellationToken.None);
        ReadOnlyMemory<byte> baselinePng = SvgConverter.ConvertToPng(baseline, SvgRenderOptions.Default, CancellationToken.None);

        Assert.True(maskedPng.Span.SequenceEqual(baselinePng.Span));
    }

    [Fact]
    public void Admits_an_empty_linear_gradient_as_transparent()
    {
        byte[] input = "<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='g'/></defs><rect x='8' y='8' width='48' height='48' fill='url(#g)'/></svg>"u8.ToArray();
        using SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, CancellationToken.None);
        SvgRectangle bounds = SvgMaskSyntax.BoundingBox(Assert.Single(root.Geometries));
        Assert.Equal((8d, 8d, 48d, 48d), (bounds.X, bounds.Y, bounds.Width, bounds.Height));
        Assert.Equal(SvgConverter.ConvertToPng("<svg xmlns='http://www.w3.org/2000/svg'/>"u8.ToArray(), SvgRenderOptions.Default, CancellationToken.None).ToArray(),
            SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None).ToArray());
    }

    [Fact]
    public void Degenerate_gradient_fills_render_byte_identical_to_solid_fills()
    {
        byte[] degenerate = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><defs><linearGradient id='g' x1='1' y1='1' x2='1' y2='1'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><rect width='5' height='5' fill='url(#g)'/></svg>");
        byte[] solid = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><rect width='5' height='5' fill='blue'/></svg>");

        ReadOnlyMemory<byte> degeneratePng = SvgConverter.ConvertToPng(degenerate, SvgRenderOptions.Default, CancellationToken.None);
        ReadOnlyMemory<byte> solidPng = SvgConverter.ConvertToPng(solid, SvgRenderOptions.Default, CancellationToken.None);

        Assert.True(degeneratePng.Span.SequenceEqual(solidPng.Span));
    }

    [Fact]
    public void Href_radial_vectors_keep_the_effective_general_transform()
    {
        const string parent = "<radialGradient id='p' gradientUnits='userSpaceOnUse' cx='4' cy='4' r='4'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient>";
        const string transform = "matrix(2 1 1 2 0 0)";
        byte[] alias = Document(parent + "<radialGradient id='g' href='#p' gradientTransform='" + transform + "'/>");
        byte[] standalone = Document(parent.Replace("id='p'", "id='g' gradientTransform='" + transform + "'", StringComparison.Ordinal));
        Assert.Equal(SvgConverter.ConvertToPng(standalone, SvgRenderOptions.Default, CancellationToken.None).ToArray(), SvgConverter.ConvertToPng(alias, SvgRenderOptions.Default, CancellationToken.None).ToArray());

        static byte[] Document(string gradients) => System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg'><defs>" + gradients + "</defs><rect width='8' height='8' fill='url(#g)'/></svg>");
    }

    [Fact]
    public void Bounding_box_radial_gradients_do_not_paint_zero_area_line_boxes()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><radialGradient id='g' cx='0.5' cy='0.5' r='0.5'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><line x1='0' y1='4' x2='8' y2='4' stroke='url(#g)' stroke-width='2'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.False(root.HasUnsupportedChild);
        Assert.False(Assert.Single(root.Geometries).RadialPolygon.HasFill);
        RenderBudget rasterBudget = new RenderBudget(new HostMemoryBudget(1_000_000));
        using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root,
            new(new SvgViewport(8, 8), 1, SvgColor.Transparent, SvgRenderLimits.Default), rasterBudget, CancellationToken.None))
            Assert.All(surface.Pixels, component => Assert.Equal((byte)0, component));
        Assert.Equal(0, rasterBudget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Clips_pattern_tile_content_through_clip_paths()
    {
        using SvgRoot userSpace = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><clipPath id='c'><rect x='0' y='0' width='1' height='1'/></clipPath><pattern id='tiles' width='4' height='4' patternUnits='userSpaceOnUse'><rect width='2' height='2' fill='red' clip-path='url(#c)'/></pattern></defs><rect width='8' height='8' fill='url(#tiles)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);
        using SvgRoot boundingBox = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><clipPath id='c' clipPathUnits='objectBoundingBox'><rect x='0' y='0' width='0.5' height='0.5'/></clipPath><pattern id='tiles' width='4' height='4' patternUnits='userSpaceOnUse'><rect width='2' height='2' fill='red' clip-path='url(#c)'/></pattern></defs><rect width='8' height='8' fill='url(#tiles)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);
        // Clipped leaves use retained tile scenes. Protect their visible
        // coverage independently from the old single-rectangle storage shortcut.
        foreach (SvgRoot scene in new[] { userSpace, boundingBox })
        {
            RenderBudget budget = new RenderBudget(new HostMemoryBudget(1_000_000));
            using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(scene,
                new(new SvgViewport(8, 8), 1, SvgColor.Transparent, SvgRenderLimits.Default), budget, CancellationToken.None))
                for (int y = 0; y < 8; y++) for (int x = 0; x < 8; x++)
                {
                    int offset = (y * 8 + x) * 4;
                    SvgColor expected = x % 4 == 0 && y % 4 == 0 ? new(255, 0, 0, 255) : SvgColor.Transparent;
                    Assert.Equal(expected, new SvgColor(surface.Pixels[offset], surface.Pixels[offset + 1], surface.Pixels[offset + 2], surface.Pixels[offset + 3]));
                }
            Assert.Equal(0, budget.Snapshot().CurrentBytes);
        }
    }

    [Fact]
    public void Admits_opacity_scale_and_stroke_on_pattern_tile_content()
    {
        using SvgRoot faded = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><pattern id='tiles' width='4' height='4' patternUnits='userSpaceOnUse'><rect width='2' height='2' fill='red' opacity='0.5'/></pattern></defs><rect width='8' height='8' fill='url(#tiles)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry fadedGeometry = Assert.Single(faded.Geometries);
        Assert.NotNull(fadedGeometry.Pattern.VectorContent);
        RenderBudget fadedBudget = new RenderBudget(new HostMemoryBudget(1_000_000));
        using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(faded,
            new(new SvgViewport(8, 8), 1, SvgColor.Transparent, SvgRenderLimits.Default), fadedBudget, CancellationToken.None))
            for (int y = 0; y < 8; y++)
                for (int x = 0; x < 8; x++)
                    Assert.Equal(x % 4 < 2 && y % 4 < 2 ? new byte[] { 255, 0, 0, 128 } : new byte[4],
                        surface.Pixels.AsSpan((y * 8 + x) * 4, 4).ToArray());
        Assert.Equal(0, fadedBudget.Snapshot().CurrentBytes);
        using SvgRoot scaled = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><pattern id='tiles' width='4' height='4' patternUnits='userSpaceOnUse'><rect width='1' height='1' fill='red' transform='scale(2)'/></pattern></defs><rect width='8' height='8' fill='url(#tiles)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry scaledGeometry = Assert.Single(scaled.Geometries);
        Assert.Equal(new SvgRectangle(0, 0, 2, 2, new SvgColor(255, 0, 0, 255), true, null), NativePatternRectangle(Assert.IsType<SvgVectorContent>(scaledGeometry.Pattern.VectorContent)));

        using SvgRoot stroked = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><pattern id='tiles' width='4' height='4' patternUnits='userSpaceOnUse'><rect x='1' y='1' width='2' height='2' fill='red' stroke='blue' stroke-width='2'/></pattern></defs><rect width='8' height='8' fill='url(#tiles)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        RenderBudget rasterBudget = new RenderBudget(new HostMemoryBudget(1_000_000));
        using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(stroked,
            new(new SvgViewport(8, 8), 1, SvgColor.Transparent, SvgRenderLimits.Default), rasterBudget, CancellationToken.None))
            for (int at = 0; at < surface.Pixels.Length; at += 4)
                Assert.Equal(new byte[] { 0, 0, 255, 255 }, surface.Pixels.AsSpan(at, 4).ToArray());
        Assert.Equal(0, rasterBudget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Original_pattern_tile_mask_renders_and_releases_sources()
    {
        byte[] input = "<svg xmlns='http://www.w3.org/2000/svg'><defs><mask id='m'><rect width='8' height='8' fill='#808080'/></mask><pattern id='tiles' width='4' height='4' patternUnits='userSpaceOnUse'><rect width='2' height='2' fill='red' mask='url(#m)'/></pattern></defs><rect width='8' height='8' fill='url(#tiles)'/></svg>"u8.ToArray();
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(8_000_000));
        using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None))
        using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, SvgRenderOptions.Default, budget, CancellationToken.None))
        {
            Assert.Equal(300, surface.Width); Assert.Equal(150, surface.Height);
            for (int y = 0; y < surface.Height; y++) for (int x = 0; x < surface.Width; x++)
            {
                SvgColor expected = x < 8 && y < 8 && x % 4 < 2 && y % 4 < 2 ? new(255, 0, 0, 128) : SvgColor.Transparent;
                int offset = (y * surface.Width + x) * 4;
                Assert.Equal(expected, new SvgColor(surface.Pixels[offset], surface.Pixels[offset + 1], surface.Pixels[offset + 2], surface.Pixels[offset + 3]));
            }
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Theory]
    [InlineData("scale(2)", 2)]
    [InlineData("matrix(1,0,0,1,0,0)", 1)]
    public void Positive_axis_pattern_maps_preserve_the_content_edge_and_transparent_gap(string transform, int scale)
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'><defs><pattern id='tiles' width='16' height='16' patternUnits='userSpaceOnUse' patternTransform='" + transform + "'><rect width='8' height='8' fill='red'/></pattern></defs><rect width='32' height='32' fill='url(#tiles)'/></svg>");
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(1_000_000));
        using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None))
        using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, SvgRenderOptions.Default, budget, CancellationToken.None))
        {
            SvgPatternGeometry pattern = Assert.Single(root.Geometries).Pattern;
            Assert.Equal(scale, pattern.ShaderPlacement.A);
            Assert.Equal(new byte[] { 255, 0, 0, 255 }, surface.Pixels.AsSpan((8 * scale - 1) * 4, 4).ToArray());
            Assert.Equal(new byte[] { 0, 0, 0, 0 }, surface.Pixels.AsSpan(8 * scale * 4, 4).ToArray());
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Retains_uniform_embedded_image_color_and_placement()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><image href='data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGP4z8DwHwAFAAH/iZk9HQAAAABJRU5ErkJggg==' x='8' y='8' width='48' height='48'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgImageGeometry placedImage = Assert.Single(root.Geometries).Image;
        Assert.Equal((8d, 8d, 48d, 48d), (placedImage.Box.X, placedImage.Box.Y, placedImage.Box.Width, placedImage.Box.Height));
        Assert.Equal(new SvgConstantShaderPaint(255, 0, 0, 255), placedImage.UniformSample);
        SvgPathFillAssertions.MatchesIntegerBands(root, [new(8, 8, 48, 48, new(255, 0, 0, 255), true, null)]);
    }

    [Fact]
    public void Defaults_dimless_embedded_images_to_the_decoded_intrinsic_size()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg' width='64' height='64'><image href='data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGP4z8DwHwAFAAH/iZk9HQAAAABJRU5ErkJggg==' x='8' y='8'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgImageGeometry placedImage = Assert.Single(root.Geometries).Image;
        Assert.Equal((8d, 8d, 1d, 1d), (placedImage.Box.X, placedImage.Box.Y, placedImage.Box.Width, placedImage.Box.Height));
        Assert.Equal(new SvgConstantShaderPaint(255, 0, 0, 255), placedImage.UniformSample);
        SvgPathFillAssertions.MatchesIntegerBands(root, [new(8, 8, 1, 1, new(255, 0, 0, 255), true, null)]);
    }

    [Fact]
    public void Empty_pattern_tiles_leave_outlines_unpainted()
    {
        byte[] input = "<svg xmlns='http://www.w3.org/2000/svg'><defs><pattern id='e' width='8' height='8' patternUnits='userSpaceOnUse'></pattern></defs><polygon points='0,0 8,0 0,8' fill='none' stroke='url(#e)' stroke-width='2'/></svg>"u8.ToArray();
        using SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, CancellationToken.None);
        SvgRectangle bounds = SvgMaskSyntax.BoundingBox(Assert.Single(root.Geometries));
        Assert.Equal((0d, 0d, 8d, 8d), (bounds.X, bounds.Y, bounds.Width, bounds.Height));
        Assert.Equal(SvgConverter.ConvertToPng("<svg xmlns='http://www.w3.org/2000/svg'/>"u8.ToArray(), SvgRenderOptions.Default, CancellationToken.None).ToArray(),
            SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None).ToArray());
    }

    [Fact]
    public void Original_partial_user_space_region_uses_default_extents()
    {
        AssertSolidRectanglePixels("<svg xmlns='http://www.w3.org/2000/svg'><defs><mask id='window' maskUnits='userSpaceOnUse' x='1' y='1'><rect x='0' y='0' width='8' height='8' fill='#808080'/></mask></defs><rect width='8' height='8' fill='red' mask='url(#window)'/></svg>"u8.ToArray(), new(1, 1, 7, 7, new(255, 0, 0, 128), true, null), 300, 150);
    }

    [Fact]
    public void Original_object_box_mask_content_preserves_gray_coverage_and_cleanup()
    {
        byte[] input = "<svg xmlns='http://www.w3.org/2000/svg'><defs><mask id='window' maskContentUnits='objectBoundingBox'><rect x='0' y='0' width='8' height='8' fill='#808080'/></mask></defs><rect width='8' height='8' fill='red' mask='url(#window)'/></svg>"u8.ToArray();
        RenderBudget sceneBudget = new RenderBudget(new HostMemoryBudget(1_000_000)), rasterBudget = new RenderBudget(new HostMemoryBudget(1_000_000));
        using (SvgRoot scene = SvgRootParser.Parse(input, SvgRenderLimits.Default, sceneBudget, CancellationToken.None))
        using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(scene, new(new SvgViewport(8, 8), 1, SvgColor.Transparent, SvgRenderLimits.Default), rasterBudget, CancellationToken.None))
            for (int pixel = 0; pixel < 64; pixel++)
            {
                Assert.Equal(255, surface.Pixels[pixel * 4]); Assert.Equal(0, surface.Pixels[pixel * 4 + 1]);
                Assert.Equal(0, surface.Pixels[pixel * 4 + 2]); Assert.Equal(128, surface.Pixels[pixel * 4 + 3]);
            }
        Assert.Equal(0, sceneBudget.Snapshot().CurrentBytes); Assert.Equal(0, rasterBudget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Original_viewport_use_mask_maps_its_region()
    {
        AssertSolidRectanglePixels("<svg xmlns='http://www.w3.org/2000/svg' width='64' height='64'><defs><rect id='r' x='0' y='0' width='8' height='8' fill='red'/><mask id='m' maskUnits='userSpaceOnUse' x='0' y='0' width='8' height='8'><rect x='0' y='0' width='16' height='16' fill='#808080'/></mask></defs><svg x='8' y='8' width='48' height='48'><use href='#r' mask='url(#m)'/></svg></svg>"u8.ToArray(), new(8, 8, 8, 8, new(255, 0, 0, 128), true, null), 64, 64);
    }

    [Fact]
    public void Original_chromatic_mask_preserves_nonoverlapping_empty_output_and_releases_sources()
    {
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(8_000_000));
        using (SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><mask id='fade'><rect x='8' y='8' width='48' height='48' fill='#ff0000'/></mask></defs><rect width='8' height='8' fill='red' mask='url(#fade)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, budget, CancellationToken.None))
        using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, SvgRenderOptions.Default, budget, CancellationToken.None))
            Assert.All(surface.Pixels, value => Assert.Equal(0, value));
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Crops_masked_groups_with_partial_mask_content()
    {
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(1_000_000));
        using (SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><mask id='m'><rect x='0' y='0' width='4' height='8' fill='#808080'/></mask></defs><g mask='url(#m)'><rect x='0' y='0' width='4' height='4' fill='red'/><rect x='4' y='4' width='4' height='4' fill='blue'/></g></svg>"u8.ToArray(), SvgRenderLimits.Default, budget, CancellationToken.None))
        using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, new(new SvgViewport(8, 8), 1, SvgColor.Transparent, SvgRenderLimits.Default), budget, CancellationToken.None))
        {
            Assert.Equal(new byte[] { 255, 0, 0, 128 }, surface.Pixels.AsSpan((1 * 8 + 1) * 4, 4).ToArray());
            Assert.Equal(new byte[] { 0, 0, 0, 0 }, surface.Pixels.AsSpan((5 * 8 + 5) * 4, 4).ToArray());
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Crops_masked_groups_with_forward_uses_outside_mask_content()
    {
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(1_000_000));
        using (SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><mask id='m'><rect x='0' y='0' width='4' height='4' fill='#808080'/></mask></defs><g mask='url(#m)'><use href='#r'/></g><defs><rect id='r' x='0' y='0' width='8' height='8'/></defs></svg>"u8.ToArray(), SvgRenderLimits.Default, budget, CancellationToken.None))
        using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, new(new SvgViewport(8, 8), 1, SvgColor.Transparent, SvgRenderLimits.Default), budget, CancellationToken.None))
        {
            Assert.Equal(new byte[] { 0, 0, 0, 128 }, surface.Pixels.AsSpan((1 * 8 + 1) * 4, 4).ToArray());
            Assert.Equal(new byte[] { 0, 0, 0, 0 }, surface.Pixels.AsSpan((5 * 8 + 5) * 4, 4).ToArray());
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Crops_masked_groups_with_strokes_outside_mask_content()
    {
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(1_000_000));
        using (SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><mask id='m'><rect x='0' y='0' width='4' height='4' fill='#808080'/></mask></defs><g mask='url(#m)'><rect x='0' y='0' width='4' height='4' fill='red' stroke='blue' stroke-width='2'/></g></svg>"u8.ToArray(), SvgRenderLimits.Default, budget, CancellationToken.None))
        using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, new(new SvgViewport(8, 8), 1, SvgColor.Transparent, SvgRenderLimits.Default), budget, CancellationToken.None))
        {
            Assert.Equal(new byte[] { 0, 0, 255, 128 }, surface.Pixels.AsSpan((0 * 8 + 0) * 4, 4).ToArray());
            Assert.Equal(new byte[] { 0, 0, 0, 0 }, surface.Pixels.AsSpan((5 * 8 + 5) * 4, 4).ToArray());
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Empty_mask_intersection_preserves_nested_opacity_cleanup()
    {
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(1_000_000));
        using (SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><mask id='fade'><rect x='8' y='8' width='48' height='48' fill='#808080'/></mask></defs><g mask='url(#fade)'><g opacity='0.5'><rect width='8' height='8' fill='red'/></g></g></svg>"u8.ToArray(), SvgRenderLimits.Default, budget, CancellationToken.None))
        using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, new(new SvgViewport(8, 8), 1, SvgColor.Transparent, SvgRenderLimits.Default), budget, CancellationToken.None))
        {
            Assert.Equal(new byte[] { 0, 0, 0, 0 }, surface.Pixels.AsSpan((0 * 8 + 0) * 4, 4).ToArray());
            Assert.Equal(new byte[] { 0, 0, 0, 0 }, surface.Pixels.AsSpan((5 * 8 + 5) * 4, 4).ToArray());
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Ignores_outermost_root_positions_when_rendering()
    {
        // SVG 1.1 gives x and y no effect on the outermost svg element.
        byte[] plain = "<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><rect x='1' y='1' width='2' height='2' fill='red'/></svg>"u8.ToArray();
        byte[] offset = "<svg xmlns='http://www.w3.org/2000/svg' x='8' y='4' width='5' height='5'><rect x='1' y='1' width='2' height='2' fill='red'/></svg>"u8.ToArray();

        ReadOnlyMemory<byte> plainPng = SvgConverter.ConvertToPng(plain, SvgRenderOptions.Default, CancellationToken.None);
        ReadOnlyMemory<byte> offsetPng = SvgConverter.ConvertToPng(offset, SvgRenderOptions.Default, CancellationToken.None);

        Assert.True(plainPng.Span.SequenceEqual(offsetPng.Span));
    }

    [Fact]
    public void Ignores_root_xml_space_when_rendering_without_text()
    {
        byte[] plain = "<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><rect x='1' y='1' width='2' height='2' fill='red'/></svg>"u8.ToArray();
        byte[] spaced = "<svg xmlns='http://www.w3.org/2000/svg' xml:space='preserve' width='5' height='5'><rect x='1' y='1' width='2' height='2' fill='red'/></svg>"u8.ToArray();

        ReadOnlyMemory<byte> plainPng = SvgConverter.ConvertToPng(plain, SvgRenderOptions.Default, CancellationToken.None);
        ReadOnlyMemory<byte> spacedPng = SvgConverter.ConvertToPng(spaced, SvgRenderOptions.Default, CancellationToken.None);

        Assert.True(plainPng.Span.SequenceEqual(spacedPng.Span));
    }

    [Fact]
    public void Ignores_root_enable_background_when_rendering()
    {
        byte[] plain = "<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><rect x='1' y='1' width='2' height='2' fill='red'/></svg>"u8.ToArray();
        byte[] transparent = "<svg xmlns='http://www.w3.org/2000/svg' enable-background='new' width='5' height='5'><rect x='1' y='1' width='2' height='2' fill='red'/></svg>"u8.ToArray();

        ReadOnlyMemory<byte> plainPng = SvgConverter.ConvertToPng(plain, SvgRenderOptions.Default, CancellationToken.None);
        ReadOnlyMemory<byte> backdropPng = SvgConverter.ConvertToPng(transparent, SvgRenderOptions.Default, CancellationToken.None);

        Assert.True(plainPng.Span.SequenceEqual(backdropPng.Span));
    }

    [Fact]
    public void Ignores_empty_id_defs_when_rendering()
    {
        byte[] plain = "<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><rect x='1' y='1' width='2' height='2' fill='red'/></svg>"u8.ToArray();
        byte[] defined = "<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><defs id='defs1'/><rect x='1' y='1' width='2' height='2' fill='red'/></svg>"u8.ToArray();

        ReadOnlyMemory<byte> plainPng = SvgConverter.ConvertToPng(plain, SvgRenderOptions.Default, CancellationToken.None);
        ReadOnlyMemory<byte> definedPng = SvgConverter.ConvertToPng(defined, SvgRenderOptions.Default, CancellationToken.None);

        Assert.True(plainPng.Span.SequenceEqual(definedPng.Span));
    }
    [Fact]
    public void Ignores_clip_rule_on_regular_paths_when_rendering()
    {
        byte[] plain = "<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><rect x='1' y='1' width='2' height='2' fill='red'/></svg>"u8.ToArray();
        byte[] ruled = "<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><rect x='1' y='1' width='2' height='2' fill='red' clip-rule='evenodd'/></svg>"u8.ToArray();

        ReadOnlyMemory<byte> plainPng = SvgConverter.ConvertToPng(plain, SvgRenderOptions.Default, CancellationToken.None);
        ReadOnlyMemory<byte> ruledPng = SvgConverter.ConvertToPng(ruled, SvgRenderOptions.Default, CancellationToken.None);

        Assert.True(plainPng.Span.SequenceEqual(ruledPng.Span));
    }

    [Fact]
    public void Ignores_clip_rule_on_groups_when_rendering()
    {
        byte[] plain = "<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><g><rect x='1' y='1' width='2' height='2' fill='red'/></g></svg>"u8.ToArray();
        byte[] ruled = "<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><g clip-rule='evenodd'><rect x='1' y='1' width='2' height='2' fill='red'/></g></svg>"u8.ToArray();

        ReadOnlyMemory<byte> plainPng = SvgConverter.ConvertToPng(plain, SvgRenderOptions.Default, CancellationToken.None);
        ReadOnlyMemory<byte> ruledPng = SvgConverter.ConvertToPng(ruled, SvgRenderOptions.Default, CancellationToken.None);

        Assert.True(plainPng.Span.SequenceEqual(ruledPng.Span));
    }
    [Fact]
    public void Ignores_clip_rule_on_root_when_rendering()
    {
        byte[] plain = "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"5\" height=\"5\"><rect x=\"1\" y=\"1\" width=\"2\" height=\"2\" fill=\"red\"/></svg>"u8.ToArray();
        byte[] ruled = "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"5\" height=\"5\" clip-rule=\"evenodd\"><rect x=\"1\" y=\"1\" width=\"2\" height=\"2\" fill=\"red\"/></svg>"u8.ToArray();

        ReadOnlyMemory<byte> plainPng = SvgConverter.ConvertToPng(plain, SvgRenderOptions.Default, CancellationToken.None);
        ReadOnlyMemory<byte> ruledPng = SvgConverter.ConvertToPng(ruled, SvgRenderOptions.Default, CancellationToken.None);

        Assert.True(plainPng.Span.SequenceEqual(ruledPng.Span));
    }

    [Fact]
    public void Ignores_root_class_when_rendering()
    {
        byte[] plain = "<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><rect x='1' y='1' width='2' height='2' fill='red'/></svg>"u8.ToArray();
        byte[] classified = "<svg xmlns='http://www.w3.org/2000/svg' class='external-icon' width='5' height='5'><rect x='1' y='1' width='2' height='2' fill='red'/></svg>"u8.ToArray();

        ReadOnlyMemory<byte> plainPng = SvgConverter.ConvertToPng(plain, SvgRenderOptions.Default, CancellationToken.None);
        ReadOnlyMemory<byte> classifiedPng = SvgConverter.ConvertToPng(classified, SvgRenderOptions.Default, CancellationToken.None);

        Assert.True(plainPng.Span.SequenceEqual(classifiedPng.Span));
    }
    private static void AssertEquivalentPixels(byte[] input, byte[] expected)
    {
        Assert.Equal(Render(expected), Render(input));

        static byte[] Render(byte[] source)
        {
            RenderBudget budget = new RenderBudget(new HostMemoryBudget(8_000_000));
            byte[] png;
            using (SvgRoot root = SvgRootParser.Parse(source, SvgRenderLimits.Default, budget, CancellationToken.None))
                png = SvgConverter.ConvertToPng(root, SvgRenderOptions.Default, budget, CancellationToken.None).ToArray();
            Assert.Equal(0, budget.Snapshot().CurrentBytes);
            return png;
        }
    }

    private static void AssertSolidRectanglePixels(byte[] input, SvgRectangle visible, int width, int height)
    {
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(8_000_000));
        using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None))
        using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, SvgRenderOptions.Default, budget, CancellationToken.None))
        {
            Assert.Equal(width, surface.Width);
            Assert.Equal(height, surface.Height);
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                SvgColor expected = x >= visible.X && x < visible.X + visible.Width && y >= visible.Y && y < visible.Y + visible.Height ? visible.Color : SvgColor.Transparent;
                int offset = (y * width + x) * 4;
                Assert.Equal(expected, new SvgColor(surface.Pixels[offset], surface.Pixels[offset + 1], surface.Pixels[offset + 2], surface.Pixels[offset + 3]));
            }
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }
}
