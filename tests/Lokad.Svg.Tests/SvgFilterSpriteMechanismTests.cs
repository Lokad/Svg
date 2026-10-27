using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

[Trait("Layer", "Mechanism")]
public sealed class SvgFilterSpriteMechanismTests
{
    public static IEnumerable<object[]> TranslationCases()
    {
        // Nearest image AA admits the half-open four-bit interval around an
        // integer. Keep adjacent float operands and the negative boundary tie.
        foreach (float center in new[] { -4096f, -1f, 0f, 1f, 4096f })
        {
            float lower = center - 1f / 32, upper = center + 1f / 32;
            yield return [center, true];
            yield return [MathF.BitDecrement(lower), false];
            yield return [lower, true];
            yield return [MathF.BitIncrement(lower), true];
            // At zero, adding .5f rounds this adjacent operand onto the next
            // grid boundary. Preserve float-stage rounding, not real arithmetic.
            yield return [MathF.BitDecrement(upper), center != 0];
            yield return [upper, false];
            yield return [MathF.BitIncrement(upper), false];
        }
        yield return [float.NaN, false];
        yield return [float.PositiveInfinity, false];
        yield return [float.NegativeInfinity, false];
        yield return [float.MaxValue, false];
        yield return [float.MinValue, false];
    }

    [Theory]
    [MemberData(nameof(TranslationCases))]
    public void Nearest_translation_retains_four_bit_ties_and_finite_arithmetic(float offset, bool admitted)
        => Assert.Equal(admitted, SvgFilterPlayback.IsNearestSpriteTranslation(offset));
}
