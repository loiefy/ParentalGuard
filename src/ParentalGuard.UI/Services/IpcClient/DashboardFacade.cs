using ParentalGuard.Ipc.Client;
using ParentalGuard.Ipc.Protocol;

namespace ParentalGuard.UI.Services.IpcClient;

/// <inheritdoc cref="IDashboardFacade"/>
public sealed class DashboardFacade(UiIpcClient client) : IDashboardFacade
{
    public async Task<DashboardStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        IpcPayload statusRequest = client.NewEnvelope();
        statusRequest.DashboardStatusQuery = new DashboardStatusQuery();
        DashboardStatusResponse status = await client.SendRequestAsync(statusRequest, resp => resp.DashboardStatusResp, cancellationToken).ConfigureAwait(false);

        // Mục 3.3 (ADR-120) — round-trip thứ 2 liên tiếp TRÊN CÙNG kết nối; UiIpcClient.SendRequestAsync
        // tự khoá SemaphoreSlim(1) từng lời gọi (ADR-119), không cần đồng bộ hoá thêm ở đây.
        IpcPayload pauseRequest = client.NewEnvelope();
        pauseRequest.PauseStatusQuery = new PauseStatusQuery();
        PauseStatusResponse pause = await client.SendRequestAsync(pauseRequest, resp => resp.PauseStatusResp, cancellationToken).ConfigureAwait(false);

        return new DashboardStatus(
            status.WatchdogAlive,
            status.VisionConnected,
            status.VisionDiagnosticState,
            status.OverlayConnected,
            status.UsingFallbackConfig,
            status.AuditLogFreeDiskBytes,
            status.PauseAnomalyPendingAck,
            pause.IsPaused,
            pause.PauseExpiresAtUnixMs);
    }

    public Task AcknowledgePauseAnomalyAsync(CancellationToken cancellationToken)
    {
        IpcPayload request = client.NewEnvelope();
        request.AckPauseAnomalyReq = new AcknowledgePauseAnomalyRequest();
        return client.SendRequestAsync(request, resp => resp.AckPauseAnomalyResp.Acknowledged, cancellationToken);
    }

    public async Task<IReadOnlyList<DailyBlockCount>> GetAuditChartAsync(uint rangeDays, CancellationToken cancellationToken)
    {
        IpcPayload request = client.NewEnvelope();
        request.AuditChartQuery = new AuditChartQuery { RangeDays = rangeDays };
        AuditChartResponse response = await client.SendRequestAsync(request, resp => resp.AuditChartResp, cancellationToken).ConfigureAwait(false);
        return response.Days.Select(d => new DailyBlockCount(d.DateUtc, d.BlockedCount)).ToList();
    }
}
