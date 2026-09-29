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

    /// <summary>
    /// Audit fix 2026-09-29 (FAIL cứng do test-runner phát hiện): trước fix, `Decide` dùng
    /// <c>PauseStateData.CreateDefault()</c> trần trụi cho nhánh auto-resume-while-offline, âm thầm
    /// reset <see cref="PauseStateData.AnomalyPendingAck"/> về <c>false</c> dù phụ huynh CHƯA từng gửi
    /// `AcknowledgePauseAnomalyRequest` — cùng bất biến đã áp dụng đúng ở
    /// <c>PauseCoordinator.ApplyResumeAsync</c> nhưng bị bỏ sót ở nhánh boot-recovery này. Test này PHẢI
    /// FAIL nếu ai đó lỡ revert về <c>CreateDefault()</c> trần trụi.
    /// </summary>
    [Fact]
    public void Decide_PausedAndExpiredWhileOffline_PreservesAnomalyPendingAck()
    {
        var loaded = new PauseStateData(IsPaused: true, PauseStartedAtUnixMs: 1_000L, PauseExpiresAtUnixMs: 2_000L, AnomalyPendingAck: true);

        PauseStateRecovery.Decision decision = PauseStateRecovery.Decide(loaded, trustedNowUnixMs: 5_000L);

        Assert.True(decision.AutoResumedWhileOffline);
        Assert.False(decision.Result.IsPaused);
        Assert.True(decision.Result.AnomalyPendingAck);
    }

    /// <summary>Kịch bản còn hạn (không auto-resume) cũng phải giữ nguyên nguyên vẹn <see cref="PauseStateData"/> — đã cover bởi <see cref="Decide_PausedAndStillWithinExpiry_KeepsOriginalPauseStartedAt_DoesNotReset"/> (dùng <c>Assert.Equal(loaded, ...)</c> toàn bộ record, bao gồm cả field mới này).</summary>
}
