using TextEncoding = System.Text.Encoding;

namespace Lokad.Svg.Tests;

public sealed class SvgRetainedCurveOutputMappingTests
{
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
}
