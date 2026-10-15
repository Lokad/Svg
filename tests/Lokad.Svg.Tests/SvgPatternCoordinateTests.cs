using TextEncoding = System.Text.Encoding;

namespace Lokad.Svg.Tests;

public sealed class SvgPatternCoordinateTests
{
    [Theory]
    [InlineData("<rect x='8' y='8' width='40' height='24' fill='url(#p)'/>")]
    [InlineData("<ellipse cx='28' cy='20' rx='20' ry='12' fill='url(#p)'/>")]
    [InlineData("<path d='M8 8H48V32H8Z' fill='url(#p)'/>")]
    [InlineData("<polygon points='8,8 48,8 48,32 8,32' fill='url(#p)'/>")]
    [InlineData("<ellipse cx='28' cy='20' rx='20' ry='12' transform='matrix(2 0 0 1.5 4 3)' fill='url(#p)'/>")]
    [InlineData("<defs><rect id='q' x='8' y='8' width='40' height='24'/></defs><use href='#q' fill='url(#p)'/>")]
    public void Object_box_units_use_the_untransformed_shape_bounds(string shape)
    {
        const string content = "<rect x='1' y='2' width='3' height='4' fill='#337ca6'/>";
        string relative = "<defs><pattern id='p' x='.125' y='.125' width='.25' height='.5' patternUnits='objectBoundingBox'>" + content + "</pattern></defs>";
        string absolute = "<defs><pattern id='p' x='13' y='11' width='10' height='12' patternUnits='userSpaceOnUse'>" + content + "</pattern></defs>";
        Assert.Equal(Render(absolute + shape), Render(relative + shape));
    }

    [Fact]
    public void Reused_patterns_bind_each_original_box_before_clipping_and_stroking()
    {
        const string content = "<rect x='1' y='2' width='3' height='4' fill='#337ca6'/>";
        const string clip = "<clipPath id='c'><rect x='10' y='10' width='20' height='12'/></clipPath>";
        const string shapes = "<rect x='4' y='5' width='40' height='30' fill='url(#p)' clip-path='url(#c)'/><rect x='54' y='5' width='32' height='20' fill='url(#p)' stroke='black' stroke-width='2'/>";
        string relative = "<defs>" + clip + "<pattern id='p' x='.125' y='.125' width='.25' height='.5' patternUnits='objectBoundingBox'>" + content + "</pattern></defs>";
        string absolute = "<defs>" + clip + "<pattern id='a' x='9' y='8.75' width='10' height='15' patternUnits='userSpaceOnUse'>" + content + "</pattern><pattern id='b' x='58' y='7.5' width='8' height='10' patternUnits='userSpaceOnUse'>" + content + "</pattern></defs>";
        string inlined = shapes.Replace("url(#p)' clip-path", "url(#a)' clip-path", StringComparison.Ordinal)
            .Replace("url(#p)' stroke", "url(#b)' stroke", StringComparison.Ordinal);
        Assert.Equal(Render(absolute + inlined), Render(relative + shapes));
    }

    [Theory]
    [InlineData("<rect x='8' y='8' width='0' height='24' fill='url(#p)'/>")]
    [InlineData("<line x1='8' y1='8' x2='48' y2='8' stroke='url(#p)' stroke-width='4'/>")]
    public void Empty_object_boxes_do_not_paint_pattern_textures(string shape)
    {
        const string pattern = "<defs><pattern id='p' width='.25' height='.5' patternUnits='objectBoundingBox'><rect width='4' height='4' fill='red'/></pattern></defs>";
        Assert.Equal(Render(string.Empty), Render(pattern + shape));
    }

    [Fact]
    public void Skewed_oval_pattern_targets_keep_the_existing_explicit_boundary()
    {
        const string input = "<defs><pattern id='p' width='.25' height='.5' patternUnits='objectBoundingBox'><rect width='4' height='4' fill='red'/></pattern></defs><ellipse cx='28' cy='20' rx='20' ry='12' transform='matrix(1 .15 .2 1 4 3)' fill='url(#p)'/>";
        SvgException error = Assert.Throws<SvgException>(() => Render(input));
        Assert.Equal(SvgErrorCode.UnsupportedFeature, error.ErrorCode);
        Assert.Contains("Only axis-aligned pattern transforms", error.Message);
    }

    [Fact]
    public void Object_relative_texture_dimensions_remain_bounded()
    {
        const string input = "<defs><pattern id='p' width='.5' height='.5' patternUnits='objectBoundingBox'><rect width='4' height='4' fill='red'/></pattern></defs><rect width='1000000' height='60' fill='url(#p)'/>";
        SvgException error = Assert.Throws<SvgException>(() => Render(input));
        Assert.Equal(SvgErrorCode.LimitExceeded, error.ErrorCode);
        Assert.Contains("Pattern tile dimensions", error.Message);
    }

    private static byte[] Render(string content)
        => SvgConverter.ConvertToPng(TextEncoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='96' height='80'>" + content + "</svg>"),
            SvgRenderOptions.Default, CancellationToken.None).ToArray();
}
