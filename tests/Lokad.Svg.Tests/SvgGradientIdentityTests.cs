using System.Xml;
using Lokad.Svg.Parsing;

namespace Lokad.Svg.Tests;

[Trait("Layer", "Mechanism")]
public sealed class SvgGradientIdentityTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(SvgSyntaxLimits.MaximumIdentifierCharacters)]
    [InlineData(SvgSyntaxLimits.MaximumIdentifierCharacters + 1)]
    public void Stop_ids_use_the_shared_identifier_bound_without_changing_stop_values(int length)
    {
        using StringReader input = new("<stop xmlns='http://www.w3.org/2000/svg' id='" + new string('a', length)
            + "' offset='.5' stop-color='#e04020' stop-opacity='.27'/>");
        using XmlReader reader = XmlReader.Create(input);
        Assert.True(reader.Read());
        if (length > SvgSyntaxLimits.MaximumIdentifierCharacters)
        {
            SvgException error = Assert.Throws<SvgException>(() => SvgElementAttributePolicy.ValidateGradientStop(reader, CancellationToken.None));
            Assert.Equal(SvgErrorCode.LimitExceeded, error.ErrorCode);
            return;
        }
        SvgElementAttributePolicy.ValidateGradientStop(reader, CancellationToken.None);
        SvgGradientStop stop = SvgGradientSyntax.ParseStop(reader, new Dictionary<string, SvgColor>(), CancellationToken.None);
        Assert.Equal(.5, stop.Offset);
        Assert.Equal(new SvgColor(224, 64, 32, 69), stop.Color);
        Assert.Equal(.27f, stop.Alpha);
    }
}
