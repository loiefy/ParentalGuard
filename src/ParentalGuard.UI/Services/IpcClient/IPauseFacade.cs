namespace ParentalGuard.UI.Services.IpcClient;

/// <summary>
/// Facade domain Pause/Resume (Architecture/10-ui-architecture.md mục 2.2/5/6.2.2, `PAUSE-001`-`004`,
/// `02-process-architecture.md` mục 3a.1/3a.2) — dùng ở `S2` Dashboard, sau khi đã qua `S5` Auth Modal
/// (caller sở hữu <c>action_token</c> có sẵn — facade chỉ forward, không tự tạo/tự xác thực).
/// </summary>
public interface IPauseFacade
{
    /// <summary><c>PauseMonitoringRequest</c> (mục 6.2.2).</summary>
    Task<PauseMonitoringResult> PauseMonitoringAsync(byte[] actionToken, PauseDurationOption duration, CancellationToken cancellationToken);

    /// <summary><c>ResumeMonitoringRequest</c> (mục 6.2.2) — cùng <c>action_context="pause_monitoring"</c> với Pause (ADR-101 ở `02`).</summary>
    Task<ResumeMonitoringResult> ResumeMonitoringAsync(byte[] actionToken, CancellationToken cancellationToken);
}

/// <summary>5 lựa chọn thời lượng picker (`PAUSE-002`, mục 6.2.2) — 1-1 với <c>PauseDuration</c> proto.</summary>
public enum PauseDurationOption
{
    FifteenMinutes,
    ThirtyMinutes,
    OneHour,
    FourHours,
    EndOfDay,
}

public enum PauseOutcome
{
    Success,
    InvalidToken,
    AlreadyPaused,
}

public sealed record PauseMonitoringResult(PauseOutcome Outcome, long PauseExpiresAtUnixMs);

public enum ResumeOutcome
{
    Success,
    InvalidToken,
    NotPaused,
}

public sealed record ResumeMonitoringResult(ResumeOutcome Outcome);
