using TextEncoding = System.Text.Encoding;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgTransformBoundaryTests
{
    public static IEnumerable<object[]> NonfiniteTransforms()
    {
        foreach (int zeroes in new[] { 39, 240 })
        foreach (string sign in new[] { "", "-" })
        foreach (string exponent in new[] { "", "e0" })
        {
            string token = sign + new string('0', zeroes) + "4" + exponent;
            foreach (string transform in new[] {
                "translate(" + token + " 0)", "translate(0 " + token + ")",
                "scale(" + token + ")", "scale(1 " + token + ")",
                "matrix(1 0 0 1 " + token + " 0)", "matrix(1 .1 0 1 " + token + " 0)",
                "rotate(" + token + ")", "rotate(10 " + token + " 16)",
                "skewX(" + token + ")", "skewY(" + token + ")",
                "translate(1 2) translate(" + token + ") scale(2)",
                "rotate(10) translate(" + token + ") scale(2)" })
                yield return [transform];
        }
    }

    [Theory]
    [MemberData(nameof(NonfiniteTransforms))]
    public void Nonfinite_native_coordinate_is_categorized_before_affine_construction(string transform)
    {
        SvgException error = Assert.Throws<SvgException>(() => SvgGeometrySyntax.ParseTransform(transform, new(1_000_000), CancellationToken.None));
        Assert.Equal(SvgErrorCode.UnsupportedFeature, error.ErrorCode);
    }

    [Theory]
    [InlineData("direct")]
    [InlineData("ancestor")]
    [InlineData("use")]
    [InlineData("leaf-use")]
    [InlineData("symbol")]
    [InlineData("pattern")]
    [InlineData("mask")]
    [InlineData("clip")]
    public void Nonfinite_transform_failures_keep_source_context_and_release_borrowed_storage(string consumer)
    {
        byte[] input = Document(consumer, new string('0', 39) + "4");
        SvgException conversion = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None));
        Assert.Equal(SvgErrorCode.UnsupportedFeature, conversion.ErrorCode);
        SvgSourceDiagnostic source = Assert.IsType<SvgSourceDiagnostic>(conversion.SourceDiagnostic);
        Assert.Equal("transform", source.AttributeName);
        Assert.Contains(new string('0', 39) + "4", source.AttributeValue, StringComparison.Ordinal);
        using MemoryStream destination = new(); destination.WriteByte(73);
        Assert.Equal(SvgErrorCode.UnsupportedFeature, Assert.Throws<SvgException>(() =>
            SvgConverter.ConvertToPng(input, destination, SvgRenderOptions.Default, CancellationToken.None)).ErrorCode);
        Assert.Equal(new byte[] { 73 }, destination.ToArray());
        RenderBudget budget = new(1_000_000);
        using (RenderBudget.BudgetReservation borrowed = budget.Reserve(73, SvgMemoryCategory.Scene))
        {
            SvgException parsing = Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None));
            Assert.Equal(SvgErrorCode.UnsupportedFeature, parsing.ErrorCode);
            Assert.Equal(73, budget.Snapshot().CurrentBytes);
            using (SvgRoot retry = SvgRootParser.Parse(Document(consumer, "4"), SvgRenderLimits.Default, budget, CancellationToken.None)) { }
            Assert.Equal(73, budget.Snapshot().CurrentBytes);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Float_validation_preserves_cancellation_work_and_authored_token_limits()
    {
        string transform = "translate(" + new string('0', 39) + "4)";
        using CancellationTokenSource cancellation = new(); cancellation.Cancel();
        Assert.Equal(cancellation.Token, Assert.ThrowsAny<OperationCanceledException>(() =>
            SvgGeometrySyntax.ParseTransform(transform, new(1_000_000), cancellation.Token)).CancellationToken);
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() =>
            SvgGeometrySyntax.ParseTransform(transform, new(1), CancellationToken.None)).ErrorCode);
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() =>
            SvgGeometrySyntax.ParseTransform("translate(" + new string('0', SvgSyntaxLimits.MaximumValueTokenCharacters) + "4)", new(1_000_000), CancellationToken.None)).ErrorCode);
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() =>
            SvgGeometrySyntax.ParseTransform(new string(' ', SvgSyntaxLimits.MaximumTransformCharacters + 1), new(1_000_000), CancellationToken.None)).ErrorCode);
        Assert.Throws<ArgumentOutOfRangeException>(() => SvgAffineTransform.FromTranslation(double.NaN, 0));
    }

    [Theory]
    [InlineData(0, "4")]
    [InlineData(38, "4")]
    [InlineData(38, "-4")]
    [InlineData(38, "4e0")]
    public void Finite_neighbor_keeps_native_rounding_and_placement(int zeroes, string coordinate)
    {
        string sign = coordinate.StartsWith('-') ? "-" : "";
        string number = sign + new string('0', zeroes) + coordinate.TrimStart('-');
        byte[] expected = SvgConverter.ConvertToPng(Document("direct", coordinate), SvgRenderOptions.Default, CancellationToken.None).ToArray();
        Assert.Equal(expected, SvgConverter.ConvertToPng(Document("direct", number), SvgRenderOptions.Default, CancellationToken.None).ToArray());
    }

    private static byte[] Document(string consumer, string number)
    {
        string transform = "translate(" + number + " 0)";
        string shape = "<path id='source' d='M4 4H24V24H4Z' fill='red'/>";
        string mapped = shape.Replace("/>", " transform='" + transform + "'/>", StringComparison.Ordinal);
        string definitions = consumer switch
        {
            "use" => shape, "leaf-use" => mapped,
            "symbol" => "<symbol id='r' viewBox='0 0 32 32'>" + mapped + "</symbol>",
            "pattern" => "<pattern id='r' patternUnits='userSpaceOnUse' width='32' height='32'>" + mapped + "</pattern>",
            "mask" => "<mask id='r' mask-type='alpha' maskUnits='userSpaceOnUse' x='0' y='0' width='32' height='32'>" + mapped + "</mask>",
            "clip" => "<clipPath id='r'>" + mapped + "</clipPath>",
            _ => ""
        };
        string body = consumer switch
        {
            "direct" => mapped, "ancestor" => "<g id='parent' transform='" + transform + "'>" + shape + "</g>",
            "use" => "<use href='#source' id='instance' transform='" + transform + "'/>", "leaf-use" => "<use href='#source'/>",
            "symbol" => "<use href='#r' width='32' height='32'/>",
            "pattern" => "<rect width='32' height='32' fill='url(#r)'/>",
            "mask" => "<rect width='32' height='32' fill='red' mask='url(#r)'/>",
            _ => "<rect width='32' height='32' fill='red' clip-path='url(#r)'/>"
        };
        return TextEncoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'><defs>" + definitions + "</defs>" + body + "</svg>");
    }
}
