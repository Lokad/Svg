using System.Security.Cryptography;
using System.Text.Json;
using Lokad.Svg.VisualDiff;

namespace Lokad.Svg.VisualTests;

public sealed class ClipPairRouteReferenceTests
{
    [Fact]
    public void Every_predeclared_clip_route_retains_healthy_triples_and_complete_pixel_evidence()
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "tests", "References");
        using JsonDocument declaration = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(directory, "clip-pair-route-declaration.json")));
        Assert.True(declaration.RootElement.GetProperty("DeclaredBeforeImplementation").GetBoolean());
        JsonElement cases = declaration.RootElement.GetProperty("Cases");
        Assert.Equal(18, cases.GetArrayLength());
        foreach (JsonElement value in cases.EnumerateArray())
        {
            string id = value.GetProperty("Id").GetString() ?? throw new InvalidDataException("Missing control ID.");
            string source = value.GetProperty("Source").GetString() ?? throw new InvalidDataException("Missing control source.");
            byte[] input = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "original", source));
            Assert.Equal(value.GetProperty("InputSha256").GetString(), Hash(input));
            using JsonDocument metadata = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(directory, "original-" + id + ".json")));
            JsonElement record = metadata.RootElement;
            byte[] reference = File.ReadAllBytes(Path.Combine(directory, "original-" + id + ".png"));
            Assert.Equal(record.GetProperty("referenceSha256").GetString(), Hash(reference));
            Assert.Equal(3, record.GetProperty("captureSha256").GetArrayLength());
            Assert.All(record.GetProperty("captureSha256").EnumerateArray(), capture => Assert.Equal(Hash(reference), capture.GetString()));
            Assert.All(record.GetProperty("captureStartupHealth").EnumerateArray(), health => Assert.Equal("healthy", health.GetString()));
            Assert.All(record.GetProperty("captureProfilesCleaned").EnumerateArray(), cleaned => Assert.True(cleaned.GetBoolean()));
            SvgColor background = value.GetProperty("Background").GetString() switch { "transparent" => SvgColor.Transparent, "white" => new(255, 255, 255, 255), "black" => new(0, 0, 0, 255), _ => throw new InvalidDataException("Invalid control background.") };
            int width = value.GetProperty("Width").GetInt32();
            int height = value.GetProperty("Height").GetInt32();
            ReadOnlyMemory<byte> output = SvgConverter.ConvertToPng(input, new(new SvgViewport(width, height), 1, background, SvgRenderLimits.Default), CancellationToken.None);
            Assert.Equal(record.GetProperty("candidateSha256").GetString(), Hash(output.Span));
            PixelComparison comparison = PixelComparer.Compare(PngRgbaDecoder.Decode(reference, width * height), PngRgbaDecoder.Decode(output, width * height));
            Assert.Equal(record.GetProperty("comparison").GetProperty("exact").GetBoolean(), comparison.IsExact);
        }

        static string Hash(ReadOnlySpan<byte> bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    }
}
