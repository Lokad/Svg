using System.Text;
using System.Globalization;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgCompoundClipAdmissionTests
{
    [Theory]
    [InlineData("width='-1%'", SvgErrorCode.MalformedInput)]
    [InlineData("height='-1%'", SvgErrorCode.MalformedInput)]
    [InlineData("rx='-1%'", SvgErrorCode.MalformedInput)]
    [InlineData("width='NaN%'", SvgErrorCode.MalformedInput)]
    [InlineData("height='1e100%'", SvgErrorCode.LimitExceeded)]
    public void Invalid_percentage_rectangles_release_parser_owners(string attributes, SvgErrorCode error)
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'><rect " + attributes + "/></svg>");
        RenderBudget budget = new(1_000_000);
        Assert.Equal(error, Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, null, new(1_000_000), CancellationToken.None)).ErrorCode);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Percentage_rectangles_require_a_known_user_viewport()
    {
        Assert.Equal(SvgErrorCode.UnsupportedFeature, Assert.Throws<SvgException>(() => SvgGeometrySyntax.ParseViewportCoordinate("50%", null, SvgViewportCoordinateKind.Rectangle)).ErrorCode);
    }

    [Theory]
    [InlineData("display='none'")]
    [InlineData("visibility='hidden'")]
    [InlineData("width='0' height='32'")]
    [InlineData("width='32' height='0'")]
    public void Unpainted_affine_shader_rectangles_produce_an_empty_surface(string attributes)
    {
        string start = "<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'>";
        byte[] empty = SvgConverter.ConvertToPng(System.Text.Encoding.UTF8.GetBytes(start + "</svg>"), SvgRenderOptions.Default, CancellationToken.None).ToArray();
        string dimensions = attributes.StartsWith("width", StringComparison.Ordinal) ? "" : "width='100%' height='100%' ";
        byte[] input = System.Text.Encoding.UTF8.GetBytes(start + "<defs><radialGradient id='p'><stop stop-color='red'/><stop offset='1' stop-color='blue'/></radialGradient></defs><rect " + dimensions + attributes + " transform='rotate(17 16 16)' fill='url(#p)'/></svg>");
        Assert.Equal(empty, SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None).ToArray());
    }

    [Theory]
    [InlineData(42, false)]
    [InlineData(43, true)]
    public void Disjoint_child_union_preserves_native_path_and_mask_routes(int count, bool maskFallback)
    {
        StringBuilder input = new("<svg xmlns='http://www.w3.org/2000/svg' width='256' height='32'><defs><clipPath id='c'>");
        for (int index = 0; index < count; index++)
            input.Append("<path d='M0 0L2 0L1 2Z' transform='translate(").Append(index * 5).Append(" 8) rotate(3)'/>");
        input.Append("</clipPath></defs><rect width='256' height='32' clip-path='url(#c)'/></svg>");
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(input.ToString());
        RenderBudget budget = new(8_000_000);
        using (SvgRoot root = SvgRootParser.Parse(bytes, SvgRenderLimits.Default, budget, null, new(1_000_000), CancellationToken.None))
        {
            Assert.NotEmpty(root.Geometries.ToArray());
            SvgSceneStorage storage = Assert.IsType<SvgSceneStorage>(root.SceneStorage);
            Assert.Equal(maskFallback, storage.MaskContents.Any(content => content.VectorContent is not null));
            RenderBudget rasterBudget = new(8_000_000);
            using (SvgConverter.RasterizedSurface output = SvgConverter.Rasterize(root, SvgRenderOptions.Default, rasterBudget, CancellationToken.None))
            {
                Assert.Equal(256, output.Width);
                Assert.Equal(32, output.Height);
                Assert.Contains(output.Pixels, value => value != 0);
            }
            Assert.Equal(0, rasterBudget.Snapshot().CurrentBytes);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Theory]
    [InlineData(36)]
    [InlineData(37)]
    [InlineData(38)]
    public void Union_normalization_preserves_pixels_across_authored_closing_endpoints(int height)
    {
        string header = "<svg xmlns='http://www.w3.org/2000/svg' width='130' height='"
            + height.ToString(CultureInfo.InvariantCulture) + "' viewBox='0 0 100 "
            + (height / 1.3).ToString("R", CultureInfo.InvariantCulture) + "'>";
        byte[] implicitClose = SvgConverter.ConvertToPng(System.Text.Encoding.UTF8.GetBytes(CreateSvg(false)), SvgRenderOptions.Default, CancellationToken.None).ToArray();
        byte[] explicitClose = SvgConverter.ConvertToPng(System.Text.Encoding.UTF8.GetBytes(CreateSvg(true)), SvgRenderOptions.Default, CancellationToken.None).ToArray();
        byte[] empty = SvgConverter.ConvertToPng(System.Text.Encoding.UTF8.GetBytes(header + "</svg>"), SvgRenderOptions.Default, CancellationToken.None).ToArray();
        Assert.NotEqual(empty, implicitClose);
        Assert.Equal(implicitClose, explicitClose);

        string CreateSvg(bool explicitEndpoint)
        {
            StringBuilder input = new(header);
            input.Append("<defs><clipPath id='ball'><circle cx='50' cy='50' r='50'/></clipPath><clipPath id='c'>");
            for (int index = 0; index < 17; index++)
            {
                input.Append("<path d='M50 29.5L52 29.5L50 37.5L48 37.5");
                if (explicitEndpoint) input.Append("L50 29.5");
                input.Append("Z' transform='rotate(").Append((40.5 - index * 5).ToString(CultureInfo.InvariantCulture)).Append(" 50 -30)'/>");
            }
            input.Append("</clipPath></defs><g clip-path='url(#ball)' transform='rotate(-23.4 50 50)'><rect width='100' height='100' fill='white' clip-path='url(#c)'/></g></svg>");
            return input.ToString();
        }
    }

    [Fact]
    public void Overlapping_transformed_children_preserve_coverage_and_release_owners()
    {
        byte[] input = "<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'><defs><clipPath id='c'><path d='M3 3H22V22H3Z' transform='rotate(3 16 16)'/><path d='M12 12V29H29V12Z'/></clipPath></defs><rect width='32' height='32' clip-path='url(#c)'/></svg>"u8.ToArray();
        RenderBudget budget = new(1_000_000);
        byte[] expected;
        using (SvgRoot scene = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, null, new(1_000_000), CancellationToken.None))
        using (SvgConverter.RasterizedSurface output = SvgConverter.Rasterize(scene, SvgRenderOptions.Default, budget, CancellationToken.None))
            expected = output.Pixels.ToArray();
        Assert.Contains(expected, value => value != 0);
        byte[] reversed = System.Text.Encoding.UTF8.GetBytes(System.Text.Encoding.UTF8.GetString(input)
            .Replace("M12 12V29H29V12Z", "M12 12H29V29H12Z", StringComparison.Ordinal));
        using (SvgRoot scene = SvgRootParser.Parse(reversed, SvgRenderLimits.Default, budget, CancellationToken.None))
        using (SvgConverter.RasterizedSurface output = SvgConverter.Rasterize(scene, SvgRenderOptions.Default, budget, CancellationToken.None))
            Assert.Equal(expected, output.Pixels);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }
}
