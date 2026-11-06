using TextEncoding = System.Text.Encoding;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgGradientReflectionTests
{
    [Theory]
    [Trait("Layer", "Mechanism")]
    [InlineData("matrix(-1 0 0 1 32 0)", -1, 1, 1, 0)]
    [InlineData("matrix(1 0 0 -1 0 32)", 1, -1, 0, 1)]
    [InlineData("matrix(-1 0 0 -1 32 32)", -1, -1, 1, 1)]
    public void Object_box_reflection_preserves_factory_endpoints_and_the_shared_definition(
        string transform, double scaleX, double scaleY, double offsetX, double offsetY)
    {
        byte[] input = TextEncoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'><defs><linearGradient id='p' x2='.0000306' spreadMethod='repeat'><stop stop-color='red'/><stop offset='1' stop-color='blue'/></linearGradient></defs><rect x='4' y='4' width='20' height='20' fill='url(#p)'/><rect x='4' y='4' width='20' height='20' fill='url(#p)' transform='" + transform + "'/></svg>");
        using SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, CancellationToken.None);
        Assert.Equal(2, root.Geometries.Length);
        SvgLinearGradient original = root.Geometries[0].Gradient.Gradient;
        SvgLinearGradient placed = root.Geometries[1].Gradient.Gradient;
        SvgLinearPaintMap source = Assert.IsType<SvgLinearPaintMap>(original.SourcePaint);
        SvgLinearPaintMap reflected = Assert.IsType<SvgLinearPaintMap>(placed.SourcePaint);
        Assert.NotSame(source, reflected);
        Assert.Equal(SvgGradientTransform.Identity, source.Data.Local);
        Assert.Equal(new SvgGradientTransform(scaleX, 0, 0, scaleY, offsetX, offsetY), reflected.Data.Local);
        Assert.Equal(source.Data.X1, reflected.Data.X1);
        Assert.Equal(source.Data.Y1, reflected.Data.Y1);
        Assert.Equal(source.Data.X2, reflected.Data.X2);
        Assert.Equal(source.Data.Y2, reflected.Data.Y2);
        Assert.False(SvgShaderCoordinates.IsConstantFactory(placed));
    }

    [Fact]
    [Trait("Layer", "Contract")]
    public void Reflected_maps_release_after_refusal_cancellation_and_retry()
    {
        byte[] input = "<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'><defs><linearGradient id='p' x2='.0000306' spreadMethod='repeat'><stop stop-color='red'/><stop offset='1' stop-color='blue'/></linearGradient></defs><rect x='4' y='4' width='20' height='20' fill='url(#p)'/><rect x='4' y='4' width='20' height='20' fill='url(#p)' transform='matrix(-1 0 0 1 32 0)'/></svg>"u8.ToArray();
        SvgParseOwnershipAssertions.PreservesBorrowedStorageOnDenialCancellationAndRetry(input);
    }
}
