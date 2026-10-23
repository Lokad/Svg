namespace Lokad.Svg.Tests;

public sealed partial class OpenTypeFontTests
{
    public static IEnumerable<object[]> DeferredGradientFragments()
    {
        foreach (string parent in new[] { "linearGradient", "radialGradient" })
        foreach (string child in new[] { "linearGradient", "radialGradient" })
        foreach ((string target, string href, string collision) in new[] { ("%73", "%2573", "s"), ("%qq", "%25qq", "other"), ("é%73", "%C3%A9%2573", "és") })
        foreach (string context in new[] { "direct", "use", "symbol", "pattern" })
            yield return [parent, child, target, href, collision, context];
    }

    [Theory]
    [MemberData(nameof(DeferredGradientFragments))]
    public async Task Deferred_gradients_keep_authored_hrefs_separate_from_decoded_dependency_keys(string parent, string child, string target, string href, string collision, string context)
    {
        const string stops = "<stop stop-color='red'/><stop offset='1' stop-color='blue'/>";
        (var catalog, var provider, _) = CreateExamplePack(CreateTriangleFont());
        foreach (bool forward in new[] { false, true })
        foreach (bool chain in new[] { false, true })
        foreach (string variant in new[] { "borrow", "own", "full" })
        {
            string blue = Parent(collision, "blue", ".75", ".2");
            string wanted = Parent(target, "red", ".25", ".4");
            string middle = chain ? "<" + child + " id='middle' href='#" + href + "'/>" : "";
            string coordinates = child == "linearGradient" ? "x1='0' y1='0' x2='1' y2='0'" : "cx='.5' cy='.5' r='.45'";
            string attributes = variant == "full" ? coordinates + " gradientUnits='objectBoundingBox' gradientTransform='translate(0 0)' spreadMethod='pad'" : "";
            string alias = "<" + child + " id='g' href='#" + (chain ? "middle" : href) + "' " + attributes + ">" + (variant == "borrow" ? "" : stops) + "</" + child + ">";
            string definitions = blue + (forward ? alias + middle + wanted : wanted + middle + alias);
            string expectedCoordinates = child == "linearGradient" ? "x1='0' y1='0' x2='1' y2='0'"
                : "cx='.5' cy='.5' r='" + (parent == "radialGradient" || variant == "full" ? ".45" : ".5") + "' fx='" + (parent == "radialGradient" ? ".4" : ".5") + "' fy='.5' fr='0'";
            string standalone = "<" + child + " id='g' gradientUnits='objectBoundingBox' gradientTransform='translate(" + (variant == "full" ? "0" : ".25") + " 0)' " + expectedCoordinates + ">" + (variant == "borrow" ? "<stop stop-color='red'/>" : stops) + "</" + child + ">";
            byte[] input = Input(definitions);
            byte[] expected = Render(Input(standalone));
            Assert.Equal(expected, Render(input));
            using SvgPreparedDocument prepared = await SvgPreparedDocument.CreateAsync(input, catalog, provider, SvgRenderOptions.Default, CancellationToken.None);
            Assert.Equal(expected, prepared.ConvertToPng(CancellationToken.None).ToArray());
            Assert.Equal(0, provider.RequestCount);

            byte[] Input(string gradients)
            {
                const string rectangle = "<rect x='4' y='4' width='24' height='24' fill='url(#g)'/>";
                string content = context switch
                {
                    "use" => "<defs>" + rectangle.Replace("<rect", "<rect id='shape'", StringComparison.Ordinal) + "</defs><use href='#shape'/>",
                    "symbol" => "<defs><symbol id='host'>" + rectangle + "</symbol></defs><use href='#host'/>",
                    "pattern" => "<defs><pattern id='tile' patternUnits='userSpaceOnUse' width='32' height='32'>" + rectangle + "</pattern></defs><rect width='32' height='32' fill='url(#tile)'/>",
                    _ => rectangle
                };
                string resources = "<defs>" + gradients + "</defs>";
                // A live leaf use needs the referenced paint registry populated.
                return System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'>" + (chain && context != "use" ? content + resources : resources + content) + "</svg>");
            }
        }

        string Parent(string id, string color, string shift, string focal)
            => "<" + parent + " id='" + id + "' gradientTransform='translate(" + shift + " 0)' "
                + (parent == "radialGradient" ? "cx='.5' cy='.5' r='.45' fx='" + focal + "' fy='.5' fr='0'" : "")
                + "><stop stop-color='" + color + "'/></" + parent + ">";
    }
}
