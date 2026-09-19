namespace Lokad.Svg.Chrome;

internal static class Program
{
    private static async Task<int> Main(string[] arguments)
    {
        if (arguments.Length != 3 || !int.TryParse(arguments[1], out int width) || !int.TryParse(arguments[2], out int height))
        {
            Console.Error.WriteLine("Usage: Lokad.Svg.Chrome <path-to-pinned-chrome> <width> <height> < svg > capture.png");
            return 2;
        }

        using CancellationTokenSource cancellation = new(TimeSpan.FromSeconds(30));
        using MemoryStream svg = new();
        await Console.OpenStandardInput().CopyToAsync(svg, cancellation.Token);
        byte[] png = await ChromeSvgCapture.CaptureAsync(arguments[0], svg.ToArray(), width, height, cancellation.Token);
        await Console.OpenStandardOutput().WriteAsync(png, cancellation.Token);
        return 0;
    }
}
