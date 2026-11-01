using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

[Trait("Layer", "Mechanism")]
public sealed class SvgFilterSourceMapTests
{
    private static readonly SvgAffineTransform Source = new(.800000011920929, .24000000953674316,
        -.15000000596046448, .9000000357627869, 55, 40);
    private static readonly SvgAffineTransform Layer = SvgAffineTransform.FromScale(.8352245688438416, .9124143719673157);
    private static readonly SvgAffineTransform Placement = new(.9943149469169793, -.28965535152134353,
        .16571915781949656, .9655178144255843, -61.316088393213725, -22.689668243349484);

    [Fact]
    public void The_original_binding_retains_the_exact_recorded_layer_map()
    {
        SvgConverter.SvgFilterSourceMap relation = new(Source, Layer, Placement);
        Assert.NotEqual(Layer, Placement.ComposeChecked(Source));
        Assert.Equal(Layer, relation.Place(Source, Placement));
    }

    [Fact]
    public void A_translated_child_retains_the_known_linear_map_and_composed_translation()
    {
        SvgConverter.SvgFilterSourceMap relation = new(Source, Layer, Placement);
        SvgAffineTransform child = Source with { E = 59.71250005811453, F = 44.48500017821789 };
        SvgAffineTransform composed = Placement.ComposeChecked(child);
        Assert.NotEqual(0, composed.B);
        Assert.Equal(new(Layer.A, Layer.B, Layer.C, Layer.D, composed.E, composed.F), relation.Place(child, Placement));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void A_distinct_linear_map_is_composed_without_snapping_even_one_ulp(int coefficient)
    {
        SvgAffineTransform child = coefficient switch
        {
            0 => Source with { A = Math.BitIncrement(Source.A) },
            1 => Source with { B = Math.BitIncrement(Source.B) },
            2 => Source with { C = Math.BitIncrement(Source.C) },
            3 => Source with { D = Math.BitIncrement(Source.D) },
            _ => throw new ArgumentOutOfRangeException(nameof(coefficient)),
        };
        SvgConverter.SvgFilterSourceMap relation = new(Source, Layer, Placement);
        Assert.Equal(Placement.ComposeChecked(child), relation.Place(child, Placement));
    }
}
