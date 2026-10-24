using Lokad.Svg.Fonts;

namespace Lokad.Svg.Tests;

public sealed partial class OpenTypeFontTests
{
    public static IEnumerable<object[]> LineMarkerMetrics()
    {
        foreach (string consumer in new[] { "direct", "use", "symbol", "pattern" })
        foreach (bool hidden in new[] { false, true })
        foreach (bool forward in new[] { false, true })
            yield return [consumer, hidden, forward];
    }

    [Theory]
    [MemberData(nameof(LineMarkerMetrics))]
    public async Task Stroke_free_line_markers_prepare_their_own_ex_metrics(string consumer, bool hidden, bool forward)
    {
        (var catalog, var provider, _) = CreateExamplePack(CreateTriangleFont());
        byte[] input = Document(false);
        using SvgPreparedDocument control = await SvgPreparedDocument.CreateAsync(Document(true), catalog, provider, SvgRenderOptions.Default, CancellationToken.None);
        byte[] expected = control.ConvertToPng(CancellationToken.None).ToArray();
        using (SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync(input, catalog, provider, CancellationToken.None))
        {
            if (hidden) Assert.Empty(snapshot.Fonts); else Assert.Single(snapshot.Fonts);
            SvgRenderOptions options = new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, snapshot);
            Assert.Equal(expected, SvgConverter.ConvertToPng(input, options, CancellationToken.None).ToArray());
        }
        using SvgPreparedDocument prepared = await SvgPreparedDocument.CreateAsync(input, catalog, provider, SvgRenderOptions.Default, CancellationToken.None);
        Assert.Equal(expected, prepared.ConvertToPng(CancellationToken.None).ToArray());
        Assert.Equal(hidden ? 0 : 3, provider.RequestCount);
        prepared.Dispose();
        Assert.Throws<ObjectDisposedException>(() => prepared.ConvertToPng(CancellationToken.None));

        byte[] Document(bool path)
        {
            string marker = (hidden ? "<style>#d{display:none}</style>" : "") + "<defs id='d'><marker id='m' markerUnits='userSpaceOnUse' font-family='Example' font-size='16' markerWidth='3ex' markerHeight='3ex'><rect width='8' height='8' fill='blue'/></marker></defs>";
            string leaf = (path ? "<path d='M0 0L1 0'" : "<line x2='1'") + " id='s' marker-start='url(#m)'/>";
            string body = consumer switch
            {
                "use" => "<defs>" + leaf + "</defs><use href='#s'/>",
                "symbol" => "<defs><symbol id='v' viewBox='0 0 32 32'>" + leaf + "</symbol></defs><use href='#v' width='32' height='32'/>",
                "pattern" => "<defs><pattern id='p' patternUnits='userSpaceOnUse' width='32' height='32'>" + leaf + "</pattern></defs><rect width='32' height='32' fill='url(#p)'/>",
                _ => leaf
            };
            return System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'>" + (forward ? body + marker : marker + body) + "</svg>");
        }
    }
}
