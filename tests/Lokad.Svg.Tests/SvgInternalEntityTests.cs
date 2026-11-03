using System.Globalization;
using System.Text;
using Lokad.Svg.Fonts;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

/// <summary>XML expansion mechanisms and the preparation interactions they enable.</summary>
public sealed class SvgInternalEntityTests
{
    private const string Rectangle = "<rect width='8' height='4' fill='red'/>";

    [Theory]
    [Trait("Layer", "Mechanism")]
    [InlineData("<!ENTITY box \"<rect width='8' height='4' fill='red'/>\">", "&box;")]
    [InlineData("<!ENTITY red 'red'><!ENTITY box \"<rect width='8' height='4' fill='&red;'/>\">", "&box;")]
    [InlineData("<!ENTITY box \"<rect width='8' height='4' fill='&red;'/>\"><!ENTITY red 'red'>", "&box;")]
    [InlineData("<!ENTITY red 'red'><!ENTITY color '&#38;red;'>", "<rect width='8' height='4' fill='&color;'/>")]
    [InlineData("<!ENTITY box \"&#60;rect width='8' height='4' fill='red'/>\">", "&box;")]
    [InlineData("<!ENTITY color '&#114;ed'>", "<rect width='8' height='4' fill='&color;'/>")]
    [InlineData("<!ENTITY empty ''>", "&empty;<rect width='8' height='4' fill='red'/>")]
    [InlineData("<!ENTITY color 'red'><!ENTITY color 'blue'>", "<rect width='8' height='4' fill='&color;'/>")]
    [InlineData("<!-- <!ENTITY fake '>'> --><?note ] > ?>", "<rect width='8' height='4' fill='red'/>")]
    [InlineData("", "<rect width='8' height='4' fill='red'/>")]
    public void Bcl_expansion_preserves_markup_attributes_and_reference_normalization(string declarations, string body)
    {
        Assert.Equal(Render(Document("", Rectangle)), Render(Document("<!DOCTYPE svg [" + declarations + "]>", body)));
    }

    [Fact]
    [Trait("Layer", "Mechanism")]
    public void Entity_encoded_styles_and_paint_resources_use_the_expanded_document()
    {
        const string content = "<style>rect { fill: url(#paint); }</style><defs><linearGradient id='paint'><stop stop-color='red'/><stop offset='1' stop-color='blue'/></linearGradient></defs><rect width='8' height='4'/>";
        Assert.Equal(Render(Document("", content)), Render(EntityDocument(content)));
    }

    [Theory]
    [Trait("Layer", "Mechanism")]
    [InlineData("<defs><g id='label'><text font-family='Example'>x</text></g></defs><use href='#label'/>")]
    [InlineData("<defs><pattern id='p' width='1' height='1' patternUnits='userSpaceOnUse'><text font-family='Example'>x</text></pattern></defs><rect width='8' height='4' fill='url(#p)'/>")]
    [InlineData("<style>text { font-family: Example; }</style><text>x</text>")]
    public void Entity_encoded_dependencies_preserve_font_selection(string content)
    {
        SvgFontFaceRequest[] expected = SvgFontRequestDiscovery.Discover(Document("", content), CancellationToken.None);
        SvgFontFaceRequest[] actual = SvgFontRequestDiscovery.Discover(EntityDocument(content), CancellationToken.None);
        SvgFontFaceRequest first = Assert.Single(expected), second = Assert.Single(actual);
        Assert.Equal((first.Family, first.Weight, first.Italic), (second.Family, second.Weight, second.Italic));
        Assert.Equal(first.Families.Select(f => (f.Name, f.IsGeneric)), second.Families.Select(f => (f.Name, f.IsGeneric)));
    }

    [Theory]
    [Trait("Layer", "Mechanism")]
    [InlineData("<!ENTITY a '&a;'>", "&a;")]
    [InlineData("<!ENTITY a '&b;'><!ENTITY b '&a;'>", "&a;")]
    [InlineData("<!ENTITY a '&missing;'>", "&a;")]
    [InlineData("<!ENTITY a SYSTEM 'file:///does-not-exist'>", "")]
    [InlineData("<!ENTITY % a 'red'>", "")]
    [InlineData("<!ATTLIST rect fill CDATA 'red'>", "")]
    [InlineData("<!ELEMENT svg ANY>", "")]
    [InlineData("<!NOTATION n SYSTEM 'urn:unused'>", "")]
    public void Invalid_references_and_other_subset_declarations_keep_the_explicit_boundary(string declarations, string body)
    {
        byte[] input = Document("<!DOCTYPE svg [" + declarations + "]>", body);
        Assert.Equal(SvgErrorCode.MalformedInput, Assert.Throws<SvgException>(() => Render(input)).ErrorCode);
    }

    [Fact]
    [Trait("Layer", "Mechanism")]
    public void Saturated_estimates_do_not_reject_unused_or_small_expansions()
    {
        StringBuilder declarations = new("<!ENTITY a 'red'>");
        for (int i = 0; i < 30; i++) declarations.Append("<!ENTITY n").Append(i).Append(" '&a;&a;'>");
        byte[] input = Document("<!DOCTYPE svg [" + declarations + "]>", "<rect width='8' height='4' fill='&a;'/>");
        Assert.Equal(Render(Document("", Rectangle)), Render(input));
    }

    [Fact]
    [Trait("Layer", "Contract")]
    public void Reader_allowance_stays_with_preparation_through_discovery_and_sizing()
    {
        byte[] input = EntityDocument("<text font-family='Example'>x</text>");
        RenderBudget budget = new();
        long ordinary = SvgRootParser.GetXmlReaderWorkspaceBytes(input.Length, SvgRenderLimits.Default.MaximumAttributesPerElement);
        using (SvgDocumentPreparation preparation = SvgDocumentPreparation.Create(input, SvgRenderLimits.Default, budget, CancellationToken.None))
        {
            Assert.Equal(System.Xml.DtdProcessing.Parse, preparation.Settings.DtdProcessing);
            Assert.Equal(SvgRenderLimits.Default.MaximumInputBytes, preparation.Settings.MaxCharactersFromEntities);
            Assert.True(budget.Snapshot().CurrentBytes > ordinary);
            Assert.Equal("Example", Assert.Single(preparation.DiscoverFontRequests()).Family);
            preparation.InspectDimensions(SvgRenderOptions.Default);
            Assert.True(budget.Snapshot().CurrentBytes > ordinary);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    [Trait("Layer", "Contract")]
    public void Entity_allowance_refusal_releases_the_preparation_owner_and_allows_retry()
    {
        byte[] input = EntityDocument(Rectangle);
        long ordinary = SvgRootParser.GetXmlReaderWorkspaceBytes(input.Length, SvgRenderLimits.Default.MaximumAttributesPerElement);
        RenderBudget budget = new(new HostMemoryBudget(ordinary + 32 * 1024));
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() =>
            SvgDocumentPreparation.Create(input, SvgRenderLimits.Default, budget, CancellationToken.None)).ErrorCode);
        Assert.Equal(1, budget.Snapshot().DeniedReservationCount);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        using (SvgDocumentPreparation retry = SvgDocumentPreparation.Create(Document("", Rectangle), SvgRenderLimits.Default, budget, CancellationToken.None))
            retry.InspectDimensions(SvgRenderOptions.Default);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    [Trait("Layer", "Contract")]
    public void Body_reference_scan_observes_cancellation_and_releases_both_allowances()
    {
        byte[] input = Document("<!DOCTYPE svg [<!ENTITY box \"" + Rectangle + "\">]>", "<!--" + new string('x', 20_000) + "-->&box;");
        RenderBudget budget = new();
        using CancellationTokenSource cancellation = new();
        int checkpoint = -1;
        using (SvgXmlPrologProbe.Arm(position =>
        {
            if (position < 4096) return;
            checkpoint = position;
            cancellation.Cancel();
        }))
        {
            Assert.Equal(cancellation.Token, Assert.Throws<OperationCanceledException>(() =>
                SvgDocumentPreparation.Create(input, SvgRenderLimits.Default, budget, cancellation.Token)).CancellationToken);
        }
        Assert.Equal(4096, checkpoint);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        using (SvgDocumentPreparation retry = SvgDocumentPreparation.Create(input, SvgRenderLimits.Default, budget, CancellationToken.None))
            retry.InspectDimensions(SvgRenderOptions.Default);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    [Trait("Layer", "Mechanism")]
    public void Expansion_uses_the_existing_xml_character_ceiling()
    {
        byte[] input = Document("<!DOCTYPE svg [<!ENTITY a '" + new string('x', 200) + "'><!ENTITY b '&a;&a;&a;&a;&a;&a;&a;&a;'><!ENTITY c '&b;&b;&b;&b;&b;&b;&b;&b;'>]>", "<desc>&c;</desc>");
        Assert.True(input.Length < 1024);
        SvgRenderLimits limits = new(1024, 16, 256, 4096, 100, 100, 16, 16);
        RenderBudget budget = new();
        Assert.Equal(SvgErrorCode.MalformedInput, Assert.Throws<SvgException>(() =>
            SvgDocumentPreparation.Create(input, limits, budget, CancellationToken.None)).ErrorCode);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    [Trait("Layer", "Mechanism")]
    public void Expanded_elements_keep_structural_node_limits()
    {
        byte[] input = Document("<!DOCTYPE svg [<!ENTITY box \"" + Rectangle + "\">]>", "&box;&box;");
        SvgRenderLimits limits = new(4096, 16, 256, 4096, 2, 100, 16, 16);
        RenderBudget budget = new();
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() =>
            SvgDocumentPreparation.Create(input, limits, budget, CancellationToken.None)).ErrorCode);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Theory]
    [Trait("Layer", "Contract")]
    [InlineData("<!ENTITY box '&box;'>")]
    [InlineData("<!ENTITY box '&missing;'>")]
    [InlineData("<!ENTITY box '<broken>'>")]
    public void Bcl_rejection_refunds_entity_storage_and_preserves_the_owner_for_retry(string declaration)
    {
        RenderBudget budget = new(new HostMemoryBudget(8_000_000));
        Assert.Equal(SvgErrorCode.MalformedInput, Assert.Throws<SvgException>(() =>
            SvgDocumentPreparation.Create(Document("<!DOCTYPE svg [" + declaration + "]>", "&box;"),
                SvgRenderLimits.Default, budget, CancellationToken.None)).ErrorCode);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        using (SvgDocumentPreparation retry = SvgDocumentPreparation.Create(EntityDocument(Rectangle), SvgRenderLimits.Default, budget, CancellationToken.None))
            retry.InspectDimensions(SvgRenderOptions.Default);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    [Trait("Layer", "Mechanism")]
    public void Unused_declarations_do_not_charge_the_expansion_ceiling()
    {
        StringBuilder declarations = new("<!ENTITY a 'red'>");
        for (int i = 0; i < 30; i++) declarations.Append("<!ENTITY n").Append(i).Append(" '&a;&a;'>");
        RenderBudget budget = new(new HostMemoryBudget(1_000_000));
        using (SvgDocumentPreparation preparation = SvgDocumentPreparation.Create(
            Document("<!DOCTYPE svg [" + declarations + "]>", Rectangle), SvgRenderLimits.Default, budget, CancellationToken.None))
            preparation.InspectDimensions(SvgRenderOptions.Default);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    private static byte[] EntityDocument(string content)
    {
        string encoded = string.Concat(content.Select(c => "&#" + ((int)c).ToString(CultureInfo.InvariantCulture) + ";"));
        return Document("<!DOCTYPE svg [<!ENTITY content \"" + encoded + "\">]>", "&content;");
    }

    private static byte[] Document(string prolog, string body)
        => System.Text.Encoding.UTF8.GetBytes(prolog + "<svg xmlns='http://www.w3.org/2000/svg' width='8' height='4'>" + body + "</svg>");

    private static byte[] Render(byte[] input)
        => SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None).ToArray();
}
