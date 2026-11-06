namespace Lokad.Svg.Benchmarks;

/// <summary>Locates committed benchmark inputs from ordinary or generated executables.</summary>
internal static class BenchmarkCorpus
{
    internal static string FindRepository()
    {
        foreach (string start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
            for (DirectoryInfo? candidate = new(start); candidate is not null; candidate = candidate.Parent)
                if (File.Exists(Path.Combine(candidate.FullName, "Lokad.Svg.slnx"))) return candidate.FullName;
        throw new DirectoryNotFoundException("Run asset-backed benchmarks from a Lokad.Svg checkout.");
    }
}
