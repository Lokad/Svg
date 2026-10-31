using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;
using Lokad.Svg.Fonts;

namespace Lokad.Svg.Tests;

public sealed class SvgCurvedClipSourceTests
{
    [Theory]
    [InlineData("<defs><clipPath id='c'><path d='M3 24 Q16 0 29 24'/></clipPath></defs><g transform='rotate(12 16 16)' clip-path='url(#c)'><path d='M0 0H32V32H0Z'/></g>")]
    [InlineData("<defs><clipPath id='unused'><path d='M3 24 C3 0 29 0 29 24'/></clipPath></defs><rect width='32' height='32'/>")]
    [InlineData("<defs><clipPath><path d='M3 24 A13 13 0 0 1 29 24'/></clipPath></defs><rect width='32' height='32'/>")]
    [InlineData("<defs><clipPath id='c'><use href='#s' x='.25' y='.5'/></clipPath><path id='s' d='M3 24C3 0 29 0 29 24Z'/></defs><rect width='32' height='32' clip-path='url(#c)'/>")]
    [InlineData("<defs><clipPath id='c'><path transform='matrix(.31 0 .05 .35 780 640)' d='M180 140A80 120 0 1 1 20 140A80 120 0 1 1 180 140Z'/></clipPath></defs><g transform='translate(-800 -660)'><rect width='2000' height='2000' clip-path='url(#c)'/></g>")]
    [InlineData("<defs><clipPath id='c'><path d='M760 512C800 490 880 515 880 545L880 610H760Z'/></clipPath></defs><g transform='translate(-16 -32)'><g transform='matrix(.490589 0 -.01152337 .695881 -352.0716 -337.17108)' clip-path='url(#c)'><path transform='matrix(.245003 0 .04088223 .278334 778.0912 526.6771)' d='M180 140A80 120 0 1 1 20 140A80 120 0 1 1 180 140Z'/></g></g>")]
    [InlineData("bowtie-leaf")]
    [InlineData("<defs><filter id='b' x='-.3' y='-.25' width='1.6' height='1.5'><feGaussianBlur stdDeviation='12.342325'/></filter><clipPath id='c'><path d='M660 390C705 382 765 403 790 440L767 525H660Z'/></clipPath></defs><g transform='translate(-710 -428)'><g filter='url(#b)' opacity='.56956522'><path transform='translate(.375 0)' d='M720 402C772 396 783 468 748 471L697 516C690 520 712 458 720 402Z' fill='white' clip-path='url(#c)'/></g></g>")]
    [InlineData("bowtie-oval-strokes")]
    [InlineData("star-evenodd")]
    [InlineData("star-stylesheet-priority")]
    [InlineData("concave-leaf")]
    [InlineData("<defs><clipPath id='c'><path d='M3 24 Q16 0 29 24 M3 30Q16 27 29 30'/></clipPath></defs><rect width='32' height='32' clip-path='url(#c)'/>")]
    [InlineData("<defs><clipPath id='c'><path d='M3 24Q16 0 29 24 M3 24Q16 0 29 24 M3 24Q16 0 29 24 M3 24Q16 0 29 24 M3 24Q16 0 29 24'/></clipPath></defs><rect width='32' height='32' clip-path='url(#c)'/>")]
    [InlineData("compound:cubic-hole-evenodd")]
    [InlineData("compound:arc-ring-implicit-close")]
    [InlineData("compound:mixed-line-curve-nested")]
    [InlineData("compound:disjoint-rotated-children")]
    [InlineData("compound:nested-local-clip-definitions")]
    [InlineData("compound:percentage-affine-radial")]
    [InlineData("marker:marker-forward-cascade")]
    [InlineData("marker:marker-reversed-diagonal")]
    [InlineData("marker:marker-element-opacity")]
    [InlineData("path-marker:path-forward-mid")]
    [InlineData("path-marker:path-css-closed-auto")]
    [InlineData("path-marker:path-css-opacity")]
    [InlineData("marker-instance:instance-forward-alias")]
    [InlineData("marker-instance:instance-affine-leaf")]
    [InlineData("marker-instance:instance-marker-source-paint")]
    public void Native_clip_owners_release_at_every_parser_work_denial_and_sampled_memory_denials(string content)
    {
        byte[] input = content.StartsWith('<')
            ? System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'>" + content + "</svg>")
            : content.StartsWith("marker-instance:", StringComparison.Ordinal)
                ? File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "marker-instance-content", content[16..] + ".svg"))
                : content.StartsWith("path-marker:", StringComparison.Ordinal)
                ? File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "path-marker-content", content[12..] + ".svg"))
                : content.StartsWith("marker:", StringComparison.Ordinal)
                ? File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "marker-content", content[7..] + ".svg"))
                : content.StartsWith("compound:", StringComparison.Ordinal)
                ? File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "compound-clip-content", content[9..] + ".svg"))
                : File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "native-clip-winding", content + ".svg"));
        RenderBudget discovery = new RenderBudget(new HostMemoryBudget(1_000_000));
        Assert.Empty(SvgFontRequestDiscovery.Discover(input, SvgRenderLimits.Default, discovery, CancellationToken.None));
        Assert.Equal(0, discovery.Snapshot().CurrentBytes);
        RenderBudget measured = new RenderBudget(new HostMemoryBudget(1_000_000));
        using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, measured, null, CancellationToken.None))
            Assert.NotEmpty(root.Geometries.ToArray());
        Assert.Equal(0, measured.Snapshot().CurrentBytes);
        long peak = measured.Snapshot().PeakBytes;
        for (long limit = 1; limit < peak; limit += 256) DenyMemory(limit);
        DenyMemory(peak - 1);
        RenderBudget retry = new RenderBudget(new HostMemoryBudget(1_000_000));
        using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, retry, null, CancellationToken.None))
        {
            SvgSceneStorage storage = root.SceneStorage ?? throw new InvalidOperationException("Missing scene storage.");
            SvgAuthoredPath[] paths = storage.MaskContents.SelectMany(mask => mask.Geometries).Select(geometry => geometry.AuthoredPath).OfType<SvgAuthoredPath>().ToArray();
            SvgAuthoredPathCommand[][] original = paths.Select(path => path.Commands.ToArray()).ToArray();
            long owned = retry.Snapshot().CurrentBytes;
            using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, SvgRenderOptions.Default, retry, CancellationToken.None))
            {
                Assert.Contains(surface.Pixels, value => value != 0);
            }
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
            for (int index = 0; index < paths.Length; index++) Assert.Equal(original[index], paths[index].Commands.ToArray());
            using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, SvgRenderOptions.Default, retry, CancellationToken.None))
                Assert.Contains(surface.Pixels, value => value != 0);
            Assert.Equal(owned, retry.Snapshot().CurrentBytes);
        }
        Assert.Equal(0, retry.Snapshot().CurrentBytes);

        void DenyMemory(long limit)
        {
            RenderBudget denied = new RenderBudget(new HostMemoryBudget(limit));
            Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, denied, null, CancellationToken.None)).ErrorCode);
            Assert.Equal(0, denied.Snapshot().CurrentBytes);
        }
    }
    [Fact]
    public void Unreferenced_mixed_curve_union_preserves_pixels_and_releases_sources()
    {
        const string content = "<clipPath id='c'><path d='M3 24 Q16 0 29 24'/><path d='M0 0H32V32Z'/></clipPath>";
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'><defs>" + content + "</defs><rect width='32' height='32'/></svg>");
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(1_000_000)), rasterBudget = new RenderBudget(new HostMemoryBudget(1_000_000));
        using (SvgRoot scene = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, null, CancellationToken.None))
        using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(scene, SvgRenderOptions.Default, rasterBudget, CancellationToken.None))
        {
            Assert.Equal(32, surface.Width); Assert.Equal(32, surface.Height);
            for (int offset = 0; offset < surface.Pixels.Length; offset += 4)
            {
                Assert.Equal(0, surface.Pixels[offset]); Assert.Equal(0, surface.Pixels[offset + 1]);
                Assert.Equal(0, surface.Pixels[offset + 2]); Assert.Equal(255, surface.Pixels[offset + 3]);
            }
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes); Assert.Equal(0, rasterBudget.Snapshot().CurrentBytes);
    }

    [Theory]
    [InlineData("<clipPath id='c'><path d='M3 24 Q16 0 29 24'/></clipPath><clipPath id='c'><rect width='32' height='32'/></clipPath>", SvgErrorCode.MalformedInput)]
    [InlineData("<clipPath id='c'><rect width='32' height='32'/></clipPath><clipPath id='c'><path d='M3 24 Q16 0 29 24'/></clipPath>", SvgErrorCode.MalformedInput)]
    public void Rejected_clip_definitions_release_pending_and_registered_commands(string content, SvgErrorCode error)
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'><defs>" + content + "</defs><rect width='32' height='32'/></svg>");
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(1_000_000));
        Assert.Equal(error, Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, null, CancellationToken.None)).ErrorCode);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        using (SvgRoot retry = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'><rect width='32' height='32'/></svg>"u8.ToArray(), SvgRenderLimits.Default, budget, null, CancellationToken.None))
            Assert.Single(retry.Geometries);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

}
