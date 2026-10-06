using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgNativePathStrokeTests
{
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
            RenderBudget budget = new(16_777_216);
            using (BudgetedParserList<SvgStrokeOutlineCommand> output = new(budget, SvgMemoryCategory.Geometry))
            {
                SvgNativePathStroke.Build(source, Number(columns[1]), Number(columns[2]), (SvgStrokeCap)int.Parse(columns[4], CultureInfo.InvariantCulture),
                    (SvgStrokeJoin)int.Parse(columns[5], CultureInfo.InvariantCulture), Number(columns[3]), output, budget, new(2_000_000), CancellationToken.None);
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
    [InlineData(0)]
    [InlineData(16)]
    [InlineData(100)]
    public void Construction_work_denial_releases_private_lists_without_mutating_source(int limit)
    {
        SvgAuthoredPathCommand[] source = CurvedPath();
        SvgAuthoredPathCommand[] original = source.ToArray();
        RenderBudget budget = new(1_000_000);
        using (BudgetedParserList<SvgStrokeOutlineCommand> output = new(budget, SvgMemoryCategory.Geometry))
        {
            SvgException denied = Assert.Throws<SvgException>(() => Build(source, output, budget, new(limit), CancellationToken.None));
            Assert.Equal(SvgErrorCode.LimitExceeded, denied.ErrorCode);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        Assert.Equal(original, source);
        using (BudgetedParserList<SvgStrokeOutlineCommand> retry = new(budget, SvgMemoryCategory.Geometry))
        {
            Build(source, retry, budget, new(100_000), CancellationToken.None);
            Assert.Contains(retry.AsSpan().ToArray(), command => command.Verb == SvgStrokeOutlineVerb.Quadratic);
            Assert.Contains(retry.AsSpan().ToArray(), command => command.Verb == SvgStrokeOutlineVerb.Conic);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(1024)]
    [InlineData(1200)]
    public void Construction_reserves_private_storage_before_allocation(long limit)
    {
        RenderBudget budget = new(limit);
        using (BudgetedParserList<SvgStrokeOutlineCommand> output = new(budget, SvgMemoryCategory.Geometry))
        {
            SvgException denied = Assert.Throws<SvgException>(() => Build(CurvedPath(), output, budget, new(100_000), CancellationToken.None));
            Assert.Equal(SvgErrorCode.LimitExceeded, denied.ErrorCode);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Cancelled_construction_publishes_nothing_and_can_retry()
    {
        RenderBudget budget = new(1_000_000);
        using CancellationTokenSource source = new(); source.Cancel();
        using (BudgetedParserList<SvgStrokeOutlineCommand> output = new(budget, SvgMemoryCategory.Geometry))
        {
            OperationCanceledException error = Assert.ThrowsAny<OperationCanceledException>(() => Build(CurvedPath(), output, budget, new(100_000), source.Token));
            Assert.Equal(source.Token, error.CancellationToken);
            Assert.Equal(0, output.Count);
            Build(CurvedPath(), output, budget, new(100_000), CancellationToken.None);
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
        RenderBudget budget = new(1_000_000);
        using (BudgetedParserList<SvgStrokeOutlineCommand> first = new(budget, SvgMemoryCategory.Geometry))
        using (BudgetedParserList<SvgStrokeOutlineCommand> second = new(budget, SvgMemoryCategory.Geometry))
        {
            SvgNativePathStroke.Build(source, 4, 4, SvgStrokeCap.Round, SvgStrokeJoin.Round, 1, map, first, budget, new(100_000), CancellationToken.None);
            Build(mapped, second, budget, new(100_000), CancellationToken.None);
            Assert.Equal(first.AsSpan().ToArray(), second.AsSpan().ToArray());
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    private static void Build(SvgAuthoredPathCommand[] source, BudgetedParserList<SvgStrokeOutlineCommand> output,
        RenderBudget budget, ParserWorkCounter work, CancellationToken token)
        => SvgNativePathStroke.Build(source, 4, 4, SvgStrokeCap.Round, SvgStrokeJoin.Round, 1, output, budget, work, token);

    private static SvgAuthoredPathCommand[] CurvedPath() =>
    [
        new(SvgAuthoredPathVerb.Move, SvgAuthoredArcFlags.None, new(6, 40), default, default),
        new(SvgAuthoredPathVerb.Cubic, SvgAuthoredArcFlags.None, new(-12, -12), new(76, 80), new(58, 12)),
        new(SvgAuthoredPathVerb.Quadratic, SvgAuthoredArcFlags.None, new(26, 70), new(6, 40), default),
        new(SvgAuthoredPathVerb.Close, SvgAuthoredArcFlags.None, default, default, default)
    ];
}
