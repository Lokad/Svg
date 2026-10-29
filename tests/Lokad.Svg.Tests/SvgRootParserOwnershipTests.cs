using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;
using System.Text;

namespace Lokad.Svg.Tests;

[Trait("Layer", "Contract")]
public sealed class SvgRootParserOwnershipTests
{
    [Fact]
    public void Reserves_raw_and_comment_stripped_stylesheet_text_during_parsing()
    {
        string stylesheet = "/**/" + new string(' ', 512) + "rect { fill: red; }";
        byte[] svg = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg'><style>" + stylesheet + "</style><rect width='1'/></svg>");
        SvgRenderLimits limits = new(1_024, 16, 16, 1_024, 16, 16);
        RenderBudget measured = new RenderBudget(new HostMemoryBudget(1_000_000));
        using (SvgRootParser.Parse(svg, limits, measured, CancellationToken.None)) { }
        Assert.Equal(0, measured.Snapshot().CurrentBytes);
        RenderBudget insufficient = new RenderBudget(new HostMemoryBudget(measured.Snapshot().PeakBytes - 1));
        SvgException exception = Assert.Throws<SvgException>(() => SvgRootParser.Parse(svg, limits, insufficient, CancellationToken.None));
        Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode);
        Assert.Equal(0, insufficient.Snapshot().CurrentBytes);
        using SvgRoot root = SvgRootParser.Parse(svg, limits, new RenderBudget(new HostMemoryBudget(measured.Snapshot().PeakBytes)), CancellationToken.None);
        Assert.Single(root.Rectangles);
    }

    [Fact]
    public void Scene_growth_reserves_replacement_geometry_before_releasing_its_previous_capacity()
    {
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(10_000));
        SvgRectangle rectangle = new(0, 0, 1, 1, new SvgColor(0, 0, 0, 255), true, null);
        using var scene = new SvgSceneBuilder(budget, CancellationToken.None);
        for (int index = 0; index < 4; index++) scene.AddGeometry(SvgGeometry.FromRectangle(rectangle));
        long beforeGrowth = budget.Snapshot().CurrentBytes;

        scene.AddGeometry(SvgGeometry.FromRectangle(rectangle));

        RenderBudgetSnapshot afterGrowth = budget.Snapshot();
        Assert.True(afterGrowth.PeakBytes > afterGrowth.CurrentBytes);
        Assert.True(afterGrowth.PeakBytes > beforeGrowth);
    }

    [Fact]
    public void Parser_collection_transfer_preserves_values()
    {
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(1_000));
        using (BudgetedParserList<int> limited = new(budget, SvgMemoryCategory.Parser, CancellationToken.None))
        {
            for (int value = 0; value < 5; value++) limited.Add(value);
        }
        Assert.Equal(0, budget.Snapshot().BytesByCategory[(int)SvgMemoryCategory.Parser]);

        using BudgetedParserList<int> accepted = new(new RenderBudget(new HostMemoryBudget(1_000)), SvgMemoryCategory.Parser, CancellationToken.None);
        for (int value = 0; value < 5; value++) accepted.Add(value);
        using OwnedParserArray<int> storage = accepted.Detach();
        Assert.Equal([0, 1, 2, 3, 4], storage.Items);
    }

    [Fact]
    public void Reserves_secondary_parser_collections_before_they_allocate()
    {
        SvgRenderLimits limits = new(1024, 16, 16, 1024, 16, 16);

        SvgException group = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><g></g></svg>"u8.ToArray(), limits, new RenderBudget(new HostMemoryBudget(16_385)), CancellationToken.None));
        SvgException stylesheet = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><style>rect { fill: red; }</style></svg>"u8.ToArray(), limits, new RenderBudget(new HostMemoryBudget(16_385)), CancellationToken.None));
        SvgException definitions = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><defs><rect id='sample'/></defs></svg>"u8.ToArray(), limits, new RenderBudget(new HostMemoryBudget(16_385)), CancellationToken.None));
        SvgException pendingUse = Assert.Throws<SvgException>(() => SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><use href='#sample'/></svg>"u8.ToArray(), new SvgRenderLimits(1024, 16, 16, 1024, 16, 16), new RenderBudget(new HostMemoryBudget(17_023)), CancellationToken.None));

        Assert.Equal(SvgErrorCode.LimitExceeded, group.ErrorCode);
        Assert.Equal(SvgErrorCode.LimitExceeded, stylesheet.ErrorCode);
        Assert.Equal(SvgErrorCode.LimitExceeded, definitions.ErrorCode);
        Assert.Equal(SvgErrorCode.LimitExceeded, pendingUse.ErrorCode);
    }

    [Fact]
    public void Reserves_xml_reader_workspace_from_the_input_and_attribute_limits_before_parsing()
    {
        byte[] svg = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg'><metadata>" + new string('x', 9_000) + "</metadata></svg>");
        SvgRenderLimits limits = new(100_000, 16, 16, 1024, 16, 16);
        const long workspace = 256L * 512;

        SvgException exception = Assert.Throws<SvgException>(() => SvgRootParser.Parse(svg, limits, new RenderBudget(new HostMemoryBudget(workspace - 1)), CancellationToken.None));
        using SvgRoot root = SvgRootParser.Parse(svg, limits, new RenderBudget(new HostMemoryBudget(workspace + SvgSceneBuilder.RecordedPaintOwnerBytes)), CancellationToken.None);

        Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode);
        Assert.Empty(root.Rectangles);
    }

    [Fact]
    public void Reserves_xml_reader_attribute_workspace_from_the_configured_limit_before_parsing()
    {
        SvgRenderLimits limits = new(1024, 16, 16, 1024, 16, 16, 128, 257);
        const long workspace = 257L * 512;
        byte[] svg = "<svg xmlns='http://www.w3.org/2000/svg'/>"u8.ToArray();

        SvgException exception = Assert.Throws<SvgException>(() => SvgRootParser.Parse(svg, limits, new RenderBudget(new HostMemoryBudget(workspace - 1)), CancellationToken.None));
        using SvgRoot root = SvgRootParser.Parse(svg, limits, new RenderBudget(new HostMemoryBudget(workspace)), CancellationToken.None);

        Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode);
        Assert.Empty(root.Rectangles);
    }

    [Fact]
    public void Reserves_retained_stylesheet_strings_before_copying_selectors()
    {
        string identifier = new('a', SvgSyntaxLimits.MaximumIdentifierCharacters);
        byte[] svg = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg'><style>#" + identifier + " { fill: red; }</style><rect id='" + identifier + "' width='1' height='1'/></svg>");
        SvgRenderLimits limits = new(4_096, 16, 256, 1_024, 16, 16, 16, 1);

        SvgException exception = Assert.Throws<SvgException>(() => SvgRootParser.Parse(svg, limits, new RenderBudget(new HostMemoryBudget(18_384)), CancellationToken.None));

        Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode);
    }

    [Fact]
    public void Reserves_definition_identifiers_before_retaining_them()
    {
        string identifier = new('a', SvgSyntaxLimits.MaximumIdentifierCharacters);
        byte[] svg = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg'><defs><rect id='" + identifier + "' width='1' height='1'/></defs></svg>");
        SvgRenderLimits limits = new(4_096, 16, 256, 1_024, 16, 16, 16, 1);

        SvgException exception = Assert.Throws<SvgException>(() => SvgRootParser.Parse(svg, limits, new RenderBudget(new HostMemoryBudget(18_384)), CancellationToken.None));

        Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode);
    }

    [Fact]
    public void Reserves_the_definition_dictionary_prime_capacity_before_allocating()
    {
        using BudgetedParserDictionary<string, int> definitions = new(new RenderBudget(new HostMemoryBudget(2_000)), SvgMemoryCategory.Parser, 256);

        SvgException exception = Assert.Throws<SvgException>(() => definitions.TryAdd("id", 1));

        Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode);
    }

    [Fact]
    public void Reserves_geometry_collection_capacity_before_allocation()
    {
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(63));
        using BudgetedParserList<SvgPoint> points = new(budget, SvgMemoryCategory.Geometry);

        SvgException exception = Assert.Throws<SvgException>(() => points.Add(new SvgPoint(0, 0)));

        Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode);
    }

    [Fact]
    public void Releases_transient_parser_collection_reservations_after_scene_construction()
    {
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(300_000));

        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><style>rect { fill: red; }</style><defs><rect id='sample'/></defs><use href='#sample'/></svg>"u8.ToArray(), SvgRenderLimits.Default, budget, CancellationToken.None);

        Assert.Equal(0, budget.Snapshot().BytesByCategory[(int)SvgMemoryCategory.Parser]);
    }

    [Fact]
    public void Releases_transient_orthogonal_geometry_reservations_after_scene_construction()
    {
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(300_000));

        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><path d='M 0 0 H 2 V 2 H 0 Z' fill='red'/></svg>"u8.ToArray(), SvgRenderLimits.Default, budget, CancellationToken.None);

        Assert.NotNull(Assert.Single(root.Geometries).AuthoredPath);
        Assert.Equal(0, budget.Snapshot().BytesByCategory[(int)SvgMemoryCategory.Geometry]);
    }

    [Fact]
    public void Twenty_circles_report_exact_budget_peak_before_denial()
    {
        StringBuilder discs = new("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'>");
        for (int index = 0; index < 20; index++)
        {
            discs.Append("<circle cx='4' cy='4' r='3' fill='red'/>");
        }
        discs.Append("</svg>");
        byte[] svg = System.Text.Encoding.UTF8.GetBytes(discs.ToString());
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(100_000_000));
        using SvgRoot root = SvgRootParser.Parse(svg, SvgRenderLimits.Default, budget, CancellationToken.None);
        long peak = budget.Snapshot().PeakBytes;
        RenderBudget denied = new RenderBudget(new HostMemoryBudget(peak - 1));
        SvgException exception = Assert.Throws<SvgException>(() =>
        {
            using SvgRoot deniedRoot = SvgRootParser.Parse(svg, SvgRenderLimits.Default, denied, CancellationToken.None);
        });

        Assert.NotEmpty(root.Geometries);
        Assert.True(peak > 0);
        Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode);
    }
}
