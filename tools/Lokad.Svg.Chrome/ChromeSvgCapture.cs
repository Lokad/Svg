using System.Text;
using System.Text.Json;
using System.Globalization;

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

    /// <summary>Defines the image element's CSS viewport independently of screenshot dimensions.</summary>
    internal readonly record struct ImageViewport(double Width, double Height);

    internal const int MaximumSvgBytes = 8 * 1024 * 1024;
    internal const int MaximumCapturePixels = 8_388_608;
    internal const int MaximumCaptureDimension = 16_384;
    private const int MaximumNavigationUrlCharacters = 2 * 1024 * 1024;

    internal static Task<byte[]> CaptureAsync(string executable, ReadOnlyMemory<byte> svg, int width, int height, CaptureBackground background, CancellationToken cancellationToken)
        => CaptureCoreAsync(executable, svg, width, height, null, background, cancellationToken);

    /// <summary>Captures unchanged SVG bytes with an explicit image viewport; the caller selects screenshot rounding separately.</summary>
    internal static Task<byte[]> CaptureAtViewportAsync(string executable, ReadOnlyMemory<byte> svg, int width, int height,
        ImageViewport imageViewport, CaptureBackground background, CancellationToken cancellationToken)
        => CaptureCoreAsync(executable, svg, width, height, imageViewport, background, cancellationToken);

    private static async Task<byte[]> CaptureCoreAsync(string executable, ReadOnlyMemory<byte> svg, int width, int height,
        ImageViewport? imageViewport, CaptureBackground background, CancellationToken cancellationToken)
    {
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
        if (width > MaximumCaptureDimension || height > MaximumCaptureDimension) throw new InvalidDataException("Chrome capture dimensions exceed the 16,384-pixel side limit.");
        if (svg.Length > MaximumSvgBytes) throw new InvalidDataException("SVG input exceeds the Chrome capture limit of 8 MiB.");
        if ((long)width * height > MaximumCapturePixels) throw new InvalidDataException("Chrome capture dimensions exceed the 8,388,608-pixel transport limit.");
        if (imageViewport is {} viewport)
        {
            if (!double.IsFinite(viewport.Width) || viewport.Width <= 0 || viewport.Width > MaximumCaptureDimension)
                throw new ArgumentOutOfRangeException(nameof(imageViewport), "Image viewport width must be finite, positive and within the capture side limit.");
            if (!double.IsFinite(viewport.Height) || viewport.Height <= 0 || viewport.Height > MaximumCaptureDimension)
                throw new ArgumentOutOfRangeException(nameof(imageViewport), "Image viewport height must be finite, positive and within the capture side limit.");
        }
        string svgBase64 = Convert.ToBase64String(svg.Span);
        string pageBackground = background == CaptureBackground.White ? "#ffffff" : background == CaptureBackground.Black ? "#000000" : "transparent";
        string pageStyle = "<style>html,body{margin:0;overflow:hidden;background:" + pageBackground + "}</style>";
        string imagePrefix = "<img id=svg src='data:image/svg+xml;base64,";
        string imageStyle = imageViewport is {} image
            ? "display:block;width:" + image.Width.ToString("R", CultureInfo.InvariantCulture) + "px;height:" + image.Height.ToString("R", CultureInfo.InvariantCulture) + "px"
            : "display:block";
        string imageSuffix = "' style='" + imageStyle + "'>";
        int htmlBytes = Encoding.UTF8.GetByteCount(pageStyle + imagePrefix + imageSuffix) + svgBase64.Length;
        // Chromium caps navigation URLs at 2 MiB. Keep the established small-input
        // image recipe; large inputs transport the same SVG bytes in a Blob inside
        // the same image element. Commands retain the existing 16 MiB bound.
        bool useBlob = "data:text/html;base64,".Length + 4L * ((htmlBytes + 2) / 3) > MaximumNavigationUrlCharacters;
        string html = pageStyle + (useBlob ? "<img id=svg style='" + imageStyle + "'>" : imagePrefix + svgBase64 + imageSuffix);
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
                string expression = useBlob
                    ? "(()=>{const bytes=Uint8Array.from(atob(" + JsonSerializer.Serialize(svgBase64) + "),c=>c.charCodeAt(0));const url=URL.createObjectURL(new Blob([bytes],{type:'image/svg+xml'}));const image=document.getElementById('svg');image.src=url;return image.decode().then(()=>new Promise(requestAnimationFrame)).finally(()=>URL.revokeObjectURL(url));})()"
                    : "document.getElementById('svg').decode().then(()=>new Promise(requestAnimationFrame))";
                await page.SendAsync("Runtime.evaluate", JsonSerializer.Serialize(new { expression, awaitPromise = true, returnByValue = true }), cancellationToken);
                using JsonDocument capture = JsonDocument.Parse(await page.SendAsync("Page.captureScreenshot", JsonSerializer.Serialize(new { format = "png", fromSurface = true, captureBeyondViewport = false, clip = new { x = 0, y = 0, width, height, scale = 1 } }), cancellationToken));
                screenshot = Convert.FromBase64String(capture.RootElement.GetProperty("result").GetProperty("data").GetString() ?? throw new InvalidDataException("Chrome returned no screenshot bytes."));
            }
            await browser.SendAsync("Browser.close", "{}", cancellationToken);
        }
        // Drain diagnostics through shutdown before a PNG can escape a failed sandbox launch.
        await chrome.DisposeAsync();
        chrome.ThrowIfUnhealthy();
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
