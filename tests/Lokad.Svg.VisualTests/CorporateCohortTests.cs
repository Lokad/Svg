using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace Lokad.Svg.VisualTests;

public sealed class CorporateCohortTests
{
    [Fact]
    public void Frozen_membership_retains_every_document_background_size_and_transport_gap()
    {
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllBytes(Resolve("tests/References/inventory/corporate-cohort-v1.json")));
        JsonElement cohort = manifest.RootElement;
        Assert.True(cohort.GetProperty("selectedBeforeFreshCandidateComparisons").GetBoolean());
        Assert.Equal("inv-027", cohort.GetProperty("curvedMarkTarget").GetString());
        Assert.Equal(27, cohort.GetProperty("documentCount").GetInt32());
        Assert.Equal(258, cohort.GetProperty("variantCount").GetInt32());
        int[] expectedIds = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 19, 20, 26, 27, 40, 66, 92, 95, 96, 97, 98, 99, 100, 101, 102];
        JsonElement[] documents = cohort.GetProperty("documents").EnumerateArray().ToArray();
        Assert.Equal(expectedIds.Select(number => "inv-" + number.ToString("D3")), documents.Select(document => document.GetProperty("id").GetString()));
        Assert.Equal(8, documents.Count(document => document.GetProperty("role").GetString() == "holdout"));
        Assert.Equal(13, documents.Count(document => document.GetProperty("stratum").GetString() == "corporate-mark"));
        Assert.Equal(14, documents.Count(document => document.GetProperty("stratum").GetString() == "outlined-wordmark"));
        Assert.Equal(22, cohort.GetProperty("requiredExactDocuments").GetInt32());
        foreach (JsonElement stratum in cohort.GetProperty("strata").EnumerateArray())
        {
            int count = documents.Count(document => document.GetProperty("stratum").GetString() == stratum.GetProperty("id").GetString());
            Assert.Equal(count, stratum.GetProperty("documentCount").GetInt32());
            Assert.Equal((int)Math.Ceiling(count * 0.8), stratum.GetProperty("requiredExactDocuments").GetInt32());
        }
        HashSet<string> ids = new(StringComparer.Ordinal);
        int variants = 0;
        int gaps = 0;
        foreach (JsonElement document in documents)
        {
            byte[] input = File.ReadAllBytes(Resolve(Required(document, "sourcePath")));
            Assert.Equal(Required(document, "inputSha256"), Hash(input));
            JsonElement[] cases = document.GetProperty("variants").EnumerateArray().ToArray();
            foreach (string size in new[] { "small", "intended", "large" })
                Assert.Equal(new[] { "black", "transparent", "white" }, cases.Where(item => Required(item, "size") == size).Select(item => Required(item, "background")).Order(StringComparer.Ordinal));
            foreach (JsonElement item in cases)
            {
                Assert.True(ids.Add(Required(item, "id")));
                Assert.Equal(1, item.GetProperty("outputScale").GetDouble());
                int width = item.GetProperty("width").GetInt32();
                int height = item.GetProperty("height").GetInt32();
                Assert.True(width > 0 && height > 0);
                Assert.Equal(width, item.GetProperty("logicalViewport").GetProperty("width").GetInt32());
                Assert.Equal(height, item.GetProperty("logicalViewport").GetProperty("height").GetInt32());
                Assert.Equal(Required(item, "renderInputSha256"), Hash(File.ReadAllBytes(Resolve(Required(item, "renderInput")))));
                Assert.Equal(Required(item, "oracleInputSha256"), Hash(File.ReadAllBytes(Resolve(Required(item, "oracleInput")))));
                bool admitted = item.GetProperty("oracleTransportAdmitted").GetBoolean();
                Assert.Equal((long)width * height <= 2_000_000, admitted);
                if (!admitted) gaps++;
                // No render result is a membership filter. Measurement belongs in a separate record.
                Assert.False(item.TryGetProperty("exact", out _));
                variants++;
            }
        }
        Assert.Equal(258, variants);
        Assert.Equal(15, gaps);
        JsonElement party = documents.Single(document => Required(document, "id") == "inv-026");
        Assert.All(party.GetProperty("variants").EnumerateArray().Where(item => Required(item, "size") == "large"), item =>
        {
            Assert.Equal(4100, item.GetProperty("width").GetInt32());
            Assert.Equal(1120, item.GetProperty("height").GetInt32());
            Assert.False(item.GetProperty("oracleTransportAdmitted").GetBoolean());
        });
    }

    [Fact]
    public void Materializations_preserve_all_geometry_and_only_change_declared_root_sizing()
    {
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllBytes(Resolve("tests/References/inventory/corporate-cohort-v1.json")));
        Regex startTag = new("<svg\\b(?:[^>\"']|\"[^\"]*\"|'[^']*')*>", RegexOptions.CultureInvariant);
        Regex sizing = new("(^|;)(\\s*(width|height)\\s*:\\s*)[^;]*", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        int addedViewBoxes = 0;
        foreach (JsonElement document in manifest.RootElement.GetProperty("documents").EnumerateArray())
        {
            string source = System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(Resolve(Required(document, "sourcePath"))));
            Match originalTag = startTag.Match(source);
            Assert.True(originalTag.Success);
            XElement originalRoot = Parse(source);
            foreach (JsonElement item in document.GetProperty("variants").EnumerateArray().Where(item => Required(item, "background") == "transparent"))
            {
                string materialized = System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(Resolve(Required(item, "oracleInput"))));
                Match changedTag = startTag.Match(materialized);
                Assert.True(changedTag.Success);
                Assert.Equal(source[..originalTag.Index], materialized[..changedTag.Index]);
                Assert.Equal(source[(originalTag.Index + originalTag.Length)..], materialized[(changedTag.Index + changedTag.Length)..]);
                XElement changedRoot = Parse(materialized);
                Dictionary<XName, string> originalAttributes = originalRoot.Attributes().Where(attribute => attribute.Name.LocalName is not ("width" or "height" or "style" or "viewBox")).ToDictionary(attribute => attribute.Name, attribute => attribute.Value);
                Dictionary<XName, string> changedAttributes = changedRoot.Attributes().Where(attribute => attribute.Name.LocalName is not ("width" or "height" or "style" or "viewBox")).ToDictionary(attribute => attribute.Name, attribute => attribute.Value);
                Assert.Equal(originalAttributes.OrderBy(pair => pair.Key.ToString()), changedAttributes.OrderBy(pair => pair.Key.ToString()));
                Assert.Equal(sizing.Replace(originalRoot.Attribute("style")?.Value ?? "", "$1$2"), sizing.Replace(changedRoot.Attribute("style")?.Value ?? "", "$1$2"));
                string? added = item.GetProperty("addedViewBox").GetString();
                if (added is null)
                {
                    Assert.Equal(originalRoot.Attribute("viewBox")?.Value, changedRoot.Attribute("viewBox")?.Value);
                    Assert.Equal(Required(document, "sourcePath"), Required(item, "renderInput"));
                }
                else
                {
                    Assert.Null(originalRoot.Attribute("viewBox"));
                    Assert.Equal(added, changedRoot.Attribute("viewBox")?.Value);
                    Assert.Equal(Required(item, "oracleInput"), Required(item, "renderInput"));
                    addedViewBoxes++;
                }
                if (Required(document, "id") == "inv-002") Assert.Equal(source, materialized);
                else
                {
                    Assert.Equal(item.GetProperty("width").GetInt32().ToString(), changedRoot.Attribute("width")?.Value);
                    Assert.Equal(item.GetProperty("height").GetInt32().ToString(), changedRoot.Attribute("height")?.Value);
                }
            }
        }
        Assert.Equal(14, addedViewBoxes);

        static XElement Parse(string text)
        {
            // Parse retained test data without resolving their preserved external DTDs.
            using StringReader input = new(text);
            using XmlReader reader = XmlReader.Create(input, new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null });
            return XElement.Load(reader);
        }
    }

    private static string Required(JsonElement item, string name) => item.GetProperty(name).GetString() ?? throw new InvalidDataException("Missing " + name);
    private static string Hash(ReadOnlySpan<byte> input) => Convert.ToHexStringLower(SHA256.HashData(input));
    private static string Resolve(string path) => Path.Combine(AppContext.BaseDirectory, path.StartsWith("tests/Fixtures/", StringComparison.Ordinal) ? path[6..] : path);
}
