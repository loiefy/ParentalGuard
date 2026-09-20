using ParentalGuard.Service.Data;
using ParentalGuard.Service.Pause;

namespace ParentalGuard.Service.Tests;

/// <summary>`PAUSE-030`, Architecture/02 mục 3a.3 — khôi phục đúng trạng thái khi <c>Service</c> restart giữa lúc đang Pause, kể cả trường hợp hết hạn lúc Service offline.</summary>
public class PauseStateRecoveryTests
{
    [Fact]
    public void Decide_NotPaused_ReturnsAsIs()
    {
        PauseStateData loaded = PauseStateData.CreateDefault();

        PauseStateRecovery.Decision decision = PauseStateRecovery.Decide(loaded, trustedNowUnixMs: 1_700_000_000_000L);

        Assert.Equal(loaded, decision.Result);
        Assert.False(decision.AutoResumedWhileOffline);
    }

    [Fact]
    public void Decide_PausedAndStillWithinExpiry_KeepsOriginalPauseStartedAt_DoesNotReset()
    {
        var loaded = new PauseStateData(IsPaused: true, PauseStartedAtUnixMs: 1_000L, PauseExpiresAtUnixMs: 2_000L);

        PauseStateRecovery.Decision decision = PauseStateRecovery.Decide(loaded, trustedNowUnixMs: 1_500L);

        Assert.Equal(loaded, decision.Result); // giữ nguyên gốc, KHÔNG reset pause_started_at
        Assert.False(decision.AutoResumedWhileOffline);
    }

    [Fact]
    public void Decide_PausedAndExpiredWhileOffline_AutoResumesImmediately()
    {
        var loaded = new PauseStateData(IsPaused: true, PauseStartedAtUnixMs: 1_000L, PauseExpiresAtUnixMs: 2_000L);

        PauseStateRecovery.Decision decision = PauseStateRecovery.Decide(loaded, trustedNowUnixMs: 5_000L);

        Assert.False(decision.Result.IsPaused);
        Assert.Null(decision.Result.PauseStartedAtUnixMs);
        Assert.Null(decision.Result.PauseExpiresAtUnixMs);
        Assert.True(decision.AutoResumedWhileOffline);
    }

    [Fact]
    public void Decide_PausedAndExpiresExactlyAtTrustedNow_AutoResumes()
    {
        var loaded = new PauseStateData(IsPaused: true, PauseStartedAtUnixMs: 1_000L, PauseExpiresAtUnixMs: 2_000L);

        PauseStateRecovery.Decision decision = PauseStateRecovery.Decide(loaded, trustedNowUnixMs: 2_000L);

        Assert.True(decision.AutoResumedWhileOffline);
    }
}
