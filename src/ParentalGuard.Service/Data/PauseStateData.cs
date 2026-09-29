namespace ParentalGuard.Service.Data;

/// <summary>
/// <c>PauseState</c> (Architecture/02-process-architecture.md mục 5.3,
/// Architecture/04-data-architecture.md mục 3.4) — Đợt 0 chỉ cần khung schema, logic
/// pause/resume thật ở Đợt 5 (PAUSE-0xx). <see cref="AnomalyPendingAck"/> thêm ở Đợt 8/9 gap-fix
/// (`PAUSE-021`, mục 3a.7 — thiết kế có từ Đợt 6, chưa từng có field code cho tới lượt này).
/// </summary>
public sealed record PauseStateData(bool IsPaused, long? PauseStartedAtUnixMs, long? PauseExpiresAtUnixMs, bool AnomalyPendingAck = false)
{
    public static PauseStateData CreateDefault() => new(false, null, null, false);
}
