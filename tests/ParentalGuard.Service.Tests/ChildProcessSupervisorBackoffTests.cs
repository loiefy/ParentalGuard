using ParentalGuard.Service.Ipc;

namespace ParentalGuard.Service.Tests;

/// <summary>
/// Bug real-hardware 2026-10-01 — vòng crash-restart không backoff (~230 lần/giây). Lần đầu restart
/// ngay (ngân sách BE-023), lặp liên tiếp giãn dần, chặn trần 5s.
/// </summary>
public class ChildProcessSupervisorBackoffTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 0)]
    [InlineData(2, 500)]
    [InlineData(3, 1000)]
    [InlineData(4, 2000)]
    [InlineData(5, 4000)]
    [InlineData(6, 5000)]
    [InlineData(50, 5000)]
    public void RestartBackoff_GrowsExponentially_CappedAtFiveSeconds(int consecutiveFailures, int expectedMs) =>
        Assert.Equal(TimeSpan.FromMilliseconds(expectedMs), ChildProcessSupervisor.RestartBackoff(consecutiveFailures));
}
