using System.Security.Cryptography;
using System.Text.Json;

namespace Lokad.Svg.VisualDiff;

/// <summary>Measures every frozen corporate variant against verified fresh or banked references.</summary>
internal static class CorporateCohortMeasurement
{
    /// <remarks>Runs sequentially with Default renderer limits. Fresh references require three complete, healthy, byte-identical captures; acceptance never depends on candidate pixels. Inputs and reference identities are checked before use. The caller owns cancellation; files and render storage are scoped to each case. Only the explicitly supplied report directory receives candidate PNGs and reports.</remarks>
    internal static void Run(string repositoryDirectory, string captureDirectory, string reportDirectory, CancellationToken cancellationToken)
    {
        string repository = Path.GetFullPath(repositoryDirectory);
        string captures = Path.GetFullPath(captureDirectory);
        string output = Path.GetFullPath(reportDirectory);
        string references = Path.Combine(repository, "tests/References/inventory");
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(references, "corporate-cohort-v1.json")));
        using JsonDocument bank = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(references, "corporate-cohort-current.json")));
        using JsonDocument fresh = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(captures, "captures.json")));
        VerifyPin(manifest.RootElement.GetProperty("oraclePin"), bank.RootElement.GetProperty("oraclePin"));
        VerifyPin(manifest.RootElement.GetProperty("oraclePin"), fresh.RootElement.GetProperty("oraclePin"));
        if (Required(manifest.RootElement, "cohortId") != Required(fresh.RootElement, "cohortId")) throw new InvalidDataException("Capture cohort identity differs from the frozen manifest.");
        Dictionary<string, JsonElement> banked = bank.RootElement.GetProperty("results").EnumerateArray().ToDictionary(item => Required(item, "id"), StringComparer.Ordinal);
        Dictionary<string, JsonElement> captured = fresh.RootElement.GetProperty("results").EnumerateArray().ToDictionary(item => Required(item, "id"), StringComparer.Ordinal);
        HashSet<string> declared = manifest.RootElement.GetProperty("documents").EnumerateArray().SelectMany(document => document.GetProperty("variants").EnumerateArray()).Select(variant => Required(variant, "id")).ToHashSet(StringComparer.Ordinal);
        if (captured.Keys.Any(id => !declared.Contains(id))) throw new InvalidDataException("Capture report contains an undeclared variant.");
        Directory.CreateDirectory(output);
        List<object> reports = new();
        List<object> documents = new();
        foreach (JsonElement document in manifest.RootElement.GetProperty("documents").EnumerateArray())
        {
            int variants = 0, measured = 0, rendered = 0, rejected = 0, exact = 0, known = 0;
            foreach (JsonElement variant in document.GetProperty("variants").EnumerateArray())
            {
                cancellationToken.ThrowIfCancellationRequested();
                string id = Required(variant, "id");
                byte[] input = File.ReadAllBytes(Path.Combine(repository, Required(variant, "renderInput")));
                string inputHash = Hash(input);
                if (inputHash != Required(variant, "renderInputSha256")) throw new InvalidDataException("Frozen renderer input changed: " + id);
                int width = variant.GetProperty("width").GetInt32(), height = variant.GetProperty("height").GetInt32();
                byte[]? reference = null;
                JsonElement? provenance = null;
                string? captureFailure = null;
                if (captured.TryGetValue(id, out JsonElement capture))
                {
                    if (capture.GetProperty("failure").ValueKind != JsonValueKind.Null)
                        captureFailure = Required(capture, "failure");
                    else
                    {
                        reference = File.ReadAllBytes(Path.Combine(captures, id + "-r1.png"));
                        if (capture.GetProperty("width").GetInt32() != width || capture.GetProperty("height").GetInt32() != height || Required(capture, "background") != Required(variant, "background")) throw new InvalidDataException("Capture options differ from the frozen variant: " + id);
                        VerifyCaptures(variant, capture, Hash(reference));
                        for (int repeat = 2; repeat <= 3; repeat++)
                            if (!reference.AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(captures, id + "-r" + repeat + ".png")))) throw new InvalidDataException("Fresh capture PNGs differ: " + id);
                        provenance = capture;
                    }
                }
                if (reference is null && banked.TryGetValue(id, out JsonElement recorded) && Required(recorded, "oracleState") == "measured")
                {
                    reference = File.ReadAllBytes(Path.Combine(references, Required(recorded, "referencePng")));
                    if (Hash(reference) != Required(recorded, "referenceSha256")) throw new InvalidDataException("Banked reference changed: " + id);
                    VerifyCaptures(variant, recorded.GetProperty("captureProvenance"), Hash(reference));
                    provenance = recorded.GetProperty("captureProvenance");
                }
                RgbaImage? oracle = reference is null ? null : PngRgbaDecoder.Decode(reference, checked(width * height));
                if (oracle is not null && (oracle.Width != width || oracle.Height != height)) throw new InvalidDataException("Reference dimensions differ from the frozen variant: " + id);
                string outcome;
                string? message = null, candidateHash = null;
                CohortComparison? comparison = null;
                int? pngBytes = null;
                try
                {
                    SvgColor background = Required(variant, "background") switch
                    {
                        "transparent" => SvgColor.Transparent,
                        "white" => new(255, 255, 255, 255),
                        "black" => new(0, 0, 0, 255),
                        _ => throw new InvalidDataException("Unknown frozen background: " + id),
                    };
                    JsonElement viewport = variant.GetProperty("logicalViewport");
                    SvgRenderOptions options = new(new SvgViewport(viewport.GetProperty("width").GetDouble(), viewport.GetProperty("height").GetDouble()), variant.GetProperty("outputScale").GetDouble(), background, SvgRenderLimits.Default);
                    byte[] png = SvgConverter.ConvertToPng(input, options, cancellationToken).ToArray();
                    File.WriteAllBytes(Path.Combine(output, id + ".png"), png);
                    RgbaImage candidate = PngRgbaDecoder.Decode(png, checked(width * height));
                    if (candidate.Width != width || candidate.Height != height) throw new InvalidDataException("Candidate dimensions differ from the frozen variant: " + id);
                    candidateHash = Hash(png);
                    pngBytes = png.Length;
                    outcome = "renders";
                    if (oracle is not null) comparison = Compare(oracle, candidate, cancellationToken);
                }
                catch (SvgException error) { outcome = error.ErrorCode.ToString(); message = error.Message; }
                variants++;
                if (reference is not null) measured++;
                if (outcome == "renders") rendered++;
                else rejected++;
                if (comparison?.Diff.IsExact == true) exact++;
                else if (comparison is not null) known++;
                reports.Add(new { Id = id, Outcome = outcome, Message = message, InputSha256 = inputHash, CandidateSha256 = candidateHash, PngBytes = pngBytes, ReferenceSha256 = reference is null ? null : Hash(reference), Comparison = comparison, CaptureProvenance = provenance, CaptureFailure = captureFailure });
            }
            documents.Add(new { Id = Required(document, "id"), Stratum = Required(document, "stratum"), Role = Required(document, "role"), Variants = variants, References = measured, Renders = rendered, Rejections = rejected, ExactVariants = exact, KnownDifferences = known, VerifiedExact = variants > 0 && exact == variants });
        }
        File.WriteAllText(Path.Combine(output, "summary.json"), JsonSerializer.Serialize(new { CohortId = Required(manifest.RootElement, "cohortId"), OraclePin = manifest.RootElement.GetProperty("oraclePin"), Documents = documents, Reports = reports }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("Measured " + reports.Count + " frozen variants under " + output + ".");

        static void VerifyPin(JsonElement expected, JsonElement actual)
        {
            foreach (JsonProperty property in expected.EnumerateObject())
                if (!actual.TryGetProperty(property.Name, out JsonElement value) || !JsonElement.DeepEquals(value, property.Value)) throw new InvalidDataException("Capture pin differs from the frozen manifest: " + property.Name);
        }

        static void VerifyCaptures(JsonElement variant, JsonElement capture, string referenceHash)
        {
            if (Required(variant, "oracleInputSha256") != Required(capture, "oracleInputSha256")) throw new InvalidDataException("Capture input identity differs from the frozen variant.");
            JsonElement[] runs = capture.GetProperty("captures").EnumerateArray().ToArray();
            if (runs.Length != 3) throw new InvalidDataException("Reference requires three fresh captures.");
            foreach (JsonElement run in runs)
            {
                if (Required(run, "pngSha256") != referenceHash || Required(run, "startupHealth") != "healthy" || !run.GetProperty("profileCleaned").GetBoolean()) throw new InvalidDataException("Reference captures are unstable or unhealthy.");
                if (run.TryGetProperty("stderrTruncated", out JsonElement truncated) && truncated.GetBoolean()) throw new InvalidDataException("Reference capture diagnostics are truncated.");
            }
        }

        static CohortComparison Compare(RgbaImage reference, RgbaImage candidate, CancellationToken cancellationToken)
        {
            PixelComparison diff = PixelComparer.Compare(reference, candidate);
            long alphaMass = 0, over = 0, under = 0, opaqueLost = 0, opaqueAdded = 0;
            List<object> witnesses = new();
            for (int pixel = 0; pixel < reference.Width * reference.Height; pixel++)
            {
                if ((pixel & 4095) == 0) cancellationToken.ThrowIfCancellationRequested();
                int offset = pixel * 4;
                int delta = candidate.Pixels[offset + 3] - reference.Pixels[offset + 3];
                alphaMass += Math.Abs(delta);
                if (delta > 0) over++;
                else if (delta < 0) under++;
                if (reference.Pixels[offset + 3] == 255 && candidate.Pixels[offset + 3] == 0) opaqueLost++;
                if (reference.Pixels[offset + 3] == 0 && candidate.Pixels[offset + 3] == 255) opaqueAdded++;
                if (witnesses.Count < 16 && (delta != 0 || (candidate.Pixels[offset + 3] != 0 && !candidate.Pixels.AsSpan(offset, 3).SequenceEqual(reference.Pixels.AsSpan(offset, 3)))))
                    witnesses.Add(new { X = pixel % candidate.Width, Y = pixel / candidate.Width, Reference = reference.Pixels.AsSpan(offset, 4).ToArray(), Candidate = candidate.Pixels.AsSpan(offset, 4).ToArray() });
            }
            return new(diff, alphaMass, over, under, opaqueLost, opaqueAdded, witnesses);
        }

        static string Required(JsonElement item, string name) => item.GetProperty(name).GetString() ?? throw new InvalidDataException("Missing " + name + ".");
        static string Hash(ReadOnlySpan<byte> bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    }

    private sealed record CohortComparison(PixelComparison Diff, long AlphaMass, long OverCoveredPixels, long UnderCoveredPixels, long OpaqueLost, long OpaqueAdded, IReadOnlyList<object> Witnesses);
}
