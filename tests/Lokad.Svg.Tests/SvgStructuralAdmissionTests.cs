using System.Xml;
using Lokad.Svg.Parsing;

namespace Lokad.Svg.Tests;

public sealed class SvgStructuralAdmissionTests
{
    [Theory]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg'><g/></svg>", 128, 256, false)]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg'><g/></svg>", 1, 256, true)]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' a='1' b='2'/>", 128, 1, true)]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' a='1'/>", 128, 2, false)]
    public void Validates_element_structure(string element, int maximumXmlDepth, int maximumAttributesPerElement, bool throws)
    {
        using XmlReader reader = XmlReader.Create(new StringReader(element));
        Assert.True(reader.Read());
        if (element.Contains("<g/>"))
        {
            Assert.True(reader.Read());
        }

        SvgRenderLimits limits = new(1024, 16, 16, 1024, 100, 100, maximumXmlDepth, maximumAttributesPerElement);
        if (throws)
        {
            SvgException exception = Assert.Throws<SvgException>(() => SvgStructuralAdmission.ValidateElementStructure(reader, limits));

            Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode);
        }
        else
        {
            SvgStructuralAdmission.ValidateElementStructure(reader, limits);
        }
    }

    [Theory]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg'/>", false)]
    [InlineData("<not-svg/>", true)]
    [InlineData("<svg xmlns='urn:x'/>", true)]
    public void Rejects_a_non_svg_root(string element, bool throws)
    {
        using XmlReader reader = XmlReader.Create(new StringReader(element));
        Assert.True(reader.Read());
        if (throws)
        {
            SvgException exception = Assert.Throws<SvgException>(() => SvgStructuralAdmission.ThrowIfNotSvgRoot(reader));

            Assert.Equal(SvgErrorCode.MalformedInput, exception.ErrorCode);
        }
        else
        {
            SvgStructuralAdmission.ThrowIfNotSvgRoot(reader);
        }
    }
}
