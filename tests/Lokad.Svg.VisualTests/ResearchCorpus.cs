namespace Lokad.Svg.VisualTests;

/// <summary>Reads the committed corpus without copying it into each test output.</summary>
internal static class ResearchCorpus
{
    internal static string Root { get; } = Resolve(AppContext.BaseDirectory,
        Environment.GetEnvironmentVariable("LOKAD_SVG_RESEARCH_ROOT"));

    internal static string Resolve(string testDirectory, string? configuredRoot)
    {
        if (!string.IsNullOrEmpty(configuredRoot)) return Validate(configuredRoot);

        // Prefer the checkout to old copies left by an earlier ordinary build.
        for (DirectoryInfo? directory = new(testDirectory); directory is not null; directory = directory.Parent)
        {
            string candidate = Path.Combine(directory.FullName, "tests", "Research");
            if (File.Exists(Path.Combine(candidate, "manifest.json"))) return candidate;
        }

        // An explicit export contains the complete corpus beside the binaries.
        return Validate(Path.Combine(testDirectory, "Research"));

        static string Validate(string directory)
        {
            string fullPath = Path.GetFullPath(directory);
            if (!File.Exists(Path.Combine(fullPath, "manifest.json")))
                throw new DirectoryNotFoundException("Research corpus manifest not found: " + fullPath
                    + ". Run from a checkout, export the visual suite, or set LOKAD_SVG_RESEARCH_ROOT.");
            return fullPath;
        }
    }
}
