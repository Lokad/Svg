using System.ComponentModel;
using Lokad.Svg.Chrome;

namespace Lokad.Svg.VisualTests;

public sealed class ChromeProcessTests : IDisposable
{
    private readonly string _profileParent = Path.Combine(Path.GetTempPath(), "lokad-svg-launch-test-" + Guid.NewGuid().ToString("N"));

    [Fact]
    [Trait("Layer", "Contract")]
    public async Task Failed_launch_cleans_its_profile()
    {
        string unrelated = Directory.CreateDirectory(Path.Combine(_profileParent, "lokad-svg-chrome-unrelated")).FullName;
        string marker = Path.Combine(unrelated, "keep.txt");
        File.WriteAllText(marker, "borrowed parent");
        string decoy = Path.Combine(_profileParent, "invalid-chrome.exe");
        File.WriteAllText(decoy, "not an executable");

        await Assert.ThrowsAsync<Win32Exception>(async () => await ChromeProcess.LaunchAsync(decoy, _profileParent, CancellationToken.None));

        Assert.Equal(unrelated, Assert.Single(Directory.GetDirectories(_profileParent)));
        Assert.Equal("borrowed parent", File.ReadAllText(marker));
    }

    [Fact]
    [Trait("Layer", "Contract")]
    public async Task Failed_native_startup_cleans_profile_and_native_scratch()
    {
        if (!OperatingSystem.IsLinux()) return;
        Directory.CreateDirectory(_profileParent);
        string executable = Path.Combine(_profileParent, "native-scratch-probe");
        File.WriteAllText(executable, """
            #!/bin/sh
            parent=$(dirname "$0")
            printf '%s' "${TMPDIR-}" > "$parent/observed-scratch.txt"
            case "${TMPDIR-}" in "$parent"/*) mkdir "$TMPDIR/org.chromium.Chromium.fixture" ;; esac
            exit 1
            """.Replace("\r\n", "\n", StringComparison.Ordinal) + "\n");
        File.SetUnixFileMode(executable, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await ChromeProcess.LaunchAsync(executable, _profileParent, CancellationToken.None));

        string scratch = File.ReadAllText(Path.Combine(_profileParent, "observed-scratch.txt"));
        Assert.StartsWith(_profileParent + Path.DirectorySeparatorChar, scratch, StringComparison.Ordinal);
        Assert.False(Directory.Exists(scratch));
        Assert.Empty(Directory.GetDirectories(_profileParent));
    }

    [Fact]
    [Trait("Layer", "Contract")]
    public async Task Missing_executable_creates_no_profile()
    {
        await Assert.ThrowsAsync<FileNotFoundException>(async () => await ChromeProcess.LaunchAsync(Path.Combine(_profileParent, "missing"), _profileParent, CancellationToken.None));

        Assert.False(Directory.Exists(_profileParent));
    }

    public void Dispose()
    {
        if (Directory.Exists(_profileParent)) Directory.Delete(_profileParent, true);
    }
}
