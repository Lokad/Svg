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
    [InlineData("bowtie-leaf")]
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
    public void Native_clip_owners_release_at_every_parser_work_denial_and_sampled_memory_denials(string content)
    {
        byte[] input = content.StartsWith('<')
            ? System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'>" + content + "</svg>")
            : content.StartsWith("path-marker:", StringComparison.Ordinal)
                ? File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "path-marker-content", content[12..] + ".svg"))
                : content.StartsWith("marker:", StringComparison.Ordinal)
                ? File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "marker-content", content[7..] + ".svg"))
                : content.StartsWith("compound:", StringComparison.Ordinal)
                ? File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "compound-clip-content", content[9..] + ".svg"))
                : File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "native-clip-winding", content + ".svg"));
        RenderBudget discovery = new(1_000_000);
        ParserWorkCounter discoveryWork = new(1_000_000);
        Assert.Empty(SvgFontRequestDiscovery.Discover(input, SvgRenderLimits.Default, discoveryWork, discovery, CancellationToken.None));
        Assert.Equal(0, discovery.Snapshot().CurrentBytes);
        for (long limit = 0; limit < discoveryWork.Used; limit++)
        {
            RenderBudget denied = new(1_000_000);
            Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgFontRequestDiscovery.Discover(input, SvgRenderLimits.Default, new(limit), denied, CancellationToken.None)).ErrorCode);
            Assert.Equal(0, denied.Snapshot().CurrentBytes);
        }
        RenderBudget measured = new(1_000_000);
        ParserWorkCounter work = new(1_000_000);
        using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, measured, null, work, CancellationToken.None))
            Assert.NotEmpty(root.Geometries.ToArray());
        Assert.Equal(0, measured.Snapshot().CurrentBytes);
        for (long limit = 0; limit < work.Used; limit++)
        {
            RenderBudget denied = new(1_000_000);
            Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, denied, null, new(limit), CancellationToken.None)).ErrorCode);
            Assert.Equal(0, denied.Snapshot().CurrentBytes);
        }
        long peak = measured.Snapshot().PeakBytes;
        for (long limit = 1; limit < peak; limit += 256) DenyMemory(limit);
        DenyMemory(peak - 1);
        RenderBudget retry = new(1_000_000);
        using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, retry, null, new(work.Used), CancellationToken.None))
        {
            SvgSceneStorage storage = root.SceneStorage ?? throw new InvalidOperationException("Missing scene storage.");
            SvgAuthoredPath[] paths = storage.MaskContents.SelectMany(mask => mask.Geometries).Select(geometry => geometry.AuthoredPath).OfType<SvgAuthoredPath>().ToArray();
            SvgAuthoredPathCommand[][] original = paths.Select(path => path.Commands.ToArray()).ToArray();
            long owned = retry.Snapshot().CurrentBytes;
            long rasterWork;
            using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, SvgRenderOptions.Default, retry, CancellationToken.None))
            {
                Assert.Contains(surface.Pixels, value => value != 0);
                rasterWork = surface.WorkUnits;
            }
            SvgRenderLimits defaults = SvgRenderLimits.Default;
            foreach (long limit in new[] { root.ParserWorkUnits + 1, (root.ParserWorkUnits + rasterWork) / 2, rasterWork - 1 })
            {
                SvgRenderLimits tight = new(defaults.MaximumAccountedBytes, defaults.MaximumInputBytes, defaults.MaximumDimension,
                    defaults.MaximumPixels, defaults.MaximumEncodedPngBytes, defaults.MaximumNodes, defaults.MaximumPathCommands,
                    checked((int)limit), defaults.MaximumXmlDepth, defaults.MaximumAttributesPerElement);
                Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() =>
                {
                    using var denied = SvgConverter.Rasterize(root, new(null, 1, SvgColor.Transparent, tight), retry, CancellationToken.None);
                }).ErrorCode);
                Assert.Equal(owned, retry.Snapshot().CurrentBytes);
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
            RenderBudget denied = new(limit);
            Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, denied, null, new(1_000_000), CancellationToken.None)).ErrorCode);
            Assert.Equal(0, denied.Snapshot().CurrentBytes);
        }
    }
    [Theory]
    [InlineData("<clipPath id='c'><path d='M3 24 Q16 0 29 24'/></clipPath><clipPath id='c'><rect width='32' height='32'/></clipPath>", SvgErrorCode.MalformedInput)]
    [InlineData("<clipPath id='c'><rect width='32' height='32'/></clipPath><clipPath id='c'><path d='M3 24 Q16 0 29 24'/></clipPath>", SvgErrorCode.MalformedInput)]
    [InlineData("<clipPath id='c'><path d='M3 24 Q16 0 29 24'/><path d='M0 0H32V32Z'/></clipPath>", SvgErrorCode.UnsupportedFeature)]
    public void Rejected_clip_definitions_release_pending_and_registered_commands(string content, SvgErrorCode error)
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'><defs>" + content + "</defs><rect width='32' height='32'/></svg>");
        RenderBudget budget = new(1_000_000);
        Assert.Equal(error, Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, null, new(1_000_000), CancellationToken.None)).ErrorCode);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        using (SvgRoot retry = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'><rect width='32' height='32'/></svg>"u8.ToArray(), SvgRenderLimits.Default, budget, null, new(1_000_000), CancellationToken.None))
            Assert.Single(retry.Geometries);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

}
