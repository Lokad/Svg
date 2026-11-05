using Lokad.Svg.Fonts;
using Lokad.Svg.Testing;

namespace Lokad.Svg.Tests;

[Trait("Layer", "Contract")]
public sealed class RejectFontProgramSourceProviderTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Rejection_stays_synchronous_with_the_selected_cancellation_policy(bool observeCancellation)
    {
        const string message = "Unexpected program acquisition for this fixture.";
        RejectFontProgramSourceProvider provider = new(message, observeCancellation);
        SvgFontPackFile file = new("unused.ttf", 1, new string('0', 64));
        Assert.Equal(message, Assert.Throws<InvalidOperationException>(() =>
        {
            _ = provider.GetSourceAsync(file, CancellationToken.None);
        }).Message);
        using CancellationTokenSource canceled = new(); canceled.Cancel();
        if (observeCancellation)
            Assert.Equal(canceled.Token, Assert.Throws<OperationCanceledException>(() =>
            {
                _ = provider.GetSourceAsync(file, canceled.Token);
            }).CancellationToken);
        else
            Assert.Equal(message, Assert.Throws<InvalidOperationException>(() =>
            {
                _ = provider.GetSourceAsync(file, canceled.Token);
            }).Message);
    }
}
