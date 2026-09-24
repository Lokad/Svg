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

    [Theory]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg'><path d='M0,0 L8,8 L8,0 L0,8 Z' fill='red'/></svg>", "Only simple diagonal polygon contours are implemented;")]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg'><path d='m0,0 l8,0 l0,8 l-8,0 z m8,4 l4,-4 l0,8 z' fill='red'/></svg>", "Only simple diagonal polygon contours are implemented;")]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg'><path d='M0,0 H8 L0,8 Z M16,16 L24,24 L24,16 L16,24 Z' fill='red'/></svg>", "Only simple diagonal polygon contours are implemented;")]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg'><path d='M0,0 H8 V8 H0 Z M8,4 L12,0 L12,8 Z' fill='red'/></svg>", "Only simple diagonal polygon contours are implemented;")]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg'><path d='M0,0 L8,8' fill='red'/></svg>", "Only closed axis-aligned integer paths are implemented.")]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg'><path d='M0,0 H8 M16,16 H24 V24 H16 Z' fill='red'/></svg>", "Only closed axis-aligned integer paths are implemented.")]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg'><polygon points='0,0 8,8 8,0 0,8' fill='red'/></svg>", "Only simple diagonal polygon contours are implemented;")]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg'><polyline points='0,0 8,8 4,4 0,8' fill='red'/></svg>", "Only simple diagonal polygon contours are implemented;")]
    public void Non_simple_diagonal_contours_stay_unsupported(string svg, string messageFragment)
    {
        SvgException exception = Assert.Throws<SvgException>(() => SvgRootParser.Parse(System.Text.Encoding.UTF8.GetBytes(svg), SvgRenderLimits.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, exception.ErrorCode);
        Assert.Contains(messageFragment, exception.Message);
    }

    [Fact]
    public void Degenerate_contours_still_reject_at_tokenizing()
    {
        SvgException exception = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><path d='M0,0 L0,0 L8,0 Z' fill='red'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, exception.ErrorCode);
        Assert.Contains("Only closed axis-aligned integer paths are implemented.", exception.Message);
    }

    [Fact]
    public void Final_subpath_may_omit_Z_for_fill_closure()
    {
        SvgRoot closed = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><path d='M0,0 H8 V8 H0 Z' fill='red'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);
        SvgRoot open = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><path d='M0,0 H8 V8 H0' fill='red'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        Assert.Equal(closed.Rectangles, open.Rectangles);
    }
}