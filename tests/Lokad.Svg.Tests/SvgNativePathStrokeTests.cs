using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgNativePathStrokeTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Coincident_prefix_lookahead_is_linear_and_preserves_contours_and_curve_tangents(bool squareCap, bool mapped)
    {
        SvgAuthoredPathCommand move = new(SvgAuthoredPathVerb.Move, SvgAuthoredArcFlags.None, new(2, 2), default, default);
        SvgAuthoredPathCommand duplicate = new(SvgAuthoredPathVerb.Line, SvgAuthoredArcFlags.None, new(2, 2), default, default);
        SvgAuthoredPathCommand[][] tails =
        [
            [],
            [new(SvgAuthoredPathVerb.Line, SvgAuthoredArcFlags.None, new(8, 2), default, default)],
            [new(SvgAuthoredPathVerb.Quadratic, SvgAuthoredArcFlags.None, new(5, 1), new(8, 2), default)],
            [new(SvgAuthoredPathVerb.Conic, SvgAuthoredArcFlags.None, new(5, 1), new(8, 2), new(.75, 0))],
            [new(SvgAuthoredPathVerb.Cubic, SvgAuthoredArcFlags.None, new(3, 1), new(7, 3), new(8, 2))],
            [new(SvgAuthoredPathVerb.Arc, SvgAuthoredArcFlags.Sweep, new(3, 2), default, new(8, 2))],
            [new(SvgAuthoredPathVerb.Close, SvgAuthoredArcFlags.None, default, default, default)],
            [new(SvgAuthoredPathVerb.Move, SvgAuthoredArcFlags.None, new(10, 2), default, default),
                new(SvgAuthoredPathVerb.Line, SvgAuthoredArcFlags.None, new(14, 2), default, default)],
            [new(SvgAuthoredPathVerb.End, SvgAuthoredArcFlags.None, default, default, default),
                new(SvgAuthoredPathVerb.Line, SvgAuthoredArcFlags.None, new(8, 2), default, default)]
        ];
        SvgAffineTransform map = mapped ? new(2, .25, .5, 3, .25, -.5) : SvgAffineTransform.Identity;
        SvgStrokeCap cap = squareCap ? SvgStrokeCap.Square : SvgStrokeCap.Round;
        foreach (SvgAuthoredPathCommand[] tail in tails)
        {
            SvgAuthoredPathCommand[] shortPath = [move, duplicate, .. tail];
            SvgAuthoredPathCommand[] longPath = [move, .. Enumerable.Repeat(duplicate, 15_000), .. tail];
            RenderBudget budget = new RenderBudget(new HostMemoryBudget(1_000_000));
            using (BudgetedParserList<SvgStrokeOutlineCommand> expected = new(budget, SvgMemoryCategory.Geometry))
            using (BudgetedParserList<SvgStrokeOutlineCommand> actual = new(budget, SvgMemoryCategory.Geometry))
            {
                SvgNativePathStroke.Build(shortPath, 1, 4, cap, SvgStrokeJoin.Miter, 1, map, expected, budget, CancellationToken.None);
                SvgNativePathStroke.Build(longPath, 1, 4, cap, SvgStrokeJoin.Miter, 1, map, actual, budget,
                    CancellationToken.None);
                Assert.Equal(expected.AsSpan().ToArray(), actual.AsSpan().ToArray());
            }
            Assert.Equal(0, budget.Snapshot().CurrentBytes);
        }
    }

    [Theory]
    [InlineData("round")]
    [InlineData("square")]
    public void Public_conversion_of_coincident_prefixes_matches_the_same_line(string cap)
    {
        string duplicates = string.Concat(Enumerable.Repeat("L2 2 ", 15_000));
        byte[] expected = SvgConverter.ConvertToPng(Input(string.Empty), SvgRenderOptions.Default, CancellationToken.None).ToArray();
        Assert.Equal(expected, SvgConverter.ConvertToPng(Input(duplicates), SvgRenderOptions.Default, CancellationToken.None).ToArray());

        byte[] Input(string prefix) => System.Text.Encoding.UTF8.GetBytes(
            $"<svg xmlns='http://www.w3.org/2000/svg' width='8' height='4'><path d='M2 2 {prefix}L6 2' fill='none' stroke='red' stroke-width='2' stroke-linecap='{cap}'/></svg>");
    }

    [Fact]
    public void Lookahead_restarts_when_a_teeny_line_changes_the_origin()
    {
        SvgAuthoredPathCommand move = new(SvgAuthoredPathVerb.Move, SvgAuthoredArcFlags.None, new(2, 2), default, default);
        SvgAuthoredPathCommand first = new(SvgAuthoredPathVerb.Line, SvgAuthoredArcFlags.None, new(2, 2), default, default);
        SvgAuthoredPathCommand second = first with { First = new(2.00001, 2) };
        SvgAuthoredPathCommand end = first with { First = new(6, 2) };
        SvgAuthoredPathCommand[] shortPath = [move, first, second, end];
        SvgAuthoredPathCommand[] longPath = [move, .. Enumerable.Repeat(first, 8000), .. Enumerable.Repeat(second, 8000), end];
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(1_000_000));
        using (BudgetedParserList<SvgStrokeOutlineCommand> expected = new(budget, SvgMemoryCategory.Geometry))
        using (BudgetedParserList<SvgStrokeOutlineCommand> actual = new(budget, SvgMemoryCategory.Geometry))
        {
            Build(shortPath, expected, budget, CancellationToken.None);
            Build(longPath, actual, budget, CancellationToken.None);
            Assert.Equal(expected.AsSpan().ToArray(), actual.AsSpan().ToArray());
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Coincident_lookahead_denial_releases_storage_and_allows_retry()
    {
        SvgAuthoredPathCommand[] path =
        [
            new(SvgAuthoredPathVerb.Move, SvgAuthoredArcFlags.None, new(2, 2), default, default),
            .. Enumerable.Repeat(new SvgAuthoredPathCommand(SvgAuthoredPathVerb.Line, SvgAuthoredArcFlags.None, new(2, 2), default, default), 15_000),
            new(SvgAuthoredPathVerb.Line, SvgAuthoredArcFlags.None, new(6, 2), default, default)
        ];
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(1_000_000));
        using (BudgetedParserList<SvgStrokeOutlineCommand> output = new(budget, SvgMemoryCategory.Geometry))
        {
            Assert.Equal(0, output.Count);
            Assert.Equal(0, budget.Snapshot().CurrentBytes);
            Build(path, output, budget, CancellationToken.None);
            Assert.NotEqual(0, output.Count);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Curve_offsets_joins_caps_and_closure_match_compiled_native_float_bits()
    {
        byte[] data = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData/path-stroke-native.csv.gz"));
        using JsonDocument metadata = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData/path-stroke-native.json")));
        Assert.Equal(metadata.RootElement.GetProperty("dataSha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(data)));
        using MemoryStream bytes = new(data);
        using GZipStream gzip = new(bytes, CompressionMode.Decompress);
        using StreamReader reader = new(gzip);
        _ = reader.ReadLine();
        int cases = 0, commands = 0;
        while (reader.ReadLine() is string row)
        {
            string[] columns = row.Split(',');
            SvgAuthoredPathCommand[] source = columns[6].Split(';').Select(ParseCommand).ToArray();
            uint[][] expected = columns[7].Length == 0 ? [] : columns[7].Split(';')
                .Select(value => value.Split(':').Select(v => uint.Parse(v, CultureInfo.InvariantCulture)).ToArray()).ToArray();
            RenderBudget budget = new RenderBudget(new HostMemoryBudget(16_777_216));
            using (BudgetedParserList<SvgStrokeOutlineCommand> output = new(budget, SvgMemoryCategory.Geometry))
            {
                SvgNativePathStroke.BuildGeneric(source, Number(columns[1]), Number(columns[2]), (SvgStrokeCap)int.Parse(columns[4], CultureInfo.InvariantCulture),
                    (SvgStrokeJoin)int.Parse(columns[5], CultureInfo.InvariantCulture), Number(columns[3]), output, budget, CancellationToken.None);
                Assert.True(expected.Length == output.Count, columns[0] + ": outline command count");
                for (int index = 0; index < output.Count; index++)
                {
                    SvgStrokeOutlineCommand actual = output[index];
                    uint[] bits = [actual.Verb == SvgStrokeOutlineVerb.Close ? 5u : (uint)actual.Verb,
                        Bits(actual.Control.X), Bits(actual.Control.Y), 0, 0, Bits(actual.End.X), Bits(actual.End.Y), Bits(actual.Weight)];
                    Assert.True(expected[index].SequenceEqual(bits), columns[0] + ": command " + index);
                }
                commands += output.Count;
            }
            Assert.Equal(0, budget.Snapshot().CurrentBytes);
            cases++;
        }
        Assert.Equal(1920, cases);
        Assert.Equal(metadata.RootElement.GetProperty("commands").GetInt32(), commands);

        static float Number(string value) => float.Parse(value, CultureInfo.InvariantCulture);
        static uint Bits(float value) => BitConverter.SingleToUInt32Bits(value);
        static SvgAuthoredPathCommand ParseCommand(string row)
        {
            float[] values = row.Split(':').Select(Number).ToArray();
            SvgAuthoredPathVerb verb = (int)values[0] switch
            {
                0 => SvgAuthoredPathVerb.Move, 1 => SvgAuthoredPathVerb.Line, 2 => SvgAuthoredPathVerb.Quadratic,
                3 => SvgAuthoredPathVerb.Conic, 4 => SvgAuthoredPathVerb.Cubic, 5 => SvgAuthoredPathVerb.Close,
                _ => throw new InvalidDataException("Invalid native input verb.")
            };
            return new(verb, SvgAuthoredArcFlags.None, new(values[1], values[2]), new(values[3], values[4]),
                verb == SvgAuthoredPathVerb.Conic ? new(values[7], 0) : new(values[5], values[6]));
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(1024)]
    [InlineData(1200)]
    public void Construction_reserves_private_storage_before_allocation(long limit)
    {
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(limit));
        using (BudgetedParserList<SvgStrokeOutlineCommand> output = new(budget, SvgMemoryCategory.Geometry))
        {
            SvgException denied = Assert.Throws<SvgException>(() => Build(CurvedPath(), output, budget, CancellationToken.None));
            Assert.Equal(SvgErrorCode.LimitExceeded, denied.ErrorCode);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Cancelled_construction_publishes_nothing_and_can_retry()
    {
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(1_000_000));
        using CancellationTokenSource source = new(); source.Cancel();
        using (BudgetedParserList<SvgStrokeOutlineCommand> output = new(budget, SvgMemoryCategory.Geometry))
        {
            OperationCanceledException error = Assert.ThrowsAny<OperationCanceledException>(() => Build(CurvedPath(), output, budget, source.Token));
            Assert.Equal(source.Token, error.CancellationToken);
            Assert.Equal(0, output.Count);
            Build(CurvedPath(), output, budget, CancellationToken.None);
            Assert.NotEqual(0, output.Count);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Non_scaling_construction_maps_source_verbs_once_before_fitting()
    {
        SvgAuthoredPathCommand[] source = CurvedPath();
        SvgAffineTransform map = new(2, 0, 0, 3, .25, -.5);
        SvgAuthoredPathCommand[] mapped = source.Select(command => command with
        {
            First = SvgNativePathStroke.MapPoint(map, command.First),
            Second = SvgNativePathStroke.MapPoint(map, command.Second),
            Third = SvgNativePathStroke.MapPoint(map, command.Third)
        }).ToArray();
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(1_000_000));
        using (BudgetedParserList<SvgStrokeOutlineCommand> first = new(budget, SvgMemoryCategory.Geometry))
        using (BudgetedParserList<SvgStrokeOutlineCommand> second = new(budget, SvgMemoryCategory.Geometry))
        {
            SvgNativePathStroke.Build(source, 4, 4, SvgStrokeCap.Round, SvgStrokeJoin.Round, 1, map, first, budget, CancellationToken.None);
            Build(mapped, second, budget, CancellationToken.None);
            Assert.Equal(first.AsSpan().ToArray(), second.AsSpan().ToArray());
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    private static void Build(SvgAuthoredPathCommand[] source, BudgetedParserList<SvgStrokeOutlineCommand> output,
        RenderBudget budget, CancellationToken token)
        => SvgNativePathStroke.Build(source, 4, 4, SvgStrokeCap.Round, SvgStrokeJoin.Round, 1, output, budget, token);

    private static SvgAuthoredPathCommand[] CurvedPath() =>
    [
        new(SvgAuthoredPathVerb.Move, SvgAuthoredArcFlags.None, new(6, 40), default, default),
        new(SvgAuthoredPathVerb.Cubic, SvgAuthoredArcFlags.None, new(-12, -12), new(76, 80), new(58, 12)),
        new(SvgAuthoredPathVerb.Quadratic, SvgAuthoredArcFlags.None, new(26, 70), new(6, 40), default),
        new(SvgAuthoredPathVerb.Close, SvgAuthoredArcFlags.None, default, default, default)
    ];
}
