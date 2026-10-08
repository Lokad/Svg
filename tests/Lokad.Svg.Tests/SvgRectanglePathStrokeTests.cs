using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgRectanglePathStrokeTests
{
    [Fact]
    public void Rectangle_dispatch_matches_compiled_native_outline_bits_for_both_directions_and_all_joins()
    {
        byte[] data = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData/rectangle-stroke-native.csv"));
        using JsonDocument metadata = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData/rectangle-stroke-native.json")));
        Assert.Equal(metadata.RootElement.GetProperty("dataSha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(data)));
        string[] rows = System.Text.Encoding.UTF8.GetString(data).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(metadata.RootElement.GetProperty("cases").GetInt32(), rows.Length - 1);
        foreach (string row in rows.Skip(1))
        {
            string[] columns = row.TrimEnd('\r').Split(',');
            SvgAuthoredPathCommand[] source = Rectangle(Number(columns[0]), Number(columns[1]), Number(columns[2]), Number(columns[3]), columns[7] == "1");
            uint[][] expected = columns[8].Split(';').Select(value => value.Split(':').Select(v => uint.Parse(v, CultureInfo.InvariantCulture)).ToArray()).ToArray();
            RenderBudget budget = new(1_000_000);
            using (BudgetedParserList<SvgStrokeOutlineCommand> output = new(budget, SvgMemoryCategory.Geometry))
            {
                SvgNativePathStroke.Build(source, Number(columns[4]), Number(columns[5]), SvgStrokeCap.Butt,
                    (SvgStrokeJoin)int.Parse(columns[6], CultureInfo.InvariantCulture), 1, output, budget, new(100_000), CancellationToken.None);
                Assert.True(expected.Length == output.Count, row + ": command count");
                for (int index = 0; index < output.Count; index++)
                {
                    SvgStrokeOutlineCommand command = output[index];
                    uint[] bits = [command.Verb == SvgStrokeOutlineVerb.Close ? 5u : (uint)command.Verb,
                        Bits(command.Control.X), Bits(command.Control.Y), Bits(command.End.X), Bits(command.End.Y), Bits(command.Weight)];
                    Assert.True(expected[index].SequenceEqual(bits), row + ": command " + index);
                }
            }
            Assert.Equal(0, budget.Snapshot().CurrentBytes);
        }

        static float Number(string value) => float.Parse(value, CultureInfo.InvariantCulture);
        static uint Bits(float value) => BitConverter.SingleToUInt32Bits(value);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(1)]
    public void Every_work_and_memory_denial_releases_output_and_preserves_source(int joinValue)
    {
        SvgStrokeJoin join = (SvgStrokeJoin)joinValue;
        SvgAuthoredPathCommand[] source = Rectangle(4.5f, 6.25f, 25.5f, 23.5f, true);
        SvgAuthoredPathCommand[] original = source.ToArray();
        ParserWorkCounter measured = new(100_000);
        RenderBudget baseline = new(1_000_000);
        using (BudgetedParserList<SvgStrokeOutlineCommand> output = new(baseline, SvgMemoryCategory.Geometry, measured, CancellationToken.None))
            Build(source, join, SvgAffineTransform.Identity, output, baseline, measured, CancellationToken.None);
        Assert.Equal(0, baseline.Snapshot().CurrentBytes);
        for (long limit = 0; limit < measured.Used; limit++)
        {
            RenderBudget budget = new(1_000_000);
            ParserWorkCounter work = new(limit);
            using (BudgetedParserList<SvgStrokeOutlineCommand> output = new(budget, SvgMemoryCategory.Geometry, work, CancellationToken.None))
                Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => Build(source, join, SvgAffineTransform.Identity, output, budget, work, CancellationToken.None)).ErrorCode);
            Assert.Equal(0, budget.Snapshot().CurrentBytes);
        }
        for (long limit = 1; limit < baseline.Snapshot().PeakBytes; limit++)
        {
            RenderBudget budget = new(limit);
            using (BudgetedParserList<SvgStrokeOutlineCommand> output = new(budget, SvgMemoryCategory.Geometry))
                Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => Build(source, join, SvgAffineTransform.Identity, output, budget, new(100_000), CancellationToken.None)).ErrorCode);
            Assert.Equal(0, budget.Snapshot().CurrentBytes);
        }
        Assert.Equal(original, source);
        using (BudgetedParserList<SvgStrokeOutlineCommand> retry = new(baseline, SvgMemoryCategory.Geometry))
            Build(source, join, SvgAffineTransform.Identity, retry, baseline, new(100_000), CancellationToken.None);
        Assert.Equal(0, baseline.Snapshot().CurrentBytes);
    }

    [Theory]
    [InlineData(2, 0, 0, 3, .25, -.5)]
    [InlineData(-1, 0, 0, 1, 40, .125)]
    [InlineData(0, 1, -1, 0, 40, .5)]
    [InlineData(1, .25, .5, 1, .125, .375)]
    public void Construction_mapping_precedes_rectangle_recognition(double a, double b, double c, double d, double e, double f)
    {
        SvgAuthoredPathCommand[] source = Rectangle(4.5f, 6.25f, 25.5f, 23.5f, true);
        SvgAffineTransform map = new(a, b, c, d, e, f);
        SvgAuthoredPathCommand[] mapped = source.Select(command => command.Verb is SvgAuthoredPathVerb.Move or SvgAuthoredPathVerb.Line
            ? command with { First = SvgNativePathStroke.MapPoint(map, command.First) } : command).ToArray();
        RenderBudget budget = new(1_000_000);
        using (BudgetedParserList<SvgStrokeOutlineCommand> first = new(budget, SvgMemoryCategory.Geometry))
        using (BudgetedParserList<SvgStrokeOutlineCommand> second = new(budget, SvgMemoryCategory.Geometry))
        {
            Build(source, SvgStrokeJoin.Round, map, first, budget, new(100_000), CancellationToken.None);
            Build(mapped, SvgStrokeJoin.Round, SvgAffineTransform.Identity, second, budget, new(100_000), CancellationToken.None);
            Assert.Equal(first.AsSpan().ToArray(), second.AsSpan().ToArray());
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Cancelled_rectangle_construction_publishes_nothing_and_can_retry()
    {
        RenderBudget budget = new(1_000_000);
        using CancellationTokenSource cancellation = new(); cancellation.Cancel();
        using (BudgetedParserList<SvgStrokeOutlineCommand> output = new(budget, SvgMemoryCategory.Geometry))
        {
            SvgAuthoredPathCommand[] source = Rectangle(4.5f, 6.25f, 25.5f, 23.5f, true);
            OperationCanceledException error = Assert.ThrowsAny<OperationCanceledException>(() => Build(source, SvgStrokeJoin.Round, SvgAffineTransform.Identity, output, budget, new(100_000), cancellation.Token));
            Assert.Equal(cancellation.Token, error.CancellationToken);
            Assert.Equal(0, output.Count);
            Build(source, SvgStrokeJoin.Round, SvgAffineTransform.Identity, output, budget, new(100_000), CancellationToken.None);
            Assert.Equal(15, output.Count);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    private static void Build(SvgAuthoredPathCommand[] source, SvgStrokeJoin join, SvgAffineTransform map,
        BudgetedParserList<SvgStrokeOutlineCommand> output, RenderBudget budget, ParserWorkCounter work, CancellationToken token)
        => SvgNativePathStroke.Build(source, 1, 4, SvgStrokeCap.Butt, join, 1, map, output, budget, work, token);

    private static SvgAuthoredPathCommand[] Rectangle(float left, float top, float right, float bottom, bool clockwise) =>
    [
        new(SvgAuthoredPathVerb.Move, SvgAuthoredArcFlags.None, new(left, top), default, default),
        new(SvgAuthoredPathVerb.Line, SvgAuthoredArcFlags.None, clockwise ? new(right, top) : new(left, bottom), default, default),
        new(SvgAuthoredPathVerb.Line, SvgAuthoredArcFlags.None, new(right, bottom), default, default),
        new(SvgAuthoredPathVerb.Line, SvgAuthoredArcFlags.None, clockwise ? new(left, bottom) : new(right, top), default, default),
        new(SvgAuthoredPathVerb.Close, SvgAuthoredArcFlags.None, default, default, default)
    ];
}
