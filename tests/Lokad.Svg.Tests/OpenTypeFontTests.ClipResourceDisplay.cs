using Lokad.Svg.Fonts;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed partial class OpenTypeFontTests
{
    public static IEnumerable<object[]> DisabledClipResources()
    {
        foreach (string content in new[] { "rect", "path", "circle", "empty", "union", "use" })
        foreach (string target in new[] { "rect", "path", "group", "use", "symbol", "pattern", "text" })
        foreach (bool forward in new[] { false, true }) yield return [content, target, forward];
    }

    [Theory]
    [MemberData(nameof(DisabledClipResources))]
    public async Task A_disabled_clip_resource_removes_its_binding_without_removing_ancestor_effects(string content, string target, bool forward)
    {
        (SvgFontPackCatalog catalog, CountingFontProvider provider, CountingFontSource _) = CreateExamplePack(CreateTriangleFont());
        using SvgFontSnapshot? fonts = target == "text" ? await SvgFontSnapshot.CreateAsync(catalog, new[] { new SvgFontFaceRequest("Example", 400, false) }, provider, CancellationToken.None) : null;
        foreach (bool objectBox in new[] { false, true })
        foreach (string location in new[] { "inline", "rule" })
        foreach (SvgColor background in target == "text" ? new[] { SvgColor.Transparent, new SvgColor(255, 255, 255, 255) } : new[] { SvgColor.Transparent })
        {
            if (objectBox && content == "use") continue;
            SvgRenderOptions rawOptions = new(null, 1, background, SvgRenderLimits.Default, fonts);
            SvgRenderOptions preparedOptions = new(null, 1, background, SvgRenderLimits.Default);
            byte[] input = Svg(true), expectedInput = Svg(false);
            byte[] expected = SvgConverter.ConvertToPng(expectedInput, rawOptions, CancellationToken.None).ToArray();
            if (target == "text") Assert.NotEqual(SvgConverter.ConvertToPng(Document(""), rawOptions, CancellationToken.None).ToArray(), expected);
            Assert.Equal(expected, SvgConverter.ConvertToPng(input, rawOptions, CancellationToken.None).ToArray());
            using SvgPreparedDocument prepared = await SvgPreparedDocument.CreateAsync(input, catalog, provider, preparedOptions, CancellationToken.None);
            Assert.Equal(expected, prepared.ConvertToPng(CancellationToken.None).ToArray());

            byte[] Svg(bool binding)
            {
                string clipContent = content switch
                {
                    "rect" => objectBox ? "<rect width='.25' height='1'/>" : "<rect width='2' height='8'/>",
                    "path" => objectBox ? "<path d='M0 0H.25V1H0Z'/>" : "<path d='M0 0H2V8H0Z'/>",
                    "circle" => objectBox ? "<circle cx='.25' cy='.5' r='.25'/>" : "<circle cx='2' cy='4' r='2'/>",
                    "union" => objectBox ? "<rect width='.25' height='.5'/><path d='M0 .5H.25V1H0Z'/>" : "<rect width='2' height='4'/><path d='M0 4H2V8H0Z'/>",
                    "use" => "<use href='#contour'/>",
                    _ => ""
                };
                string sheet = location == "rule" ? "<style>#disabled{display:none}</style>" : "";
                string definition = "<defs><clipPath id='disabled' " + (location == "inline" ? "style='display:none'" : "")
                    + (objectBox ? " clipPathUnits='objectBoundingBox'" : "") + ">" + clipContent + "</clipPath></defs>";
                string effect = binding ? " clip-path='url(#disabled)'" : "";
                string drawing = target switch
                {
                    "path" => "<path d='M0 0H8V8H0Z' fill='red'" + effect + "/>",
                    "group" => "<g" + effect + "><rect width='8' height='8' fill='red'/></g>",
                    "use" => "<defs><path id='s' d='M0 0H8V8H0Z' fill='red'/></defs><use href='#s'" + effect + "/>",
                    "symbol" => "<defs><symbol id='s'><path d='M0 0H8V8H0Z' fill='red'" + effect + "/></symbol></defs><use href='#s'/>",
                    "pattern" => "<defs><pattern id='p' width='8' height='8' patternUnits='userSpaceOnUse'><rect width='8' height='8' fill='red'" + effect + "/></pattern></defs><rect width='8' height='8' fill='url(#p)'/>",
                    "text" => "<text x='.25' y='7' font-size='200' font-family='Example' fill='red'" + effect + ">A</text>",
                    _ => "<rect width='8' height='8' fill='red'" + effect + "/>"
                };
                string ancestors = "<defs><clipPath id='outer'><rect width='7' height='8'/></clipPath><path id='contour' d='M0 0H2V8H0Z'/></defs>";
                if (target != "text") drawing = "<g clip-path='url(#outer)' opacity='.75'>" + drawing + "</g>";
                return Document(sheet + ancestors + (forward ? drawing + definition : definition + drawing));
            }
        }
        if (target != "text") Assert.Equal(0, provider.RequestCount);
    }

    [Theory]
    [InlineData("rect", false)]
    [InlineData("rect", true)]
    [InlineData("path", false)]
    [InlineData("path", true)]
    [InlineData("group", false)]
    [InlineData("group", true)]
    [InlineData("use", false)]
    [InlineData("use", true)]
    public void Disabled_clips_leave_filtered_masked_output_and_opacity_unchanged(string target, bool forward)
    {
        byte[] Svg(bool binding)
        {
            string effect = " filter='url(#f)' mask='url(#m)' opacity='.5'" + (binding ? " clip-path='url(#c)'" : "");
            string source = "<rect x='2' y='2' width='4' height='4' fill='blue'" + (target == "rect" ? effect : "") + "/>";
            string body = target switch
            {
                "group" => "<g" + effect + ">" + source + "</g>",
                "use" => "<defs><rect id='s' x='2' y='2' width='4' height='4' fill='blue'/></defs><use href='#s'" + effect + "/>",
                "path" => "<path d='M2 2H6V6H2Z' fill='blue'" + effect + "/>",
                _ => source
            };
            const string definitions = "<defs><clipPath id='c' style='display:none'><circle r='1'/></clipPath><mask id='m' maskUnits='userSpaceOnUse' x='0' y='0' width='8' height='8'><rect width='6' height='8' fill='white'/></mask><filter id='f' filterUnits='userSpaceOnUse' x='0' y='0' width='8' height='8'><feFlood flood-color='red'/></filter></defs>";
            return Document(forward ? body + definitions : definitions + body);
        }
        Assert.Equal(Render(Svg(false)), Render(Svg(true)));
    }

    [Fact]
    public void Disabled_nested_clips_preserve_the_active_parent_and_referenced_child_ids()
    {
        const string resources = "<defs><clipPath id='c' style='display:none'><path id='child' d='M0 0H2V8H0Z' fill='blue'/></clipPath><clipPath id='parent'><path d='M0 0H4V8H0Z' clip-path='url(#c)'/></clipPath></defs>";
        Assert.Equal(Render(Document("<rect width='4' height='8' fill='red'/><rect x='4' width='2' height='8' fill='blue'/>")),
            Render(Document(resources + "<rect width='8' height='8' fill='red' clip-path='url(#parent)'/><use href='#child' x='4'/>")));
    }

    [Fact]
    public void Disabled_clip_storage_releases_on_denial_and_preserves_duplicate_id_errors()
    {
        byte[] input = Document("<defs><clipPath id='c' style='display:none'><path d='M0 0H4V8H0Z'/><circle cx='4' cy='4' r='3'/></clipPath></defs><rect width='8' height='8' clip-path='url(#c)'/>");
        RenderBudget measured = new RenderBudget(new HostMemoryBudget(1_000_000));
        using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, measured, null, CancellationToken.None)) Assert.False(root.HasUnsupportedChild);
        Assert.Equal(0, measured.Snapshot().CurrentBytes);
        byte[] duplicate = Document("<defs><clipPath id='c' style='display:none'/><clipPath id='c'><rect width='8' height='8'/></clipPath></defs>");
        Assert.Equal(SvgErrorCode.MalformedInput, Assert.Throws<SvgException>(() => Render(duplicate)).ErrorCode);
    }
}
