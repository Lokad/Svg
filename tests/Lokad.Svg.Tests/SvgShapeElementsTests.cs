using System.Buffers.Binary;
using System.IO.Compression;
using Lokad.Svg.Parsing;

namespace Lokad.Svg.Tests;

public sealed class SvgShapeElementsTests
{
    [Fact]
    public void Circle_renders_analytic_disc()
    {
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='64' height='64'><circle cx='32' cy='32' r='20' fill='red'/></svg>", out _, out _);

        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, 64, 32, 32));
        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, 64, 32, 20));
        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, 64, 16, 32));
        Assert.Equal(Pixel(pixels, 64, 16, 32), Pixel(pixels, 64, 48, 32));
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(pixels, 64, 0, 0));
    }

    [Fact]
    public void Ellipse_renders_analytic_disc()
    {
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='64' height='64'><ellipse cx='32' cy='32' rx='24' ry='14' fill='red'/></svg>", out _, out _);

        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, 64, 32, 32));
        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, 64, 32, 24));
        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, 64, 16, 32));
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(pixels, 64, 0, 0));
    }

    [Fact]
    public void Viewport_rounded_gradient_matches_direct_rendering()
    {
        byte[] viewport = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='8'><defs><linearGradient id='g' x1='0' y1='0' x2='1' y2='0'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><svg x='8' y='0' width='8' height='8'><rect x='0' y='0' width='8' height='8' rx='2' fill='url(#g)'/></svg></svg>", out _, out _);
        byte[] direct = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='8'><defs><linearGradient id='g' x1='0' y1='0' x2='1' y2='0'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><rect x='8' y='0' width='8' height='8' rx='2' fill='url(#g)'/></svg>", out _, out _);

        Assert.Equal(direct, viewport);
    }

    [Fact]
    public void Mirrored_linear_gradient_paints_flipped_stops()
    {
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='64' height='64'><defs><linearGradient id='g' gradientUnits='userSpaceOnUse' x1='8' y1='32' x2='56' y2='32' gradientTransform='scale(-1 1)'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><rect x='0' y='0' width='64' height='64' fill='url(#g)'/></svg>", out int width, out int height);
        byte[] plain = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='64' height='64'><defs><linearGradient id='g' gradientUnits='userSpaceOnUse' x1='8' y1='32' x2='56' y2='32'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><rect x='0' y='0' width='64' height='64' fill='url(#g)'/></svg>", out _, out _);

        Assert.NotEqual(plain, pixels);
        Assert.Equal(64, width);
        Assert.Equal(64, height);
        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, width, 0, 0));
        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, width, 63, 63));
        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, width, 32, 32));
        Assert.Equal(64 * 64 * 255, AlphaSum(pixels));
    }

    [Fact]
    public void Mirrored_radial_matches_unmirrored_twin()
    {
        byte[] mirrored = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='64' height='64'><defs><radialGradient id='g' gradientUnits='userSpaceOnUse' cx='44' cy='32' r='22' fx='52' fy='32' gradientTransform='translate(64 0) scale(-1 1)'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><rect x='0' y='0' width='64' height='64' fill='url(#g)'/></svg>", out int width, out _);
        byte[] plain = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='64' height='64'><defs><radialGradient id='g' gradientUnits='userSpaceOnUse' cx='20' cy='32' r='22' fx='12' fy='32'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><rect x='0' y='0' width='64' height='64' fill='url(#g)'/></svg>", out _, out _);

        Assert.Equal(plain, mirrored);
        Assert.Equal(new SvgColor(248, 0, 6, 255), Pixel(mirrored, width, 12, 32));
        Assert.Equal(new SvgColor(0, 0, 255, 255), Pixel(mirrored, width, 63, 63));
    }

    [Fact]
    public void Mirrored_nonuniform_radial_matches_scaled_twin()
    {
        byte[] mirrored = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='64' height='64'><defs><radialGradient id='g' gradientUnits='userSpaceOnUse' cx='22' cy='32' r='11' fx='26' fy='32' gradientTransform='translate(64 0) scale(-2 1)'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><rect x='0' y='0' width='64' height='64' fill='url(#g)'/></svg>", out _, out _);
        byte[] scaled = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='64' height='64'><defs><radialGradient id='g' gradientUnits='userSpaceOnUse' cx='10' cy='32' r='11' fx='6' fy='32' gradientTransform='scale(2 1)'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><rect x='0' y='0' width='64' height='64' fill='url(#g)'/></svg>", out _, out _);

        Assert.Equal(scaled, mirrored);
    }

    [Fact]
    public void Composed_mirror_linear_gradient_flips_blend()
    {
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='64' height='64'><defs><linearGradient id='g' gradientUnits='userSpaceOnUse' x1='8' y1='32' x2='56' y2='32' gradientTransform='translate(64 0) scale(-1 1)'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><rect x='0' y='0' width='64' height='64' fill='url(#g)'/></svg>", out int width, out _);

        Assert.Equal(new SvgColor(0, 0, 255, 255), Pixel(pixels, width, 0, 0));
        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, width, 63, 63));
        Assert.Equal(new SvgColor(0, 0, 255, 255), Pixel(pixels, width, 0, 32));
        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, width, 63, 32));
        Assert.Equal(new SvgColor(130, 0, 124, 255), Pixel(pixels, width, 32, 32));
    }

    [Fact]
    public void Rotated_radial_gradient_moves_focal_hotspot()
    {
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='64' height='64'><defs><radialGradient id='g' gradientUnits='userSpaceOnUse' cx='32' cy='32' r='22' fx='44' fy='32' gradientTransform='rotate(30 32 32)'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><rect x='0' y='0' width='64' height='64' fill='url(#g)'/></svg>", out int width, out int height);
        byte[] plain = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='64' height='64'><defs><radialGradient id='g' gradientUnits='userSpaceOnUse' cx='32' cy='32' r='22' fx='44' fy='32'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><rect x='0' y='0' width='64' height='64' fill='url(#g)'/></svg>", out _, out _);

        Assert.NotEqual(plain, pixels);
        Assert.Equal(64, width);
        Assert.Equal(64, height);
        Assert.Equal(new SvgColor(244, 0, 10, 255), Pixel(pixels, width, 42, 38));
        Assert.Equal(new SvgColor(0, 0, 255, 255), Pixel(pixels, width, 0, 0));
        Assert.Equal(new SvgColor(0, 0, 255, 255), Pixel(pixels, width, 63, 63));
        Assert.Equal(new SvgColor(170, 0, 84, 255), Pixel(pixels, width, 32, 32));
    }

    [Fact]
    public void Rotated_then_scaled_gradient_chain_stays_unsupported()
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='64' height='64'><defs><linearGradient id='p' gradientUnits='userSpaceOnUse' x1='8' y1='32' x2='56' y2='32' gradientTransform='rotate(45 32 32)'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient><linearGradient id='g' href='#p' gradientTransform='scale(2 1)'/></defs><rect x='0' y='0' width='64' height='64' fill='url(#g)'/></svg>");

        SvgException rejected = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, rejected.ErrorCode);
        Assert.Contains("Only axis-aligned or similar linear gradient transforms are implemented.", rejected.Message);
    }

    [Fact]
    public void Scaled_then_rotated_gradient_chain_stays_unsupported()
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='64' height='64'><defs><linearGradient id='p' gradientUnits='userSpaceOnUse' x1='8' y1='32' x2='56' y2='32' gradientTransform='scale(2 1)'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient><linearGradient id='g' href='#p' gradientTransform='rotate(45 32 32)'/></defs><rect x='0' y='0' width='64' height='64' fill='url(#g)'/></svg>");

        SvgException rejected = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, rejected.ErrorCode);
        Assert.Contains("Only axis-aligned or similar linear gradient transforms are implemented.", rejected.Message);
    }

    [Fact]
    public void Rotated_linear_gradient_paints_diagonal_blend()
    {
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='64' height='64'><defs><linearGradient id='g' gradientUnits='userSpaceOnUse' x1='8' y1='32' x2='56' y2='32' gradientTransform='rotate(45 32 32)'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><rect x='0' y='0' width='64' height='64' fill='url(#g)'/></svg>", out int width, out _);
        byte[] plain = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='64' height='64'><defs><linearGradient id='g' gradientUnits='userSpaceOnUse' x1='8' y1='32' x2='56' y2='32'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><rect x='0' y='0' width='64' height='64' fill='url(#g)'/></svg>", out _, out _);

        Assert.NotEqual(plain, pixels);
        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, width, 0, 0));
        Assert.Equal(new SvgColor(0, 0, 255, 255), Pixel(pixels, width, 63, 63));
        Assert.Equal(new SvgColor(128, 0, 128, 255), Pixel(pixels, width, 0, 63));
        Assert.Equal(new SvgColor(128, 0, 128, 255), Pixel(pixels, width, 63, 0));
        Assert.Equal(new SvgColor(123, 0, 131, 255), Pixel(pixels, width, 32, 32));
    }

    [Fact]
    public void Root_linear_gradient_matches_defs_twin()
    {
        byte[] rooted = RenderPixels("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"16\"><linearGradient id=\"g\" x1=\"0\" y1=\"0\" x2=\"1\" y2=\"0\"><stop offset=\"0\" stop-color=\"#ff0000\"/><stop offset=\"1\" stop-color=\"#0000ff\"/></linearGradient><rect x=\"2\" y=\"2\" width=\"12\" height=\"12\" fill=\"url(#g)\"/></svg>", out _, out _);
        byte[] defsed = RenderPixels("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"16\"><defs><linearGradient id=\"g\" x1=\"0\" y1=\"0\" x2=\"1\" y2=\"0\"><stop offset=\"0\" stop-color=\"#ff0000\"/><stop offset=\"1\" stop-color=\"#0000ff\"/></linearGradient></defs><rect x=\"2\" y=\"2\" width=\"12\" height=\"12\" fill=\"url(#g)\"/></svg>", out _, out _);

        Assert.Equal(defsed, rooted);
    }
    [Fact]
    public void Radial_from_linear_href_matches_same_type_twin()
    {
        // S13: a radial gradient inherits stops through a cross-type href,
        // rendering exactly like the same stops declared locally (pinned
        // Chrome capture shows the cross-type render byte-identical to the
        // same-type render).
        byte[] crossed = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><defs><linearGradient id='l'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient><radialGradient id='r' href='#l'/></defs><rect x='2' y='2' width='12' height='12' fill='url(#r)'/></svg>", out _, out _);
        byte[] local = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><defs><radialGradient id='r'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><rect x='2' y='2' width='12' height='12' fill='url(#r)'/></svg>", out _, out _);

        Assert.Equal(local, crossed);
    }

    [Fact]
    public void Radial_from_linear_xlink_href_matches_same_type_twin()
    {
        // S13: the cross-type inheritance accepts the xlink:href spelling
        // used by real exports like inv-035.
        byte[] crossed = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' xmlns:xlink='http://www.w3.org/1999/xlink' width='16' height='16'><defs><linearGradient id='l'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient><radialGradient id='r' xlink:href='#l'/></defs><rect x='2' y='2' width='12' height='12' fill='url(#r)'/></svg>", out _, out _);
        byte[] local = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><defs><radialGradient id='r'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><rect x='2' y='2' width='12' height='12' fill='url(#r)'/></svg>", out _, out _);

        Assert.Equal(local, crossed);
    }

    [Fact]
    public void Radial_filled_curved_path_paints_end_stops()
    {
        // S13: radial fills flatten curved paths through shared device
        // outlines; clamped end-stop corners are exact while the curve edge
        // carries the standing radial-curve residual against Chrome.
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><defs><radialGradient id='g'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><path d='M0,8 Q8,0 16,8 L16,16 L0,16 Z' fill='url(#g)'/></svg>", out int width, out int height);

        Assert.Equal(16, width);
        Assert.Equal(16, height);
        Assert.Equal(new SvgColor(0, 0, 255, 255), Pixel(pixels, width, 0, 15));
        Assert.Equal(new SvgColor(0, 0, 255, 255), Pixel(pixels, width, 15, 15));
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(pixels, width, 0, 0));
    }

    [Fact]
    public void Radial_fill_with_solid_stroke_on_curved_path_paints_outline()
    {
        // S13: curved radial fills compose with solid outlines through
        // flattened geometry; the axis-aligned bottom-edge stroke pixel is
        // exact while the curve carries the standing residual.
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><defs><radialGradient id='g'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><path d='M0,8 Q8,0 16,8 L16,16 L0,16 Z' fill='url(#g)' stroke='blue' stroke-width='2'/></svg>", out int width, out int height);

        Assert.Equal(16, width);
        Assert.Equal(16, height);
        Assert.Equal(new SvgColor(0, 0, 255, 255), Pixel(pixels, width, 8, 15));
        Assert.Equal(new SvgColor(0, 0, 255, 255), Pixel(pixels, width, 0, 15));
    }

    [Fact]
    public void Radial_from_linear_spread_matches_local_spread_twin()
    {
        // S13: unspecified spread and units follow the linear parent, so a
        // cross-type reflect render matches the locally declared reflect twin.
        byte[] crossed = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><defs><linearGradient id='l' spreadMethod='reflect'><stop offset='0' stop-color='#ff0000'/><stop offset='0.5' stop-color='#0000ff'/></linearGradient><radialGradient id='r' href='#l'/></defs><rect x='2' y='2' width='12' height='12' fill='url(#r)'/></svg>", out _, out _);
        byte[] local = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><defs><radialGradient id='r' spreadMethod='reflect'><stop offset='0' stop-color='#ff0000'/><stop offset='0.5' stop-color='#0000ff'/></radialGradient></defs><rect x='2' y='2' width='12' height='12' fill='url(#r)'/></svg>", out _, out _);

        Assert.Equal(local, crossed);
    }

    [Fact]
    public void Linear_from_radial_href_matches_same_type_twin()
    {
        // S13: a linear gradient inherits stops through a cross-type href,
        // rendering exactly like the same stops declared locally while the
        // vector stays linear-local.
        byte[] crossed = RenderPixels("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"16\"><defs><radialGradient id=\"r\"><stop offset=\"0\" stop-color=\"#ff0000\"/><stop offset=\"1\" stop-color=\"#0000ff\"/></radialGradient><linearGradient id=\"l\" href=\"#r\"/></defs><rect x=\"2\" y=\"2\" width=\"12\" height=\"12\" fill=\"url(#l)\"/></svg>", out _, out _);
        byte[] local = RenderPixels("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"16\"><defs><linearGradient id=\"l\"><stop offset=\"0\" stop-color=\"#ff0000\"/><stop offset=\"1\" stop-color=\"#0000ff\"/></linearGradient></defs><rect x=\"2\" y=\"2\" width=\"12\" height=\"12\" fill=\"url(#l)\"/></svg>", out _, out _);

        Assert.Equal(local, crossed);
    }

    [Fact]
    public void Linear_from_radial_xlink_href_matches_same_type_twin()
    {
        // S13: the cross-type inheritance accepts the xlink:href spelling.
        byte[] crossed = RenderPixels("<svg xmlns=\"http://www.w3.org/2000/svg\" xmlns:xlink=\"http://www.w3.org/1999/xlink\" width=\"16\" height=\"16\"><defs><radialGradient id=\"r\"><stop offset=\"0\" stop-color=\"#ff0000\"/><stop offset=\"1\" stop-color=\"#0000ff\"/></radialGradient><linearGradient id=\"l\" xlink:href=\"#r\"/></defs><rect x=\"2\" y=\"2\" width=\"12\" height=\"12\" fill=\"url(#l)\"/></svg>", out _, out _);
        byte[] local = RenderPixels("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"16\"><defs><linearGradient id=\"l\"><stop offset=\"0\" stop-color=\"#ff0000\"/><stop offset=\"1\" stop-color=\"#0000ff\"/></linearGradient></defs><rect x=\"2\" y=\"2\" width=\"12\" height=\"12\" fill=\"url(#l)\"/></svg>", out _, out _);

        Assert.Equal(local, crossed);
    }

    [Fact]
    public void Linear_from_radial_spread_matches_local_spread_twin()
    {
        // S13: unspecified spread and units follow the radial parent, so a
        // cross-type reflect render matches the locally declared reflect twin.
        byte[] crossed = RenderPixels("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"16\"><defs><radialGradient id=\"r\" spreadMethod=\"reflect\"><stop offset=\"0\" stop-color=\"#ff0000\"/><stop offset=\"0.5\" stop-color=\"#0000ff\"/></radialGradient><linearGradient id=\"l\" href=\"#r\"/></defs><rect x=\"2\" y=\"2\" width=\"12\" height=\"12\" fill=\"url(#l)\"/></svg>", out _, out _);
        byte[] local = RenderPixels("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"16\"><defs><linearGradient id=\"l\" spreadMethod=\"reflect\"><stop offset=\"0\" stop-color=\"#ff0000\"/><stop offset=\"0.5\" stop-color=\"#0000ff\"/></linearGradient></defs><rect x=\"2\" y=\"2\" width=\"12\" height=\"12\" fill=\"url(#l)\"/></svg>", out _, out _);

        Assert.Equal(local, crossed);
    }

    [Fact]
    public void Linear_partial_vector_with_radial_href_stays_unsupported()
    {
        // S13: partial vector overrides have no cross-type meaning, mirroring
        // the partial radial vector gate in the other direction.
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"16\"><defs><radialGradient id=\"r\"><stop offset=\"0\" stop-color=\"#ff0000\"/><stop offset=\"1\" stop-color=\"#0000ff\"/></radialGradient><linearGradient id=\"l\" href=\"#r\" x1=\"0\"/></defs><rect x=\"2\" y=\"2\" width=\"12\" height=\"12\" fill=\"url(#l)\"/></svg>");

        SvgException rejected = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, rejected.ErrorCode);
        Assert.Contains("Partial gradient vector overrides with cross-type gradient references are not implemented.", rejected.Message);
    }

    [Fact]
    public void Pattern_filled_diagonal_polygon_tiles_exactly()
    {
        // S13/S16: pattern fills lower diagonal outlines through the shared
        // polygon machinery repainted with the tile, byte-exact against the
        // pinned Chrome capture of the same document.
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><defs><pattern id='p' width='4' height='4' patternUnits='userSpaceOnUse'><rect width='2' height='2' fill='red'/></pattern></defs><polygon points='0,0 16,0 0,16' fill='url(#p)'/></svg>", out int width, out int height);

        Assert.Equal(16, width);
        Assert.Equal(16, height);
        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, width, 1, 1));
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(pixels, width, 3, 1));
        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, width, 13, 1));
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(pixels, width, 15, 15));
    }

    [Fact]
    public void Shared_clip_path_binds_each_object_box()
    {
        // S14: one objectBoundingBox clip definition resolves per referencing
        // box; the second rect keeps its full half width instead of inheriting
        // the first rects narrower clip.
        byte[] pixels = RenderPixels("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"16\"><defs><clipPath id=\"c\" clipPathUnits=\"objectBoundingBox\"><rect x=\"0\" y=\"0\" width=\"0.5\" height=\"1\"/></clipPath></defs><rect x=\"0\" y=\"0\" width=\"4\" height=\"8\" fill=\"red\" clip-path=\"url(#c)\"/><rect x=\"8\" y=\"0\" width=\"8\" height=\"8\" fill=\"blue\" clip-path=\"url(#c)\"/></svg>", out int width, out int height);

        Assert.Equal(16, width);
        Assert.Equal(16, height);
        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, width, 0, 0));
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(pixels, width, 3, 0));
        Assert.Equal(new SvgColor(0, 0, 255, 255), Pixel(pixels, width, 8, 0));
        Assert.Equal(new SvgColor(0, 0, 255, 255), Pixel(pixels, width, 11, 0));
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(pixels, width, 13, 0));
        Assert.Equal(12240, AlphaSum(pixels));
    }

    [Fact]
    public void Shared_gradient_binds_each_object_box()
    {
        // S13: one objectBoundingBox gradient definition evaluates per
        // referencing box; the second ramp restarts instead of continuing.
        byte[] pixels = RenderPixels("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"16\"><defs><linearGradient id=\"g\" x1=\"0\" y1=\"0\" x2=\"1\" y2=\"0\"><stop offset=\"0\" stop-color=\"#ff0000\"/><stop offset=\"1\" stop-color=\"#0000ff\"/></linearGradient></defs><rect x=\"0\" y=\"0\" width=\"4\" height=\"8\" fill=\"url(#g)\"/><rect x=\"8\" y=\"0\" width=\"8\" height=\"8\" fill=\"url(#g)\"/></svg>", out _, out _);

        SvgColor left = Pixel(pixels, 16, 0, 0);
        SvgColor narrowRight = Pixel(pixels, 16, 3, 0);
        SvgColor wideLeft = Pixel(pixels, 16, 8, 0);
        SvgColor wideRight = Pixel(pixels, 16, 15, 0);
        Assert.True(left.Red > left.Blue);
        Assert.True(narrowRight.Blue > narrowRight.Red);
        Assert.True(wideLeft.Red > wideLeft.Blue);
        Assert.True(wideRight.Blue > wideRight.Red);
        Assert.Equal(24480, AlphaSum(pixels));
    }

    [Fact]
    public void Gradient_filled_diagonal_path_matches_polygon()
    {
        // S13/S16: diagonal paths share the point-list gradient-fill lowering,
        // so the same outline renders byte-identically through both spellings.
        byte[] path = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><defs><linearGradient id='g' x1='0' y1='0' x2='1' y2='0'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><path d='M0,0 L16,0 L0,16 Z' fill='url(#g)'/></svg>", out _, out _);
        byte[] polygon = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><defs><linearGradient id='g' x1='0' y1='0' x2='1' y2='0'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><polygon points='0,0 16,0 0,16' fill='url(#g)'/></svg>", out _, out _);

        Assert.Equal(polygon, path);
    }

    [Fact]
    public void Radial_filled_diagonal_path_matches_polygon()
    {
        // S13/S16: diagonal paths share the point-list radial-fill lowering,
        // so the same circle renders byte-identically through both spellings.
        byte[] path = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><defs><radialGradient id='g'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><path d='M0,0 L16,0 L0,16 Z' fill='url(#g)'/></svg>", out _, out _);
        byte[] polygon = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><defs><radialGradient id='g'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><polygon points='0,0 16,0 0,16' fill='url(#g)'/></svg>", out _, out _);

        Assert.Equal(polygon, path);
    }

    [Fact]
    public void Pattern_filled_curved_path_tiles_arch_interior()
    {
        // S13/S16: curved pattern fills flatten through shared device outlines
        // repainted with the tile; interior pixels are exact while the curved
        // edge carries a 3-pixel flattening residual against Chrome.
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><defs><pattern id='p' width='4' height='4' patternUnits='userSpaceOnUse'><rect width='2' height='2' fill='red'/></pattern></defs><path d='M0,8 Q8,0 16,8 L16,16 L0,16 Z' fill='url(#p)'/></svg>", out int width, out int height);

        Assert.Equal(16, width);
        Assert.Equal(16, height);
        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, width, 1, 8));
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(pixels, width, 3, 8));
        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, width, 13, 8));
    }

    [Fact]
    public void Pattern_filled_diagonal_path_matches_polygon()
    {
        // S13/S16: diagonal paths share the point-list pattern-fill lowering,
        // so the same outline renders byte-identically through both spellings.
        byte[] path = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><defs><pattern id='p' width='4' height='4' patternUnits='userSpaceOnUse'><rect width='2' height='2' fill='red'/></pattern></defs><path d='M0,0 L16,0 L0,16 Z' fill='url(#p)'/></svg>", out _, out _);
        byte[] polygon = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><defs><pattern id='p' width='4' height='4' patternUnits='userSpaceOnUse'><rect width='2' height='2' fill='red'/></pattern></defs><polygon points='0,0 16,0 0,16' fill='url(#p)'/></svg>", out _, out _);

        Assert.Equal(polygon, path);
    }

    [Fact]
    public void Root_radial_gradient_matches_defs_twin()
    {
        byte[] rooted = RenderPixels("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"16\"><radialGradient id=\"g\"><stop offset=\"0\" stop-color=\"#ff0000\"/><stop offset=\"1\" stop-color=\"#0000ff\"/></radialGradient><circle cx=\"8\" cy=\"8\" r=\"5\" fill=\"url(#g)\"/></svg>", out _, out _);
        byte[] defsed = RenderPixels("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"16\"><defs><radialGradient id=\"g\"><stop offset=\"0\" stop-color=\"#ff0000\"/><stop offset=\"1\" stop-color=\"#0000ff\"/></radialGradient></defs><circle cx=\"8\" cy=\"8\" r=\"5\" fill=\"url(#g)\"/></svg>", out _, out _);

        Assert.Equal(defsed, rooted);
    }

    [Fact]
    public void Scaled_viewBox_rounded_matches_direct_rendering()
    {
        byte[] viewport = RenderPixels("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"64\" height=\"64\" viewBox=\"0 0 32 32\"><rect x=\"4\" y=\"4\" width=\"24\" height=\"24\" rx=\"6\" fill=\"red\"/></svg>", out _, out _);
        byte[] direct = RenderPixels("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"64\" height=\"64\"><rect x=\"8\" y=\"8\" width=\"48\" height=\"48\" rx=\"12\" fill=\"red\"/></svg>", out _, out _);

        Assert.Equal(direct, viewport);
    }
    [Fact]
    public void Rounded_rectangle_cuts_corners()
    {
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='64' height='64'><rect x='8' y='8' width='48' height='48' rx='8' ry='8' fill='red'/></svg>", out _, out _);

        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, 64, 32, 32));
        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, 64, 32, 10));
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(pixels, 64, 8, 8));
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(pixels, 64, 56, 56));
    }

    [Fact]
    public void Corner_radius_defaults_to_sibling()
    {
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='64' height='64'><rect x='8' y='8' width='48' height='16' rx='8' fill='red'/></svg>", out _, out _);

        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, 64, 32, 10));
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(pixels, 64, 8, 8));
    }

    [Fact]
    public void Zero_radius_draws_nothing()
    {
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><circle cx='4' cy='4' r='0' fill='red'/></svg>", out _, out _);

        Assert.Equal(0, AlphaSum(pixels));
    }

    [Theory]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><circle cx='4' cy='4' r='-1' fill='red'/></svg>")]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><ellipse cx='4' cy='4' rx='-1' ry='2' fill='red'/></svg>")]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><rect x='1' y='1' width='6' height='6' rx='-1' fill='red'/></svg>")]
    public void Negative_shape_radii_are_malformed(string svg)
    {
        SvgException rejected = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(System.Text.Encoding.UTF8.GetBytes(svg), SvgRenderOptions.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.MalformedInput, rejected.ErrorCode);
    }

    [Fact]
    public void Quarter_center_renders()
    {
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><circle cx='4.5' cy='3.5' r='2' fill='red'/></svg>", out _, out _);

        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, 8, 4, 3));
    }

    [Fact]
    public void Fractional_beyond_quarter_renders()
    {
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><circle cx='4.1' cy='4' r='2' fill='red'/></svg>", out _, out _);

        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, 8, 4, 4));
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(pixels, 8, 0, 0));
    }

    [Fact]
    public void Fractional_rectangle_renders_exact_analytic_coverage()
    {
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><rect x='1.125' y='2' width='4' height='3' fill='red'/></svg>", out _, out _);

        Assert.Equal(new SvgColor(255, 0, 0, 224), Pixel(pixels, 8, 1, 2));
        Assert.Equal(new SvgColor(255, 0, 0, 32), Pixel(pixels, 8, 5, 2));
        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, 8, 3, 3));
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(pixels, 8, 0, 2));
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(pixels, 8, 6, 2));
    }

    [Fact]
    public void Fractional_translate_shifts_exact_coverage()
    {
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><g transform='translate(2.5, 0)'><rect x='0' y='2' width='2' height='2' fill='red'/></g></svg>", out _, out _);

        Assert.Equal(new SvgColor(255, 0, 0, 128), Pixel(pixels, 8, 2, 2));
        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, 8, 3, 2));
        Assert.Equal(new SvgColor(255, 0, 0, 128), Pixel(pixels, 8, 4, 2));
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(pixels, 8, 0, 2));
    }

    [Fact]
    public void Fractional_scale_maps_exact_coverage()
    {
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><g transform='scale(1.5)'><rect x='1' y='1' width='3' height='3' fill='red'/></g></svg>", out _, out _);

        Assert.Equal(new SvgColor(255, 0, 0, 128), Pixel(pixels, 16, 1, 2));
        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, 16, 3, 3));
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(pixels, 16, 0, 0));
    }

    [Fact]
    public void Fractional_effective_line_width_matches_direct_native_scaling()
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><defs><line id='l' x1='0' y1='0' x2='4' y2='0' stroke='red' stroke-width='2'/></defs><use href='#l' transform='scale(1.25)'/></svg>");

        byte[] direct = "<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><line x1='0' y1='0' x2='4' y2='0' stroke='red' stroke-width='2' transform='scale(1.25)'/></svg>"u8.ToArray();
        Assert.Equal(SvgConverter.ConvertToPng(direct, SvgRenderOptions.Default, CancellationToken.None).ToArray(), SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None).ToArray());
    }

    [Fact]
    public void Fractional_rectangle_stroke_renders_exact_bands()
    {
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><rect x='4.5' y='4.5' width='5' height='5' fill='none' stroke='black' stroke-width='2'/></svg>", out _, out _);

        Assert.Equal(new SvgColor(0, 0, 0, 255), Pixel(pixels, 16, 4, 4));
        Assert.Equal(new SvgColor(0, 0, 0, 128), Pixel(pixels, 16, 3, 4));
        Assert.Equal(new SvgColor(0, 0, 0, 128), Pixel(pixels, 16, 10, 4));
        Assert.Equal(new SvgColor(0, 0, 0, 255), Pixel(pixels, 16, 4, 7));
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(pixels, 16, 7, 7));
    }

    [Fact]
    public void Odd_width_rectangle_stroke_renders_exact_bands()
    {
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><rect x='2' y='2' width='8' height='8' fill='red' stroke='black' stroke-width='1'/></svg>", out _, out _);

        Assert.Equal(new SvgColor(0, 0, 0, 128), Pixel(pixels, 16, 5, 1));
        Assert.Equal(new SvgColor(0, 0, 0, 128), Pixel(pixels, 16, 1, 5));
        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, 16, 5, 5));
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(pixels, 16, 0, 0));
    }

    [Fact]
    public void Scaled_rectangle_stroke_scales_its_width_with_the_shape()
    {
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='24' height='24'><rect x='2' y='2' width='8' height='8' fill='red' stroke='black' stroke-width='2' transform='scale(2)'/></svg>", out _, out _);

        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(pixels, 24, 1, 10));
        Assert.Equal(new SvgColor(0, 0, 0, 255), Pixel(pixels, 24, 2, 10));
        Assert.Equal(new SvgColor(0, 0, 0, 255), Pixel(pixels, 24, 5, 10));
        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, 24, 6, 10));
        Assert.Equal(new SvgColor(0, 0, 0, 255), Pixel(pixels, 24, 21, 10));
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(pixels, 24, 22, 10));
    }

    [Fact]
    public void Oversize_rectangle_stroke_collapses_its_native_inner_frame()
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><rect x='2' y='2' width='4' height='4' fill='red' stroke='black' stroke-width='6'/></svg>");

        ReadOnlyMemory<byte> png = SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None);
        Assert.False(png.IsEmpty);
    }

    [Fact]
    public void Gradient_fill_on_circle_renders_ramp()
    {
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><linearGradient id='g'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><circle cx='4' cy='4' r='2' fill='url(#g)'/></svg>", out _, out _);

        Assert.True(Pixel(pixels, 8, 3, 4).Red > Pixel(pixels, 8, 3, 4).Blue);
        Assert.True(Pixel(pixels, 8, 5, 4).Blue > Pixel(pixels, 8, 5, 4).Red);
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(pixels, 8, 0, 0));
    }

    [Fact]
    public void Circle_stroke_renders_ring_over_fill()
    {
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><circle cx='8' cy='8' r='6' fill='red' stroke='blue' stroke-width='2'/></svg>", out _, out _);

        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, 16, 8, 8));
        Assert.Equal(new SvgColor(0, 0, 255, 255), Pixel(pixels, 16, 8, 2));
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(pixels, 16, 8, 0));
    }

    [Fact]
    public void Circle_stroke_without_fill_leaves_transparent_hole()
    {
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><circle cx='8' cy='8' r='6' fill='none' stroke='blue' stroke-width='2'/></svg>", out _, out _);

        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(pixels, 16, 8, 8));
        Assert.Equal(new SvgColor(0, 0, 255, 255), Pixel(pixels, 16, 8, 2));
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(pixels, 16, 8, 0));
    }

    [Fact]
    public void Circle_stroke_evenodd_matches_nonzero()
    {
        byte[] nonzero = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><circle cx='8' cy='8' r='6' fill='red' stroke='blue' stroke-width='2'/></svg>", out _, out _);
        byte[] evenOdd = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><circle cx='8' cy='8' r='6' fill='red' stroke='blue' stroke-width='2' fill-rule='evenodd'/></svg>", out _, out _);

        Assert.Equal(nonzero, evenOdd);
    }

    [Fact]
    public void Circle_stroke_use_matches_direct()
    {
        byte[] direct = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><circle cx='8' cy='8' r='6' fill='red' stroke='blue' stroke-width='2'/></svg>", out _, out _);
        byte[] instanced = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><defs><circle id='c' cx='8' cy='8' r='6' fill='red' stroke='blue' stroke-width='2'/></defs><use href='#c'/></svg>", out _, out _);

        Assert.Equal(direct, instanced);
    }

    [Fact]
    public void Thick_circle_stroke_preserves_the_native_inner_offset_coverage()
    {
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><circle cx='8' cy='8' r='4' fill='red' stroke='blue' stroke-width='10'/></svg>", out _, out _);

        // The pinned browser's native offset inverts the inner curve at this
        // width; its center has partial blue coverage over the original red fill.
        Assert.Equal(new SvgColor(188, 0, 67, 255), Pixel(pixels, 16, 8, 8));
    }

    [Fact]
    public void Zero_radius_circle_stroke_paints_nothing()
    {
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><circle cx='8' cy='8' r='0' fill='none' stroke='red' stroke-width='2'/></svg>", out _, out _);

        Assert.Equal(0, AlphaSum(pixels));
    }

    [Fact]
    public void Odd_width_circle_stroke_renders()
    {
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><circle cx='8' cy='8' r='5' fill='none' stroke='red' stroke-width='3'/></svg>", out _, out _);

        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(pixels, 16, 8, 8));
        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, 16, 8, 3));
        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, 16, 2, 8));
    }

    [Fact]
    public void Masked_circle_stroke_matches_unmasked()
    {
        byte[] direct = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><circle cx='8' cy='8' r='6' fill='red' stroke='blue' stroke-width='2'/></svg>", out _, out _);
        byte[] masked = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><defs><mask id='m'><rect x='0' y='0' width='16' height='16' fill='#ffffff'/></mask></defs><circle cx='8' cy='8' r='6' fill='red' stroke='blue' stroke-width='2' mask='url(#m)'/></svg>", out _, out _);

        Assert.Equal(direct, masked);
    }

    [Fact]
    public void Ellipse_stroke_renders_ring()
    {
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><ellipse cx='8' cy='8' rx='6' ry='4' fill='none' stroke='red' stroke-width='2'/></svg>", out _, out _);

        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(pixels, 16, 8, 8));
        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, 16, 13, 8));
    }

    [Fact]
    public void ViewBox_scaled_circle_matches_direct_rendering()
    {
        byte[] scaled = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='128' height='128' viewBox='0 0 8 8'><circle cx='4' cy='4' r='3' fill='red'/></svg>", out _, out _);
        byte[] direct = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='128' height='128'><circle cx='64' cy='64' r='48' fill='red'/></svg>", out _, out _);
        Assert.Equal(direct, scaled);
    }
    [Fact]
    public void ViewBox_scaled_ellipse_matches_direct_rendering()
    {
        byte[] scaled = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='64' height='64' viewBox='0 0 16 16'><ellipse cx='8' cy='8' rx='5' ry='3' fill='red'/></svg>", out _, out _);
        byte[] direct = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='64' height='64'><ellipse cx='32' cy='32' rx='20' ry='12' fill='red'/></svg>", out _, out _);
        Assert.Equal(direct, scaled);
    }

    [Fact]
    public void OutputScale_rect_matches_enlarged_direct_rendering()
    {
        byte[] small = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='64' height='64'><rect x='8' y='8' width='48' height='48' fill='red'/></svg>");
        byte[] large = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='128' height='128'><rect x='16' y='16' width='96' height='96' fill='red'/></svg>");
        SvgRenderOptions scaled = new(null, 2, SvgColor.Transparent, SvgRenderLimits.Default);
        SvgRenderOptions direct = new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default);
        ReadOnlyMemory<byte> scaledPng = SvgConverter.ConvertToPng(small, scaled, CancellationToken.None);
        ReadOnlyMemory<byte> directPng = SvgConverter.ConvertToPng(large, direct, CancellationToken.None);

        Assert.True(scaledPng.Span.SequenceEqual(directPng.Span));
    }

    [Fact]
    public void OutputScale_circle_matches_enlarged_direct_rendering()
    {
        byte[] small = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='64' height='64'><circle cx='32' cy='32' r='20' fill='red'/></svg>");
        byte[] large = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='128' height='128'><circle cx='64' cy='64' r='40' fill='red'/></svg>");
        SvgRenderOptions scaled = new(null, 2, SvgColor.Transparent, SvgRenderLimits.Default);
        SvgRenderOptions direct = new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default);
        ReadOnlyMemory<byte> scaledPng = SvgConverter.ConvertToPng(small, scaled, CancellationToken.None);
        ReadOnlyMemory<byte> directPng = SvgConverter.ConvertToPng(large, direct, CancellationToken.None);

        Assert.True(scaledPng.Span.SequenceEqual(directPng.Span));
    }

    [Fact]
    public void OutputScale_diagonal_matches_enlarged_direct_rendering()
    {
        byte[] small = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='64' height='64'><path d='M8,56 L32,8 L56,56 Z' fill='red'/></svg>");
        byte[] large = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='128' height='128'><path d='M16,112 L64,16 L112,112 Z' fill='red'/></svg>");
        SvgRenderOptions scaled = new(null, 2, SvgColor.Transparent, SvgRenderLimits.Default);
        SvgRenderOptions direct = new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default);
        ReadOnlyMemory<byte> scaledPng = SvgConverter.ConvertToPng(small, scaled, CancellationToken.None);
        ReadOnlyMemory<byte> directPng = SvgConverter.ConvertToPng(large, direct, CancellationToken.None);

        Assert.True(scaledPng.Span.SequenceEqual(directPng.Span));
    }

    [Fact]
    public void OutputScale_gradient_matches_enlarged_direct_rendering()
    {
        string gradient = "<defs><linearGradient id='g' x1='0' y1='0' x2='1' y2='0'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs>";
        byte[] small = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='64' height='64'>" + gradient + "<rect x='8' y='8' width='48' height='48' fill='url(#g)'/></svg>");
        byte[] large = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='128' height='128'>" + gradient + "<rect x='16' y='16' width='96' height='96' fill='url(#g)'/></svg>");
        SvgRenderOptions scaled = new(null, 2, SvgColor.Transparent, SvgRenderLimits.Default);
        SvgRenderOptions direct = new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default);
        ReadOnlyMemory<byte> scaledPng = SvgConverter.ConvertToPng(small, scaled, CancellationToken.None);
        ReadOnlyMemory<byte> directPng = SvgConverter.ConvertToPng(large, direct, CancellationToken.None);

        Assert.True(scaledPng.Span.SequenceEqual(directPng.Span));
    }

    [Fact]
    public void Composed_viewBox_outputScale_matches_enlarged_direct_rendering()
    {
        byte[] small = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='128' height='128' viewBox='0 0 8 8'><circle cx='4' cy='4' r='3' fill='red'/></svg>");
        byte[] large = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='256' height='256'><circle cx='128' cy='128' r='96' fill='red'/></svg>");
        SvgRenderOptions scaled = new(null, 2, SvgColor.Transparent, SvgRenderLimits.Default);
        SvgRenderOptions direct = new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default);
        ReadOnlyMemory<byte> scaledPng = SvgConverter.ConvertToPng(small, scaled, CancellationToken.None);
        ReadOnlyMemory<byte> directPng = SvgConverter.ConvertToPng(large, direct, CancellationToken.None);

        Assert.True(scaledPng.Span.SequenceEqual(directPng.Span));
    }

    [Fact]
    public void OutputScale_radial_matches_enlarged_direct_rendering()
    {
        string radial = "<defs><radialGradient id='g' cx='0.5' cy='0.5' r='0.5'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs>";
        byte[] small = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='64' height='64'>" + radial + "<rect x='8' y='8' width='48' height='48' fill='url(#g)'/></svg>");
        byte[] large = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='128' height='128'>" + radial + "<rect x='16' y='16' width='96' height='96' fill='url(#g)'/></svg>");
        SvgRenderOptions scaled = new(null, 2, SvgColor.Transparent, SvgRenderLimits.Default);
        SvgRenderOptions direct = new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default);
        ReadOnlyMemory<byte> scaledPng = SvgConverter.ConvertToPng(small, scaled, CancellationToken.None);
        ReadOnlyMemory<byte> directPng = SvgConverter.ConvertToPng(large, direct, CancellationToken.None);

        Assert.True(scaledPng.Span.SequenceEqual(directPng.Span));
    }

    [Fact]
    public void OutputScale_line_stroke_matches_enlarged_direct_rendering()
    {
        byte[] small = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='64' height='64'><line x1='8' y1='32' x2='56' y2='32' stroke='red' stroke-width='4'/></svg>");
        byte[] large = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='128' height='128'><line x1='16' y1='64' x2='112' y2='64' stroke='red' stroke-width='8'/></svg>");
        SvgRenderOptions scaled = new(null, 2, SvgColor.Transparent, SvgRenderLimits.Default);
        SvgRenderOptions direct = new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default);
        ReadOnlyMemory<byte> scaledPng = SvgConverter.ConvertToPng(small, scaled, CancellationToken.None);
        ReadOnlyMemory<byte> directPng = SvgConverter.ConvertToPng(large, direct, CancellationToken.None);

        Assert.True(scaledPng.Span.SequenceEqual(directPng.Span));
    }

    [Fact]
    public void OutputScale_pattern_matches_scaled_tile_direct_rendering()
    {
        string tile4 = "<defs><pattern id='p' width='4' height='4' patternUnits='userSpaceOnUse'><rect width='2' height='2' fill='red'/></pattern></defs>";
        string tile8 = "<defs><pattern id='p' width='8' height='8' patternUnits='userSpaceOnUse'><rect width='4' height='4' fill='red'/></pattern></defs>";
        byte[] small = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='64' height='64'>" + tile4 + "<rect x='8' y='8' width='48' height='48' fill='url(#p)'/></svg>");
        byte[] large = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='128' height='128'>" + tile8 + "<rect x='16' y='16' width='96' height='96' fill='url(#p)'/></svg>");
        SvgRenderOptions scaled = new(null, 2, SvgColor.Transparent, SvgRenderLimits.Default);
        SvgRenderOptions direct = new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default);
        ReadOnlyMemory<byte> scaledPng = SvgConverter.ConvertToPng(small, scaled, CancellationToken.None);
        ReadOnlyMemory<byte> directPng = SvgConverter.ConvertToPng(large, direct, CancellationToken.None);

        Assert.True(scaledPng.Span.SequenceEqual(directPng.Span));
    }

    [Fact]
    public void OutputScale_circle_stroke_matches_enlarged_direct_rendering()
    {
        byte[] small = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='64' height='64'><circle cx='32' cy='32' r='16' fill='none' stroke='red' stroke-width='8'/></svg>");
        byte[] large = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='128' height='128'><circle cx='64' cy='64' r='32' fill='none' stroke='red' stroke-width='16'/></svg>");
        SvgRenderOptions scaled = new(null, 2, SvgColor.Transparent, SvgRenderLimits.Default);
        SvgRenderOptions direct = new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default);
        ReadOnlyMemory<byte> scaledPng = SvgConverter.ConvertToPng(small, scaled, CancellationToken.None);
        ReadOnlyMemory<byte> directPng = SvgConverter.ConvertToPng(large, direct, CancellationToken.None);

        Assert.True(scaledPng.Span.SequenceEqual(directPng.Span));
    }

    [Fact]
    public void OutputScale_group_opacity_matches_enlarged_direct_rendering()
    {
        byte[] small = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='64' height='64'><g opacity='0.5'><rect x='8' y='8' width='48' height='48' fill='red'/><rect x='8' y='8' width='48' height='48' fill='blue'/></g></svg>");
        byte[] large = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='128' height='128'><g opacity='0.5'><rect x='16' y='16' width='96' height='96' fill='red'/><rect x='16' y='16' width='96' height='96' fill='blue'/></g></svg>");
        SvgRenderOptions scaled = new(null, 2, SvgColor.Transparent, SvgRenderLimits.Default);
        SvgRenderOptions direct = new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default);
        ReadOnlyMemory<byte> scaledPng = SvgConverter.ConvertToPng(small, scaled, CancellationToken.None);
        ReadOnlyMemory<byte> directPng = SvgConverter.ConvertToPng(large, direct, CancellationToken.None);

        Assert.True(scaledPng.Span.SequenceEqual(directPng.Span));
    }

    [Fact]
    public void OutputScale_mask_matches_enlarged_direct_rendering()
    {
        string mask64 = "<defs><mask id='m'><rect x='0' y='0' width='64' height='64' fill='#808080'/></mask></defs>";
        string mask128 = "<defs><mask id='m'><rect x='0' y='0' width='128' height='128' fill='#808080'/></mask></defs>";
        byte[] small = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='64' height='64'>" + mask64 + "<rect x='8' y='8' width='48' height='48' fill='red' mask='url(#m)'/></svg>");
        byte[] large = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='128' height='128'>" + mask128 + "<rect x='16' y='16' width='96' height='96' fill='red' mask='url(#m)'/></svg>");
        SvgRenderOptions scaled = new(null, 2, SvgColor.Transparent, SvgRenderLimits.Default);
        SvgRenderOptions direct = new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default);
        ReadOnlyMemory<byte> scaledPng = SvgConverter.ConvertToPng(small, scaled, CancellationToken.None);
        ReadOnlyMemory<byte> directPng = SvgConverter.ConvertToPng(large, direct, CancellationToken.None);

        Assert.True(scaledPng.Span.SequenceEqual(directPng.Span));
    }

    [Fact]
    public void OutputScale_image_matches_enlarged_direct_rendering()
    {
        string pixel = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGP4z8DwHwAFAAH/iZk9HQAAAABJRU5ErkJggg==";
        byte[] small = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='64' height='64'><image href='" + pixel + "' x='8' y='8' width='48' height='48'/></svg>");
        byte[] large = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='128' height='128'><image href='" + pixel + "' x='16' y='16' width='96' height='96'/></svg>");
        SvgRenderOptions scaled = new(null, 2, SvgColor.Transparent, SvgRenderLimits.Default);
        SvgRenderOptions direct = new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default);
        ReadOnlyMemory<byte> scaledPng = SvgConverter.ConvertToPng(small, scaled, CancellationToken.None);
        ReadOnlyMemory<byte> directPng = SvgConverter.ConvertToPng(large, direct, CancellationToken.None);

        Assert.True(scaledPng.Span.SequenceEqual(directPng.Span));
    }

    [Fact]
    public void OutputScale_nested_viewport_matches_enlarged_direct_rendering()
    {
        byte[] small = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='64' height='64'><svg x='8' y='8' width='48' height='48'><rect x='8' y='8' width='48' height='48' fill='red'/></svg></svg>");
        byte[] large = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='128' height='128'><svg x='16' y='16' width='96' height='96'><rect x='16' y='16' width='96' height='96' fill='red'/></svg></svg>");
        SvgRenderOptions scaled = new(null, 2, SvgColor.Transparent, SvgRenderLimits.Default);
        SvgRenderOptions direct = new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default);
        ReadOnlyMemory<byte> scaledPng = SvgConverter.ConvertToPng(small, scaled, CancellationToken.None);
        ReadOnlyMemory<byte> directPng = SvgConverter.ConvertToPng(large, direct, CancellationToken.None);

        Assert.True(scaledPng.Span.SequenceEqual(directPng.Span));
    }

    [Fact]
    public void OutputScale_translated_rect_matches_enlarged_direct_rendering()
    {
        byte[] small = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='64' height='64'><rect x='0' y='0' width='48' height='48' fill='red' transform='translate(8,8)'/></svg>");
        byte[] large = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='128' height='128'><rect x='0' y='0' width='96' height='96' fill='red' transform='translate(16,16)'/></svg>");
        SvgRenderOptions scaled = new(null, 2, SvgColor.Transparent, SvgRenderLimits.Default);
        SvgRenderOptions direct = new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default);
        ReadOnlyMemory<byte> scaledPng = SvgConverter.ConvertToPng(small, scaled, CancellationToken.None);
        ReadOnlyMemory<byte> directPng = SvgConverter.ConvertToPng(large, direct, CancellationToken.None);

        Assert.True(scaledPng.Span.SequenceEqual(directPng.Span));
    }

    [Fact]
    public void Fractional_outputScale_matches_enlarged_direct_rendering()
    {
        byte[] small = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='64' height='64'><rect x='8' y='8' width='48' height='48' fill='red'/></svg>");
        byte[] large = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='96' height='96'><rect x='12' y='12' width='72' height='72' fill='red'/></svg>");
        SvgRenderOptions scaled = new(null, 1.5, SvgColor.Transparent, SvgRenderLimits.Default);
        SvgRenderOptions direct = new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default);
        ReadOnlyMemory<byte> scaledPng = SvgConverter.ConvertToPng(small, scaled, CancellationToken.None);
        ReadOnlyMemory<byte> directPng = SvgConverter.ConvertToPng(large, direct, CancellationToken.None);

        Assert.True(scaledPng.Span.SequenceEqual(directPng.Span));
    }

    [Fact]
    public void OutputScale_banded_disc_matches_enlarged_direct_rendering()
    {
        byte[] small = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='70' height='60'><rect width='70' height='60' fill='#0DB02B'/><rect width='70' height='40' fill='#FFF'/><rect width='70' height='20' fill='#e05206'/><circle cx='35' cy='30' r='8' fill='#e05206'/></svg>");
        byte[] large = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='140' height='120'><rect width='140' height='120' fill='#0DB02B'/><rect width='140' height='80' fill='#FFF'/><rect width='140' height='40' fill='#e05206'/><circle cx='70' cy='60' r='16' fill='#e05206'/></svg>");
        SvgRenderOptions scaled = new(null, 2, SvgColor.Transparent, SvgRenderLimits.Default);
        SvgRenderOptions direct = new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default);
        ReadOnlyMemory<byte> scaledPng = SvgConverter.ConvertToPng(small, scaled, CancellationToken.None);
        ReadOnlyMemory<byte> directPng = SvgConverter.ConvertToPng(large, direct, CancellationToken.None);

        Assert.True(scaledPng.Span.SequenceEqual(directPng.Span));
    }

    [Fact]
    public void Circle_sized_ellipse_stroke_matches_circle_stroke()
    {
        byte[] ellipse = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><ellipse cx='8' cy='8' rx='6' ry='6' fill='none' stroke='red' stroke-width='2'/></svg>", out _, out _);
        byte[] circle = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><circle cx='8' cy='8' r='6' fill='none' stroke='red' stroke-width='2'/></svg>", out _, out _);

        Assert.Equal(circle, ellipse);
    }

    [Fact]
    public void Non_uniform_ellipse_stroke_retains_native_source()
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><g transform='scale(2,1)'><ellipse cx='8' cy='8' rx='6' ry='4' fill='none' stroke='red' stroke-width='2'/></g></svg>");

        using SvgRoot scene = SvgRootParser.Parse(input, SvgRenderLimits.Default, CancellationToken.None);
        Assert.Contains(scene.Geometries, geometry => geometry.AuthoredPath?.StrokeStyle is not null);
        Assert.False(SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None).IsEmpty);
    }

    [Fact]
    public void Dasharray_on_ellipse_strokes_paints_runs()
    {
        byte[] plain = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><ellipse cx='8' cy='8' rx='6' ry='4' fill='red'/></svg>", out _, out _);
        byte[] dashed = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><ellipse cx='8' cy='8' rx='6' ry='4' fill='red' stroke='blue' stroke-width='2' stroke-dasharray='2,2'/></svg>", out _, out _);

        Assert.NotEqual(plain, dashed);
    }

    [Fact]
    public void Circle_sized_dashed_ellipse_matches_dashed_circle()
    {
        byte[] ellipse = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><ellipse cx='8' cy='8' rx='6' ry='6' fill='red' stroke='blue' stroke-width='2' stroke-dasharray='4,2'/></svg>", out _, out _);
        byte[] circle = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><circle cx='8' cy='8' r='6' fill='red' stroke='blue' stroke-width='2' stroke-dasharray='4,2'/></svg>", out _, out _);

        Assert.Equal(circle, ellipse);
    }

    [Fact]
    public void Dashed_ellipse_use_matches_direct()
    {
        byte[] direct = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><ellipse cx='8' cy='8' rx='6' ry='4' fill='red' stroke='blue' stroke-width='2' stroke-dasharray='2,2'/></svg>", out _, out _);
        byte[] instanced = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><defs><ellipse id='e' cx='8' cy='8' rx='6' ry='4' fill='red' stroke='blue' stroke-width='2' stroke-dasharray='2,2'/></defs><use href='#e'/></svg>", out _, out _);

        Assert.Equal(direct, instanced);
    }

    [Fact]
    public void Dashed_ellipse_symbol_matches_direct()
    {
        byte[] direct = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><ellipse cx='8' cy='8' rx='6' ry='4' fill='red' stroke='blue' stroke-width='2' stroke-dasharray='2,2'/></svg>", out _, out _);
        byte[] instanced = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><defs><symbol id='s' viewBox='0 0 16 16'><ellipse cx='8' cy='8' rx='6' ry='4' fill='red' stroke='blue' stroke-width='2' stroke-dasharray='2,2'/></symbol></defs><use href='#s' width='16' height='16'/></svg>", out _, out _);

        Assert.Equal(direct, instanced);
    }

    [Fact]
    public void Offset_dashed_circle_sized_ellipse_matches_dashed_circle()
    {
        byte[] ellipse = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><ellipse cx='8' cy='8' rx='6' ry='6' fill='red' stroke='blue' stroke-width='2' stroke-dasharray='4 2' stroke-dashoffset='2'/></svg>", out _, out _);
        byte[] circle = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><circle cx='8' cy='8' r='6' fill='red' stroke='blue' stroke-width='2' stroke-dasharray='4 2' stroke-dashoffset='2'/></svg>", out _, out _);

        Assert.Equal(circle, ellipse);
    }

    [Fact]
    public void Non_uniform_dashed_ellipse_stroke_retains_native_source()
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><g transform='scale(2,1)'><ellipse cx='8' cy='8' rx='6' ry='4' fill='none' stroke='red' stroke-width='2' stroke-dasharray='2,2'/></g></svg>");

        using SvgRoot scene = SvgRootParser.Parse(input, SvgRenderLimits.Default, CancellationToken.None);
        Assert.Contains(scene.Geometries, geometry => geometry.AuthoredPath?.StrokeStyle is not null);
        Assert.False(SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None).IsEmpty);
    }

    [Fact]
    public void Ellipse_width_without_paint_renders_fill_only()
    {
        byte[] styled = System.Text.Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"16\"><ellipse cx=\"8\" cy=\"8\" rx=\"6\" ry=\"4\" fill=\"red\" stroke-width=\"2\"/></svg>");
        byte[] plain = System.Text.Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"16\"><ellipse cx=\"8\" cy=\"8\" rx=\"6\" ry=\"4\" fill=\"red\"/></svg>");

        ReadOnlyMemory<byte> styledPng = SvgConverter.ConvertToPng(styled, SvgRenderOptions.Default, CancellationToken.None);
        ReadOnlyMemory<byte> plainPng = SvgConverter.ConvertToPng(plain, SvgRenderOptions.Default, CancellationToken.None);

        Assert.True(plainPng.Span.SequenceEqual(styledPng.Span));
    }

    [Fact]
    public void Non_uniform_circle_stroke_retains_native_source()
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><circle cx='8' cy='8' r='6' fill='red' stroke='blue' stroke-width='2' transform='scale(2,1)'/></svg>");

        using SvgRoot scene = SvgRootParser.Parse(input, SvgRenderLimits.Default, CancellationToken.None);
        Assert.Contains(scene.Geometries, geometry => geometry.AuthoredPath?.StrokeStyle is not null);
        Assert.False(SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None).IsEmpty);
    }

    [Fact]
    public void Negative_circle_stroke_width_stays_unsupported()
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><circle cx='8' cy='8' r='6' fill='red' stroke='blue' stroke-width='-2'/></svg>");

        SvgException rejected = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, rejected.ErrorCode);
    }

    [Fact]
    public void Circle_stroke_miterlimit_validates_without_joins()
    {
        byte[] plain = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><circle cx='8' cy='8' r='6' fill='red' stroke='blue' stroke-width='2'/></svg>", out _, out _);
        byte[] mitered = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><circle cx='8' cy='8' r='6' fill='red' stroke='blue' stroke-width='2' stroke-miterlimit='10'/></svg>", out _, out _);

        Assert.Equal(plain, mitered);
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><circle cx='8' cy='8' r='6' fill='red' stroke='blue' stroke-width='2' stroke-miterlimit='0.5'/></svg>");

        SvgException rejected = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, rejected.ErrorCode);
    }

    [Fact]
    public void Fractional_circle_stroke_renders_ring()
    {
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><circle cx='8' cy='8' r='6' fill='red' stroke='blue' stroke-width='2.5'/></svg>", out _, out _);

        Assert.Equal(new SvgColor(0, 0, 255, 255), Pixel(pixels, 16, 8, 2));
        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, 16, 8, 8));
        Assert.NotEqual(0, AlphaSum(pixels));
    }

    [Fact]
    public void Fractional_ellipse_stroke_matches_circle()
    {
        byte[] ellipse = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><ellipse cx='8' cy='8' rx='6' ry='6' fill='red' stroke='blue' stroke-width='2.5'/></svg>", out _, out _);
        byte[] circle = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><circle cx='8' cy='8' r='6' fill='red' stroke='blue' stroke-width='2.5'/></svg>", out _, out _);

        Assert.Equal(circle, ellipse);
    }

    [Fact]
    public void Fractional_rectangle_width_preserves_fill_and_viewport_edge_pixels()
    {
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><rect x='0' y='0' width='8' height='8' fill='red' stroke='blue' stroke-width='2.5'/></svg>", out int width, out int height);
        Assert.Equal(8, width);
        Assert.Equal(8, height);
        Assert.Equal(new SvgColor(0, 0, 255, 255), Pixel(pixels, width, 0, 0));
        Assert.Equal(new SvgColor(191, 0, 64, 255), Pixel(pixels, width, 1, 4));
        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, width, 4, 4));
        Assert.Equal(new SvgColor(0, 0, 255, 255), Pixel(pixels, width, 7, 7));
    }

    [Theory]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><line x1='0' y1='4' x2='8' y2='4' stroke='red' stroke-width='2.5'/></svg>")]
    public void Fractional_line_strokes_retain_the_shared_native_centerline(string svg)
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes(svg);
        using SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, CancellationToken.None);
        SvgGeometry geometry = Assert.Single(root.Geometries);
        SvgAuthoredPath source = Assert.IsType<SvgAuthoredPath>(geometry.AuthoredPath);
        Assert.Equal(2.5, source.StrokeStyle?.Width);
    }

    [Fact]
    public void Concentric_radial_rect_matches_polygon_bands()
    {
        byte[] direct = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><radialGradient id='g' cx='0.5' cy='0.5' r='0.5'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><rect x='0' y='0' width='8' height='8' fill='url(#g)'/></svg>", out _, out _);
        byte[] bands = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><radialGradient id='g' cx='0.5' cy='0.5' r='0.5'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><path d='M0,0 H8 V8 H0 Z' fill='url(#g)'/></svg>", out _, out _);

        Assert.Equal(direct, bands);
    }

    [Fact]
    public void Radial_href_matches_direct()
    {
        byte[] direct = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><radialGradient id='g' cx='0.5' cy='0.5' r='0.5'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><rect x='0' y='0' width='8' height='8' fill='url(#g)'/></svg>", out _, out _);
        byte[] inherited = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><radialGradient id='p' cx='0.5' cy='0.5' r='0.5'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient><radialGradient id='g' href='#p'/></defs><rect x='0' y='0' width='8' height='8' fill='url(#g)'/></svg>", out _, out _);

        Assert.Equal(direct, inherited);
    }

    [Fact]
    public void Single_stop_radial_renders_solid()
    {
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><radialGradient id='g' cx='0.5' cy='0.5' r='0.5'><stop offset='0.3' stop-color='#ff0000'/></radialGradient></defs><rect x='0' y='0' width='8' height='8' fill='url(#g)'/></svg>", out _, out _);

        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, 8, 3, 3));
        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, 8, 0, 0));
    }

    [Fact]
    public void Degenerate_radial_renders_end_stop()
    {
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><radialGradient id='g' cx='0.5' cy='0.5' r='0'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><rect x='0' y='0' width='8' height='8' fill='url(#g)'/></svg>", out _, out _);

        Assert.Equal(new SvgColor(0, 0, 255, 255), Pixel(pixels, 8, 3, 3));
        Assert.Equal(new SvgColor(0, 0, 255, 255), Pixel(pixels, 8, 0, 0));
    }

    [Fact]
    public void Translucent_radial_differs_from_opaque()
    {
        byte[] opaque = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><radialGradient id='g' cx='0.5' cy='0.5' r='0.5'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><rect x='0' y='0' width='8' height='8' fill='url(#g)'/></svg>", out _, out _);
        byte[] faded = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><radialGradient id='g' cx='0.5' cy='0.5' r='0.5'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff' stop-opacity='0'/></radialGradient></defs><rect x='0' y='0' width='8' height='8' fill='url(#g)'/></svg>", out _, out _);

        Assert.NotEqual(opaque, faded);
    }

    [Fact]
    public void Radial_rect_with_clip_renders()
    {
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><radialGradient id='g' cx='0.5' cy='0.5' r='0.5'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient><clipPath id='c'><rect x='0' y='0' width='4' height='8'/></clipPath></defs><rect x='0' y='0' width='8' height='8' fill='url(#g)' clip-path='url(#c)'/></svg>", out _, out _);

        Assert.Equal(new SvgColor(0, 0, 255, 255), Pixel(pixels, 8, 0, 0));
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(pixels, 8, 7, 7));
    }

    [Fact]
    public void Userspace_radial_matches_bounding_box()
    {
        byte[] boxed = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><radialGradient id='g' cx='0.5' cy='0.5' r='0.5'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><rect x='0' y='0' width='8' height='8' fill='url(#g)'/></svg>", out _, out _);
        byte[] userspace = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><radialGradient id='g' gradientUnits='userSpaceOnUse' cx='4' cy='4' r='4'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><rect x='0' y='0' width='8' height='8' fill='url(#g)'/></svg>", out _, out _);

        Assert.Equal(boxed, userspace);
    }

    [Fact]
    public void Scaled_userspace_radial_matches_direct()
    {
        byte[] scaled = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><defs><radialGradient id='g' gradientUnits='userSpaceOnUse' cx='4' cy='4' r='4'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><rect x='0' y='0' width='8' height='8' fill='url(#g)' transform='scale(2)'/></svg>", out _, out _);
        byte[] direct = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><defs><radialGradient id='g' gradientUnits='userSpaceOnUse' cx='8' cy='8' r='8'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><rect x='0' y='0' width='16' height='16' fill='url(#g)'/></svg>", out _, out _);

        Assert.Equal(direct, scaled);
    }

    [Fact]
    public void Reflected_bounding_box_radial_matches_plain()
    {
        byte[] plain = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><defs><radialGradient id='g' cx='0.5' cy='0.5' r='0.5'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><rect x='0' y='0' width='8' height='8' fill='url(#g)'/></svg>", out _, out _);
        byte[] reflected = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><defs><radialGradient id='g' cx='0.5' cy='0.5' r='0.5'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><rect x='0' y='0' width='8' height='8' fill='url(#g)' transform='translate(8,0) scale(-1,1)'/></svg>", out _, out _);

        Assert.Equal(plain, reflected);
    }

    [Fact]
    public void Radial_href_units_match_direct()
    {
        byte[] direct = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><radialGradient id='g' gradientUnits='userSpaceOnUse' cx='4' cy='4' r='4'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><rect x='0' y='0' width='8' height='8' fill='url(#g)'/></svg>", out _, out _);
        byte[] inherited = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><radialGradient id='p' gradientUnits='userSpaceOnUse' cx='4' cy='4' r='4'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient><radialGradient id='g' href='#p'/></defs><rect x='0' y='0' width='8' height='8' fill='url(#g)'/></svg>", out _, out _);

        Assert.Equal(direct, inherited);
    }

    [Fact]
    public void Focal_at_center_matches_concentric()
    {
        byte[] plain = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><radialGradient id='g' cx='0.5' cy='0.5' r='0.5'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><rect x='0' y='0' width='8' height='8' fill='url(#g)'/></svg>", out _, out _);
        byte[] explicitFocal = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><radialGradient id='g' cx='0.5' cy='0.5' r='0.5' fx='0.5' fy='0.5'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><rect x='0' y='0' width='8' height='8' fill='url(#g)'/></svg>", out _, out _);

        Assert.Equal(plain, explicitFocal);
    }

    [Fact]
    public void Offset_focal_differs_from_concentric()
    {
        byte[] concentric = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><radialGradient id='g' cx='0.5' cy='0.5' r='0.5'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><rect x='0' y='0' width='8' height='8' fill='url(#g)'/></svg>", out _, out _);
        byte[] offset = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><radialGradient id='g' cx='0.5' cy='0.5' r='0.5' fx='0.25' fy='0.5'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><rect x='0' y='0' width='8' height='8' fill='url(#g)'/></svg>", out _, out _);

        Assert.NotEqual(concentric, offset);
    }

    [Fact]
    public void Offset_focal_rect_matches_polygon_bands()
    {
        byte[] direct = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><radialGradient id='g' cx='0.5' cy='0.5' r='0.5' fx='0.25' fy='0.5'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><rect x='0' y='0' width='8' height='8' fill='url(#g)'/></svg>", out _, out _);
        byte[] bands = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><radialGradient id='g' cx='0.5' cy='0.5' r='0.5' fx='0.25' fy='0.5'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><path d='M0,0 H8 V8 H0 Z' fill='url(#g)'/></svg>", out _, out _);

        Assert.Equal(direct, bands);
    }

    [Fact]
    public void Scaled_userspace_focal_matches_direct()
    {
        byte[] scaled = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><defs><radialGradient id='g' gradientUnits='userSpaceOnUse' cx='4' cy='4' r='4' fx='2' fy='4'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><rect x='0' y='0' width='8' height='8' fill='url(#g)' transform='scale(2)'/></svg>", out _, out _);
        byte[] direct = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><defs><radialGradient id='g' gradientUnits='userSpaceOnUse' cx='8' cy='8' r='8' fx='4' fy='8'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><rect x='0' y='0' width='16' height='16' fill='url(#g)'/></svg>", out _, out _);

        Assert.Equal(direct, scaled);
    }

    [Fact]
    public void Radial_focal_href_matches_direct()
    {
        byte[] direct = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><radialGradient id='g' cx='0.5' cy='0.5' r='0.5' fx='0.25' fy='0.5'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><rect x='0' y='0' width='8' height='8' fill='url(#g)'/></svg>", out _, out _);
        byte[] inherited = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><radialGradient id='p' cx='0.5' cy='0.5' r='0.5' fx='0.25' fy='0.5'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient><radialGradient id='g' href='#p'/></defs><rect x='0' y='0' width='8' height='8' fill='url(#g)'/></svg>", out _, out _);

        Assert.Equal(direct, inherited);
    }

    [Fact]
    public void Focal_pixel_renders_start_stop()
    {
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><radialGradient id='g' cx='0.5' cy='0.5' r='0.5' fx='0.4375' fy='0.5625'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><rect x='0' y='0' width='8' height='8' fill='url(#g)'/></svg>", out _, out _);

        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, 8, 3, 4));
    }
    [Fact]
    public void Repeat_radial_differs_from_pad()
    {
        byte[] pad = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><radialGradient id='g' cx='0.5' cy='0.5' r='0.5'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><rect x='0' y='0' width='8' height='8' fill='url(#g)'/></svg>", out _, out _);
        byte[] repeat = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><radialGradient id='g' cx='0.5' cy='0.5' r='0.5' spreadMethod='repeat'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><rect x='0' y='0' width='8' height='8' fill='url(#g)'/></svg>", out _, out _);

        Assert.NotEqual(pad, repeat);
    }

    [Fact]
    public void Reflect_radial_differs_from_pad()
    {
        byte[] pad = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><radialGradient id='g' cx='0.5' cy='0.5' r='0.5'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><rect x='0' y='0' width='8' height='8' fill='url(#g)'/></svg>", out _, out _);
        byte[] reflect = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><radialGradient id='g' cx='0.5' cy='0.5' r='0.5' spreadMethod='reflect'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><rect x='0' y='0' width='8' height='8' fill='url(#g)'/></svg>", out _, out _);

        Assert.NotEqual(pad, reflect);
    }

    [Fact]
    public void Repeat_radial_differs_from_reflect()
    {
        byte[] repeat = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><radialGradient id='g' cx='0.5' cy='0.5' r='0.5' spreadMethod='repeat'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><rect x='0' y='0' width='8' height='8' fill='url(#g)'/></svg>", out _, out _);
        byte[] reflect = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><radialGradient id='g' cx='0.5' cy='0.5' r='0.5' spreadMethod='reflect'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><rect x='0' y='0' width='8' height='8' fill='url(#g)'/></svg>", out _, out _);

        Assert.NotEqual(repeat, reflect);
    }

    [Fact]
    public void Repeat_radial_rect_matches_polygon_bands()
    {
        byte[] direct = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><radialGradient id='g' cx='0.5' cy='0.5' r='0.5' spreadMethod='repeat'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><rect x='0' y='0' width='8' height='8' fill='url(#g)'/></svg>", out _, out _);
        byte[] bands = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><radialGradient id='g' cx='0.5' cy='0.5' r='0.5' spreadMethod='repeat'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><path d='M0,0 H8 V8 H0 Z' fill='url(#g)'/></svg>", out _, out _);

        Assert.Equal(direct, bands);
    }

    [Fact]
    public void Radial_spread_href_matches_direct()
    {
        byte[] direct = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><radialGradient id='g' cx='0.5' cy='0.5' r='0.5' spreadMethod='repeat'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><rect x='0' y='0' width='8' height='8' fill='url(#g)'/></svg>", out _, out _);
        byte[] inherited = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><radialGradient id='p' cx='0.5' cy='0.5' r='0.5' spreadMethod='repeat'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient><radialGradient id='g' href='#p'/></defs><rect x='0' y='0' width='8' height='8' fill='url(#g)'/></svg>", out _, out _);

        Assert.Equal(direct, inherited);
    }

    [Fact]
    public void Repeat_wraps_to_start_at_circle_edge()
    {
        byte[] repeat = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><defs><radialGradient id='g' gradientUnits='userSpaceOnUse' cx='8.5' cy='8.5' r='2' spreadMethod='repeat'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><rect x='0' y='0' width='16' height='16' fill='url(#g)'/></svg>", out _, out _);
        byte[] pad = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><defs><radialGradient id='g' gradientUnits='userSpaceOnUse' cx='8.5' cy='8.5' r='2'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><rect x='0' y='0' width='16' height='16' fill='url(#g)'/></svg>", out _, out _);

        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(repeat, 16, 10, 8));
        Assert.Equal(new SvgColor(0, 0, 255, 255), Pixel(pad, 16, 10, 8));
    }

    [Fact]
    public void Repeat_returns_to_start_two_radii_out()
    {
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><defs><radialGradient id='g' gradientUnits='userSpaceOnUse' cx='8.5' cy='8.5' r='2' spreadMethod='repeat'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><rect x='0' y='0' width='16' height='16' fill='url(#g)'/></svg>", out _, out _);

        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, 16, 12, 8));
    }

    [Fact]
    public void Reflect_returns_to_start_two_radii_out()
    {
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><defs><radialGradient id='g' gradientUnits='userSpaceOnUse' cx='8.5' cy='8.5' r='2' spreadMethod='reflect'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><rect x='0' y='0' width='16' height='16' fill='url(#g)'/></svg>", out _, out _);

        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, 16, 12, 8));
    }
    [Fact]
    public void Translated_radial_matches_explicit()
    {
        byte[] transformed = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><radialGradient id='g' gradientTransform='translate(0.25, 0)' cx='0.25' cy='0.5' r='0.5'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><rect x='0' y='0' width='8' height='8' fill='url(#g)'/></svg>", out _, out _);
        byte[] explicitCircle = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><radialGradient id='g' cx='0.5' cy='0.5' r='0.5'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><rect x='0' y='0' width='8' height='8' fill='url(#g)'/></svg>", out _, out _);

        Assert.Equal(explicitCircle, transformed);
    }

    [Fact]
    public void Scaled_radial_matches_explicit()
    {
        byte[] transformed = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><radialGradient id='g' gradientTransform='scale(2)' cx='0.25' cy='0.25' r='0.25'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><rect x='0' y='0' width='8' height='8' fill='url(#g)'/></svg>", out _, out _);
        byte[] explicitCircle = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><radialGradient id='g' cx='0.5' cy='0.5' r='0.5'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><rect x='0' y='0' width='8' height='8' fill='url(#g)'/></svg>", out _, out _);

        Assert.Equal(explicitCircle, transformed);
    }

    [Fact]
    public void Transformed_radial_rect_matches_polygon_bands()
    {
        byte[] direct = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><radialGradient id='g' gradientTransform='translate(0.25, 0)' cx='0.25' cy='0.5' r='0.5'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><rect x='0' y='0' width='8' height='8' fill='url(#g)'/></svg>", out _, out _);
        byte[] bands = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><radialGradient id='g' gradientTransform='translate(0.25, 0)' cx='0.25' cy='0.5' r='0.5'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><path d='M0,0 H8 V8 H0 Z' fill='url(#g)'/></svg>", out _, out _);

        Assert.Equal(direct, bands);
    }

    [Fact]
    public void Transformed_radial_href_matches_direct()
    {
        byte[] direct = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><radialGradient id='g' gradientTransform='translate(0.25, 0)' cx='0.25' cy='0.5' r='0.5'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><rect x='0' y='0' width='8' height='8' fill='url(#g)'/></svg>", out _, out _);
        byte[] inherited = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><radialGradient id='p' cx='0.75' cy='0.5' r='0.5'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient><radialGradient id='g' href='#p' gradientTransform='translate(-0.25, 0)'/></defs><rect x='0' y='0' width='8' height='8' fill='url(#g)'/></svg>", out _, out _);

        Assert.Equal(direct, inherited);
    }
    [Fact]
    public void Focal_circle_differs_from_point_focal()
    {
        byte[] point = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><radialGradient id='g' cx='0.5' cy='0.5' r='0.5'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><rect x='0' y='0' width='8' height='8' fill='url(#g)'/></svg>", out _, out _);
        byte[] circle = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><radialGradient id='g' cx='0.5' cy='0.5' r='0.5' fr='0.1'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><rect x='0' y='0' width='8' height='8' fill='url(#g)'/></svg>", out _, out _);

        Assert.NotEqual(point, circle);
    }

    [Fact]
    public void Focal_circle_rect_matches_polygon_bands()
    {
        byte[] direct = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><radialGradient id='g' cx='0.5' cy='0.5' r='0.5' fr='0.1'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><rect x='0' y='0' width='8' height='8' fill='url(#g)'/></svg>", out _, out _);
        byte[] bands = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><radialGradient id='g' cx='0.5' cy='0.5' r='0.5' fr='0.1'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><path d='M0,0 H8 V8 H0 Z' fill='url(#g)'/></svg>", out _, out _);

        Assert.Equal(direct, bands);
    }

    [Fact]
    public void Offset_focal_circle_rect_matches_polygon_bands()
    {
        byte[] direct = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><radialGradient id='g' cx='0.5' cy='0.5' r='0.5' fx='0.3' fy='0.5' fr='0.1'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><rect x='0' y='0' width='8' height='8' fill='url(#g)'/></svg>", out _, out _);
        byte[] bands = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><radialGradient id='g' cx='0.5' cy='0.5' r='0.5' fx='0.3' fy='0.5' fr='0.1'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><path d='M0,0 H8 V8 H0 Z' fill='url(#g)'/></svg>", out _, out _);

        Assert.Equal(direct, bands);
    }

    [Fact]
    public void Focal_circle_href_matches_direct()
    {
        byte[] direct = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><radialGradient id='g' cx='0.5' cy='0.5' r='0.5' fr='0.1'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><rect x='0' y='0' width='8' height='8' fill='url(#g)'/></svg>", out _, out _);
        byte[] inherited = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><radialGradient id='p' cx='0.5' cy='0.5' r='0.5' fr='0.1'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient><radialGradient id='g' href='#p'/></defs><rect x='0' y='0' width='8' height='8' fill='url(#g)'/></svg>", out _, out _);

        Assert.Equal(direct, inherited);
    }

    [Fact]
    public void Focal_circle_edge_renders_start_stop()
    {
        byte[] circle = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><defs><radialGradient id='g' gradientUnits='userSpaceOnUse' cx='8.5' cy='8.5' r='4' fr='2'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><rect x='0' y='0' width='16' height='16' fill='url(#g)'/></svg>", out _, out _);
        byte[] point = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><defs><radialGradient id='g' gradientUnits='userSpaceOnUse' cx='8.5' cy='8.5' r='4'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><rect x='0' y='0' width='16' height='16' fill='url(#g)'/></svg>", out _, out _);

        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(circle, 16, 10, 8));
        Assert.NotEqual(circle, point);
    }
    [Fact]
    public void Radial_rect_stroke_differs_from_unstroked()
    {
        byte[] plain = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><radialGradient id='g' cx='0.5' cy='0.5' r='0.5'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><rect x='0' y='0' width='8' height='8' fill='url(#g)'/></svg>", out _, out _);
        byte[] stroked = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><radialGradient id='g' cx='0.5' cy='0.5' r='0.5'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><rect x='0' y='0' width='8' height='8' fill='red' stroke='url(#g)' stroke-width='2'/></svg>", out _, out _);

        Assert.NotEqual(plain, stroked);
    }

    [Fact]
    public void Userspace_radial_line_stroke_matches_filled_band()
    {
        byte[] stroke = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><radialGradient id='g' gradientUnits='userSpaceOnUse' cx='4' cy='4' r='4'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><line x1='0' y1='4' x2='8' y2='4' stroke='url(#g)' stroke-width='2'/></svg>", out _, out _);
        byte[] band = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><radialGradient id='g' gradientUnits='userSpaceOnUse' cx='4' cy='4' r='4'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><rect x='0' y='3' width='8' height='2' fill='url(#g)'/></svg>", out _, out _);

        Assert.Equal(band, stroke);
    }

    [Fact]
    public void Radial_rect_stroke_href_matches_direct()
    {
        byte[] direct = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><radialGradient id='g' cx='0.5' cy='0.5' r='0.5'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><rect x='0' y='0' width='8' height='8' fill='red' stroke='url(#g)' stroke-width='2'/></svg>", out _, out _);
        byte[] inherited = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><radialGradient id='p' cx='0.5' cy='0.5' r='0.5'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient><radialGradient id='g' href='#p'/></defs><rect x='0' y='0' width='8' height='8' fill='red' stroke='url(#g)' stroke-width='2'/></svg>", out _, out _);

        Assert.Equal(direct, inherited);
    }
    [Fact]
    public void Transformed_circle_moves_center()
    {
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'><g transform='translate(8,8)'><circle cx='8' cy='8' r='8' fill='red'/></g></svg>", out _, out _);

        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, 32, 16, 16));
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(pixels, 32, 8, 8));
    }

    [Fact]
    public void Clip_path_intersects_circle_bounds()
    {
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='64' height='64'><defs><clipPath id='c'><rect x='32' y='0' width='32' height='64'/></clipPath></defs><circle cx='32' cy='32' r='20' fill='red' clip-path='url(#c)'/></svg>", out _, out _);

        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, 64, 48, 32));
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(pixels, 64, 16, 32));
    }

    [Fact]
    public void Even_odd_on_circle_matches_default()
    {
        byte[] plain = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='64' height='64'><circle cx='32' cy='32' r='20' fill='red'/></svg>", out _, out _);
        byte[] evenOdd = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='64' height='64'><circle cx='32' cy='32' r='20' fill='red' fill-rule='evenodd'/></svg>", out _, out _);

        Assert.Equal(plain, evenOdd);
    }

    [Fact]
    public void Rounded_rectangle_in_definitions_renders_through_use()
    {
        byte[] instanced = System.Text.Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"64\" height=\"64\"><defs><rect id=\"r\" x=\"8\" y=\"8\" width=\"48\" height=\"48\" rx=\"12\" fill=\"red\"/></defs><use href=\"#r\"/></svg>");
        byte[] baseline = System.Text.Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"64\" height=\"64\"><rect x=\"8\" y=\"8\" width=\"48\" height=\"48\" rx=\"12\" fill=\"red\"/></svg>");

        ReadOnlyMemory<byte> instancedPng = SvgConverter.ConvertToPng(instanced, SvgRenderOptions.Default, CancellationToken.None);
        ReadOnlyMemory<byte> baselinePng = SvgConverter.ConvertToPng(baseline, SvgRenderOptions.Default, CancellationToken.None);

        Assert.True(instancedPng.Span.SequenceEqual(baselinePng.Span));
    }

    [Fact]
    public void Paint_strokes_on_ellipse_definitions_render_through_use()
    {
        byte[] instanced = System.Text.Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"64\" height=\"64\"><defs><linearGradient id=\"g\"><stop offset=\"0\" stop-color=\"#ff0000\"/><stop offset=\"1\" stop-color=\"#0000ff\"/></linearGradient></defs><defs><ellipse id=\"e\" cx=\"32\" cy=\"32\" rx=\"20\" ry=\"12\" fill=\"red\" stroke=\"url(#g)\" stroke-width=\"8\"/></defs><use href=\"#e\"/></svg>");
        byte[] baseline = System.Text.Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"64\" height=\"64\"><defs><linearGradient id=\"g\"><stop offset=\"0\" stop-color=\"#ff0000\"/><stop offset=\"1\" stop-color=\"#0000ff\"/></linearGradient></defs><ellipse cx=\"32\" cy=\"32\" rx=\"20\" ry=\"12\" fill=\"red\" stroke=\"url(#g)\" stroke-width=\"8\"/></svg>");

        ReadOnlyMemory<byte> instancedPng = SvgConverter.ConvertToPng(instanced, SvgRenderOptions.Default, CancellationToken.None);
        ReadOnlyMemory<byte> baselinePng = SvgConverter.ConvertToPng(baseline, SvgRenderOptions.Default, CancellationToken.None);

        Assert.True(instancedPng.Span.SequenceEqual(baselinePng.Span));
    }

    [Fact]
    public void Stroked_ellipses_in_symbols_render_through_use()
    {
        byte[] instanced = System.Text.Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"64\" height=\"64\"><defs><symbol id=\"s\" viewBox=\"0 0 64 64\"><ellipse cx=\"32\" cy=\"32\" rx=\"20\" ry=\"12\" fill=\"red\" stroke=\"blue\" stroke-width=\"8\"/></symbol></defs><use href=\"#s\" width=\"64\" height=\"64\"/></svg>");
        byte[] baseline = System.Text.Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"64\" height=\"64\"><ellipse cx=\"32\" cy=\"32\" rx=\"20\" ry=\"12\" fill=\"red\" stroke=\"blue\" stroke-width=\"8\"/></svg>");

        ReadOnlyMemory<byte> instancedPng = SvgConverter.ConvertToPng(instanced, SvgRenderOptions.Default, CancellationToken.None);
        ReadOnlyMemory<byte> baselinePng = SvgConverter.ConvertToPng(baseline, SvgRenderOptions.Default, CancellationToken.None);

        Assert.True(instancedPng.Span.SequenceEqual(baselinePng.Span));
    }

    [Fact]
    public void Paint_strokes_on_circle_definitions_render_through_use()
    {
        byte[] instanced = System.Text.Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"64\" height=\"64\"><defs><linearGradient id=\"g\"><stop offset=\"0\" stop-color=\"#ff0000\"/><stop offset=\"1\" stop-color=\"#0000ff\"/></linearGradient></defs><defs><circle id=\"c\" cx=\"32\" cy=\"32\" r=\"20\" fill=\"red\" stroke=\"url(#g)\" stroke-width=\"8\"/></defs><use href=\"#c\"/></svg>");
        byte[] baseline = System.Text.Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"64\" height=\"64\"><defs><linearGradient id=\"g\"><stop offset=\"0\" stop-color=\"#ff0000\"/><stop offset=\"1\" stop-color=\"#0000ff\"/></linearGradient></defs><circle cx=\"32\" cy=\"32\" r=\"20\" fill=\"red\" stroke=\"url(#g)\" stroke-width=\"8\"/></svg>");

        ReadOnlyMemory<byte> instancedPng = SvgConverter.ConvertToPng(instanced, SvgRenderOptions.Default, CancellationToken.None);
        ReadOnlyMemory<byte> baselinePng = SvgConverter.ConvertToPng(baseline, SvgRenderOptions.Default, CancellationToken.None);

        Assert.True(instancedPng.Span.SequenceEqual(baselinePng.Span));
    }

    [Fact]
    public void Stroked_circles_in_symbols_render_through_use()
    {
        byte[] instanced = System.Text.Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"64\" height=\"64\"><defs><symbol id=\"s\" viewBox=\"0 0 64 64\"><circle cx=\"32\" cy=\"32\" r=\"20\" fill=\"red\" stroke=\"blue\" stroke-width=\"8\"/></symbol></defs><use href=\"#s\" width=\"64\" height=\"64\"/></svg>");
        byte[] baseline = System.Text.Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"64\" height=\"64\"><circle cx=\"32\" cy=\"32\" r=\"20\" fill=\"red\" stroke=\"blue\" stroke-width=\"8\"/></svg>");

        ReadOnlyMemory<byte> instancedPng = SvgConverter.ConvertToPng(instanced, SvgRenderOptions.Default, CancellationToken.None);
        ReadOnlyMemory<byte> baselinePng = SvgConverter.ConvertToPng(baseline, SvgRenderOptions.Default, CancellationToken.None);

        Assert.True(instancedPng.Span.SequenceEqual(baselinePng.Span));
    }

    [Fact]
    public void Paint_strokes_on_rounded_definitions_render_through_use()
    {
        byte[] instanced = System.Text.Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"64\" height=\"64\"><defs><linearGradient id=\"g\"><stop offset=\"0\" stop-color=\"#ff0000\"/><stop offset=\"1\" stop-color=\"#0000ff\"/></linearGradient></defs><defs><rect id=\"r\" x=\"8\" y=\"8\" width=\"48\" height=\"48\" rx=\"12\" fill=\"red\" stroke=\"url(#g)\" stroke-width=\"8\"/></defs><use href=\"#r\"/></svg>");
        byte[] baseline = System.Text.Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"64\" height=\"64\"><defs><linearGradient id=\"g\"><stop offset=\"0\" stop-color=\"#ff0000\"/><stop offset=\"1\" stop-color=\"#0000ff\"/></linearGradient></defs><rect x=\"8\" y=\"8\" width=\"48\" height=\"48\" rx=\"12\" fill=\"red\" stroke=\"url(#g)\" stroke-width=\"8\"/></svg>");

        ReadOnlyMemory<byte> instancedPng = SvgConverter.ConvertToPng(instanced, SvgRenderOptions.Default, CancellationToken.None);
        ReadOnlyMemory<byte> baselinePng = SvgConverter.ConvertToPng(baseline, SvgRenderOptions.Default, CancellationToken.None);

        Assert.True(instancedPng.Span.SequenceEqual(baselinePng.Span));
    }

    [Fact]
    public void Rounded_rectangles_in_symbols_render_through_use()
    {
        byte[] instanced = System.Text.Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"64\" height=\"64\"><defs><symbol id=\"s\" viewBox=\"0 0 64 64\"><rect x=\"8\" y=\"8\" width=\"48\" height=\"48\" rx=\"12\" fill=\"red\" stroke=\"blue\" stroke-width=\"8\"/></symbol></defs><use href=\"#s\" width=\"64\" height=\"64\"/></svg>");
        byte[] baseline = System.Text.Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"64\" height=\"64\"><rect x=\"8\" y=\"8\" width=\"48\" height=\"48\" rx=\"12\" fill=\"red\" stroke=\"blue\" stroke-width=\"8\"/></svg>");

        ReadOnlyMemory<byte> instancedPng = SvgConverter.ConvertToPng(instanced, SvgRenderOptions.Default, CancellationToken.None);
        ReadOnlyMemory<byte> baselinePng = SvgConverter.ConvertToPng(baseline, SvgRenderOptions.Default, CancellationToken.None);

        Assert.True(instancedPng.Span.SequenceEqual(baselinePng.Span));
    }

    [Fact]
    public void Paint_strokes_on_rounded_symbols_render_through_use()
    {
        byte[] instanced = System.Text.Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"64\" height=\"64\"><defs><linearGradient id=\"g\"><stop offset=\"0\" stop-color=\"#ff0000\"/><stop offset=\"1\" stop-color=\"#0000ff\"/></linearGradient></defs><defs><symbol id=\"s\" viewBox=\"0 0 64 64\"><rect x=\"8\" y=\"8\" width=\"48\" height=\"48\" rx=\"12\" fill=\"red\" stroke=\"url(#g)\" stroke-width=\"8\"/></symbol></defs><use href=\"#s\" width=\"64\" height=\"64\"/></svg>");
        byte[] baseline = System.Text.Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"64\" height=\"64\"><defs><linearGradient id=\"g\"><stop offset=\"0\" stop-color=\"#ff0000\"/><stop offset=\"1\" stop-color=\"#0000ff\"/></linearGradient></defs><rect x=\"8\" y=\"8\" width=\"48\" height=\"48\" rx=\"12\" fill=\"red\" stroke=\"url(#g)\" stroke-width=\"8\"/></svg>");

        ReadOnlyMemory<byte> instancedPng = SvgConverter.ConvertToPng(instanced, SvgRenderOptions.Default, CancellationToken.None);
        ReadOnlyMemory<byte> baselinePng = SvgConverter.ConvertToPng(baseline, SvgRenderOptions.Default, CancellationToken.None);

        Assert.True(instancedPng.Span.SequenceEqual(baselinePng.Span));
    }

    [Fact]
    public void Retains_translucent_paint_strokes_on_rounded_definitions()
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"64\" height=\"64\"><defs><linearGradient id=\"g\"><stop offset=\"0\" stop-color=\"#ff0000\"/><stop offset=\"1\" stop-color=\"#0000ff\"/></linearGradient></defs><defs><rect id=\"r\" x=\"8\" y=\"8\" width=\"48\" height=\"48\" rx=\"12\" fill=\"red\" stroke=\"url(#g)\" stroke-width=\"8\" opacity=\"0.5\"/></defs><use href=\"#r\"/></svg>");

        ReadOnlyMemory<byte> png = SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None);

        Assert.Equal(new byte[] { 0, 0, 0, 64, 0, 0, 0, 64 }, png.Span.Slice(16, 8).ToArray());
    }

    [Fact]
    public void Stroked_rounded_rectangle_in_definitions_renders_through_use()
    {
        byte[] instanced = System.Text.Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"64\" height=\"64\"><defs><rect id=\"r\" x=\"8\" y=\"8\" width=\"48\" height=\"48\" rx=\"12\" fill=\"red\" stroke=\"blue\" stroke-width=\"8\"/></defs><use href=\"#r\"/></svg>");
        byte[] baseline = System.Text.Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"64\" height=\"64\"><rect x=\"8\" y=\"8\" width=\"48\" height=\"48\" rx=\"12\" fill=\"red\" stroke=\"blue\" stroke-width=\"8\"/></svg>");

        ReadOnlyMemory<byte> instancedPng = SvgConverter.ConvertToPng(instanced, SvgRenderOptions.Default, CancellationToken.None);
        ReadOnlyMemory<byte> baselinePng = SvgConverter.ConvertToPng(baseline, SvgRenderOptions.Default, CancellationToken.None);

        Assert.True(instancedPng.Span.SequenceEqual(baselinePng.Span));
    }

    [Fact]
    public void Rejects_dashed_rounded_rectangles_in_definitions()
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"64\" height=\"64\"><defs><rect id=\"r\" x=\"8\" y=\"8\" width=\"48\" height=\"48\" rx=\"12\" fill=\"red\" stroke=\"blue\" stroke-width=\"8\" stroke-dasharray=\"2 2\"/></defs><use href=\"#r\"/></svg>");

        SvgException rejected = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, rejected.ErrorCode);
    }

    [Fact]
    public void Circle_content_in_clip_path_preserves_disc_coverage()
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><clipPath id='c'><circle cx='4' cy='4' r='2'/></clipPath></defs><rect width='8' height='8' fill='red' clip-path='url(#c)'/></svg>");

        byte[] pixels = RenderPixels(System.Text.Encoding.UTF8.GetString(input), out int width, out _);
        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, width, 4, 4));
        Assert.Equal(SvgColor.Transparent, Pixel(pixels, width, 0, 0));
    }


    [Fact]
    public void Gray_masked_group_matches_half_opacity_group()
    {
        byte[] masked = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><mask id='m'><rect x='0' y='0' width='8' height='8' fill='#808080'/></mask></defs><g mask='url(#m)'><polygon points='0,0 8,0 0,8' fill='red'/></g></svg>", out _, out _);
        byte[] faded = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><g opacity='0.5'><polygon points='0,0 8,0 0,8' fill='red'/></g></svg>", out _, out _);

        Assert.Equal(faded, masked);
    }

    [Fact]
    public void Object_bounding_box_clip_matches_explicit_clip_box()
    {
        byte[] fractional = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><clipPath id='c' clipPathUnits='objectBoundingBox'><rect x='0.25' y='0.25' width='0.5' height='0.5'/></clipPath></defs><polygon points='0,0 8,0 8,4 4,4 4,8 0,8' fill='red' clip-path='url(#c)'/></svg>", out _, out _);
        byte[] explicitBox = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><clipPath id='c'><rect x='2' y='2' width='4' height='4'/></clipPath></defs><polygon points='0,0 8,0 8,4 4,4 4,8 0,8' fill='red' clip-path='url(#c)'/></svg>", out _, out _);

        Assert.Equal(explicitBox, fractional);
    }

    [Fact]
    public void Translucent_gradient_fade_matches_calibrated_profile()
    {
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='64' height='16'><defs><linearGradient id='g'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff' stop-opacity='0'/></linearGradient></defs><rect x='0' y='0' width='64' height='16' fill='url(#g)'/></svg>", out _, out _);

        SvgColor opaqueEnd = Pixel(pixels, 64, 0, 8);
        Assert.InRange(opaqueEnd.Alpha, 250, 255);
        Assert.True(opaqueEnd.Red > 240, "opaque end stays red");
        Assert.True(opaqueEnd.Blue < 16, "opaque end stays red");
        SvgColor transparentEnd = Pixel(pixels, 64, 63, 8);
        Assert.True(transparentEnd.Alpha <= 8, "transparent end fades out");
        Assert.True(transparentEnd.Blue > 200, "transparent end stays blue");
        SvgColor midpoint = Pixel(pixels, 64, 32, 8);
        Assert.InRange(midpoint.Alpha, 120, 135);
        Assert.InRange(midpoint.Red, 115, 140);
        Assert.InRange(midpoint.Blue, 120, 140);
    }

    [Fact]
    public void Stop_opacity_retains_precision_while_rgba_encodes_color_alpha()
    {
        byte[] faded = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><linearGradient id='g'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff' stop-opacity='0.5'/></linearGradient></defs><rect width='8' height='8' fill='url(#g)'/></svg>", out _, out _);
        byte[] rgba = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><linearGradient id='h'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='rgba(0, 0, 255, 0.5)'/></linearGradient></defs><rect width='8' height='8' fill='url(#h)'/></svg>", out _, out _);

        // Independent pinned-browser captures distinguish authored .5 opacity
        // from the 128/255 alpha encoded in a CSS rgba() color.
        Assert.Equal(new SvgColor(111, 0, 144, 183), Pixel(faded, 8, 4, 0));
        Assert.Equal(new SvgColor(111, 0, 143, 184), Pixel(rgba, 8, 4, 0));
    }

    [Fact]
    public void Duplicate_gradient_stops_render_a_hard_edge()
    {
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><linearGradient id='g'><stop offset='0' stop-color='#ff0000'/><stop offset='0.5' stop-color='#ff0000'/><stop offset='0.5' stop-color='#0000ff'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><rect width='8' height='8' fill='url(#g)'/></svg>", out _, out _);

        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, 8, 0, 0));
        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, 8, 3, 7));
        Assert.Equal(new SvgColor(0, 0, 255, 255), Pixel(pixels, 8, 4, 0));
        Assert.Equal(new SvgColor(0, 0, 255, 255), Pixel(pixels, 8, 7, 7));
    }

    [Fact]
    public void Pattern_stroke_composes_over_gradient_fill()
    {
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><defs><linearGradient id='f' x1='0' y1='0' x2='1' y2='0'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient><pattern id='s' width='4' height='4' patternUnits='userSpaceOnUse'><rect width='4' height='2' fill='#00ff00'/></pattern></defs><path d='M2,2 H14 V14 H2 Z' fill='url(#f)' stroke='url(#s)' stroke-width='2'/></svg>", out int width, out int height);

        Assert.Equal(16, width);
        Assert.Equal(16, height);
        Assert.Equal(new SvgColor(0, 255, 0, 255), Pixel(pixels, width, 8, 1));
        Assert.Equal(new SvgColor(116, 0, 138, 255), Pixel(pixels, width, 8, 8));
        Assert.Equal(new SvgColor(0, 255, 0, 255), Pixel(pixels, width, 2, 8));
    }

    [Fact]
    public void Pattern_stroke_outlines_curved_gradient_path()
    {
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><defs><linearGradient id='f' x1='0' y1='0' x2='1' y2='0'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient><pattern id='s' width='4' height='4' patternUnits='userSpaceOnUse'><rect width='4' height='2' fill='#00ff00'/></pattern></defs><path d='M2,12 Q8,2 14,12 Z' fill='url(#f)' stroke='url(#s)' stroke-width='2'/></svg>", out _, out _);

        Assert.Equal(new SvgColor(0, 255, 0, 255), Pixel(pixels, 16, 2, 12));
        Assert.Equal(new SvgColor(0, 255, 0, 255), Pixel(pixels, 16, 14, 12));
        Assert.Equal(new SvgColor(117, 0, 138, 255), Pixel(pixels, 16, 8, 11));
    }

    [Fact]
    public void Pattern_stroke_supports_dashes_and_polylines()
    {
        byte[] dashed = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><defs><linearGradient id='f' x1='0' y1='0' x2='1' y2='0'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient><pattern id='s' width='4' height='4' patternUnits='userSpaceOnUse'><rect width='4' height='2' fill='#00ff00'/></pattern></defs><path d='M2,2 H14 V14 H2 Z' fill='url(#f)' stroke='url(#s)' stroke-width='2' stroke-dasharray='3 2'/></svg>", out _, out _);
        byte[] poly = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><defs><linearGradient id='f' x1='0' y1='0' x2='1' y2='0'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient><pattern id='s' width='4' height='4' patternUnits='userSpaceOnUse'><rect width='4' height='2' fill='#00ff00'/></pattern></defs><polyline points='2,2 14,2 14,14' fill='none' stroke='url(#s)' stroke-width='2'/></svg>", out _, out _);

        Assert.Equal(new SvgColor(0, 255, 0, 255), Pixel(dashed, 16, 8, 1));
        Assert.Equal(new SvgColor(0, 255, 0, 255), Pixel(poly, 16, 8, 1));
        Assert.True(AlphaSum(poly) > 0);
    }

    [Fact]
    public void Pattern_strokes_over_non_linear_fills_stay_unsupported()
    {
        string radialFill = "<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><defs><radialGradient id='f'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient><pattern id='s' width='4' height='4' patternUnits='userSpaceOnUse'><rect width='4' height='2' fill='#00ff00'/></pattern></defs><path d='M2,2 H14 V14 H2 Z' fill='url(#f)' stroke='url(#s)' stroke-width='2'/></svg>";
        string patternFill = "<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><defs><pattern id='f' width='4' height='4' patternUnits='userSpaceOnUse'><rect width='4' height='2' fill='#ff0000'/></pattern><pattern id='s' width='4' height='4' patternUnits='userSpaceOnUse'><rect width='4' height='2' fill='#00ff00'/></pattern></defs><path d='M2,2 H14 V14 H2 Z' fill='url(#f)' stroke='url(#s)' stroke-width='2'/></svg>";

        foreach (string svg in new[] { radialFill, patternFill })
        {
            SvgException rejected = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(System.Text.Encoding.UTF8.GetBytes(svg), SvgRenderOptions.Default, CancellationToken.None));

            Assert.Equal(SvgErrorCode.UnsupportedFeature, rejected.ErrorCode);
        }
    }

    private static byte[] RenderPixels(string svg, out int width, out int height)
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes(svg);
        ReadOnlyMemory<byte> png = SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None);
        return DecodeRgba(png, out width, out height);
    }

    private static byte[] DecodeRgba(ReadOnlyMemory<byte> png, out int width, out int height)
    {
        width = BinaryPrimitives.ReadInt32BigEndian(png.Span.Slice(16, 4));
        height = BinaryPrimitives.ReadInt32BigEndian(png.Span.Slice(20, 4));
        using MemoryStream compressed = new();
        for (int offset = 8; offset < png.Length;)
        {
            int length = BinaryPrimitives.ReadInt32BigEndian(png.Span.Slice(offset, 4));
            if (png.Span.Slice(offset + 4, 4).SequenceEqual("IDAT"u8)) compressed.Write(png.Span.Slice(offset + 8, length));
            offset += length + 12;
        }

        compressed.Position = 0;
        using ZLibStream zlib = new(compressed, CompressionMode.Decompress);
        byte[] raw = new byte[checked((width * 4 + 1) * height)];
        zlib.ReadExactly(raw);
        byte[] pixels = new byte[checked(width * 4 * height)];
        for (int y = 0; y < height; y++)
        {
            if (raw[y * (width * 4 + 1)] != 0) throw new InvalidDataException("Test PNG decoder expects an unfiltered scanline.");
            raw.AsSpan(y * (width * 4 + 1) + 1, width * 4).CopyTo(pixels.AsSpan(y * width * 4, width * 4));
        }

        return pixels;
    }

    private static SvgColor Pixel(byte[] pixels, int width, int x, int y)
    {
        int offset = (y * width + x) * 4;
        return new SvgColor(pixels[offset], pixels[offset + 1], pixels[offset + 2], pixels[offset + 3]);
    }

    private static long AlphaSum(byte[] pixels)
    {
        long total = 0;
        for (int index = 3; index < pixels.Length; index += 4) total += pixels[index];
        return total;
    }
    [Fact]
    public void General_matrix_radial_renders_centered_hotspot()
    {
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='64' height='64'><defs><radialGradient id='g' gradientUnits='userSpaceOnUse' cx='32' cy='32' r='20' gradientTransform='matrix(1 0.5 0.5 1 -16 -16)'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><rect x='0' y='0' width='64' height='64' fill='url(#g)'/></svg>", out int width, out int height);

        Assert.Equal(64, width);
        Assert.Equal(64, height);
        Assert.Equal(new SvgColor(248, 0, 6, 255), Pixel(pixels, width, 32, 32));
        Assert.Equal(new SvgColor(0, 0, 255, 255), Pixel(pixels, width, 0, 0));
        Assert.Equal(new SvgColor(0, 0, 255, 255), Pixel(pixels, width, 63, 63));
        Assert.Equal(new SvgColor(32, 0, 222, 255), Pixel(pixels, width, 50, 50));
    }

    [Fact]
    public void Skewed_matrix_radial_moves_focal_hotspot()
    {
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='64' height='64'><defs><radialGradient id='g' gradientUnits='userSpaceOnUse' cx='32' cy='32' r='20' fx='40' fy='32' gradientTransform='matrix(1 0 0.5 1 0 0)'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><rect x='0' y='0' width='64' height='64' fill='url(#g)'/></svg>", out int width, out _);
        byte[] plain = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='64' height='64'><defs><radialGradient id='g' gradientUnits='userSpaceOnUse' cx='32' cy='32' r='20' fx='40' fy='32'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><rect x='0' y='0' width='64' height='64' fill='url(#g)'/></svg>", out _, out _);

        Assert.NotEqual(plain, pixels);
        Assert.Equal(new SvgColor(38, 0, 216, 255), Pixel(pixels, width, 32, 32));
        Assert.Equal(new SvgColor(0, 0, 255, 255), Pixel(pixels, width, 0, 0));
        Assert.Equal(new SvgColor(3, 0, 251, 255), Pixel(pixels, width, 50, 50));
    }

    [Fact]
    public void Off_canvas_general_radial_paints_solid_stop()
    {
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='64' height='64'><defs><radialGradient id='g' gradientUnits='userSpaceOnUse' cx='32' cy='32' r='20' gradientTransform='matrix(3 1 2 2 8 8)'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><rect x='0' y='0' width='64' height='64' fill='url(#g)'/></svg>", out int width, out _);

        Assert.Equal(new SvgColor(0, 0, 255, 255), Pixel(pixels, width, 0, 0));
        Assert.Equal(new SvgColor(0, 0, 255, 255), Pixel(pixels, width, 32, 32));
        Assert.Equal(new SvgColor(0, 0, 255, 255), Pixel(pixels, width, 63, 63));
    }

    [Fact]
    public void General_matrix_radial_paints_shared_polygon_path()
    {
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='64' height='64'><defs><radialGradient id='g' gradientUnits='userSpaceOnUse' cx='32' cy='32' r='20' gradientTransform='matrix(1 0.5 0.5 1 -16 -16)'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><path d='M32,0 L64,32 L32,64 L0,32 Z' fill='url(#g)'/></svg>", out int width, out _);

        Assert.Equal(new SvgColor(248, 0, 6, 255), Pixel(pixels, width, 32, 32));
        Assert.Equal(new SvgColor(28, 0, 226, 255), Pixel(pixels, width, 32, 20));
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(pixels, width, 0, 0));
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(pixels, width, 63, 63));
    }
    [Fact]
    public void Nonuniform_object_bounding_box_radial_renders_stretched_ellipse()
    {
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='64' height='64'><defs><radialGradient id='g' cx='0.5' cy='0.5' r='0.5' gradientTransform='scale(2 1)'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><rect x='0' y='0' width='64' height='64' fill='url(#g)'/></svg>", out int width, out _);

        Assert.Equal(new SvgColor(251, 0, 5, 255), Pixel(pixels, width, 63, 32));
        Assert.Equal(new SvgColor(129, 0, 125, 255), Pixel(pixels, width, 32, 32));
        Assert.Equal(new SvgColor(0, 0, 255, 255), Pixel(pixels, width, 0, 0));
        Assert.Equal(new SvgColor(0, 0, 255, 255), Pixel(pixels, width, 32, 0));
    }
}
