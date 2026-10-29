using System.Diagnostics;
using System.Runtime.InteropServices;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.MemoryProbe;

internal static class Program
{
    private static async Task<int> Main(string[] arguments)
    {
        if (!TryParseArguments(arguments, out ProbeOptions? options) || options is null)
        {
            Console.Error.WriteLine("Usage: Lokad.Svg.MemoryProbe <input.svg> <output.png> [--mode conversion|xml-reader|font-discovery] [--memory-budget-bytes <positive-int64>] [--background-color <r,g,b,a>] [--cancel-after-ms <positive-int32>] [--cancel-after-conversion-start-ms <positive-int32>] [--cancel-after-background-start-ms <nonnegative-int32; 0 cancels synchronously>]");
            return 2;
        }

        using Process process = Process.GetCurrentProcess();
        using HostMemoryBudget memoryBudget = new(options.MemoryBudgetBytes ?? 268_435_456L);
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
                WriteXmlReaderReport(measurement);
                return 0;
            }
            if (options.Mode == ProbeMode.FontDiscovery)
            {
                FontDiscoveryMeasurement measurement = await FontDiscoveryProbe.MeasureAsync(svg, SvgRenderLimits.Default, memoryBudget, cancellation?.Token ?? CancellationToken.None).ConfigureAwait(false);
                return WriteFontDiscoveryReport(measurement);
            }
            conversionCancellation = options.CancelAfterConversionStartMilliseconds is int conversionDelay ? new CancellationTokenSource(conversionDelay) : null;
            if (options.CancelAfterBackgroundStartMilliseconds is int backgroundDelay)
            {
                backgroundCancellation = new CancellationTokenSource();
                CancellationTokenSource capturedCancellation = backgroundCancellation;
                backgroundProbe = BackgroundInitializationProbe.Arm(
                    () =>
                    {
                        backgroundInitializationStarted = true;
                        if (backgroundDelay == 0) capturedCancellation.Cancel();
                        else capturedCancellation.CancelAfter(backgroundDelay);
                    },
                    () => { backgroundCancellationObserved = true; });
            }
            CancellationToken cancellationToken = backgroundCancellation?.Token ?? conversionCancellation?.Token ?? cancellation?.Token ?? CancellationToken.None;
            long allocationStart = GC.GetTotalAllocatedBytes(precise: true);
            SvgPngResult result;
            try { result = SvgConverter.ConvertToOwnedPng(svg, CreateOptions(memoryBudget, options.BackgroundColor), cancellationToken); }
            finally { conversionManagedAllocatedBytes = GC.GetTotalAllocatedBytes(precise: true) - allocationStart; }
            using SvgPngResult output = result;
            ReadOnlyMemory<byte> png = output.Memory;
            File.WriteAllBytes(options.OutputPath, png.Span);
            WriteReportHeader("success", png.Length, null);
            return 0;
        }
        catch (SvgException exception)
        {
            WriteReportHeader("svg-error", null, exception.ErrorCode);
            return 1;
        }
        catch (OperationCanceledException) when ((cancellation is { IsCancellationRequested: true }) || (conversionCancellation is { IsCancellationRequested: true }) || (backgroundCancellation is { IsCancellationRequested: true }))
        {
            WriteReportHeader("cancelled", null, null);
            return 3;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            WriteReportHeader("unhandled-" + exception.GetType().Name, null, null);
            return 4;
        }
        finally
        {
            backgroundProbe?.Dispose();
            backgroundCancellation?.Dispose();
            conversionCancellation?.Dispose();
        }

        void WriteReportHeader(string outcome, int? pngBytes, SvgErrorCode? svgErrorCode)
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
        }

        void WriteXmlReaderReport(XmlReaderWorkspaceMeasurement measurement)
        {
            WriteReportHeader("xml-reader-measured", null, null);
            Console.WriteLine("xmlReaderManagedAllocatedBytes=" + measurement.ManagedAllocatedBytes);
            Console.WriteLine("xmlReaderWorkspaceBytes=" + measurement.WorkspaceBytes);
            Console.WriteLine("xmlReaderTouchedCharacters=" + measurement.TouchedCharacters);
            Console.WriteLine("xmlReaderTextNodeCount=" + measurement.TextNodeCount);
            Console.WriteLine("xmlReaderSetupManagedAllocatedBytes=" + measurement.ReaderSetupManagedAllocatedBytes);
            Console.WriteLine("xmlReaderTraversalManagedAllocatedBytes=" + measurement.ReaderTraversalManagedAllocatedBytes);
            Console.WriteLine("xmlReaderAttributeAccessManagedAllocatedBytes=" + measurement.AttributeAccessManagedAllocatedBytes);
        }

        int WriteFontDiscoveryReport(FontDiscoveryMeasurement measurement)
        {
            SvgErrorCode? failure = measurement.SnapshotErrorCode ?? measurement.DiscoveryErrorCode;
            string outcome = measurement.Cancelled ? "cancelled" : failure is not null ? "svg-error" : "success";
            WriteReportHeader(outcome, null, measurement.Cancelled ? null : failure);
            Console.WriteLine("providerCalls=" + measurement.ProviderCalls);
            if (measurement.DiscoveryRequestCount is not null) Console.WriteLine("discoveryRequestCount=" + measurement.DiscoveryRequestCount.Value);
            Console.WriteLine("discoveryCurrentBytes=" + measurement.DiscoveryCurrentBytes);
            Console.WriteLine("discoveryPeakBytes=" + measurement.DiscoveryPeakBytes);
            Console.WriteLine("discoveryDeniedReservations=" + measurement.DiscoveryDeniedReservations);
            return measurement.Cancelled ? 3 : failure is not null ? 1 : 0;
        }
    }

    private static bool TryParseArguments(string[] arguments, out ProbeOptions? options)
    {
        options = null;
        if (arguments.Length < 2) return false;
        long? memoryBudgetBytes = null;
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
            if (option == "--memory-budget-bytes" && long.TryParse(value, out long parsedMaximum) && parsedMaximum > 0)
            {
                memoryBudgetBytes = parsedMaximum;
            }
            else if (option == "--background-color" && TryParseColor(value, out SvgColor parsedColor))
            {
                backgroundColor = parsedColor;
            }
            else if (option == "--mode" && value is "conversion" or "xml-reader" or "font-discovery")
            {
                mode = value == "conversion" ? ProbeMode.Conversion : value == "xml-reader" ? ProbeMode.XmlReader : ProbeMode.FontDiscovery;
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
            else if (option == "--cancel-after-background-start-ms" && int.TryParse(value, out int parsedBackgroundDelay) && parsedBackgroundDelay >= 0)
            {
                if (cancelAfterMilliseconds is not null || cancelAfterConversionStartMilliseconds is not null) return false;
                cancelAfterBackgroundStartMilliseconds = parsedBackgroundDelay;
            }
            else
            {
                return false;
            }
        }
        if (mode == ProbeMode.XmlReader && (memoryBudgetBytes is not null || backgroundColor != SvgColor.Transparent || cancelAfterMilliseconds is not null || cancelAfterConversionStartMilliseconds is not null || cancelAfterBackgroundStartMilliseconds is not null)) return false;
        if (mode == ProbeMode.FontDiscovery && (backgroundColor != SvgColor.Transparent || cancelAfterConversionStartMilliseconds is not null || cancelAfterBackgroundStartMilliseconds is not null)) return false;
        options = new ProbeOptions(arguments[0], arguments[1], memoryBudgetBytes, backgroundColor, cancelAfterMilliseconds, cancelAfterConversionStartMilliseconds, cancelAfterBackgroundStartMilliseconds, mode);
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

    private static SvgRenderOptions CreateOptions(IMemoryBudget memoryBudget, SvgColor backgroundColor)
        => new SvgRenderOptions(null, 1, backgroundColor, SvgRenderLimits.Default).WithMemoryBudget(memoryBudget);

    private sealed record ProbeOptions(string InputPath, string OutputPath, long? MemoryBudgetBytes, SvgColor BackgroundColor, int? CancelAfterMilliseconds, int? CancelAfterConversionStartMilliseconds, int? CancelAfterBackgroundStartMilliseconds, ProbeMode Mode);

    private enum ProbeMode { Conversion, XmlReader, FontDiscovery }
}
