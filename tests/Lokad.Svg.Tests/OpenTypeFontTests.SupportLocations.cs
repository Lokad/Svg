namespace Lokad.Svg.Tests;

public sealed partial class OpenTypeFontTests
{
    public static IEnumerable<object[]> SupportDeclarationLocations()
    {
        (string Target, string Property, string Value, bool Attribute, bool Inline, bool Sheet)[] cases =
        [
            ("root", "overflow", "hidden", true, true, true),
            ("root", "overflow", "visible", false, false, false),
            ("root", "opacity", "1", true, true, true),
            ("root", "opacity", ".5", true, true, true),
            ("root", "color-interpolation-filters", "sRGB", false, false, false),
            ("root", "color-interpolation-filters", "linearRGB", false, true, true),
            ("nested", "overflow", "hidden", true, false, true),
            ("nested", "overflow", "visible", false, false, false),
            ("nested", "opacity", "1", true, false, true),
            ("nested", "opacity", ".5", true, false, true),
            ("nested", "color-interpolation-filters", "sRGB", false, false, false),
            ("nested", "color-interpolation-filters", "linearRGB", false, false, true),
            ("symbol", "overflow", "hidden", false, true, true),
            ("symbol", "overflow", "visible", false, false, false),
            ("symbol", "opacity", "1", true, true, true),
            ("symbol", "opacity", ".5", true, true, true),
            ("symbol", "color-interpolation-filters", "sRGB", false, false, false),
            ("symbol", "color-interpolation-filters", "linearRGB", false, true, true),
            ("pattern", "overflow", "hidden", true, true, true),
            ("pattern", "overflow", "visible", true, false, false),
            ("pattern", "opacity", "1", true, true, true),
            ("pattern", "opacity", ".5", false, false, false),
            ("pattern", "color-interpolation-filters", "sRGB", false, false, false),
            ("pattern", "color-interpolation-filters", "linearRGB", false, true, true),
            ("group", "overflow", "hidden", true, true, true),
            ("group", "overflow", "visible", true, true, true),
            ("group", "opacity", "1", true, true, true),
            ("group", "opacity", ".5", true, true, true),
            ("group", "color-interpolation-filters", "sRGB", false, false, false),
            ("group", "color-interpolation-filters", "linearRGB", false, true, true),
        ];
        foreach (var item in cases)
        {
            yield return [item.Target, item.Property, item.Value, "attribute", item.Attribute];
            yield return [item.Target, item.Property, item.Value, "inline", item.Inline];
            yield return [item.Target, item.Property, item.Value, "sheet", item.Sheet];
        }
    }

    [Theory]
    [MemberData(nameof(SupportDeclarationLocations))]
    public async Task Documented_declaration_locations_agree_in_conversion_and_preparation(string target, string property, string value, string location, bool admitted)
    {
        const string shape = "<rect width='8' height='8' fill='red'/>";
        string attributes = location switch
        {
            "attribute" => property + "='" + value + "'",
            "inline" => "style='" + property + ":" + value + "'",
            _ => "class='case'",
        };
        string css = location == "sheet" ? "<style>.case{" + property + ":" + value + "}</style>" : "";
        string rootAttributes = target == "root" ? attributes : "";
        string body = target switch
        {
            "nested" => "<svg width='16' height='16' " + attributes + ">" + shape + "</svg>",
            "symbol" => "<defs><symbol id='s' " + attributes + ">" + shape + "</symbol></defs><use href='#s'/>",
            "pattern" => "<defs><pattern id='p' width='1' height='1' " + attributes + ">" + shape + "</pattern></defs><rect width='32' height='32' fill='url(#p)'/>",
            "group" => "<g " + attributes + ">" + shape + "</g>",
            _ => shape,
        };
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32' " + rootAttributes + ">" + css + body + "</svg>");
        await AssertSupportAdmission(input, admitted);
    }

    [Theory]
    [InlineData("symbol")]
    [InlineData("pattern")]
    public async Task Nested_svg_stays_unsupported_inside_retained_resource_content(string target)
    {
        string source = "<svg width='16' height='16'><rect width='32' height='32' fill='red'/></svg>";
        string body = target == "symbol" ? "<defs><symbol id='s'>" + source + "</symbol></defs><use href='#s'/>"
            : "<defs><pattern id='p' width='1' height='1'>" + source + "</pattern></defs><rect width='32' height='32' fill='url(#p)'/>";
        await AssertSupportAdmission(System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'>" + body + "</svg>"), false);
    }

    [Theory]
    [InlineData("transform='translate(0)'", "<rect width='16' height='16'/>")]
    [InlineData("fill='red'", "<rect width='16' height='16'/>")]
    [InlineData("opacity='.5'", "<rect width='16' height='16'/>")]
    [InlineData("", "<g><rect width='16' height='16'/></g>")]
    public async Task Clip_resource_attributes_and_group_children_keep_their_documented_restrictions(string attributes, string child)
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'><defs><clipPath id='c' " + attributes + ">" + child + "</clipPath></defs><rect width='32' height='32' clip-path='url(#c)'/></svg>");
        await AssertSupportAdmission(input, false);
    }

    [Theory]
    [InlineData("objectBoundingBox", "x", false)]
    [InlineData("objectBoundingBox", "y", false)]
    [InlineData("objectBoundingBox", "width", false)]
    [InlineData("objectBoundingBox", "height", false)]
    [InlineData("userSpaceOnUse", "x", true)]
    [InlineData("userSpaceOnUse", "y", true)]
    [InlineData("userSpaceOnUse", "width", true)]
    [InlineData("userSpaceOnUse", "height", true)]
    public async Task Mask_region_percentages_require_user_space_units(string units, string attribute, bool percentages)
    {
        string extent = units == "objectBoundingBox" ? "1" : "32";
        foreach (bool percent in new[] { false, true })
        {
            string value = percent ? "50%" : units == "objectBoundingBox" ? ".5" : "16";
            string region = "x='" + (attribute == "x" ? value : "0") + "' y='" + (attribute == "y" ? value : "0")
                + "' width='" + (attribute == "width" ? value : extent) + "' height='" + (attribute == "height" ? value : extent) + "'";
            byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'><defs><mask id='m' maskUnits='" + units + "' " + region + "><rect width='32' height='32' fill='white'/></mask></defs><rect width='32' height='32' fill='red' mask='url(#m)'/></svg>");
            await AssertSupportAdmission(input, !percent || percentages);
        }
    }

    [Theory]
    [InlineData("objectBoundingBox", "x")]
    [InlineData("objectBoundingBox", "y")]
    [InlineData("objectBoundingBox", "width")]
    [InlineData("objectBoundingBox", "height")]
    [InlineData("userSpaceOnUse", "x")]
    [InlineData("userSpaceOnUse", "y")]
    [InlineData("userSpaceOnUse", "width")]
    [InlineData("userSpaceOnUse", "height")]
    public async Task Pattern_region_lengths_keep_numeric_and_px_admission_without_percentage_resolution(string units, string attribute)
    {
        string extent = units == "objectBoundingBox" ? "1" : "32";
        foreach (string suffix in new[] { "", "px", "%" })
        foreach (bool alias in new[] { false, true })
        {
            string value = suffix == "%" ? "50%" : (units == "objectBoundingBox" ? ".5" : "16") + suffix;
            string region = "x='" + (attribute == "x" ? value : "0") + "' y='" + (attribute == "y" ? value : "0")
                + "' width='" + (attribute == "width" ? value : extent) + "' height='" + (attribute == "height" ? value : extent) + "'";
            string id = alias ? "base" : "p";
            string template = alias ? "<pattern id='p' href='#base'/>" : "";
            byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'><defs><pattern id='" + id + "' patternUnits='" + units + "' " + region + "><rect width='32' height='32' fill='red'/></pattern>" + template + "</defs><rect width='32' height='32' fill='url(#p)'/></svg>");
            await AssertSupportAdmission(input, suffix != "%");
        }
    }

    [Theory]
    [InlineData("linearGradient")]
    [InlineData("radialGradient")]
    [InlineData("pattern")]
    public async Task Referenced_leaf_paint_servers_must_exist_by_the_live_use_not_by_the_leaf_declaration(string server)
    {
        string paint = server == "pattern" ? "<pattern id='p' width='1' height='1'><rect width='32' height='32' fill='red'/></pattern>"
            : "<" + server + " id='p'><stop stop-color='red'/></" + server + ">";
        const string source = "<defs><rect id='s' width='32' height='32' fill='url(#p)'/></defs>";
        const string use = "<use href='#s'/>";
        string definitions = "<defs>" + paint + "</defs>";
        foreach (bool late in new[] { false, true })
        {
            byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'>" + source + (late ? use + definitions : definitions + use) + "</svg>");
            await AssertSupportAdmission(input, !late);
        }
    }

    public static IEnumerable<object[]> AffineDashedStrokeBoundaries()
    {
        foreach (string shape in new[] { "line", "circle", "curve" })
        foreach (string width in new[] { ".5", "1", "2" })
        foreach (string map in new[] { "rotate(15 16 16)", "matrix(1 .2 .3 1 -4 -3)" }) yield return [shape, width, map];
    }

    [Theory]
    [MemberData(nameof(AffineDashedStrokeBoundaries))]
    public async Task Dashed_non_scaling_strokes_keep_the_documented_affine_boundary(string shape, string width, string map)
    {
        string source = shape switch
        {
            "line" => "<line x1='4' y1='16' x2='28' y2='16'",
            "circle" => "<circle cx='16' cy='16' r='10'",
            _ => "<path d='M4 24Q16 -4 28 24'",
        };
        foreach (bool nonScaling in new[] { false, true })
        {
            string effect = nonScaling ? " vector-effect='non-scaling-stroke'" : "";
            byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'>" + source + " fill='none' stroke='red' stroke-width='" + width + "' stroke-dasharray='2 1' transform='" + map + "'" + effect + "/></svg>");
            await AssertSupportAdmission(input, !nonScaling);
        }
    }

    [Theory]
    [InlineData("matrix(.75 0 0 1.25 4 -4)", false)]
    [InlineData("matrix(.75 0 0 .75 4 -4)", true)]
    public async Task Dashed_gradient_line_strokes_require_uniform_placement(string map, bool admitted)
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'><defs><linearGradient id='p'><stop stop-color='red'/><stop offset='1' stop-color='blue'/></linearGradient></defs><line x1='4' y1='16' x2='28' y2='16' fill='none' stroke='url(#p)' stroke-width='2' stroke-dasharray='2 1' pathLength='10' transform='" + map + "'/></svg>");
        await AssertSupportAdmission(input, admitted);
    }

    private static async Task AssertSupportAdmission(byte[] input, bool admitted)
    {
        (var catalog, var provider, _) = CreateExamplePack(CreateTriangleFont());
        if (admitted)
        {
            byte[] expected = SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None).ToArray();
            using SvgPreparedDocument prepared = await SvgPreparedDocument.CreateAsync(input, catalog, provider, SvgRenderOptions.Default, CancellationToken.None);
            Assert.Equal(expected, prepared.ConvertToPng(CancellationToken.None).ToArray());
        }
        else
        {
            SvgException direct = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None));
            Assert.Equal(SvgErrorCode.UnsupportedFeature, direct.ErrorCode);
            SvgException prepared = await Assert.ThrowsAsync<SvgException>(async () =>
            {
                using SvgPreparedDocument document = await SvgPreparedDocument.CreateAsync(input, catalog, provider, SvgRenderOptions.Default, CancellationToken.None);
                document.ConvertToPng(CancellationToken.None);
            });
            Assert.Equal(SvgErrorCode.UnsupportedFeature, prepared.ErrorCode);
        }
        Assert.Equal(0, provider.RequestCount);
    }
}
