using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

[Trait("Layer", "Contract")]
public sealed class SvgLanguageCasingOwnershipTests
{
    [Fact]
    public void Deletion_projection_releases_partial_metadata_after_refusal_cancellation_and_retry()
    {
        RenderBudget generous = new(new HostMemoryBudget(1_000_000));
        Project(generous, CancellationToken.None);
        Assert.Equal(0, generous.Snapshot().CurrentBytes);
        HostMemoryBudget host = new(generous.Snapshot().PeakBytes - 1);
        RenderBudget limited = new(host);
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => Project(limited, CancellationToken.None)).ErrorCode);
        Assert.Equal(0, limited.Snapshot().CurrentBytes);
        using CancellationTokenSource cancelled = new();
        cancelled.Cancel();
        Assert.Equal(cancelled.Token, Assert.Throws<OperationCanceledException>(() => Project(generous, cancelled.Token)).CancellationToken);
        host.Limit = generous.Snapshot().PeakBytes;
        Project(limited, CancellationToken.None);
        Assert.Equal(0, limited.Snapshot().CurrentBytes);

        static void Project(RenderBudget budget, CancellationToken token)
        {
            SvgTextStyle style = new(new("Example", 400, false), 20, 0, 0, 0, 0, 0, SvgTextAnchor.Start, "retry")
                { TextTransform = SvgTextTransform.Lowercase, Language = "tr", PositionLists = new("12 80 132", null, "0 7 3", null, "5 15 25", new(20)) };
            using SvgTextProjection projection = new(style, "I\u0307a", [], [], [], [],
                [new(1, true) { PreviousElementIdentity = 1, ElementIdentity = 0 }, new(2, true) { PreviousElementIdentity = 0, ElementIdentity = 2 }],
                [new(0, 1, null, null), new(1, 2, new("p", 0, false, null), null), new(2, 3, null, new(100, 20))],
                [new(0, 1, 0, 0, false), new(2, 3, 2, 0, false), new(0, 3, 1, 4, true)], budget, token);
            Assert.Equal("ia", projection.Value);
            Assert.Equal(2, projection.Placements.Length);
            Assert.Equal(2, projection.Recordings.Length);
        }
    }
}
