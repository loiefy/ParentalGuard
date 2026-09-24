namespace ParentalGuard.UI.Services.IpcClient;

/// <summary>
/// Facade domain Pause/Resume (Architecture/10-ui-architecture.md mục 2.2/5, `PAUSE-0xx`) — dùng ở
/// `S2` Dashboard. CHƯA implement ở giai đoạn nền tảng này (chỉ `S1`/`S5`) — điền ở giai đoạn viết
/// `S2` (`DashboardStatusQuery`/`PauseMonitoringRequest`/`ResumeMonitoringRequest`).
/// </summary>
public interface IPauseFacade;
