using System.Security.Cryptography;
using TextEncoding = System.Text.Encoding;
using System.Text.Json;
using Lokad.Svg.VisualDiff;

namespace Lokad.Svg.VisualTests;

[Trait("Layer", "Rendering")]
public sealed class SvgRootImageBoundaryReferenceTests
{
    [Theory]
    [InlineData("overflow")]
    [InlineData("group")]
    public void Root_image_edges_match_independent_native_canvas_references(string id)
    {
        string folder = Path.Combine(ResearchCorpus.Root, "svgweb");
        using JsonDocument data = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(folder, "root-image-boundary.json")));
        JsonElement row = data.RootElement.GetProperty("cases").EnumerateArray()
            .Single(item => item.GetProperty("id").GetString() == id);
        byte[] source = TextEncoding.UTF8.GetBytes(row.GetProperty("source").GetString() ?? throw new InvalidDataException());
        byte[] target = File.ReadAllBytes(Path.Combine(folder,
            row.GetProperty("nativeTarget").GetString() ?? throw new InvalidDataException()));
        Assert.Equal(row.GetProperty("sourceSha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(source)));
        Assert.Equal(row.GetProperty("nativeTargetSha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(target)));
        string host = OperatingSystem.IsWindows() ? "windows" : "linux";
        JsonElement captures = row.GetProperty("hosts").GetProperty(host).GetProperty("captures");
        Assert.Equal(3, captures.GetArrayLength());
        foreach (JsonElement capture in captures.EnumerateArray())
        {
            Assert.Equal(row.GetProperty("nativeTargetSha256").GetString(), capture.GetProperty("pngSha256").GetString());
            Assert.Equal("healthy", capture.GetProperty("startupHealth").GetString());
            Assert.True(capture.GetProperty("profileCleaned").GetBoolean());
        }
        byte[] actual = SvgConverter.ConvertToPng(source,
            new(null, 1.5, SvgColor.Transparent, SvgRenderLimits.Default), CancellationToken.None).ToArray();
        Assert.True(PixelComparer.Compare(PngRgbaDecoder.Decode(target, 1024), PngRgbaDecoder.Decode(actual, 1024)).IsExact, id);
    }
}
