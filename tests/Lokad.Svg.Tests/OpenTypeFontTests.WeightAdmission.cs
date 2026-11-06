using System.Globalization;
using Lokad.Svg.Fonts;
using Lokad.Svg.Parsing;

namespace Lokad.Svg.Tests;

public sealed partial class OpenTypeFontTests
{
    public enum FontWeightDeclaration { Attribute, Inline, Rule }
    public enum FontWeightScope { Text, Group, Symbol, Pattern }

    [Theory]
    [Trait("Layer", "Mechanism")]
    [InlineData(-1, false)]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(400, true)]
    [InlineData(500, true)]
    [InlineData(700, true)]
    [InlineData(1000, true)]
    [InlineData(1001, false)]
    [InlineData(2000, false)]
    [InlineData(int.MaxValue, false)]
    public void Svg_font_weight_scalar_boundaries_preserve_the_supported_range(int weight, bool admitted)
    {
        string value = weight.ToString(CultureInfo.InvariantCulture);
        if (admitted) Assert.Equal(weight, SvgTextFontSyntax.ParseWeight(value));
        else Assert.Equal(SvgErrorCode.UnsupportedFeature, Assert.Throws<SvgException>(() => SvgTextFontSyntax.ParseWeight(value)).ErrorCode);
    }

    public static IEnumerable<object[]> RepresentativeFontWeightContexts() => FontWeightContexts(false);
    public static IEnumerable<object[]> ExtendedFontWeightContexts() => FontWeightContexts(true);

    private static IEnumerable<object[]> FontWeightContexts(bool research)
    {
        foreach ((int weight, bool admitted) in new (int, bool)[]
        {
            (-1, false), (0, false), (1, true), (400, true), (500, true),
            (700, true), (1000, true), (1001, false), (2000, false), (int.MaxValue, false)
        })
        {
            if (!research && weight is not (1000 or 1001)) continue;
            if (research && weight is 1000 or 1001) continue;
            foreach (FontWeightDeclaration declaration in Enum.GetValues<FontWeightDeclaration>())
            foreach (FontWeightScope scope in Enum.GetValues<FontWeightScope>())
                yield return [weight, admitted, declaration, scope];
        }
    }

    [Theory]
    [Trait("Layer", "Contract")]
    [MemberData(nameof(RepresentativeFontWeightContexts))]
    public Task Font_weight_contexts_validate_before_acquisition_and_preserve_prepared_selection(
        int weight, bool admitted, FontWeightDeclaration declaration, FontWeightScope scope)
        => AssertFontWeightContextAsync(weight, admitted, declaration, scope);

    [Theory]
    [Trait("Layer", "Contract")]
    [Trait("Category", "Research")]
    [MemberData(nameof(ExtendedFontWeightContexts))]
    public Task Extended_font_weight_contexts_preserve_original_numeric_combinations(
        int weight, bool admitted, FontWeightDeclaration declaration, FontWeightScope scope)
        => AssertFontWeightContextAsync(weight, admitted, declaration, scope);

    [Fact]
    [Trait("Layer", "Contract")]
    public void Literal_public_font_requests_keep_their_existing_positive_weight_contract()
    {
        (var catalog, _, _) = CreateExamplePack(CreateTriangleFont());
        SvgFontFaceRequest request = new("Example", int.MaxValue, false);
        Assert.Equal(int.MaxValue, request.Weight);
        Assert.Equal(400, catalog.Resolve(request).Face.Weight);
    }

    private static async Task AssertFontWeightContextAsync(int weight, bool admitted, FontWeightDeclaration declaration, FontWeightScope scope)
    {
        (var catalog, var provider, var source) = CreateExamplePack(CreateTriangleFont());
        byte[] input = Input(weight, declaration, scope);
        if (!admitted)
        {
            Assert.Equal(SvgErrorCode.UnsupportedFeature, Assert.Throws<SvgException>(() => Render(input)).ErrorCode);
            SvgException failure = await Assert.ThrowsAsync<SvgException>(async () =>
            {
                using SvgPreparedDocument document = await SvgPreparedDocument.CreateAsync(input, catalog, provider, SvgRenderOptions.Default, CancellationToken.None);
            });
            Assert.Equal(SvgErrorCode.UnsupportedFeature, failure.ErrorCode);
            Assert.Equal(0, provider.RequestCount);
            Assert.Equal(0, source.RequestCount);
            return;
        }
        Assert.Equal(weight, Assert.Single(SvgFontRequestDiscovery.Discover(input, CancellationToken.None)).Weight);
        using SvgFontSnapshot fonts = await SvgFontSnapshot.CreateAsync(input, catalog, provider, CancellationToken.None);
        SvgPreparedFont font = Assert.Single(fonts.Fonts);
        Assert.Equal(weight, font.Resolution.Resolution.Request.Weight);
        Assert.Equal(400, font.Resolution.Resolution.Face.Weight);
        Assert.Equal(1, provider.RequestCount);
        Assert.Equal(1, source.RequestCount);
        byte[] expected = SvgConverter.ConvertToPng(input, new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, fonts), CancellationToken.None).ToArray();
        Assert.NotEqual(Render(Document("")), expected);
        using SvgPreparedDocument prepared = await SvgPreparedDocument.CreateAsync(input, catalog, provider, SvgRenderOptions.Default, CancellationToken.None);
        Assert.Equal(expected, prepared.ConvertToPng(CancellationToken.None).ToArray());
        Assert.Equal(2, provider.RequestCount);
        Assert.Equal(2, source.RequestCount);

        static byte[] Input(int weight, FontWeightDeclaration location, FontWeightScope scope)
        {
            string value = weight.ToString(CultureInfo.InvariantCulture);
            string attribute = location switch
            {
                FontWeightDeclaration.Attribute => "font-weight='" + value + "'",
                FontWeightDeclaration.Inline => "style='font-weight:" + value + "'",
                FontWeightDeclaration.Rule => "",
                _ => throw new ArgumentOutOfRangeException(nameof(location))
            };
            string text = "<text x='0' y='7' font-family='Example' font-size='200' " + (scope == FontWeightScope.Group ? "" : "id='t' " + attribute) + ">A</text>";
            text = scope switch
            {
                FontWeightScope.Text => text,
                FontWeightScope.Group => "<g id='t' " + attribute + ">" + text + "</g>",
                FontWeightScope.Symbol => "<defs><symbol id='s'>" + text + "</symbol></defs><use href='#s'/>",
                FontWeightScope.Pattern => "<defs><pattern id='p' width='8' height='8' patternUnits='userSpaceOnUse'>" + text + "</pattern></defs><rect width='8' height='8' fill='url(#p)'/>",
                _ => throw new ArgumentOutOfRangeException(nameof(scope))
            };
            if (location == FontWeightDeclaration.Rule) text = "<style>#t{font-weight:" + value + "}</style>" + text;
            return Document(text);
        }
    }
}
