using ParentalGuard.Service.Ipc;

namespace ParentalGuard.Service.Tests;

public class OverlayThresholdDecisionTests
{
    [Theory]
    [InlineData(0.7f, 0.7f, true)] // IMG-013: "≥" ngưỡng tính là vi phạm, không phải "vượt hẳn".
    [InlineData(0.71f, 0.7f, true)]
    [InlineData(0.69f, 0.7f, false)]
    [InlineData(0f, 0.7f, false)]
    [InlineData(1f, 0.7f, true)]
    public void Violates_MatchesGreaterThanOrEqualSemantics(float riskScore, float threshold, bool expected)
    {
        Assert.Equal(expected, OverlayThresholdDecision.Violates(riskScore, threshold));
    }
}
