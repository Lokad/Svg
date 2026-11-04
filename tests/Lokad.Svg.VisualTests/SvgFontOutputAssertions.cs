using Lokad.Svg.Fonts;

namespace Lokad.Svg.VisualTests;

/// <summary>Checks output forms while retaining a separate prepared owner for every conversion.</summary>
internal static class SvgFontOutputAssertions
{
    internal static async Task MatchesTwelveForms(byte[] input, SvgRenderOptions options, Func<ValueTask<SvgPreparedDocument>> prepare)
    {
        byte[] unchanged = input.ToArray();
        byte[] png = SvgConverter.ConvertToPng(input, options, CancellationToken.None).ToArray();
        byte[] destination = new byte[png.Length];
        Array.Fill(destination, (byte)0xCC);
        Assert.Equal(png.Length, SvgConverter.ConvertToPng(input, destination, options, CancellationToken.None));
        Assert.Equal(png, destination);
        using MemoryStream stream = new();
        Assert.Equal(png.Length, SvgConverter.ConvertToPng(input, stream, options, CancellationToken.None));
        Assert.Equal(png, stream.ToArray());
        Assert.Equal(png, (await SvgConverter.ConvertToPngAsync(input, options, CancellationToken.None)).ToArray());
        Array.Fill(destination, (byte)0xCC);
        Assert.Equal(png.Length, await SvgConverter.ConvertToPngAsync(input, destination, options, CancellationToken.None));
        Assert.Equal(png, destination);
        using MemoryStream asyncStream = new();
        Assert.Equal(png.Length, await SvgConverter.ConvertToPngAsync(input, asyncStream, options, CancellationToken.None));
        Assert.Equal(png, asyncStream.ToArray());
        for (int mode = 0; mode < 6; mode++)
        {
            // Poison reused memory so a missing write cannot pass on a prior result.
            Array.Fill(destination, (byte)0xCC);
            using SvgPreparedDocument prepared = await prepare();
            using MemoryStream preparedStream = new();
            byte[] preparedPng = mode switch
            {
                0 => prepared.ConvertToPng(CancellationToken.None).ToArray(),
                1 => (await prepared.ConvertToPngAsync(CancellationToken.None)).ToArray(),
                2 => prepared.ConvertToPng(destination, CancellationToken.None) == png.Length ? destination : [],
                3 => await prepared.ConvertToPngAsync(destination, CancellationToken.None) == png.Length ? destination : [],
                4 => prepared.ConvertToPng(preparedStream, CancellationToken.None) == png.Length ? preparedStream.ToArray() : [],
                _ => await prepared.ConvertToPngAsync(preparedStream, CancellationToken.None) == png.Length ? preparedStream.ToArray() : []
            };
            Assert.Equal(png, preparedPng);
        }
        Assert.Equal(unchanged, input);
    }
}
