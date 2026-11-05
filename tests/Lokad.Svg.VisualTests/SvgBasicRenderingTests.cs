using Lokad.Svg.VisualDiff;

namespace Lokad.Svg.VisualTests;

[Trait("Layer", "Rendering")]
public sealed class SvgBasicRenderingTests
{
    [Fact]
    public void Single_integer_rectangle_renders_to_expected_rgba_pixels()
    {
        byte[] png = SvgConverter.ConvertToPng("<svg xmlns='http://www.w3.org/2000/svg' width='2' height='1'><rect width='1' height='1' fill='#ff0000'/></svg>"u8.ToArray(), SvgRenderOptions.Default, CancellationToken.None).ToArray();

        RgbaImage decoded = PngRgbaDecoder.Decode(png, 2);

        Assert.Equal(new byte[] { 255, 0, 0, 255, 0, 0, 0, 0 }, decoded.Pixels);
    }

    [Fact]
    public void Explicit_background_colors_the_transparent_empty_surface()
    {
        SvgRenderOptions options = new(null, 1, new SvgColor(1, 2, 3, 255), SvgRenderLimits.Default);
        byte[] png = SvgConverter.ConvertToPng("<svg xmlns='http://www.w3.org/2000/svg' width='1' height='1'/>"u8.ToArray(), options, CancellationToken.None).ToArray();

        Assert.Equal(new byte[] { 1, 2, 3, 255 }, PngRgbaDecoder.Decode(png, 1).Pixels);
    }

    [Fact]
    public void Opaque_rectangles_follow_document_order()
    {
        byte[] png = SvgConverter.ConvertToPng("<svg xmlns='http://www.w3.org/2000/svg' width='2' height='1'><rect width='2' height='1' fill='#ff0000'/><rect x='1' width='1' height='1' fill='#0000ff'/></svg>"u8.ToArray(), SvgRenderOptions.Default, CancellationToken.None).ToArray();

        Assert.Equal(new byte[] { 255, 0, 0, 255, 0, 0, 255, 255 }, PngRgbaDecoder.Decode(png, 2).Pixels);
    }

    [Fact]
    public void Attribute_free_groups_preserve_rectangle_document_order()
    {
        byte[] png = SvgConverter.ConvertToPng("<svg xmlns='http://www.w3.org/2000/svg' width='1' height='1'><g><rect width='1' height='1' fill='red'/><g><rect width='1' height='1' fill='blue'/></g></g></svg>"u8.ToArray(), SvgRenderOptions.Default, CancellationToken.None).ToArray();

        RgbaImage image = PngRgbaDecoder.Decode(png, 1);

        Assert.Equal(new byte[] { 0, 0, 255, 255 }, image.Pixels);
    }

    [Fact]
    public void Group_fill_and_fill_opacity_are_inherited_by_rectangles()
    {
        byte[] png = SvgConverter.ConvertToPng("<svg xmlns='http://www.w3.org/2000/svg' width='1' height='1'><g fill='blue' fill-opacity='0.5'><rect width='1' height='1'/></g></svg>"u8.ToArray(), SvgRenderOptions.Default, CancellationToken.None).ToArray();

        Assert.Equal(new byte[] { 0, 0, 255, 128 }, PngRgbaDecoder.Decode(png, 1).Pixels);
    }

    [Fact]
    public void Root_fill_and_fill_opacity_are_inherited_by_rectangles()
    {
        byte[] png = SvgConverter.ConvertToPng("<svg xmlns='http://www.w3.org/2000/svg' width='1' height='1' fill='red' fill-opacity='0.5'><rect width='1' height='1'/></svg>"u8.ToArray(), SvgRenderOptions.Default, CancellationToken.None).ToArray();

        Assert.Equal(new byte[] { 255, 0, 0, 128 }, PngRgbaDecoder.Decode(png, 1).Pixels);
    }

    [Fact]
    public void Display_none_on_a_group_hides_its_descendants()
    {
        byte[] png = SvgConverter.ConvertToPng("<svg xmlns='http://www.w3.org/2000/svg' width='1' height='1'><g display='none'><rect width='1' height='1' fill='red'/></g></svg>"u8.ToArray(), SvgRenderOptions.Default, CancellationToken.None).ToArray();

        Assert.Equal(new byte[] { 0, 0, 0, 0 }, PngRgbaDecoder.Decode(png, 1).Pixels);
    }

    [Fact]
    public void Visibility_visible_on_a_child_restores_an_ancestor_hidden_group()
    {
        byte[] png = SvgConverter.ConvertToPng("<svg xmlns='http://www.w3.org/2000/svg' width='1' height='1'><g visibility='hidden'><rect width='1' height='1' fill='red' visibility='visible'/></g></svg>"u8.ToArray(), SvgRenderOptions.Default, CancellationToken.None).ToArray();

        Assert.Equal(new byte[] { 255, 0, 0, 255 }, PngRgbaDecoder.Decode(png, 1).Pixels);
    }

    [Fact]
    public void Rectangle_opacity_multiplies_fill_opacity()
    {
        byte[] png = SvgConverter.ConvertToPng("<svg xmlns='http://www.w3.org/2000/svg' width='1' height='1'><rect width='1' height='1' fill='blue' fill-opacity='0.5' opacity='0.5'/></svg>"u8.ToArray(), SvgRenderOptions.Default, CancellationToken.None).ToArray();

        Assert.Equal(new byte[] { 0, 0, 255, 64 }, PngRgbaDecoder.Decode(png, 1).Pixels);
    }

    [Fact]
    public void Hex_colors_with_alpha_are_supported()
    {
        byte[] png = SvgConverter.ConvertToPng("<svg xmlns='http://www.w3.org/2000/svg' width='2' height='1'><rect width='1' height='1' fill='#1aF8'/><rect x='1' width='1' height='1' fill='#ff000080'/></svg>"u8.ToArray(), SvgRenderOptions.Default, CancellationToken.None).ToArray();

        // Native solid paint rounds initial premultiplication: (170 * 136 + 127) / 255 = 91, which un-premultiplies to 171.
        Assert.Equal(new byte[] { 17, 171, 255, 136, 255, 0, 0, 128 }, PngRgbaDecoder.Decode(png, 2).Pixels);
    }

    [Fact]
    public void Rectangles_with_omitted_dimensions_are_non_painting()
    {
        byte[] png = SvgConverter.ConvertToPng("<svg xmlns='http://www.w3.org/2000/svg' width='1' height='1'><rect fill='red'/></svg>"u8.ToArray(), SvgRenderOptions.Default, CancellationToken.None).ToArray();

        Assert.Equal(new byte[] { 0, 0, 0, 0 }, PngRgbaDecoder.Decode(png, 1).Pixels);
    }

    [Fact]
    public void Extreme_off_canvas_rectangle_coordinates_do_not_overflow_or_paint()
    {
        byte[] png = SvgConverter.ConvertToPng("<svg xmlns='http://www.w3.org/2000/svg' width='1' height='1'><rect x='2147483647' y='2147483647' width='1' height='1' fill='red'/></svg>"u8.ToArray(), SvgRenderOptions.Default, CancellationToken.None).ToArray();

        Assert.Equal(new byte[] { 0, 0, 0, 0 }, PngRgbaDecoder.Decode(png, 1).Pixels);
    }

    [Fact]
    public void Extreme_orthogonal_path_coordinates_preserve_visible_coverage()
    {
        byte[] png = SvgConverter.ConvertToPng("<svg xmlns='http://www.w3.org/2000/svg' width='1' height='1'><path d='M -2147483648,0 H 2147483647 V 1 H -2147483648 Z' fill='red'/></svg>"u8.ToArray(), SvgRenderOptions.Default, CancellationToken.None).ToArray();

        Assert.Equal(new byte[] { 255, 0, 0, 255 }, PngRgbaDecoder.Decode(png, 1).Pixels);
    }

    [Fact]
    public void Comma_form_rgb_and_rgba_colors_are_supported()
    {
        byte[] png = SvgConverter.ConvertToPng("<svg xmlns='http://www.w3.org/2000/svg' width='2' height='1'><rect width='1' height='1' fill='rgb(1, 2, 3)'/><rect x='1' width='1' height='1' fill='rgba(4,5,6,0.5)'/></svg>"u8.ToArray(), SvgRenderOptions.Default, CancellationToken.None).ToArray();

        // Native solid paint rounds initial premultiplication: (5 * 128 + 127) / 255 = 3, which un-premultiplies to 6.
        Assert.Equal(new byte[] { 1, 2, 3, 255, 4, 6, 6, 128 }, PngRgbaDecoder.Decode(png, 2).Pixels);
    }

    [Fact]
    public void Output_scale_scales_integer_rectangle_geometry()
    {
        SvgRenderOptions options = new(null, 2, SvgColor.Transparent, SvgRenderLimits.Default);
        byte[] png = SvgConverter.ConvertToPng("<svg xmlns='http://www.w3.org/2000/svg' width='1' height='1'><rect width='1' height='1' fill='red'/></svg>"u8.ToArray(), options, CancellationToken.None).ToArray();

        Assert.Equal(new byte[] { 255, 0, 0, 255, 255, 0, 0, 255, 255, 0, 0, 255, 255, 0, 0, 255 }, PngRgbaDecoder.Decode(png, 4).Pixels);
    }

    [Fact]
    public void Absolute_root_length_units_use_the_96_css_pixel_conversion()
    {
        byte[] png = SvgConverter.ConvertToPng("<svg xmlns='http://www.w3.org/2000/svg' width='0.5in' height='12pt'/>"u8.ToArray(), SvgRenderOptions.Default, CancellationToken.None).ToArray();

        RgbaImage image = PngRgbaDecoder.Decode(png, 48 * 16);
        Assert.Equal(48, image.Width);
        Assert.Equal(16, image.Height);
    }

    [Fact]
    public void Unreferenced_defs_are_non_painting()
    {
        byte[] png = SvgConverter.ConvertToPng("<svg xmlns='http://www.w3.org/2000/svg' width='1' height='1'><defs><rect width='1' height='1' fill='blue'/></defs><rect width='1' height='1' fill='red'/></svg>"u8.ToArray(), SvgRenderOptions.Default, CancellationToken.None).ToArray();

        Assert.Equal(new byte[] { 255, 0, 0, 255 }, PngRgbaDecoder.Decode(png, 1).Pixels);
    }

    [Fact]
    public void Inert_id_attributes_do_not_change_supported_rendering()
    {
        byte[] png = SvgConverter.ConvertToPng("<svg xmlns='http://www.w3.org/2000/svg' width='1' height='1' id='canvas'><g id='layer'><rect id='box' width='1' height='1' fill='red'/></g></svg>"u8.ToArray(), SvgRenderOptions.Default, CancellationToken.None).ToArray();

        Assert.Equal(new byte[] { 255, 0, 0, 255 }, PngRgbaDecoder.Decode(png, 1).Pixels);
    }

    [Fact]
    public void Link_containers_preserve_static_rectangle_rendering()
    {
        byte[] png = SvgConverter.ConvertToPng("<svg xmlns='http://www.w3.org/2000/svg' width='1' height='1'><a href='https://example.test'><rect width='1' height='1' fill='red'/></a></svg>"u8.ToArray(), SvgRenderOptions.Default, CancellationToken.None).ToArray();

        Assert.Equal(new byte[] { 255, 0, 0, 255 }, PngRgbaDecoder.Decode(png, 1).Pixels);
    }

    [Fact]
    public void Css_color_tokens_are_case_insensitive()
    {
        byte[] png = SvgConverter.ConvertToPng("<svg xmlns='http://www.w3.org/2000/svg' width='2' height='1'><rect width='1' height='1' fill='RED'/><rect x='1' width='1' height='1' fill='RGB(0,0,255)'/></svg>"u8.ToArray(), SvgRenderOptions.Default, CancellationToken.None).ToArray();

        Assert.Equal(new byte[] { 255, 0, 0, 255, 0, 0, 255, 255 }, PngRgbaDecoder.Decode(png, 2).Pixels);
    }

    [Fact]
    public void Rgb_percentage_channels_are_supported()
    {
        byte[] png = SvgConverter.ConvertToPng("<svg xmlns='http://www.w3.org/2000/svg' width='1' height='1'><rect width='1' height='1' fill='rgb(100%,50%,0%)'/></svg>"u8.ToArray(), SvgRenderOptions.Default, CancellationToken.None).ToArray();
        Assert.Equal(new byte[] { 255, 128, 0, 255 }, PngRgbaDecoder.Decode(png, 1).Pixels);
    }

    [Fact]
    public void Integer_rectangle_geometry_accepts_px_units()
    {
        byte[] png = SvgConverter.ConvertToPng("<svg xmlns='http://www.w3.org/2000/svg' width='1' height='1'><rect x='0px' y='0px' width='1px' height='1px' fill='red'/></svg>"u8.ToArray(), SvgRenderOptions.Default, CancellationToken.None).ToArray();
        Assert.Equal(new byte[] { 255, 0, 0, 255 }, PngRgbaDecoder.Decode(png, 1).Pixels);
    }

    [Fact]
    public void Classic_hsl_colors_are_supported()
    {
        byte[] png = SvgConverter.ConvertToPng("<svg xmlns='http://www.w3.org/2000/svg' width='1' height='1'><rect width='1' height='1' fill='hsl(120,100%,50%)'/></svg>"u8.ToArray(), SvgRenderOptions.Default, CancellationToken.None).ToArray();
        Assert.Equal(new byte[] { 0, 255, 0, 255 }, PngRgbaDecoder.Decode(png, 1).Pixels);
    }

    [Fact]
    public void Functional_color_alpha_accepts_percentages()
    {
        byte[] png = SvgConverter.ConvertToPng("<svg xmlns='http://www.w3.org/2000/svg' width='1' height='1'><rect width='1' height='1' fill='rgba(255,0,0,50%)'/></svg>"u8.ToArray(), SvgRenderOptions.Default, CancellationToken.None).ToArray();
        Assert.Equal(new byte[] { 255, 0, 0, 128 }, PngRgbaDecoder.Decode(png, 1).Pixels);
    }

    [Fact]
    public void Current_color_inherits_through_groups()
    {
        byte[] png = SvgConverter.ConvertToPng("<svg xmlns='http://www.w3.org/2000/svg' width='1' height='1' color='blue'><g><rect width='1' height='1' fill='currentColor'/></g></svg>"u8.ToArray(), SvgRenderOptions.Default, CancellationToken.None).ToArray();
        Assert.Equal(new byte[] { 0, 0, 255, 255 }, PngRgbaDecoder.Decode(png, 1).Pixels);
    }

    [Fact]
    public void Rectangle_fill_rules_are_accepted()
    {
        byte[] png = SvgConverter.ConvertToPng("<svg xmlns='http://www.w3.org/2000/svg' width='1' height='1'><rect width='1' height='1' fill='red' fill-rule='evenodd'/></svg>"u8.ToArray(), SvgRenderOptions.Default, CancellationToken.None).ToArray();
        Assert.Equal(new byte[] { 255, 0, 0, 255 }, PngRgbaDecoder.Decode(png, 1).Pixels);
    }

    [Fact]
    public void Non_painting_rectangle_strokes_are_accepted()
    {
        byte[] png = SvgConverter.ConvertToPng("<svg xmlns='http://www.w3.org/2000/svg' width='1' height='1'><rect width='1' height='1' fill='red' stroke='none'/></svg>"u8.ToArray(), SvgRenderOptions.Default, CancellationToken.None).ToArray();
        Assert.Equal(new byte[] { 255, 0, 0, 255 }, PngRgbaDecoder.Decode(png, 1).Pixels);
    }

    [Fact]
    public void Css_keyword_values_are_case_insensitive()
    {
        byte[] png = SvgConverter.ConvertToPng("<svg xmlns='http://www.w3.org/2000/svg' width='1' height='1'><rect width='1' height='1' fill='red' fill-rule='EVENODD' display='INLINE' visibility='VISIBLE'/></svg>"u8.ToArray(), SvgRenderOptions.Default, CancellationToken.None).ToArray();
        Assert.Equal(new byte[] { 255, 0, 0, 255 }, PngRgbaDecoder.Decode(png, 1).Pixels);
    }

    [Fact]
    public void Non_painting_strokes_are_accepted_on_containers()
    {
        byte[] png = SvgConverter.ConvertToPng("<svg xmlns='http://www.w3.org/2000/svg' width='1' height='1' stroke='none'><g stroke='none'><rect width='1' height='1' fill='red'/></g></svg>"u8.ToArray(), SvgRenderOptions.Default, CancellationToken.None).ToArray();
        Assert.Equal(new byte[] { 255, 0, 0, 255 }, PngRgbaDecoder.Decode(png, 1).Pixels);
    }

    [Fact]
    public void Explicit_zero_rectangle_corner_radii_are_accepted()
    {
        byte[] png = SvgConverter.ConvertToPng("<svg xmlns='http://www.w3.org/2000/svg' width='1' height='1'><rect width='1' height='1' rx='0' ry='0' fill='red'/></svg>"u8.ToArray(), SvgRenderOptions.Default, CancellationToken.None).ToArray();
        Assert.Equal(new byte[] { 255, 0, 0, 255 }, PngRgbaDecoder.Decode(png, 1).Pixels);
    }

    [Fact]
    public void Named_lime_rectangle_fill_is_supported()
    {
        byte[] png = SvgConverter.ConvertToPng("<svg xmlns='http://www.w3.org/2000/svg' width='1' height='1'><rect width='1' height='1' fill='lime'/></svg>"u8.ToArray(), SvgRenderOptions.Default, CancellationToken.None).ToArray();
        Assert.Equal(new byte[] { 0, 255, 0, 255 }, PngRgbaDecoder.Decode(png, 1).Pixels);
    }

    [Fact]
    public void Integer_fill_opacity_composites_on_transparent_surface()
    {
        byte[] png = SvgConverter.ConvertToPng("<svg xmlns='http://www.w3.org/2000/svg' width='1' height='1'><rect width='1' height='1' fill='#ff0000' fill-opacity='0.5'/></svg>"u8.ToArray(), SvgRenderOptions.Default, CancellationToken.None).ToArray();

        Assert.Equal(new byte[] { 255, 0, 0, 128 }, PngRgbaDecoder.Decode(png, 1).Pixels);
    }

    [Fact]
    public void Full_canvas_rectangles_preserve_source_opacity_until_solid_color_quantization()
    {
        byte[] png = SvgConverter.ConvertToPng("<svg xmlns='http://www.w3.org/2000/svg' width='1' height='1'><rect width='1' height='1' fill='red' fill-opacity='0.5'/><rect width='1' height='1' fill='blue' fill-opacity='0.5'/></svg>"u8.ToArray(), SvgRenderOptions.Default, CancellationToken.None).ToArray();

        RgbaImage image = PngRgbaDecoder.Decode(png, 1);

        // Three recipe-3 browser captures at 1x1 and 2x2 establish 191 for this scene.
        // The separately retained inset/path and encoded-alpha controls establish 192.
        Assert.Equal(new byte[] { 85, 0, 170, 191 }, image.Pixels);
    }

    [Fact]
    public void Integer_fill_opacity_composites_source_over_an_opaque_background()
    {
        SvgRenderOptions options = new(null, 1, new SvgColor(0, 0, 255, 255), SvgRenderLimits.Default);
        byte[] png = SvgConverter.ConvertToPng("<svg xmlns='http://www.w3.org/2000/svg' width='1' height='1'><rect width='1' height='1' fill='#ff0000' fill-opacity='0.5'/></svg>"u8.ToArray(), options, CancellationToken.None).ToArray();

        Assert.Equal(new byte[] { 128, 0, 128, 255 }, PngRgbaDecoder.Decode(png, 1).Pixels);
    }

    [Fact]
    public void Short_hex_rectangle_fill_expands_each_color_nibble()
    {
        byte[] png = SvgConverter.ConvertToPng("<svg xmlns='http://www.w3.org/2000/svg' width='1' height='1'><rect width='1' height='1' fill='#1aF'/></svg>"u8.ToArray(), SvgRenderOptions.Default, CancellationToken.None).ToArray();
        Assert.Equal(new byte[] { 17, 170, 255, 255 }, PngRgbaDecoder.Decode(png, 1).Pixels);
    }

    [Fact]
    public void Integral_decimal_rectangle_coordinates_are_accepted()
    {
        byte[] png = SvgConverter.ConvertToPng("<svg xmlns='http://www.w3.org/2000/svg' width='1' height='1'><rect x='0.0' y='0e0' width='1.0' height='1.00' fill='#f00'/></svg>"u8.ToArray(), SvgRenderOptions.Default, CancellationToken.None).ToArray();
        Assert.Equal(new byte[] { 255, 0, 0, 255 }, PngRgbaDecoder.Decode(png, 1).Pixels);
    }

    [Fact]
    public void None_fill_leaves_the_surface_unpainted()
    {
        byte[] png = SvgConverter.ConvertToPng("<svg xmlns='http://www.w3.org/2000/svg' width='1' height='1'><rect width='1' height='1' fill='none'/></svg>"u8.ToArray(), SvgRenderOptions.Default, CancellationToken.None).ToArray();
        Assert.Equal(new byte[] { 0, 0, 0, 0 }, PngRgbaDecoder.Decode(png, 1).Pixels);
    }

    [Fact]
    public void Named_red_rectangle_fill_is_supported()
    {
        byte[] png = SvgConverter.ConvertToPng("<svg xmlns='http://www.w3.org/2000/svg' width='1' height='1'><rect width='1' height='1' fill='red'/></svg>"u8.ToArray(), SvgRenderOptions.Default, CancellationToken.None).ToArray();
        Assert.Equal(new byte[] { 255, 0, 0, 255 }, PngRgbaDecoder.Decode(png, 1).Pixels);
    }

    [Fact]
    public void Rectangle_without_fill_uses_the_svg_black_default()
    {
        byte[] png = SvgConverter.ConvertToPng("<svg xmlns='http://www.w3.org/2000/svg' width='1' height='1'><rect width='1' height='1'/></svg>"u8.ToArray(), SvgRenderOptions.Default, CancellationToken.None).ToArray();

        RgbaImage image = PngRgbaDecoder.Decode(png, 1);

        Assert.Equal(new byte[] { 0, 0, 0, 255 }, image.Pixels);
    }

    [Fact]
    public void Named_blue_rectangle_fill_is_supported()
    {
        byte[] png = SvgConverter.ConvertToPng("<svg xmlns='http://www.w3.org/2000/svg' width='1' height='1'><rect width='1' height='1' fill='blue'/></svg>"u8.ToArray(), SvgRenderOptions.Default, CancellationToken.None).ToArray();
        Assert.Equal(new byte[] { 0, 0, 255, 255 }, PngRgbaDecoder.Decode(png, 1).Pixels);
    }

    [Fact]
    public void Transparent_fill_leaves_the_surface_unpainted()
    {
        byte[] png = SvgConverter.ConvertToPng("<svg xmlns='http://www.w3.org/2000/svg' width='1' height='1'><rect width='1' height='1' fill='transparent'/></svg>"u8.ToArray(), SvgRenderOptions.Default, CancellationToken.None).ToArray();
        Assert.Equal(new byte[] { 0, 0, 0, 0 }, PngRgbaDecoder.Decode(png, 1).Pixels);
    }
}
