using System.Diagnostics;
using System.Runtime.InteropServices;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.MemoryProbe;

internal static class Program
{
    private static int Main(string[] arguments)
    {
        if (!TryParseArguments(arguments, out ProbeOptions? options) || options is null)
        {
            Console.Error.WriteLine("Usage: Lokad.Svg.MemoryProbe <input.svg> <output.png> [--mode conversion|xml-reader] [--maximum-accounted-bytes <positive-int64>] [--background-color <r,g,b,a>] [--cancel-after-ms <positive-int32>] [--cancel-after-conversion-start-ms <positive-int32>] [--cancel-after-background-start-ms <positive-int32>]");
            return 2;
        }

        using Process process = Process.GetCurrentProcess();
        using CancellationTokenSource? cancellation = options.CancelAfterMilliseconds is int delay ? new CancellationTokenSource(delay) : null;
        CancellationTokenSource? conversionCancellation = null;
        CancellationTokenSource? backgroundCancellation = null;
        IDisposable? backgroundProbe = null;
        bool backgroundInitializationStarted = false;
        bool backgroundCancellationObserved = false;
        string cancellationMode = options.CancelAfterBackgroundStartMilliseconds is not null ? "after-background-start" : options.CancelAfterConversionStartMilliseconds is not null ? "after-conversion-start" : options.CancelAfterMilliseconds is not null ? "before-input-load" : "none";
        int? inputBytes = null;
        string? inputSha256 = null;
        long? conversionManagedAllocatedBytes = null;
        Stopwatch stopwatch = Stopwatch.StartNew();
        try
        {
            byte[] svg = File.ReadAllBytes(options.InputPath);
            inputBytes = svg.Length;
            inputSha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(svg)).ToLowerInvariant();
            if (options.Mode == ProbeMode.XmlReader)
            {
                XmlReaderWorkspaceMeasurement measurement = XmlReaderWorkspaceProbe.Measure(svg, SvgRenderLimits.Default);
                WriteReport("xml-reader-measured", stopwatch, process, null, null, inputBytes, inputSha256, cancellationMode, null,
                    xmlReaderManagedAllocatedBytes: measurement.ManagedAllocatedBytes,
                    xmlReaderWorkspaceBytes: measurement.WorkspaceBytes,
                    xmlReaderTouchedCharacters: measurement.TouchedCharacters,
                    xmlReaderTextNodeCount: measurement.TextNodeCount,
                    xmlReaderSetupManagedAllocatedBytes: measurement.ReaderSetupManagedAllocatedBytes,
                    xmlReaderTraversalManagedAllocatedBytes: measurement.ReaderTraversalManagedAllocatedBytes,
                    xmlReaderAttributeAccessManagedAllocatedBytes: measurement.AttributeAccessManagedAllocatedBytes);
                return 0;
            }
            conversionCancellation = options.CancelAfterConversionStartMilliseconds is int conversionDelay ? new CancellationTokenSource(conversionDelay) : null;
            backgroundCancellation = options.CancelAfterBackgroundStartMilliseconds is int backgroundDelay ? new CancellationTokenSource() : null;
            if (backgroundCancellation is not null)
            {
                CancellationTokenSource capturedCancellation = backgroundCancellation;
                int capturedDelay = options.CancelAfterBackgroundStartMilliseconds!.Value;
                backgroundProbe = BackgroundInitializationProbe.Arm(
                    () => { backgroundInitializationStarted = true; capturedCancellation.CancelAfter(capturedDelay); },
                    () => { backgroundCancellationObserved = true; });
            }
            CancellationToken cancellationToken = backgroundCancellation?.Token ?? conversionCancellation?.Token ?? cancellation?.Token ?? CancellationToken.None;
            long allocationStart = GC.GetTotalAllocatedBytes(precise: true);
            ReadOnlyMemory<byte> png;
            try { png = SvgConverter.ConvertToPng(svg, CreateOptions(options.MaximumAccountedBytes, options.BackgroundColor), cancellationToken); }
            finally { conversionManagedAllocatedBytes = GC.GetTotalAllocatedBytes(precise: true) - allocationStart; }
            File.WriteAllBytes(options.OutputPath, png.Span);
            WriteReport("success", stopwatch, process, png.Length, null, inputBytes, inputSha256, cancellationMode, conversionManagedAllocatedBytes, backgroundInitializationStarted, backgroundCancellationObserved);
            return 0;
        }
        catch (SvgException exception)
        {
            WriteReport("svg-error", stopwatch, process, null, exception.ErrorCode, inputBytes, inputSha256, cancellationMode, conversionManagedAllocatedBytes, backgroundInitializationStarted, backgroundCancellationObserved);
            return 1;
        }
        catch (OperationCanceledException) when ((cancellation is { IsCancellationRequested: true }) || (conversionCancellation is { IsCancellationRequested: true }) || (backgroundCancellation is { IsCancellationRequested: true }))
        {
            WriteReport("cancelled", stopwatch, process, null, null, inputBytes, inputSha256, cancellationMode, conversionManagedAllocatedBytes, backgroundInitializationStarted, backgroundCancellationObserved);
            return 3;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            WriteReport("unhandled-" + exception.GetType().Name, stopwatch, process, null, null, inputBytes, inputSha256, cancellationMode, conversionManagedAllocatedBytes, backgroundInitializationStarted, backgroundCancellationObserved);
            return 4;
        }
        finally
        {
            backgroundProbe?.Dispose();
            backgroundCancellation?.Dispose();
            conversionCancellation?.Dispose();
        }
    }

    private static bool TryParseArguments(string[] arguments, out ProbeOptions? options)
    {
        options = null;
        if (arguments.Length < 2) return false;
        long? maximumAccountedBytes = null;
        SvgColor backgroundColor = SvgColor.Transparent;
        int? cancelAfterMilliseconds = null;
        int? cancelAfterConversionStartMilliseconds = null;
        int? cancelAfterBackgroundStartMilliseconds = null;
        ProbeMode mode = ProbeMode.Conversion;
        for (int index = 2; index < arguments.Length; index += 2)
        {
            if (index + 1 >= arguments.Length) return false;
            string option = arguments[index];
            string value = arguments[index + 1];
            if (option == "--maximum-accounted-bytes" && long.TryParse(value, out long parsedMaximum) && parsedMaximum > 0)
            {
                maximumAccountedBytes = parsedMaximum;
            }
            else if (option == "--background-color" && TryParseColor(value, out SvgColor parsedColor))
            {
                backgroundColor = parsedColor;
            }
            else if (option == "--mode" && value is "conversion" or "xml-reader")
            {
                mode = value == "conversion" ? ProbeMode.Conversion : ProbeMode.XmlReader;
            }
            else if (option == "--cancel-after-ms" && int.TryParse(value, out int parsedDelay) && parsedDelay > 0)
            {
                if (cancelAfterConversionStartMilliseconds is not null || cancelAfterBackgroundStartMilliseconds is not null) return false;
                cancelAfterMilliseconds = parsedDelay;
            }
            else if (option == "--cancel-after-conversion-start-ms" && int.TryParse(value, out int parsedConversionDelay) && parsedConversionDelay > 0)
            {
                if (cancelAfterMilliseconds is not null || cancelAfterBackgroundStartMilliseconds is not null) return false;
                cancelAfterConversionStartMilliseconds = parsedConversionDelay;
            }
            else if (option == "--cancel-after-background-start-ms" && int.TryParse(value, out int parsedBackgroundDelay) && parsedBackgroundDelay > 0)
            {
                if (cancelAfterMilliseconds is not null || cancelAfterConversionStartMilliseconds is not null) return false;
                cancelAfterBackgroundStartMilliseconds = parsedBackgroundDelay;
            }
            else
            {
                return false;
            }
        }
        if (mode == ProbeMode.XmlReader && (maximumAccountedBytes is not null || backgroundColor != SvgColor.Transparent || cancelAfterMilliseconds is not null || cancelAfterConversionStartMilliseconds is not null || cancelAfterBackgroundStartMilliseconds is not null)) return false;
        options = new ProbeOptions(arguments[0], arguments[1], maximumAccountedBytes, backgroundColor, cancelAfterMilliseconds, cancelAfterConversionStartMilliseconds, cancelAfterBackgroundStartMilliseconds, mode);
        return true;
    }

    private static bool TryParseColor(string value, out SvgColor color)
    {
        color = SvgColor.Transparent;
        string[] components = value.Split(',');
        if (components.Length != 4 || !byte.TryParse(components[0], out byte red) || !byte.TryParse(components[1], out byte green) || !byte.TryParse(components[2], out byte blue) || !byte.TryParse(components[3], out byte alpha)) return false;
        color = new SvgColor(red, green, blue, alpha);
        return true;
    }

    private static SvgRenderOptions CreateOptions(long? maximumAccountedBytes, SvgColor backgroundColor)
    {
        if (maximumAccountedBytes is null) return new SvgRenderOptions(null, 1, backgroundColor, SvgRenderLimits.Default);
        SvgRenderLimits defaults = SvgRenderLimits.Default;
        SvgRenderLimits limits = new(maximumAccountedBytes.Value, defaults.MaximumInputBytes, defaults.MaximumDimension, defaults.MaximumPixels, defaults.MaximumEncodedPngBytes, defaults.MaximumNodes, defaults.MaximumPathCommands, defaults.MaximumWorkUnits, defaults.MaximumXmlDepth);
        return new SvgRenderOptions(null, 1, backgroundColor, limits);
    }

    private static void WriteReport(string outcome, Stopwatch stopwatch, Process process, int? pngBytes, SvgErrorCode? svgErrorCode, int? inputBytes, string? inputSha256, string cancellationMode, long? conversionManagedAllocatedBytes, bool backgroundInitializationStarted = false, bool backgroundCancellationObserved = false, long? xmlReaderManagedAllocatedBytes = null, long? xmlReaderWorkspaceBytes = null, long? xmlReaderTouchedCharacters = null, long? xmlReaderTextNodeCount = null, long? xmlReaderSetupManagedAllocatedBytes = null, long? xmlReaderTraversalManagedAllocatedBytes = null, long? xmlReaderAttributeAccessManagedAllocatedBytes = null)
    {
        process.Refresh();
        Console.WriteLine("outcome=" + outcome);
        Console.WriteLine("elapsedMilliseconds=" + stopwatch.ElapsedMilliseconds);
        Console.WriteLine("peakWorkingSetBytes=" + process.PeakWorkingSet64);
        Console.WriteLine("operatingSystem=" + RuntimeInformation.OSDescription);
        Console.WriteLine("processArchitecture=" + RuntimeInformation.ProcessArchitecture);
        Console.WriteLine("runtimeFramework=" + RuntimeInformation.FrameworkDescription);
        Console.WriteLine("cancellationMode=" + cancellationMode);
        Console.WriteLine("backgroundInitializationStarted=" + backgroundInitializationStarted.ToString().ToLowerInvariant());
        Console.WriteLine("backgroundCancellationObserved=" + backgroundCancellationObserved.ToString().ToLowerInvariant());
        if (conversionManagedAllocatedBytes is not null) Console.WriteLine("conversionManagedAllocatedBytes=" + conversionManagedAllocatedBytes.Value);
        if (inputBytes is not null) Console.WriteLine("inputBytes=" + inputBytes.Value);
        if (inputSha256 is not null) Console.WriteLine("inputSha256=" + inputSha256);
        if (pngBytes is not null) Console.WriteLine("pngBytes=" + pngBytes.Value);
        if (svgErrorCode is not null) Console.WriteLine("svgErrorCode=" + svgErrorCode.Value);
        if (xmlReaderManagedAllocatedBytes is not null) Console.WriteLine("xmlReaderManagedAllocatedBytes=" + xmlReaderManagedAllocatedBytes.Value);
        if (xmlReaderWorkspaceBytes is not null) Console.WriteLine("xmlReaderWorkspaceBytes=" + xmlReaderWorkspaceBytes.Value);
        if (xmlReaderTouchedCharacters is not null) Console.WriteLine("xmlReaderTouchedCharacters=" + xmlReaderTouchedCharacters.Value);
        if (xmlReaderTextNodeCount is not null) Console.WriteLine("xmlReaderTextNodeCount=" + xmlReaderTextNodeCount.Value);
        if (xmlReaderSetupManagedAllocatedBytes is not null) Console.WriteLine("xmlReaderSetupManagedAllocatedBytes=" + xmlReaderSetupManagedAllocatedBytes.Value);
        if (xmlReaderTraversalManagedAllocatedBytes is not null) Console.WriteLine("xmlReaderTraversalManagedAllocatedBytes=" + xmlReaderTraversalManagedAllocatedBytes.Value);
        if (xmlReaderAttributeAccessManagedAllocatedBytes is not null) Console.WriteLine("xmlReaderAttributeAccessManagedAllocatedBytes=" + xmlReaderAttributeAccessManagedAllocatedBytes.Value);
    }

    private sealed record ProbeOptions(string InputPath, string OutputPath, long? MaximumAccountedBytes, SvgColor BackgroundColor, int? CancelAfterMilliseconds, int? CancelAfterConversionStartMilliseconds, int? CancelAfterBackgroundStartMilliseconds, ProbeMode Mode);

    private enum ProbeMode { Conversion, XmlReader }
}
