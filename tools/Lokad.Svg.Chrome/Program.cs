namespace Lokad.Svg.Chrome;

using System.Runtime.InteropServices;

internal static class Program
{
    private static async Task<int> Main(string[] arguments)
    {
        if (arguments.Length == 1 && arguments[0] == "--runtime")
        {
            Console.WriteLine(RuntimeInformation.FrameworkDescription);
            return 0;
        }

        if ((arguments.Length != 3 && (arguments.Length != 4 || arguments[3] != "white")) || !int.TryParse(arguments[1], out int width) || !int.TryParse(arguments[2], out int height))
        {
            Console.Error.WriteLine("Usage: Lokad.Svg.Chrome <path-to-pinned-chrome> <width> <height> [white] < svg > capture.png");
            return 2;
        }

        using CancellationTokenSource cancellation = new(TimeSpan.FromSeconds(30));
        byte[] svg = await ChromeSvgCapture.ReadSvgAsync(Console.OpenStandardInput(), cancellation.Token);
        ChromeSvgCapture.CaptureBackground background = arguments.Length == 4 ? ChromeSvgCapture.CaptureBackground.White : ChromeSvgCapture.CaptureBackground.Transparent;
        byte[] png = await ChromeSvgCapture.CaptureAsync(arguments[0], svg, width, height, background, cancellation.Token);
        await Console.OpenStandardOutput().WriteAsync(png, cancellation.Token);
        return 0;
    }
}
