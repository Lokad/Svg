using System.Buffers.Binary;
using System.IO.Compression;

namespace Lokad.Svg.Tests;

public sealed class SvgStrokeFillTests
{
    [Fact]
    public void Open_polyline_stroke_makes_butt_band()
    {
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><polyline points='0,0 4,0 8,0' fill='none' stroke='red' stroke-width='2'/></svg>", out _, out _);

        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, 8, 0, 0));
        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, 8, 4, 0));
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(pixels, 8, 4, 2));
        Assert.Equal(2040, AlphaSum(pixels));
    }

    [Fact]
    public void Closed_square_stroke_makes_ring()
    {
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><polygon points='0,0 8,0 8,8 0,8' fill='none' stroke='red' stroke-width='2'/></svg>", out _, out _);

        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, 8, 0, 0));
        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, 8, 7, 7));
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(pixels, 8, 4, 4));
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(pixels, 8, 1, 1));
        Assert.Equal(7140, AlphaSum(pixels));
    }

    [Fact]
    public void Open_path_stroke_miters()
    {
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><path d='M0,0 L8,0 L8,4' fill='none' stroke='red' stroke-width='2'/></svg>", out _, out _);

        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, 8, 0, 0));
        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, 8, 4, 0));
        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, 8, 7, 3));
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(pixels, 8, 0, 3));
        Assert.Equal(2805, AlphaSum(pixels));
    }

    [Fact]
    public void Closed_triangle_stroke_rings_diagonal()
    {
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><path d='M0,0 L8,0 L0,8 Z' fill='none' stroke='red' stroke-width='2'/></svg>", out _, out _);

        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, 8, 0, 4));
        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, 8, 4, 0));
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(pixels, 8, 1, 1));
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(pixels, 8, 4, 7));
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(pixels, 8, 7, 7));
    }

    [Fact]
    public void Fill_and_stroke_combine_in_paint_order()
    {
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><polygon points='0,0 8,0 8,8 0,8' fill='red' stroke='blue' stroke-width='2'/></svg>", out _, out _);

        Assert.Equal(new SvgColor(0, 0, 255, 255), Pixel(pixels, 8, 0, 0));
        Assert.Equal(new SvgColor(0, 0, 255, 255), Pixel(pixels, 8, 7, 7));
        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, 8, 4, 4));
    }

    [Fact]
    public void Open_polyline_and_closed_polygon_stroke_differently()
    {
        byte[] open = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><polyline points='0,0 8,0 8,8 0,8' fill='none' stroke='red' stroke-width='2'/></svg>", out _, out _);
        byte[] closed = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><polygon points='0,0 8,0 8,8 0,8' fill='none' stroke='red' stroke-width='2'/></svg>", out _, out _);

        Assert.NotEqual(open, closed);
    }

    [Fact]
    public void Fill_rule_does_not_change_stroke_outlines()
    {
        byte[] plain = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><polygon points='0,0 8,0 8,8 0,8' fill='none' stroke='red' stroke-width='2'/></svg>", out _, out _);
        byte[] evenOdd = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><polygon points='0,0 8,0 8,8 0,8' fill='none' stroke='red' stroke-width='2' fill-rule='evenodd'/></svg>", out _, out _);

        Assert.Equal(plain, evenOdd);
    }

    [Fact]
    public void Inline_style_width_matches_direct_attribute()
    {
        byte[] styled = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><line x1='2' y1='8' x2='14' y2='8' stroke='red' style='stroke-width:4'/></svg>", out _, out _);
        byte[] direct = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><line x1='2' y1='8' x2='14' y2='8' stroke='red' stroke-width='4'/></svg>", out _, out _);

        Assert.Equal(direct, styled);
    }

    [Fact]
    public void Inline_width_overrides_attribute()
    {
        byte[] styled = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><line x1='2' y1='8' x2='14' y2='8' stroke='red' stroke-width='2' style='stroke-width:6'/></svg>", out _, out _);
        byte[] direct = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><line x1='2' y1='8' x2='14' y2='8' stroke='red' stroke-width='6'/></svg>", out _, out _);

        Assert.Equal(direct, styled);
    }

    [Fact]
    public void Group_attribute_width_inherits_to_line()
    {
        byte[] grouped = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><g stroke-width='4'><line x1='2' y1='8' x2='14' y2='8' stroke='red'/></g></svg>", out _, out _);
        byte[] direct = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><line x1='2' y1='8' x2='14' y2='8' stroke='red' stroke-width='4'/></svg>", out _, out _);

        Assert.Equal(direct, grouped);
    }

    [Fact]
    public void Stylesheet_class_width_applies_to_line()
    {
        byte[] styled = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><style>.w { stroke-width: 4; }</style><line class='w' x1='2' y1='8' x2='14' y2='8' stroke='red'/></svg>", out _, out _);
        byte[] direct = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><line x1='2' y1='8' x2='14' y2='8' stroke='red' stroke-width='4'/></svg>", out _, out _);

        Assert.Equal(direct, styled);
    }

    [Fact]
    public void Root_style_width_applies_to_line()
    {
        byte[] styled = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16' style='stroke-width:4'><line x1='2' y1='8' x2='14' y2='8' stroke='red'/></svg>", out _, out _);
        byte[] direct = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><line x1='2' y1='8' x2='14' y2='8' stroke='red' stroke-width='4'/></svg>", out _, out _);

        Assert.Equal(direct, styled);
    }

    [Fact]
    public void Rect_inline_width_matches_direct_attribute()
    {
        byte[] styled = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><rect x='2' y='2' width='12' height='12' fill='red' stroke='blue' style='stroke-width:4'/></svg>", out _, out _);
        byte[] direct = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><rect x='2' y='2' width='12' height='12' fill='red' stroke='blue' stroke-width='4'/></svg>", out _, out _);

        Assert.Equal(direct, styled);
    }

    [Fact]
    public void Inline_miterlimit_matches_direct_attribute()
    {
        byte[] styled = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><polygon points='0,0 16,0 8,16' fill='red' stroke='blue' stroke-width='4' style='stroke-miterlimit:10'/></svg>", out _, out _);
        byte[] direct = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><polygon points='0,0 16,0 8,16' fill='red' stroke='blue' stroke-width='4' stroke-miterlimit='10'/></svg>", out _, out _);

        Assert.Equal(direct, styled);
    }

    [Fact]
    public void Group_attribute_miterlimit_inherits()
    {
        byte[] grouped = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><g stroke-miterlimit='10'><polygon points='0,0 16,0 8,16' fill='red' stroke='blue' stroke-width='4'/></g></svg>", out _, out _);
        byte[] direct = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><polygon points='0,0 16,0 8,16' fill='red' stroke='blue' stroke-width='4' stroke-miterlimit='10'/></svg>", out _, out _);

        Assert.Equal(direct, grouped);
    }

    [Fact]
    public void Line_style_opacity_scales_alpha()
    {
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><line x1='2' y1='8' x2='14' y2='8' stroke='red' stroke-width='2' style='stroke-opacity:0.5'/></svg>", out _, out _);

        Assert.Equal(new SvgColor(255, 0, 0, 128), Pixel(pixels, 16, 8, 7));
        Assert.Equal(new SvgColor(255, 0, 0, 128), Pixel(pixels, 16, 8, 8));
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(pixels, 16, 8, 0));
    }

    [Fact]
    public void Group_style_opacity_inherits_to_line()
    {
        byte[] grouped = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><g style='stroke-opacity:0.5'><line x1='2' y1='8' x2='14' y2='8' stroke='red' stroke-width='2'/></g></svg>", out _, out _);
        byte[] direct = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><line x1='2' y1='8' x2='14' y2='8' stroke='red' stroke-width='2' style='stroke-opacity:0.5'/></svg>", out _, out _);

        Assert.Equal(direct, grouped);
    }

    [Fact]
    public void Stroke_opacity_on_fill_less_rect_matches_element_opacity()
    {
        // S12: rectangle stroke bands are butt-jointed without overlap, so a
        // translucent stroke composes exactly like element opacity on
        // fill-less strokes.
        byte[] strokeOpacity = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><rect x='0' y='0' width='8' height='8' fill='none' stroke='black' stroke-width='2' stroke-opacity='0.5'/></svg>", out _, out _);
        byte[] elementOpacity = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><rect x='0' y='0' width='8' height='8' fill='none' stroke='black' stroke-width='2' opacity='0.5'/></svg>", out _, out _);

        Assert.Equal(elementOpacity, strokeOpacity);
    }

    [Fact]
    public void Stroke_opacity_on_butt_line_matches_element_opacity()
    {
        // S12: butt-capped line bands never overlap, so stroke opacity
        // composes exactly like element opacity.
        byte[] strokeOpacity = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='8'><line x1='0' y1='4' x2='16' y2='4' stroke='red' stroke-width='2' stroke-opacity='0.5'/></svg>", out _, out _);
        byte[] elementOpacity = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='8'><line x1='0' y1='4' x2='16' y2='4' stroke='red' stroke-width='2' opacity='0.5'/></svg>", out _, out _);

        Assert.Equal(elementOpacity, strokeOpacity);
    }

    [Fact]
    public void Stroke_opacity_on_polygon_outline_matches_element_opacity()
    {
        // S12: polygon outline fills inherit the same single-coverage paint,
        // so both opacity spellings coincide byte-identically.
        byte[] strokeOpacity = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='8'><polygon points='0,0 16,0 16,8 0,8' fill='none' stroke='red' stroke-width='2' stroke-opacity='0.5'/></svg>", out _, out _);
        byte[] elementOpacity = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='8'><polygon points='0,0 16,0 16,8 0,8' fill='none' stroke='red' stroke-width='2' opacity='0.5'/></svg>", out _, out _);

        Assert.Equal(elementOpacity, strokeOpacity);
    }

    [Fact]
    public void Stroke_opacity_on_path_outline_matches_element_opacity()
    {
        // S12: path outline fills share the polygon machinery, so both opacity
        // spellings coincide byte-identically here as well.
        byte[] strokeOpacity = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='8'><path d='M0,0 H16 V8 H0 Z' fill='none' stroke='red' stroke-width='2' stroke-opacity='0.5'/></svg>", out _, out _);
        byte[] elementOpacity = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='8'><path d='M0,0 H16 V8 H0 Z' fill='none' stroke='red' stroke-width='2' opacity='0.5'/></svg>", out _, out _);

        Assert.Equal(elementOpacity, strokeOpacity);
    }

    [Fact]
    public void Stroke_opacity_on_circle_ring_matches_element_opacity()
    {
        // S12: opposite-wound ring fills cover each pixel once, so stroke
        // opacity matches element opacity exactly; round caps with
        // translucency stay honestly gated by Rejects_translucent_round_capped_lines.
        byte[] strokeOpacity = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='8'><circle cx='8' cy='4' r='3' fill='none' stroke='red' stroke-width='2' stroke-opacity='0.5'/></svg>", out _, out _);
        byte[] elementOpacity = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='8'><circle cx='8' cy='4' r='3' fill='none' stroke='red' stroke-width='2' opacity='0.5'/></svg>", out _, out _);

        Assert.Equal(elementOpacity, strokeOpacity);
    }

    [Fact]
    public void Stroke_opacity_on_filled_rect_paints_blended_band()
    {
        // S12: translucent bands composite once over the fill with no corner
        // double-coverage, so black at half opacity over red is exactly
        // (127, 0, 128, 255) on fully covered band pixels.
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><rect x='2' y='2' width='12' height='12' fill='red' stroke='blue' stroke-width='2' style='stroke-opacity:0.5'/></svg>", out int width, out int height);

        Assert.Equal(16, width);
        Assert.Equal(16, height);
        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, width, 8, 8));
        Assert.Equal(new SvgColor(127, 0, 128, 255), Pixel(pixels, width, 8, 2));
    }

    [Fact]
    public void Group_stroke_paint_inherits_to_line()
    {
        byte[] grouped = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><g stroke='red'><line x1='2' y1='8' x2='14' y2='8' stroke-width='2'/></g></svg>", out _, out _);
        byte[] direct = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><line x1='2' y1='8' x2='14' y2='8' stroke='red' stroke-width='2'/></svg>", out _, out _);

        Assert.Equal(direct, grouped);
    }

    [Fact]
    public void Inner_none_blocks_group_stroke()
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><g stroke='red'><g stroke='none'><line x1='2' y1='8' x2='14' y2='8' stroke-width='2'/></g></g></svg>");

        SvgException rejected = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, rejected.ErrorCode);
        Assert.Contains("Only untransformed horizontal or vertical lines are implemented.", rejected.Message);
    }

    [Fact]
    public void Root_stroke_attribute_inherits_to_line()
    {
        byte[] rooted = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16' stroke='red'><line x1='2' y1='8' x2='14' y2='8' stroke-width='2'/></svg>", out _, out _);
        byte[] direct = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><line x1='2' y1='8' x2='14' y2='8' stroke='red' stroke-width='2'/></svg>", out _, out _);

        Assert.Equal(direct, rooted);
    }

    [Fact]
    public void Invalid_group_stroke_value_stays_unsupported()
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><g stroke='banana'><line x1='2' y1='8' x2='14' y2='8' stroke-width='2'/></g></svg>");

        SvgException rejected = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, rejected.ErrorCode);
    }

    [Theory]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><polygon points='0,0 8,0 8,8 0,8' fill='none' stroke='red' stroke-width='3'/></svg>")]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><polygon points='0,0 8,0 8,8 0,8' fill='none' stroke='red' stroke-width='0'/></svg>")]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><polygon points='0,0 8,0 8,8 0,8' fill='none' stroke='red' stroke-width='2.5'/></svg>")]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><polygon points='0,0 8,0 8,8 0,8' fill='none' stroke='red' style='stroke-width:3'/>")]
    public void Non_even_stroke_widths_stay_unsupported(string svg)
    {
        SvgException rejected = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(System.Text.Encoding.UTF8.GetBytes(svg), SvgRenderOptions.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, rejected.ErrorCode);
    }

    [Theory]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><polygon points='0,0 8,0 8,8 0,8' fill='none' stroke='red' stroke-width='2' stroke-linejoin='bevel'/></svg>")]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><polygon points='0.5,0 8,0 8,8 0,8' fill='none' stroke='red' stroke-width='2' stroke-linejoin='bevel'/></svg>")]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><polygon points='0,0 8,0 8,8 0,8' fill='none' stroke='red' stroke-width='2' stroke-miterlimit='0.5'/></svg>")]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><polygon points='0,0 8,0 8,8 0,8' fill='none' stroke='red' stroke-width='2' style='stroke-miterlimit:0.5'/>")]
    public void Non_admitted_stroke_styles_stay_unsupported(string svg)
    {
        SvgException rejected = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(System.Text.Encoding.UTF8.GetBytes(svg), SvgRenderOptions.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, rejected.ErrorCode);
    }

    [Fact]
    public void Stroke_under_anisotropic_transform_stays_unsupported()
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='8'><g transform='scale(2,1)'><polygon points='0,0 8,0 8,8 0,8' fill='none' stroke='red' stroke-width='2'/></g></svg>");

        SvgException rejected = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, rejected.ErrorCode);
    }

    [Fact]
    public void Round_cap_extends_past_tip()
    {
        byte[] round = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='8'><polyline points='2,2 12,2 12,6' fill='none' stroke='red' stroke-width='4' stroke-linecap='round'/></svg>", out _, out _);
        byte[] butt = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='8'><polyline points='2,2 12,2 12,6' fill='none' stroke='red' stroke-width='4'/></svg>", out _, out _);

        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(round, 16, 1, 2));
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(butt, 16, 1, 2));
    }

    [Fact]
    public void Square_cap_extends_past_tip()
    {
        byte[] square = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='8'><polyline points='2,2 12,2 12,6' fill='none' stroke='red' stroke-width='4' stroke-linecap='square'/></svg>", out _, out _);
        byte[] butt = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='8'><polyline points='2,2 12,2 12,6' fill='none' stroke='red' stroke-width='4'/></svg>", out _, out _);

        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(square, 16, 1, 2));
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(butt, 16, 1, 2));
    }

    [Fact]
    public void Square_caps_match_butt_on_closed_contours()
    {
        byte[] square = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><polygon points='0,0 8,0 8,8 0,8' fill='none' stroke='red' stroke-width='2' stroke-linecap='square'/></svg>", out _, out _);
        byte[] butt = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><polygon points='0,0 8,0 8,8 0,8' fill='none' stroke='red' stroke-width='2'/></svg>", out _, out _);

        Assert.Equal(butt, square);
    }

    [Fact]
    public void Single_square_dash_matches_solid_square_line()
    {
        byte[] dashed = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='8'><line x1='0' y1='4' x2='8' y2='4' stroke='red' stroke-width='2' stroke-dasharray='8' stroke-linecap='square'/></svg>", out _, out _);
        byte[] solid = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='8'><line x1='0' y1='4' x2='8' y2='4' stroke='red' stroke-width='2' stroke-linecap='square'/></svg>", out _, out _);

        Assert.Equal(solid, dashed);
    }

    [Fact]
    public void Square_dash_extends_past_butt_tip()
    {
        byte[] square = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='8'><line x1='2' y1='4' x2='14' y2='4' stroke='red' stroke-width='2' stroke-dasharray='4 4' stroke-linecap='square'/></svg>", out _, out _);
        byte[] butt = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='8'><line x1='2' y1='4' x2='14' y2='4' stroke='red' stroke-width='2' stroke-dasharray='4 4'/></svg>", out _, out _);

        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(square, 16, 1, 4));
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(butt, 16, 1, 4));
    }
    [Fact]
    public void Negative_dash_offset_wraps_phase()
    {
        byte[] negative = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='8'><line x1='0' y1='4' x2='16' y2='4' stroke='red' stroke-width='2' stroke-dasharray='4 4' stroke-dashoffset='-2'/></svg>", out _, out _);
        byte[] wrapped = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='8'><line x1='0' y1='4' x2='16' y2='4' stroke='red' stroke-width='2' stroke-dasharray='4 4' stroke-dashoffset='6'/></svg>", out _, out _);

        Assert.Equal(wrapped, negative);
    }

    [Fact]
    public void Round_capped_line_matches_line_with_circle_tips()
    {
        byte[] round = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='8'><line x1='2' y1='4' x2='14' y2='4' stroke='red' stroke-width='4' stroke-linecap='round'/></svg>", out _, out _);
        byte[] composed = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='8'><line x1='2' y1='4' x2='14' y2='4' stroke='red' stroke-width='4'/><circle cx='2' cy='4' r='2' fill='red'/><circle cx='14' cy='4' r='2' fill='red'/></svg>", out _, out _);

        Assert.Equal(composed, round);
        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(round, 16, 2, 4));
        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(round, 16, 14, 4));
        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(round, 16, 8, 4));
    }

    [Fact]
    public void Dashed_open_polyline_paints_runs()
    {
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='8'><polyline points='2,2 12,2 12,6' fill='none' stroke='red' stroke-width='2' stroke-dasharray='4,4'/></svg>", out _, out _);

        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, 16, 3, 2));
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(pixels, 16, 7, 2));
        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, 16, 10, 2));
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(pixels, 16, 12, 5));
    }

    [Fact]
    public void Dash_offset_shifts_phase()
    {
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='8'><polyline points='2,2 12,2 12,6' fill='none' stroke='red' stroke-width='2' stroke-dasharray='6,2' stroke-dashoffset='2'/></svg>", out _, out _);

        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, 16, 3, 2));
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(pixels, 16, 7, 2));
    }

    [Fact]
    public void Round_caps_apply_to_each_dash()
    {
        byte[] round = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='8'><polyline points='2,2 12,2 12,6' fill='none' stroke='red' stroke-width='2' stroke-linecap='round' stroke-dasharray='4,4'/></svg>", out _, out _);
        byte[] butt = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='8'><polyline points='2,2 12,2 12,6' fill='none' stroke='red' stroke-width='2' stroke-dasharray='4,4'/></svg>", out _, out _);

        Assert.NotEqual(butt, round);
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(butt, 16, 6, 2));
        Assert.NotEqual(0, Pixel(round, 16, 6, 2).Alpha);
    }

    [Fact]
    public void Dashed_path_stroke_paints_runs()
    {
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='8'><path d='M2,2 H12 V6' fill='none' stroke='red' stroke-width='2' stroke-dasharray='4,4'/></svg>", out _, out _);

        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, 16, 3, 2));
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(pixels, 16, 7, 2));
    }

    [Fact]
    public void Dashed_fractional_polygon_differs_from_solid()
    {
        byte[] dashed = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><polygon points='0.5,0.5 7.5,0.5 7.5,7.5 0.5,7.5' fill='none' stroke='red' stroke-width='2' stroke-dasharray='4,4'/></svg>", out _, out _);
        byte[] solid = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><polygon points='0.5,0.5 7.5,0.5 7.5,7.5 0.5,7.5' fill='none' stroke='red' stroke-width='2'/></svg>", out _, out _);

        Assert.NotEqual(solid, dashed);
    }

    [Fact]
    public void Path_length_scales_dash_pattern()
    {
        byte[] scaled = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='8'><polyline points='2,2 12,2 12,6' fill='none' stroke='red' stroke-width='2' stroke-dasharray='4,4' pathLength='28'/></svg>", out _, out _);
        byte[] halved = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='8'><polyline points='2,2 12,2 12,6' fill='none' stroke='red' stroke-width='2' stroke-dasharray='2,2'/></svg>", out _, out _);

        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(scaled, 16, 3, 2));
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(scaled, 16, 9, 2));
        Assert.Equal(halved, scaled);
    }

    [Fact]
    public void Zero_path_length_renders_solid_stroke()
    {
        byte[] pinned = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><polygon points='0,0 8,0 8,8 0,8' fill='blue' stroke='red' stroke-width='2' stroke-dasharray='4,4' pathLength='0'/></svg>", out _, out _);
        byte[] solid = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><polygon points='0,0 8,0 8,8 0,8' fill='blue' stroke='red' stroke-width='2'/></svg>", out _, out _);

        Assert.Equal(solid, pinned);
    }

    [Fact]
    public void Zero_path_length_with_zero_dash_paints_no_stroke()
    {
        byte[] dashed = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><polygon points='0,0 8,0 8,8 0,8' fill='blue' stroke='red' stroke-width='2' stroke-dasharray='0,4' pathLength='0'/></svg>", out _, out _);
        byte[] unstroked = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><polygon points='0,0 8,0 8,8 0,8' fill='blue'/></svg>", out _, out _);

        Assert.Equal(unstroked, dashed);
    }

    [Fact]
    public void Rejects_negative_path_lengths()
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><polyline points='0,4 8,4 8,6' fill='none' stroke='red' stroke-width='2' stroke-dasharray='4,4' pathLength='-2'/></svg>");

        SvgException rejected = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.MalformedInput, rejected.ErrorCode);
    }

    [Fact]
    public void Rejects_non_numeric_path_lengths()
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><polyline points='0,4 8,4 8,6' fill='none' stroke='red' stroke-width='2' stroke-dasharray='4,4' pathLength='10px'/></svg>");

        SvgException rejected = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, rejected.ErrorCode);
    }

    [Fact]
    public void Inline_path_length_matches_direct_attribute()
    {
        byte[] styled = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><polyline points='0,4 8,4 8,6' fill='none' stroke='red' stroke-width='2' stroke-dasharray='3 1' style='pathLength: 14'/></svg>", out _, out _);
        byte[] direct = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><polyline points='0,4 8,4 8,6' fill='none' stroke='red' stroke-width='2' stroke-dasharray='3 1' pathLength='14'/></svg>", out _, out _);

        Assert.Equal(direct, styled);
    }

    [Fact]
    public void Stylesheet_path_length_matches_direct_attribute()
    {
        byte[] styled = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><style>.d { pathLength: 14; }</style><polyline class='d' points='0,4 8,4 8,6' fill='none' stroke='red' stroke-width='2' stroke-dasharray='3 1'/></svg>", out _, out _);
        byte[] direct = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><polyline points='0,4 8,4 8,6' fill='none' stroke='red' stroke-width='2' stroke-dasharray='3 1' pathLength='14'/></svg>", out _, out _);

        Assert.Equal(direct, styled);
    }

    [Fact]
    public void Odd_dasharray_doubles()
    {
        byte[] odd = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='8'><polyline points='2,2 12,2 12,6' fill='none' stroke='red' stroke-width='2' stroke-dasharray='4,4,4'/></svg>", out _, out _);
        byte[] even = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='8'><polyline points='2,2 12,2 12,6' fill='none' stroke='red' stroke-width='2' stroke-dasharray='4,4'/></svg>", out _, out _);

        Assert.Equal(even, odd);
    }

    [Fact]
    public void All_zero_dash_renders_fill_only()
    {
        byte[] dashed = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><polygon points='0,0 8,0 8,8 0,8' fill='blue' stroke='red' stroke-width='2' stroke-dasharray='0,0'/></svg>", out _, out _);
        byte[] unstroked = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><polygon points='0,0 8,0 8,8 0,8' fill='blue'/></svg>", out _, out _);

        Assert.Equal(unstroked, dashed);
    }

    [Fact]
    public void Rejects_negative_dash_entries()
    {
        SvgException rejected = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><polyline points='0,4 8,4' fill='none' stroke='red' stroke-width='2' stroke-dasharray='4,-1'/></svg>"), SvgRenderOptions.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.MalformedInput, rejected.ErrorCode);
    }

    [Fact]
    public void Rejects_overlong_dash_arrays()
    {
        SvgException rejected = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><polyline points='0,4 8,4' fill='none' stroke='red' stroke-width='2' stroke-dasharray='1,1,1,1,1,1,1,1,1'/></svg>"), SvgRenderOptions.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, rejected.ErrorCode);
    }

    [Fact]
    public void Rejects_percentage_dash_entries()
    {
        SvgException rejected = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><polyline points='0,4 8,4' fill='none' stroke='red' stroke-width='2' stroke-dasharray='50%,2'/></svg>"), SvgRenderOptions.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, rejected.ErrorCode);
    }

    [Fact]
    public void Rejects_zero_dashes_with_round_caps()
    {
        SvgException rejected = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><polyline points='0,4 8,4' fill='none' stroke='red' stroke-width='2' stroke-linecap='round' stroke-dasharray='0,4'/></svg>"), SvgRenderOptions.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, rejected.ErrorCode);
    }

    [Fact]
    public void Nonscaling_uniform_stroke_matches_explicit()
    {
        byte[] scaled = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'><g transform='scale(2)'><polygon points='0,0 8,0 8,8 0,8' fill='none' stroke='red' stroke-width='2' vector-effect='non-scaling-stroke'/></g></svg>", out _, out _);
        byte[] direct = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'><polygon points='0,0 16,0 16,16 0,16' fill='none' stroke='red' stroke-width='2'/></svg>", out _, out _);

        Assert.Equal(direct, scaled);
    }

    [Fact]
    public void Nonscaling_nonuniform_stroke_matches_explicit()
    {
        byte[] scaled = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'><g transform='scale(2,1)'><polygon points='0,0 8,0 8,8 0,8' fill='none' stroke='red' stroke-width='2' vector-effect='non-scaling-stroke'/></g></svg>", out _, out _);
        byte[] direct = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'><polygon points='0,0 16,0 16,8 0,8' fill='none' stroke='red' stroke-width='2'/></svg>", out _, out _);

        Assert.Equal(direct, scaled);
    }

    [Fact]
    public void Nonscaling_dashed_stroke_matches_explicit()
    {
        byte[] scaled = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'><g transform='scale(2)'><polygon points='0,0 8,0 8,8 0,8' fill='none' stroke='red' stroke-width='2' stroke-dasharray='2,2' vector-effect='non-scaling-stroke'/></g></svg>", out _, out _);
        byte[] direct = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'><polygon points='0,0 16,0 16,16 0,16' fill='none' stroke='red' stroke-width='2' stroke-dasharray='4,4'/></svg>", out _, out _);

        Assert.Equal(direct, scaled);
    }

    [Fact]
    public void Nonscaling_circle_stroke_matches_explicit()
    {
        byte[] scaled = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'><g transform='scale(2)'><circle cx='8' cy='8' r='6' fill='none' stroke='red' stroke-width='2' vector-effect='non-scaling-stroke'/></g></svg>", out _, out _);
        byte[] direct = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'><circle cx='16' cy='16' r='12' fill='none' stroke='red' stroke-width='2'/></svg>", out _, out _);

        Assert.Equal(direct, scaled);
    }

    [Fact]
    public void Nonuniform_dashed_nonscaling_stroke_stays_unsupported()
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'><g transform='scale(2,1)'><polygon points='0,0 8,0 8,8 0,8' fill='none' stroke='red' stroke-width='2' stroke-dasharray='2,2' vector-effect='non-scaling-stroke'/></g></svg>");

        SvgException rejected = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, rejected.ErrorCode);
    }

    [Fact]
    public void Rejects_unknown_vector_effects()
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><polygon points='0,0 8,0 8,8 0,8' fill='none' stroke='red' stroke-width='2' vector-effect='non-rotation'/></svg>");

        SvgException rejected = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, rejected.ErrorCode);
    }

    [Fact]
    public void Vector_effect_on_rectangle_stroke_stays_unsupported()
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><rect x='0' y='0' width='8' height='8' fill='red' stroke='blue' stroke-width='2' vector-effect='non-scaling-stroke'/></svg>");

        SvgException rejected = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, rejected.ErrorCode);
    }

    [Fact]
    public void Round_join_differs_from_miter()
    {
        byte[] round = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><polyline points='2,14 8,2 14,14' fill='none' stroke='red' stroke-width='4' stroke-linejoin='round'/></svg>", out _, out _);
        byte[] miter = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><polyline points='2,14 8,2 14,14' fill='none' stroke='red' stroke-width='4'/></svg>", out _, out _);

        Assert.NotEqual(miter, round);
    }

    [Fact]
    public void Round_cap_on_rectangle_stroke_renders_as_butt()
    {
        byte[] butt = System.Text.Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"8\" height=\"8\"><rect x=\"0\" y=\"0\" width=\"8\" height=\"8\" fill=\"red\" stroke=\"blue\" stroke-width=\"2\"/></svg>");
        byte[] round = System.Text.Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"8\" height=\"8\"><rect x=\"0\" y=\"0\" width=\"8\" height=\"8\" fill=\"red\" stroke=\"blue\" stroke-width=\"2\" stroke-linecap=\"round\"/></svg>");

        ReadOnlyMemory<byte> buttPng = SvgConverter.ConvertToPng(butt, SvgRenderOptions.Default, CancellationToken.None);
        ReadOnlyMemory<byte> roundPng = SvgConverter.ConvertToPng(round, SvgRenderOptions.Default, CancellationToken.None);

        Assert.True(roundPng.Span.SequenceEqual(buttPng.Span));
    }

    [Fact]
    public void Square_cap_on_rectangle_stroke_renders_as_butt()
    {
        byte[] butt = System.Text.Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"8\" height=\"8\"><rect x=\"0\" y=\"0\" width=\"8\" height=\"8\" fill=\"red\" stroke=\"blue\" stroke-width=\"2\"/></svg>");
        byte[] square = System.Text.Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"8\" height=\"8\"><rect x=\"0\" y=\"0\" width=\"8\" height=\"8\" fill=\"red\" stroke=\"blue\" stroke-width=\"2\" stroke-linecap=\"square\"/></svg>");

        ReadOnlyMemory<byte> buttPng = SvgConverter.ConvertToPng(butt, SvgRenderOptions.Default, CancellationToken.None);
        ReadOnlyMemory<byte> squarePng = SvgConverter.ConvertToPng(square, SvgRenderOptions.Default, CancellationToken.None);

        Assert.True(squarePng.Span.SequenceEqual(buttPng.Span));
    }

    [Fact]
    public void Dasharray_on_rectangle_stroke_stays_unsupported()
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><rect x='0' y='0' width='8' height='8' fill='red' stroke='blue' stroke-width='2' stroke-dasharray='2,2'/></svg>");

        SvgException rejected = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, rejected.ErrorCode);
    }

    [Fact]
    public void Inline_dash_list_on_rectangle_stroke_stays_unsupported()
    {
        // S12: solid sharp-rectangle bands silently dropped inline dash lists;
        // the lowering now gates them exactly like the attribute spelling.
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><rect x='0' y='0' width='8' height='8' fill='red' stroke='blue' stroke-width='2' style='stroke-dasharray:4 4'/></svg>");

        SvgException rejected = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, rejected.ErrorCode);
    }

    [Fact]
    public void Inline_dash_list_on_polygon_stroke_matches_attribute()
    {
        // S12: inline dash lists flow through the cascade into the admitted
        // polygon dash kernel exactly like the attribute spelling.
        byte[] inline = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='8'><polygon points='0,0 16,0 16,8 0,8' fill='none' stroke='red' stroke-width='2' style='stroke-dasharray:4 4'/></svg>");
        byte[] attribute = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='8'><polygon points='0,0 16,0 16,8 0,8' fill='none' stroke='red' stroke-width='2' stroke-dasharray='4 4'/></svg>");
        ReadOnlyMemory<byte> inlinePng = SvgConverter.ConvertToPng(inline, SvgRenderOptions.Default, CancellationToken.None);
        ReadOnlyMemory<byte> attributePng = SvgConverter.ConvertToPng(attribute, SvgRenderOptions.Default, CancellationToken.None);

        Assert.True(inlinePng.Span.SequenceEqual(attributePng.Span));
    }

    [Fact]
    public void Stylesheet_dash_list_on_polygon_stroke_matches_attribute()
    {
        // S12: stylesheet dash lists resolve through the same cascade into the
        // admitted polygon dash kernel.
        byte[] styled = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='8'><style>#p { stroke-dasharray: 4 4; }</style><polygon id='p' points='0,0 16,0 16,8 0,8' fill='none' stroke='red' stroke-width='2'/></svg>");
        byte[] attribute = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='8'><polygon points='0,0 16,0 16,8 0,8' fill='none' stroke='red' stroke-width='2' stroke-dasharray='4 4'/></svg>");
        ReadOnlyMemory<byte> styledPng = SvgConverter.ConvertToPng(styled, SvgRenderOptions.Default, CancellationToken.None);
        ReadOnlyMemory<byte> attributePng = SvgConverter.ConvertToPng(attribute, SvgRenderOptions.Default, CancellationToken.None);

        Assert.True(styledPng.Span.SequenceEqual(attributePng.Span));
    }

    [Fact]
    public void Inline_neutralized_dasharray_on_rectangle_stroke_matches_solid()
    {
        // R36: validation honors inline overrides of rejected attribute
        // spellings, and lowering already evaluates the cascaded value.
        byte[] neutralized = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><rect x='0' y='0' width='8' height='8' fill='red' stroke='blue' stroke-width='2' stroke-dasharray='2,2' style='stroke-dasharray:none'/></svg>");
        byte[] solid = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><rect x='0' y='0' width='8' height='8' fill='red' stroke='blue' stroke-width='2'/></svg>");
        ReadOnlyMemory<byte> neutralizedPng = SvgConverter.ConvertToPng(neutralized, SvgRenderOptions.Default, CancellationToken.None);
        ReadOnlyMemory<byte> solidPng = SvgConverter.ConvertToPng(solid, SvgRenderOptions.Default, CancellationToken.None);

        Assert.True(neutralizedPng.Span.SequenceEqual(solidPng.Span));
    }

    [Fact]
    public void Inline_neutralized_linejoin_on_rectangle_stroke_matches_miter()
    {
        // R36: the rectangle band gate evaluates the inline-neutralized join,
        // not the rejected attribute spelling.
        byte[] neutralized = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><rect x='0' y='0' width='8' height='8' fill='red' stroke='blue' stroke-width='2' stroke-linejoin='round' style='stroke-linejoin:miter'/></svg>");
        byte[] miter = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><rect x='0' y='0' width='8' height='8' fill='red' stroke='blue' stroke-width='2' stroke-linejoin='miter'/></svg>");
        ReadOnlyMemory<byte> neutralizedPng = SvgConverter.ConvertToPng(neutralized, SvgRenderOptions.Default, CancellationToken.None);
        ReadOnlyMemory<byte> miterPng = SvgConverter.ConvertToPng(miter, SvgRenderOptions.Default, CancellationToken.None);

        Assert.True(neutralizedPng.Span.SequenceEqual(miterPng.Span));
    }

    [Fact]
    public void Inline_only_linejoin_on_unstroked_rect_matches_plain()
    {
        // R36: inline stroke declarations without an attribute spelling stay
        // lowering business; band gates must not trip on them, since
        // fill-only geometry never lowers a stroke.
        byte[] styled = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><rect x='0' y='0' width='8' height='8' fill='red' style='stroke-linejoin:round'/></svg>");
        byte[] plain = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><rect x='0' y='0' width='8' height='8' fill='red'/></svg>");
        ReadOnlyMemory<byte> styledPng = SvgConverter.ConvertToPng(styled, SvgRenderOptions.Default, CancellationToken.None);
        ReadOnlyMemory<byte> plainPng = SvgConverter.ConvertToPng(plain, SvgRenderOptions.Default, CancellationToken.None);

        Assert.True(styledPng.Span.SequenceEqual(plainPng.Span));
    }

    [Fact]
    public void Stylesheet_neutralized_dasharray_on_rectangle_stroke_stays_unsupported()
    {
        // R36: stylesheet rules resolve after attribute validation, so a
        // stylesheet-neutralized rejected spelling stays explicitly unsupported
        // while lowering would already honor the cascaded value.
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><style>rect { stroke-dasharray: none; }</style><rect x='0' y='0' width='8' height='8' fill='red' stroke='blue' stroke-width='2' stroke-dasharray='2,2'/></svg>");

        SvgException rejected = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, rejected.ErrorCode);
    }

    [Fact]
    public void Inline_linecap_on_line_stroke_matches_attribute()
    {
        // R36: linecap style overrides evaluate through the cascade exactly
        // like the attribute spelling.
        byte[] styled = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><line x1='4' y1='8' x2='12' y2='8' stroke='red' stroke-width='4' stroke-linecap='square' style='stroke-linecap:butt'/></svg>", out _, out _);
        byte[] attribute = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><line x1='4' y1='8' x2='12' y2='8' stroke='red' stroke-width='4' stroke-linecap='butt'/></svg>", out _, out _);

        Assert.Equal(attribute, styled);
    }

    [Fact]
    public void Inline_dasharray_on_line_stroke_matches_attribute()
    {
        // R36: an inline neutralized dasharray renders the solid line exactly
        // like the dash-free spelling.
        byte[] styled = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><line x1='2' y1='8' x2='14' y2='8' stroke='red' stroke-width='2' stroke-dasharray='4 4' style='stroke-dasharray:none'/></svg>", out _, out _);
        byte[] attribute = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><line x1='2' y1='8' x2='14' y2='8' stroke='red' stroke-width='2'/></svg>", out _, out _);

        Assert.Equal(attribute, styled);
    }

    [Fact]
    public void Inline_dashoffset_on_line_stroke_matches_attribute()
    {
        // R36: dashoffset style overrides shift dash runs exactly like the
        // attribute spelling.
        byte[] styled = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><line x1='2' y1='8' x2='14' y2='8' stroke='red' stroke-width='2' stroke-dasharray='4 4' style='stroke-dashoffset:3'/></svg>", out _, out _);
        byte[] attribute = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><line x1='2' y1='8' x2='14' y2='8' stroke='red' stroke-width='2' stroke-dasharray='4 4' stroke-dashoffset='3'/></svg>", out _, out _);

        Assert.Equal(attribute, styled);
    }

    [Fact]
    public void Inline_dash_and_cap_on_gradient_line_matches_attribute()
    {
        // R36: the gradient-stroke routers and band gates evaluate effective
        // style values, so neutralized dash and cap spellings render exactly
        // like the plain gradient line.
        string gradient = "<defs><linearGradient id='g' gradientUnits='userSpaceOnUse' x1='0' y1='0' x2='16' y2='0'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs>";
        byte[] styled = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='8'>" + gradient + "<line x1='0' y1='4' x2='16' y2='4' stroke='url(#g)' stroke-width='2' stroke-dasharray='4 4' stroke-linecap='round' style='stroke-dasharray:none;stroke-linecap:butt'/></svg>", out _, out _);
        byte[] attribute = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='8'>" + gradient + "<line x1='0' y1='4' x2='16' y2='4' stroke='url(#g)' stroke-width='2'/></svg>", out _, out _);

        Assert.Equal(attribute, styled);
    }

    [Fact]
    public void Inline_cap_on_gradient_line_matches_attribute()
    {
        // R36: the gradient round-cap path honors the style cap, rendering
        // exactly like the attribute butt spelling.
        string gradient = "<defs><linearGradient id='g' gradientUnits='userSpaceOnUse' x1='0' y1='0' x2='16' y2='0'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs>";
        byte[] styled = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='8'>" + gradient + "<line x1='0' y1='4' x2='16' y2='4' stroke='url(#g)' stroke-width='2' stroke-linecap='round' style='stroke-linecap:butt'/></svg>", out _, out _);
        byte[] attribute = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='8'>" + gradient + "<line x1='0' y1='4' x2='16' y2='4' stroke='url(#g)' stroke-width='2' stroke-linecap='butt'/></svg>", out _, out _);

        Assert.Equal(attribute, styled);
    }

    [Fact]
    public void Gradient_fill_with_solid_stroke_paints_band_over_fill()
    {
        // S13: gradient-filled bands compose with an admitted solid stroke
        // outline in paint order, byte-exact against the pinned Chrome capture.
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><defs><linearGradient id='g' x1='0' y1='0' x2='1' y2='0'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><path d='M0,0 H16 V16 H0 Z' fill='url(#g)' stroke='blue' stroke-width='2'/></svg>", out int width, out int height);

        Assert.Equal(16, width);
        Assert.Equal(16, height);
        Assert.Equal(new SvgColor(0, 0, 255, 255), Pixel(pixels, width, 8, 0));
    }

    [Fact]
    public void Gradient_fill_with_solid_stroke_path_matches_polygon()
    {
        // S13: paths and polygons share the fill-plus-stroke lowering, so the
        // same outline renders byte-identically through both spellings.
        byte[] path = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><defs><linearGradient id='g' x1='0' y1='0' x2='1' y2='0'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><path d='M0,0 H16 V16 H0 Z' fill='url(#g)' stroke='blue' stroke-width='2'/></svg>", out _, out _);
        byte[] polygon = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><defs><linearGradient id='g' x1='0' y1='0' x2='1' y2='0'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><polygon points='0,0 16,0 16,16 0,16' fill='url(#g)' stroke='blue' stroke-width='2'/></svg>", out _, out _);

        Assert.Equal(polygon, path);
    }

    [Fact]
    public void Radial_fill_with_solid_stroke_paints_band_over_fill()
    {
        // S13: radial-filled bands compose with an admitted solid stroke
        // outline in paint order, matching Chrome up to a 3-pixel
        // single-step edge residual.
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><defs><radialGradient id='g'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><path d='M0,0 H16 V16 H0 Z' fill='url(#g)' stroke='blue' stroke-width='2'/></svg>", out int width, out int height);

        Assert.Equal(16, width);
        Assert.Equal(16, height);
        Assert.Equal(new SvgColor(0, 0, 255, 255), Pixel(pixels, width, 8, 0));
    }

    [Fact]
    public void Pattern_fill_with_solid_stroke_paints_band_over_fill()
    {
        // S13/S16: pattern-filled bands compose with an admitted solid stroke
        // outline in paint order, byte-exact against the pinned Chrome capture.
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><defs><pattern id='p' width='4' height='4' patternUnits='userSpaceOnUse'><rect width='2' height='2' fill='red'/></pattern></defs><path d='M0,0 H16 V16 H0 Z' fill='url(#p)' stroke='blue' stroke-width='2'/></svg>", out int width, out int height);

        Assert.Equal(16, width);
        Assert.Equal(16, height);
        Assert.Equal(new SvgColor(0, 0, 255, 255), Pixel(pixels, width, 8, 0));
    }

    [Fact]
    public void Pattern_fill_with_solid_stroke_path_matches_polygon()
    {
        // S13/S16: paths and polygons share the pattern fill-plus-stroke
        // lowering, so the same outline renders byte-identically through both
        // spellings.
        byte[] path = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><defs><pattern id='p' width='4' height='4' patternUnits='userSpaceOnUse'><rect width='2' height='2' fill='red'/></pattern></defs><path d='M0,0 H16 V16 H0 Z' fill='url(#p)' stroke='blue' stroke-width='2'/></svg>", out _, out _);
        byte[] polygon = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><defs><pattern id='p' width='4' height='4' patternUnits='userSpaceOnUse'><rect width='2' height='2' fill='red'/></pattern></defs><polygon points='0,0 16,0 16,16 0,16' fill='url(#p)' stroke='blue' stroke-width='2'/></svg>", out _, out _);

        Assert.Equal(polygon, path);
    }

    [Fact]
    public void Pattern_fill_with_solid_stroke_on_diagonal_paints_outline()
    {
        // S13/S16: diagonal outlines compose the same way; the axis-aligned
        // left edge stroke pixel is exact while the diagonal carries the
        // standing pattern-triangle delta.
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><defs><pattern id='p' width='4' height='4' patternUnits='userSpaceOnUse'><rect width='2' height='2' fill='red'/></pattern></defs><polygon points='0,0 16,0 0,16' fill='url(#p)' stroke='blue' stroke-width='2'/></svg>", out int width, out int height);

        Assert.Equal(16, width);
        Assert.Equal(16, height);
        Assert.Equal(new SvgColor(0, 0, 255, 255), Pixel(pixels, width, 0, 10));
    }

    [Fact]
    public void Radial_fill_with_solid_stroke_path_matches_polygon()
    {
        // S13: paths and polygons share the radial fill-plus-stroke lowering,
        // so the same outline renders byte-identically through both spellings.
        byte[] path = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><defs><radialGradient id='g'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><path d='M0,0 H16 V16 H0 Z' fill='url(#g)' stroke='blue' stroke-width='2'/></svg>", out _, out _);
        byte[] polygon = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><defs><radialGradient id='g'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><polygon points='0,0 16,0 16,16 0,16' fill='url(#g)' stroke='blue' stroke-width='2'/></svg>", out _, out _);

        Assert.Equal(polygon, path);
    }

    [Fact]
    public void Gradient_fill_with_solid_stroke_on_curved_path_paints_outline()
    {
        // S13: curved fills compose with solid outlines through flattened
        // geometry; the axis-aligned edge stroke pixels are exact while the
        // flattened curve carries the standing curve-edge delta.
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><defs><linearGradient id='g' x1='0' y1='0' x2='1' y2='0'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><path d='M0,8 Q8,0 16,8 L16,16 L0,16 Z' fill='url(#g)' stroke='blue' stroke-width='2'/></svg>", out int width, out int height);

        Assert.Equal(16, width);
        Assert.Equal(16, height);
        Assert.Equal(new SvgColor(0, 0, 255, 255), Pixel(pixels, width, 0, 12));
        Assert.Equal(new SvgColor(0, 0, 255, 255), Pixel(pixels, width, 8, 15));
    }

    [Fact]
    public void Gradient_fill_with_solid_stroke_on_diagonal_paints_outline()
    {
        // S13: diagonal outlines compose the same way; the axis-aligned left
        // edge stroke pixel is exact while the diagonal carries the standing
        // gradient-triangle delta.
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><defs><linearGradient id='g' x1='0' y1='0' x2='1' y2='0'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><polygon points='0,0 16,0 0,16' fill='url(#g)' stroke='blue' stroke-width='2'/></svg>", out int width, out int height);

        Assert.Equal(16, width);
        Assert.Equal(16, height);
        Assert.Equal(new SvgColor(0, 0, 255, 255), Pixel(pixels, width, 0, 10));
    }

    [Fact]
    public void Dasharray_on_line_stroke_renders_runs()
    {
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><line x1='0' y1='4' x2='8' y2='4' stroke='red' stroke-width='2' stroke-dasharray='2,2'/></svg>", out _, out _);

        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, 8, 0, 3));
        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, 8, 1, 4));
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(pixels, 8, 2, 3));
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(pixels, 8, 3, 4));
        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, 8, 4, 3));
        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, 8, 5, 4));
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(pixels, 8, 6, 3));
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(pixels, 8, 7, 4));
    }

    [Fact]
    public void Style_butt_cap_overrides_square_attribute_on_lines()
    {
        byte[] square = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><line x1='1' y1='4' x2='6' y2='4' stroke='red' stroke-width='2' stroke-linecap='square'/></svg>", out _, out _);
        byte[] styled = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><line x1='1' y1='4' x2='6' y2='4' stroke='red' stroke-width='2' stroke-linecap='square' style='stroke-linecap: butt'/></svg>", out _, out _);
        byte[] butt = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><line x1='1' y1='4' x2='6' y2='4' stroke='red' stroke-width='2'/></svg>", out _, out _);
        Assert.Equal(butt, styled);
        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(square, 8, 0, 4));
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(styled, 8, 0, 4));
    }
    [Fact]
    public void Style_none_dasharray_overrides_attribute_dashes()
    {
        byte[] dashed = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><line x1='0' y1='4' x2='8' y2='4' stroke='red' stroke-width='2' stroke-dasharray='2,2'/></svg>", out _, out _);
        byte[] styled = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><line x1='0' y1='4' x2='8' y2='4' stroke='red' stroke-width='2' stroke-dasharray='2,2' style='stroke-dasharray: none'/></svg>", out _, out _);
        byte[] solid = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><line x1='0' y1='4' x2='8' y2='4' stroke='red' stroke-width='2'/></svg>", out _, out _);
        Assert.Equal(solid, styled);
        Assert.NotEqual(dashed, styled);
    }
    [Fact]
    public void Style_dashoffset_matches_attribute_offset()
    {
        byte[] plain = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='8'><line x1='0' y1='4' x2='16' y2='4' stroke='red' stroke-width='2' stroke-dasharray='4 4'/></svg>", out _, out _);
        byte[] offsetAttribute = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='8'><line x1='0' y1='4' x2='16' y2='4' stroke='red' stroke-width='2' stroke-dasharray='4 4' stroke-dashoffset='3'/></svg>", out _, out _);
        byte[] offsetStyle = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='8'><line x1='0' y1='4' x2='16' y2='4' stroke='red' stroke-width='2' stroke-dasharray='4 4' style='stroke-dashoffset: 3'/></svg>", out _, out _);
        Assert.Equal(offsetAttribute, offsetStyle);
        Assert.NotEqual(plain, offsetStyle);
    }
    [Fact]
    public void Dasharray_on_circle_strokes_paints_runs()
    {
        byte[] plain = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><circle cx='4' cy='4' r='3' fill='red'/></svg>", out _, out _);
        byte[] dashed = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><circle cx='4' cy='4' r='3' fill='red' stroke='blue' stroke-width='2' stroke-dasharray='2,2'/></svg>", out _, out _);

        Assert.NotEqual(plain, dashed);
    }

    [Fact]
    public void Inline_dasharray_matches_direct_attribute()
    {
        byte[] styled = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><polyline points='0,4 8,4' fill='none' stroke='red' stroke-width='2' style='stroke-dasharray: 2,2'/></svg>", out _, out _);
        byte[] direct = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><polyline points='0,4 8,4' fill='none' stroke='red' stroke-width='2' stroke-dasharray='2,2'/></svg>", out _, out _);

        Assert.Equal(direct, styled);
    }

    [Fact]
    public void Round_join_on_line_stroke_stays_unsupported()
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><line x1='0' y1='4' x2='8' y2='4' stroke='red' stroke-width='2' stroke-linejoin='round'/></svg>");

        SvgException rejected = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, rejected.ErrorCode);
    }

    [Fact]
    public void Round_cap_on_gradient_stroke_renders_as_butt()
    {
        byte[] butt = System.Text.Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"8\" height=\"8\"><defs><linearGradient id=\"g\"><stop offset=\"0\" stop-color=\"#ff0000\"/><stop offset=\"1\" stop-color=\"#0000ff\"/></linearGradient></defs><rect x=\"0\" y=\"0\" width=\"8\" height=\"8\" fill=\"red\" stroke=\"url(#g)\" stroke-width=\"2\"/></svg>");
        byte[] round = System.Text.Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"8\" height=\"8\"><defs><linearGradient id=\"g\"><stop offset=\"0\" stop-color=\"#ff0000\"/><stop offset=\"1\" stop-color=\"#0000ff\"/></linearGradient></defs><rect x=\"0\" y=\"0\" width=\"8\" height=\"8\" fill=\"red\" stroke=\"url(#g)\" stroke-width=\"2\" stroke-linecap=\"round\"/></svg>");

        ReadOnlyMemory<byte> buttPng = SvgConverter.ConvertToPng(butt, SvgRenderOptions.Default, CancellationToken.None);
        ReadOnlyMemory<byte> roundPng = SvgConverter.ConvertToPng(round, SvgRenderOptions.Default, CancellationToken.None);

        Assert.True(roundPng.Span.SequenceEqual(buttPng.Span));
    }

    [Fact]
    public void Inline_round_cap_matches_direct_on_polygon()
    {
        byte[] styled = RenderPixels("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"8\" height=\"8\"><polygon points=\"0,0 8,0 8,8 0,8\" fill=\"none\" stroke=\"red\" stroke-width=\"2\" style=\"stroke-linecap: round\"/></svg>", out _, out _);
        byte[] direct = RenderPixels("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"8\" height=\"8\"><polygon points=\"0,0 8,0 8,8 0,8\" fill=\"none\" stroke=\"red\" stroke-width=\"2\" stroke-linecap=\"round\"/></svg>", out _, out _);

        Assert.Equal(direct, styled);
    }
    [Fact]
    public void Group_round_join_matches_direct_on_polygon()
    {
        byte[] grouped = RenderPixels("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"8\" height=\"8\"><g stroke-linejoin=\"round\"><polygon points=\"0,0 8,0 8,8 0,8\" fill=\"none\" stroke=\"red\" stroke-width=\"2\"/></g></svg>", out _, out _);
        byte[] direct = RenderPixels("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"8\" height=\"8\"><polygon points=\"0,0 8,0 8,8 0,8\" fill=\"none\" stroke=\"red\" stroke-width=\"2\" stroke-linejoin=\"round\"/></svg>", out _, out _);

        Assert.Equal(direct, grouped);
    }

    [Fact]
    public void Round_cap_on_symbol_rectangle_renders_as_butt()
    {
        byte[] butt = System.Text.Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"8\" height=\"8\"><defs><symbol id=\"s\" viewBox=\"0 0 8 8\"><rect x=\"0\" y=\"0\" width=\"8\" height=\"8\" fill=\"red\" stroke=\"blue\" stroke-width=\"2\"/></symbol></defs><use href=\"#s\" width=\"8\" height=\"8\"/></svg>");
        byte[] round = System.Text.Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"8\" height=\"8\"><defs><symbol id=\"s\" viewBox=\"0 0 8 8\"><rect x=\"0\" y=\"0\" width=\"8\" height=\"8\" fill=\"red\" stroke=\"blue\" stroke-width=\"2\" stroke-linecap=\"round\"/></symbol></defs><use href=\"#s\" width=\"8\" height=\"8\"/></svg>");

        ReadOnlyMemory<byte> buttPng = SvgConverter.ConvertToPng(butt, SvgRenderOptions.Default, CancellationToken.None);
        ReadOnlyMemory<byte> roundPng = SvgConverter.ConvertToPng(round, SvgRenderOptions.Default, CancellationToken.None);

        Assert.True(roundPng.Span.SequenceEqual(buttPng.Span));
    }

    [Fact]
    public void Round_caps_and_joins_render_identical_on_circle_strokes()
    {
        byte[] butt = System.Text.Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"64\" height=\"64\"><circle cx=\"32\" cy=\"32\" r=\"20\" fill=\"red\" stroke=\"blue\" stroke-width=\"8\"/></svg>");
        byte[] round = System.Text.Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"64\" height=\"64\"><circle cx=\"32\" cy=\"32\" r=\"20\" fill=\"red\" stroke=\"blue\" stroke-width=\"8\" stroke-linecap=\"round\" stroke-linejoin=\"round\"/></svg>");

        ReadOnlyMemory<byte> buttPng = SvgConverter.ConvertToPng(butt, SvgRenderOptions.Default, CancellationToken.None);
        ReadOnlyMemory<byte> roundPng = SvgConverter.ConvertToPng(round, SvgRenderOptions.Default, CancellationToken.None);

        Assert.True(roundPng.Span.SequenceEqual(buttPng.Span));
    }

    [Fact]
    public void Bevel_joins_render_identical_on_circle_strokes()
    {
        byte[] butt = System.Text.Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"64\" height=\"64\"><circle cx=\"32\" cy=\"32\" r=\"20\" fill=\"red\" stroke=\"blue\" stroke-width=\"8\"/></svg>");
        byte[] bevel = System.Text.Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"64\" height=\"64\"><circle cx=\"32\" cy=\"32\" r=\"20\" fill=\"red\" stroke=\"blue\" stroke-width=\"8\" stroke-linejoin=\"bevel\"/></svg>");

        ReadOnlyMemory<byte> buttPng = SvgConverter.ConvertToPng(butt, SvgRenderOptions.Default, CancellationToken.None);
        ReadOnlyMemory<byte> bevelPng = SvgConverter.ConvertToPng(bevel, SvgRenderOptions.Default, CancellationToken.None);

        Assert.True(bevelPng.Span.SequenceEqual(buttPng.Span));
    }

    [Fact]
    public void Bevel_joins_render_identical_on_ellipse_strokes()
    {
        byte[] butt = System.Text.Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"64\" height=\"64\"><ellipse cx=\"32\" cy=\"32\" rx=\"20\" ry=\"12\" fill=\"red\" stroke=\"blue\" stroke-width=\"8\"/></svg>");
        byte[] bevel = System.Text.Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"64\" height=\"64\"><ellipse cx=\"32\" cy=\"32\" rx=\"20\" ry=\"12\" fill=\"red\" stroke=\"blue\" stroke-width=\"8\" stroke-linejoin=\"bevel\"/></svg>");

        ReadOnlyMemory<byte> buttPng = SvgConverter.ConvertToPng(butt, SvgRenderOptions.Default, CancellationToken.None);
        ReadOnlyMemory<byte> bevelPng = SvgConverter.ConvertToPng(bevel, SvgRenderOptions.Default, CancellationToken.None);

        Assert.True(bevelPng.Span.SequenceEqual(buttPng.Span));
    }

    [Fact]
    public void Round_caps_and_joins_render_identical_on_ellipse_strokes()
    {
        byte[] butt = System.Text.Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"64\" height=\"64\"><ellipse cx=\"32\" cy=\"32\" rx=\"20\" ry=\"12\" fill=\"red\" stroke=\"blue\" stroke-width=\"8\"/></svg>");
        byte[] round = System.Text.Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"64\" height=\"64\"><ellipse cx=\"32\" cy=\"32\" rx=\"20\" ry=\"12\" fill=\"red\" stroke=\"blue\" stroke-width=\"8\" stroke-linecap=\"round\" stroke-linejoin=\"round\"/></svg>");

        ReadOnlyMemory<byte> buttPng = SvgConverter.ConvertToPng(butt, SvgRenderOptions.Default, CancellationToken.None);
        ReadOnlyMemory<byte> roundPng = SvgConverter.ConvertToPng(round, SvgRenderOptions.Default, CancellationToken.None);

        Assert.True(roundPng.Span.SequenceEqual(buttPng.Span));
    }

    [Fact]
    public void Circle_stroke_renders_ring()
    {
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><circle cx='8' cy='8' r='6' fill='red' stroke='blue' stroke-width='2'/></svg>", out _, out _);

        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, 16, 8, 8));
        Assert.Equal(new SvgColor(0, 0, 255, 255), Pixel(pixels, 16, 8, 2));
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(pixels, 16, 8, 0));
    }

    [Fact]
    public void Single_stop_gradient_stroke_matches_solid_stroke()
    {
        byte[] gradient = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><linearGradient id='g'><stop offset='0.3' stop-color='#ff0000'/></linearGradient></defs><rect x='1' y='1' width='6' height='6' fill='none' stroke='url(#g)' stroke-width='2'/></svg>", out _, out _);
        byte[] solid = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><rect x='1' y='1' width='6' height='6' fill='none' stroke='red' stroke-width='2'/></svg>", out _, out _);

        Assert.Equal(solid, gradient);
    }
    [Fact]
    public void Degenerate_gradient_stroke_matches_end_stop_solid_stroke()
    {
        byte[] gradient = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><linearGradient id='g' x1='1' y1='1' x2='1' y2='1'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><rect x='1' y='1' width='6' height='6' fill='none' stroke='url(#g)' stroke-width='2'/></svg>", out _, out _);
        byte[] solid = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><rect x='1' y='1' width='6' height='6' fill='none' stroke='blue' stroke-width='2'/></svg>", out _, out _);

        Assert.Equal(solid, gradient);
    }

    [Fact]
    public void Gradient_fill_with_single_stop_stroke_matches_solid_stroke_pair()
    {
        byte[] gradient = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><linearGradient id='g'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient><linearGradient id='s'><stop offset='0.3' stop-color='#00ff00'/></linearGradient></defs><rect x='1' y='1' width='6' height='6' fill='url(#g)' stroke='url(#s)' stroke-width='2'/></svg>", out _, out _);
        byte[] solid = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><linearGradient id='g'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient><linearGradient id='s'><stop offset='0.3' stop-color='#00ff00'/></linearGradient></defs><rect x='1' y='1' width='6' height='6' fill='url(#g)' stroke='#00ff00' stroke-width='2'/></svg>", out _, out _);

        Assert.Equal(solid, gradient);
    }

    [Fact]
    public void Solid_pattern_stroke_matches_solid_stroke()
    {
        byte[] pattern = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><pattern id='p' width='8' height='8' patternUnits='userSpaceOnUse'><rect width='8' height='8' fill='red'/></pattern></defs><rect x='1' y='1' width='6' height='6' fill='none' stroke='url(#p)' stroke-width='2'/></svg>", out _, out _);
        byte[] solid = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><rect x='1' y='1' width='6' height='6' fill='none' stroke='red' stroke-width='2'/></svg>", out _, out _);

        Assert.Equal(solid, pattern);
    }

    [Fact]
    public void Gradient_fill_with_solid_pattern_stroke_matches_solid_pair()
    {
        byte[] pattern = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><linearGradient id='g'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient><pattern id='p' width='8' height='8' patternUnits='userSpaceOnUse'><rect width='8' height='8' fill='#00ff00'/></pattern></defs><rect x='1' y='1' width='6' height='6' fill='url(#g)' stroke='url(#p)' stroke-width='2'/></svg>", out _, out _);
        byte[] solid = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><linearGradient id='g'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient><pattern id='p' width='8' height='8' patternUnits='userSpaceOnUse'><rect width='8' height='8' fill='#00ff00'/></pattern></defs><rect x='1' y='1' width='6' height='6' fill='url(#g)' stroke='#00ff00' stroke-width='2'/></svg>", out _, out _);

        Assert.Equal(solid, pattern);
    }

    [Fact]
    public void Pattern_fill_with_single_stop_stroke_matches_solid_pair()
    {
        byte[] gradient = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><pattern id='p' width='8' height='8' patternUnits='userSpaceOnUse'><rect width='8' height='8' fill='#ff0000'/></pattern><linearGradient id='s'><stop offset='0.3' stop-color='#00ff00'/></linearGradient></defs><rect x='1' y='1' width='6' height='6' fill='url(#p)' stroke='url(#s)' stroke-width='2'/></svg>", out _, out _);
        byte[] solid = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><pattern id='p' width='8' height='8' patternUnits='userSpaceOnUse'><rect width='8' height='8' fill='#ff0000'/></pattern><linearGradient id='s'><stop offset='0.3' stop-color='#00ff00'/></linearGradient></defs><rect x='1' y='1' width='6' height='6' fill='url(#p)' stroke='#00ff00' stroke-width='2'/></svg>", out _, out _);

        Assert.Equal(solid, gradient);
    }

    [Fact]
    public void Pattern_fill_with_pattern_stroke_matches_solid_pair()
    {
        byte[] pattern = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><pattern id='f' width='8' height='8' patternUnits='userSpaceOnUse'><rect width='8' height='8' fill='#ff0000'/></pattern><pattern id='s' width='8' height='8' patternUnits='userSpaceOnUse'><rect width='8' height='8' fill='#0000ff'/></pattern></defs><rect x='1' y='1' width='6' height='6' fill='url(#f)' stroke='url(#s)' stroke-width='2'/></svg>", out _, out _);
        byte[] solid = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><pattern id='f' width='8' height='8' patternUnits='userSpaceOnUse'><rect width='8' height='8' fill='#ff0000'/></pattern><pattern id='s' width='8' height='8' patternUnits='userSpaceOnUse'><rect width='8' height='8' fill='#0000ff'/></pattern></defs><rect x='1' y='1' width='6' height='6' fill='url(#f)' stroke='#0000ff' stroke-width='2'/></svg>", out _, out _);

        Assert.Equal(solid, pattern);
    }

    [Fact]
    public void Square_cap_line_matches_extended_butt_line()
    {
        byte[] square = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><line x1='2' y1='4' x2='6' y2='4' stroke='red' stroke-width='2' stroke-linecap='square'/></svg>", out _, out _);
        byte[] butt = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><line x1='1' y1='4' x2='7' y2='4' stroke='red' stroke-width='2'/></svg>", out _, out _);

        Assert.Equal(butt, square);
    }

    [Fact]
    public void Path_length_scaled_dash_line_matches_manual_dashes()
    {
        byte[] scaled = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='8'><line x1='0' y1='4' x2='16' y2='4' stroke='red' stroke-width='2' stroke-dasharray='4 2' pathLength='8'/></svg>", out _, out _);
        byte[] manual = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='8'><line x1='0' y1='4' x2='16' y2='4' stroke='red' stroke-width='2' stroke-dasharray='8 4'/></svg>", out _, out _);

        Assert.Equal(manual, scaled);
    }

    [Fact]
    public void Path_length_scaled_gradient_dash_line_matches_manual_dashes()
    {
        byte[] scaled = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='8'><defs><linearGradient id='g' gradientUnits='userSpaceOnUse' x1='0' y1='0' x2='16' y2='0'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><line x1='0' y1='4' x2='16' y2='4' stroke='url(#g)' stroke-width='2' stroke-dasharray='4 2' pathLength='8'/></svg>", out _, out _);
        byte[] manual = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='8'><defs><linearGradient id='g' gradientUnits='userSpaceOnUse' x1='0' y1='0' x2='16' y2='0'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><line x1='0' y1='4' x2='16' y2='4' stroke='url(#g)' stroke-width='2' stroke-dasharray='8 4'/></svg>", out _, out _);

        Assert.Equal(manual, scaled);
    }

    [Fact]
    public void Solid_tile_dashed_pattern_line_matches_dashed_solid_line()
    {
        byte[] pattern = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='8'><defs><pattern id='p' width='8' height='8' patternUnits='userSpaceOnUse'><rect width='8' height='8' fill='red'/></pattern></defs><line x1='0' y1='4' x2='16' y2='4' stroke='url(#p)' stroke-width='2' stroke-dasharray='4 2'/></svg>", out _, out _);
        byte[] solid = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='8'><line x1='0' y1='4' x2='16' y2='4' stroke='red' stroke-width='2' stroke-dasharray='4 2'/></svg>", out _, out _);

        Assert.Equal(solid, pattern);
    }

    [Fact]
    public void Single_stop_dashed_gradient_line_matches_dashed_solid_line()
    {
        byte[] gradient = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='8'><defs><linearGradient id='g' gradientUnits='userSpaceOnUse' x1='0' y1='0' x2='16' y2='0'><stop offset='0.3' stop-color='#ff0000'/></linearGradient></defs><line x1='0' y1='4' x2='16' y2='4' stroke='url(#g)' stroke-width='2' stroke-dasharray='4 2'/></svg>", out _, out _);
        byte[] solid = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='8'><line x1='0' y1='4' x2='16' y2='4' stroke='red' stroke-width='2' stroke-dasharray='4 2'/></svg>", out _, out _);

        Assert.Equal(solid, gradient);
    }

    [Fact]
    public void Single_stop_dashed_radial_line_matches_dashed_solid_line()
    {
        byte[] gradient = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='8'><defs><radialGradient id='g' gradientUnits='userSpaceOnUse' cx='8' cy='4' r='8'><stop offset='0.3' stop-color='#ff0000'/></radialGradient></defs><line x1='0' y1='4' x2='16' y2='4' stroke='url(#g)' stroke-width='2' stroke-dasharray='4 2'/></svg>", out _, out _);
        byte[] solid = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='8'><line x1='0' y1='4' x2='16' y2='4' stroke='red' stroke-width='2' stroke-dasharray='4 2'/></svg>", out _, out _);

        Assert.Equal(solid, gradient);
    }

    [Fact]
    public void Full_dash_line_matches_solid_line()
    {
        byte[] dashed = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='8'><line x1='0' y1='4' x2='16' y2='4' stroke='red' stroke-width='2' stroke-dasharray='16'/></svg>", out _, out _);
        byte[] solid = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='8'><line x1='0' y1='4' x2='16' y2='4' stroke='red' stroke-width='2'/></svg>", out _, out _);

        Assert.Equal(solid, dashed);
    }

    [Fact]
    public void Diagonal_gradient_polygon_paints_coverage()
    {
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><linearGradient id='g'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><polygon points='0,0 8,0 0,8' fill='url(#g)'/></svg>", out _, out _);

        Assert.Equal(255, Pixel(pixels, 8, 0, 0).Alpha);
        Assert.Equal(255, Pixel(pixels, 8, 1, 1).Alpha);
        Assert.Equal(0, Pixel(pixels, 8, 7, 7).Alpha);
        Assert.Equal(0, Pixel(pixels, 8, 4, 4).Alpha);
    }

    [Fact]
    public void Single_stop_diagonal_gradient_matches_solid_fill()
    {
        byte[] gradient = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><linearGradient id='g'><stop offset='0.3' stop-color='#ff0000'/></linearGradient></defs><polygon points='0,0 8,0 0,8' fill='url(#g)'/></svg>", out _, out _);
        byte[] solid = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><polygon points='0,0 8,0 0,8' fill='red'/></svg>", out _, out _);

        Assert.Equal(solid, gradient);
    }

    [Fact]
    public void Gradient_outline_paints_stroke_coverage()
    {
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><linearGradient id='g' gradientUnits='userSpaceOnUse' x1='0' y1='0' x2='8' y2='0'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><polygon points='0,0 8,0 0,8' fill='none' stroke='url(#g)' stroke-width='2'/></svg>", out _, out _);

        Assert.Equal(255, Pixel(pixels, 8, 4, 0).Alpha);
        Assert.Equal(0, Pixel(pixels, 8, 7, 7).Alpha);
    }

    [Fact]
    public void Single_stop_outline_matches_solid_outline()
    {
        byte[] gradient = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><linearGradient id='g' x1='0' y1='0' x2='1' y2='0'><stop offset='0.3' stop-color='#0000ff'/></linearGradient></defs><polygon points='0,0 8,0 0,8' fill='none' stroke='url(#g)' stroke-width='2'/></svg>", out _, out _);
        byte[] solid = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><polygon points='0,0 8,0 0,8' fill='none' stroke='blue' stroke-width='2'/></svg>", out _, out _);

        Assert.Equal(solid, gradient);
    }

    [Fact]
    public void Gradient_outline_on_diagonal_path_paints_coverage()
    {
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><linearGradient id='g' gradientUnits='userSpaceOnUse' x1='0' y1='0' x2='8' y2='0'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><path d='M0,0 L8,0 L0,8 Z' fill='none' stroke='url(#g)' stroke-width='2'/></svg>", out _, out _);

        Assert.Equal(255, Pixel(pixels, 8, 4, 0).Alpha);
        Assert.Equal(0, Pixel(pixels, 8, 7, 7).Alpha);
    }

    [Fact]
    public void Diagonal_radial_polygon_paints_coverage()
    {
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><radialGradient id='g' cx='0.5' cy='0.5' r='0.5'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><polygon points='0,0 8,0 0,8' fill='url(#g)'/></svg>", out _, out _);

        Assert.Equal(255, Pixel(pixels, 8, 0, 0).Alpha);
        Assert.Equal(255, Pixel(pixels, 8, 1, 1).Alpha);
        Assert.Equal(0, Pixel(pixels, 8, 7, 7).Alpha);
        Assert.Equal(0, Pixel(pixels, 8, 4, 4).Alpha);
    }

    [Fact]
    public void Single_stop_diagonal_radial_matches_solid_fill()
    {
        byte[] gradient = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><radialGradient id='g' cx='0.5' cy='0.5' r='0.5'><stop offset='0.3' stop-color='#ff0000'/></radialGradient></defs><polygon points='0,0 8,0 0,8' fill='url(#g)'/></svg>", out _, out _);
        byte[] solid = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><polygon points='0,0 8,0 0,8' fill='red'/></svg>", out _, out _);

        Assert.Equal(solid, gradient);
    }

    [Fact]
    public void Bounding_box_path_outline_matches_userspace_twin()
    {
        byte[] boxed = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><linearGradient id='g' x1='0' y1='0' x2='1' y2='0'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><path d='M0,0 L8,0 L0,8 Z' fill='none' stroke='url(#g)' stroke-width='2'/></svg>", out _, out _);
        byte[] userSpace = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><linearGradient id='g' gradientUnits='userSpaceOnUse' x1='0' y1='0' x2='8' y2='0'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><path d='M0,0 L8,0 L0,8 Z' fill='none' stroke='url(#g)' stroke-width='2'/></svg>", out _, out _);

        Assert.Equal(userSpace, boxed);
    }

    [Fact]
    public void Bounding_box_radial_path_outline_matches_userspace_twin()
    {
        byte[] boxed = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><radialGradient id='g' cx='0.5' cy='0.5' r='0.5'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><path d='M0,0 L8,0 L0,8 Z' fill='none' stroke='url(#g)' stroke-width='2'/></svg>", out _, out _);
        byte[] userSpace = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><radialGradient id='g' gradientUnits='userSpaceOnUse' cx='4' cy='4' r='4'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><path d='M0,0 L8,0 L0,8 Z' fill='none' stroke='url(#g)' stroke-width='2'/></svg>", out _, out _);

        Assert.Equal(userSpace, boxed);
    }

    [Fact]
    public void Bounding_box_curved_path_outline_matches_userspace_twin()
    {
        byte[] boxed = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><linearGradient id='g' x1='0' y1='0' x2='1' y2='0'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><path d='M0,8 Q4,-4 8,8' fill='none' stroke='url(#g)' stroke-width='2'/></svg>", out _, out _);
        byte[] userSpace = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><linearGradient id='g' gradientUnits='userSpaceOnUse' x1='0' y1='0' x2='8' y2='0'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><path d='M0,8 Q4,-4 8,8' fill='none' stroke='url(#g)' stroke-width='2'/></svg>", out _, out _);

        Assert.Equal(userSpace, boxed);
    }

    [Fact]
    public void Bounding_box_fractional_path_outline_matches_userspace_twin()
    {
        byte[] boxed = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><linearGradient id='g' x1='0' y1='0' x2='1' y2='0'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><path d='M0.5,0 L8,0 L0,8 Z' fill='none' stroke='url(#g)' stroke-width='2'/></svg>", out _, out _);
        byte[] userSpace = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><linearGradient id='g' gradientUnits='userSpaceOnUse' x1='0' y1='0' x2='8' y2='0'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><path d='M0.5,0 L8,0 L0,8 Z' fill='none' stroke='url(#g)' stroke-width='2'/></svg>", out _, out _);

        Assert.Equal(userSpace, boxed);
    }
    [Fact]
    public void Fractional_polygon_outline_matches_userspace_twin()
    {
        byte[] boxed = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><linearGradient id='g' x1='0' y1='0' x2='1' y2='0'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><polygon points='0.5,0 8,0 0,8' fill='none' stroke='url(#g)' stroke-width='2'/></svg>", out _, out _);
        byte[] userSpace = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><linearGradient id='g' gradientUnits='userSpaceOnUse' x1='0' y1='0' x2='8' y2='0'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><polygon points='0.5,0 8,0 0,8' fill='none' stroke='url(#g)' stroke-width='2'/></svg>", out _, out _);

        Assert.Equal(userSpace, boxed);
    }

    [Fact]
    public void Single_stop_fractional_outline_matches_solid_outline()
    {
        byte[] gradient = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><linearGradient id='g' x1='0' y1='0' x2='1' y2='0'><stop offset='0.3' stop-color='#0000ff'/></linearGradient></defs><polygon points='0.5,0 8,0 0,8' fill='none' stroke='url(#g)' stroke-width='2'/></svg>", out _, out _);
        byte[] solid = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><polygon points='0.5,0 8,0 0,8' fill='none' stroke='blue' stroke-width='2'/></svg>", out _, out _);

        Assert.Equal(solid, gradient);
    }
    [Fact]
    public void Single_stop_bounding_box_outline_on_path_matches_solid_outline()
    {
        byte[] gradient = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><linearGradient id='g' x1='0' y1='0' x2='1' y2='0'><stop offset='0.3' stop-color='#0000ff'/></linearGradient></defs><path d='M0,0 L8,0 L0,8 Z' fill='none' stroke='url(#g)' stroke-width='2'/></svg>", out _, out _);
        byte[] solid = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><path d='M0,0 L8,0 L0,8 Z' fill='none' stroke='blue' stroke-width='2'/></svg>", out _, out _);

        Assert.Equal(solid, gradient);
    }
    [Fact]
    public void Solid_tile_pattern_outline_matches_solid_outline()
    {
        byte[] pattern = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><pattern id='p' width='8' height='8' patternUnits='userSpaceOnUse'><rect width='8' height='8' fill='red'/></pattern></defs><polygon points='0,0 8,0 0,8' fill='none' stroke='url(#p)' stroke-width='2'/></svg>", out _, out _);
        byte[] solid = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><polygon points='0,0 8,0 0,8' fill='none' stroke='red' stroke-width='2'/></svg>", out _, out _);

        Assert.Equal(solid, pattern);
    }

    [Fact]
    public void Empty_tile_pattern_outline_paints_no_stroke()
    {
        byte[] pattern = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><pattern id='e' width='8' height='8' patternUnits='userSpaceOnUse'></pattern></defs><polygon points='0,0 8,0 0,8' fill='none' stroke='url(#e)' stroke-width='2'/></svg>", out _, out _);
        byte[] bare = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><polygon points='0,0 8,0 0,8' fill='none' stroke='none'/></svg>", out _, out _);

        Assert.Equal(bare, pattern);
    }
    [Fact]
    public void Single_stop_radial_outline_matches_solid_outline()
    {
        byte[] gradient = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><radialGradient id='g' gradientUnits='userSpaceOnUse' cx='4' cy='4' r='4'><stop offset='0.3' stop-color='#0000ff'/></radialGradient></defs><polygon points='0,0 8,0 0,8' fill='none' stroke='url(#g)' stroke-width='2'/></svg>", out _, out _);
        byte[] solid = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><polygon points='0,0 8,0 0,8' fill='none' stroke='blue' stroke-width='2'/></svg>", out _, out _);

        Assert.Equal(solid, gradient);
    }

    [Fact]
    public void White_masked_gradient_outline_matches_unmasked_outline()
    {
        byte[] masked = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><linearGradient id='g' gradientUnits='userSpaceOnUse' x1='0' y1='0' x2='8' y2='0'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient><mask id='m'><rect x='-8' y='-8' width='24' height='24' fill='#ffffff'/></mask></defs><polygon points='0,0 8,0 0,8' fill='none' stroke='url(#g)' stroke-width='2' mask='url(#m)'/></svg>", out _, out _);
        byte[] bare = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><linearGradient id='g' gradientUnits='userSpaceOnUse' x1='0' y1='0' x2='8' y2='0'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><polygon points='0,0 8,0 0,8' fill='none' stroke='url(#g)' stroke-width='2'/></svg>", out _, out _);

        Assert.Equal(bare, masked);
    }

    [Fact]
    public void Gray_masked_group_outline_matches_direct_masked_outline()
    {
        byte[] grouped = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><linearGradient id='g' gradientUnits='userSpaceOnUse' x1='0' y1='0' x2='8' y2='0'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient><mask id='m'><rect x='-8' y='-8' width='24' height='24' fill='#808080'/></mask></defs><g mask='url(#m)'><polygon points='0,0 8,0 0,8' fill='none' stroke='url(#g)' stroke-width='2'/></g></svg>", out _, out _);
        byte[] direct = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><linearGradient id='g' gradientUnits='userSpaceOnUse' x1='0' y1='0' x2='8' y2='0'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient><mask id='m'><rect x='-8' y='-8' width='24' height='24' fill='#808080'/></mask></defs><polygon points='0,0 8,0 0,8' fill='none' stroke='url(#g)' stroke-width='2' mask='url(#m)'/></svg>", out _, out _);

        Assert.Equal(direct, grouped);
    }

    [Fact]
    public void Bounding_box_mask_region_matches_userspace_region()
    {
        byte[] boxed = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><mask id='m' x='0.25' y='0.25' width='0.5' height='0.5'><rect x='0' y='0' width='8' height='8' fill='#808080'/></mask></defs><rect width='8' height='8' fill='red' mask='url(#m)'/></svg>", out _, out _);
        byte[] userSpace = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><mask id='m' maskUnits='userSpaceOnUse' x='2' y='2' width='4' height='4'><rect x='0' y='0' width='8' height='8' fill='#808080'/></mask></defs><rect width='8' height='8' fill='red' mask='url(#m)'/></svg>", out _, out _);

        Assert.Equal(userSpace, boxed);
    }

    [Fact]
    public void Offset_viewport_gradient_fill_matches_direct_fill()
    {
        byte[] nested = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='8'><defs><linearGradient id='g' gradientUnits='userSpaceOnUse' x1='0' y1='0' x2='8' y2='0'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><svg x='8' y='0' width='8' height='8'><rect width='8' height='8' fill='url(#g)'/></svg></svg>", out _, out _);
        byte[] direct = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='8'><defs><linearGradient id='g' gradientUnits='userSpaceOnUse' x1='8' y1='0' x2='16' y2='0'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><rect x='8' y='0' width='8' height='8' fill='url(#g)'/></svg>", out _, out _);

        Assert.Equal(direct, nested);
    }

    [Fact]
    public void Scaled_viewport_gradient_fill_matches_direct_fill()
    {
        byte[] nested = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='8'><defs><linearGradient id='g' gradientUnits='userSpaceOnUse' x1='0' y1='0' x2='8' y2='0'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><svg x='0' y='0' width='16' height='8' viewBox='0 0 8 8' preserveAspectRatio='none'><rect width='8' height='8' fill='url(#g)'/></svg></svg>", out _, out _);
        byte[] direct = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='8'><defs><linearGradient id='g' gradientUnits='userSpaceOnUse' x1='0' y1='0' x2='16' y2='0'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><rect width='16' height='8' fill='url(#g)'/></svg>", out _, out _);

        Assert.Equal(direct, nested);
    }

    [Fact]
    public void Offset_viewport_radial_fill_matches_direct_fill()
    {
        byte[] nested = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='8'><defs><radialGradient id='g' gradientUnits='userSpaceOnUse' cx='4' cy='4' r='4'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><svg x='8' y='0' width='8' height='8'><rect width='8' height='8' fill='url(#g)'/></svg></svg>", out _, out _);
        byte[] direct = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='8'><defs><radialGradient id='g' gradientUnits='userSpaceOnUse' cx='12' cy='4' r='4'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><rect x='8' y='0' width='8' height='8' fill='url(#g)'/></svg>", out _, out _);

        Assert.Equal(direct, nested);
    }

    [Fact]
    public void Inline_style_linecap_overrides_cap_attribute()
    {
        byte[] styled = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><line x1='4' y1='8' x2='12' y2='8' stroke='red' stroke-width='4' stroke-linecap='square' style='stroke-linecap:butt'/></svg>", out _, out _);
        byte[] plain = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><line x1='4' y1='8' x2='12' y2='8' stroke='red' stroke-width='4' stroke-linecap='butt'/></svg>", out _, out _);

        Assert.Equal(plain, styled);
    }

    [Fact]
    public void Inline_style_dasharray_none_overrides_dash_attribute()
    {
        byte[] styled = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><line x1='2' y1='8' x2='14' y2='8' stroke='red' stroke-width='2' stroke-dasharray='4 4' style='stroke-dasharray:none'/></svg>", out _, out _);
        byte[] plain = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><line x1='2' y1='8' x2='14' y2='8' stroke='red' stroke-width='2'/></svg>", out _, out _);

        Assert.Equal(plain, styled);
    }

    [Fact]
    public void Inline_style_dashoffset_overrides_offset_attribute()
    {
        byte[] styled = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><line x1='2' y1='8' x2='14' y2='8' stroke='red' stroke-width='2' stroke-dasharray='4 4' style='stroke-dashoffset:3'/></svg>", out _, out _);
        byte[] plain = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><line x1='2' y1='8' x2='14' y2='8' stroke='red' stroke-width='2' stroke-dasharray='4 4' stroke-dashoffset='3'/></svg>", out _, out _);

        Assert.Equal(plain, styled);
    }
    [Fact]
    public void Stylesheet_dasharray_matches_dash_attribute()
    {
        byte[] styled = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><style>.dsw { stroke-dasharray: 4 4; }</style><line x1='2' y1='8' x2='14' y2='8' stroke='red' stroke-width='2' class='dsw'/></svg>", out _, out _);
        byte[] plain = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><line x1='2' y1='8' x2='14' y2='8' stroke='red' stroke-width='2' stroke-dasharray='4 4'/></svg>", out _, out _);

        Assert.Equal(plain, styled);
    }

    [Fact]
    public void Important_inline_dasharray_overrides_dash_attribute()
    {
        byte[] styled = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><line x1='2' y1='8' x2='14' y2='8' stroke='red' stroke-width='2' stroke-dasharray='none' style='stroke-dasharray:4 4 !important'/></svg>", out _, out _);
        byte[] plain = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><line x1='2' y1='8' x2='14' y2='8' stroke='red' stroke-width='2' stroke-dasharray='4 4'/></svg>", out _, out _);

        Assert.Equal(plain, styled);
    }

    [Fact]
    public void Group_dasharray_is_inherited_by_line()
    {
        byte[] grouped = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><g stroke-dasharray='4 4'><line x1='2' y1='8' x2='14' y2='8' stroke='red' stroke-width='2'/></g></svg>", out _, out _);
        byte[] plain = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><line x1='2' y1='8' x2='14' y2='8' stroke='red' stroke-width='2' stroke-dasharray='4 4'/></svg>", out _, out _);

        Assert.Equal(plain, grouped);
    }

    [Fact]
    public void Use_instance_dash_matches_direct_dash()
    {
        byte[] instanced = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><defs><line id='l' x1='2' y1='8' x2='14' y2='8' stroke='red' stroke-width='2' stroke-dasharray='4 4'/></defs><use href='#l'/></svg>", out _, out _);
        byte[] plain = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><line x1='2' y1='8' x2='14' y2='8' stroke='red' stroke-width='2' stroke-dasharray='4 4'/></svg>", out _, out _);

        Assert.Equal(plain, instanced);
    }

    [Fact]
    public void OutputScale_dashed_line_matches_scaled_pattern_direct_rendering()
    {
        byte[] small = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='64' height='64'><line x1='8' y1='32' x2='56' y2='32' stroke='red' stroke-width='4' stroke-dasharray='8 4'/></svg>");
        byte[] large = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='128' height='128'><line x1='16' y1='64' x2='112' y2='64' stroke='red' stroke-width='8' stroke-dasharray='16 8'/></svg>");
        SvgRenderOptions scaled = new(null, 2, SvgColor.Transparent, SvgRenderLimits.Default);
        SvgRenderOptions direct = new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default);
        ReadOnlyMemory<byte> scaledPng = SvgConverter.ConvertToPng(small, scaled, CancellationToken.None);
        ReadOnlyMemory<byte> directPng = SvgConverter.ConvertToPng(large, direct, CancellationToken.None);

        Assert.True(scaledPng.Span.SequenceEqual(directPng.Span));
    }

    [Fact]
    public void OutputScale_dash_offset_matches_scaled_direct_rendering()
    {
        byte[] small = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='64' height='64'><line x1='8' y1='32' x2='56' y2='32' stroke='red' stroke-width='4' stroke-dasharray='8 4' stroke-dashoffset='3'/></svg>");
        byte[] large = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='128' height='128'><line x1='16' y1='64' x2='112' y2='64' stroke='red' stroke-width='8' stroke-dasharray='16 8' stroke-dashoffset='6'/></svg>");
        SvgRenderOptions scaled = new(null, 2, SvgColor.Transparent, SvgRenderLimits.Default);
        SvgRenderOptions direct = new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default);
        ReadOnlyMemory<byte> scaledPng = SvgConverter.ConvertToPng(small, scaled, CancellationToken.None);
        ReadOnlyMemory<byte> directPng = SvgConverter.ConvertToPng(large, direct, CancellationToken.None);

        Assert.True(scaledPng.Span.SequenceEqual(directPng.Span));
    }

    [Fact]
    public void OutputScale_dashed_polygon_matches_scaled_pattern_direct_rendering()
    {
        byte[] small = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='64' height='64'><polygon points='8,56 32,8 56,56' fill='none' stroke='red' stroke-width='4' stroke-dasharray='8 4'/></svg>");
        byte[] large = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='128' height='128'><polygon points='16,112 64,16 112,112' fill='none' stroke='red' stroke-width='8' stroke-dasharray='16 8'/></svg>");
        SvgRenderOptions scaled = new(null, 2, SvgColor.Transparent, SvgRenderLimits.Default);
        SvgRenderOptions direct = new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default);
        ReadOnlyMemory<byte> scaledPng = SvgConverter.ConvertToPng(small, scaled, CancellationToken.None);
        ReadOnlyMemory<byte> directPng = SvgConverter.ConvertToPng(large, direct, CancellationToken.None);

        Assert.True(scaledPng.Span.SequenceEqual(directPng.Span));
    }
    [Fact]
    public void Group_linecap_inherits_to_line()
    {
        byte[] grouped = RenderPixels("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"16\"><g stroke-linecap=\"round\"><line x1=\"2\" y1=\"8\" x2=\"14\" y2=\"8\" stroke=\"red\" stroke-width=\"2\"/></g></svg>", out _, out _);
        byte[] direct = RenderPixels("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"16\"><line x1=\"2\" y1=\"8\" x2=\"14\" y2=\"8\" stroke=\"red\" stroke-width=\"2\" stroke-linecap=\"round\"/></svg>", out _, out _);

        Assert.Equal(direct, grouped);
    }
    [Fact]
    public void Inline_style_linecap_matches_direct_attribute()
    {
        byte[] styled = RenderPixels("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"16\"><line x1=\"2\" y1=\"8\" x2=\"14\" y2=\"8\" stroke=\"red\" stroke-width=\"2\" style=\"stroke-linecap:round\"/></svg>", out _, out _);
        byte[] direct = RenderPixels("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"16\"><line x1=\"2\" y1=\"8\" x2=\"14\" y2=\"8\" stroke=\"red\" stroke-width=\"2\" stroke-linecap=\"round\"/></svg>", out _, out _);

        Assert.Equal(direct, styled);
    }
    [Fact]
    public void Stylesheet_class_linecap_applies_to_line()
    {
        byte[] styled = RenderPixels("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"16\"><style>.c { stroke-linecap: round; }</style><line class=\"c\" x1=\"2\" y1=\"8\" x2=\"14\" y2=\"8\" stroke=\"red\" stroke-width=\"2\"/></svg>", out _, out _);
        byte[] direct = RenderPixels("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"16\"><line x1=\"2\" y1=\"8\" x2=\"14\" y2=\"8\" stroke=\"red\" stroke-width=\"2\" stroke-linecap=\"round\"/></svg>", out _, out _);

        Assert.Equal(direct, styled);
    }
    [Fact]
    public void Root_attribute_linecap_inherits_to_line()
    {
        byte[] grouped = RenderPixels("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"16\" stroke-linecap=\"round\"><line x1=\"2\" y1=\"8\" x2=\"14\" y2=\"8\" stroke=\"red\" stroke-width=\"2\"/></svg>", out _, out _);
        byte[] direct = RenderPixels("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"16\"><line x1=\"2\" y1=\"8\" x2=\"14\" y2=\"8\" stroke=\"red\" stroke-width=\"2\" stroke-linecap=\"round\"/></svg>", out _, out _);

        Assert.Equal(direct, grouped);
    }
    [Fact]
    public void Group_linejoin_inherits_to_path()
    {
        byte[] grouped = RenderPixels("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"16\"><g stroke-linejoin=\"round\"><path d=\"M2,2 L14,2 L14,14\" fill=\"none\" stroke=\"red\" stroke-width=\"2\"/></g></svg>", out _, out _);
        byte[] direct = RenderPixels("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"16\"><path d=\"M2,2 L14,2 L14,14\" fill=\"none\" stroke=\"red\" stroke-width=\"2\" stroke-linejoin=\"round\"/></svg>", out _, out _);

        Assert.Equal(direct, grouped);
    }
    [Fact]
    public void Invalid_inline_linecap_still_rejects()
    {
        SvgException exception = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(System.Text.Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"16\"><line x1=\"2\" y1=\"8\" x2=\"14\" y2=\"8\" stroke=\"red\" stroke-width=\"2\" style=\"stroke-linecap:foo\"/></svg>"), SvgRenderOptions.Default, CancellationToken.None));

        Assert.Contains("stroke linecaps", exception.Message);
    }
    [Fact]
    public void Rect_inline_width_without_stroke_renders_fill_only()
    {
        byte[] styled = RenderPixels("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"16\"><rect x=\"2\" y=\"2\" width=\"12\" height=\"12\" fill=\"red\" style=\"stroke-width:2\"/></svg>", out _, out _);
        byte[] plain = RenderPixels("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"16\"><rect x=\"2\" y=\"2\" width=\"12\" height=\"12\" fill=\"red\"/></svg>", out _, out _);

        Assert.Equal(plain, styled);
    }
    [Fact]
    public void Circle_attribute_width_without_stroke_renders_fill_only()
    {
        byte[] styled = RenderPixels("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"16\"><circle cx=\"8\" cy=\"8\" r=\"5\" fill=\"red\" stroke-width=\"2\"/></svg>", out _, out _);
        byte[] plain = RenderPixels("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"16\"><circle cx=\"8\" cy=\"8\" r=\"5\" fill=\"red\"/></svg>", out _, out _);

        Assert.Equal(plain, styled);
    }
    [Fact]
    public void Group_paint_with_leaf_width_strokes_rect()
    {
        byte[] grouped = RenderPixels("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"16\"><g stroke=\"blue\"><rect x=\"2\" y=\"2\" width=\"12\" height=\"12\" fill=\"red\" stroke-width=\"2\"/></g></svg>", out _, out _);
        byte[] direct = RenderPixels("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"16\"><rect x=\"2\" y=\"2\" width=\"12\" height=\"12\" fill=\"red\" stroke=\"blue\" stroke-width=\"2\"/></svg>", out _, out _);

        Assert.Equal(direct, grouped);
    }
    [Fact]
    public void Group_paint_with_leaf_width_strokes_circle()
    {
        byte[] grouped = RenderPixels("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"16\"><g stroke=\"blue\"><circle cx=\"8\" cy=\"8\" r=\"5\" fill=\"red\" stroke-width=\"2\"/></g></svg>", out _, out _);
        byte[] direct = RenderPixels("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"16\"><circle cx=\"8\" cy=\"8\" r=\"5\" fill=\"red\" stroke=\"blue\" stroke-width=\"2\"/></svg>", out _, out _);

        Assert.Equal(direct, grouped);
    }
    [Fact]
    public void Relative_moveto_offsets_from_open_subpath_end()
    {
        byte[] relative = RenderPixels("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"200\" height=\"200\"><path d=\"M8,36h184m0,64H8\" stroke=\"#036\" stroke-width=\"2\"/></svg>", out _, out _);
        byte[] absolute = RenderPixels("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"200\" height=\"200\"><path d=\"M8,36h184M192,100H8\" stroke=\"#036\" stroke-width=\"2\"/></svg>", out _, out _);

        Assert.Equal(absolute, relative);
    }
    [Fact]
    public void Bare_moveto_subpath_stays_unsupported()
    {
        SvgException exception = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(System.Text.Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"200\" height=\"200\"><path d=\"M192,100H8M0,0\" stroke=\"#036\" stroke-width=\"2\"/></svg>"), SvgRenderOptions.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, exception.ErrorCode);
    }
    [Fact]
    public void Fractional_path_gradient_fill_matches_solid_coverage()
    {
        byte[] painted = RenderPixels("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"16\"><defs><linearGradient id=\"g\" x1=\"0\" y1=\"0\" x2=\"1\" y2=\"0\"><stop offset=\"0\" stop-color=\"red\"/><stop offset=\"1\" stop-color=\"blue\"/></linearGradient></defs><path d=\"M0,0H4.5H8V8H0Z\" fill=\"url(#g)\"/></svg>", out _, out _);
        byte[] solid = RenderPixels("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"16\"><path d=\"M0,0H4.5H8V8H0Z\" fill=\"red\"/></svg>", out _, out _);
        byte[] solidAlpha = new byte[solid.Length / 4];
        byte[] paintedAlpha = new byte[painted.Length / 4];
        for (int i = 0; i < solidAlpha.Length; i++)
        {
            solidAlpha[i] = solid[i * 4 + 3];
            paintedAlpha[i] = painted[i * 4 + 3];
        }
        Assert.Equal(solidAlpha, paintedAlpha);
    }
    [Fact]
    public void Curved_path_gradient_fill_matches_solid_coverage()
    {
        byte[] painted = RenderPixels("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"16\"><defs><linearGradient id=\"g\" x1=\"0\" y1=\"0\" x2=\"1\" y2=\"0\"><stop offset=\"0\" stop-color=\"red\"/><stop offset=\"1\" stop-color=\"blue\"/></linearGradient></defs><path d=\"M2,2 Q8,0 14,2 L14,14 L2,14 Z\" fill=\"url(#g)\"/></svg>", out _, out _);
        byte[] solid = RenderPixels("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"16\"><path d=\"M2,2 Q8,0 14,2 L14,14 L2,14 Z\" fill=\"red\"/></svg>", out _, out _);
        byte[] solidAlpha = new byte[solid.Length / 4];
        byte[] paintedAlpha = new byte[painted.Length / 4];
        for (int i = 0; i < solidAlpha.Length; i++)
        {
            solidAlpha[i] = solid[i * 4 + 3];
            paintedAlpha[i] = painted[i * 4 + 3];
        }
        Assert.Equal(solidAlpha, paintedAlpha);
    }
    [Fact]
    public void Single_stop_fractional_gradient_matches_solid_fill()
    {
        byte[] painted = RenderPixels("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"16\"><defs><linearGradient id=\"g\" x1=\"0\" y1=\"0\" x2=\"1\" y2=\"0\"><stop offset=\"0\" stop-color=\"blue\"/></linearGradient></defs><path d=\"M0,0H4.5H8V8H0Z\" fill=\"url(#g)\"/></svg>", out _, out _);
        byte[] solid = RenderPixels("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"16\"><path d=\"M0,0H4.5H8V8H0Z\" fill=\"blue\"/></svg>", out _, out _);

        Assert.Equal(solid, painted);
    }
    [Fact]
    public void Degenerate_nonpad_fractional_gradient_stays_unsupported()
    {
        SvgException exception = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(System.Text.Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"16\"><defs><linearGradient id=\"g\" x1=\"0\" y1=\"0\" x2=\"0\" y2=\"0\" spreadMethod=\"reflect\"><stop offset=\"0\" stop-color=\"red\"/><stop offset=\"1\" stop-color=\"blue\"/></linearGradient></defs><path d=\"M0,0H4.5H8V8H0Z\" fill=\"url(#g)\"/></svg>"), SvgRenderOptions.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, exception.ErrorCode);
    }
    [Fact]
    public void Sign_abutted_coordinates_match_spaced_twin()
    {
        byte[] abutted = RenderPixels("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"64\" height=\"64\"><path d=\"M48,40l8-8L56,48Z\" fill=\"red\"/></svg>", out _, out _);
        byte[] spaced = RenderPixels("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"64\" height=\"64\"><path d=\"M48,40l8 -8L56,48Z\" fill=\"red\"/></svg>", out _, out _);

        Assert.Equal(spaced, abutted);
    }
    [Fact]
    public void Sign_abutted_coordinates_match_spaced_twin_after_relative_moveto()
    {
        byte[] abutted = RenderPixels("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"64\" height=\"64\"><path d=\"m48,40l8-8L56,48Z\" fill=\"red\"/></svg>", out _, out _);
        byte[] spaced = RenderPixels("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"64\" height=\"64\"><path d=\"m48,40l8 -8L56,48Z\" fill=\"red\"/></svg>", out _, out _);

        Assert.Equal(spaced, abutted);
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
}

