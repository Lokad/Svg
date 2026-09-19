using System.Diagnostics;

namespace Lokad.Svg.MemoryProbe;

internal static class Program
{
    private static int Main(string[] arguments)
    {
        if (!TryParseArguments(arguments, out ProbeOptions? options) || options is null)
        {
            Console.Error.WriteLine("Usage: Lokad.Svg.MemoryProbe <input.svg> <output.png> [--maximum-accounted-bytes <positive-int64>] [--cancel-after-ms <positive-int32>] [--cancel-after-conversion-start-ms <positive-int32>]");
            return 2;
        }

        using Process process = Process.GetCurrentProcess();
        using CancellationTokenSource? cancellation = options.CancelAfterMilliseconds is int delay ? new CancellationTokenSource(delay) : null;
        CancellationTokenSource? conversionCancellation = null;
        string cancellationMode = options.CancelAfterConversionStartMilliseconds is not null ? "after-conversion-start" : options.CancelAfterMilliseconds is not null ? "before-input-load" : "none";
        int? inputBytes = null;
        Stopwatch stopwatch = Stopwatch.StartNew();
        try
        {
            byte[] svg = File.ReadAllBytes(options.InputPath);
            inputBytes = svg.Length;
            conversionCancellation = options.CancelAfterConversionStartMilliseconds is int conversionDelay ? new CancellationTokenSource(conversionDelay) : null;
            CancellationToken cancellationToken = conversionCancellation?.Token ?? cancellation?.Token ?? CancellationToken.None;
            ReadOnlyMemory<byte> png = SvgConverter.ConvertToPng(svg, CreateOptions(options.MaximumAccountedBytes), cancellationToken);
            File.WriteAllBytes(options.OutputPath, png.Span);
            WriteReport("success", stopwatch, process, png.Length, null, inputBytes, cancellationMode);
            return 0;
        }
        catch (SvgException exception)
        {
            WriteReport("svg-error", stopwatch, process, null, exception.ErrorCode, inputBytes, cancellationMode);
            return 1;
        }
        catch (OperationCanceledException) when ((cancellation is { IsCancellationRequested: true }) || (conversionCancellation is { IsCancellationRequested: true }))
        {
            WriteReport("cancelled", stopwatch, process, null, null, inputBytes, cancellationMode);
            return 3;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            WriteReport("unhandled-" + exception.GetType().Name, stopwatch, process, null, null, inputBytes, cancellationMode);
            return 4;
        }
        finally { conversionCancellation?.Dispose(); }
    }

    private static bool TryParseArguments(string[] arguments, out ProbeOptions? options)
    {
        options = null;
        if (arguments.Length < 2) return false;
        long? maximumAccountedBytes = null;
        int? cancelAfterMilliseconds = null;
        int? cancelAfterConversionStartMilliseconds = null;
        for (int index = 2; index < arguments.Length; index += 2)
        {
            if (index + 1 >= arguments.Length) return false;
            string option = arguments[index];
            string value = arguments[index + 1];
            if (option == "--maximum-accounted-bytes" && long.TryParse(value, out long parsedMaximum) && parsedMaximum > 0)
            {
                maximumAccountedBytes = parsedMaximum;
            }
            else if (option == "--cancel-after-ms" && int.TryParse(value, out int parsedDelay) && parsedDelay > 0)
            {
                if (cancelAfterConversionStartMilliseconds is not null) return false;
                cancelAfterMilliseconds = parsedDelay;
            }
            else if (option == "--cancel-after-conversion-start-ms" && int.TryParse(value, out int parsedConversionDelay) && parsedConversionDelay > 0)
            {
                if (cancelAfterMilliseconds is not null) return false;
                cancelAfterConversionStartMilliseconds = parsedConversionDelay;
            }
            else
            {
                return false;
            }
        }
        options = new ProbeOptions(arguments[0], arguments[1], maximumAccountedBytes, cancelAfterMilliseconds, cancelAfterConversionStartMilliseconds);
        return true;
    }

    private static SvgRenderOptions CreateOptions(long? maximumAccountedBytes)
    {
        if (maximumAccountedBytes is null) return SvgRenderOptions.Default;
        SvgRenderLimits defaults = SvgRenderLimits.Default;
        SvgRenderLimits limits = new(maximumAccountedBytes.Value, defaults.MaximumInputBytes, defaults.MaximumDimension, defaults.MaximumPixels, defaults.MaximumEncodedPngBytes, defaults.MaximumNodes, defaults.MaximumPathCommands, defaults.MaximumWorkUnits, defaults.MaximumXmlDepth);
        return new SvgRenderOptions(null, 1, SvgColor.Transparent, limits);
    }

    private static void WriteReport(string outcome, Stopwatch stopwatch, Process process, int? pngBytes, SvgErrorCode? svgErrorCode, int? inputBytes, string cancellationMode)
    {
        process.Refresh();
        Console.WriteLine("outcome=" + outcome);
        Console.WriteLine("elapsedMilliseconds=" + stopwatch.ElapsedMilliseconds);
        Console.WriteLine("peakWorkingSetBytes=" + process.PeakWorkingSet64);
        Console.WriteLine("cancellationMode=" + cancellationMode);
        if (inputBytes is not null) Console.WriteLine("inputBytes=" + inputBytes.Value);
        if (pngBytes is not null) Console.WriteLine("pngBytes=" + pngBytes.Value);
        if (svgErrorCode is not null) Console.WriteLine("svgErrorCode=" + svgErrorCode.Value);
    }

    private sealed record ProbeOptions(string InputPath, string OutputPath, long? MaximumAccountedBytes, int? CancelAfterMilliseconds, int? CancelAfterConversionStartMilliseconds);
}
