namespace ParentalGuard.UI.Services.IpcClient;

/// <summary>
/// Facade domain Dashboard status/health check/biểu đồ (Architecture/10-ui-architecture.md mục
/// 2.2/5/6.2, `MISC-050`/`FE-041`/`PAUSE-021`/`FE-070`-`072`) — dùng ở `S2`.
/// </summary>
public interface IDashboardFacade
{
    /// <summary>
    /// Gộp <c>DashboardStatusQuery</c> + <c>PauseStatusQuery</c> thành 2 round-trip liên tiếp trên
    /// CÙNG kết nối (mục 3.3, ADR-120).
    /// </summary>
    Task<DashboardStatus> GetStatusAsync(CancellationToken cancellationToken);

    /// <summary><c>AcknowledgePauseAnomalyRequest</c> (mục 6.2.3) — không gate, thuần cờ thông tin (`02` mục 3a.7).</summary>
    Task AcknowledgePauseAnomalyAsync(CancellationToken cancellationToken);

    /// <summary><c>AuditChartQuery{range_days}</c> (mục 6.2.5, `FE-071`) — Phase 1 chỉ hỗ trợ 7 hoặc 30.</summary>
    Task<IReadOnlyList<DailyBlockCount>> GetAuditChartAsync(uint rangeDays, CancellationToken cancellationToken);
}

/// <summary>POCO gộp <c>DashboardStatusResponse</c> + <c>PauseStatusResponse</c> (mục 6.2.1/6.2.4).</summary>
public sealed record DashboardStatus(
    bool WatchdogAlive,
    bool VisionConnected,
    string VisionDiagnosticState,
    bool OverlayConnected,
    bool UsingFallbackConfig,
    long AuditLogFreeDiskBytes,
    bool PauseAnomalyPendingAck,
    bool IsPaused,
    long PauseExpiresAtUnixMs);

public sealed record DailyBlockCount(string DateUtc, uint BlockedCount);
