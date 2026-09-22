using System.Text;
using System.Text.Json;

namespace Lokad.Svg.Chrome;

/// <summary>Captures an SVG in an image element through headless Chrome DevTools.</summary>
internal static class ChromeSvgCapture
{
    internal enum CaptureBackground
    {
        Transparent,
        White,
        Black,
    }

    internal const int MaximumSvgBytes = 8 * 1024 * 1024;
    private const int MaximumCapturePixels = 2_000_000;

    internal static async Task<byte[]> CaptureAsync(string executable, ReadOnlyMemory<byte> svg, int width, int height, CaptureBackground background, CancellationToken cancellationToken)
    {
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
        if (svg.Length > MaximumSvgBytes) throw new InvalidDataException("SVG input exceeds the Chrome capture limit of 8 MiB.");
        if ((long)width * height > MaximumCapturePixels) throw new InvalidDataException("Chrome capture dimensions exceed the 2-million-pixel transport limit.");
        string svgDataUrl = "data:image/svg+xml;base64," + Convert.ToBase64String(svg.Span);
        string pageBackground = background == CaptureBackground.White ? "#ffffff" : background == CaptureBackground.Black ? "#000000" : "transparent";
        string html = "<style>html,body{margin:0;overflow:hidden;background:" + pageBackground + "}</style><img id=svg src='" + svgDataUrl + "' style='display:block'>";
        string pageUrl = "data:text/html;base64," + Convert.ToBase64String(Encoding.UTF8.GetBytes(html));
        await using ChromeProcess chrome = await ChromeProcess.LaunchAsync(executable, cancellationToken);
        byte[] screenshot;
        await using (DevToolsConnection browser = await DevToolsConnection.ConnectAsync(chrome.DebuggerAddress, cancellationToken))
        {
            using JsonDocument created = JsonDocument.Parse(await browser.SendAsync("Target.createTarget", JsonSerializer.Serialize(new { url = "about:blank" }), cancellationToken));
            string targetId = created.RootElement.GetProperty("result").GetProperty("targetId").GetString() ?? throw new InvalidDataException("Chrome did not return a target ID.");
            await using (DevToolsConnection page = await DevToolsConnection.ConnectToTargetAsync(chrome.DebuggerAddress, targetId, cancellationToken))
            {
                await page.SendAsync("Page.enable", "{}", cancellationToken);
                await page.SendAsync("Network.enable", "{}", cancellationToken);
                await page.SendAsync("Network.setBlockedURLs", "{\"urls\":[\"http://*\",\"https://*\",\"file://*\",\"ftp://*\",\"ws://*\",\"wss://*\"]}", cancellationToken);
                await page.SendAsync("Emulation.setDeviceMetricsOverride", JsonSerializer.Serialize(new { width, height, deviceScaleFactor = 1, mobile = false }), cancellationToken);
                string backgroundOverride = background == CaptureBackground.White ? "{\"color\":{\"r\":255,\"g\":255,\"b\":255,\"a\":255}}" : background == CaptureBackground.Black ? "{\"color\":{\"r\":0,\"g\":0,\"b\":0,\"a\":255}}" : "{\"color\":{\"r\":0,\"g\":0,\"b\":0,\"a\":0}}";
                await page.SendAsync("Emulation.setDefaultBackgroundColorOverride", backgroundOverride, cancellationToken);
                await page.SendAsync("Page.navigate", JsonSerializer.Serialize(new { url = pageUrl }), cancellationToken);
                await page.WaitForEventAsync("Page.loadEventFired", cancellationToken);
                await page.SendAsync("Runtime.evaluate", JsonSerializer.Serialize(new { expression = "document.getElementById('svg').decode().then(()=>new Promise(requestAnimationFrame))", awaitPromise = true, returnByValue = true }), cancellationToken);
                using JsonDocument capture = JsonDocument.Parse(await page.SendAsync("Page.captureScreenshot", JsonSerializer.Serialize(new { format = "png", fromSurface = true, captureBeyondViewport = false, clip = new { x = 0, y = 0, width, height, scale = 1 } }), cancellationToken));
                screenshot = Convert.FromBase64String(capture.RootElement.GetProperty("result").GetProperty("data").GetString() ?? throw new InvalidDataException("Chrome returned no screenshot bytes."));
            }
            await browser.SendAsync("Browser.close", "{}", cancellationToken);
        }
        return screenshot;
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
