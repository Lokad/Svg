using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

[Trait("Layer", "Mechanism")]
public sealed class SvgCenteredRotationTests
{
    // Independent DOM matrices and native image evidence: Research/svgweb/geometry-fixed-lengths.json.
    [Theory]
    [InlineData("rotate(3)", 0, 0)]
    [InlineData("rotate(3 96 64)", 3.4810658631093077, -4.936542023615331)]
    [InlineData("translate(.3 .4) rotate(3 96 64)", 3.78106891486712, -4.536540497736425)]
    [InlineData("scale(1.1 .9) translate(.3 .4) rotate(3 96 64)", 4.159178178167295, -4.082887387446732)]
    [InlineData("matrix(1 0 0 1 .3 .4) rotate(3 96 64)", 3.7810658750302366, -4.536542017654867)]
    [InlineData("rotate(7) translate(.3 .4) rotate(3 96 64)", 4.305747780385076, -4.0419313058209525)]
    public void Rotation_centres_preserve_the_native_translation_storage_before_matrix_promotion(string input, double expectedX, double expectedY)
    {
        SvgAffineTransform transform = SvgGeometrySyntax.ParseTransform(input, CancellationToken.None);
        // Shader/drawing matrices store these lanes as floats. Check their exact
        // bit patterns; the affine geometry model retains its double coordinates.
        Assert.Equal(BitConverter.SingleToInt32Bits((float)expectedX), BitConverter.SingleToInt32Bits((float)transform.E));
        Assert.Equal(BitConverter.SingleToInt32Bits((float)expectedY), BitConverter.SingleToInt32Bits((float)transform.F));
    }
}
