namespace Lokad.Svg.Tests;

[Trait("Layer", "Contract")]
public sealed class SvgRectangleShaderOwnershipTests
{
    [Theory]
    [InlineData("clip")]
    [InlineData("mask")]
    [InlineData("filter")]
    [InlineData("viewport")]
    public void Retained_shader_frames_release_storage_and_retry_after_denial_or_cancellation(string effect)
    {
        const string resources = "<defs><linearGradient id='p'><stop stop-color='red'/><stop offset='1' stop-color='blue'/></linearGradient><clipPath id='c'><rect x='2' y='2' width='26' height='26'/></clipPath><mask id='m' mask-type='alpha' maskUnits='userSpaceOnUse' x='0' y='0' width='32' height='32'><rect width='32' height='32' fill='white' fill-opacity='.5'/></mask><filter id='f' filterUnits='userSpaceOnUse' x='0' y='0' width='32' height='32'><feOffset dx='1' dy='1'/></filter><rect id='s' x='4' y='4' width='20' height='20' fill='green' stroke='url(#p)' stroke-width='1.5' stroke-opacity='.75'/></defs>";
        string body = "<g transform='scale(.75 1.1)' opacity='.5'><use href='#s'/></g>";
        body = effect switch
        {
            "clip" => "<g clip-path='url(#c)'>" + body + "</g>",
            "mask" => "<g mask='url(#m)'>" + body + "</g>",
            "filter" => "<g filter='url(#f)'>" + body + "</g>",
            _ => "<svg x='1' y='1' width='30' height='30' viewBox='0 0 32 32'>" + body + "</svg>"
        };
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'>" + resources + body + "</svg>");
        SvgRenderOptions options = new(null, 1.5, SvgColor.Transparent, SvgRenderLimits.Default);
        SvgRasterOwnershipAssertions.SeparateBudgetsPreserveBorrowedSceneAfterFailures(input, options, 8_000_000);
    }
}
