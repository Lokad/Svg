namespace Lokad.Svg.VisualTests;

[Trait("Layer", "Integrity")]
public sealed class ResearchCorpusTests
{
    [Theory]
    [InlineData("configured")]
    [InlineData("checkout")]
    [InlineData("bundle")]
    [InlineData("bad-configuration")]
    [InlineData("missing")]
    public void Corpus_selection_preserves_explicit_roots_checkout_freshness_and_standalone_exports(string scenario)
    {
        string temporaryParent = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath()));
        string temporary = Path.Combine(temporaryParent, "lokad-svg-corpus-" + Guid.NewGuid().ToString("N"));
        Assert.Equal(temporaryParent, Path.GetDirectoryName(temporary));
        Directory.CreateDirectory(temporary);
        try
        {
            string output = Path.Combine(temporary, "bin", "Release");
            Directory.CreateDirectory(output);
            string checkout = Path.Combine(temporary, "tests", "Research");
            string bundled = Path.Combine(output, "Research");
            string configured = Path.Combine(temporary, "selected");
            if (scenario is "configured" or "checkout" or "bad-configuration") CreateManifest(checkout);
            if (scenario != "missing") CreateManifest(bundled);
            if (scenario == "configured") CreateManifest(configured);

            string? selected = scenario is "configured" or "bad-configuration" ? configured : null;
            if (scenario is "missing" or "bad-configuration")
            {
                DirectoryNotFoundException error = Assert.Throws<DirectoryNotFoundException>(() => ResearchCorpus.Resolve(output, selected));
                Assert.Contains("LOKAD_SVG_RESEARCH_ROOT", error.Message);
            }
            else
            {
                string expected = scenario switch { "configured" => configured, "checkout" => checkout, _ => bundled };
                Assert.Equal(expected, ResearchCorpus.Resolve(output, selected));
            }
        }
        finally { Directory.Delete(temporary, true); }

        static void CreateManifest(string directory)
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "manifest.json"), "{}");
        }
    }
}
