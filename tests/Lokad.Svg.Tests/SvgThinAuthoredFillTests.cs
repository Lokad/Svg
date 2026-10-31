using TextEncoding = System.Text.Encoding;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgThinAuthoredFillTests
{
    [Theory]
    [Trait("Layer", "Mechanism")]
    [InlineData("M4.25 4.125 C4.25 4.2 5.8 4.5 7.5 4.75 L4.25 4.125 Z")]
    [InlineData("M4.25 4.125 C4.25 4.2 5.8 4.5 7.5 4.75 Z")]
    [InlineData("M4.25 4.125 C4.25 4.2 5.8 4.5 7.5 4.75")]
    [InlineData("M4.25 4.125 Q5.8 4.3 7.5 4.75 L4.25 4.125 Z")]
    [InlineData("M4.25 4.125 Q5.8 4.3 7.5 4.75 Z")]
    public void A_single_flattened_segment_keeps_its_authored_curve_and_closing_chord(string path)
    {
        using SvgRoot scene = SvgRootParser.Parse(Source(path), SvgRenderLimits.Default, CancellationToken.None);
        SvgGeometry geometry = Assert.Single(scene.Geometries);
        Assert.Empty(geometry.Polygon.Points);
        SvgAuthoredPath source = Assert.IsType<SvgAuthoredPath>(geometry.AuthoredPath);
        using SvgPreparedPathContours prepared = source.PrepareSource(SvgAffineTransform.Identity, true,
            new(new HostMemoryBudget(1_000_000)), CancellationToken.None);
        Assert.Equal(3, Assert.Single(prepared.ContourEnds));
        Assert.Equal(prepared.Points[0], prepared.Points[^1]);
        Assert.Equal(1, prepared.QuadSpans.Length + prepared.CubicSpans.Length);
        Assert.Equal(source.Commands, prepared.Commands);
    }

    [Theory]
    [Trait("Layer", "Mechanism")]
    [InlineData("M4.25 4.125")]
    [InlineData("M4.25 4.125 L7.5 4.75 Z")]
    [InlineData("M4.25 4.125 L4.25 4.125 Z")]
    public void A_short_straight_contour_stays_empty(string path)
    {
        using SvgRoot scene = SvgRootParser.Parse(Source(path), SvgRenderLimits.Default, CancellationToken.None);
        SvgAuthoredPath source = Assert.IsType<SvgAuthoredPath>(Assert.Single(scene.Geometries).AuthoredPath);
        using SvgPreparedPathContours prepared = source.PrepareSource(SvgAffineTransform.Identity, true,
            new(new HostMemoryBudget(1_000_000)), CancellationToken.None);
        Assert.Empty(prepared.Points);
        Assert.Empty(prepared.ContourEnds);
    }

    [Theory]
    [Trait("Layer", "Mechanism")]
    [InlineData("M1 1 L2 1 Z M4.25 4.125 C4.25 4.2 5.8 4.5 7.5 4.75 Z M10.25 4.125 Q11.8 4.3 13.5 4.75 Z", 1, 0)]
    [InlineData("M4.25 4.125 Q5.8 4.3 7.5 4.75 Z M1 1 L2 1 Z M10.25 4.125 C10.25 4.2 11.8 4.5 13.5 4.75 Z", 0, 1)]
    public void Dropped_straight_contours_do_not_move_retained_curve_indices(string path, int quadraticContour, int cubicContour)
    {
        using SvgRoot scene = SvgRootParser.Parse(Source(path), SvgRenderLimits.Default, CancellationToken.None);
        SvgAuthoredPath source = Assert.IsType<SvgAuthoredPath>(Assert.Single(scene.Geometries).AuthoredPath);
        using SvgPreparedPathContours prepared = source.PrepareSource(SvgAffineTransform.Identity, true,
            new(new HostMemoryBudget(1_000_000)), CancellationToken.None);
        Assert.Equal(new[] { 3, 6 }, prepared.ContourEnds);
        Assert.Equal(quadraticContour, Assert.Single(prepared.QuadSpans).Contour);
        Assert.Equal(cubicContour, Assert.Single(prepared.CubicSpans).Contour);
    }

    [Theory]
    [Trait("Layer", "Mechanism")]
    [InlineData("M4.25 4.125 C4.25 4.2 5.8 4.5 7.5 4.75 L4.25 4.125 Z")]
    [InlineData("M4.25 4.125 Q5.8 4.3 7.5 4.75 Z")]
    [InlineData("M1 1 L2 1 Z M4.25 4.125 Q5.8 4.3 7.5 4.75 Z")]
    public void Clip_staging_keeps_a_short_curved_contour(string path)
    {
        byte[] input = TextEncoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='24' height='16'><defs><clipPath id='c'><path d='"
            + path + "'/></clipPath></defs><rect width='24' height='16' clip-path='url(#c)'/></svg>");
        using SvgRoot scene = SvgRootParser.Parse(input, SvgRenderLimits.Default, CancellationToken.None);
        SvgSceneStorage storage = Assert.IsType<SvgSceneStorage>(scene.SceneStorage);
        SvgAuthoredPath source = Assert.Single(storage.MaskContents.SelectMany(content => content.Geometries)
            .Select(geometry => geometry.AuthoredPath).OfType<SvgAuthoredPath>(), HasCurve);
        using SvgPreparedPathContours prepared = source.PrepareSource(SvgAffineTransform.Identity, false,
            new(new HostMemoryBudget(1_000_000)), CancellationToken.None);
        Assert.Equal(3, Assert.Single(prepared.ContourEnds));
        Assert.Equal(1, prepared.QuadSpans.Length + prepared.CubicSpans.Length);

        static bool HasCurve(SvgAuthoredPath source)
        {
            foreach (SvgAuthoredPathCommand command in source.Commands)
                if (command.Verb is SvgAuthoredPathVerb.Quadratic or SvgAuthoredPathVerb.Cubic) return true;
            return false;
        }
    }

    [Theory]
    [Trait("Layer", "Contract")]
    [InlineData("fill='none'")]
    [InlineData("display='none'")]
    [InlineData("clip-path='url(#empty)'")]
    public void Explicit_paint_suppression_still_prevents_authored_replay(string attributes)
    {
        byte[] input = TextEncoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='24' height='16'><defs><clipPath id='empty'/></defs><path "
            + attributes + " d='M4.25 4.125 Q5.8 4.3 7.5 4.75 Z'/></svg>");
        using SvgRoot scene = SvgRootParser.Parse(input, SvgRenderLimits.Default, CancellationToken.None);
        using SvgConverter.RasterizedSurface pixels = SvgConverter.Rasterize(scene, SvgRenderOptions.Default,
            new(new HostMemoryBudget(1_000_000)), CancellationToken.None);
        for (int offset = 3; offset < pixels.Pixels.Length; offset += 4) Assert.Equal(0, pixels.Pixels[offset]);
    }

    [Fact]
    [Trait("Layer", "Contract")]
    public void Thin_curve_preparation_releases_refused_or_cancelled_scratch_and_retries_without_mutating_source()
    {
        using SvgRoot scene = SvgRootParser.Parse(Source("M4.25 4.125 Q5.8 4.3 7.5 4.75 Z"), SvgRenderLimits.Default, CancellationToken.None);
        SvgAuthoredPath source = Assert.IsType<SvgAuthoredPath>(Assert.Single(scene.Geometries).AuthoredPath);
        SvgAuthoredPathCommand[] original = source.Commands.ToArray();
        RenderBudget accepted = new(new HostMemoryBudget(1_000_000));
        SvgDevicePoint[] expected;
        using (SvgPreparedPathContours prepared = source.PrepareSource(SvgAffineTransform.Identity, true, accepted, CancellationToken.None))
        {
            Assert.Single(prepared.ContourEnds);
            expected = prepared.Points.ToArray();
        }
        Assert.Equal(0, accepted.Snapshot().CurrentBytes);
        RenderBudget denied = new(new HostMemoryBudget(accepted.Snapshot().PeakBytes - 1));
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() =>
        {
            using SvgPreparedPathContours prepared = source.PrepareSource(SvgAffineTransform.Identity, true, denied, CancellationToken.None);
        }).ErrorCode);
        Assert.Equal(0, denied.Snapshot().CurrentBytes);
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() =>
        {
            using SvgPreparedPathContours prepared = source.PrepareSource(SvgAffineTransform.Identity, true, accepted, cancellation.Token);
        });
        Assert.Equal(0, accepted.Snapshot().CurrentBytes);
        using (SvgPreparedPathContours retry = source.PrepareSource(SvgAffineTransform.Identity, true, accepted, CancellationToken.None))
            Assert.Equal(expected, retry.Points);
        Assert.Equal(0, accepted.Snapshot().CurrentBytes);
        Assert.Equal(original, source.Commands);
    }

    private static byte[] Source(string path)
        => TextEncoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='24' height='16'><path fill-rule='evenodd' d='" + path + "'/></svg>");
}
