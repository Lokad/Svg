using Lokad.Svg.Parsing;

namespace Lokad.Svg.Tests;

public sealed class SvgEquivalentShapeTests
{
    [Fact]
    public void Rectangle_path_lowers_identically_to_rectangle()
    {
        using SvgRoot rect = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><rect x='2' y='3' width='8' height='6' fill='red'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);
        using SvgRoot path = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><path d='M2,3 H10 V9 H2 Z' fill='red'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgPathFillAssertions.Equivalent(rect, path);
    }

    [Fact]
    public void Polygon_matches_path_with_same_vertices()
    {
        using SvgRoot polygon = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><polygon points='2,3 10,3 10,9 2,9' fill='red'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);
        using SvgRoot path = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><path d='M2,3 H10 V9 H2 Z' fill='red'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgPathFillAssertions.Equivalent(path, polygon);
    }

    [Fact]
    public void Polyline_fill_matches_polygon()
    {
        using SvgRoot polyline = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><polyline points='2,3 10,3 10,9 2,9' fill='red'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);
        using SvgRoot polygon = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><polygon points='2,3 10,3 10,9 2,9' fill='red'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgPathFillAssertions.Equivalent(polygon, polyline);
    }

    [Fact]
    public void Line_use_matches_direct_line()
    {
        using SvgRoot direct = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><line x1='0' y1='1' x2='4' y2='1' stroke='red' stroke-width='2'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);
        using SvgRoot instanced = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><line id='l' x1='0' y1='1' x2='4' y2='1' stroke='red' stroke-width='2'/></defs><use href='#l'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgPathFillAssertions.Equivalent(direct, instanced);
    }

    [Fact]
    public void Rect_use_matches_direct_rect()
    {
        using SvgRoot direct = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><rect x='2' y='3' width='8' height='6' fill='red'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);
        using SvgRoot instanced = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><rect id='r' x='2' y='3' width='8' height='6' fill='red'/></defs><use href='#r'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgPathFillAssertions.Equivalent(direct, instanced);
    }    [Fact]
    public void Fill_rules_agree_on_simple_contours()
    {
        using SvgRoot nonzero = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><path d='M2,3 H10 V9 H2 Z' fill='red' fill-rule='nonzero'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);
        using SvgRoot evenodd = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><path d='M2,3 H10 V9 H2 Z' fill='red' fill-rule='evenodd'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgPathFillAssertions.Equivalent(nonzero, evenodd);
    }
}