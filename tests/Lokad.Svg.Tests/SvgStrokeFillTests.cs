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
    public void Duplicate_straight_strokes_union_to_single_coverage()
    {
        // Bitwise-identical outline loops merge instead of over-painting shared fringe.
        byte[] single = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><path d='M2,2 H14 V14 H2 Z' fill='none' stroke='blue' stroke-width='2'/></svg>", out _, out _);
        byte[] duplicate = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><path d='M2,2 H14 V14 H2 Z M2,2 H14 V14 H2 Z' fill='none' stroke='blue' stroke-width='2'/></svg>", out _, out _);

        Assert.Equal(AlphaSum(single), AlphaSum(duplicate));
        Assert.Equal(24480, AlphaSum(duplicate));
        Assert.Equal(new SvgColor(0, 0, 255, 255), Pixel(duplicate, 16, 2, 2));
    }

    [Fact]
    public void Contained_stroke_rings_survive_crossing_union()
    {
        // Native directed outlines preserve both rings and the crossing band.
        // Stable browser captures paint these witnesses opaque; one other
        // fringe pixel remains different in the complete reference comparison.
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><path d='M1,1 H15 V15 H1 Z M6,6 H10 V10 H6 Z M1,15 L15,1' fill='none' stroke='blue' stroke-width='2'/></svg>", out _, out _);

        Assert.Equal(new SvgColor(0, 0, 255, 255), Pixel(pixels, 16, 6, 6));
        Assert.Equal(new SvgColor(0, 0, 255, 255), Pixel(pixels, 16, 8, 8));
    }

    [Fact]
    public void Overlapping_stroke_rings_union_without_overlap()
    {
        // Sibling-subpath bands separate per subpath and combine by winding, so each band survives the other ring's hole; the pinned oracle paints the full overlap zone opaque (research-twin-stroke-rings-16x16 graduates exact).
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><path d='M1,1 H9 V9 H1 Z M5,5 H13 V13 H5 Z' fill='none' stroke='blue' stroke-width='2'/></svg>", out _, out _);

        Assert.Equal(new SvgColor(0, 0, 255, 255), Pixel(pixels, 16, 1, 1));
        Assert.Equal(new SvgColor(0, 0, 255, 255), Pixel(pixels, 16, 5, 1));
        Assert.Equal(new SvgColor(0, 0, 255, 255), Pixel(pixels, 16, 5, 6));
        Assert.Equal(new SvgColor(0, 0, 255, 255), Pixel(pixels, 16, 9, 9));
        Assert.Equal(30600, AlphaSum(pixels));
    }

    [Fact]
    public void Sibling_subpath_strokes_union_like_separate_elements()
    {
        // On integer geometry the single-path twin rings render byte-identical to two separately painted ring elements, locking band-union semantics at the API level.
        byte[] single = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><path d='M1,1 H9 V9 H1 Z M5,5 H13 V13 H5 Z' fill='none' stroke='blue' stroke-width='2'/></svg>", out _, out _);
        byte[] split = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><path d='M1,1 H9 V9 H1 Z' fill='none' stroke='blue' stroke-width='2'/><path d='M5,5 H13 V13 H5 Z' fill='none' stroke='blue' stroke-width='2'/></svg>", out _, out _);

        Assert.Equal(split, single);
    }

    [Fact]
    public void Self_crossing_star_stroke_renders_without_denial()
    {
        // Clean multi-loop outlines pass through separation with roles only.
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'><path d='M16,2 L20,12 L30,12 L22,19 L25,29 L16,23 L7,29 L10,19 L2,12 L12,12 Z' fill='none' stroke='red' stroke-width='2'/></svg>", out _, out _);

        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, 32, 16, 2));
        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, 32, 25, 12));
        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, 32, 16, 23));
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(pixels, 32, 16, 16));
        // The complete original star reference remains a known-difference
        // fixture; this test checks band/counter semantics and admission.
    }

    [Fact]
    public void Curved_stroke_matches_native_offset_coverage()
    {
        // The original quadratic's offset outline matches the complete pinned
        // browser reference, including its partially covered inner edge.
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><path d='M2,12 Q8,2 14,12' fill='none' stroke='red' stroke-width='2'/></svg>", out _, out _);

        Assert.Equal(8461, AlphaSum(pixels));
        Assert.Equal(new SvgColor(255, 0, 0, 198), Pixel(pixels, 16, 6, 6));
        Assert.Equal(new SvgColor(255, 0, 0, 24), Pixel(pixels, 16, 8, 8));
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
        // Rectangle stroke bands are butt-jointed without overlap, so a
        // translucent stroke composes exactly like element opacity on
        // fill-less strokes.
        byte[] strokeOpacity = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><rect x='0' y='0' width='8' height='8' fill='none' stroke='black' stroke-width='2' stroke-opacity='0.5'/></svg>", out _, out _);
        byte[] elementOpacity = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><rect x='0' y='0' width='8' height='8' fill='none' stroke='black' stroke-width='2' opacity='0.5'/></svg>", out _, out _);

        Assert.Equal(elementOpacity, strokeOpacity);
    }

    [Fact]
    public void Stroke_opacity_on_butt_line_matches_element_opacity()
    {
        // Butt-capped line bands never overlap, so stroke opacity
        // composes exactly like element opacity.
        byte[] strokeOpacity = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='8'><line x1='0' y1='4' x2='16' y2='4' stroke='red' stroke-width='2' stroke-opacity='0.5'/></svg>", out _, out _);
        byte[] elementOpacity = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='8'><line x1='0' y1='4' x2='16' y2='4' stroke='red' stroke-width='2' opacity='0.5'/></svg>", out _, out _);

        Assert.Equal(elementOpacity, strokeOpacity);
    }

    [Fact]
    public void Stroke_opacity_on_polygon_outline_matches_element_opacity()
    {
        // Polygon outline fills inherit the same single-coverage paint,
        // so both opacity spellings coincide byte-identically.
        byte[] strokeOpacity = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='8'><polygon points='0,0 16,0 16,8 0,8' fill='none' stroke='red' stroke-width='2' stroke-opacity='0.5'/></svg>", out _, out _);
        byte[] elementOpacity = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='8'><polygon points='0,0 16,0 16,8 0,8' fill='none' stroke='red' stroke-width='2' opacity='0.5'/></svg>", out _, out _);

        Assert.Equal(elementOpacity, strokeOpacity);
    }

    [Fact]
    public void Stroke_opacity_on_path_outline_matches_element_opacity()
    {
        // Path outline fills share the polygon machinery, so both opacity
        // spellings coincide byte-identically here as well.
        byte[] strokeOpacity = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='8'><path d='M0,0 H16 V8 H0 Z' fill='none' stroke='red' stroke-width='2' stroke-opacity='0.5'/></svg>", out _, out _);
        byte[] elementOpacity = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='8'><path d='M0,0 H16 V8 H0 Z' fill='none' stroke='red' stroke-width='2' opacity='0.5'/></svg>", out _, out _);

        Assert.Equal(elementOpacity, strokeOpacity);
    }

    [Fact]
    public void Stroke_opacity_on_circle_ring_matches_element_opacity()
    {
        // Opposite-wound ring fills cover each pixel once, so stroke
        // opacity matches element opacity exactly; round caps with
        // translucency stay honestly gated by Rejects_translucent_round_capped_lines.
        byte[] strokeOpacity = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='8'><circle cx='8' cy='4' r='3' fill='none' stroke='red' stroke-width='2' stroke-opacity='0.5'/></svg>", out _, out _);
        byte[] elementOpacity = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='8'><circle cx='8' cy='4' r='3' fill='none' stroke='red' stroke-width='2' opacity='0.5'/></svg>", out _, out _);

        Assert.Equal(elementOpacity, strokeOpacity);
    }

    [Fact]
    public void Stroke_opacity_on_filled_rect_paints_blended_band()
    {
        // Translucent bands composite once over the fill with no corner
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

        byte[] expected = SvgConverter.ConvertToPng("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'/>"u8.ToArray(), SvgRenderOptions.Default, CancellationToken.None).ToArray();
        Assert.Equal(expected, SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None).ToArray());
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

    [Fact]
    public void Odd_integer_stroke_widths_paint()
    {
        // The shared outline kernel maps half-integer offsets exactly like fractional geometry.
        byte[] attribute = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><polygon points='0,0 8,0 8,8 0,8' fill='none' stroke='red' stroke-width='3'/></svg>", out _, out _);
        byte[] styled = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><polygon points='0,0 8,0 8,8 0,8' fill='none' stroke='red' style='stroke-width:3'/></svg>", out _, out _);

        Assert.Equal(attribute, styled);
        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(attribute, 8, 0, 0));
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(attribute, 8, 4, 4));
    }

    [Fact]
    public void Negative_stroke_width_stays_unsupported()
    {
        SvgException rejected = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><polygon points='0,0 8,0 8,8 0,8' fill='none' stroke='red' stroke-width='-1'/></svg>"), SvgRenderOptions.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, rejected.ErrorCode);
    }

    [Fact]
    public void Zero_path_stroke_paints_nothing()
    {
        // A zero width disables the stroke, so the path paints nothing like the pinned oracle (fully transparent 16x16 triple).
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><path d='M2,8 H14' fill='none' stroke='red' stroke-width='0'/></svg>", out _, out _);

        Assert.Equal(0, AlphaSum(pixels));
    }

    [Fact]
    public void Zero_closed_strokes_paint_nothing()
    {
        // Every solid spelling paints nothing at zero width, matching the path oracle by the same empty-stroke rule.
        byte[] polygon = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><polygon points='0,0 8,0 8,8 0,8' fill='none' stroke='red' stroke-width='0'/></svg>", out _, out _);
        byte[] polyline = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><polyline points='0,0 8,0 8,8' fill='none' stroke='red' stroke-width='0'/></svg>", out _, out _);
        byte[] rect = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><rect x='1' y='1' width='6' height='6' fill='none' stroke='red' stroke-width='0'/></svg>", out _, out _);
        byte[] circle = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><circle cx='4' cy='4' r='3' fill='none' stroke='red' stroke-width='0'/></svg>", out _, out _);
        byte[] ellipse = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><ellipse cx='4' cy='4' rx='3' ry='2' fill='none' stroke='red' stroke-width='0'/></svg>", out _, out _);
        byte[] line = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><line x1='0' y1='4' x2='8' y2='4' stroke='red' stroke-width='0'/></svg>", out _, out _);

        Assert.Equal(0, AlphaSum(polygon));
        Assert.Equal(0, AlphaSum(polyline));
        Assert.Equal(0, AlphaSum(rect));
        Assert.Equal(0, AlphaSum(circle));
        Assert.Equal(0, AlphaSum(ellipse));
        Assert.Equal(0, AlphaSum(line));
    }

    [Fact]
    public void Zero_width_wins_over_dashes()
    {
        // Zero disables the stroke before dash handling, so a dashed zero-width path paints nothing.
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><path d='M0,4 H8' fill='none' stroke='red' stroke-width='0' stroke-dasharray='2 2'/></svg>", out _, out _);

        Assert.Equal(0, AlphaSum(pixels));
    }

    [Fact]
    public void Zero_gradient_path_stroke_paints_fill_only()
    {
        // A zero width disables paint-server strokes exactly like solid ones, so the fill renders alone like its stroke-none twin.
        byte[] painted = RenderPixels("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"16\"><defs><linearGradient id=\"g\"><stop offset=\"0\" stop-color=\"blue\"/><stop offset=\"1\" stop-color=\"green\"/></linearGradient></defs><path d=\"M2,2 H14 V14 H2 Z\" fill=\"red\" stroke=\"url(#g)\" stroke-width=\"0\"/></svg>", out _, out _);
        byte[] twin = RenderPixels("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"16\"><path d=\"M2,2 H14 V14 H2 Z\" fill=\"red\" stroke=\"none\"/></svg>", out int width, out _);

        Assert.Equal(twin, painted);
        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(painted, width, 8, 8));
    }

    [Fact]
    public void Zero_paint_strokes_paint_fill_only_across_spellings()
    {
        // Every paint-server spelling paints only its fill at zero width, matching stroke-none or solid-zero twins by the shared empty-stroke rule.
        string defs = "<defs><linearGradient id=\"g\"><stop offset=\"0\" stop-color=\"blue\"/><stop offset=\"1\" stop-color=\"green\"/></linearGradient></defs>";
        byte[] polygon = RenderPixels("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"16\">" + defs + "<polygon points=\"2,2 14,2 14,14 2,14\" fill=\"red\" stroke=\"url(#g)\" stroke-width=\"0\"/></svg>", out _, out _);
        byte[] polygonTwin = RenderPixels("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"16\"><polygon points=\"2,2 14,2 14,14 2,14\" fill=\"red\" stroke=\"none\"/></svg>", out _, out _);
        byte[] polyline = RenderPixels("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"16\">" + defs + "<polyline points=\"2,2 14,2 14,14\" fill=\"none\" stroke=\"url(#g)\" stroke-width=\"0\"/></svg>", out _, out _);
        byte[] polylineTwin = RenderPixels("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"16\"><polyline points=\"2,2 14,2 14,14\" fill=\"none\" stroke=\"red\" stroke-width=\"0\"/></svg>", out _, out _);
        byte[] rect = RenderPixels("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"16\">" + defs + "<rect x=\"2\" y=\"2\" width=\"12\" height=\"12\" fill=\"red\" stroke=\"url(#g)\" stroke-width=\"0\"/></svg>", out _, out _);
        byte[] rectTwin = RenderPixels("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"16\"><rect x=\"2\" y=\"2\" width=\"12\" height=\"12\" fill=\"red\" stroke=\"none\"/></svg>", out _, out _);
        byte[] circle = RenderPixels("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"16\">" + defs + "<circle cx=\"8\" cy=\"8\" r=\"6\" fill=\"red\" stroke=\"url(#g)\" stroke-width=\"0\"/></svg>", out _, out _);
        byte[] circleTwin = RenderPixels("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"16\"><circle cx=\"8\" cy=\"8\" r=\"6\" fill=\"red\" stroke=\"none\"/></svg>", out _, out _);
        byte[] ellipse = RenderPixels("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"16\">" + defs + "<ellipse cx=\"8\" cy=\"8\" rx=\"6\" ry=\"4\" fill=\"red\" stroke=\"url(#g)\" stroke-width=\"0\"/></svg>", out _, out _);
        byte[] ellipseTwin = RenderPixels("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"16\"><ellipse cx=\"8\" cy=\"8\" rx=\"6\" ry=\"4\" fill=\"red\" stroke=\"none\"/></svg>", out _, out _);
        byte[] line = RenderPixels("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"16\">" + defs + "<line x1=\"2\" y1=\"8\" x2=\"14\" y2=\"8\" stroke=\"url(#g)\" stroke-width=\"0\"/></svg>", out _, out _);
        byte[] lineTwin = RenderPixels("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"16\"><line x1=\"2\" y1=\"8\" x2=\"14\" y2=\"8\" stroke=\"red\" stroke-width=\"0\"/></svg>", out _, out _);

        Assert.Equal(polygonTwin, polygon);
        Assert.Equal(polylineTwin, polyline);
        Assert.Equal(rectTwin, rect);
        Assert.Equal(circleTwin, circle);
        Assert.Equal(ellipseTwin, ellipse);
        Assert.Equal(lineTwin, line);
    }

    [Fact]
    public void Zero_radial_and_pattern_strokes_paint_fill_only()
    {
        // Radial and pattern strokes share the gradient zero-width path, so each spelling matches its solid-zero or stroke-none twin.
        string radial = "<defs><radialGradient id=\"r\"><stop offset=\"0\" stop-color=\"blue\"/><stop offset=\"1\" stop-color=\"green\"/></radialGradient></defs>";
        string pattern = "<defs><pattern id=\"p\" width=\"8\" height=\"8\" patternUnits=\"userSpaceOnUse\"><rect x=\"0\" y=\"0\" width=\"8\" height=\"4\" fill=\"#00ff00\"/></pattern></defs>";
        byte[] radialPath = RenderPixels("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"16\">" + radial + "<path d=\"M2,2 H14 V14 H2 Z\" fill=\"red\" stroke=\"url(#r)\" stroke-width=\"0\"/></svg>", out _, out _);
        byte[] patternPath = RenderPixels("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"16\">" + pattern + "<path d=\"M2,2 H14 V14 H2 Z\" fill=\"red\" stroke=\"url(#p)\" stroke-width=\"0\"/></svg>", out _, out _);
        byte[] solidTwin = RenderPixels("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"16\"><path d=\"M2,2 H14 V14 H2 Z\" fill=\"red\" stroke=\"red\" stroke-width=\"0\"/></svg>", out _, out _);
        byte[] radialLine = RenderPixels("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"16\">" + radial + "<line x1=\"2\" y1=\"8\" x2=\"14\" y2=\"8\" stroke=\"url(#r)\" stroke-width=\"0\"/></svg>", out _, out _);
        byte[] patternLine = RenderPixels("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"16\">" + pattern + "<line x1=\"2\" y1=\"8\" x2=\"14\" y2=\"8\" stroke=\"url(#p)\" stroke-width=\"0\"/></svg>", out _, out _);
        byte[] emptyTwin = RenderPixels("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"16\"><line x1=\"2\" y1=\"8\" x2=\"14\" y2=\"8\" stroke=\"red\" stroke-width=\"0\"/></svg>", out _, out _);
        byte[] radialRect = RenderPixels("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"16\">" + radial + "<rect x=\"2\" y=\"2\" width=\"12\" height=\"12\" fill=\"red\" stroke=\"url(#r)\" stroke-width=\"0\"/></svg>", out _, out _);
        byte[] patternRect = RenderPixels("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"16\">" + pattern + "<rect x=\"2\" y=\"2\" width=\"12\" height=\"12\" fill=\"red\" stroke=\"url(#p)\" stroke-width=\"0\"/></svg>", out _, out _);
        byte[] fillTwin = RenderPixels("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"16\"><rect x=\"2\" y=\"2\" width=\"12\" height=\"12\" fill=\"red\" stroke=\"none\"/></svg>", out _, out _);

        Assert.Equal(solidTwin, radialPath);
        Assert.Equal(solidTwin, patternPath);
        Assert.Equal(emptyTwin, radialLine);
        Assert.Equal(emptyTwin, patternLine);
        Assert.Equal(fillTwin, radialRect);
        Assert.Equal(fillTwin, patternRect);
    }

    [Fact]
    public void Zero_paint_width_wins_over_dashes()
    {
        // Zero disables paint-server strokes before dash handling on every kernel, so dashed zero-width paints leave nothing behind.
        byte[] path = RenderPixels("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"8\" height=\"8\"><defs><linearGradient id=\"g\"><stop offset=\"0\" stop-color=\"blue\"/><stop offset=\"1\" stop-color=\"green\"/></linearGradient></defs><path d=\"M0,4 H8\" fill=\"none\" stroke=\"url(#g)\" stroke-width=\"0\" stroke-dasharray=\"2 2\"/></svg>", out _, out _);
        byte[] line = RenderPixels("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"8\" height=\"8\"><defs><linearGradient id=\"g\"><stop offset=\"0\" stop-color=\"blue\"/><stop offset=\"1\" stop-color=\"green\"/></linearGradient></defs><line x1=\"0\" y1=\"4\" x2=\"8\" y2=\"4\" stroke=\"url(#g)\" stroke-width=\"0\" stroke-dasharray=\"2 2\"/></svg>", out _, out _);

        Assert.Equal(0, AlphaSum(path));
        Assert.Equal(0, AlphaSum(line));
    }

    [Fact]
    public void Zero_gradient_use_stroke_paints_fill_only()
    {
        // A zero-width paint-server stroke override on use resolves to no override, so the instance renders its fill alone.
        byte[] painted = RenderPixels("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"16\"><defs><linearGradient id=\"g\"><stop offset=\"0\" stop-color=\"blue\"/><stop offset=\"1\" stop-color=\"green\"/></linearGradient><path id=\"q\" d=\"M2,2 H14 V14 H2 Z\"/></defs><use href=\"#q\" fill=\"red\" stroke=\"url(#g)\" stroke-width=\"0\"/></svg>", out _, out _);
        byte[] twin = RenderPixels("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"16\"><defs><path id=\"q\" d=\"M2,2 H14 V14 H2 Z\"/></defs><use href=\"#q\" fill=\"red\" stroke=\"none\"/></svg>", out int width, out _);

        Assert.Equal(twin, painted);
        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(painted, width, 8, 8));
    }

    [Fact]
    public void Zero_width_with_missing_paint_paints_fill_only()
    {
        // An unresolvable paint never evaluates when the width is zero, so path, rect, circle, and line renders match their fill-only twins.
        byte[] path = RenderPixels("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"16\"><path d=\"M2,2 H14 V14 H2 Z\" fill=\"red\" stroke=\"url(#nope)\" stroke-width=\"0\"/></svg>", out _, out _);
        byte[] pathTwin = RenderPixels("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"16\"><path d=\"M2,2 H14 V14 H2 Z\" fill=\"red\" stroke=\"none\"/></svg>", out _, out _);
        byte[] rect = RenderPixels("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"16\"><rect x=\"2\" y=\"2\" width=\"12\" height=\"12\" fill=\"red\" stroke=\"url(#nope)\" stroke-width=\"0\"/></svg>", out _, out _);
        byte[] rectTwin = RenderPixels("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"16\"><rect x=\"2\" y=\"2\" width=\"12\" height=\"12\" fill=\"red\" stroke=\"none\"/></svg>", out _, out _);
        byte[] circle = RenderPixels("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"16\"><circle cx=\"8\" cy=\"8\" r=\"6\" fill=\"red\" stroke=\"url(#nope)\" stroke-width=\"0\"/></svg>", out _, out _);
        byte[] circleTwin = RenderPixels("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"16\"><circle cx=\"8\" cy=\"8\" r=\"6\" fill=\"red\" stroke=\"none\"/></svg>", out _, out _);
        byte[] line = RenderPixels("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"16\"><line x1=\"2\" y1=\"8\" x2=\"14\" y2=\"8\" stroke=\"url(#nope)\" stroke-width=\"0\"/></svg>", out _, out _);
        byte[] lineTwin = RenderPixels("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"16\"><line x1=\"2\" y1=\"8\" x2=\"14\" y2=\"8\" stroke=\"red\" stroke-width=\"0\"/></svg>", out _, out _);

        Assert.Equal(pathTwin, path);
        Assert.Equal(rectTwin, rect);
        Assert.Equal(circleTwin, circle);
        Assert.Equal(lineTwin, line);
    }

    [Fact]
    public void Zero_inherited_paint_stroke_paints_fill_only()
    {
        // An inherited paint-server stroke paints nothing at zero width, so the grouped path matches its direct stroke-none twin.
        byte[] grouped = RenderPixels("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"16\"><defs><linearGradient id=\"g\"><stop offset=\"0\" stop-color=\"blue\"/><stop offset=\"1\" stop-color=\"green\"/></linearGradient></defs><g stroke=\"url(#g)\"><path d=\"M2,2 H14 V14 H2 Z\" fill=\"red\" stroke-width=\"0\"/></g></svg>", out _, out _);
        byte[] direct = RenderPixels("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"16\"><path d=\"M2,2 H14 V14 H2 Z\" fill=\"red\" stroke=\"none\"/></svg>", out _, out _);

        Assert.Equal(direct, grouped);
    }

    [Fact]
    public void Zero_combined_and_rounded_paint_strokes_paint_fill_only()
    {
        // Rounded rectangles and combined gradient fills also lose only the stroke at zero width, matching their stroke-none twins.
        string defs = "<defs><linearGradient id=\"g\"><stop offset=\"0\" stop-color=\"blue\"/><stop offset=\"1\" stop-color=\"green\"/></linearGradient></defs>";
        byte[] rounded = RenderPixels("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"16\">" + defs + "<rect x=\"2\" y=\"2\" width=\"12\" height=\"12\" rx=\"2\" fill=\"red\" stroke=\"url(#g)\" stroke-width=\"0\"/></svg>", out _, out _);
        byte[] roundedTwin = RenderPixels("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"16\"><rect x=\"2\" y=\"2\" width=\"12\" height=\"12\" rx=\"2\" fill=\"red\" stroke=\"none\"/></svg>", out _, out _);
        byte[] combinedRect = RenderPixels("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"16\">" + defs + "<rect x=\"2\" y=\"2\" width=\"12\" height=\"12\" fill=\"url(#g)\" stroke=\"url(#g)\" stroke-width=\"0\"/></svg>", out _, out _);
        byte[] combinedRectTwin = RenderPixels("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"16\">" + defs + "<rect x=\"2\" y=\"2\" width=\"12\" height=\"12\" fill=\"url(#g)\" stroke=\"none\"/></svg>", out _, out _);
        byte[] combinedPath = RenderPixels("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"16\">" + defs + "<path d=\"M2,2 H14 V14 H2 Z\" fill=\"url(#g)\" stroke=\"url(#g)\" stroke-width=\"0\"/></svg>", out _, out _);
        byte[] combinedPathTwin = RenderPixels("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"16\">" + defs + "<path d=\"M2,2 H14 V14 H2 Z\" fill=\"url(#g)\" stroke=\"none\"/></svg>", out _, out _);

        Assert.Equal(roundedTwin, rounded);
        Assert.Equal(combinedRectTwin, combinedRect);
        Assert.Equal(combinedPathTwin, combinedPath);
    }

    [Fact]
    public void Zero_solid_strokes_over_paint_fills_paint_fill_only()
    {
        // Paint-filled ellipses and circles skip their combination gates at zero width, so the fill renders alone like its stroke-none twin.
        string gradient = "<defs><linearGradient id=\"g\"><stop offset=\"0\" stop-color=\"blue\"/><stop offset=\"1\" stop-color=\"green\"/></linearGradient></defs>";
        string pattern = "<defs><pattern id=\"p\" width=\"8\" height=\"8\" patternUnits=\"userSpaceOnUse\"><rect x=\"0\" y=\"0\" width=\"8\" height=\"4\" fill=\"#00ff00\"/></pattern></defs>";
        byte[] gradientEllipse = RenderPixels("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"16\">" + gradient + "<ellipse cx=\"8\" cy=\"8\" rx=\"6\" ry=\"4\" fill=\"url(#g)\" stroke=\"red\" stroke-width=\"0\"/></svg>", out _, out _);
        byte[] gradientEllipseTwin = RenderPixels("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"16\">" + gradient + "<ellipse cx=\"8\" cy=\"8\" rx=\"6\" ry=\"4\" fill=\"url(#g)\" stroke=\"none\"/></svg>", out _, out _);
        byte[] gradientCircle = RenderPixels("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"16\">" + gradient + "<circle cx=\"8\" cy=\"8\" r=\"6\" fill=\"url(#g)\" stroke=\"red\" stroke-width=\"0\"/></svg>", out _, out _);
        byte[] gradientCircleTwin = RenderPixels("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"16\">" + gradient + "<circle cx=\"8\" cy=\"8\" r=\"6\" fill=\"url(#g)\" stroke=\"none\"/></svg>", out _, out _);
        byte[] patternEllipse = RenderPixels("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"16\">" + pattern + "<ellipse cx=\"8\" cy=\"8\" rx=\"6\" ry=\"4\" fill=\"url(#p)\" stroke=\"red\" stroke-width=\"0\"/></svg>", out _, out _);
        byte[] patternEllipseTwin = RenderPixels("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"16\">" + pattern + "<ellipse cx=\"8\" cy=\"8\" rx=\"6\" ry=\"4\" fill=\"url(#p)\" stroke=\"none\"/></svg>", out _, out _);
        byte[] patternCircle = RenderPixels("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"16\">" + pattern + "<circle cx=\"8\" cy=\"8\" r=\"6\" fill=\"url(#p)\" stroke=\"red\" stroke-width=\"0\"/></svg>", out _, out _);
        byte[] patternCircleTwin = RenderPixels("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"16\">" + pattern + "<circle cx=\"8\" cy=\"8\" r=\"6\" fill=\"url(#p)\" stroke=\"none\"/></svg>", out _, out _);

        Assert.Equal(gradientEllipseTwin, gradientEllipse);
        Assert.Equal(gradientCircleTwin, gradientCircle);
        Assert.Equal(patternEllipseTwin, patternEllipse);
        Assert.Equal(patternCircleTwin, patternCircle);
    }

    [Fact]
    public void Zero_paint_strokes_over_pattern_fills_paint_fill_only()
    {
        // Pattern-filled shapes lose only the paint stroke at zero width, matching fill-only twins through the shared band paths.
        string pattern = "<defs><pattern id=\"p\" width=\"8\" height=\"8\" patternUnits=\"userSpaceOnUse\"><rect x=\"0\" y=\"0\" width=\"8\" height=\"4\" fill=\"#00ff00\"/></pattern></defs>";
        byte[] patternRect = RenderPixels("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"16\">" + pattern + "<rect x=\"2\" y=\"2\" width=\"12\" height=\"12\" fill=\"url(#p)\" stroke=\"url(#g)\" stroke-width=\"0\"/></svg>", out _, out _);
        byte[] patternRectTwin = RenderPixels("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"16\">" + pattern + "<rect x=\"2\" y=\"2\" width=\"12\" height=\"12\" fill=\"url(#p)\" stroke=\"none\"/></svg>", out _, out _);
        byte[] patternPath = RenderPixels("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"16\">" + pattern + "<path d=\"M2,2 H14 V14 H2 Z\" fill=\"url(#p)\" stroke=\"url(#g)\" stroke-width=\"0\"/></svg>", out _, out _);
        byte[] patternPathTwin = RenderPixels("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"16\">" + pattern + "<path d=\"M2,2 H14 V14 H2 Z\" fill=\"url(#p)\" stroke=\"none\"/></svg>", out _, out _);

        Assert.Equal(patternRectTwin, patternRect);
        Assert.Equal(patternPathTwin, patternPath);
    }

    [Fact]
    public void Gradient_stroke_composes_over_gradient_fill()
    {
        // A linear-gradient stroke outlines a linear-gradient fill through white-outline repaint, so outer bands read the stroke paint while the interior reads the fill ramp.
        byte[] painted = RenderPixels("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"16\"><defs><linearGradient id=\"f\" x1=\"0\" y1=\"0\" x2=\"1\" y2=\"0\"><stop offset=\"0\" stop-color=\"#ff0000\"/><stop offset=\"1\" stop-color=\"#0000ff\"/></linearGradient><linearGradient id=\"s\" x1=\"0\" y1=\"0\" x2=\"0\" y2=\"1\"><stop offset=\"0\" stop-color=\"#00ff00\"/><stop offset=\"1\" stop-color=\"#000000\"/></linearGradient></defs><path d=\"M2,2 H14 V14 H2 Z\" fill=\"url(#f)\" stroke=\"url(#s)\" stroke-width=\"2\"/></svg>", out int width, out _);
        byte[] fillOnly = RenderPixels("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"16\"><defs><linearGradient id=\"f\" x1=\"0\" y1=\"0\" x2=\"1\" y2=\"0\"><stop offset=\"0\" stop-color=\"#ff0000\"/><stop offset=\"1\" stop-color=\"#0000ff\"/></linearGradient></defs><path d=\"M2,2 H14 V14 H2 Z\" fill=\"url(#f)\" stroke=\"none\"/></svg>", out _, out _);

        Assert.Equal(new SvgColor(0, 255, 0, 255), Pixel(painted, width, 8, 1));
        Assert.Equal(new SvgColor(0, 0, 0, 255), Pixel(painted, width, 8, 14));
        Assert.Equal(255, Pixel(painted, width, 1, 8).Alpha);
        Assert.Equal(255, Pixel(painted, width, 14, 8).Alpha);
        Assert.True(Pixel(painted, width, 4, 8).Red > 128);
        Assert.True(Pixel(painted, width, 4, 8).Blue > 0);
        Assert.True(Pixel(painted, width, 11, 8).Blue > Pixel(painted, width, 11, 8).Red);
        Assert.True(AlphaSum(painted) > AlphaSum(fillOnly));
    }

    [Fact]
    public void Gradient_stroke_outlines_curved_gradient_path()
    {
        // The same white-outline repaint serves curved fills, so the arch renders both paints where the fill-only twin shows fill alone.
        byte[] painted = RenderPixels("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"16\"><defs><linearGradient id=\"f\" x1=\"0\" y1=\"0\" x2=\"1\" y2=\"0\"><stop offset=\"0\" stop-color=\"#ff0000\"/><stop offset=\"1\" stop-color=\"#0000ff\"/></linearGradient><linearGradient id=\"s\" x1=\"0\" y1=\"0\" x2=\"0\" y2=\"1\"><stop offset=\"0\" stop-color=\"#00ff00\"/><stop offset=\"1\" stop-color=\"#000000\"/></linearGradient></defs><path d=\"M2,12 Q8,2 14,12 Z\" fill=\"url(#f)\" stroke=\"url(#s)\" stroke-width=\"2\"/></svg>", out _, out _);
        byte[] fillOnly = RenderPixels("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"16\"><defs><linearGradient id=\"f\" x1=\"0\" y1=\"0\" x2=\"1\" y2=\"0\"><stop offset=\"0\" stop-color=\"#ff0000\"/><stop offset=\"1\" stop-color=\"#0000ff\"/></linearGradient></defs><path d=\"M2,12 Q8,2 14,12 Z\" fill=\"url(#f)\" stroke=\"none\"/></svg>", out _, out _);

        Assert.NotEqual(fillOnly, painted);
        Assert.True(AlphaSum(painted) > AlphaSum(fillOnly));
    }

    [Fact]
    public void Gradient_stroke_supports_dashes_and_polylines()
    {
        // Dash cutting and point-list lowering ride the shared white outline, so dashed paths and polygons render both paints.
        byte[] dashed = RenderPixels("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"16\"><defs><linearGradient id=\"f\" x1=\"0\" y1=\"0\" x2=\"1\" y2=\"0\"><stop offset=\"0\" stop-color=\"#ff0000\"/><stop offset=\"1\" stop-color=\"#0000ff\"/></linearGradient><linearGradient id=\"s\" x1=\"0\" y1=\"0\" x2=\"0\" y2=\"1\"><stop offset=\"0\" stop-color=\"#00ff00\"/><stop offset=\"1\" stop-color=\"#000000\"/></linearGradient></defs><path d=\"M2,2 H14 V14 H2 Z\" fill=\"url(#f)\" stroke=\"url(#s)\" stroke-width=\"2\" stroke-dasharray=\"3 2\"/></svg>", out _, out _);
        byte[] poly = RenderPixels("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"16\"><defs><linearGradient id=\"f\" x1=\"0\" y1=\"0\" x2=\"1\" y2=\"0\"><stop offset=\"0\" stop-color=\"#ff0000\"/><stop offset=\"1\" stop-color=\"#0000ff\"/></linearGradient><linearGradient id=\"s\" x1=\"0\" y1=\"0\" x2=\"0\" y2=\"1\"><stop offset=\"0\" stop-color=\"#00ff00\"/><stop offset=\"1\" stop-color=\"#000000\"/></linearGradient></defs><polygon points=\"2,2 14,2 14,14 2,14\" fill=\"url(#f)\" stroke=\"url(#s)\" stroke-width=\"2\"/></svg>", out _, out _);
        byte[] solid = RenderPixels("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"16\"><defs><linearGradient id=\"f\" x1=\"0\" y1=\"0\" x2=\"1\" y2=\"0\"><stop offset=\"0\" stop-color=\"#ff0000\"/><stop offset=\"1\" stop-color=\"#0000ff\"/></linearGradient></defs><polygon points=\"2,2 14,2 14,14 2,14\" fill=\"url(#f)\" stroke=\"red\" stroke-width=\"2\"/></svg>", out _, out _);

        Assert.True(AlphaSum(dashed) > 0);
        Assert.NotEqual(solid, poly);
        Assert.True(AlphaSum(poly) > 0);
    }

    [Fact]
    public void Non_linear_fills_accept_gradient_strokes_and_missing_paints_reject()
    {
        // Fill and stroke keep their own paint while missing resources still reject.
        string missing = "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"16\"><defs><linearGradient id=\"f\" x1=\"0\" y1=\"0\" x2=\"1\" y2=\"0\"><stop offset=\"0\" stop-color=\"#ff0000\"/><stop offset=\"1\" stop-color=\"#0000ff\"/></linearGradient></defs><path d=\"M2,2 H14 V14 H2 Z\" fill=\"url(#f)\" stroke=\"url(#nope)\" stroke-width=\"2\"/></svg>";
        string radialFill = "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"16\"><defs><radialGradient id=\"r\"><stop offset=\"0\" stop-color=\"#ff0000\"/><stop offset=\"1\" stop-color=\"#0000ff\"/></radialGradient><linearGradient id=\"s\" x1=\"0\" y1=\"0\" x2=\"0\" y2=\"1\"><stop offset=\"0\" stop-color=\"#00ff00\"/><stop offset=\"1\" stop-color=\"#000000\"/></linearGradient></defs><path d=\"M2,2 H14 V14 H2 Z\" fill=\"url(#r)\" stroke=\"url(#s)\" stroke-width=\"2\"/></svg>";
        string patternFill = "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"16\"><defs><pattern id=\"p\" width=\"8\" height=\"8\" patternUnits=\"userSpaceOnUse\"><rect x=\"0\" y=\"0\" width=\"8\" height=\"4\" fill=\"#00ff00\"/></pattern><linearGradient id=\"s\" x1=\"0\" y1=\"0\" x2=\"0\" y2=\"1\"><stop offset=\"0\" stop-color=\"#00ff00\"/><stop offset=\"1\" stop-color=\"#000000\"/></linearGradient></defs><path d=\"M2,2 H14 V14 H2 Z\" fill=\"url(#p)\" stroke=\"url(#s)\" stroke-width=\"2\"/></svg>";

        SvgException rejected = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(System.Text.Encoding.UTF8.GetBytes(missing), SvgRenderOptions.Default, CancellationToken.None));
        Assert.Equal(SvgErrorCode.UnsupportedFeature, rejected.ErrorCode);
        foreach (string svg in new[] { radialFill, patternFill })
        {
            byte[] painted = RenderPixels(svg, out int width, out _);
            Assert.Equal(255, Pixel(painted, width, 8, 1).Alpha);
            Assert.True(Pixel(painted, width, 8, 1).Green > 100);
            Assert.Equal(0, Pixel(painted, width, 8, 1).Red);
            SvgColor center = Pixel(painted, width, 8, 8);
            if (svg == radialFill)
            {
                Assert.Equal(0, center.Green);
                Assert.True(center.Red > 0 && center.Blue > 0);
            }
            else Assert.Equal(new SvgColor(0, 255, 0, 255), center);
        }
        byte[] patterned = RenderPixels(patternFill, out int patternWidth, out _);
        Assert.Equal(255, Pixel(patterned, patternWidth, 8, 9).Alpha);
        Assert.Equal(0, Pixel(patterned, patternWidth, 8, 5).Alpha);
    }

    [Fact]
    public void Radial_stroke_composes_over_gradient_fill()
    {
        // A radial-gradient stroke outlines a linear-gradient fill through white-outline repaint, so outer bands read the radial paint while the interior reads the fill ramp.
        byte[] painted = RenderPixels("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"16\"><defs><linearGradient id=\"f\" x1=\"0\" y1=\"0\" x2=\"1\" y2=\"0\"><stop offset=\"0\" stop-color=\"#ff0000\"/><stop offset=\"1\" stop-color=\"#ffff00\"/></linearGradient><radialGradient id=\"r\"><stop offset=\"0\" stop-color=\"#0000ff\"/><stop offset=\"1\" stop-color=\"#00ff00\"/></radialGradient></defs><path d=\"M2,2 H14 V14 H2 Z\" fill=\"url(#f)\" stroke=\"url(#r)\" stroke-width=\"2\"/></svg>", out int width, out _);
        byte[] fillOnly = RenderPixels("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"16\"><defs><linearGradient id=\"f\" x1=\"0\" y1=\"0\" x2=\"1\" y2=\"0\"><stop offset=\"0\" stop-color=\"#ff0000\"/><stop offset=\"1\" stop-color=\"#ffff00\"/></linearGradient></defs><path d=\"M2,2 H14 V14 H2 Z\" fill=\"url(#f)\" stroke=\"none\"/></svg>", out _, out _);

        Assert.Equal(255, Pixel(painted, width, 8, 1).Alpha);
        Assert.True(Pixel(painted, width, 8, 1).Green > 100);
        Assert.True(Pixel(painted, width, 8, 1).Red < 8);
        Assert.True(Pixel(painted, width, 8, 1).Blue < 8);
        Assert.Equal(255, Pixel(painted, width, 8, 14).Alpha);
        Assert.Equal(255, Pixel(painted, width, 1, 8).Alpha);
        Assert.Equal(255, Pixel(painted, width, 14, 8).Alpha);
        Assert.Equal(255, Pixel(painted, width, 4, 8).Red);
        Assert.True(Pixel(painted, width, 4, 8).Green > 0);
        Assert.True(Pixel(painted, width, 11, 8).Green > 128);
        Assert.True(AlphaSum(painted) > AlphaSum(fillOnly));
    }

    [Fact]
    public void Radial_stroke_outlines_curved_gradient_path()
    {
        // The same white-outline repaint serves curved fills for radial strokes, so the arch renders both paints where the fill-only twin shows fill alone.
        byte[] painted = RenderPixels("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"16\"><defs><linearGradient id=\"f\" x1=\"0\" y1=\"0\" x2=\"1\" y2=\"0\"><stop offset=\"0\" stop-color=\"#ff0000\"/><stop offset=\"1\" stop-color=\"#ffff00\"/></linearGradient><radialGradient id=\"r\"><stop offset=\"0\" stop-color=\"#0000ff\"/><stop offset=\"1\" stop-color=\"#00ff00\"/></radialGradient></defs><path d=\"M2,12 Q8,2 14,12 Z\" fill=\"url(#f)\" stroke=\"url(#r)\" stroke-width=\"2\"/></svg>", out _, out _);
        byte[] fillOnly = RenderPixels("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"16\"><defs><linearGradient id=\"f\" x1=\"0\" y1=\"0\" x2=\"1\" y2=\"0\"><stop offset=\"0\" stop-color=\"#ff0000\"/><stop offset=\"1\" stop-color=\"#ffff00\"/></linearGradient></defs><path d=\"M2,12 Q8,2 14,12 Z\" fill=\"url(#f)\" stroke=\"none\"/></svg>", out _, out _);

        Assert.NotEqual(fillOnly, painted);
        Assert.True(AlphaSum(painted) > AlphaSum(fillOnly));
    }

    [Fact]
    public void Radial_stroke_supports_dashes_and_polylines()
    {
        // Dash cutting and point-list lowering ride the shared radial white outline, so dashed paths and polygons render both paints.
        byte[] dashed = RenderPixels("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"16\"><defs><linearGradient id=\"f\" x1=\"0\" y1=\"0\" x2=\"1\" y2=\"0\"><stop offset=\"0\" stop-color=\"#ff0000\"/><stop offset=\"1\" stop-color=\"#ffff00\"/></linearGradient><radialGradient id=\"r\"><stop offset=\"0\" stop-color=\"#0000ff\"/><stop offset=\"1\" stop-color=\"#00ff00\"/></radialGradient></defs><path d=\"M2,2 H14 V14 H2 Z\" fill=\"url(#f)\" stroke=\"url(#r)\" stroke-width=\"2\" stroke-dasharray=\"3 2\"/></svg>", out _, out _);
        byte[] poly = RenderPixels("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"16\"><defs><linearGradient id=\"f\" x1=\"0\" y1=\"0\" x2=\"1\" y2=\"0\"><stop offset=\"0\" stop-color=\"#ff0000\"/><stop offset=\"1\" stop-color=\"#ffff00\"/></linearGradient><radialGradient id=\"r\"><stop offset=\"0\" stop-color=\"#0000ff\"/><stop offset=\"1\" stop-color=\"#00ff00\"/></radialGradient></defs><polygon points=\"2,2 14,2 14,14 2,14\" fill=\"url(#f)\" stroke=\"url(#r)\" stroke-width=\"2\"/></svg>", out _, out _);
        byte[] solid = RenderPixels("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"16\"><defs><linearGradient id=\"f\" x1=\"0\" y1=\"0\" x2=\"1\" y2=\"0\"><stop offset=\"0\" stop-color=\"#ff0000\"/><stop offset=\"1\" stop-color=\"#ffff00\"/></linearGradient></defs><polygon points=\"2,2 14,2 14,14 2,14\" fill=\"url(#f)\" stroke=\"red\" stroke-width=\"2\"/></svg>", out _, out _);

        Assert.True(AlphaSum(dashed) > 0);
        Assert.NotEqual(solid, poly);
        Assert.True(AlphaSum(poly) > 0);
    }

    [Fact]
    public void Fractional_stroke_width_renders_ring()
    {
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><polygon points='0,0 8,0 8,8 0,8' fill='none' stroke='red' stroke-width='2.5'/></svg>", out _, out _);

        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, 8, 0, 0));
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(pixels, 8, 4, 4));
        Assert.NotEqual(0, AlphaSum(pixels));
    }

    [Fact]
    public void Fractional_dashed_path_renders_runs()
    {
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><path d='M0,4 H8' fill='none' stroke='red' stroke-width='2.5' stroke-dasharray='2.5 2.5'/></svg>", out _, out _);

        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, 8, 1, 4));
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(pixels, 8, 3, 4));
        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, 8, 6, 4));
    }

    [Fact]
    public void Fractional_gradient_stroke_repaints_outline()
    {
        byte[] fractional = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><defs><linearGradient id='g' x1='0' y1='0' x2='1' y2='0'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><path d='M0,0 H16 V16 H0 Z' fill='red' stroke='url(#g)' stroke-width='2.5'/></svg>", out _, out _);
        byte[] integral = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><defs><linearGradient id='g' x1='0' y1='0' x2='1' y2='0'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><path d='M0,0 H16 V16 H0 Z' fill='red' stroke='url(#g)' stroke-width='2'/></svg>", out _, out _);

        Assert.NotEqual(integral, fractional);
        Assert.NotEqual(0, AlphaSum(fractional));
        Assert.Equal(255, Pixel(fractional, 16, 8, 0).Alpha);
    }

    [Theory]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><polygon points='0,0 8,0 8,8 0,8' fill='none' stroke='red' stroke-width='2' stroke-linejoin='bevel'/></svg>")]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><polygon points='0.5,0 8,0 8,8 0,8' fill='none' stroke='red' stroke-width='2' stroke-linejoin='bevel'/></svg>")]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><polygon points='0,0 8,0 8,8 0,8' fill='none' stroke='red' stroke-width='2' stroke-miterlimit='0.5'/></svg>")]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><polygon points='0,0 8,0 8,8 0,8' fill='none' stroke='red' stroke-width='2' style='stroke-miterlimit:0.5'/>")]
    public void Stroke_styles_preserve_admitted_bevel_joins_and_invalid_miter_limit_errors(string svg)
    {
        if (svg.Contains("stroke-linejoin='bevel'", StringComparison.Ordinal))
        {
            byte[] pixels = RenderPixels(svg, out _, out _);
            Assert.NotEqual(0, AlphaSum(pixels));
        }
        else
        {
            SvgException rejected = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(System.Text.Encoding.UTF8.GetBytes(svg), SvgRenderOptions.Default, CancellationToken.None));
            Assert.Equal(SvgErrorCode.UnsupportedFeature, rejected.ErrorCode);
        }
    }

    [Fact]
    public void Stroke_under_anisotropic_transform_preserves_source_width()
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='8'><g transform='scale(2,1)'><polygon points='0,0 8,0 8,8 0,8' fill='none' stroke='red' stroke-width='2'/></g></svg>");

        byte[] pixels = RenderPixels(System.Text.Encoding.UTF8.GetString(input), out int width, out int height);
        Assert.Equal(16, width);
        Assert.Equal(8, height);
        Assert.Equal(255, Pixel(pixels, width, 0, 4).Alpha);
        Assert.Equal(0, Pixel(pixels, width, 8, 4).Alpha);
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
    public void Dashed_diagonal_vertex_run_survives_overlap()
    {
        // The dash run starting on the diagonal just past the vertex
        // overlaps the run ending at the vertex, and first-vertex winding
        // roles must not cancel the partially overlapped run.
        byte[] pixels = RenderPixels("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"16\"><polyline points=\"0,0 16,0 0,16\" fill=\"none\" stroke=\"red\" stroke-width=\"2\" stroke-dasharray=\"4 2\"/></svg>", out _, out _);

        Assert.NotEqual(0, Pixel(pixels, 16, 13, 3).Alpha);
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(pixels, 16, 11, 5));
    }

    [Fact]
    public void Dashed_diagonal_path_run_matches_polyline()
    {
        // The path lowering shares the dashed vertex-adjacent run with
        // the polyline lowering instead of dropping it at role resolution.
        byte[] path = RenderPixels("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"16\"><path d=\"M0,0 L16,0 L0,16\" fill=\"none\" stroke=\"red\" stroke-width=\"2\" stroke-dasharray=\"4 2\"/></svg>", out _, out _);
        byte[] polyline = RenderPixels("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"16\"><polyline points=\"0,0 16,0 0,16\" fill=\"none\" stroke=\"red\" stroke-width=\"2\" stroke-dasharray=\"4 2\"/></svg>", out _, out _);

        Assert.NotEqual(0, Pixel(path, 16, 13, 3).Alpha);
        Assert.Equal(polyline, path);
    }

    [Fact]
    public void Rotated_open_spoke_paints_with_stable_width()
    {
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><path d='M8,8 L8,0' stroke='red' stroke-width='2' fill='none' transform='rotate(90,8,8)'/></svg>", out _, out _);

        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, 16, 12, 8));
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(pixels, 16, 4, 8));
    }

    [Fact]
    public void Solid_bowtie_stroke_paints_both_halves()
    {
        // Solid stroke outlines union across the crossing like dashes;
        // the render is 180-degree rotationally symmetric with stroke on both sides.
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><polygon points='0,0 8,8 8,0 0,8' fill='none' stroke='blue' stroke-width='2'/></svg>", out _, out _);

        Assert.True(Pixel(pixels, 8, 7, 0).Alpha > 0);
        Assert.True(Pixel(pixels, 8, 0, 7).Alpha > 0);
        Assert.Equal(Pixel(pixels, 8, 0, 0), Pixel(pixels, 8, 7, 7));
        Assert.Equal(Pixel(pixels, 8, 1, 0), Pixel(pixels, 8, 6, 7));
    }

    [Fact]
    public void Filled_bowtie_stroke_covers_both_halves()
    {
        // Solid stroke over a filled crossing reaches both halves in paint order.
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><polygon points='0,0 8,8 8,0 0,8' fill='red' stroke='blue' stroke-width='2'/></svg>", out _, out _);

        Assert.True(Pixel(pixels, 8, 7, 0).Blue > 0);
        Assert.True(Pixel(pixels, 8, 0, 7).Blue > 0);
        Assert.Equal(Pixel(pixels, 8, 0, 0), Pixel(pixels, 8, 7, 7));
    }

    [Fact]
    public void Dashed_bowtie_matches_path_twin()
    {
        // Dashed outlines agree across polygon and path lowerings on crossing input.
        byte[] polygon = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><polygon points='0,0 8,8 8,0 0,8' fill='none' stroke='red' stroke-width='2' stroke-dasharray='4 2'/></svg>", out _, out _);
        byte[] path = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><path d='M0,0 L8,8 L8,0 L0,8 Z' fill='none' stroke='red' stroke-width='2' stroke-dasharray='4 2'/></svg>", out _, out _);

        Assert.Equal(path, polygon);
        Assert.True(Pixel(polygon, 8, 0, 0).Alpha > 0);
    }

    [Fact]
    public void Dashed_diagonal_polygon_run_survives_overlap()
    {
        // The closed dashed triangle keeps the same vertex-adjacent
        // diagonal run as the open polyline through seam merging.
        byte[] polygon = RenderPixels("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"16\"><polygon points=\"0,0 16,0 0,16\" fill=\"none\" stroke=\"red\" stroke-width=\"2\" stroke-dasharray=\"4 2\"/></svg>", out _, out _);

        Assert.NotEqual(0, Pixel(polygon, 16, 13, 3).Alpha);
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(polygon, 16, 11, 5));
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
    public void Zero_dashes_with_round_caps_paint_separated_dots()
    {
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><polyline points='0,4 8,4' fill='none' stroke='red' stroke-width='2' stroke-linecap='round' stroke-dasharray='0,4'/></svg>", out int width, out int height);
        Assert.Equal(8, width);
        Assert.Equal(8, height);
        Assert.True(Pixel(pixels, width, 0, 4).Alpha > 0);
        Assert.Equal(0, Pixel(pixels, width, 2, 4).Alpha);
        Assert.True(Pixel(pixels, width, 4, 4).Alpha > 0);
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
        // Stable pinned captures keep dash lengths in host coordinates too.
        byte[] direct = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'><polygon points='0,0 16,0 16,16 0,16' fill='none' stroke='red' stroke-width='2' stroke-dasharray='2,2'/></svg>", out _, out _);

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
    public void Nonuniform_dashed_nonscaling_stroke_preserves_device_width()
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'><g transform='scale(2,1)'><polygon points='0,0 8,0 8,8 0,8' fill='none' stroke='red' stroke-width='2' stroke-dasharray='2,2' vector-effect='non-scaling-stroke'/></g></svg>");

        byte[] pixels = RenderPixels(System.Text.Encoding.UTF8.GetString(input), out int width, out int height);
        Assert.Equal(32, width);
        Assert.Equal(32, height);
        Assert.True(Pixel(pixels, width, 0, 0).Alpha > 0);
        Assert.Equal(0, Pixel(pixels, width, 14, 4).Alpha);
        Assert.Equal(255, Pixel(pixels, width, 15, 4).Alpha);
        Assert.Equal(0, Pixel(pixels, width, 8, 4).Alpha);
        Assert.Equal(0, Pixel(pixels, width, 8, 12).Alpha);
    }

    [Fact]
    public void Rejects_unknown_vector_effects()
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><polygon points='0,0 8,0 8,8 0,8' fill='none' stroke='red' stroke-width='2' vector-effect='non-rotation'/></svg>");

        SvgException rejected = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, rejected.ErrorCode);
    }

    [Fact]
    public void Non_scaling_rectangle_stroke_preserves_fill_and_outline()
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><rect x='0' y='0' width='8' height='8' fill='red' stroke='blue' stroke-width='2' vector-effect='non-scaling-stroke'/></svg>");

        byte[] pixels = DecodeRgba(SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None), out int width, out _);
        Assert.Equal(new SvgColor(0, 0, 255, 255), Pixel(pixels, width, 0, 0));
        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, width, 4, 4));
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
    public void Rectangle_dasharray_preserves_ink_and_gaps()
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><rect x='0' y='0' width='8' height='8' fill='red' stroke='blue' stroke-width='2' stroke-dasharray='2,2'/></svg>");

        byte[] pixels = DecodeRgba(SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None), out int width, out _);
        Assert.Equal(new SvgColor(0, 0, 255, 255), Pixel(pixels, width, 0, 0));
        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, width, 3, 0));
    }

    [Fact]
    public void Inline_rectangle_dasharray_preserves_ink_and_gaps()
    {
        // Inline dash lists retain the same source stroke semantics as attributes.
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><rect x='0' y='0' width='8' height='8' fill='red' stroke='blue' stroke-width='2' style='stroke-dasharray:4 4'/></svg>");

        byte[] pixels = DecodeRgba(SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None), out int width, out _);
        Assert.Equal(new SvgColor(0, 0, 255, 255), Pixel(pixels, width, 0, 0));
        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, width, 6, 0));
    }

    [Fact]
    public void Inline_dash_list_on_polygon_stroke_matches_attribute()
    {
        // Inline dash lists flow through the cascade into the admitted
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
        // Stylesheet dash lists resolve through the same cascade into the
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
        // Validation honors inline overrides of rejected attribute
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
        // The rectangle band gate evaluates the inline-neutralized join,
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
        // Inline stroke declarations without an attribute spelling stay
        // lowering business; band gates must not trip on them, since
        // fill-only geometry never lowers a stroke.
        byte[] styled = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><rect x='0' y='0' width='8' height='8' fill='red' style='stroke-linejoin:round'/></svg>");
        byte[] plain = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><rect x='0' y='0' width='8' height='8' fill='red'/></svg>");
        ReadOnlyMemory<byte> styledPng = SvgConverter.ConvertToPng(styled, SvgRenderOptions.Default, CancellationToken.None);
        ReadOnlyMemory<byte> plainPng = SvgConverter.ConvertToPng(plain, SvgRenderOptions.Default, CancellationToken.None);

        Assert.True(styledPng.Span.SequenceEqual(plainPng.Span));
    }

    [Fact]
    public void Stylesheet_neutralized_dasharray_on_rectangle_stroke_matches_solid()
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><style>rect { stroke-dasharray: none; }</style><rect x='0' y='0' width='8' height='8' fill='red' stroke='blue' stroke-width='2' stroke-dasharray='2,2'/></svg>");

        byte[] plain = System.Text.Encoding.UTF8.GetBytes(System.Text.Encoding.UTF8.GetString(input).Replace("stroke-dasharray='2,2'", "stroke-dasharray='none'", StringComparison.Ordinal));
        Assert.Equal(SvgConverter.ConvertToPng(plain, SvgRenderOptions.Default, CancellationToken.None).ToArray(),
            SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None).ToArray());
    }

    [Fact]
    public void Inline_linecap_on_line_stroke_matches_attribute()
    {
        // Linecap style overrides evaluate through the cascade exactly
        // like the attribute spelling.
        byte[] styled = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><line x1='4' y1='8' x2='12' y2='8' stroke='red' stroke-width='4' stroke-linecap='square' style='stroke-linecap:butt'/></svg>", out _, out _);
        byte[] attribute = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><line x1='4' y1='8' x2='12' y2='8' stroke='red' stroke-width='4' stroke-linecap='butt'/></svg>", out _, out _);

        Assert.Equal(attribute, styled);
    }

    [Fact]
    public void Inline_dasharray_on_line_stroke_matches_attribute()
    {
        // An inline neutralized dasharray renders the solid line exactly
        // like the dash-free spelling.
        byte[] styled = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><line x1='2' y1='8' x2='14' y2='8' stroke='red' stroke-width='2' stroke-dasharray='4 4' style='stroke-dasharray:none'/></svg>", out _, out _);
        byte[] attribute = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><line x1='2' y1='8' x2='14' y2='8' stroke='red' stroke-width='2'/></svg>", out _, out _);

        Assert.Equal(attribute, styled);
    }

    [Fact]
    public void Inline_dashoffset_on_line_stroke_matches_attribute()
    {
        // Dashoffset style overrides shift dash runs exactly like the
        // attribute spelling.
        byte[] styled = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><line x1='2' y1='8' x2='14' y2='8' stroke='red' stroke-width='2' stroke-dasharray='4 4' style='stroke-dashoffset:3'/></svg>", out _, out _);
        byte[] attribute = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><line x1='2' y1='8' x2='14' y2='8' stroke='red' stroke-width='2' stroke-dasharray='4 4' stroke-dashoffset='3'/></svg>", out _, out _);

        Assert.Equal(attribute, styled);
    }

    [Fact]
    public void Inline_dash_and_cap_on_gradient_line_matches_attribute()
    {
        // The gradient-stroke routers and band gates evaluate effective
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
        // The gradient round-cap path honors the style cap, rendering
        // exactly like the attribute butt spelling.
        string gradient = "<defs><linearGradient id='g' gradientUnits='userSpaceOnUse' x1='0' y1='0' x2='16' y2='0'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs>";
        byte[] styled = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='8'>" + gradient + "<line x1='0' y1='4' x2='16' y2='4' stroke='url(#g)' stroke-width='2' stroke-linecap='round' style='stroke-linecap:butt'/></svg>", out _, out _);
        byte[] attribute = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='8'>" + gradient + "<line x1='0' y1='4' x2='16' y2='4' stroke='url(#g)' stroke-width='2' stroke-linecap='butt'/></svg>", out _, out _);

        Assert.Equal(attribute, styled);
    }

    [Fact]
    public void Gradient_fill_with_solid_stroke_paints_band_over_fill()
    {
        // Gradient-filled bands compose with an admitted solid stroke
        // outline in paint order, byte-exact against the pinned Chrome capture.
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><defs><linearGradient id='g' x1='0' y1='0' x2='1' y2='0'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><path d='M0,0 H16 V16 H0 Z' fill='url(#g)' stroke='blue' stroke-width='2'/></svg>", out int width, out int height);

        Assert.Equal(16, width);
        Assert.Equal(16, height);
        Assert.Equal(new SvgColor(0, 0, 255, 255), Pixel(pixels, width, 8, 0));
    }

    [Fact]
    public void Gradient_fill_with_solid_stroke_path_matches_polygon()
    {
        // Paths and polygons share the fill-plus-stroke lowering, so the
        // same outline renders byte-identically through both spellings.
        byte[] path = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><defs><linearGradient id='g' x1='0' y1='0' x2='1' y2='0'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><path d='M0,0 H16 V16 H0 Z' fill='url(#g)' stroke='blue' stroke-width='2'/></svg>", out _, out _);
        byte[] polygon = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><defs><linearGradient id='g' x1='0' y1='0' x2='1' y2='0'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><polygon points='0,0 16,0 16,16 0,16' fill='url(#g)' stroke='blue' stroke-width='2'/></svg>", out _, out _);

        Assert.Equal(polygon, path);
    }

    [Fact]
    public void Radial_fill_with_solid_stroke_paints_band_over_fill()
    {
        // Radial-filled bands compose with an admitted solid stroke
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
        // Pattern-filled bands compose with an admitted solid stroke
        // outline in paint order, byte-exact against the pinned Chrome capture.
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><defs><pattern id='p' width='4' height='4' patternUnits='userSpaceOnUse'><rect width='2' height='2' fill='red'/></pattern></defs><path d='M0,0 H16 V16 H0 Z' fill='url(#p)' stroke='blue' stroke-width='2'/></svg>", out int width, out int height);

        Assert.Equal(16, width);
        Assert.Equal(16, height);
        Assert.Equal(new SvgColor(0, 0, 255, 255), Pixel(pixels, width, 8, 0));
    }

    [Fact]
    public void Pattern_fill_with_solid_stroke_path_matches_polygon()
    {
        // Paths and polygons share the pattern fill-plus-stroke
        // lowering, so the same outline renders byte-identically through both
        // spellings.
        byte[] path = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><defs><pattern id='p' width='4' height='4' patternUnits='userSpaceOnUse'><rect width='2' height='2' fill='red'/></pattern></defs><path d='M0,0 H16 V16 H0 Z' fill='url(#p)' stroke='blue' stroke-width='2'/></svg>", out _, out _);
        byte[] polygon = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><defs><pattern id='p' width='4' height='4' patternUnits='userSpaceOnUse'><rect width='2' height='2' fill='red'/></pattern></defs><polygon points='0,0 16,0 16,16 0,16' fill='url(#p)' stroke='blue' stroke-width='2'/></svg>", out _, out _);

        Assert.Equal(polygon, path);
    }

    [Fact]
    public void Pattern_fill_with_solid_stroke_on_curved_path_paints_outline()
    {
        // Curved pattern fills compose with solid outlines through
        // flattened geometry; the axis-aligned edge stroke pixels are exact
        // while the flattened curve carries the standing curve-edge delta.
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><defs><pattern id='p' width='4' height='4' patternUnits='userSpaceOnUse'><rect width='2' height='2' fill='red'/></pattern></defs><path d='M0,8 Q8,0 16,8 L16,16 L0,16 Z' fill='url(#p)' stroke='blue' stroke-width='2'/></svg>", out int width, out int height);

        Assert.Equal(16, width);
        Assert.Equal(16, height);
        Assert.Equal(new SvgColor(0, 0, 255, 255), Pixel(pixels, width, 0, 12));
        Assert.Equal(new SvgColor(0, 0, 255, 255), Pixel(pixels, width, 8, 15));
    }

    [Fact]
    public void Pattern_fill_with_solid_stroke_on_diagonal_paints_outline()
    {
        // Diagonal outlines compose the same way; the axis-aligned
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
        // Paths and polygons share the radial fill-plus-stroke lowering,
        // so the same outline renders byte-identically through both spellings.
        byte[] path = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><defs><radialGradient id='g'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><path d='M0,0 H16 V16 H0 Z' fill='url(#g)' stroke='blue' stroke-width='2'/></svg>", out _, out _);
        byte[] polygon = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><defs><radialGradient id='g'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><polygon points='0,0 16,0 16,16 0,16' fill='url(#g)' stroke='blue' stroke-width='2'/></svg>", out _, out _);

        Assert.Equal(polygon, path);
    }

    [Fact]
    public void Gradient_fill_with_solid_stroke_on_curved_path_paints_outline()
    {
        // Curved fills compose with solid outlines through flattened
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
        // Diagonal outlines compose the same way; the axis-aligned left
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
    public void Degenerate_box_linear_gradient_path_stroke_paints_nothing()
    {
        // A horizontal path has a zero-height objectBoundingBox, so its gradient stroke covers nothing.
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><defs><linearGradient id='g' x1='0' y1='0' x2='1' y2='0'><stop offset='0' stop-color='#f00'/><stop offset='1' stop-color='#00f'/></linearGradient></defs><path d='M2,8 H14' fill='none' stroke='url(#g)' stroke-width='2'/></svg>", out _, out _);

        Assert.Equal(0, AlphaSum(pixels));
    }

    [Fact]
    public void Degenerate_box_linear_gradient_vertical_path_stroke_paints_nothing()
    {
        // A vertical path has a zero-width objectBoundingBox, so its gradient stroke covers nothing.
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><defs><linearGradient id='g' x1='0' y1='0' x2='1' y2='0'><stop offset='0' stop-color='#f00'/><stop offset='1' stop-color='#00f'/></linearGradient></defs><path d='M8,2 V14' fill='none' stroke='url(#g)' stroke-width='2'/></svg>", out _, out _);

        Assert.Equal(0, AlphaSum(pixels));
    }

    [Fact]
    public void Degenerate_box_radial_gradient_path_stroke_paints_nothing()
    {
        // The zero-area box rule also applies to radial objectBoundingBox paints on degenerate paths.
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><defs><radialGradient id='g'><stop offset='0' stop-color='#f00'/><stop offset='1' stop-color='#00f'/></radialGradient></defs><path d='M2,8 H14' fill='none' stroke='url(#g)' stroke-width='2'/></svg>", out _, out _);

        Assert.Equal(0, AlphaSum(pixels));
    }

    [Fact]
    public void Userspace_linear_gradient_path_stroke_paints_band()
    {
        // UserSpaceOnUse paints do not use the bounding box, so the same degenerate path still paints its band.
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><defs><linearGradient id='g' gradientUnits='userSpaceOnUse' x1='2' y1='8' x2='14' y2='8'><stop offset='0' stop-color='#f00'/><stop offset='1' stop-color='#00f'/></linearGradient></defs><path d='M2,8 H14' fill='none' stroke='url(#g)' stroke-width='2'/></svg>", out _, out _);

        Assert.Equal(6120, AlphaSum(pixels));
        Assert.Equal(255, Pixel(pixels, 16, 8, 7).Alpha);
        Assert.Equal(255, Pixel(pixels, 16, 2, 7).Alpha);
    }

    [Fact]
    public void Degenerate_box_linear_gradient_path_use_paints_nothing()
    {
        // Instanced degenerate paths share the paint box, so the use instance also paints nothing.
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><defs><linearGradient id='g' x1='0' y1='0' x2='1' y2='0'><stop offset='0' stop-color='#f00'/><stop offset='1' stop-color='#00f'/></linearGradient></defs><defs><path id='p' d='M2,8 H14' fill='none' stroke='url(#g)' stroke-width='2'/></defs><use href='#p'/></svg>", out _, out _);

        Assert.Equal(0, AlphaSum(pixels));
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
    public void Default_mask_on_horizontal_line_has_zero_object_height()
    {
        // The default mask uses the unstroked object box. A horizontal line has zero height; browser captures paint nothing.
        byte[] masked = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='8'><defs><mask id='m' mask-type='luminance'><rect x='0' y='0' width='16' height='8' fill='#ffffff'/></mask></defs><line x1='0' y1='4' x2='16' y2='4' stroke='red' stroke-width='2' mask='url(#m)'/></svg>", out _, out _);
        byte[] direct = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='8'><line x1='0' y1='4' x2='16' y2='4' stroke='red' stroke-width='2'/></svg>", out _, out _);

        Assert.All(masked, lane => Assert.Equal(0, lane));
        Assert.Contains(direct, lane => lane != 0);
    }

    [Fact]
    public void White_mask_on_path_matches_unmasked_rendering()
    {
        // A white luminance mask is the identity on filled paths, so
        // both spellings render byte-identically.
        byte[] masked = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><defs><mask id='m' mask-type='luminance'><rect x='0' y='0' width='16' height='16' fill='#ffffff'/></mask></defs><path d='M0,0 H16 V8 H0 Z' fill='red' mask='url(#m)'/></svg>", out _, out _);
        byte[] direct = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><path d='M0,0 H16 V8 H0 Z' fill='red'/></svg>", out _, out _);

        Assert.Equal(direct, masked);
    }

    [Fact]
    public void White_masked_gradient_outline_matches_unmasked_outline()
    {
        byte[] masked = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><linearGradient id='g' gradientUnits='userSpaceOnUse' x1='0' y1='0' x2='8' y2='0'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient><mask id='m'><rect x='-8' y='-8' width='24' height='24' fill='#ffffff'/></mask></defs><polygon points='0,0 8,0 0,8' fill='none' stroke='url(#g)' stroke-width='2' mask='url(#m)'/></svg>", out _, out _);
        byte[] bare = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><linearGradient id='g' gradientUnits='userSpaceOnUse' x1='0' y1='0' x2='8' y2='0'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><polygon points='0,0 8,0 0,8' fill='none' stroke='url(#g)' stroke-width='2'/></svg>", out _, out _);

        Assert.Equal(bare, masked);
    }

    [Fact]
    public void Gray_masked_group_outline_preserves_pixels_when_the_mask_definition_moves_forward()
    {
        byte[] grouped = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><linearGradient id='g' gradientUnits='userSpaceOnUse' x1='0' y1='0' x2='8' y2='0'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient><mask id='m'><rect x='-8' y='-8' width='24' height='24' fill='#808080'/></mask></defs><g mask='url(#m)'><polygon points='0,0 8,0 0,8' fill='none' stroke='url(#g)' stroke-width='2'/></g></svg>", out _, out _);
        byte[] forward = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><linearGradient id='g' gradientUnits='userSpaceOnUse' x1='0' y1='0' x2='8' y2='0'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><g mask='url(#m)'><polygon points='0,0 8,0 0,8' fill='none' stroke='url(#g)' stroke-width='2'/></g><defs><mask id='m'><rect x='-8' y='-8' width='24' height='24' fill='#808080'/></mask></defs></svg>", out _, out _);

        Assert.Equal(forward, grouped);
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
    public void Bevel_join_cuts_outer_miter_tip()
    {
        byte[] pixels = RenderPixels("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"64\" height=\"64\"><path d=\"M8,8 L32,8 L32,32 L56,32\" fill=\"none\" stroke=\"red\" stroke-width=\"8\" stroke-linejoin=\"bevel\"/></svg>", out _, out _);

        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, 64, 32, 8));
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(pixels, 64, 35, 5));
    }

    [Fact]
    public void Bevel_join_matches_miter_on_straight_runs()
    {
        byte[] beveled = RenderPixels("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"64\" height=\"64\"><path d=\"M8,8 L32,8 L32,32 L56,32\" fill=\"none\" stroke=\"red\" stroke-width=\"8\" stroke-linejoin=\"bevel\"/></svg>", out _, out _);
        byte[] mitered = RenderPixels("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"64\" height=\"64\"><path d=\"M8,8 L32,8 L32,32 L56,32\" fill=\"none\" stroke=\"red\" stroke-width=\"8\" stroke-linejoin=\"miter\"/></svg>", out _, out _);

        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(beveled, 64, 32, 12));
        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(beveled, 64, 40, 32));
        Assert.NotEqual(mitered, beveled);
    }

    [Fact]
    public void Bevel_join_renders_curved_corners()
    {
        byte[] pixels = RenderPixels("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"64\" height=\"64\"><path d=\"M8,8 L32,8 Q32,32 56,32 L56,56\" fill=\"none\" stroke=\"red\" stroke-width=\"8\" stroke-linejoin=\"bevel\"/></svg>", out _, out _);

        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, 64, 12, 8));
        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, 64, 20, 8));
        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, 64, 56, 50));
        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, 64, 56, 44));
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(pixels, 64, 8, 56));
    }

    [Fact]
    public void Bevel_join_renders_closed_loops()
    {
        byte[] pixels = RenderPixels("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"64\" height=\"64\"><path d=\"M8,8 L56,8 L56,56 L8,56 Z\" fill=\"none\" stroke=\"red\" stroke-width=\"8\" stroke-linejoin=\"bevel\"/></svg>", out _, out _);

        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, 64, 32, 4));
        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, 64, 4, 32));
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(pixels, 64, 32, 60));
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(pixels, 64, 60, 32));
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(pixels, 64, 32, 32));
    }

    [Fact]
    public void Bevel_join_renders_dashed_runs()
    {
        byte[] pixels = RenderPixels("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"64\" height=\"64\"><path d=\"M8,8 L32,8 L32,32 L56,32\" fill=\"none\" stroke=\"red\" stroke-width=\"8\" stroke-linejoin=\"bevel\" stroke-dasharray=\"12 6\"/></svg>", out _, out _);

        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, 64, 12, 8));
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(pixels, 64, 20, 8));
        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, 64, 32, 20));
    }

    [Fact]
    public void Validates_trailing_XML_after_bevel_polygon_strokes()
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"64\" height=\"64\"><polygon points=\"8,8 32,8 32,32\" fill=\"none\" stroke=\"red\" stroke-width=\"8\" stroke-linejoin=\"bevel\"/>");
        SvgException rejected = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.MalformedInput, rejected.ErrorCode);
    }

    [Fact]
    public void Relative_moveto_offsets_from_open_subpath_end()
    {
        byte[] relative = RenderPixels("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"200\" height=\"200\"><path d=\"M8,36h184m0,64H8\" stroke=\"#036\" stroke-width=\"2\"/></svg>", out _, out _);
        byte[] absolute = RenderPixels("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"200\" height=\"200\"><path d=\"M8,36h184M192,100H8\" stroke=\"#036\" stroke-width=\"2\"/></svg>", out _, out _);

        Assert.Equal(absolute, relative);
    }
    [Fact]
    public void Bare_moveto_with_butt_caps_is_noop()
    {
        byte[] dotted = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='200' height='200'><path d='M192,100H8M0,0' stroke='#036' stroke-width='2'/></svg>", out _, out _);
        byte[] plain = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='200' height='200'><path d='M192,100H8' stroke='#036' stroke-width='2'/></svg>", out _, out _);

        Assert.Equal(plain, dotted);
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
    [Fact]
    public void Multi_subpath_stroke_only_paints_all_subpaths()
    {
        // Unpainted fills skip fill validation (degenerate subpaths enclose no area), so every
        // open subpath still strokes, including two-point members.
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='90' height='60'><g fill='none'><path stroke='#007749' stroke-width='12' d='m0 0 45 30h45M0 60l45-30'/></g></svg>", out _, out _);

        Assert.Equal(new SvgColor(0, 119, 73, 255), Pixel(pixels, 90, 70, 30));
        Assert.Equal(new SvgColor(0, 119, 73, 255), Pixel(pixels, 90, 10, 53));
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(pixels, 90, 80, 50));
    }

    [Fact]
    public void Open_compound_gradient_stroke_uses_the_complete_centerline_box()
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='90' height='60'><defs><linearGradient id='g' x1='0' y1='0' x2='1' y2='0'><stop offset='0' stop-color='red'/><stop offset='1' stop-color='blue'/></linearGradient></defs><g fill='none'><path stroke='url(#g)' stroke-width='12' d='m0 0 45 30h45M0 60l45-30'/></g></svg>");

        byte[] pixels = RenderPixels(System.Text.Encoding.UTF8.GetString(input), out int width, out _);
        SvgColor left = Pixel(pixels, width, 8, 4), right = Pixel(pixels, width, 70, 30);
        Assert.Equal(255, left.Alpha);
        Assert.Equal(255, right.Alpha);
        Assert.True(left.Red > left.Blue);
        Assert.True(right.Blue > right.Red);
    }

    [Fact]
    public void Degenerate_subpath_with_mask_stays_unsupported()
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='90' height='60'><defs><mask id='m'><rect width='90' height='60' fill='#ffffff'/></mask></defs><g fill='none'><path stroke='#007749' stroke-width='12' mask='url(#m)' d='m0 0 45 30h45M0 60l45-30'/></g></svg>");

        SvgException rejected = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, rejected.ErrorCode);
        Assert.Contains("closed axis-aligned integer path contours", rejected.Message);
    }
    [Fact]
    public void Round_cap_dot_renders_disc()
    {
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='8'><path d='M8,4h0' fill='none' stroke='red' stroke-width='4' stroke-linecap='round'/></svg>", out _, out _);

        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, 16, 8, 4));
        Assert.NotEqual(0, Pixel(pixels, 16, 6, 4).Alpha);
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(pixels, 16, 5, 4));
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(pixels, 16, 11, 4));
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(pixels, 16, 0, 0));
        Assert.NotEqual(0, AlphaSum(pixels));
    }

    [Fact]
    public void Butt_dot_renders_nothing()
    {
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='8'><path d='M8,4h0' fill='none' stroke='red' stroke-width='4'/></svg>", out _, out _);

        Assert.Equal(0, AlphaSum(pixels));
    }

    [Fact]
    public void Square_cap_dot_renders_square()
    {
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='8'><path d='M8,4h0' fill='none' stroke='red' stroke-width='4' stroke-linecap='square'/></svg>", out _, out _);

        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, 16, 8, 4));
        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, 16, 6, 2));
        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, 16, 9, 5));
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(pixels, 16, 5, 4));
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(pixels, 16, 10, 4));
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(pixels, 16, 8, 1));
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(pixels, 16, 8, 6));
    }

    [Fact]
    public void Zero_length_dash_run_paints_nothing()
    {
        byte[] full = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><path d='M2,8 H14' fill='none' stroke='#000' stroke-width='4' stroke-dasharray='4 2' stroke-linecap='round'/></svg>", out _, out _);
        byte[] trimmed = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><path d='M2,8 H12' fill='none' stroke='#000' stroke-width='4' stroke-dasharray='4 2' stroke-linecap='round'/></svg>", out _, out _);

        Assert.Equal(trimmed, full);
    }

    [Fact]
    public void Bare_moveto_is_empty_but_zero_segment_round_caps_paint_a_dot()
    {
        byte[] bare = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='8'><path d='M8,4' fill='none' stroke='red' stroke-width='4' stroke-linecap='round'/></svg>", out _, out _);
        byte[] segment = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='8'><path d='M8,4h0' fill='none' stroke='red' stroke-width='4' stroke-linecap='round'/></svg>", out _, out _);

        // Stable pinned-browser captures distinguish these two source paths.
        Assert.Equal(0, AlphaSum(bare));
        Assert.Equal(3092, AlphaSum(segment));
    }

    [Fact]
    public void Mixed_square_and_dot_fill_drops_dot()
    {
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='8'><path d='M0,0 H8 V8 H0 Z M12,4h0' fill='red'/></svg>", out _, out _);

        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, 16, 4, 4));
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(pixels, 16, 12, 4));
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

