using ParentalGuard.Service.Data;

namespace ParentalGuard.Service.Pause;

/// <summary>
/// Quyết định thuần (không I/O) cho luồng khôi phục Pause lúc <c>Service</c> <c>Starting</c>
/// (Architecture/02-process-architecture.md mục 3a.3, <c>PAUSE-030</c>) — tách khỏi <c>Worker</c> để
/// unit test được logic mà không cần spawn process/ACL thật. <c>Worker</c> thực hiện phần I/O
/// (ghi <c>config.db</c>, audit log) dựa theo <see cref="Decision"/> trả về.
/// </summary>
public static class PauseStateRecovery
{
    public readonly record struct Decision(PauseStateData Result, bool AutoResumedWhileOffline);

    public static Decision Decide(PauseStateData loaded, long trustedNowUnixMs)
    {
        if (!loaded.IsPaused)
        {
            return new Decision(loaded, AutoResumedWhileOffline: false);
        }

        if (loaded.PauseExpiresAtUnixMs is long expiresAt && expiresAt > trustedNowUnixMs)
        {
            // Còn hạn — quay thẳng vào Running·Paused, giữ nguyên pause_started_at gốc, KHÔNG reset.
            return new Decision(loaded, AutoResumedWhileOffline: false);
        }

        // Hết hạn TRONG LÚC Service down (vd máy tắt qua đêm lúc đang pause) — auto-resume NGAY
        // trong bước Starting, trước khi vào Running·Monitoring chính thức.
        //
        // 2026-09-29 audit fix (FAIL cứng do test-runner phát hiện) — GIỮ NGUYÊN AnomalyPendingAck
        // của loaded (KHÔNG dùng CreateDefault() trần trụi, sẽ âm thầm reset cờ về false dù phụ huynh
        // CHƯA từng gửi AcknowledgePauseAnomalyRequest): cùng bất biến đã áp dụng đúng ở
        // PauseCoordinator.ApplyResumeAsync (mục 4a) nhưng bị bỏ sót ở nhánh boot-recovery này.
        return new Decision(PauseStateData.CreateDefault() with { AnomalyPendingAck = loaded.AnomalyPendingAck }, AutoResumedWhileOffline: true);
    }
}
