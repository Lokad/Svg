namespace Lokad.Svg.Tests;

[Trait("Layer", "Contract")]
public sealed class SvgClipGroupOwnershipTests
{
    [Fact]
    public void Dormant_group_and_clip_sources_release_without_refunding_borrowed_storage()
        => SvgParseOwnershipAssertions.PreservesBorrowedStorageOnDenialCancellationAndRetry(
            "<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'><defs><clipPath id='c'><path d='M0 0H16V16H0Z'/><g><g id='dormant' transform='translate(2)'><path id='leaf' d='M8 8H24V24H8Z'/></g></g><path d='M16 16H32V32H16Z'/></clipPath></defs><path d='M0 0H32V32H0Z' clip-path='url(#c)'/><use href='#leaf' fill='red'/></svg>"u8.ToArray());
}
