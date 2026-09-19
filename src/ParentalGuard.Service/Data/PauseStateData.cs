namespace ParentalGuard.Service.Data;

/// <summary>
/// <c>PauseState</c> (Architecture/02-process-architecture.md mục 5.3,
/// Architecture/04-data-architecture.md mục 3.4) — Đợt 0 chỉ cần khung schema, logic
/// pause/resume thật ở Đợt 5 (PAUSE-0xx).
/// </summary>
public sealed record PauseStateData(bool IsPaused, long? PauseStartedAtUnixMs, long? PauseExpiresAtUnixMs)
{
    public static PauseStateData CreateDefault() => new(false, null, null);
}
