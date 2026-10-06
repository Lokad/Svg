using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgNativeDashPathTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(40)]
    [InlineData(400)]
    public void Filter_work_denial_and_cancellation_release_private_lists_and_allow_retry(int limit)
    {
        SvgAuthoredPathCommand[] commands = CurvedPath();
        SvgAuthoredPathCommand[] original = commands.ToArray();
        RenderBudget budget = new(1_000_000);
        SvgException denied = Assert.Throws<SvgException>(() => Run(new(limit), CancellationToken.None));
        Assert.Equal(SvgErrorCode.LimitExceeded, denied.ErrorCode);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        using CancellationTokenSource cancelled = new(); cancelled.Cancel();
        OperationCanceledException error = Assert.ThrowsAny<OperationCanceledException>(() => Run(new(100_000), cancelled.Token));
        Assert.Equal(cancelled.Token, error.CancellationToken);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        using (OwnedParserArray<SvgAuthoredPathCommand> result = SvgNativeDashPath.Filter(commands, [5, 3], .75, null,
            SvgAffineTransform.Identity, 1, budget, new(100_000), CancellationToken.None))
        {
            Assert.Contains(result.Items, c => c.Verb == SvgAuthoredPathVerb.Conic);
            Assert.True(budget.Snapshot().CurrentBytes > 0);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        Assert.Equal(original, commands);

        void Run(ParserWorkCounter work, CancellationToken token)
        {
            using OwnedParserArray<SvgAuthoredPathCommand> result = SvgNativeDashPath.Filter(commands, [5, 3], .75, null,
                SvgAffineTransform.Identity, 1, budget, work, token);
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(1024)]
    [InlineData(4096)]
    public void Filter_memory_denial_releases_all_temporary_and_output_owners(int bytes)
    {
        RenderBudget budget = new(bytes);
        SvgException denied = Assert.Throws<SvgException>(() =>
        {
            using OwnedParserArray<SvgAuthoredPathCommand> result = SvgNativeDashPath.Filter(CurvedPath(), [5, 3], .75, null,
                SvgAffineTransform.Identity, 4, budget, new(100_000), CancellationToken.None);
        });
        Assert.Equal(SvgErrorCode.LimitExceeded, denied.ErrorCode);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Closed_contour_appends_initial_dash_to_final_dash_through_the_original_seam()
    {
        SvgAuthoredPathCommand[] commands = [Move(0, 0), Line(5, 0), Line(5, 4), Line(0, 4), Close()];
        RenderBudget budget = new(100_000);
        using (OwnedParserArray<SvgAuthoredPathCommand> result = SvgNativeDashPath.Filter(commands, [5, 3], 0, null,
            SvgAffineTransform.Identity, 1, budget, new(100_000), CancellationToken.None))
        {
            Assert.Equal(2, result.Items.Count(c => c.Verb == SvgAuthoredPathVerb.Move));
            int last = Array.FindLastIndex(result.Items, c => c.Verb == SvgAuthoredPathVerb.Move);
            Assert.Equal(new SvgDevicePoint(0, 2), result.Items[last].First);
            Assert.Equal(new SvgDevicePoint(0, 0), result.Items[last + 1].First);
            Assert.Equal(new SvgDevicePoint(5, 0), result.Items[last + 2].First);
            Assert.Equal(SvgAuthoredPathVerb.End, result.Items[last + 3].Verb);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Zero_on_intervals_emit_cap_dots_and_negative_phase_wraps_in_float_units()
    {
        SvgAuthoredPathCommand[] commands = [Move(0, 0), Line(9, 0), End()];
        RenderBudget budget = new(100_000);
        using (OwnedParserArray<SvgAuthoredPathCommand> dots = SvgNativeDashPath.Filter(commands, [0, 3], 0, null,
            SvgAffineTransform.Identity, 1, budget, new(100_000), CancellationToken.None))
        {
            Assert.Equal(9, dots.Items.Length);
            for (int at = 0; at < 3; at++)
            {
                Assert.Equal(new SvgDevicePoint(3 * at, 0), dots.Items[3 * at].First);
                Assert.Equal(dots.Items[3 * at].First, dots.Items[3 * at + 1].First);
                Assert.Equal(SvgAuthoredPathVerb.Line, dots.Items[3 * at + 1].Verb);
            }
        }
        using (OwnedParserArray<SvgAuthoredPathCommand> negative = SvgNativeDashPath.Filter(commands, [5, 3], -.75, null,
            SvgAffineTransform.Identity, 1, budget, new(100_000), CancellationToken.None))
            Assert.Equal(new SvgDevicePoint(.75f, 0), negative.Items[0].First);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Path_length_scales_each_contours_intervals_from_the_total_path_distance()
    {
        SvgAuthoredPathCommand[] commands = [Move(0, 0), Line(8, 0), End(), Move(0, 2), Line(8, 2), End()];
        RenderBudget budget = new(100_000);
        using (OwnedParserArray<SvgAuthoredPathCommand> result = SvgNativeDashPath.Filter(commands, [2, 2], 0, 8,
            SvgAffineTransform.Identity, 1, budget, new(100_000), CancellationToken.None))
        {
            Assert.Equal(6, result.Items.Length);
            Assert.Equal(new SvgDevicePoint(4, 0), result.Items[1].First);
            Assert.Equal(new SvgDevicePoint(4, 2), result.Items[4].First);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Theory]
    [InlineData("M6 40 Q30 -4 58 38", "butt", ".5")]
    [InlineData("M6 40 C-12 -12 76 80 58 12", "round", ".5")]
    [InlineData("M6 36 A25 17 25 1 1 56 32 Z", "square", ".5")]
    [InlineData("M6 40 Q30 -4 58 38", "butt", "8")]
    [InlineData("M6 40 C-12 -12 76 80 58 12", "round", "8")]
    [InlineData("M6 36 A25 17 25 1 1 56 32 Z", "square", "8")]
    public async Task Retained_dashed_curves_agree_through_six_outputs_and_release_after_raster_denial(string path, string cap, string width)
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='64' height='64'><path fill='none' stroke='#eb1c23' stroke-width='" + width + "' stroke-linecap='" + cap + "' stroke-dasharray='5 3' stroke-dashoffset='.75' d='" + path + "'/></svg>");
        byte[] expected = SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None).ToArray();
        byte[] destination = new byte[expected.Length];
        Assert.Equal(expected.Length, SvgConverter.ConvertToPng(input, destination, SvgRenderOptions.Default, CancellationToken.None));
        Assert.Equal(expected, destination);
        using MemoryStream stream = new();
        Assert.Equal(expected.Length, SvgConverter.ConvertToPng(input, stream, SvgRenderOptions.Default, CancellationToken.None));
        Assert.Equal(expected, stream.ToArray());
        Assert.Equal(expected, (await SvgConverter.ConvertToPngAsync(input, SvgRenderOptions.Default, CancellationToken.None)).ToArray());
        Assert.Equal(expected.Length, await SvgConverter.ConvertToPngAsync(input, destination, SvgRenderOptions.Default, CancellationToken.None));
        Assert.Equal(expected, destination);
        using MemoryStream asyncStream = new();
        Assert.Equal(expected.Length, await SvgConverter.ConvertToPngAsync(input, asyncStream, SvgRenderOptions.Default, CancellationToken.None));
        Assert.Equal(expected, asyncStream.ToArray());

        SvgRenderLimits defaults = SvgRenderLimits.Default;
        RenderBudget budget = new(defaults.MaximumAccountedBytes);
        using (SvgRoot root = SvgRootParser.Parse(input, defaults, budget, null, new(defaults.MaximumWorkUnits), CancellationToken.None))
        {
            SvgAuthoredPath retained = Assert.IsType<SvgAuthoredPath>(Assert.Single(root.Geometries).AuthoredPath);
            SvgAuthoredPathCommand[] original = retained.Commands.ToArray();
            long owned = budget.Snapshot().CurrentBytes;
            SvgRenderLimits tight = new(defaults.MaximumAccountedBytes, defaults.MaximumInputBytes, defaults.MaximumDimension,
                defaults.MaximumPixels, defaults.MaximumEncodedPngBytes, defaults.MaximumNodes, defaults.MaximumPathCommands,
                checked((int)root.ParserWorkUnits + 100), defaults.MaximumXmlDepth, defaults.MaximumAttributesPerElement);
            SvgException work = Assert.Throws<SvgException>(() => { using var denied = SvgConverter.Rasterize(root, new(null, 1, SvgColor.Transparent, tight), budget, CancellationToken.None); });
            Assert.Equal(SvgErrorCode.LimitExceeded, work.ErrorCode);
            Assert.Equal(owned, budget.Snapshot().CurrentBytes);
            using (budget.Reserve(defaults.MaximumAccountedBytes - owned - 1024, SvgMemoryCategory.Surface))
            {
                long padded = budget.Snapshot().CurrentBytes;
                SvgException memory = Assert.Throws<SvgException>(() => { using var denied = SvgConverter.Rasterize(root, SvgRenderOptions.Default, budget, CancellationToken.None); });
                Assert.Equal(SvgErrorCode.LimitExceeded, memory.ErrorCode);
                Assert.Equal(padded, budget.Snapshot().CurrentBytes);
            }
            using CancellationTokenSource cancelled = new(); cancelled.Cancel();
            OperationCanceledException error = Assert.ThrowsAny<OperationCanceledException>(() => { using var denied = SvgConverter.Rasterize(root, SvgRenderOptions.Default, budget, cancelled.Token); });
            Assert.Equal(cancelled.Token, error.CancellationToken);
            Assert.Equal(original, retained.Commands.ToArray());
            using (SvgConverter.RasterizedSurface retry = SvgConverter.Rasterize(root, SvgRenderOptions.Default, budget, CancellationToken.None))
                Assert.Contains(retry.Pixels, v => v != 0);
            Assert.Equal(owned, budget.Snapshot().CurrentBytes);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    private static SvgAuthoredPathCommand[] CurvedPath() => [Move(6, 40),
        new(SvgAuthoredPathVerb.Cubic, SvgAuthoredArcFlags.None, new(-12, -12), new(76, 80), new(58, 12)),
        new(SvgAuthoredPathVerb.Arc, SvgAuthoredArcFlags.Large | SvgAuthoredArcFlags.Sweep, new(25, 17), new(25, 0), new(6, 40)), Close()];
    private static SvgAuthoredPathCommand Move(float x, float y) => new(SvgAuthoredPathVerb.Move, SvgAuthoredArcFlags.None, new(x, y), default, default);
    private static SvgAuthoredPathCommand Line(float x, float y) => new(SvgAuthoredPathVerb.Line, SvgAuthoredArcFlags.None, new(x, y), default, default);
    private static SvgAuthoredPathCommand End() => new(SvgAuthoredPathVerb.End, SvgAuthoredArcFlags.None, default, default, default);
    private static SvgAuthoredPathCommand Close() => new(SvgAuthoredPathVerb.Close, SvgAuthoredArcFlags.None, default, default, default);
}
