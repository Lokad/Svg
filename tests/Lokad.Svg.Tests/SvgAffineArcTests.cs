using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgAffineArcTests
{
    [Theory]
    [InlineData("matrix(1.2 .3 .45 .85 3 6)")]
    [InlineData("matrix(-1.2 .3 .45 .85 45 6)")]
    [InlineData("matrix(1.4 0 0 .65 3 12)")]
    public void Affine_arc_owners_release_after_parser_denials_and_survive_failed_replay(string transform)
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='64' height='64'><path transform='" + transform + "' d='M32 20 A12 7 17 1 1 8 20 A12 7 17 1 1 32 20 Z'/></svg>");
        SvgRenderLimits defaults = SvgRenderLimits.Default;
        RenderBudget measured = new(1_000_000);
        using (SvgRoot root = SvgRootParser.Parse(input, defaults, measured, null, CancellationToken.None))
            Assert.NotEmpty(Assert.Single(root.Geometries.ToArray()).Polygon.QuadSpans);
        Assert.Equal(0, measured.Snapshot().CurrentBytes);
        long peak = measured.Snapshot().PeakBytes;
        for (long limit = 1; limit < peak; limit += 256) DenyMemory(limit);
        DenyMemory(peak - 1);
        RenderBudget retry = new(1_000_000);
        using (SvgRoot root = SvgRootParser.Parse(input, defaults, retry, null, CancellationToken.None))
        {
            SvgAuthoredPath source = Assert.Single(root.Geometries.ToArray()).AuthoredPath ?? throw new InvalidOperationException("Missing source arc commands.");
            SvgAuthoredPathCommand[] original = source.Commands.ToArray();
            Assert.Equal(2, original.Count(command => command.Verb == SvgAuthoredPathVerb.Arc));
            long owned = retry.Snapshot().CurrentBytes;
            SvgRenderLimits tight = new(defaults.MaximumAccountedBytes, defaults.MaximumInputBytes, defaults.MaximumDimension,
                defaults.MaximumPixels, defaults.MaximumEncodedPngBytes, defaults.MaximumNodes, defaults.MaximumPathCommands,
                checked((int)0L + 1), defaults.MaximumXmlDepth, defaults.MaximumAttributesPerElement);
            Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() =>
            {
                using var denied = SvgConverter.Rasterize(root, new(null, 1, SvgColor.Transparent, tight), retry, CancellationToken.None);
            }).ErrorCode);
            Assert.Equal(owned, retry.Snapshot().CurrentBytes);
            using (retry.Reserve(1_000_000 - owned - 1024, SvgMemoryCategory.Surface))
            {
                long padded = retry.Snapshot().CurrentBytes;
                Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() =>
                {
                    using var denied = SvgConverter.Rasterize(root, SvgRenderOptions.Default, retry, CancellationToken.None);
                }).ErrorCode);
                Assert.Equal(padded, retry.Snapshot().CurrentBytes);
            }
            using CancellationTokenSource cancellation = new();
            cancellation.Cancel();
            Assert.Equal(cancellation.Token, Assert.ThrowsAny<OperationCanceledException>(() =>
            {
                using var denied = SvgConverter.Rasterize(root, SvgRenderOptions.Default, retry, cancellation.Token);
            }).CancellationToken);
            Assert.Equal(original, source.Commands.ToArray());
            using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, SvgRenderOptions.Default, retry, CancellationToken.None))
                Assert.Contains(surface.Pixels, value => value != 0);
            Assert.Equal(owned, retry.Snapshot().CurrentBytes);
        }
        Assert.Equal(0, retry.Snapshot().CurrentBytes);

        void DenyMemory(long limit)
        {
            RenderBudget denied = new(limit);
            Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, defaults, denied, null, CancellationToken.None)).ErrorCode);
            Assert.Equal(0, denied.Snapshot().CurrentBytes);
        }
    }

    [Fact]
    public void Equal_length_sheared_columns_cannot_supply_a_scalar_stroke_width()
    {
        Assert.False(SvgAffineTransform.FromMatrix(1, .5, .5, 1, 0, 0).TryGetUniformScale(out _));
        Assert.True(SvgAffineTransform.FromMatrix(-.6, .8, .8, .6, 0, 0).TryGetUniformScale(out double scale));
        Assert.Equal(1, scale);
    }

    [Fact]
    public void Empty_line_verbs_count_for_native_dispatch_without_duplicate_classification_vertices()
    {
        byte[] input = "<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><path d='M1.5 1.5 L12.5 1.5 L12.5 1.5 L12.5 12.5 L1.5 12.5 Z'/></svg>"u8.ToArray();
        RenderBudget budget = new(1_000_000);
        using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, null, CancellationToken.None))
        {
            SvgAuthoredPath source = Assert.Single(root.Geometries.ToArray()).AuthoredPath ?? throw new InvalidOperationException();
            using SvgPreparedPathContours prepared = source.PrepareSource(SvgAffineTransform.Identity, false, budget, CancellationToken.None);
            Assert.Equal(5, prepared.NativePointCount);
            Assert.Equal(4, prepared.Points.Length);
            Assert.Equal(6, prepared.Commands.Length);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Mapped_zero_radius_arc_keeps_its_line_fallback_and_releases_storage()
    {
        byte[] input = "<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'><path transform='matrix(1 .3 .45 1 3 4)' d='M4 4 A0 3 17 1 1 14 4 L8 12 Z'/></svg>"u8.ToArray();
        RenderBudget budget = new(1_000_000);
        using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, null, CancellationToken.None))
        {
            SvgGeometry geometry = Assert.Single(root.Geometries.ToArray());
            Assert.Empty(geometry.Polygon.QuadSpans);
            Assert.Contains((geometry.AuthoredPath ?? throw new InvalidOperationException()).Commands.ToArray(), command => command.Verb == SvgAuthoredPathVerb.Arc);
            using SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, SvgRenderOptions.Default, budget, CancellationToken.None);
            Assert.Contains(surface.Pixels, value => value != 0);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }
}
