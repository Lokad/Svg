using Lokad.Svg.VisualDiff;
using Lokad.Svg.Encoding;
using System.Buffers.Binary;
using System.IO.Compression;

namespace Lokad.Svg.VisualTests;

public sealed class PixelComparerTests
{
    private static readonly byte[] OnePixelRgbaPng = CreateOnePixelRgbaPng();

    [Fact]
    public void One_channel_difference_is_an_exact_failure()
    {
        RgbaImage reference = new(1, 1, [10, 20, 30, 40]);
        RgbaImage candidate = new(1, 1, [10, 21, 30, 40]);

        PixelComparison result = PixelComparer.Compare(reference, candidate);

        Assert.True(result.DimensionsMatch);
        Assert.Equal(1, result.ChangedPixelCount);
        Assert.Equal(1, result.MaximumChannelError);
        Assert.Equal(0, result.MaximumAlphaError);
        Assert.Equal(0, result.MinimumMismatchX);
        Assert.Equal(0, result.MaximumMismatchY);
    }

    [Fact]
    public void Invisible_rgb_is_canonicalized_but_alpha_is_not()
    {
        RgbaImage reference = new(1, 1, [1, 2, 3, 0]);
        RgbaImage candidate = new(1, 1, [200, 100, 50, 0]);

        PixelComparison result = PixelComparer.Compare(reference, candidate);

        Assert.Equal(0, result.ChangedPixelCount);
    }

    [Fact]
    public void Alpha_differences_are_reported_separately_with_mismatch_bounds()
    {
        RgbaImage reference = new(2, 1, [0, 0, 0, 0, 0, 0, 0, 12]);
        RgbaImage candidate = new(2, 1, [0, 0, 0, 0, 0, 0, 0, 20]);

        PixelComparison result = PixelComparer.Compare(reference, candidate);

        Assert.Equal(1, result.AlphaChangedPixelCount);
        Assert.Equal(8, result.MaximumAlphaError);
        Assert.Equal(1, result.MinimumMismatchX);
        Assert.Equal(1, result.MaximumMismatchX);
        Assert.Equal(0, result.MinimumMismatchY);
    }

    [Fact]
    public void Dimension_difference_fails_without_intersection_comparison()
    {
        PixelComparison result = PixelComparer.Compare(new RgbaImage(1, 1, [0, 0, 0, 0]), new RgbaImage(2, 1, [0, 0, 0, 0, 0, 0, 0, 0]));

        Assert.False(result.DimensionsMatch);
    }

    [Fact]
    public void Independent_decoder_reads_a_known_rgba_png()
    {
        RgbaImage image = PngRgbaDecoder.Decode(OnePixelRgbaPng, 1);

        Assert.Equal(1, image.Width);
        Assert.Equal(1, image.Height);
        Assert.Equal(new byte[] { 10, 20, 30, 40 }, image.Pixels);
    }

    [Fact]
    public void Independent_decoder_rejects_a_corrupted_chunk()
    {
        byte[] corrupted = (byte[])OnePixelRgbaPng.Clone();
        corrupted[20] ^= 1;

        Assert.Throws<InvalidDataException>(() => PngRgbaDecoder.Decode(corrupted, 1));
    }

    [Fact]
    public void Independent_decoder_enforces_its_pixel_limit()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PngRgbaDecoder.Decode(OnePixelRgbaPng, 0));
    }

    [Fact]
    public void Independent_decoder_reads_production_encoder_output()
    {
        byte[] source = [10, 20, 30, 40, 50, 60, 70, 80];
        byte[] png = PngEncoder.Encode(2, 1, source, CancellationToken.None);

        RgbaImage decoded = PngRgbaDecoder.Decode(png, 2);

        Assert.Equal(source, decoded.Pixels);
    }

    [Fact]
    public void Production_encoder_canonicalizes_invisible_rgb()
    {
        byte[] png = PngEncoder.Encode(1, 1, [10, 20, 30, 0], CancellationToken.None);
        Assert.Equal(new byte[] { 0, 0, 0, 0 }, PngRgbaDecoder.Decode(png, 1).Pixels);
    }

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

        // Pinned premultiplied backend: (170 * 137 >> 8) * 255 + 68 over 136 un-premultiplies to 169.
        Assert.Equal(new byte[] { 17, 169, 255, 136, 255, 0, 0, 128 }, PngRgbaDecoder.Decode(png, 2).Pixels);
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

        // Pinned premultiplied backend: (5 * 129 >> 8) * 255 + 64 over 128 un-premultiplies to 4.
        // Pinned backend: rgba alpha truncates (0.5 reads 127); mid-tone un-premultiplies to 4.
        Assert.Equal(new byte[] { 1, 2, 3, 255, 4, 4, 6, 128 }, PngRgbaDecoder.Decode(png, 2).Pixels);
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

    private static byte[] CreateOnePixelRgbaPng()
    {
        using MemoryStream result = new();
        result.Write([137, 80, 78, 71, 13, 10, 26, 10]);
        WriteChunk(result, "IHDR"u8, [0, 0, 0, 1, 0, 0, 0, 1, 8, 6, 0, 0, 0]);
        using MemoryStream compressed = new();
        using (ZLibStream zlib = new(compressed, CompressionLevel.NoCompression, true)) zlib.Write([0, 10, 20, 30, 40]);
        WriteChunk(result, "IDAT"u8, compressed.ToArray());
        WriteChunk(result, "IEND"u8, []);
        return result.ToArray();
    }

    private static void WriteChunk(Stream destination, ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(length, checked((uint)data.Length));
        destination.Write(length);
        destination.Write(type);
        destination.Write(data);
        uint crc = 0xffffffff;
        foreach (byte value in type) crc = CrcStep(crc, value);
        foreach (byte value in data) crc = CrcStep(crc, value);
        BinaryPrimitives.WriteUInt32BigEndian(length, ~crc);
        destination.Write(length);
    }

    private static uint CrcStep(uint crc, byte value)
    {
        crc ^= value;
        for (int bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) == 0 ? 0u : 0xedb88320u);
        return crc;
    }
}
