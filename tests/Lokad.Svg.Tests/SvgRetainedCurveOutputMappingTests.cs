using TextEncoding = System.Text.Encoding;

namespace Lokad.Svg.Tests;

public sealed class SvgRetainedCurveOutputMappingTests
{
    [Theory]
    [InlineData("quadratic-start-phases.svg")]
    [InlineData("conic-chop-device-circle.svg")]
    [InlineData("conic-chop-scaled-circle.svg")]
    [InlineData("conic-chop-translated-circle.svg")]
    public async Task Retained_curve_regressions_preserve_six_output_parity(string file)
    {
        byte[] input = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", file));
        byte[] expected = SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None).ToArray();
        byte[] destination = new byte[expected.Length];
        Assert.Equal(expected.Length, SvgConverter.ConvertToPng(input, destination, SvgRenderOptions.Default, CancellationToken.None));
        Assert.Equal(expected, destination);
        using MemoryStream stream = new();
        Assert.Equal(expected.Length, SvgConverter.ConvertToPng(input, stream, SvgRenderOptions.Default, CancellationToken.None));
        Assert.Equal(expected, stream.ToArray());
        Assert.Equal(expected, (await SvgConverter.ConvertToPngAsync(input, SvgRenderOptions.Default, CancellationToken.None)).ToArray());
        Assert.Equal(expected.Length, await SvgConverter.ConvertToPngAsync(input, destination, SvgRenderOptions.Default, CancellationToken.None));
        Assert.Equal(expected, destination);
        using MemoryStream asyncStream = new();
        Assert.Equal(expected.Length, await SvgConverter.ConvertToPngAsync(input, asyncStream, SvgRenderOptions.Default, CancellationToken.None));
        Assert.Equal(expected, asyncStream.ToArray());
    }

    [Theory]
    [InlineData("<path fill='red' d='M0,0 C2,2 6,6 8,8 L8,0 C6,2 2,6 0,8 Z'/>")]
    [InlineData("<path fill='red' fill-rule='evenodd' d='M2,2 C14,2 14,14 2,14 Z M2,2 C14,2 14,14 2,14 Z'/>")]
    [InlineData("<path fill='red' fill-rule='evenodd' d='M1,1 Q15,1 15,15 L1,15 Z M5,5 Q11,5 11,11 L5,11 Z'/>")]
    [InlineData("<defs><linearGradient id='g'><stop stop-color='red'/><stop offset='1' stop-color='blue'/></linearGradient></defs><g transform='translate(.25 .5)'><path fill='url(#g)' fill-rule='evenodd' d='M1,1 C5,5 11,11 15,15 L15,1 C11,5 5,11 1,15 Z'/></g>")]
    public async Task Authored_crossings_and_even_odd_curves_preserve_six_output_parity(string body)
    {
        byte[] input = TextEncoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'>" + body + "</svg>");
        byte[] expected = SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None).ToArray();
        byte[] destination = new byte[expected.Length];
        Assert.Equal(expected.Length, SvgConverter.ConvertToPng(input, destination, SvgRenderOptions.Default, CancellationToken.None));
        Assert.Equal(expected, destination);
        using MemoryStream stream = new();
        Assert.Equal(expected.Length, SvgConverter.ConvertToPng(input, stream, SvgRenderOptions.Default, CancellationToken.None));
        Assert.Equal(expected, stream.ToArray());
        Assert.Equal(expected, (await SvgConverter.ConvertToPngAsync(input, SvgRenderOptions.Default, CancellationToken.None)).ToArray());
        Assert.Equal(expected.Length, await SvgConverter.ConvertToPngAsync(input, destination, SvgRenderOptions.Default, CancellationToken.None));
        Assert.Equal(expected, destination);
        using MemoryStream asyncStream = new();
        Assert.Equal(expected.Length, await SvgConverter.ConvertToPngAsync(input, asyncStream, SvgRenderOptions.Default, CancellationToken.None));
        Assert.Equal(expected, asyncStream.ToArray());
    }

    [Theory]
    [InlineData("M4.137 33.256 C20.619 21.987 33.187 2.135 58.813 4.785 C78.919 12.678 84.013 31.784 66.417 37.161 L21.913 41.317 Z")]
    [InlineData("M8.137 35.319 Q46.913 2.137 83.619 35.319 Q48.137 49.619 8.137 35.319 Z")]
    [InlineData("M-8.137 33.256 C20.619 21.987 33.187 -7.135 58.813 4.785 C108.919 12.678 84.013 61.784 66.417 57.161 Z")]
    public async Task Root_mapping_matches_native_axis_aligned_group_mapping_in_six_outputs(string path)
    {
        string body = "<path fill='red' d='" + path + "'/>";
        byte[] root = TextEncoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='60' height='40' viewBox='0 0 96 56'>" + body + "</svg>");
        byte[] group = TextEncoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='60' height='40'><g transform='matrix(.625 0 0 .625 0 2.5)'>" + body + "</g></svg>");
        byte[] expected = SvgConverter.ConvertToPng(group, SvgRenderOptions.Default, CancellationToken.None).ToArray();
        Assert.Equal(expected, SvgConverter.ConvertToPng(root, SvgRenderOptions.Default, CancellationToken.None).ToArray());
        byte[] destination = new byte[expected.Length];
        Assert.Equal(expected.Length, SvgConverter.ConvertToPng(root, destination, SvgRenderOptions.Default, CancellationToken.None));
        Assert.Equal(expected, destination);
        using MemoryStream stream = new();
        Assert.Equal(expected.Length, SvgConverter.ConvertToPng(root, stream, SvgRenderOptions.Default, CancellationToken.None));
        Assert.Equal(expected, stream.ToArray());
        Assert.Equal(expected, (await SvgConverter.ConvertToPngAsync(root, SvgRenderOptions.Default, CancellationToken.None)).ToArray());
        Assert.Equal(expected.Length, await SvgConverter.ConvertToPngAsync(root, destination, SvgRenderOptions.Default, CancellationToken.None));
        Assert.Equal(expected, destination);
        using MemoryStream asyncStream = new();
        Assert.Equal(expected.Length, await SvgConverter.ConvertToPngAsync(root, asyncStream, SvgRenderOptions.Default, CancellationToken.None));
        Assert.Equal(expected, asyncStream.ToArray());
    }

    [Fact]
    public async Task Composed_root_and_group_mapping_matches_the_recorded_native_matrix_in_six_outputs()
    {
        // The matrix is the independently recorded UAAR browser CTM. Use a
        // small original contour here; the complete export remains in the cohort.
        const string body = "<path fill='red' d='M300.137 380.319 C308.913 373.137 336.619 390.137 340.813 410.319 L312.137 420.619 Z'/>";
        byte[] root = TextEncoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='297' height='370' viewBox='0 0 594.87943 739.14391'><g transform='matrix(5.3156151 0 0 5.3156151 -1284.7008 -1868.0163)'>" + body + "</g></svg>");
        byte[] native = TextEncoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='297' height='370'><g transform='matrix(2.6538784503936768 0 0 2.6538784503936768 -641.4007568359375 -932.14013671875)'>" + body + "</g></svg>");
        byte[] expected = SvgConverter.ConvertToPng(native, SvgRenderOptions.Default, CancellationToken.None).ToArray();
        Assert.Equal(expected, SvgConverter.ConvertToPng(root, SvgRenderOptions.Default, CancellationToken.None).ToArray());
        byte[] destination = new byte[expected.Length];
        Assert.Equal(expected.Length, SvgConverter.ConvertToPng(root, destination, SvgRenderOptions.Default, CancellationToken.None));
        Assert.Equal(expected, destination);
        using MemoryStream stream = new();
        Assert.Equal(expected.Length, SvgConverter.ConvertToPng(root, stream, SvgRenderOptions.Default, CancellationToken.None));
        Assert.Equal(expected, stream.ToArray());
        Assert.Equal(expected, (await SvgConverter.ConvertToPngAsync(root, SvgRenderOptions.Default, CancellationToken.None)).ToArray());
        Assert.Equal(expected.Length, await SvgConverter.ConvertToPngAsync(root, destination, SvgRenderOptions.Default, CancellationToken.None));
        Assert.Equal(expected, destination);
        using MemoryStream asyncStream = new();
        Assert.Equal(expected.Length, await SvgConverter.ConvertToPngAsync(root, asyncStream, SvgRenderOptions.Default, CancellationToken.None));
        Assert.Equal(expected, asyncStream.ToArray());
    }
}
