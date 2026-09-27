using System.Buffers.Binary;
using System.IO.Compression;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgUseInstanceTests
{
    [Fact]
    public void Direct_use_matches_direct_rendering()
    {
        byte[] direct = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><polygon points='0,0 8,0 0,8' fill='red'/></svg>", out _, out _);
        byte[] instanced = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><polygon id='p' points='0,0 8,0 0,8' fill='red'/></defs><use href='#p'/></svg>", out _, out _);

        Assert.Equal(direct, instanced);
    }

    [Fact]
    public void Pattern_path_use_matches_direct_rendering()
    {
        string pattern = "<defs><pattern id='p' width='4' height='4' patternUnits='userSpaceOnUse'><rect width='2' height='2' fill='red'/></pattern></defs>";
        byte[] direct = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'>" + pattern + "<path d='M2,2 H14 V14 H2 Z' fill='url(#p)'/></svg>", out _, out _);
        byte[] instanced = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'>" + pattern + "<defs><path id='r' d='M2,2 H14 V14 H2 Z' fill='url(#p)'/></defs><use href='#r'/></svg>", out _, out _);

        Assert.Equal(direct, instanced);
    }

    [Fact]
    public void Use_offset_matches_shifted_rendering()
    {
        byte[] shifted = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><polygon points='2,1 6,1 2,5' fill='red'/></svg>", out _, out _);
        byte[] instanced = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><polygon id='p' points='0,0 4,0 0,4' fill='red'/></defs><use href='#p' x='2' y='1'/></svg>", out _, out _);

        Assert.Equal(shifted, instanced);
    }

    [Fact]
    public void Use_scale_matches_scaled_rendering()
    {
        byte[] scaled = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><polygon points='0,0 8,0 0,8' fill='red'/></svg>", out _, out _);
        byte[] instanced = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><polygon id='p' points='0,0 4,0 0,4' fill='red'/></defs><use href='#p' transform='scale(2)'/></svg>", out _, out _);

        Assert.Equal(scaled, instanced);
    }

    [Fact]
    public void Forward_reference_matches_backward_reference()
    {
        byte[] backward = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><polygon id='p' points='0,0 8,0 0,8' fill='red'/></defs><use href='#p'/></svg>", out _, out _);
        byte[] forward = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><use href='#p'/><defs><polygon id='p' points='0,0 8,0 0,8' fill='red'/></defs></svg>", out _, out _);

        Assert.Equal(backward, forward);
    }

    [Fact]
    public void Orthogonal_bands_instance_matches_direct()
    {
        byte[] direct = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><polygon points='0,0 8,0 8,4 4,4 4,8 0,8' fill='red'/></svg>", out _, out _);
        byte[] instanced = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><polygon id='p' points='0,0 8,0 8,4 4,4 4,8 0,8' fill='red'/></defs><use href='#p'/></svg>", out _, out _);

        Assert.Equal(direct, instanced);
    }

    [Fact]
    public void Dashed_line_use_matches_direct()
    {
        byte[] direct = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='8'><line x1='0' y1='4' x2='16' y2='4' stroke='red' stroke-width='2' stroke-dasharray='4 4'/></svg>", out _, out _);
        byte[] instanced = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='8'><defs><line id='d' x1='0' y1='4' x2='16' y2='4' stroke='red' stroke-width='2' stroke-dasharray='4 4'/></defs><use href='#d'/></svg>", out _, out _);

        Assert.Equal(direct, instanced);
    }

    [Fact]
    public void Square_capped_dashed_line_use_matches_direct()
    {
        byte[] direct = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='8'><line x1='0' y1='4' x2='16' y2='4' stroke='red' stroke-width='2' stroke-dasharray='4 4' stroke-linecap='square'/></svg>", out _, out _);
        byte[] instanced = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='8'><defs><line id='d' x1='0' y1='4' x2='16' y2='4' stroke='red' stroke-width='2' stroke-dasharray='4 4' stroke-linecap='square'/></defs><use href='#d'/></svg>", out _, out _);

        Assert.Equal(direct, instanced);
    }

    [Fact]
    public void Non_uniform_dashed_line_use_matches_scaled_direct()
    {
        byte[] direct = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='8'><line x1='0' y1='4' x2='16' y2='4' stroke='red' stroke-width='2' stroke-dasharray='8 8'/></svg>", out _, out _);
        byte[] instanced = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='8'><defs><line id='d' x1='0' y1='4' x2='8' y2='4' stroke='red' stroke-width='2' stroke-dasharray='4 4'/></defs><use href='#d' transform='scale(2 1)'/></svg>", out _, out _);

        Assert.Equal(direct, instanced);
    }

    [Fact]
    public void Symbol_dashed_line_matches_direct_rendering()
    {
        byte[] direct = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='8'><line x1='0' y1='4' x2='16' y2='4' stroke='red' stroke-width='2' stroke-dasharray='4 4'/></svg>", out _, out _);
        byte[] instanced = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='8'><defs><symbol id='s' viewBox='0 0 16 8'><line x1='0' y1='4' x2='16' y2='4' stroke='red' stroke-width='2' stroke-dasharray='4 4'/></symbol></defs><use href='#s' width='16' height='8'/></svg>", out _, out _);

        Assert.Equal(direct, instanced);
    }

    [Fact]
    public void Stroked_definition_instances_both_geometries()
    {
        byte[] direct = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><polygon points='0,0 8,0 8,8 0,8' fill='red' stroke='blue' stroke-width='2'/></svg>", out _, out _);
        byte[] instanced = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><polygon id='p' points='0,0 8,0 8,8 0,8' fill='red' stroke='blue' stroke-width='2'/></defs><use href='#p'/></svg>", out _, out _);

        Assert.Equal(direct, instanced);
    }

    [Fact]
    public void Use_opacity_matches_fill_opacity()
    {
        byte[] faded = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><polygon points='0,0 8,0 0,8' fill='red' fill-opacity='0.5'/></svg>", out _, out _);
        byte[] instanced = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><polygon id='p' points='0,0 8,0 0,8' fill='red'/></defs><use href='#p' opacity='0.5'/></svg>", out _, out _);

        Assert.Equal(faded, instanced);
    }

    [Fact]
    public void Use_clip_matches_direct_clip()
    {
        byte[] direct = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><clipPath id='c'><rect x='4' y='0' width='4' height='8'/></clipPath></defs><polygon points='0,0 8,0 0,8' fill='red' clip-path='url(#c)'/></svg>", out _, out _);
        byte[] instanced = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><clipPath id='c'><rect x='4' y='0' width='4' height='8'/></clipPath></defs><defs><polygon id='p' points='0,0 8,0 0,8' fill='red'/></defs><use href='#p' clip-path='url(#c)'/></svg>", out _, out _);

        Assert.Equal(direct, instanced);
    }

    [Fact]
    public void Circle_definition_instances()
    {
        byte[] direct = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><circle cx='8' cy='8' r='6' fill='red'/></svg>", out _, out _);
        byte[] instanced = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><defs><circle id='c' cx='8' cy='8' r='6' fill='red'/></defs><use href='#c'/></svg>", out _, out _);

        Assert.Equal(direct, instanced);
    }

    [Fact]
    public void Forward_multi_geometry_use_matches_backward_reference()
    {
        byte[] backward = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><polygon id='p' points='0,0 8,0 8,8 0,8' fill='red' stroke='blue' stroke-width='2'/></defs><use href='#p'/></svg>", out _, out _);
        byte[] forward = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><use href='#p'/><defs><polygon id='p' points='0,0 8,0 8,8 0,8' fill='red' stroke='blue' stroke-width='2'/></defs></svg>", out _, out _);

        Assert.Equal(backward, forward);
    }

    [Fact]
    public void Forward_bands_use_preserves_following_content_order()
    {
        byte[] backward = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><polygon id='p' points='0,0 8,0 8,4 4,4 4,8 0,8' fill='red'/></defs><use href='#p'/><rect x='4' y='0' width='4' height='8' fill='blue'/></svg>", out _, out _);
        byte[] forward = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><use href='#p'/><rect x='4' y='0' width='4' height='8' fill='blue'/><defs><polygon id='p' points='0,0 8,0 8,4 4,4 4,8 0,8' fill='red'/></defs></svg>", out _, out _);

        Assert.Equal(backward, forward);
    }

    [Fact]
    public void ViewBox_less_symbol_polygon_instances_directly()
    {
        byte[] direct = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><polygon points='0,0 8,0 0,8' fill='red'/></svg>", out _, out _);
        byte[] instanced = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><symbol id='s'><polygon points='0,0 8,0 0,8' fill='red'/></symbol></defs><use href='#s' width='8' height='8'/></svg>", out _, out _);

        Assert.Equal(direct, instanced);
    }

    [Fact]
    public void Symbol_polygon_matches_direct_rendering()
    {
        byte[] direct = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><polygon points='0,0 8,0 0,8' fill='red'/></svg>", out _, out _);
        byte[] instanced = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><symbol id='s' viewBox='0 0 8 8'><polygon points='0,0 8,0 0,8' fill='red'/></symbol></defs><use href='#s' width='8' height='8'/></svg>", out _, out _);

        Assert.Equal(direct, instanced);
    }

    [Fact]
    public void Scaled_symbol_polygon_matches_direct_rendering()
    {
        byte[] direct = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><polygon points='0,0 8,0 0,8' fill='red'/></svg>", out _, out _);
        byte[] instanced = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><symbol id='s' viewBox='0 0 4 4'><polygon points='0,0 4,0 0,4' fill='red'/></symbol></defs><use href='#s' width='8' height='8'/></svg>", out _, out _);

        Assert.Equal(direct, instanced);
    }

    [Fact]
    public void Offset_symbol_polygon_matches_shifted_rendering()
    {
        byte[] shifted = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><polygon points='2,1 6,1 2,5' fill='red'/></svg>", out _, out _);
        byte[] instanced = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><symbol id='s' viewBox='0 0 4 4'><polygon points='0,0 4,0 0,4' fill='red'/></symbol></defs><use href='#s' x='2' y='1' width='4' height='4'/></svg>", out _, out _);

        Assert.Equal(shifted, instanced);
    }

    [Fact]
    public void Symbol_polygon_viewBox_origin_matches_direct_rendering()
    {
        byte[] direct = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='4' height='4'><polygon points='0,0 4,0 0,4' fill='red'/></svg>", out _, out _);
        byte[] instanced = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='4' height='4'><defs><symbol id='s' viewBox='4 0 4 4'><polygon points='4,0 8,0 4,4' fill='red'/></symbol></defs><use href='#s' width='4' height='4'/></svg>", out _, out _);

        Assert.Equal(direct, instanced);
    }

    [Fact]
    public void Symbol_polygon_opacity_matches_fill_opacity()
    {
        byte[] faded = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><polygon points='0,0 8,0 0,8' fill='red' fill-opacity='0.5'/></svg>", out _, out _);
        byte[] instanced = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><symbol id='s' viewBox='0 0 8 8'><polygon points='0,0 8,0 0,8' fill='red'/></symbol></defs><use href='#s' width='8' height='8' opacity='0.5'/></svg>", out _, out _);

        Assert.Equal(faded, instanced);
    }

    [Fact]
    public void Forward_symbol_polygon_matches_backward_reference()
    {
        byte[] backward = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><symbol id='s' viewBox='0 0 8 8'><polygon points='0,0 8,0 0,8' fill='red'/></symbol></defs><use href='#s' width='8' height='8'/></svg>", out _, out _);
        byte[] forward = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><use href='#s' width='8' height='8'/><defs><symbol id='s' viewBox='0 0 8 8'><polygon points='0,0 8,0 0,8' fill='red'/></symbol></defs></svg>", out _, out _);

        Assert.Equal(backward, forward);
    }

    [Fact]
    public void Symbol_path_matches_direct_rendering()
    {
        byte[] direct = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><path d='M0,0 L8,0 L0,8 Z' fill='red'/></svg>", out _, out _);
        byte[] instanced = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><symbol id='s' viewBox='0 0 4 4'><path d='M0,0 L4,0 L0,4 Z' fill='red'/></symbol></defs><use href='#s' width='8' height='8'/></svg>", out _, out _);

        Assert.Equal(direct, instanced);
    }

    [Fact]
    public void Symbol_orthogonal_band_matches_direct_rendering()
    {
        byte[] direct = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><polygon points='0,0 8,0 8,8 0,8' fill='red'/></svg>", out _, out _);
        byte[] instanced = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><symbol id='s' viewBox='0 0 4 4'><polygon points='0,0 4,0 4,4 0,4' fill='red'/></symbol></defs><use href='#s' width='8' height='8'/></svg>", out _, out _);

        Assert.Equal(direct, instanced);
    }

    [Fact]
    public void Non_uniform_symbol_polygon_stays_unsupported()
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='4'><defs><symbol id='s' viewBox='0 0 4 4' preserveAspectRatio='none'><polygon points='0,0 4,0 0,4' fill='red'/></symbol></defs><use href='#s' width='8' height='4'/></svg>");

        SvgException rejected = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, rejected.ErrorCode);
    }

    [Fact]
    public void Fill_override_on_polygon_stays_unsupported()
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><polygon id='p' points='0,0 8,0 0,8' fill='red'/></defs><use href='#p' fill='blue'/></svg>");

        SvgException rejected = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, rejected.ErrorCode);
    }

    [Fact]
    public void White_masked_polygon_matches_unmasked_rendering()
    {
        byte[] direct = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><polygon points='0,0 8,0 0,8' fill='red'/></svg>", out _, out _);
        byte[] masked = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><mask id='m'><rect x='0' y='0' width='8' height='8' fill='#ffffff'/></mask></defs><polygon points='0,0 8,0 0,8' fill='red' mask='url(#m)'/></svg>", out _, out _);

        Assert.Equal(direct, masked);
    }

    [Fact]
    public void Masked_polygon_use_matches_masked_direct_rendering()
    {
        byte[] direct = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><mask id='m'><rect x='0' y='0' width='8' height='8' fill='#808080'/></mask></defs><polygon points='0,0 8,0 0,8' fill='red' mask='url(#m)'/></svg>", out _, out _);
        byte[] instanced = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><mask id='m'><rect x='0' y='0' width='8' height='8' fill='#808080'/></mask></defs><defs><polygon id='p' points='0,0 8,0 0,8' fill='red'/></defs><use href='#p' mask='url(#m)'/></svg>", out _, out _);

        Assert.Equal(direct, instanced);
    }

    [Fact]
    public void Fill_override_on_unstyled_polygon_matches_direct()
    {
        byte[] direct = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><polygon points='0,0 8,0 0,8' fill='blue'/></svg>", out _, out _);
        byte[] instanced = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><polygon id='p' points='0,0 8,0 0,8'/></defs><use href='#p' fill='blue'/></svg>", out _, out _);

        Assert.Equal(direct, instanced);
    }

    [Fact]
    public void Forward_polygon_use_inside_masked_group_matches_backward()
    {
        byte[] backward = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><mask id='m'><rect x='0' y='0' width='8' height='8' fill='#808080'/></mask></defs><defs><polygon id='p' points='0,0 8,0 0,8' fill='red'/></defs><g mask='url(#m)'><use href='#p'/></g></svg>", out _, out _);
        byte[] forward = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><mask id='m'><rect x='0' y='0' width='8' height='8' fill='#808080'/></mask></defs><g mask='url(#m)'><use href='#p'/></g><defs><polygon id='p' points='0,0 8,0 0,8' fill='red'/></defs></svg>", out _, out _);

        Assert.Equal(backward, forward);
    }

    [Fact]
    public void Nested_viewport_polygon_use_matches_direct_rendering()
    {
        byte[] direct = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='8'><svg x='4' y='0' width='12' height='8'><polygon points='0,0 8,0 0,8' fill='red'/></svg></svg>", out _, out _);
        byte[] instanced = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='8'><defs><polygon id='p' points='0,0 8,0 0,8' fill='red'/></defs><svg x='4' y='0' width='12' height='8'><use href='#p'/></svg></svg>", out _, out _);

        Assert.Equal(direct, instanced);
    }

    [Fact]
    public void Gradient_filled_polygon_use_matches_shifted_direct()
    {
        byte[] direct = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='8'><defs><linearGradient id='g'><stop offset='0' stop-color='red'/><stop offset='1' stop-color='blue'/></linearGradient></defs><polygon points='8,0 16,0 16,4 12,4 12,8 8,8' fill='url(#g)'/></svg>", out _, out _);
        byte[] instanced = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='8'><defs><linearGradient id='g'><stop offset='0' stop-color='red'/><stop offset='1' stop-color='blue'/></linearGradient></defs><defs><polygon id='p' points='0,0 8,0 8,4 4,4 4,8 0,8' fill='url(#g)'/></defs><use href='#p' x='8'/></svg>", out _, out _);

        Assert.Equal(direct, instanced);
    }

    [Fact]
    public void Nonuniform_userspace_gradient_use_matches_direct()
    {
        byte[] direct = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='16'><defs><linearGradient id='g' gradientUnits='userSpaceOnUse' x1='0' y1='0' x2='16' y2='16'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><g transform='scale(2, 1)'><rect x='0' y='0' width='16' height='16' fill='url(#g)'/></g></svg>", out _, out _);
        byte[] instanced = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='16'><defs><linearGradient id='g' gradientUnits='userSpaceOnUse' x1='0' y1='0' x2='16' y2='16'><stop offset='0' stop-color='red'/><stop offset='1' stop-color='blue'/></linearGradient></defs><defs><polygon id='p' points='0,0 16,0 16,16 0,16' fill='url(#g)'/></defs><use href='#p' transform='scale(2, 1)'/></svg>", out _, out _);

        Assert.Equal(direct, instanced);
    }

    [Fact]
    public void Nonuniform_userspace_gradient_bands_match_direct()
    {
        byte[] direct = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='8'><defs><linearGradient id='g' gradientUnits='userSpaceOnUse' x1='0' y1='0' x2='8' y2='8'><stop offset='0' stop-color='red'/><stop offset='1' stop-color='blue'/></linearGradient></defs><g transform='scale(2, 1)'><polygon points='0,0 8,0 8,4 4,4 4,8 0,8' fill='url(#g)'/></g></svg>", out _, out _);
        byte[] instanced = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='8'><defs><linearGradient id='g' gradientUnits='userSpaceOnUse' x1='0' y1='0' x2='8' y2='8'><stop offset='0' stop-color='red'/><stop offset='1' stop-color='blue'/></linearGradient></defs><defs><polygon id='p' points='0,0 8,0 8,4 4,4 4,8 0,8' fill='url(#g)'/></defs><use href='#p' transform='scale(2, 1)'/></svg>", out _, out _);

        Assert.Equal(direct, instanced);
    }

    [Fact]
    public void Radial_filled_polygon_use_matches_shifted_direct()
    {
        byte[] direct = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='8'><defs><radialGradient id='g' cx='0.5' cy='0.5' r='0.5'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><polygon points='8,0 16,0 16,4 12,4 12,8 8,8' fill='url(#g)'/></svg>", out _, out _);
        byte[] instanced = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='8'><defs><radialGradient id='g' cx='0.5' cy='0.5' r='0.5'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><defs><polygon id='p' points='0,0 8,0 8,4 4,4 4,8 0,8' fill='url(#g)'/></defs><use href='#p' x='8'/></svg>", out _, out _);

        Assert.Equal(direct, instanced);
    }

    [Fact]
    public void Userspace_radial_use_matches_shifted_direct()
    {
        byte[] direct = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='8'><defs><radialGradient id='g' gradientUnits='userSpaceOnUse' cx='12' cy='4' r='8'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><polygon points='8,0 16,0 16,4 12,4 12,8 8,8' fill='url(#g)'/></svg>", out _, out _);
        byte[] instanced = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='8'><defs><radialGradient id='g' gradientUnits='userSpaceOnUse' cx='4' cy='4' r='8'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><defs><polygon id='p' points='0,0 8,0 8,4 4,4 4,8 0,8' fill='url(#g)'/></defs><use href='#p' x='8'/></svg>", out _, out _);

        Assert.Equal(direct, instanced);
    }

    [Fact]
    public void Symbol_radial_use_matches_direct_use()
    {
        byte[] direct = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><radialGradient id='g' cx='0.5' cy='0.5' r='0.5'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><defs><polygon id='p' points='0,0 8,0 8,4 4,4 4,8 0,8' fill='url(#g)'/></defs><use href='#p'/></svg>", out _, out _);
        byte[] symbol = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><radialGradient id='g' cx='0.5' cy='0.5' r='0.5'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><defs><symbol id='s' viewBox='0 0 8 8'><polygon points='0,0 8,0 8,4 4,4 4,8 0,8' fill='url(#g)'/></symbol></defs><use href='#s' width='8' height='8'/></svg>", out _, out _);

        Assert.Equal(direct, symbol);
    }

    [Fact]
    public void Radial_bands_use_matches_direct()
    {
        byte[] direct = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><radialGradient id='g' cx='0.5' cy='0.5' r='0.5'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><path d='M0,0 H8 V8 H0 Z' fill='url(#g)'/></svg>", out _, out _);
        byte[] instanced = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><radialGradient id='g' cx='0.5' cy='0.5' r='0.5'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><defs><path id='p' d='M0,0 H8 V8 H0 Z' fill='url(#g)'/></defs><use href='#p'/></svg>", out _, out _);

        Assert.Equal(direct, instanced);
    }

    [Fact]
    public void Paintserver_radial_use_matches_direct()
    {
        byte[] direct = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><radialGradient id='g' cx='0.5' cy='0.5' r='0.5'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><rect x='0' y='0' width='8' height='8' fill='url(#g)'/></svg>", out _, out _);
        byte[] instanced = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><radialGradient id='g' cx='0.5' cy='0.5' r='0.5'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient><rect id='r' x='0' y='0' width='8' height='8'/></defs><use href='#r' fill='url(#g)'/></svg>", out _, out _);

        Assert.Equal(direct, instanced);
    }

    [Fact]
    public void White_masked_radial_matches_unmasked_rendering()
    {
        byte[] direct = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><radialGradient id='g' cx='0.5' cy='0.5' r='0.5'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><rect x='0' y='0' width='8' height='8' fill='url(#g)'/></svg>", out _, out _);
        byte[] masked = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><radialGradient id='g' cx='0.5' cy='0.5' r='0.5'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><defs><mask id='m'><rect x='0' y='0' width='8' height='8' fill='#ffffff'/></mask></defs><rect x='0' y='0' width='8' height='8' fill='url(#g)' mask='url(#m)'/></svg>", out _, out _);

        Assert.Equal(direct, masked);
    }

    [Fact]
    public void Masked_radial_use_matches_masked_direct_rendering()
    {
        byte[] direct = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><radialGradient id='g' cx='0.5' cy='0.5' r='0.5'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><defs><mask id='m'><rect x='0' y='0' width='8' height='8' fill='#808080'/></mask></defs><polygon points='0,0 8,0 8,4 4,4 4,8 0,8' fill='url(#g)' mask='url(#m)'/></svg>", out _, out _);
        byte[] instanced = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><radialGradient id='g' cx='0.5' cy='0.5' r='0.5'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><defs><mask id='m'><rect x='0' y='0' width='8' height='8' fill='#808080'/></mask></defs><defs><polygon id='p' points='0,0 8,0 8,4 4,4 4,8 0,8' fill='url(#g)'/></defs><use href='#p' mask='url(#m)'/></svg>", out _, out _);

        Assert.Equal(direct, instanced);
    }

    [Fact]
    public void Radial_stroke_rect_use_matches_shifted_direct()
    {
        byte[] direct = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='8'><defs><radialGradient id='g' cx='0.5' cy='0.5' r='0.5'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><rect x='8' y='0' width='8' height='8' fill='red' stroke='url(#g)' stroke-width='2'/></svg>", out _, out _);
        byte[] instanced = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='8'><defs><radialGradient id='g' cx='0.5' cy='0.5' r='0.5'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><defs><rect id='r' x='0' y='0' width='8' height='8' fill='red' stroke='url(#g)' stroke-width='2'/></defs><use href='#r' x='8'/></svg>", out _, out _);

        Assert.Equal(direct, instanced);
    }

    [Fact]
    public void Clipped_symbol_use_matches_clipped_direct_use()
    {
        byte[] direct = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><clipPath id='c'><rect x='0' y='0' width='4' height='8'/></clipPath></defs><defs><rect id='r' x='0' y='0' width='8' height='8' fill='red'/></defs><use href='#r' clip-path='url(#c)'/></svg>", out _, out _);
        byte[] symbol = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><clipPath id='c'><rect x='0' y='0' width='4' height='8'/></clipPath></defs><defs><symbol id='s' viewBox='0 0 8 8'><rect x='0' y='0' width='8' height='8' fill='red'/></symbol></defs><use href='#s' width='8' height='8' clip-path='url(#c)'/></svg>", out _, out _);

        Assert.Equal(direct, symbol);
    }

    [Fact]
    public void Object_bounding_box_clipped_symbol_renders_kept_half()
    {
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><symbol id='s' viewBox='0 0 8 8'><rect x='0' y='0' width='8' height='8' fill='red'/></symbol></defs><defs><clipPath id='window' clipPathUnits='objectBoundingBox'><rect x='0' y='0' width='0.5' height='1'/></clipPath></defs><use href='#s' width='8' height='8' clip-path='url(#window)'/></svg>", out _, out _);

        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, 8, 1, 4));
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(pixels, 8, 6, 4));
    }

    [Fact]
    public void Forward_object_bounding_box_clipped_use_matches_backward()
    {
        byte[] backward = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><rect id='r' x='0' y='0' width='8' height='8' fill='red'/></defs><defs><clipPath id='window' clipPathUnits='objectBoundingBox'><rect x='0' y='0' width='0.5' height='1'/></clipPath></defs><use href='#r' clip-path='url(#window)'/></svg>", out _, out _);
        byte[] forward = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><clipPath id='window' clipPathUnits='objectBoundingBox'><rect x='0' y='0' width='0.5' height='1'/></clipPath></defs><use href='#r' clip-path='url(#window)'/><defs><rect id='r' x='0' y='0' width='8' height='8' fill='red'/></defs></svg>", out _, out _);

        Assert.Equal(backward, forward);
    }

    [Fact]
    public void Forward_object_bounding_box_clipped_symbol_matches_backward()
    {
        byte[] backward = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><symbol id='s' viewBox='0 0 8 8'><rect x='0' y='0' width='8' height='8' fill='red'/></symbol></defs><defs><clipPath id='window' clipPathUnits='objectBoundingBox'><rect x='0' y='0' width='0.5' height='1'/></clipPath></defs><use href='#s' width='8' height='8' clip-path='url(#window)'/></svg>", out _, out _);
        byte[] forward = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><clipPath id='window' clipPathUnits='objectBoundingBox'><rect x='0' y='0' width='0.5' height='1'/></clipPath></defs><use href='#s' width='8' height='8' clip-path='url(#window)'/><defs><symbol id='s' viewBox='0 0 8 8'><rect x='0' y='0' width='8' height='8' fill='red'/></symbol></defs></svg>", out _, out _);

        Assert.Equal(backward, forward);
    }

    [Fact]
    public void Forward_clip_path_definition_use_matches_backward()
    {
        byte[] backward = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><rect id='r' x='0' y='0' width='8' height='8' fill='red'/></defs><defs><clipPath id='c'><rect x='0' y='0' width='4' height='8'/></clipPath></defs><use href='#r' clip-path='url(#c)'/></svg>", out _, out _);
        byte[] forward = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><rect id='r' x='0' y='0' width='8' height='8' fill='red'/></defs><use href='#r' clip-path='url(#c)'/><defs><clipPath id='c'><rect x='0' y='0' width='4' height='8'/></clipPath></defs></svg>", out _, out _);

        Assert.Equal(backward, forward);
    }

    [Fact]
    public void Forward_object_bounding_box_clip_definition_use_matches_backward()
    {
        byte[] backward = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><rect id='r' x='0' y='0' width='8' height='8' fill='red'/></defs><defs><clipPath id='window' clipPathUnits='objectBoundingBox'><rect x='0' y='0' width='0.5' height='1'/></clipPath></defs><use href='#r' clip-path='url(#window)'/></svg>", out _, out _);
        byte[] forward = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><rect id='r' x='0' y='0' width='8' height='8' fill='red'/></defs><use href='#r' clip-path='url(#window)'/><defs><clipPath id='window' clipPathUnits='objectBoundingBox'><rect x='0' y='0' width='0.5' height='1'/></clipPath></defs></svg>", out _, out _);

        Assert.Equal(backward, forward);
    }

    [Fact]
    public void Forward_mask_definition_use_matches_backward()
    {
        byte[] backward = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><mask id='m'><rect x='0' y='0' width='8' height='8' fill='#808080'/></mask></defs><defs><polygon id='p' points='0,0 8,0 0,8' fill='red'/></defs><use href='#p' mask='url(#m)'/></svg>", out _, out _);
        byte[] forward = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><polygon id='p' points='0,0 8,0 0,8' fill='red'/></defs><use href='#p' mask='url(#m)'/><defs><mask id='m'><rect x='0' y='0' width='8' height='8' fill='#808080'/></mask></defs></svg>", out _, out _);

        Assert.Equal(backward, forward);
    }

    [Fact]
    public void Forward_mask_definition_symbol_matches_backward()
    {
        byte[] backward = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><mask id='m'><rect x='0' y='0' width='8' height='8' fill='#808080'/></mask></defs><defs><symbol id='s' viewBox='0 0 8 8'><polygon points='0,0 8,0 0,8' fill='red'/></symbol></defs><use href='#s' width='8' height='8' mask='url(#m)'/></svg>", out _, out _);
        byte[] forward = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><symbol id='s' viewBox='0 0 8 8'><polygon points='0,0 8,0 0,8' fill='red'/></symbol></defs><use href='#s' width='8' height='8' mask='url(#m)'/><defs><mask id='m'><rect x='0' y='0' width='8' height='8' fill='#808080'/></mask></defs></svg>", out _, out _);

        Assert.Equal(backward, forward);
    }

    [Fact]
    public void Forward_custom_region_mask_use_matches_backward()
    {
        byte[] backward = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><rect id='r' x='0' y='0' width='8' height='8' fill='red'/></defs><defs><mask id='m' maskUnits='userSpaceOnUse' x='2' y='2' width='4' height='4'><rect x='0' y='0' width='8' height='8' fill='#808080'/></mask></defs><use href='#r' mask='url(#m)'/></svg>", out _, out _);
        byte[] forward = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><rect id='r' x='0' y='0' width='8' height='8' fill='red'/></defs><use href='#r' mask='url(#m)'/><defs><mask id='m' maskUnits='userSpaceOnUse' x='2' y='2' width='4' height='4'><rect x='0' y='0' width='8' height='8' fill='#808080'/></mask></defs></svg>", out _, out _);

        Assert.Equal(backward, forward);
    }

    [Fact]
    public void Symbol_gradient_use_matches_direct_use()
    {
        byte[] direct = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><linearGradient id='g'><stop offset='0' stop-color='red'/><stop offset='1' stop-color='blue'/></linearGradient></defs><defs><polygon id='p' points='0,0 8,0 8,4 4,4 4,8 0,8' fill='url(#g)'/></defs><use href='#p'/></svg>", out _, out _);
        byte[] symbol = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><linearGradient id='g'><stop offset='0' stop-color='red'/><stop offset='1' stop-color='blue'/></linearGradient></defs><defs><symbol id='s' viewBox='0 0 8 8'><polygon points='0,0 8,0 8,4 4,4 4,8 0,8' fill='url(#g)'/></symbol></defs><use href='#s' width='8' height='8'/></svg>", out _, out _);

        Assert.Equal(direct, symbol);
    }

    [Fact]
    public void Gradient_stroke_line_use_matches_shifted_direct()
    {
        byte[] direct = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='8'><defs><linearGradient id='g' gradientUnits='userSpaceOnUse' x1='8' y1='0' x2='16' y2='0'><stop offset='0' stop-color='red'/><stop offset='1' stop-color='blue'/></linearGradient></defs><line x1='8' y1='4' x2='16' y2='4' stroke='url(#g)' stroke-width='2'/></svg>", out _, out _);
        byte[] instanced = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='8'><defs><linearGradient id='g' gradientUnits='userSpaceOnUse' x1='0' y1='0' x2='8' y2='0'><stop offset='0' stop-color='red'/><stop offset='1' stop-color='blue'/></linearGradient></defs><defs><line id='l' x1='0' y1='4' x2='8' y2='4' stroke='url(#g)' stroke-width='2'/></defs><use href='#l' x='8'/></svg>", out _, out _);

        Assert.Equal(direct, instanced);
    }

    [Fact]
    public void Gradient_outline_polygon_use_matches_shifted_direct()
    {
        byte[] direct = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='8'><defs><linearGradient id='g' gradientUnits='userSpaceOnUse' x1='8' y1='0' x2='16' y2='0'><stop offset='0' stop-color='red'/><stop offset='1' stop-color='blue'/></linearGradient></defs><polygon points='8,0 16,0 8,8' fill='none' stroke='url(#g)' stroke-width='2'/></svg>", out _, out _);
        byte[] instanced = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='8'><defs><linearGradient id='g' gradientUnits='userSpaceOnUse' x1='0' y1='0' x2='8' y2='0'><stop offset='0' stop-color='red'/><stop offset='1' stop-color='blue'/></linearGradient></defs><defs><polygon id='q' points='0,0 8,0 0,8' fill='none' stroke='url(#g)' stroke-width='2'/></defs><use href='#q' x='8'/></svg>", out _, out _);

        Assert.Equal(direct, instanced);
    }

    [Fact]
    public void Radial_outline_polygon_use_matches_shifted_direct()
    {
        byte[] direct = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='8'><defs><radialGradient id='g' gradientUnits='userSpaceOnUse' cx='12' cy='4' r='8'><stop offset='0' stop-color='red'/><stop offset='1' stop-color='blue'/></radialGradient></defs><polygon points='8,0 16,0 8,8' fill='none' stroke='url(#g)' stroke-width='2'/></svg>", out _, out _);
        byte[] instanced = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='8'><defs><radialGradient id='g' gradientUnits='userSpaceOnUse' cx='4' cy='4' r='8'><stop offset='0' stop-color='red'/><stop offset='1' stop-color='blue'/></radialGradient></defs><defs><polygon id='q' points='0,0 8,0 0,8' fill='none' stroke='url(#g)' stroke-width='2'/></defs><use href='#q' x='8'/></svg>", out _, out _);

        Assert.Equal(direct, instanced);
    }

    [Fact]
    public void Pattern_outline_polygon_use_matches_shifted_direct()
    {
        byte[] direct = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='8'><defs><pattern id='p' width='8' height='8' patternUnits='userSpaceOnUse'><rect width='8' height='8' fill='red'/></pattern></defs><polygon points='8,0 16,0 8,8' fill='none' stroke='url(#p)' stroke-width='2'/></svg>", out _, out _);
        byte[] instanced = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='8'><defs><pattern id='p' width='8' height='8' patternUnits='userSpaceOnUse'><rect width='8' height='8' fill='red'/></pattern></defs><defs><polygon id='q' points='0,0 8,0 0,8' fill='none' stroke='url(#p)' stroke-width='2'/></defs><use href='#q' x='8'/></svg>", out _, out _);

        Assert.Equal(direct, instanced);
    }

    [Fact]
    public void Lowers_pattern_outline_uses_through_instance_placement()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><pattern id='p' width='8' height='8' patternUnits='userSpaceOnUse'><rect width='8' height='8' fill='red'/></pattern></defs><defs><polygon id='q' points='0,0 8,0 0,8' fill='none' stroke='url(#p)' stroke-width='2'/></defs><use href='#q' x='8'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(2, root.Geometries.Length);
        Assert.Equal(SvgGeometryKind.PolygonFill, root.Geometries[0].Kind);
        Assert.Equal(SvgGeometryKind.PatternPolygon, root.Geometries[1].Kind);
        Assert.Equal(0, root.Geometries[1].PatternPolygon.OriginX);
        Assert.Equal(new SvgDevicePoint(8, 1), root.Geometries[1].PatternPolygon.Points[0]);
    }

    [Fact]
    public void Applies_a_gray_mask_to_a_pattern_outline_use_instance()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><pattern id='p' width='8' height='8' patternUnits='userSpaceOnUse'><rect width='8' height='8' fill='red'/></pattern><mask id='fade'><rect x='-4' y='-4' width='24' height='16' fill='#808080'/></mask></defs><defs><polygon id='q' points='0,0 8,0 0,8' fill='none' stroke='url(#p)' stroke-width='2'/></defs><use href='#q' mask='url(#fade)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(2, root.Geometries.Length);
        Assert.Equal(SvgGeometryKind.PatternPolygon, root.Geometries[1].Kind);
        Assert.Equal(128, root.Geometries[1].PatternPolygon.Opacity);
    }

    [Fact]
    public void Lowers_radial_outline_uses_through_instance_placement()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><radialGradient id='g' gradientUnits='userSpaceOnUse' cx='4' cy='4' r='4'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><defs><polygon id='q' points='0,0 8,0 0,8' fill='none' stroke='url(#g)' stroke-width='2'/></defs><use href='#q' x='8'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(2, root.Geometries.Length);
        Assert.Equal(SvgGeometryKind.PolygonFill, root.Geometries[0].Kind);
        Assert.Equal(SvgGeometryKind.RadialPolygon, root.Geometries[1].Kind);
        Assert.Equal(12, root.Geometries[1].RadialPolygon.Gradient.CenterX);
        Assert.Equal(4, root.Geometries[1].RadialPolygon.Gradient.Radius);
    }

    [Fact]
    public void Lowers_bounding_box_radial_outline_uses_through_instance_placement()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><radialGradient id='g' cx='0.5' cy='0.5' r='0.5'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><defs><polygon id='q' points='0,0 8,0 0,8' fill='none' stroke='url(#g)' stroke-width='2'/></defs><use href='#q' x='8'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(2, root.Geometries.Length);
        Assert.Equal(SvgGeometryKind.RadialPolygon, root.Geometries[1].Kind);
        Assert.Equal(new SvgRectangle(8, 0, 8, 8, default, true, null), root.Geometries[1].RadialPolygon.Box);
    }

    [Fact]
    public void Applies_a_gray_mask_to_a_radial_outline_use_instance()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><radialGradient id='g' gradientUnits='userSpaceOnUse' cx='4' cy='4' r='4'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient><mask id='fade'><rect x='-4' y='-4' width='24' height='16' fill='#808080'/></mask></defs><defs><polygon id='q' points='0,0 8,0 0,8' fill='none' stroke='url(#g)' stroke-width='2'/></defs><use href='#q' mask='url(#fade)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(2, root.Geometries.Length);
        Assert.Equal(SvgGeometryKind.RadialPolygon, root.Geometries[1].Kind);
        Assert.Equal(128, root.Geometries[1].RadialPolygon.Opacity);
    }

    [Fact]
    public void Lowers_gradient_outline_uses_through_instance_placement()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='g' gradientUnits='userSpaceOnUse' x1='0' y1='0' x2='8' y2='0'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><defs><polygon id='q' points='0,0 8,0 0,8' fill='none' stroke='url(#g)' stroke-width='2'/></defs><use href='#q' x='8'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(2, root.Geometries.Length);
        Assert.Equal(SvgGeometryKind.PolygonFill, root.Geometries[0].Kind);
        Assert.Equal(SvgGeometryKind.GradientPolygon, root.Geometries[1].Kind);
        Assert.Equal(8, root.Geometries[1].GradientPolygon.Gradient.X1);
        Assert.Equal(16, root.Geometries[1].GradientPolygon.Gradient.X2);
        Assert.Equal(new SvgDevicePoint(8, 1), root.Geometries[1].GradientPolygon.Points[0]);
    }

    [Fact]
    public void Lowers_bounding_box_outline_uses_through_instance_placement()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='g' x1='0' y1='0' x2='1' y2='0'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><defs><polygon id='q' points='0,0 8,0 0,8' fill='none' stroke='url(#g)' stroke-width='2'/></defs><use href='#q' x='8'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(2, root.Geometries.Length);
        Assert.Equal(SvgGeometryKind.GradientPolygon, root.Geometries[1].Kind);
        Assert.Equal(new SvgRectangle(8, 0, 8, 8, default, true, null), root.Geometries[1].GradientPolygon.Box);
    }

    [Fact]
    public void Applies_a_gray_mask_to_a_gradient_outline_use_instance()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='g' gradientUnits='userSpaceOnUse' x1='0' y1='0' x2='8' y2='0'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient><mask id='fade'><rect x='-4' y='-4' width='24' height='16' fill='#808080'/></mask></defs><defs><polygon id='q' points='0,0 8,0 0,8' fill='none' stroke='url(#g)' stroke-width='2'/></defs><use href='#q' mask='url(#fade)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(2, root.Geometries.Length);
        Assert.Equal(SvgGeometryKind.GradientPolygon, root.Geometries[1].Kind);
        Assert.Equal(128, root.Geometries[1].GradientPolygon.Opacity);
    }

    [Fact]
    public void Gradient_stroke_rect_use_matches_shifted_direct()
    {
        byte[] direct = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='8'><defs><linearGradient id='g' gradientUnits='userSpaceOnUse' x1='8' y1='0' x2='16' y2='0'><stop offset='0' stop-color='red'/><stop offset='1' stop-color='blue'/></linearGradient></defs><rect x='8' y='0' width='8' height='8' fill='red' stroke='url(#g)' stroke-width='2'/></svg>", out _, out _);
        byte[] instanced = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='8'><defs><linearGradient id='g' gradientUnits='userSpaceOnUse' x1='0' y1='0' x2='8' y2='0'><stop offset='0' stop-color='red'/><stop offset='1' stop-color='blue'/></linearGradient></defs><defs><rect id='r' x='0' y='0' width='8' height='8' fill='red' stroke='url(#g)' stroke-width='2'/></defs><use href='#r' x='8'/></svg>", out _, out _);

        Assert.Equal(direct, instanced);
    }

    [Fact]
    public void Pattern_filled_polygon_use_matches_shifted_direct()
    {
        byte[] direct = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='8'><defs><pattern id='p' width='8' height='8' patternUnits='userSpaceOnUse'><rect width='4' height='4' fill='red'/></pattern></defs><polygon points='8,0 16,0 16,4 12,4 12,8 8,8' fill='url(#p)'/></svg>", out _, out _);
        byte[] instanced = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='8'><defs><pattern id='p' width='8' height='8' patternUnits='userSpaceOnUse'><rect width='4' height='4' fill='red'/></pattern></defs><defs><polygon id='q' points='0,0 8,0 8,4 4,4 4,8 0,8' fill='url(#p)'/></defs><use href='#q' x='8'/></svg>", out _, out _);

        Assert.Equal(direct, instanced);
    }

    [Fact]
    public void Symbol_pattern_use_matches_direct_use()
    {
        byte[] direct = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><pattern id='p' width='8' height='8' patternUnits='userSpaceOnUse'><rect width='4' height='4' fill='red'/></pattern></defs><polygon points='0,0 8,0 8,4 4,4 4,8 0,8' fill='url(#p)'/></svg>", out _, out _);
        byte[] symbol = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><pattern id='p' width='8' height='8' patternUnits='userSpaceOnUse'><rect width='4' height='4' fill='red'/></pattern></defs><defs><symbol id='s' viewBox='0 0 8 8'><polygon points='0,0 8,0 8,4 4,4 4,8 0,8' fill='url(#p)'/></symbol></defs><use href='#s' width='8' height='8'/></svg>", out _, out _);

        Assert.Equal(direct, symbol);
    }

    [Fact]
    public void Symbol_gradient_outline_use_matches_direct()
    {
        byte[] direct = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><linearGradient id='g' gradientUnits='userSpaceOnUse' x1='0' y1='0' x2='8' y2='0'><stop offset='0' stop-color='red'/><stop offset='1' stop-color='blue'/></linearGradient></defs><polygon points='0,0 8,0 0,8' fill='none' stroke='url(#g)' stroke-width='2'/></svg>", out _, out _);
        byte[] symbol = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><linearGradient id='g' gradientUnits='userSpaceOnUse' x1='0' y1='0' x2='8' y2='0'><stop offset='0' stop-color='red'/><stop offset='1' stop-color='blue'/></linearGradient></defs><defs><symbol id='s' viewBox='0 0 8 8'><polygon points='0,0 8,0 0,8' fill='none' stroke='url(#g)' stroke-width='2'/></symbol></defs><use href='#s' width='8' height='8'/></svg>", out _, out _);

        Assert.Equal(direct, symbol);
    }

    [Fact]
    public void Scaled_symbol_gradient_outline_matches_direct()
    {
        byte[] direct = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><defs><linearGradient id='g' gradientUnits='userSpaceOnUse' x1='0' y1='0' x2='16' y2='0'><stop offset='0' stop-color='red'/><stop offset='1' stop-color='blue'/></linearGradient></defs><polygon points='0,0 16,0 0,16' fill='none' stroke='url(#g)' stroke-width='4'/></svg>", out _, out _);
        byte[] symbol = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><defs><linearGradient id='g' gradientUnits='userSpaceOnUse' x1='0' y1='0' x2='8' y2='0'><stop offset='0' stop-color='red'/><stop offset='1' stop-color='blue'/></linearGradient></defs><defs><symbol id='s' viewBox='0 0 8 8'><polygon points='0,0 8,0 0,8' fill='none' stroke='url(#g)' stroke-width='2'/></symbol></defs><use href='#s' width='16' height='16'/></svg>", out _, out _);

        Assert.Equal(direct, symbol);
    }

    [Fact]
    public void Symbol_radial_outline_use_matches_direct()
    {
        byte[] direct = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><radialGradient id='g' gradientUnits='userSpaceOnUse' cx='4' cy='4' r='4'><stop offset='0' stop-color='red'/><stop offset='1' stop-color='blue'/></radialGradient></defs><polygon points='0,0 8,0 0,8' fill='none' stroke='url(#g)' stroke-width='2'/></svg>", out _, out _);
        byte[] symbol = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><radialGradient id='g' gradientUnits='userSpaceOnUse' cx='4' cy='4' r='4'><stop offset='0' stop-color='red'/><stop offset='1' stop-color='blue'/></radialGradient></defs><defs><symbol id='s' viewBox='0 0 8 8'><polygon points='0,0 8,0 0,8' fill='none' stroke='url(#g)' stroke-width='2'/></symbol></defs><use href='#s' width='8' height='8'/></svg>", out _, out _);

        Assert.Equal(direct, symbol);
    }

    [Fact]
    public void Scaled_symbol_radial_outline_matches_direct()
    {
        byte[] direct = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><defs><radialGradient id='g' gradientUnits='userSpaceOnUse' cx='8' cy='8' r='8'><stop offset='0' stop-color='red'/><stop offset='1' stop-color='blue'/></radialGradient></defs><polygon points='0,0 16,0 0,16' fill='none' stroke='url(#g)' stroke-width='4'/></svg>", out _, out _);
        byte[] symbol = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><defs><radialGradient id='g' gradientUnits='userSpaceOnUse' cx='4' cy='4' r='4'><stop offset='0' stop-color='red'/><stop offset='1' stop-color='blue'/></radialGradient></defs><defs><symbol id='s' viewBox='0 0 8 8'><polygon points='0,0 8,0 0,8' fill='none' stroke='url(#g)' stroke-width='2'/></symbol></defs><use href='#s' width='16' height='16'/></svg>", out _, out _);

        Assert.Equal(direct, symbol);
    }

    [Fact]
    public void Symbol_pattern_outline_use_matches_direct()
    {
        byte[] direct = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><pattern id='p' width='8' height='8' patternUnits='userSpaceOnUse'><rect width='8' height='8' fill='red'/></pattern></defs><polygon points='0,0 8,0 0,8' fill='none' stroke='url(#p)' stroke-width='2'/></svg>", out _, out _);
        byte[] symbol = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><pattern id='p' width='8' height='8' patternUnits='userSpaceOnUse'><rect width='8' height='8' fill='red'/></pattern></defs><defs><symbol id='s' viewBox='0 0 8 8'><polygon points='0,0 8,0 0,8' fill='none' stroke='url(#p)' stroke-width='2'/></symbol></defs><use href='#s' width='8' height='8'/></svg>", out _, out _);

        Assert.Equal(direct, symbol);
    }

    [Fact]
    public void Scaled_symbol_pattern_outline_matches_direct()
    {
        byte[] direct = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><defs><pattern id='p' width='16' height='16' patternUnits='userSpaceOnUse'><rect width='16' height='16' fill='red'/></pattern></defs><polygon points='0,0 16,0 0,16' fill='none' stroke='url(#p)' stroke-width='4'/></svg>", out _, out _);
        byte[] symbol = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><defs><pattern id='p' width='8' height='8' patternUnits='userSpaceOnUse'><rect width='8' height='8' fill='red'/></pattern></defs><defs><symbol id='s' viewBox='0 0 8 8'><polygon points='0,0 8,0 0,8' fill='none' stroke='url(#p)' stroke-width='2'/></symbol></defs><use href='#s' width='16' height='16'/></svg>", out _, out _);

        Assert.Equal(direct, symbol);
    }

    [Theory]
    [InlineData("<defs><linearGradient id='g' gradientUnits='userSpaceOnUse' x1='0' y1='0' x2='8' y2='0'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><defs><polygon id='q' points='0,0 8,0 0,8' fill='none' stroke='url(#g)' stroke-width='2'/></defs><use href='#q' transform='scale(2,1)'/>")]
    [InlineData("<defs><radialGradient id='g' gradientUnits='userSpaceOnUse' cx='4' cy='4' r='4'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><defs><polygon id='q' points='0,0 8,0 0,8' fill='none' stroke='url(#g)' stroke-width='2'/></defs><use href='#q' transform='scale(2,1)'/>")]
    [InlineData("<defs><pattern id='p' width='8' height='8' patternUnits='userSpaceOnUse'><rect width='8' height='8' fill='red'/></pattern></defs><defs><polygon id='q' points='0,0 8,0 0,8' fill='none' stroke='url(#p)' stroke-width='2'/></defs><use href='#q' transform='scale(2,1)'/>")]
    public void Non_uniformly_scaled_outline_uses_stay_unsupported(string body)
    {
        SvgException rejected = Assert.Throws<SvgException>(() => SvgRootParser.Parse(System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg'>" + body + "</svg>"), SvgRenderLimits.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, rejected.ErrorCode);
    }

    [Theory]
    [InlineData("<defs><linearGradient id='g' gradientUnits='userSpaceOnUse' x1='0' y1='0' x2='8' y2='0'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><defs><symbol id='s' viewBox='0 0 8 8'><polygon points='0,0 8,0 0,8' fill='none' stroke='url(#g)' stroke-width='2'/></symbol></defs><use href='#s' width='8' height='8' transform='scale(2,1)'/>")]
    [InlineData("<defs><radialGradient id='g' gradientUnits='userSpaceOnUse' cx='4' cy='4' r='4'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><defs><symbol id='s' viewBox='0 0 8 8'><polygon points='0,0 8,0 0,8' fill='none' stroke='url(#g)' stroke-width='2'/></symbol></defs><use href='#s' width='8' height='8' transform='scale(2,1)'/>")]
    [InlineData("<defs><pattern id='p' width='8' height='8' patternUnits='userSpaceOnUse'><rect width='8' height='8' fill='red'/></pattern></defs><defs><symbol id='s' viewBox='0 0 8 8'><polygon points='0,0 8,0 0,8' fill='none' stroke='url(#p)' stroke-width='2'/></symbol></defs><use href='#s' width='8' height='8' transform='scale(2,1)'/>")]
    public void Non_uniformly_scaled_symbol_outline_uses_stay_unsupported(string body)
    {
        SvgException rejected = Assert.Throws<SvgException>(() => SvgRootParser.Parse(System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg'>" + body + "</svg>"), SvgRenderLimits.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, rejected.ErrorCode);
    }

    [Fact]
    public void Lowers_symbol_pattern_outline_uses_through_viewbox_mapping()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><pattern id='p' width='8' height='8' patternUnits='userSpaceOnUse'><rect width='8' height='8' fill='red'/></pattern></defs><defs><symbol id='s' viewBox='0 0 8 8'><polygon points='0,0 8,0 0,8' fill='none' stroke='url(#p)' stroke-width='2'/></symbol></defs><use href='#s' width='16' height='16'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(2, root.Geometries.Length);
        Assert.Equal(SvgGeometryKind.PatternPolygon, root.Geometries[1].Kind);
        Assert.Equal(16, root.Geometries[1].PatternPolygon.TileWidth);
        Assert.Equal(new SvgDevicePoint(0, 2), root.Geometries[1].PatternPolygon.Points[0]);
    }

    [Fact]
    public void Non_uniform_symbol_pattern_outline_uses_stay_unsupported()
    {
        SvgException rejected = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><pattern id='p' width='8' height='8' patternUnits='userSpaceOnUse'><rect width='8' height='8' fill='red'/></pattern></defs><defs><symbol id='s' viewBox='0 0 8 8' preserveAspectRatio='none'><polygon points='0,0 8,0 0,8' fill='none' stroke='url(#p)' stroke-width='2'/></symbol></defs><use href='#s' width='16' height='8'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, rejected.ErrorCode);
    }

    [Fact]
    public void Lowers_symbol_radial_outline_uses_through_viewbox_mapping()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><radialGradient id='g' gradientUnits='userSpaceOnUse' cx='4' cy='4' r='4'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><defs><symbol id='s' viewBox='0 0 8 8'><polygon points='0,0 8,0 0,8' fill='none' stroke='url(#g)' stroke-width='2'/></symbol></defs><use href='#s' width='16' height='16'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(2, root.Geometries.Length);
        Assert.Equal(SvgGeometryKind.RadialPolygon, root.Geometries[1].Kind);
        Assert.Equal(8, root.Geometries[1].RadialPolygon.Gradient.CenterX);
        Assert.Equal(8, root.Geometries[1].RadialPolygon.Gradient.Radius);
    }

    [Fact]
    public void Non_uniform_symbol_radial_outline_uses_stay_unsupported()
    {
        SvgException rejected = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><radialGradient id='g' gradientUnits='userSpaceOnUse' cx='4' cy='4' r='4'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><defs><symbol id='s' viewBox='0 0 8 8' preserveAspectRatio='none'><polygon points='0,0 8,0 0,8' fill='none' stroke='url(#g)' stroke-width='2'/></symbol></defs><use href='#s' width='16' height='8'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, rejected.ErrorCode);
    }

    [Fact]
    public void Lowers_symbol_gradient_outline_uses_through_viewbox_mapping()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='g' gradientUnits='userSpaceOnUse' x1='0' y1='0' x2='8' y2='0'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><defs><symbol id='s' viewBox='0 0 8 8'><polygon points='0,0 8,0 0,8' fill='none' stroke='url(#g)' stroke-width='2'/></symbol></defs><use href='#s' width='16' height='16'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(2, root.Geometries.Length);
        Assert.Equal(SvgGeometryKind.GradientPolygon, root.Geometries[1].Kind);
        Assert.Equal(0, root.Geometries[1].GradientPolygon.Gradient.X1);
        Assert.Equal(16, root.Geometries[1].GradientPolygon.Gradient.X2);
    }

    [Fact]
    public void Non_uniform_symbol_outline_uses_stay_unsupported()
    {
        SvgException rejected = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='g' gradientUnits='userSpaceOnUse' x1='0' y1='0' x2='8' y2='0'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><defs><symbol id='s' viewBox='0 0 8 8' preserveAspectRatio='none'><polygon points='0,0 8,0 0,8' fill='none' stroke='url(#g)' stroke-width='2'/></symbol></defs><use href='#s' width='16' height='8'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, rejected.ErrorCode);
    }

    [Fact]
    public void Nonuniform_symbol_radial_bands_record_viewbox_scales()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><radialGradient id='g' gradientUnits='userSpaceOnUse' cx='4' cy='4' r='4'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><defs><symbol id='s' viewBox='0 0 8 8' preserveAspectRatio='none'><polygon points='0,0 8,0 8,8 0,8' fill='url(#g)'/></symbol></defs><use href='#s' width='16' height='8'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.True(root.Geometries.Length >= 1);
        Assert.Equal(SvgGeometryKind.RadialGradient, root.Geometries[0].Kind);
        Assert.Equal(2, root.Geometries[0].Radial.Gradient.ReferenceScaleX);
        Assert.Equal(1, root.Geometries[0].Radial.Gradient.ReferenceScaleY);
        Assert.Equal(8, root.Geometries[0].Radial.Gradient.CenterX);
    }

    [Fact]
    public void Scaled_symbol_pattern_matches_direct()
    {
        byte[] direct = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><pattern id='p' width='8' height='8' patternUnits='userSpaceOnUse'><rect width='4' height='4' fill='red'/></pattern></defs><polygon points='0,0 8,0 8,4 4,4 4,8 0,8' fill='url(#p)'/></svg>", out _, out _);
        byte[] symbol = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><pattern id='p' width='4' height='4' patternUnits='userSpaceOnUse'><rect width='2' height='2' fill='red'/></pattern></defs><defs><symbol id='s' viewBox='0 0 4 4'><polygon points='0,0 4,0 4,2 2,2 2,4 0,4' fill='url(#p)'/></symbol></defs><use href='#s' width='8' height='8'/></svg>", out _, out _);

        Assert.Equal(direct, symbol);
    }

    [Fact]
    public void Non_integer_symbol_pattern_stays_unsupported()
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><pattern id='p' x='1' width='8' height='8' patternUnits='userSpaceOnUse'><rect width='4' height='4' fill='red'/></pattern></defs><defs><symbol id='s' viewBox='0 0 8 8'><polygon points='0,0 8,0 8,4 4,4 4,8 0,8' fill='url(#p)'/></symbol></defs><use href='#s' width='4' height='4'/></svg>");

        SvgException rejected = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, rejected.ErrorCode);
    }

    [Fact]
    public void Forward_symbol_pattern_matches_backward_reference()
    {
        byte[] backward = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><pattern id='p' width='8' height='8' patternUnits='userSpaceOnUse'><rect width='4' height='4' fill='red'/></pattern></defs><defs><symbol id='s' viewBox='0 0 8 8'><polygon points='0,0 8,0 8,4 4,4 4,8 0,8' fill='url(#p)'/></symbol></defs><use href='#s' width='8' height='8'/></svg>", out _, out _);
        byte[] forward = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><pattern id='p' width='8' height='8' patternUnits='userSpaceOnUse'><rect width='4' height='4' fill='red'/></pattern></defs><use href='#s' width='8' height='8'/><defs><symbol id='s' viewBox='0 0 8 8'><polygon points='0,0 8,0 8,4 4,4 4,8 0,8' fill='url(#p)'/></symbol></defs></svg>", out _, out _);

        Assert.Equal(backward, forward);
    }

    [Fact]
    public void Stroked_rect_use_matches_direct()
    {
        byte[] direct = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><rect x='0' y='0' width='8' height='8' fill='red' stroke='blue' stroke-width='2'/></svg>", out _, out _);
        byte[] instanced = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><rect id='r' x='0' y='0' width='8' height='8' fill='red' stroke='blue' stroke-width='2'/></defs><use href='#r'/></svg>", out _, out _);

        Assert.Equal(direct, instanced);
    }

    [Fact]
    public void Scaled_stroked_rect_use_matches_direct()
    {
        byte[] direct = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><rect x='0' y='0' width='16' height='16' fill='red' stroke='blue' stroke-width='4'/></svg>", out _, out _);
        byte[] instanced = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><defs><rect id='r' x='0' y='0' width='8' height='8' fill='red' stroke='blue' stroke-width='2'/></defs><use href='#r' transform='scale(2)'/></svg>", out _, out _);

        Assert.Equal(direct, instanced);
    }

    [Fact]
    public void Offset_stroked_rect_use_matches_shifted()
    {
        byte[] shifted = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><rect x='2' y='1' width='8' height='8' fill='red' stroke='blue' stroke-width='2'/></svg>", out _, out _);
        byte[] instanced = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><defs><rect id='r' x='0' y='0' width='8' height='8' fill='red' stroke='blue' stroke-width='2'/></defs><use href='#r' x='2' y='1'/></svg>", out _, out _);

        Assert.Equal(shifted, instanced);
    }

    [Fact]
    public void Non_uniform_stroked_rect_use_stays_unsupported()
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='8'><defs><rect id='r' x='0' y='0' width='8' height='8' fill='red' stroke='blue' stroke-width='2'/></defs><use href='#r' transform='scale(2,1)'/></svg>");

        SvgException rejected = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, rejected.ErrorCode);
    }

    [Fact]
    public void Symbol_stroked_rect_use_matches_direct()
    {
        byte[] direct = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><rect x='0' y='0' width='8' height='8' fill='red' stroke='blue' stroke-width='2'/></svg>", out _, out _);
        byte[] symbol = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><symbol id='s' viewBox='0 0 8 8'><rect x='0' y='0' width='8' height='8' fill='red' stroke='blue' stroke-width='2'/></symbol></defs><use href='#s' width='8' height='8'/></svg>", out _, out _);

        Assert.Equal(direct, symbol);
    }

    [Fact]
    public void Scaled_symbol_stroked_rect_matches_direct()
    {
        byte[] direct = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><rect x='0' y='0' width='8' height='8' fill='red' stroke='blue' stroke-width='4'/></svg>", out _, out _);
        byte[] symbol = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><symbol id='s' viewBox='0 0 4 4'><rect x='0' y='0' width='4' height='4' fill='red' stroke='blue' stroke-width='2'/></symbol></defs><use href='#s' width='8' height='8'/></svg>", out _, out _);

        Assert.Equal(direct, symbol);
    }

    [Fact]
    public void Non_uniform_symbol_stroked_rect_stays_unsupported()
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><symbol id='s' viewBox='0 0 8 4' preserveAspectRatio='none'><rect x='0' y='0' width='8' height='4' fill='red' stroke='blue' stroke-width='2'/></symbol></defs><use href='#s' width='8' height='8'/></svg>");

        SvgException rejected = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, rejected.ErrorCode);
    }

    [Fact]
    public void Symbol_stroked_rect_clips_stroke_to_viewport()
    {
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><symbol id='s' viewBox='0 0 8 8'><rect x='4' y='0' width='8' height='8' fill='red' stroke='blue' stroke-width='2'/></symbol></defs><use href='#s' width='8' height='8'/></svg>", out _, out _);

        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(pixels, 8, 0, 0));
        Assert.Equal(new SvgColor(0, 0, 255, 255), Pixel(pixels, 8, 3, 0));
        Assert.Equal(new SvgColor(0, 0, 255, 255), Pixel(pixels, 8, 3, 4));
        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, 8, 5, 4));
        Assert.Equal(new SvgColor(0, 0, 255, 255), Pixel(pixels, 8, 5, 7));
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(pixels, 8, 0, 7));
    }

    [Fact]
    public void Non_uniform_instance_symbol_stroke_stays_unsupported()
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='8'><defs><symbol id='s' viewBox='0 0 8 8'><rect x='0' y='0' width='8' height='8' fill='red' stroke='blue' stroke-width='2'/></symbol></defs><use href='#s' width='8' height='8' transform='scale(2,1)'/></svg>");

        SvgException rejected = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, rejected.ErrorCode);
    }

    [Fact]
    public void Fractional_symbol_stroke_scale_stays_unsupported()
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='12' height='12'><defs><symbol id='s' viewBox='0 0 8 8'><rect x='0' y='0' width='8' height='8' fill='red' stroke='blue' stroke-width='2'/></symbol></defs><use href='#s' width='12' height='12'/></svg>");

        SvgException rejected = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, rejected.ErrorCode);
    }

    [Fact]
    public void Symbol_stroked_polygon_matches_direct()
    {
        byte[] direct = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><polygon points='0,0 8,0 0,8' fill='red' stroke='blue' stroke-width='2'/></svg>", out _, out _);
        byte[] symbol = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><symbol id='s' viewBox='0 0 8 8'><polygon points='0,0 8,0 0,8' fill='red' stroke='blue' stroke-width='2'/></symbol></defs><use href='#s' width='8' height='8'/></svg>", out _, out _);

        Assert.Equal(direct, symbol);
    }

    [Fact]
    public void Scaled_symbol_stroked_polygon_matches_direct()
    {
        byte[] direct = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><polygon points='0,0 8,0 0,8' fill='red' stroke='blue' stroke-width='4'/></svg>", out _, out _);
        byte[] symbol = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><symbol id='s' viewBox='0 0 4 4'><polygon points='0,0 4,0 0,4' fill='red' stroke='blue' stroke-width='2'/></symbol></defs><use href='#s' width='8' height='8'/></svg>", out _, out _);

        Assert.Equal(direct, symbol);
    }

    [Fact]
    public void Symbol_gradient_stroke_use_matches_direct()
    {
        byte[] direct = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><linearGradient id='g' gradientUnits='userSpaceOnUse' x1='0' y1='0' x2='8' y2='0'><stop offset='0' stop-color='red'/><stop offset='1' stop-color='blue'/></linearGradient></defs><rect x='0' y='0' width='8' height='8' fill='red' stroke='url(#g)' stroke-width='2'/></svg>", out _, out _);
        byte[] symbol = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><linearGradient id='g' gradientUnits='userSpaceOnUse' x1='0' y1='0' x2='8' y2='0'><stop offset='0' stop-color='red'/><stop offset='1' stop-color='blue'/></linearGradient></defs><defs><symbol id='s' viewBox='0 0 8 8'><rect x='0' y='0' width='8' height='8' fill='red' stroke='url(#g)' stroke-width='2'/></symbol></defs><use href='#s' width='8' height='8'/></svg>", out _, out _);

        Assert.Equal(direct, symbol);
    }

    [Fact]
    public void Two_forward_multi_uses_preserve_document_order()
    {
        byte[] backward = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><polygon id='p' points='0,0 8,0 8,8 0,8' fill='red' stroke='blue' stroke-width='2'/></defs><defs><polygon id='q' points='0,0 8,0 0,8' fill='green'/></defs><use href='#p'/><rect x='4' y='0' width='4' height='8' fill='yellow'/><use href='#q'/></svg>", out _, out _);
        byte[] forward = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><use href='#p'/><rect x='4' y='0' width='4' height='8' fill='yellow'/><use href='#q'/><defs><polygon id='p' points='0,0 8,0 8,8 0,8' fill='red' stroke='blue' stroke-width='2'/></defs><defs><polygon id='q' points='0,0 8,0 0,8' fill='green'/></defs></svg>", out _, out _);

        Assert.Equal(backward, forward);
    }

    [Fact]
    public void Forward_gradient_polygon_use_matches_backward()
    {
        byte[] backward = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='8'><defs><linearGradient id='g'><stop offset='0' stop-color='red'/><stop offset='1' stop-color='blue'/></linearGradient></defs><defs><polygon id='p' points='0,0 8,0 8,4 4,4 4,8 0,8' fill='url(#g)'/></defs><use href='#p' x='8'/></svg>", out _, out _);
        byte[] forward = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='8'><defs><linearGradient id='g'><stop offset='0' stop-color='red'/><stop offset='1' stop-color='blue'/></linearGradient></defs><use href='#p' x='8'/><defs><polygon id='p' points='0,0 8,0 8,4 4,4 4,8 0,8' fill='url(#g)'/></defs></svg>", out _, out _);

        Assert.Equal(backward, forward);
    }

    [Fact]
    public void Rejects_use_of_non_geometry_definitions()
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><image id='m' href='data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGP4z8DwHwAFAAH/iZk9HQAAAABJRU5ErkJggg==' x='0' y='0' width='8' height='8'/></defs><use href='#m'/></svg>");

        SvgException rejected = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, rejected.ErrorCode);
    }

    [Fact]
    public void Masked_forward_use_matches_masked_backward()
    {
        byte[] backward = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><mask id='m'><rect x='0' y='0' width='8' height='8' fill='#808080'/></mask></defs><defs><polygon id='p' points='0,0 8,0 0,8' fill='red'/></defs><use href='#p' mask='url(#m)'/></svg>", out _, out _);
        byte[] forward = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><mask id='m'><rect x='0' y='0' width='8' height='8' fill='#808080'/></mask></defs><use href='#p' mask='url(#m)'/><defs><polygon id='p' points='0,0 8,0 0,8' fill='red'/></defs></svg>", out _, out _);

        Assert.Equal(backward, forward);
    }

    [Fact]
    public void Translucent_fade_use_matches_direct()
    {
        byte[] direct = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><defs><linearGradient id='g'><stop offset='0' stop-color='red'/><stop offset='1' stop-color='blue' stop-opacity='0'/></linearGradient></defs><polygon points='0,0 16,0 16,8 8,8 8,16 0,16' fill='url(#g)'/></svg>", out _, out _);
        byte[] instanced = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><defs><linearGradient id='g'><stop offset='0' stop-color='red'/><stop offset='1' stop-color='blue' stop-opacity='0'/></linearGradient></defs><defs><polygon id='p' points='0,0 16,0 16,8 8,8 8,16 0,16' fill='url(#g)'/></defs><use href='#p'/></svg>", out _, out _);

        Assert.Equal(direct, instanced);
    }

    [Fact]
    public void Translucent_fade_forward_use_matches_backward()
    {
        byte[] backward = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><defs><linearGradient id='g'><stop offset='0' stop-color='red'/><stop offset='1' stop-color='blue' stop-opacity='0'/></linearGradient></defs><defs><polygon id='p' points='0,0 16,0 16,8 8,8 8,16 0,16' fill='url(#g)'/></defs><use href='#p'/></svg>", out _, out _);
        byte[] forward = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><defs><linearGradient id='g'><stop offset='0' stop-color='red'/><stop offset='1' stop-color='blue' stop-opacity='0'/></linearGradient></defs><use href='#p'/><defs><polygon id='p' points='0,0 16,0 16,8 8,8 8,16 0,16' fill='url(#g)'/></defs></svg>", out _, out _);

        Assert.Equal(backward, forward);
    }

    [Fact]
    public void Translucent_fade_symbol_use_matches_direct_use()
    {
        byte[] direct = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><defs><linearGradient id='g'><stop offset='0' stop-color='red'/><stop offset='1' stop-color='blue' stop-opacity='0'/></linearGradient></defs><defs><polygon id='p' points='0,0 16,0 16,8 8,8 8,16 0,16' fill='url(#g)'/></defs><use href='#p'/></svg>", out _, out _);
        byte[] symbol = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><defs><linearGradient id='g'><stop offset='0' stop-color='red'/><stop offset='1' stop-color='blue' stop-opacity='0'/></linearGradient></defs><defs><symbol id='s' viewBox='0 0 16 16'><polygon points='0,0 16,0 16,8 8,8 8,16 0,16' fill='url(#g)'/></symbol></defs><use href='#s' width='16' height='16'/></svg>", out _, out _);

        Assert.Equal(direct, symbol);
    }

    [Fact]
    public void White_masked_fade_matches_unmasked_fade()
    {
        byte[] direct = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><defs><linearGradient id='g'><stop offset='0' stop-color='red'/><stop offset='1' stop-color='blue' stop-opacity='0'/></linearGradient></defs><polygon points='0,0 16,0 16,8 8,8 8,16 0,16' fill='url(#g)'/></svg>", out _, out _);
        byte[] masked = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><defs><linearGradient id='g'><stop offset='0' stop-color='red'/><stop offset='1' stop-color='blue' stop-opacity='0'/></linearGradient></defs><defs><mask id='m'><rect x='0' y='0' width='16' height='16' fill='#ffffff'/></mask></defs><polygon points='0,0 16,0 16,8 8,8 8,16 0,16' fill='url(#g)' mask='url(#m)'/></svg>", out _, out _);

        Assert.Equal(direct, masked);
    }

    [Fact]
    public void Nested_viewport_fade_use_matches_direct()
    {
        byte[] direct = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='24' height='16'><defs><linearGradient id='g'><stop offset='0' stop-color='red'/><stop offset='1' stop-color='blue' stop-opacity='0'/></linearGradient></defs><svg x='8' y='0' width='16' height='16'><polygon points='0,0 16,0 16,8 8,8 8,16 0,16' fill='url(#g)'/></svg></svg>", out _, out _);
        byte[] instanced = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='24' height='16'><defs><linearGradient id='g'><stop offset='0' stop-color='red'/><stop offset='1' stop-color='blue' stop-opacity='0'/></linearGradient></defs><defs><polygon id='p' points='0,0 16,0 16,8 8,8 8,16 0,16' fill='url(#g)'/></defs><svg x='8' y='0' width='16' height='16'><use href='#p'/></svg></svg>", out _, out _);

        Assert.Equal(direct, instanced);
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
}