namespace Lokad.Svg.Tests;

public sealed partial class OpenTypeFontTests
{
    [Theory]
    [InlineData(",")]
    [InlineData("\u00a0")]
    public async Task Clip_prefix_recovery_keeps_the_full_authored_command_limit(string error)
    {
        SvgRenderLimits standard = SvgRenderLimits.Default;
        SvgRenderLimits limits = new(standard.MaximumInputBytes, standard.MaximumDimension,
            standard.MaximumPixels, standard.MaximumEncodedPngBytes, standard.MaximumNodes, 5);
        SvgRenderOptions options = new(null, 1, SvgColor.Transparent, limits);
        (var catalog, var provider, _) = CreateExamplePack(CreateTriangleFont());
        string valid = "M0 0H8V8H0Z";
        byte[] control = Input(valid);
        Assert.Equal(Render(Document("<rect width='8' height='8' fill='red'/>")), SvgConverter.ConvertToPng(control, options, CancellationToken.None).ToArray());
        byte[] invalid = Input(valid + error + "M0 0");
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(invalid, options, CancellationToken.None)).ErrorCode);
        SvgException rejected = await Assert.ThrowsAsync<SvgException>(async () => await SvgPreparedDocument.CreateAsync(invalid, catalog, provider, options, CancellationToken.None));
        Assert.Equal(SvgErrorCode.LimitExceeded, rejected.ErrorCode);

        static byte[] Input(string path) => Document("<defs><clipPath id='c'><path d='" + path + "'/></clipPath></defs><rect width='8' height='8' fill='red' clip-path='url(#c)'/>");
    }

    [Theory]
    [InlineData(",M4 4H28V28H4Z", "")]
    [InlineData("M,4 4H28V28H4Z", "")]
    [InlineData("M4,,4H28V28H4Z", "")]
    [InlineData("M4 4Q,16 4 28 4V28H4Z", "M4 4")]
    [InlineData("M4 4A,4 4 0 0 1 8 8V28H4Z", "M4 4")]
    [InlineData("M\u00a04 4H28V28H4Z", "")]
    [InlineData("M4 4\u2003H28V28H4Z", "M4 4")]
    [InlineData("M4 4H28V28H4Z, M0 0H2V2H0Z", "M4 4H28V28H4Z")]
    [InlineData("M4 4H28V28H4Z,,M0 0H2V2H0Z", "M4 4H28V28H4Z")]
    [InlineData("M4 4H28V28H4Z\u00a0M0 0H2V2H0Z", "M4 4H28V28H4Z")]
    public async Task Path_separator_errors_preserve_only_complete_authored_segments(string data, string prefix)
    {
        (var catalog, var provider, _) = CreateExamplePack(CreateTriangleFont());
        foreach (string context in new[] { "direct", "use", "symbol", "pattern", "clip" })
        {
            byte[] expected = Render(Input(prefix, context));
            byte[] input = Input(data, context);
            Assert.Equal(expected, Render(input));
            using SvgPreparedDocument prepared = await SvgPreparedDocument.CreateAsync(input, catalog, provider, SvgRenderOptions.Default, CancellationToken.None);
            Assert.Equal(expected, prepared.ConvertToPng(CancellationToken.None).ToArray());
        }

        static byte[] Input(string path, string context)
        {
            string node = "<path d='" + path + "' fill='red'";
            string body = context switch
            {
                "use" => "<defs>" + node + " id='s'/></defs><use href='#s'/>",
                "symbol" => "<defs><symbol id='s'>" + node + "/></symbol></defs><use href='#s'/>",
                "pattern" => "<defs><pattern id='s' patternUnits='userSpaceOnUse' width='32' height='32'>" + node + "/></pattern></defs><rect width='32' height='32' fill='url(#s)'/>",
                "clip" => "<defs><clipPath id='c'>" + node + "/></clipPath></defs><rect width='32' height='32' fill='red' clip-path='url(#c)'/>",
                _ => node + "/>"
            };
            return System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'>" + body + "</svg>");
        }
    }

    [Theory]
    [InlineData("M4 4,H7V7H4Z")]
    [InlineData("M4,4 H7 V7 H4 Z")]
    [InlineData("M4+4H7V7H4Z")]
    public void Valid_path_separators_keep_their_existing_rendering(string data)
        => Assert.Equal(Render(Document("<path d='M4 4H7V7H4Z'/>")), Render(Document("<path d='" + data + "'/>")));

    [Theory]
    [InlineData(",4 4 7 4 7 7 4 7")]
    [InlineData("4,,4 7 4 7 7 4 7")]
    [InlineData("4 4, ,7 4 7 7 4 7")]
    [InlineData("4 4 7 4 7 7 4 7,")]
    [InlineData("4.0,,4 7 4 7 7 4 7")]
    public async Task Point_lists_require_values_around_commas(string data)
    {
        (var catalog, var provider, _) = CreateExamplePack(CreateTriangleFont());
        foreach (string element in new[] { "polygon", "polyline" })
        {
            byte[] input = Document("<" + element + " points='" + data + "' fill='red'/>");
            Assert.Equal(SvgErrorCode.MalformedInput, Assert.Throws<SvgException>(() => Render(input)).ErrorCode);
            SvgException error = await Assert.ThrowsAsync<SvgException>(async () => await SvgPreparedDocument.CreateAsync(input, catalog, provider, SvgRenderOptions.Default, CancellationToken.None));
            Assert.Equal(SvgErrorCode.MalformedInput, error.ErrorCode);
        }
    }
}
