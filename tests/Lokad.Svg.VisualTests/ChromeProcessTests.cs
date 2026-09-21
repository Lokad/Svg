using System.ComponentModel;
using Lokad.Svg.Chrome;

namespace Lokad.Svg.VisualTests;

public sealed class ChromeProcessTests
{
    [Fact]
    public async Task Failed_launch_cleans_its_profile()
    {
        HashSet<string> before = ProfileDirectories();
        string decoy = Path.GetTempFileName();
        try
        {
            File.WriteAllText(decoy, "not an executable");
            await Assert.ThrowsAsync<Win32Exception>(async () => await ChromeProcess.LaunchAsync(decoy, CancellationToken.None));
        }
        finally { File.Delete(decoy); }

        Assert.True(before.SetEquals(ProfileDirectories()));
    }

    [Fact]
    public async Task Missing_executable_creates_no_profile()
    {
        HashSet<string> before = ProfileDirectories();

        await Assert.ThrowsAsync<FileNotFoundException>(async () => await ChromeProcess.LaunchAsync(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")), CancellationToken.None));

        Assert.True(before.SetEquals(ProfileDirectories()));
    }

    private static HashSet<string> ProfileDirectories() => Directory.GetDirectories(Path.GetTempPath(), "lokad-svg-chrome-*").ToHashSet(StringComparer.Ordinal);
}