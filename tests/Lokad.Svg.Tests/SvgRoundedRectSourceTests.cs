using System.Globalization;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgRoundedRectSourceTests
{
    [Theory]
    [InlineData("rotate(17 20 16)", "fill='#2168ad'")]
    [InlineData("matrix(1 .25 -.3 .8 8 2)", "fill='#2168ad' stroke='#ab7128' stroke-width='2'")]
    public void Affine_rounded_sources_preserve_conics_instances_and_material_cleanup(string transform, string paint)
    {
        string source = "<rect id='source' x='4' y='6' width='24' height='18' rx='5' ry='3' "
            + paint + " transform='" + transform + "'/>";
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='48' height='40'>" + source + "</svg>");
        byte[] instanced = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='48' height='40'><defs>" + source + "</defs><use href='#source'/></svg>");
        RenderBudget budget = new(1_000_000);
        ParserWorkCounter work = new(1_000_000);
        using (SvgRoot scene = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, null, work, CancellationToken.None))
        {
            SvgAuthoredPath path = Assert.IsType<SvgAuthoredPath>(scene.Geometries[0].AuthoredPath);
            Assert.True(path.IsKnownConvex);
            Assert.Equal(4, path.Commands.ToArray().Count(static command => command.Verb == SvgAuthoredPathVerb.Conic));
            Assert.Equal(SvgGeometrySyntax.ParseTransform(transform, new(1_000_000), CancellationToken.None), path.SceneMap);
            Assert.Equal(SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None).ToArray(),
                SvgConverter.ConvertToPng(instanced, SvgRenderOptions.Default, CancellationToken.None).ToArray());
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        RenderBudget denied = new(budget.Snapshot().PeakBytes / 2);
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() =>
            SvgRootParser.Parse(input, SvgRenderLimits.Default, denied, null, new(1_000_000), CancellationToken.None)).ErrorCode);
        Assert.Equal(0, denied.Snapshot().CurrentBytes);
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() =>
            SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, null, new(work.Used / 2), CancellationToken.None)).ErrorCode);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Theory]
    [InlineData(5, 3, 10, 4)]
    [InlineData(12.75, 3, 10, 4)]
    [InlineData(5, 9.5, 10, 4)]
    [InlineData(12.75, 9.5, 6, 4)]
    [InlineData(0, 3, 5, 0)]
    public void Source_shapes_preserve_native_convexity_and_closed_conic_verbs(double rx, double ry, int commandCount, int conicCount)
    {
        string corners = "rx='" + rx.ToString("R", CultureInfo.InvariantCulture) + "' ry='" + ry.ToString("R", CultureInfo.InvariantCulture) + "'";
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='41' height='35'>"
            + "<rect x='4.25' y='6.5' width='25.5' height='19' " + corners + " fill='#2168ad'/></svg>");
        RenderBudget budget = new(1_000_000);
        using (SvgRoot scene = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, null, new(1_000_000), CancellationToken.None))
        {
            SvgAuthoredPath source = Assert.IsType<SvgAuthoredPath>(Assert.Single(scene.Geometries).AuthoredPath);
            Assert.True(source.IsKnownConvex);
            Assert.Equal(commandCount, source.Commands.Length);
            Assert.Equal(SvgAuthoredPathVerb.Move, source.Commands[0].Verb);
            Assert.Equal(SvgAuthoredPathVerb.Close, source.Commands[^1].Verb);
            Assert.Equal(conicCount, source.Commands.ToArray().Count(static command => command.Verb == SvgAuthoredPathVerb.Conic));
            foreach (SvgAuthoredPathCommand command in source.Commands)
                if (command.Verb == SvgAuthoredPathVerb.Conic) Assert.Equal((double)SvgRationalQuadratic.QuarterCircleWeight, command.Third.X);
            using SvgPreparedPathContours prepared = source.PrepareSource(SvgAffineTransform.Identity, false, budget, new(1_000_000), CancellationToken.None);
            Assert.True(prepared.IsKnownConvex);
            Assert.Equal(conicCount, prepared.QuadSpans.Length);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Copies_and_transfers_preserve_shape_classification_and_release_both_stroke_owners(bool stroke)
    {
        RenderBudget sceneBudget = new(1_000_000), copyBudget = new(1_000_000), prepareBudget = new(1_000_000);
        SvgRoot scene = SvgRootParser.Parse(Input(stroke), SvgRenderLimits.Default, sceneBudget, null, new(1_000_000), CancellationToken.None);
        try
        {
            SvgAuthoredPath source = Assert.IsType<SvgAuthoredPath>(scene.Geometries.Single(static geometry => geometry.AuthoredPath is not null).AuthoredPath);
            Assert.Equal(!stroke, source.IsKnownConvex);
            SvgAuthoredPathCommand[] commands = source.Commands.ToArray();
            SvgAffineTransform placement = new(-1.125, 0, 0, 1.125, 42.375, -.625);
            using SvgAuthoredPath copied = source.Copy(placement, copyBudget, new(1_000_000), CancellationToken.None);
            using SvgAuthoredPath transferred = copied.Transfer();
            copied.Dispose();
            scene.Dispose();
            Assert.Equal(0, sceneBudget.Snapshot().CurrentBytes);
            Assert.Equal(commands, transferred.Commands.ToArray());
            Assert.Equal(!stroke, transferred.IsKnownConvex);
            Assert.Equal(placement.ComposeChecked(source.SceneMap), transferred.SceneMap);
            SvgAuthoredPath filled = stroke ? Assert.IsType<SvgAuthoredPath>(transferred.StrokeOutline) : transferred;
            using SvgPreparedPathContours prepared = filled.PrepareSource(SvgAffineTransform.Identity, false, prepareBudget, new(1_000_000), CancellationToken.None);
            Assert.Equal(!stroke, prepared.IsKnownConvex);
            Assert.NotEmpty(prepared.QuadSpans);
        }
        finally { scene.Dispose(); }
        Assert.Equal(0, copyBudget.Snapshot().CurrentBytes);
        Assert.Equal(0, prepareBudget.Snapshot().CurrentBytes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Partial_source_construction_releases_storage_after_memory_work_or_cancellation_denial(bool stroke)
    {
        RenderBudget measured = new(1_000_000);
        ParserWorkCounter work = new(1_000_000);
        using (SvgRootParser.Parse(Input(stroke), SvgRenderLimits.Default, measured, null, work, CancellationToken.None)) { }
        Assert.Equal(0, measured.Snapshot().CurrentBytes);
        RenderBudget tight = new(measured.Snapshot().PeakBytes - 1);
        SvgException memory = Assert.Throws<SvgException>(() => SvgRootParser.Parse(Input(stroke), SvgRenderLimits.Default, tight, null, new(1_000_000), CancellationToken.None));
        Assert.Equal(SvgErrorCode.LimitExceeded, memory.ErrorCode);
        Assert.Equal(0, tight.Snapshot().CurrentBytes);
        RenderBudget workBudget = new(1_000_000);
        SvgException denied = Assert.Throws<SvgException>(() => SvgRootParser.Parse(Input(stroke), SvgRenderLimits.Default, workBudget, null, new(work.Used - 1), CancellationToken.None));
        Assert.Equal(SvgErrorCode.LimitExceeded, denied.ErrorCode);
        Assert.Equal(0, workBudget.Snapshot().CurrentBytes);
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        OperationCanceledException canceled = Assert.Throws<OperationCanceledException>(() => SvgRootParser.Parse(Input(stroke), SvgRenderLimits.Default, workBudget, null, new(1_000_000), cancellation.Token));
        Assert.Equal(cancellation.Token, canceled.CancellationToken);
        Assert.Equal(0, workBudget.Snapshot().CurrentBytes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Partial_source_copy_and_replay_leave_no_owned_storage_on_denial(bool stroke)
    {
        RenderBudget sceneBudget = new(1_000_000), measured = new(1_000_000);
        using (SvgRoot scene = SvgRootParser.Parse(Input(stroke), SvgRenderLimits.Default, sceneBudget, null, new(1_000_000), CancellationToken.None))
        {
            SvgAuthoredPath source = Assert.IsType<SvgAuthoredPath>(scene.Geometries.Single(static geometry => geometry.AuthoredPath is not null).AuthoredPath);
            ParserWorkCounter work = new(1_000_000);
            using (source.Copy(SvgAffineTransform.Identity, measured, work, CancellationToken.None)) { }
            RenderBudget tight = new(measured.Snapshot().PeakBytes - 1);
            Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => source.Copy(SvgAffineTransform.Identity, tight, new(1_000_000), CancellationToken.None)).ErrorCode);
            Assert.Equal(0, tight.Snapshot().CurrentBytes);
            RenderBudget denied = new(1_000_000);
            Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => source.Copy(SvgAffineTransform.Identity, denied, new(work.Used - 1), CancellationToken.None)).ErrorCode);
            Assert.Equal(0, denied.Snapshot().CurrentBytes);
            SvgAuthoredPath filled = stroke ? Assert.IsType<SvgAuthoredPath>(source.StrokeOutline) : source;
            Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => filled.PrepareSource(SvgAffineTransform.Identity, false, denied, new(2), CancellationToken.None)).ErrorCode);
            Assert.Equal(0, denied.Snapshot().CurrentBytes);
            using CancellationTokenSource cancellation = new();
            cancellation.Cancel();
            OperationCanceledException canceled = Assert.Throws<OperationCanceledException>(() => filled.PrepareSource(SvgAffineTransform.Identity, false, denied, new(1_000_000), cancellation.Token));
            Assert.Equal(cancellation.Token, canceled.CancellationToken);
            Assert.Equal(0, denied.Snapshot().CurrentBytes);
        }
        Assert.Equal(0, sceneBudget.Snapshot().CurrentBytes);
        Assert.Equal(0, measured.Snapshot().CurrentBytes);
    }

    [Theory]
    [InlineData("fill='none' stroke='rgba(86,137,193,0.37)' stroke-width='3'")]
    [InlineData("fill='#2168ad' stroke='#ab7128' stroke-width='3'")]
    public void Every_fill_stroke_construction_work_boundary_releases_unpublished_geometry(string paint)
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='41' height='35'>"
            + "<rect x='4.25' y='6.5' width='25.5' height='19' rx='5' ry='9.5' " + paint + "/></svg>");
        RenderBudget measured = new(1_000_000);
        ParserWorkCounter complete = new(1_000_000);
        using (SvgRootParser.Parse(input, SvgRenderLimits.Default, measured, null, complete, CancellationToken.None)) { }
        Assert.Equal(0, measured.Snapshot().CurrentBytes);
        for (long limit = 0; limit < complete.Used; limit++)
        {
            RenderBudget denied = new(1_000_000);
            SvgException exception = Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, denied, null, new(limit), CancellationToken.None));
            Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode);
            Assert.Equal(0, denied.Snapshot().CurrentBytes);
        }
        for (long limit = 256; limit < measured.Snapshot().PeakBytes; limit += 256)
        {
            RenderBudget denied = new(limit);
            SvgException exception = Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, denied, null, new(1_000_000), CancellationToken.None));
            Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode);
            Assert.Equal(0, denied.Snapshot().CurrentBytes);
        }
    }

    [Theory]
    [InlineData("fill='#2168ad' stroke='rgba(86,137,193,0.37)' stroke-width='3'")]
    [InlineData("fill='none' stroke='#2168ad' stroke-width='3' stroke-dasharray='2 3'")]
    public void Rounded_stroke_admission_and_denial_release_fill_and_stroke_owners(string paint)
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='41' height='35'>"
            + "<rect x='4.25' y='6.5' width='25.5' height='19' rx='5' ry='9.5' " + paint + "/></svg>");
        RenderBudget budget = new(1_000_000);
        using (SvgRoot scene = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, null, new(1_000_000), CancellationToken.None))
            Assert.NotEmpty(scene.Geometries);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        RenderBudget denied = new(budget.Snapshot().PeakBytes / 2);
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() =>
            SvgRootParser.Parse(input, SvgRenderLimits.Default, denied, null, new(1_000_000), CancellationToken.None)).ErrorCode);
        Assert.Equal(0, denied.Snapshot().CurrentBytes);
    }

    private static byte[] Input(bool stroke) => System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='41' height='35'>"
        + "<rect x='4.25' y='6.5' width='25.5' height='19' rx='5' ry='9.5' "
        + (stroke ? "fill='none' stroke='#2168ad' stroke-width='2'" : "fill='#2168ad'") + "/></svg>");
}
