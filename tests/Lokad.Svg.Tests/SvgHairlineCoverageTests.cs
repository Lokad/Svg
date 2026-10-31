using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using TextEncoding = System.Text.Encoding;
using System.Text.Json;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgHairlineCoverageTests
{
    [Fact]
    public void Containing_binary_viewport_preserves_unclipped_adjacent_pixel_rounding()
    {
        // The short rotated rectangle also has independent browser references in
        // Research/hairline-clip-rounding. Neither clip cuts its fringe pixels.
        byte[] mask = new byte[64 * 64 * 4];
        for (int index = 3; index < mask.Length; index += 4) mask[index] = 255;
        Assert.Equal(Draw([], false), Draw(mask, true));

        static byte[] Draw(ReadOnlySpan<byte> mask, bool binary)
        {
            byte[] destination = new byte[64 * 64 * 4];
            SvgHairlineCoverage walker = new(destination, 64, 64, SvgAnalyticPaint.Solid(new(255, 0, 0, 208)),
                null, SvgClipAntialiasing.None, new(mask), binary, new(new(0, 0, 64, 64), new(0, 0, 64, 64)),
                CancellationToken.None);
            SvgDevicePoint[] corners = [new(13.410023945401651, 53.04681908123335), new(19.327956946071712, 48.14011700656239),
                new(20.093880441919556, 49.06389103300366), new(14.175947441249495, 53.97059310767462)];
            for (int index = 0; index < corners.Length; index++) walker.DrawLine(corners[index], corners[(index + 1) % corners.Length]);
            return destination;
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Whole_path_clip_dispatch_preserves_opaque_bands_and_complex_region_boundaries(bool binary, bool changingBand)
    {
        const string source = "<svg xmlns='http://www.w3.org/2000/svg' width='64' height='64'><rect x='10.28125' y='33.953125' width='5.125' height='0.800000011920929' fill='none' stroke='red' stroke-width='.5' transform='matrix(0.7698124228513895,-0.6382701885750841,0.6382701885750841,0.7698124228513895,-20.64588550530315,15.789224010817065)'/></svg>";
        using SvgRoot root = SvgRootParser.Parse(TextEncoding.UTF8.GetBytes(source), SvgRenderLimits.Default, CancellationToken.None);
        SvgAuthoredPath path = Assert.IsType<SvgAuthoredPath>(Assert.Single(root.Geometries.ToArray(), geometry => geometry.AuthoredPath?.StrokeStyle is not null).AuthoredPath);
        byte[] mask = new byte[64 * 64 * 4];
        for (int index = 3; index < mask.Length; index += 4) mask[index] = 255;
        // This pixel is far from the stroke. It changes the native clip's row
        // band/region shape, so opaque pixels under the stroke do not suffice.
        if (changingBand) mask[(50 * 64) * 4 + 3] = binary ? (byte)0 : (byte)127;
        byte[] unclipped = Draw(path, [], false), actual = Draw(path, mask, binary);
        Assert.Equal(!changingBand, unclipped.AsSpan().SequenceEqual(actual));

        static byte[] Draw(SvgAuthoredPath path, ReadOnlySpan<byte> mask, bool binary)
        {
            byte[] destination = new byte[64 * 64 * 4];
            Assert.True(SvgHairlineStroke.TryRasterize(path, new(SvgAffineTransform.FromScale(1.5, 1.5), null), new(255, 0, 0, 255), null,
                null, SvgClipAntialiasing.None, new(mask), binary, destination, 64, 64, CancellationToken.None));
            return destination;
        }
    }

    [Theory]
    [InlineData(0, 0, 0, 255)]
    [InlineData(235, 28, 35, 255)]
    [InlineData(235, 28, 35, 127)]
    public void Lines_match_compiled_native_coverage_under_reversal_phase_clipping_and_subdivision(byte red, byte green, byte blue, byte alpha)
    {
        byte[] data = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData/hairline-native.csv.gz"));
        using JsonDocument metadata = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData/hairline-native.json")));
        Assert.Equal(metadata.RootElement.GetProperty("dataSha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(data)));
        using MemoryStream compressed = new(data);
        using GZipStream expanded = new(compressed, CompressionMode.Decompress);
        using StreamReader reader = new(expanded, TextEncoding.UTF8);
        Assert.Equal("id,width,height,x0,y0,x1,y1,events", reader.ReadLine());
        int count = 0;
        SvgAnalyticPaint paint = SvgAnalyticPaint.Solid(new(red, green, blue, alpha));
        while (reader.ReadLine() is string line)
        {
            string[] fields = line.Split(',');
            int width = int.Parse(fields[1], CultureInfo.InvariantCulture), height = int.Parse(fields[2], CultureInfo.InvariantCulture);
            byte[] expected = new byte[width * height * 4], actual = new byte[expected.Length];
            foreach (string entry in fields[7].Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                string[] values = entry.Split(':');
                int x = int.Parse(values[0], CultureInfo.InvariantCulture), y = int.Parse(values[1], CultureInfo.InvariantCulture);
                byte coverage = byte.Parse(values[2], CultureInfo.InvariantCulture);
                SvgAnalyticPaintKind kind = values[3] == "0" ? SvgAnalyticPaintKind.Direct : SvgAnalyticPaintKind.Pair;
                paint.Blend(expected.AsSpan((y * width + x) * 4, 4), coverage, kind);
            }
            SvgHairlineCoverage walker = new(actual, width, height, paint, null, SvgClipAntialiasing.None, CancellationToken.None);
            walker.DrawLine(new(double.Parse(fields[3], CultureInfo.InvariantCulture), double.Parse(fields[4], CultureInfo.InvariantCulture)),
                new(double.Parse(fields[5], CultureInfo.InvariantCulture), double.Parse(fields[6], CultureInfo.InvariantCulture)));
            Assert.True(expected.AsSpan().SequenceEqual(actual), fields[0]);
            count++;
        }
        Assert.Equal(112, count);
    }

    [Fact]
    public void Cancelled_long_line_preserves_pixels_and_allows_retry()
    {
        byte[] deniedPixels = new byte[1024 * 64 * 4];
        Assert.All(deniedPixels, value => Assert.Equal(0, value));
        using CancellationTokenSource cancelled = new();
        cancelled.Cancel();
        OperationCanceledException error = Assert.ThrowsAny<OperationCanceledException>(() => Draw(deniedPixels, cancelled.Token));
        Assert.Equal(cancelled.Token, error.CancellationToken);
        Array.Clear(deniedPixels);
        Draw(deniedPixels, CancellationToken.None);
        Assert.Contains(deniedPixels, value => value != 0);

        static void Draw(byte[] destination, CancellationToken token)
        {
            SvgHairlineCoverage walker = new(destination, 1024, 64, SvgAnalyticPaint.Solid(new(235, 28, 35, 127)), null, SvgClipAntialiasing.None, token);
            walker.DrawLine(new(4.25, 4.25), new(1004.25, 28.25));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Stroke_owner_preserves_explicit_closure_and_independent_width_maps_through_copy_and_transfer(bool nonScaling)
    {
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(268_435_456L));
        byte[] svg = TextEncoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='64' height='64'><path transform='scale(2)' " + (nonScaling ? "vector-effect='non-scaling-stroke' " : "") + "fill='none' stroke='red' stroke-width='.5' d='M4,4H60V60H4Z M8,8H16'/></svg>");
        using (SvgRoot root = SvgRootParser.Parse(svg, SvgRenderLimits.Default, budget, null, CancellationToken.None))
        {
            SvgAuthoredPath original = Assert.IsType<SvgAuthoredPath>(Assert.Single(root.Geometries).AuthoredPath);
            Assert.Contains(original.Commands.ToArray(), c => c.Verb == SvgAuthoredPathVerb.Close);
            Assert.Contains(original.Commands.ToArray(), c => c.Verb == SvgAuthoredPathVerb.End);
            Assert.Throws<InvalidOperationException>(() => original.Prepare(SvgAffineTransform.Identity, false, budget, CancellationToken.None));
            using SvgAuthoredPath copy = original.Copy(SvgAffineTransform.FromScale(.5, .5), budget, CancellationToken.None);
            Assert.Equal(nonScaling ? .5 : 1, Assert.IsType<SvgAuthoredStrokeStyle>(copy.StrokeStyle).WidthMap.A);
            using SvgAuthoredPath transferred = copy.Transfer();
            Assert.Empty(copy.Commands.ToArray());
            Assert.Equal(original.Commands.ToArray(), transferred.Commands.ToArray());
            Assert.Equal(.5, Assert.IsType<SvgAuthoredStrokeStyle>(transferred.StrokeStyle).Width);
            long retained = budget.Snapshot().CurrentBytes;
            SvgException overflow = Assert.Throws<SvgException>(() => original.Copy(SvgAffineTransform.FromScale(float.MaxValue, float.MaxValue), budget, CancellationToken.None));
            Assert.Equal(SvgErrorCode.UnsupportedFeature, overflow.ErrorCode);
            Assert.Equal(retained, budget.Snapshot().CurrentBytes);
            using (budget.Reserve(268_435_456L - retained - 100, SvgMemoryCategory.Surface))
            {
                long padded = budget.Snapshot().CurrentBytes;
                SvgException denied = Assert.Throws<SvgException>(() => original.Copy(SvgAffineTransform.Identity, budget, CancellationToken.None));
                Assert.Equal(SvgErrorCode.LimitExceeded, denied.ErrorCode);
                Assert.Equal(padded, budget.Snapshot().CurrentBytes);
            }
            using SvgAuthoredPath retry = original.Copy(SvgAffineTransform.Identity, budget, CancellationToken.None);
            Assert.Equal(original.Commands.ToArray(), retry.Commands.ToArray());
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }
}
