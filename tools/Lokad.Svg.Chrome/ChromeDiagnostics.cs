using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Lokad.Svg.Chrome;

/// <summary>Drains child stderr with bounded retention and detects startup failures even after truncation.</summary>
/// <remarks>One asynchronous writer owns ingestion; readers take synchronized snapshots. No child bytes go to PNG stdout.</remarks>
internal sealed partial class ChromeDiagnostics
{
    internal const int MaximumRetainedCharacters = 32 * 1024;
    private const string SandboxFailure = "Sandbox cannot access executable";
    private const string NetworkCrash = "Network service crashed or was terminated";
    private readonly object _gate = new();
    private readonly StringBuilder _retained = new();
    private string _tail = "";
    private int _sandboxFailures;
    private int _networkCrashes;
    private bool _truncated;

    internal async Task DrainAsync(TextReader reader, CancellationToken cancellationToken)
    {
        char[] buffer = new char[4096];
        while (true)
        {
            int read = await reader.ReadAsync(buffer.AsMemory(), cancellationToken);
            if (read == 0) return;
            Append(buffer.AsSpan(0, read));
        }
    }

    internal void Append(ReadOnlySpan<char> value)
    {
        lock (_gate)
        {
            string scan = string.Concat(_tail, value);
            _sandboxFailures += CountNew(scan, _tail.Length, SandboxFailure);
            _networkCrashes += CountNew(scan, _tail.Length, NetworkCrash);
            int retain = Math.Min(value.Length, MaximumRetainedCharacters - _retained.Length);
            _retained.Append(value[..retain]);
            _truncated |= retain != value.Length;
            // Preserve partial phrases between reads, without counting a complete phrase twice.
            int tailLength = Math.Min(scan.Length, Math.Max(SandboxFailure.Length, NetworkCrash.Length) - 1);
            _tail = scan[^tailLength..];
        }

        static int CountNew(string text, int previousLength, string phrase)
        {
            int count = 0;
            for (int position = 0; (position = text.IndexOf(phrase, position, StringComparison.OrdinalIgnoreCase)) >= 0; position += phrase.Length)
                if (position + phrase.Length > previousLength) count++;
            return count;
        }
    }

    internal void ThrowIfUnhealthy()
    {
        lock (_gate)
        {
            if (_sandboxFailures > 0 || _networkCrashes >= 2)
                throw new InvalidOperationException("Chrome sandbox startup failed or its network service repeatedly crashed. Re-run tools/setup-chrome.ps1 for the pinned installation and inspect the bounded chromeLaunch stderr report; do not disable the sandbox.");
        }
    }

    internal string CreateReport(string executableName, long elapsedMilliseconds, long debuggerReadyMilliseconds, bool profileCleaned)
    {
        lock (_gate)
        {
            return JsonSerializer.Serialize(new
            {
                chromeLaunch = new
                {
                    schemaVersion = 1,
                    executableName,
                    elapsedMilliseconds,
                    debuggerReadyMilliseconds,
                    sandboxAccessFailures = _sandboxFailures,
                    networkServiceCrashes = _networkCrashes,
                    stderrTruncated = _truncated,
                    stderr = Redact(_retained.ToString()),
                    profileCleaned,
                    startupHealth = debuggerReadyMilliseconds >= 0 && _sandboxFailures == 0 && _networkCrashes < 2 && profileCleaned ? "healthy" : "failed"
                }
            });
        }
    }

    internal static string Redact(string value)
    {
        // Drop data/remote URLs, which can carry an SVG or credentials, before retaining diagnostics.
        value = SensitiveUrl().Replace(value, "[redacted URL]");
        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string temporary = Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar);
        if (temporary.Length > 0) value = value.Replace(temporary, "%TEMP%", StringComparison.OrdinalIgnoreCase);
        if (profile.Length > 0) value = value.Replace(profile, "%USERPROFILE%", StringComparison.OrdinalIgnoreCase);
        return value;
    }

    [GeneratedRegex(@"(?:data:|https?://|file://|wss?://|ftp://)[^\r\n]*", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SensitiveUrl();
}
