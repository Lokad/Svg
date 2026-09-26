using System.Text.Json;
using Lokad.Svg.Chrome;

namespace Lokad.Svg.VisualTests;

public sealed class ChromeDiagnosticsTests
{
    [Fact]
    public async Task Benign_stderr_is_drained_and_does_not_fail_startup()
    {
        ChromeDiagnostics diagnostic = new();
        await diagnostic.DrainAsync(new StringReader("DevTools listening\nbenign warning\n"), CancellationToken.None);
        diagnostic.ThrowIfUnhealthy();
        using JsonDocument report = JsonDocument.Parse(diagnostic.CreateReport("chrome.exe", 20, 10, true));
        Assert.Equal("healthy", report.RootElement.GetProperty("chromeLaunch").GetProperty("startupHealth").GetString());
    }

    [Fact]
    public void Split_sandbox_error_is_detected_after_retention_is_full()
    {
        ChromeDiagnostics diagnostic = new();
        diagnostic.Append(new string('x', ChromeDiagnostics.MaximumRetainedCharacters + 100));
        diagnostic.Append("Sandbox cannot access exec");
        diagnostic.Append("utable");
        Assert.Throws<InvalidOperationException>(diagnostic.ThrowIfUnhealthy);
        using JsonDocument report = JsonDocument.Parse(diagnostic.CreateReport("chrome.exe", 20, -1, true));
        JsonElement launch = report.RootElement.GetProperty("chromeLaunch");
        Assert.Equal(1, launch.GetProperty("sandboxAccessFailures").GetInt32());
        Assert.True(launch.GetProperty("stderrTruncated").GetBoolean());
        Assert.Equal(ChromeDiagnostics.MaximumRetainedCharacters, launch.GetProperty("stderr").GetString()?.Length);
        Assert.Equal("failed", launch.GetProperty("startupHealth").GetString());
    }

    [Fact]
    public void Single_network_restart_is_recorded_but_repeated_restarts_fail()
    {
        ChromeDiagnostics diagnostic = new();
        diagnostic.Append("Network service crashed or was terminated, restarting service.\n");
        diagnostic.Append("benign\n");
        diagnostic.ThrowIfUnhealthy();
        diagnostic.Append("Network service crashed or was terminated");
        Assert.Throws<InvalidOperationException>(diagnostic.ThrowIfUnhealthy);
        using JsonDocument report = JsonDocument.Parse(diagnostic.CreateReport("chrome.exe", 20, 10, true));
        Assert.Equal(2, report.RootElement.GetProperty("chromeLaunch").GetProperty("networkServiceCrashes").GetInt32());
    }

    [Fact]
    public void Diagnostic_redacts_user_paths_and_input_urls()
    {
        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string redacted = ChromeDiagnostics.Redact(profile + "/private/chrome.exe\ndata:image/svg+xml;base64,SECRET\nhttps://private.invalid/?token=SECRET\n");
        Assert.DoesNotContain("SECRET", redacted);
        if (profile.Length > 0) Assert.DoesNotContain(profile, redacted, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Diagnostic_drain_observes_cancellation()
    {
        using CancellationTokenSource cancelled = new();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new ChromeDiagnostics().DrainAsync(new StringReader("warning"), cancelled.Token));
    }

    [Fact]
    public async Task Port_file_creation_and_partial_writes_are_retried()
    {
        string path = Path.Combine(Path.GetTempPath(), "lokad-svg-port-" + Guid.NewGuid().ToString("N"));
        try
        {
            Assert.Null(await ChromeProcess.TryReadDebuggerAddressAsync(path, CancellationToken.None));
            await File.WriteAllTextAsync(path, "12345");
            Assert.Null(await ChromeProcess.TryReadDebuggerAddressAsync(path, CancellationToken.None));
            await File.WriteAllTextAsync(path, "12345\n/devtools/browser/test");
            Assert.Equal("127.0.0.1:12345", await ChromeProcess.TryReadDebuggerAddressAsync(path, CancellationToken.None));
            if (OperatingSystem.IsWindows())
            {
                using FileStream locked = new(path, FileMode.Open, FileAccess.Read, FileShare.None);
                Assert.Null(await ChromeProcess.TryReadDebuggerAddressAsync(path, CancellationToken.None));
            }
            await File.WriteAllTextAsync(path, "70000\n/devtools/browser/test");
            await Assert.ThrowsAsync<InvalidDataException>(() => ChromeProcess.TryReadDebuggerAddressAsync(path, CancellationToken.None));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public async Task Precancelled_launch_does_not_start_a_process()
    {
        using CancellationTokenSource cancelled = new();
        cancelled.Cancel();
        OperationCanceledException error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ChromeProcess.LaunchAsync("not-created", cancelled.Token));
        Assert.Equal(cancelled.Token, error.CancellationToken);
    }
}
