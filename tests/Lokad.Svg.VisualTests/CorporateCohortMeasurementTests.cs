using System.Security.Cryptography;
using System.Text.Json;
using Lokad.Svg.VisualDiff;

namespace Lokad.Svg.VisualTests;

public sealed class CorporateCohortMeasurementTests
{
    [Fact]
    public void Fresh_exact_reference_is_accepted_independently_of_candidate_identity()
    {
        using MiniCohort cohort = new(Red, false);
        using JsonDocument report = cohort.Measure();
        JsonElement result = report.RootElement.GetProperty("Reports")[0];
        Assert.Equal("renders", result.GetProperty("Outcome").GetString());
        Assert.True(result.GetProperty("Comparison").GetProperty("Diff").GetProperty("IsExact").GetBoolean());
        Assert.NotEqual(result.GetProperty("ReferenceSha256").GetString(), result.GetProperty("CandidateSha256").GetString());
        Assert.True(report.RootElement.GetProperty("Documents")[0].GetProperty("VerifiedExact").GetBoolean());
    }

    [Fact]
    public void Known_difference_reports_pixels_and_directional_alpha_metrics()
    {
        using MiniCohort cohort = new(Red.Replace("red", "blue", StringComparison.Ordinal), false);
        using JsonDocument report = cohort.Measure();
        JsonElement comparison = report.RootElement.GetProperty("Reports")[0].GetProperty("Comparison");
        Assert.Equal(4, comparison.GetProperty("Diff").GetProperty("ChangedPixelCount").GetInt64());
        Assert.Equal(255, comparison.GetProperty("Diff").GetProperty("MaximumChannelError").GetInt32());
        Assert.Equal(0, comparison.GetProperty("AlphaMass").GetInt64());
        Assert.Equal(0, comparison.GetProperty("OpaqueLost").GetInt64());
        Assert.Equal(0, comparison.GetProperty("OpaqueAdded").GetInt64());
        Assert.Equal(4, comparison.GetProperty("Witnesses").GetArrayLength());
        Assert.False(report.RootElement.GetProperty("Documents")[0].GetProperty("VerifiedExact").GetBoolean());
    }

    [Fact]
    public void Actual_third_capture_must_match_even_when_metadata_claims_stability()
    {
        using MiniCohort cohort = new(Red, false);
        File.WriteAllBytes(Path.Combine(cohort.Captures, "mini-r3.png"), [1, 2, 3]);
        InvalidDataException error = Assert.Throws<InvalidDataException>(() => cohort.Measure());
        Assert.Contains("Fresh capture PNGs differ", error.Message);
        Assert.False(File.Exists(Path.Combine(cohort.Output, "summary.json")));
    }

    [Fact]
    public void Renderer_rejection_keeps_its_independent_reference()
    {
        using MiniCohort cohort = new("<svg xmlns='http://www.w3.org/2000/svg' width='2' height='2'><foreignObject/></svg>", false);
        using JsonDocument report = cohort.Measure();
        JsonElement result = report.RootElement.GetProperty("Reports")[0];
        Assert.Equal("UnsupportedFeature", result.GetProperty("Outcome").GetString());
        Assert.Equal(cohort.ReferenceHash, result.GetProperty("ReferenceSha256").GetString());
        Assert.Equal(JsonValueKind.Null, result.GetProperty("Comparison").ValueKind);
        Assert.Equal(JsonValueKind.Null, result.GetProperty("CandidateSha256").ValueKind);
    }

    [Fact]
    public void Missing_fresh_case_can_use_a_verified_banked_reference()
    {
        using MiniCohort cohort = new(Red, true);
        cohort.ClearFreshCaptures();
        using JsonDocument report = cohort.Measure();
        Assert.True(report.RootElement.GetProperty("Reports")[0].GetProperty("Comparison").GetProperty("Diff").GetProperty("IsExact").GetBoolean());
    }

    [Fact]
    public void Unmeasured_variants_remain_in_the_document_denominator()
    {
        using MiniCohort cohort = new(Red, false);
        cohort.ClearFreshCaptures();
        using JsonDocument report = cohort.Measure();
        Assert.Equal(1, report.RootElement.GetProperty("Reports").GetArrayLength());
        JsonElement document = report.RootElement.GetProperty("Documents")[0];
        Assert.Equal(1, document.GetProperty("Variants").GetInt32());
        Assert.Equal(0, document.GetProperty("References").GetInt32());
        Assert.False(document.GetProperty("VerifiedExact").GetBoolean());
    }

    [Fact]
    public void Capture_failure_does_not_replace_a_renderer_outcome()
    {
        using MiniCohort cohort = new(Red, false);
        cohort.MarkCaptureFailure();
        using JsonDocument report = cohort.Measure();
        JsonElement result = report.RootElement.GetProperty("Reports")[0];
        Assert.Equal("renders", result.GetProperty("Outcome").GetString());
        Assert.Equal("synthetic capture deadline", result.GetProperty("CaptureFailure").GetString());
        Assert.Equal(JsonValueKind.Null, result.GetProperty("Comparison").ValueKind);
    }

    [Fact]
    public void Linux_references_preserve_the_frozen_version_recipe_and_report_their_actual_pin()
    {
        using MiniCohort cohort = new(Red, true);
        cohort.UseLinuxPin();
        using JsonDocument report = cohort.Measure();
        Assert.Equal("linux64", report.RootElement.GetProperty("OraclePin").GetProperty("platform").GetString());
        Assert.True(report.RootElement.GetProperty("Reports")[0].GetProperty("Comparison").GetProperty("Diff").GetProperty("IsExact").GetBoolean());
    }

    [Theory]
    [InlineData("version")]
    [InlineData("chromiumRevision")]
    [InlineData("captureRecipeRevision")]
    [InlineData("executableSha256")]
    public void Linux_capture_cannot_change_the_version_recipe_or_pinned_executable(string property)
    {
        using MiniCohort cohort = new(Red, true);
        cohort.UseLinuxPin();
        cohort.ChangeCapturePin(property);
        InvalidDataException error = Assert.Throws<InvalidDataException>(() => cohort.Measure());
        Assert.Contains(property, error.Message);
        Assert.False(File.Exists(Path.Combine(cohort.Output, "summary.json")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Missing_or_failed_Linux_capture_cannot_use_a_Windows_reference(bool failed)
    {
        using MiniCohort cohort = new(Red, true);
        cohort.UseLinuxPin();
        if (failed) cohort.MarkCaptureFailure();
        else cohort.ClearFreshCaptures();
        using JsonDocument report = cohort.Measure();
        JsonElement result = report.RootElement.GetProperty("Reports")[0];
        Assert.Equal("renders", result.GetProperty("Outcome").GetString());
        Assert.Equal(JsonValueKind.Null, result.GetProperty("ReferenceSha256").ValueKind);
        Assert.Equal(JsonValueKind.Null, result.GetProperty("Comparison").ValueKind);
        Assert.Equal(1, report.RootElement.GetProperty("Documents")[0].GetProperty("Variants").GetInt32());
        Assert.Equal(0, report.RootElement.GetProperty("Documents")[0].GetProperty("References").GetInt32());
        Assert.False(report.RootElement.GetProperty("Documents")[0].GetProperty("VerifiedExact").GetBoolean());
    }

    [Fact]
    public void Cancellation_preserves_its_token_and_does_not_publish_a_report()
    {
        using MiniCohort cohort = new(Red, false);
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        OperationCanceledException error = Assert.Throws<OperationCanceledException>(() => CorporateCohortMeasurement.Run(cohort.Repository, cohort.Captures, cohort.Output, cancellation.Token));
        Assert.Equal(cancellation.Token, error.CancellationToken);
        Assert.False(File.Exists(Path.Combine(cohort.Output, "summary.json")));
    }

    private const string Red = "<svg xmlns='http://www.w3.org/2000/svg' width='2' height='2'><rect width='2' height='2' fill='red'/></svg>";

    private sealed class MiniCohort : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "lokad-svg-cohort-" + Guid.NewGuid().ToString("N"));
        private TestBrowserPin _capturePin = Pin("win64");
        internal string Repository => Path.Combine(_directory, "repository");
        internal string Captures => Path.Combine(_directory, "captures");
        internal string Output => Path.Combine(_directory, "output");
        internal string ReferenceHash { get; }

        internal MiniCohort(string svg, bool banked)
        {
            string inventory = Path.Combine(Repository, "tests/References/inventory");
            Directory.CreateDirectory(inventory);
            Directory.CreateDirectory(Captures);
            File.WriteAllText(Path.Combine(Repository, "input.svg"), svg);
            string inputHash = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(Repository, "input.svg"))));
            byte[] reference = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "tests/References/original-solid-red.png"));
            ReferenceHash = Convert.ToHexStringLower(SHA256.HashData(reference));
            TestBrowserPin pin = _capturePin;
            object provenance = new { oracleInputSha256 = inputHash, captures = Enumerable.Repeat(new { pngSha256 = ReferenceHash, startupHealth = "healthy", profileCleaned = true, stderrTruncated = false }, 3).ToArray() };
            object variant = new { id = "mini", width = 2, height = 2, background = "transparent", renderInput = "input.svg", renderInputSha256 = inputHash, oracleInputSha256 = inputHash, logicalViewport = new { width = 2, height = 2 }, outputScale = 1 };
            File.WriteAllText(Path.Combine(inventory, "corporate-cohort-v1.json"), JsonSerializer.Serialize(new { cohortId = "synthetic-test-cohort", oraclePin = pin, documents = new[] { new { id = "mini-document", stratum = "corporate-mark", role = "development", variants = new[] { variant } } } }));
            object[] results = banked ? [new { id = "mini", oracleState = "measured", referencePng = "mini.png", referenceSha256 = ReferenceHash, captureProvenance = provenance }] : [];
            File.WriteAllText(Path.Combine(inventory, "corporate-cohort-current.json"), JsonSerializer.Serialize(new { oraclePin = pin, results }));
            File.WriteAllBytes(Path.Combine(inventory, "mini.png"), reference);
            object[] runs = Enumerable.Repeat<object>(new { pngSha256 = ReferenceHash, startupHealth = "healthy", profileCleaned = true, stderrTruncated = false }, 3).ToArray();
            File.WriteAllText(Path.Combine(Captures, "captures.json"), JsonSerializer.Serialize(new { cohortId = "synthetic-test-cohort", oraclePin = pin, results = new[] { new { id = "mini", oracleInputSha256 = inputHash, width = 2, height = 2, background = "transparent", captures = runs, failure = (string?)null } } }));
            for (int repeat = 1; repeat <= 3; repeat++) File.WriteAllBytes(Path.Combine(Captures, "mini-r" + repeat + ".png"), reference);
        }

        internal void ClearFreshCaptures() => File.WriteAllText(Path.Combine(Captures, "captures.json"), JsonSerializer.Serialize(new { cohortId = "synthetic-test-cohort", oraclePin = _capturePin, results = Array.Empty<object>() }));

        internal void UseLinuxPin()
        {
            _capturePin = Pin("linux64");
            Directory.CreateDirectory(Path.Combine(Repository, "tools"));
            File.WriteAllText(Path.Combine(Repository, "tools/chrome-for-testing.linux64.json"), JsonSerializer.Serialize(_capturePin));
            string path = Path.Combine(Captures, "captures.json");
            using JsonDocument record = JsonDocument.Parse(File.ReadAllBytes(path));
            File.WriteAllText(path, JsonSerializer.Serialize(new { cohortId = "synthetic-test-cohort", oraclePin = _capturePin, results = record.RootElement.GetProperty("results") }));
        }

        internal void ChangeCapturePin(string property)
        {
            string path = Path.Combine(Captures, "captures.json");
            using JsonDocument record = JsonDocument.Parse(File.ReadAllBytes(path));
            Dictionary<string, JsonElement> pin = record.RootElement.GetProperty("oraclePin").EnumerateObject().ToDictionary(item => item.Name, item => item.Value.Clone(), StringComparer.Ordinal);
            pin[property] = JsonSerializer.SerializeToElement("changed");
            File.WriteAllText(path, JsonSerializer.Serialize(new { cohortId = "synthetic-test-cohort", oraclePin = pin, results = record.RootElement.GetProperty("results") }));
        }

        private static TestBrowserPin Pin(string platform) => new("synthetic-test-pin", "synthetic-test-revision",
            platform, "3", new string(platform == "linux64" ? 'b' : 'a', 64));

        private sealed record TestBrowserPin(string version, string chromiumRevision, string platform,
            string captureRecipeRevision, string executableSha256);

        internal void MarkCaptureFailure()
        {
            string path = Path.Combine(Captures, "captures.json");
            File.WriteAllText(path, File.ReadAllText(path).Replace("\"failure\":null", "\"failure\":\"synthetic capture deadline\"", StringComparison.Ordinal));
        }

        internal JsonDocument Measure()
        {
            CorporateCohortMeasurement.Run(Repository, Captures, Output, CancellationToken.None);
            return JsonDocument.Parse(File.ReadAllBytes(Path.Combine(Output, "summary.json")));
        }

        public void Dispose() => Directory.Delete(_directory, true);
    }
}
