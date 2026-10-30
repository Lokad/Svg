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

        const string usage = "Usage: Lokad.Svg.Chrome <path-to-pinned-chrome> <width> <height> [white|black] [--image-viewport <CSS-width> <CSS-height>] < svg > capture.png";
        if (arguments.Length is not (3 or 4 or 6 or 7) || !int.TryParse(arguments[1], out int width) || !int.TryParse(arguments[2], out int height))
        {
            Console.Error.WriteLine(usage);
            return 2;
        }

        bool hasBackground = arguments.Length is 4 or 7;
        if (hasBackground && arguments[3] is not ("white" or "black"))
        {
            Console.Error.WriteLine(usage);
            return 2;
        }
        ChromeSvgCapture.ImageViewport? imageViewport = null;
        if (arguments.Length is 6 or 7)
        {
            int index = hasBackground ? 4 : 3;
            if (arguments[index] != "--image-viewport"
                || !double.TryParse(arguments[index + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out double cssWidth)
                || !double.TryParse(arguments[index + 2], NumberStyles.Float, CultureInfo.InvariantCulture, out double cssHeight))
            {
                Console.Error.WriteLine(usage);
                return 2;
            }
            imageViewport = new(cssWidth, cssHeight);
        }

        try
        {
            using CancellationTokenSource cancellation = new(TimeSpan.FromSeconds(30));
            byte[] svg = await ChromeSvgCapture.ReadSvgAsync(Console.OpenStandardInput(), cancellation.Token);
            ChromeSvgCapture.CaptureBackground background = ChromeSvgCapture.CaptureBackground.Transparent;
            if (hasBackground)
            {
                background = arguments[3] == "black" ? ChromeSvgCapture.CaptureBackground.Black : ChromeSvgCapture.CaptureBackground.White;
            }
            byte[] png = imageViewport is {} viewport
                ? await ChromeSvgCapture.CaptureAtViewportAsync(arguments[0], svg, width, height, viewport, background, cancellation.Token)
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
