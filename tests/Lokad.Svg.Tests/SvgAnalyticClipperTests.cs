using System.Text.Json;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgAnalyticClipperTests
{
    public static IEnumerable<object[]> NativeCases()
    {
        using JsonDocument source = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "analytic-clipped-pieces.json")));
        foreach (JsonElement item in source.RootElement.GetProperty("Cases").EnumerateArray())
        {
            string identity = item.GetProperty("Id").GetString() ?? throw new InvalidDataException("Missing native case identity.");
            yield return [identity, item.GetProperty("Kind").GetInt32(), item.GetProperty("Clip").EnumerateArray().Select(value => value.GetInt32()).ToArray(), item.GetProperty("Cull").GetBoolean(), item.GetProperty("Points").EnumerateArray().Select(value => value.GetSingle()).ToArray(), item.GetProperty("ExpectedPieces").EnumerateArray().Select(piece => piece.EnumerateArray().Select(value => value.GetUInt32()).ToArray()).ToArray()];
        }
    }

    [Theory]
    [MemberData(nameof(NativeCases))]
    public void Clipped_pieces_match_compiled_native_float_bits(string identity, int kind, int[] bounds, bool cull, float[] coordinates, uint[][] expected)
    {
        Assert.NotEmpty(identity);
        Span<SvgAnalyticClipPiece> pieces = stackalloc SvgAnalyticClipPiece[SvgAnalyticCoverage.Clipper.MaximumPieces];
        SvgAnalyticCoverage.Clipper clipper = new(new(bounds[0], bounds[1], bounds[2], bounds[3]), cull, pieces, CancellationToken.None);
        SvgAnalyticClipPoint p0 = new(coordinates[0], coordinates[1]), p1 = new(coordinates[2], coordinates[3]);
        if (kind == 2) clipper.ClipLine(p0, p1);
        else if (kind == 3) clipper.ClipQuad(p0, p1, new(coordinates[4], coordinates[5]));
        else clipper.ClipCubic(p0, p1, new(coordinates[4], coordinates[5]), new(coordinates[6], coordinates[7]));
        Assert.Equal(expected.Length, clipper.Count);
        Span<SvgAnalyticClipPoint> points = stackalloc SvgAnalyticClipPoint[4];
        for (int index = 0; index < expected.Length; index++)
        {
            SvgAnalyticClipPiece piece = pieces[index];
            Assert.Equal(expected[index][0], (uint)piece.Kind);
            points[0] = piece.First; points[1] = piece.Second; points[2] = piece.Third; points[3] = piece.Fourth;
            for (int at = 0; at < (int)piece.Kind; at++)
            {
                Assert.Equal(expected[index][1 + at * 2], BitConverter.SingleToUInt32Bits(points[at].X));
                Assert.Equal(expected[index][2 + at * 2], BitConverter.SingleToUInt32Bits(points[at].Y));
            }
        }
    }

    [Fact]
    public void Clipping_preserves_the_cancelled_token_before_mutating_caller_scratch()
    {
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        Span<SvgAnalyticClipPiece> pieces = stackalloc SvgAnalyticClipPiece[SvgAnalyticCoverage.Clipper.MaximumPieces];
        pieces.Clear();
        SvgAnalyticCoverage.Clipper clipper = new(new(8, 8, 56, 56), false, pieces, cancellation.Token);
        try
        {
            clipper.ClipCubic(new(4, 12), new(30, 2), new(62, 28), new(58, 48));
            Assert.Fail("Clipping must observe the caller's cancellation.");
        }
        catch (OperationCanceledException exception) { Assert.Equal(cancellation.Token, exception.CancellationToken); }
        Assert.Equal(0, clipper.Count);
        foreach (SvgAnalyticClipPiece piece in pieces) Assert.Equal(default, piece);
    }
}
