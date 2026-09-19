using System.Text;
using System.Text.Json;

namespace Lokad.Svg.Chrome;

/// <summary>Captures an SVG in an image element through headless Chrome DevTools.</summary>
internal static class ChromeSvgCapture
{
    internal const int MaximumSvgBytes = 8 * 1024 * 1024;
    private const int MaximumCapturePixels = 2_000_000;

    internal static async Task<byte[]> CaptureAsync(string executable, ReadOnlyMemory<byte> svg, int width, int height, CancellationToken cancellationToken)
    {
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
        if (svg.Length > MaximumSvgBytes) throw new InvalidDataException("SVG input exceeds the Chrome capture limit of 8 MiB.");
        if ((long)width * height > MaximumCapturePixels) throw new InvalidDataException("Chrome capture dimensions exceed the 2-million-pixel transport limit.");
        string svgDataUrl = "data:image/svg+xml;base64," + Convert.ToBase64String(svg.Span);
        string html = "<style>html,body{margin:0;overflow:hidden;background:transparent}</style><img id=svg src='" + svgDataUrl + "' style='display:block'>";
        string pageUrl = "data:text/html;base64," + Convert.ToBase64String(Encoding.UTF8.GetBytes(html));
        await using ChromeProcess chrome = await ChromeProcess.LaunchAsync(executable, cancellationToken);
        await using DevToolsConnection browser = await DevToolsConnection.ConnectAsync(chrome.DebuggerAddress, cancellationToken);
        using JsonDocument created = JsonDocument.Parse(await browser.SendAsync("Target.createTarget", JsonSerializer.Serialize(new { url = pageUrl }), cancellationToken));
        string targetId = created.RootElement.GetProperty("result").GetProperty("targetId").GetString() ?? throw new InvalidDataException("Chrome did not return a target ID.");
        await using DevToolsConnection page = await DevToolsConnection.ConnectToTargetAsync(chrome.DebuggerAddress, targetId, cancellationToken);
        await page.SendAsync("Page.enable", "{}", cancellationToken);
        await page.SendAsync("Emulation.setDeviceMetricsOverride", JsonSerializer.Serialize(new { width, height, deviceScaleFactor = 1, mobile = false }), cancellationToken);
        await page.SendAsync("Emulation.setDefaultBackgroundColorOverride", "{\"color\":{\"r\":0,\"g\":0,\"b\":0,\"a\":0}}", cancellationToken);
        await page.SendAsync("Runtime.evaluate", JsonSerializer.Serialize(new { expression = "document.getElementById('svg').decode().then(()=>new Promise(requestAnimationFrame))", awaitPromise = true, returnByValue = true }), cancellationToken);
        using JsonDocument capture = JsonDocument.Parse(await page.SendAsync("Page.captureScreenshot", JsonSerializer.Serialize(new { format = "png", fromSurface = true, captureBeyondViewport = false, clip = new { x = 0, y = 0, width, height, scale = 1 } }), cancellationToken));
        return Convert.FromBase64String(capture.RootElement.GetProperty("result").GetProperty("data").GetString() ?? throw new InvalidDataException("Chrome returned no screenshot bytes."));
    }

    internal static async Task<byte[]> ReadSvgAsync(Stream input, CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[64 * 1024];
        using MemoryStream svg = new();
        while (true)
        {
            int read = await input.ReadAsync(buffer, cancellationToken);
            if (read == 0) return svg.ToArray();
            if (svg.Length > MaximumSvgBytes - read) throw new InvalidDataException("SVG input exceeds the Chrome capture limit of 8 MiB.");
            svg.Write(buffer, 0, read);
        }
    }
}
