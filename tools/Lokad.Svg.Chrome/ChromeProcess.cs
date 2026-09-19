using System.Diagnostics;

namespace Lokad.Svg.Chrome;

/// <summary>Owns one headless Chrome process and its isolated debugging profile.</summary>
internal sealed class ChromeProcess : IAsyncDisposable
{
    private const int ProfileCleanupRetryCount = 50;
    private static readonly TimeSpan ProfileCleanupRetryDelay = TimeSpan.FromMilliseconds(100);
    private readonly Process _process;
    private readonly string _profileDirectory;

    private ChromeProcess(Process process, string profileDirectory, string debuggerAddress)
    {
        _process = process;
        _profileDirectory = profileDirectory;
        DebuggerAddress = debuggerAddress;
    }

    internal string DebuggerAddress { get; }

    internal static async Task<ChromeProcess> LaunchAsync(string executable, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executable);
        if (!File.Exists(executable)) throw new FileNotFoundException("Pinned Chrome executable was not found.", executable);
        string profile = Path.Combine(Path.GetTempPath(), "lokad-svg-chrome-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(profile);
        ProcessStartInfo startInfo = new(executable) { UseShellExecute = false, CreateNoWindow = true };
        startInfo.ArgumentList.Add("--headless=new");
        startInfo.ArgumentList.Add("--disable-gpu");
        startInfo.ArgumentList.Add("--remote-debugging-address=127.0.0.1");
        startInfo.ArgumentList.Add("--remote-debugging-port=0");
        startInfo.ArgumentList.Add("--no-first-run");
        startInfo.ArgumentList.Add("--no-default-browser-check");
        startInfo.ArgumentList.Add("--user-data-dir=" + profile);
        Process process = Process.Start(startInfo) ?? throw new InvalidOperationException("Chrome process could not be started.");
        try { return new ChromeProcess(process, profile, await WaitForDebuggerAddressAsync(profile, process, cancellationToken)); }
        catch { if (!process.HasExited) process.Kill(true); process.Dispose(); Directory.Delete(profile, true); throw; }
    }

    public async ValueTask DisposeAsync()
    {
        if (!_process.HasExited)
        {
            Task exit = _process.WaitForExitAsync();
            if (await Task.WhenAny(exit, Task.Delay(TimeSpan.FromMilliseconds(500))) != exit)
            {
                _process.Kill(true);
                await exit;
            }
        }
        _process.Dispose();
        await DeleteProfileAsync(_profileDirectory);
    }

    private static async Task DeleteProfileAsync(string profileDirectory)
    {
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                Directory.Delete(profileDirectory, true);
                return;
            }
            catch (UnauthorizedAccessException) when (attempt < ProfileCleanupRetryCount) { await Task.Delay(ProfileCleanupRetryDelay); }
            catch (IOException) when (attempt < ProfileCleanupRetryCount) { await Task.Delay(ProfileCleanupRetryDelay); }
        }
    }

    private static async Task<string> WaitForDebuggerAddressAsync(string profile, Process process, CancellationToken cancellationToken)
    {
        string activePort = Path.Combine(profile, "DevToolsActivePort");
        while (!File.Exists(activePort))
        {
            if (process.HasExited) throw new InvalidOperationException("Chrome exited before DevTools became available.");
            await Task.Delay(25, cancellationToken);
        }
        string[] lines = await File.ReadAllLinesAsync(activePort, cancellationToken);
        if (lines.Length < 1 || !int.TryParse(lines[0], out int port) || port is <= 0 or > 65535) throw new InvalidDataException("Chrome emitted an invalid debugging port.");
        return "127.0.0.1:" + port;
    }
}
