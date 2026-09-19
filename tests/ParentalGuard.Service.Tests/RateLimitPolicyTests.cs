using ParentalGuard.Service.Auth;

namespace ParentalGuard.Service.Tests;

/// <summary>PWD-021, Architecture/08 mục 7.7 — bảng delay luỹ tiến.</summary>
public class RateLimitPolicyTests
{
    [Theory]
    [InlineData(1, 0)]
    [InlineData(2, 0)]
    [InlineData(3, 0)]
    [InlineData(4, 30_000)]
    [InlineData(5, 30_000)]
    [InlineData(6, 5 * 60_000)]
    [InlineData(7, 5 * 60_000)]
    [InlineData(8, 5 * 60_000)]
    [InlineData(9, 30 * 60_000)]
    [InlineData(10, 30 * 60_000)]
    [InlineData(100, 30 * 60_000)]
    public void DelayMsFor_MatchesTable(int consecutiveFailures, long expectedDelayMs)
    {
        Assert.Equal(expectedDelayMs, RateLimitPolicy.DelayMsFor(consecutiveFailures));
    }

    [Fact]
    public void BruteForceAuditThreshold_IsNine()
    {
        Assert.Equal(9, RateLimitPolicy.BruteForceAuditThreshold);
    }
}
