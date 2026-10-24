namespace Lokad.Svg.Tests;

public sealed partial class OpenTypeFontTests
{
    [Theory]
    [MemberData(nameof(SvgDisplayInheritanceTests.Cases), true, MemberType = typeof(SvgDisplayInheritanceTests))]
    public async Task Display_inheritance_agrees_between_font_discovery_preparation_and_replay(string location, string parent, bool forward, string form, string actual, string expected)
    {
        _ = (forward, form);
        (var catalog, var provider, _) = CreateExamplePack(CreateTriangleFont());
        byte[] input = System.Text.Encoding.UTF8.GetBytes(actual);
        using SvgPreparedDocument control = await SvgPreparedDocument.CreateAsync(System.Text.Encoding.UTF8.GetBytes(expected), catalog, provider, SvgRenderOptions.Default, CancellationToken.None);
        byte[] reference = control.ConvertToPng(CancellationToken.None).ToArray();
        using SvgPreparedDocument prepared = await SvgPreparedDocument.CreateAsync(input, catalog, provider, SvgRenderOptions.Default, CancellationToken.None);
        Assert.Equal(reference, prepared.ConvertToPng(CancellationToken.None).ToArray());
        if (parent == "none" && location != "root") Assert.Equal(0, provider.RequestCount);
        else Assert.True(provider.RequestCount > 0);
    }
}
