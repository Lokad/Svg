namespace Lokad.Svg.Chrome;

using System.Runtime.InteropServices;
using System.Globalization;

internal static class Program
{
    private static async Task<int> Main(string[] arguments)
    {
        if (arguments.Length == 1 && arguments[0] == "--runtime")
        {
            Console.WriteLine(RuntimeInformation.FrameworkDescription);
            return 0;
        }

        const string usage = "Usage: Lokad.Svg.Chrome <path-to-pinned-chrome> <physical-width> <physical-height> [white|black] [--image-viewport <CSS-width> <CSS-height> [--output-scale <density>]] < svg > capture.png";
        if (arguments.Length < 3 || !int.TryParse(arguments[1], out int width) || !int.TryParse(arguments[2], out int height))
        {
            Console.Error.WriteLine(usage);
            return 2;
        }

        int index = 3;
        ChromeSvgCapture.CaptureBackground background = ChromeSvgCapture.CaptureBackground.Transparent;
        if (index < arguments.Length && arguments[index] is "white" or "black")
        {
            background = arguments[index++] == "black" ? ChromeSvgCapture.CaptureBackground.Black : ChromeSvgCapture.CaptureBackground.White;
        }
        ChromeSvgCapture.ImageViewport? imageViewport = null;
        if (index < arguments.Length && arguments[index] == "--image-viewport")
        {
            if (arguments.Length - index < 3
                || !double.TryParse(arguments[index + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out double cssWidth)
                || !double.TryParse(arguments[index + 2], NumberStyles.Float, CultureInfo.InvariantCulture, out double cssHeight))
            {
                Console.Error.WriteLine(usage);
                return 2;
            }
            imageViewport = new(cssWidth, cssHeight);
            index += 3;
        }
        double outputScale = 1;
        if (imageViewport is not null && arguments.Length - index == 2 && arguments[index] == "--output-scale"
            && double.TryParse(arguments[index + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out outputScale)) index += 2;
        if (index != arguments.Length)
        {
            Console.Error.WriteLine(usage);
            return 2;
        }

        try
        {
            using CancellationTokenSource cancellation = new(TimeSpan.FromSeconds(30));
            byte[] svg = await ChromeSvgCapture.ReadSvgAsync(Console.OpenStandardInput(), cancellation.Token);
            byte[] png = imageViewport is {} viewport
                ? await ChromeSvgCapture.CaptureAtDensityAsync(arguments[0], svg, width, height, viewport, outputScale, background, cancellation.Token)
                : await ChromeSvgCapture.CaptureAsync(arguments[0], svg, width, height, background, cancellation.Token);
            await Console.OpenStandardOutput().WriteAsync(png, cancellation.Token);
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(ChromeDiagnostics.Redact(error.Message));
            return error is OperationCanceledException ? 3 : 1;
        }
    }
}
