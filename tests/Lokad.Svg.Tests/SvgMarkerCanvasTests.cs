using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

[Trait("Layer", "Mechanisms")]
public sealed class SvgMarkerCanvasTests
{
    [Theory]
    [InlineData(16777215, -25165818, 4.5, 4)]
    [InlineData(-16777215, 25165826, 3.5, 4)]
    [InlineData(-16777216, 25165828, 4, 4)]
    public void Marker_pose_rounding_follows_the_parent_canvas(double x, double translation, double sceneX, double deviceX)
    {
        SvgAffineTransform parent = new(1.5, 0, 0, 1, translation, 0);
        SvgAffineTransform pose = SvgAffineTransform.FromTranslation(x, 4);
        SvgAffineTransform source = parent.Compose(pose);
        Assert.Equal(sceneX, source.E);
        SvgViewportDrawMap draw = new(parent, pose, source);
        Assert.Equal(new SvgAffineTransform(1.5, 0, 0, 1, deviceX, 4), draw.Prepare(SvgAffineTransform.Identity, source));
    }

    [Fact]
    public void Source_transform_and_marker_pose_keep_separate_canvas_stages()
    {
        SvgAffineTransform instance = new(1.5, 0, 0, 1, -25165818, 0);
        SvgAffineTransform local = SvgAffineTransform.FromTranslation(.5, .25);
        SvgAffineTransform pose = SvgAffineTransform.FromTranslation(16777215, 4);
        SvgAffineTransform source = instance.Compose(local.ComposeNative(pose));
        Assert.Equal(6, source.E);
        SvgViewportDrawMap draw = new(instance.Compose(local), pose, source);
        Assert.Equal(new SvgAffineTransform(1.5, 0, 0, 1, 4, 4.25), draw.Prepare(SvgAffineTransform.Identity, source));
    }

    [Fact]
    public void Retained_path_metadata_allows_inverse_intermediates_until_complete_device_admission()
    {
        RenderBudget budget = new(2_000_000);
        ParserWorkCounter work = new(1_000_000);
        using (SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'><rect width='2' height='2' transform='matrix(1 0 .25 1 0 0)'/></svg>"u8.ToArray(),
            SvgRenderLimits.Default, budget, CancellationToken.None))
        {
            SvgAuthoredPath source = Assert.Single(root.Geometries).AuthoredPath ?? throw new InvalidOperationException();
            using SvgAuthoredPath copied = source.Copy(SvgAffineTransform.FromScale(1e20, 1e20), budget, work, CancellationToken.None);
            Assert.Equal(SvgErrorCode.UnsupportedFeature, Assert.Throws<SvgException>(() =>
                new SvgViewportDrawMap(copied.SceneMap, SvgAffineTransform.Identity, copied.SceneMap)
                    .Prepare(SvgAffineTransform.Identity, copied.SceneMap)).ErrorCode);
            using SvgPreparedPathContours restored = copied.Prepare(SvgAffineTransform.FromScale(1e-20, 1e-20), false, budget, work, CancellationToken.None);
            Assert.Contains(new SvgDevicePoint(2, 0), restored.Points);
            Assert.Contains(new SvgDevicePoint(2.5, 2), restored.Points);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }
}
