using Lokad.Svg.Parsing;

namespace Lokad.Svg.Tests;

public sealed class SvgContourTopologyTests
{
    [Fact]
    public void Classify_identifies_orthogonal_contours()
    {
        SvgPoint[] square = [new SvgPoint(0, 0), new SvgPoint(8, 0), new SvgPoint(8, 8), new SvgPoint(0, 8)];

        Assert.Equal(SvgContourKind.Orthogonal, SvgContourTopology.Classify(square, 0, square.Length));
    }

    [Fact]
    public void Classify_identifies_diagonal_contours()
    {
        SvgPoint[] triangle = [new SvgPoint(0, 0), new SvgPoint(8, 0), new SvgPoint(0, 8)];

        Assert.Equal(SvgContourKind.Diagonal, SvgContourTopology.Classify(triangle, 0, triangle.Length));
    }

    [Fact]
    public void Classify_follows_the_implicit_closing_edge()
    {
        SvgPoint[] openCorner = [new SvgPoint(0, 0), new SvgPoint(8, 0), new SvgPoint(8, 8)];

        Assert.Equal(SvgContourKind.Diagonal, SvgContourTopology.Classify(openCorner, 0, openCorner.Length));
    }

    [Fact]
    public void Classify_supports_subslices()
    {
        SvgPoint[] contours = [new SvgPoint(0, 0), new SvgPoint(8, 0), new SvgPoint(8, 8), new SvgPoint(0, 8), new SvgPoint(16, 16), new SvgPoint(24, 16), new SvgPoint(16, 24)];

        Assert.Equal(SvgContourKind.Orthogonal, SvgContourTopology.Classify(contours, 0, 4));
        Assert.Equal(SvgContourKind.Diagonal, SvgContourTopology.Classify(contours, 4, 7));
    }

    [Fact]
    public void Classify_rejects_out_of_range_slices()
    {
        SvgPoint[] points = [new SvgPoint(0, 0), new SvgPoint(8, 0)];

        Assert.Throws<ArgumentOutOfRangeException>(() => SvgContourTopology.Classify(points, -1, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => SvgContourTopology.Classify(points, 0, 3));
        Assert.Throws<ArgumentOutOfRangeException>(() => SvgContourTopology.Classify(points, 2, 1));
    }

    [Fact]
    public void Open_segment_plus_square_parses_single_fill()
    {
        SvgRoot root = SvgRootParser.Parse(System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg'><path d='M0,0 H8 M16,16 H24 V24 H16 Z' fill='red'/></svg>"), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Single(root.Geometries);
        Assert.False(root.HasUnsupportedChild);
    }

    [Fact]
    public void Closed_two_point_fill_parses_empty()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><path d='M0,0 L0,0 L8,0 Z' fill='red'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Empty(root.Geometries);
        Assert.False(root.HasUnsupportedChild);
    }
    [Fact]
    public void Diagonal_open_fill_parses_to_empty_geometries()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><path d='M0,0 L8,8' fill='red'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Empty(root.Geometries);
        Assert.False(root.HasUnsupportedChild);
    }


    [Fact]
    public void Final_subpath_may_omit_Z_for_fill_closure()
    {
        SvgRoot closed = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><path d='M0,0 H8 V8 H0 Z' fill='red'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);
        SvgRoot open = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><path d='M0,0 H8 V8 H0' fill='red'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(closed.Rectangles, open.Rectangles);
    }
}
