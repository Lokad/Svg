using System.Diagnostics;

namespace Lokad.Svg.Chrome;

/// <summary>Owns one headless Chrome process, bounded diagnostics, its profile and native scratch directory.</summary>
internal sealed class ChromeProcess : IAsyncDisposable
{
    private const int CleanupRetryCount = 50;
    private static readonly TimeSpan CleanupRetryDelay = TimeSpan.FromMilliseconds(100);
    private readonly Process _process;
    private readonly string _profileDirectory;
    private readonly string _temporaryDirectory;
    private readonly string _executableName;
    private readonly ChromeDiagnostics _diagnostics = new();
    private readonly CancellationTokenSource _drainCancellation = new();
    private readonly Task _stderrDrain;
    private readonly Task _stdoutDrain;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private long _debuggerReadyMilliseconds = -1;
    private bool _disposed;

    private ChromeProcess(Process process, string profileDirectory, string temporaryDirectory)
    {
        _process = process;
        _profileDirectory = profileDirectory;
        _temporaryDirectory = temporaryDirectory;
        _executableName = Path.GetFileName(process.StartInfo.FileName);
        _stderrDrain = _diagnostics.DrainAsync(process.StandardError, _drainCancellation.Token);
        _stdoutDrain = DrainOutputAsync(process.StandardOutput, _drainCancellation.Token);

        static async Task DrainOutputAsync(TextReader reader, CancellationToken token)
        {
            char[] buffer = new char[4096];
            while (await reader.ReadAsync(buffer.AsMemory(), token) != 0) { }
        }
    }

    internal string DebuggerAddress { get; private set; } = "";

    internal static Task<ChromeProcess> LaunchAsync(string executable, CancellationToken cancellationToken)
        => LaunchAsync(executable, Path.GetTempPath(), cancellationToken);

    /// <summary>Creates and owns one launch directory containing the profile and native scratch below the borrowed parent.</summary>
    /// <remarks>
    /// The caller retains the parent and its other children. Failure and disposal remove
    /// only this launch's directory; cancellation before launch creates no directory.
    /// Independent launches may share a parent concurrently.
    /// </remarks>
    internal static async Task<ChromeProcess> LaunchAsync(string executable, string profileParent, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executable);
        ArgumentException.ThrowIfNullOrWhiteSpace(profileParent);
        cancellationToken.ThrowIfCancellationRequested();
        if (!File.Exists(executable)) throw new FileNotFoundException("Pinned Chrome executable was not found.", executable);
        string parent = Path.TrimEndingDirectorySeparator(Path.GetFullPath(profileParent));
        // Keep the leaf short: Chromium puts its Linux socket below TMPDIR.
        string temporary = Path.GetFullPath(Path.Combine(parent, "b-" + Path.GetRandomFileName()));
        if (!string.Equals(Path.GetDirectoryName(temporary), parent, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new InvalidOperationException("Chrome launch directory must stay below its borrowed parent.");
        string profile = Path.Combine(temporary, "p");
        Directory.CreateDirectory(profile);
        ProcessStartInfo startInfo = new(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        };
        foreach (string name in new[] { "TMPDIR", "TEMP", "TMP" }) startInfo.Environment[name] = temporary;
        startInfo.ArgumentList.Add("--headless=new");
        startInfo.ArgumentList.Add("--disable-gpu");
        startInfo.ArgumentList.Add("--remote-debugging-address=127.0.0.1");
        startInfo.ArgumentList.Add("--remote-debugging-port=0");
        startInfo.ArgumentList.Add("--no-first-run");
        startInfo.ArgumentList.Add("--no-default-browser-check");
        startInfo.ArgumentList.Add("--user-data-dir=" + profile);
        Process process;
        try { process = Process.Start(startInfo) ?? throw new InvalidOperationException("Chrome process could not be started."); }
        catch { await DeleteLaunchDirectoryAsync(temporary); throw; }
        ChromeProcess chrome = new(process, profile, temporary);
        try
        {
            using CancellationTokenSource startup = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            startup.CancelAfter(TimeSpan.FromSeconds(30));
            chrome.DebuggerAddress = await chrome.WaitForDebuggerAddressAsync(startup.Token);
            chrome._debuggerReadyMilliseconds = chrome._clock.ElapsedMilliseconds;
            return chrome;
        }
        catch
        {
            await chrome.DisposeAsync();
            cancellationToken.ThrowIfCancellationRequested();
            throw;
        }
    }

    internal void ThrowIfUnhealthy() => _diagnostics.ThrowIfUnhealthy();

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        bool profileCleaned = false;
        try
        {
            if (!_process.HasExited)
            {
                // Native exit and redirected-pipe completion have separate owners.
                // An asynchronous exit callback can remain pending after HasExited;
                // do not mistake that callback for a live process or skip draining.
                if (!await WaitForProcessExitAsync(TimeSpan.FromMilliseconds(500)))
                {
                    try { _process.Kill(true); }
                    catch (InvalidOperationException) when (_process.HasExited) { }
                }
                if (!await WaitForProcessExitAsync(TimeSpan.FromSeconds(5)))
                    throw new TimeoutException("Chrome did not exit after termination.");
            }
            // Descendants must not keep inherited pipe handles open indefinitely.
            try { await Task.WhenAll(_stderrDrain, _stdoutDrain).WaitAsync(TimeSpan.FromSeconds(5)); }
            catch (TimeoutException)
            {
                _drainCancellation.Cancel();
                throw new IOException("Chrome diagnostic pipes did not close after process shutdown.");
            }
        }
        finally
        {
            _drainCancellation.Cancel();
            _process.Dispose();
            try
            {
                await DeleteLaunchDirectoryAsync(_temporaryDirectory);
                profileCleaned = true;
            }
            finally
            {
                _drainCancellation.Dispose();
                Console.Error.WriteLine(_diagnostics.CreateReport(_executableName, _clock.ElapsedMilliseconds, _debuggerReadyMilliseconds, profileCleaned));
            }
        }

        async Task<bool> WaitForProcessExitAsync(TimeSpan timeout)
        {
            Stopwatch wait = Stopwatch.StartNew();
            while (!_process.HasExited)
            {
                if (wait.Elapsed >= timeout) return false;
                await Task.Delay(25);
            }
            return true;
        }
    }

    private static async Task DeleteLaunchDirectoryAsync(string directory)
    {
        for (int attempt = 0; ; attempt++)
        {
            try { Directory.Delete(directory, true); return; }
            catch (UnauthorizedAccessException) when (attempt < CleanupRetryCount) { await Task.Delay(CleanupRetryDelay); }
            catch (IOException) when (attempt < CleanupRetryCount) { await Task.Delay(CleanupRetryDelay); }
        }
    }

    private async Task<string> WaitForDebuggerAddressAsync(CancellationToken cancellationToken)
    {
        string activePort = Path.Combine(_profileDirectory, "DevToolsActivePort");
        while (true)
        {
            _diagnostics.ThrowIfUnhealthy();
            if (_process.HasExited) throw new InvalidOperationException("Chrome exited before DevTools became available; inspect the chromeLaunch stderr report.");
            string? address = await TryReadDebuggerAddressAsync(activePort, cancellationToken);
            if (address is not null) return address;
            await Task.Delay(25, cancellationToken);
        }
    }

    /// <summary>Reads bounded port-file bytes, treating concurrent creation/partial writes as not ready.</summary>
    internal static async Task<string?> TryReadDebuggerAddressAsync(string activePort, CancellationToken cancellationToken)
    {
        try
        {
            using FileStream file = new(activePort, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 256, true);
            byte[] bytes = new byte[256];
            int length = await file.ReadAsync(bytes, cancellationToken);
            string text = System.Text.Encoding.ASCII.GetString(bytes, 0, length);
            int newline = text.IndexOf('\n');
            if (newline < 0 || !text[(newline + 1)..].StartsWith("/devtools/browser/", StringComparison.Ordinal)) return null;
            if (!int.TryParse(text[..newline].Trim(), out int port) || port is <= 0 or > 65535) throw new InvalidDataException("Chrome emitted an invalid debugging port.");
            return "127.0.0.1:" + port;
        }
        catch (IOException error) when ((error.HResult & 0xffff) is 2 or 3 or 32 or 33) { return null; }
    }
}
