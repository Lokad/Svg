using System.Xml;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgRootSyntaxTests
{
    [Fact]
    public void Parses_root_size_and_view_box_syntax_without_presentation_resolution()
    {
        using XmlReader reader = XmlReader.Create(new StringReader("<svg width='12.5px' height='7' viewBox='1 2 3 4' preserveAspectRatio='none'/>"));
        Assert.True(reader.Read());

        SvgRootSyntax syntax = SvgRootSyntax.Parse(reader, new ParserWorkCounter(100), CancellationToken.None);

        Assert.Equal(12.5, syntax.Width?.Value);
        Assert.Equal(7, syntax.Height?.Value);
        Assert.Equal(new SvgViewBox(1, 2, 3, 4, SvgViewBoxAlignment.None, SvgViewBoxAlignment.None, SvgViewBoxFit.None), syntax.ViewBox);
    }

    [Fact]
    public void Root_and_group_composition_matches_the_recorded_browser_matrix()
    {
        // Pinned Chrome 153.0.8010.52: UAAR original-small CTM, recorded
        // before this repair in the root/group diagnosis.
        ParserWorkCounter work = new(1000);
        SvgViewBox box = Assert.IsType<SvgViewBox>(SvgGeometrySyntax.ParseViewBox("0 0 594.87943 739.14391", null, work, CancellationToken.None));
        (double sx, double sy, double tx, double ty) = SvgGeometrySyntax.MapViewBox(box, 297, 370);
        SvgAffineTransform group = SvgGeometrySyntax.ParseTransform("matrix(5.3156151 0 0 5.3156151 -1284.7008 -1868.0163)", work, CancellationToken.None);
        SvgAffineTransform composed = SvgAffineTransform.FromScaleOffset(sx, sy, tx, ty).ComposeChecked(group);
        Assert.Equal(2.6538784503936768f, (float)composed.A);
        Assert.Equal(2.6538784503936768f, (float)composed.D);
        Assert.Equal(-641.4007568359375f, (float)composed.E);
        Assert.Equal(-932.14013671875f, (float)composed.F);
    }

    [Fact]
    public void Native_view_box_operands_keep_double_scale_then_translate_arithmetic()
    {
        SvgViewBox box = Assert.IsType<SvgViewBox>(SvgGeometrySyntax.ParseViewBox("0 0 508.204 141.732", null, new ParserWorkCounter(100), CancellationToken.None));
        Assert.Equal(508.2040100097656, box.Width);
        Assert.Equal(141.73199462890625, box.Height);
        (double sx, double sy, double tx, double ty) = SvgGeometrySyntax.MapViewBox(box, 254, 71);
        Assert.Equal(0.49979928335299667, sx);
        Assert.Equal(sx, sy);
        Assert.Equal(0, tx);
        Assert.Equal(0.08122532814741135f, (float)ty);
    }

    [Theory]
    [InlineData("0 0 1e40 1", SvgErrorCode.LimitExceeded)]
    [InlineData("0 0 1e-50 1", SvgErrorCode.LimitExceeded)]
    [InlineData("0 0 -1 1", SvgErrorCode.MalformedInput)]
    [InlineData("0 0 0 1", SvgErrorCode.MalformedInput)]
    [InlineData("0 0 NaN 1", SvgErrorCode.MalformedInput)]
    [InlineData("0 0 1 1 1", SvgErrorCode.MalformedInput)]
    public void View_box_float_range_and_grammar_fail_honestly(string value, SvgErrorCode expected)
    {
        SvgException denied = Assert.Throws<SvgException>(() => SvgGeometrySyntax.ParseViewBox(value, null, new ParserWorkCounter(100), CancellationToken.None));
        Assert.Equal(expected, denied.ErrorCode);
    }

    [Fact]
    public void Native_view_box_parsing_observes_work_and_cancellation()
    {
        SvgException denied = Assert.Throws<SvgException>(() => SvgGeometrySyntax.ParseViewBox("0 0 1 1", null, new ParserWorkCounter(0), CancellationToken.None));
        Assert.Equal(SvgErrorCode.LimitExceeded, denied.ErrorCode);
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        OperationCanceledException cancelled = Assert.Throws<OperationCanceledException>(() => SvgGeometrySyntax.ParseViewBox("0 0 1 1", null, new ParserWorkCounter(100), cancellation.Token));
        Assert.Equal(cancellation.Token, cancelled.CancellationToken);
    }
}
