using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;
using Xunit;

namespace Lokad.Svg.Tests;

public sealed class SvgIdentityGroupTests
{
    [Fact]
    public void Identity_group_keeps_two_nonfoldable_paints_without_an_offscreen_surface_while_real_opacity_isolates()
    {
        (byte[] pixels, long peak) direct = Render(null);
        (byte[] pixels, long peak) identity = Render(SvgPaintOpacity.Opaque);
        (byte[] pixels, long peak) translucent = Render(new SvgPaintOpacity(128, .5f));
        Assert.Equal(direct.pixels, identity.pixels);
        Assert.Equal(direct.peak, identity.peak);
        Assert.True(translucent.peak > identity.peak);
        Assert.False(direct.pixels.AsSpan().SequenceEqual(translucent.pixels));

        static (byte[], long) Render(SvgPaintOpacity? opacity)
        {
            RenderBudget sceneBudget = new(1_000_000), rasterBudget = new(1_000_000);
            byte[] pixels;
            using (SvgSceneBuilder builder = new(sceneBudget, CancellationToken.None))
            {
                SvgRecordingScope scope = new(1);
                if (opacity is {} value) builder.BeginSourceOpacityScope(value, scope);
                SvgRecordedPaint red = builder.RegisterRecordedPaint(new(new(1), 1, false, scope, SvgPaintOpacity.Opaque, scope));
                SvgRecordedPaint blue = builder.RegisterRecordedPaint(new(new(2), 1, false, scope, SvgPaintOpacity.Opaque, scope));
                builder.AddGeometry(SvgGeometry.FromRectangle(new(1, 1, 10, 10, new(255, 0, 0, 128), true, null)) with { RecordedPaint = red });
                builder.AddGeometry(SvgGeometry.FromRectangle(new(5, 5, 10, 10, new(0, 0, 255, 128), true, null)) with { RecordedPaint = blue });
                if (opacity is not null) builder.EndOpacityScope();
                SvgSceneStorage storage = builder.Build();
                using SvgRoot root = new(new(16, false), new(16, false), null, storage.Geometries, storage.Commands, false, storage);
                using SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, SvgRenderOptions.Default, rasterBudget, CancellationToken.None);
                pixels = surface.Pixels.ToArray();
            }
            Assert.Equal(0, sceneBudget.Snapshot().CurrentBytes);
            Assert.Equal(0, rasterBudget.Snapshot().CurrentBytes);
            return (pixels, rasterBudget.Snapshot().PeakBytes);
        }
    }
}
