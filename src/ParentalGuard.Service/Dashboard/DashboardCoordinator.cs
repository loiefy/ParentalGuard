using ParentalGuard.Ipc.Framing;
using ParentalGuard.Ipc.Protocol;
using ParentalGuard.Service.Audit;
using ParentalGuard.Service.Data;
using ParentalGuard.Service.Ipc;
using ParentalGuard.Service.Pause;

namespace ParentalGuard.Service.Dashboard;

/// <summary>
/// Orchestrator domain Dashboard (`10-ui-architecture.md` mục 6.2, `MISC-050`/`FE-070`-`072`,
/// Architecture/03-ipc-communication.md mục 3.7a, ADR-144/145/146) — nhận <c>DashboardStatusQuery</c>/
/// <c>AuditChartQuery</c> từ pipe <c>UI</c> (Đợt 6 gap-fix cuối cùng, đóng Đợt 8/9). Thuần đọc tổng hợp
/// từ state đã có sẵn ở nơi khác — KHÔNG tự giữ state riêng, KHÔNG gate `action_token` (ADR-146: cùng
/// nhóm "biểu đồ/tổng quan sức khoẻ" như `AuditChartQuery`/biểu đồ `S2` đã chốt ở ADR-121, khác danh
/// sách chi tiết `AuditLogQuery` có gate `view_audit_log`).
/// </summary>
public sealed class DashboardCoordinator(
    ChildProcessSupervisor visionSupervisor,
    ChildProcessSupervisor overlaySupervisor,
    WatchdogSessionServer watchdogSessionServer,
    MonitoringStateHolder monitoringStateHolder,
    PauseCoordinator pauseCoordinator,
    string auditLogPath)
{
    private readonly IpcMessageIdGenerator _messageIds = new();

    /// <summary>Định tuyến theo <see cref="IpcPayload.BodyOneofCase"/> — pipe UI gọi đúng hàm này cho domain Dashboard (field 98-99/144-145).</summary>
    public Task<IpcPayload> HandleAsync(IpcPayload request, CancellationToken cancellationToken) => request.BodyCase switch
    {
        IpcPayload.BodyOneofCase.DashboardStatusQuery => Task.FromResult(HandleDashboardStatus(request)),
        IpcPayload.BodyOneofCase.AuditChartQuery => HandleAuditChartAsync(request, cancellationToken),
        _ => throw new InvalidOperationException($"DashboardCoordinator received unexpected message: {request.BodyCase}."),
    };

    /// <summary>
    /// Mục 6.2 (`MISC-050` Health Check) — `vision_connected`/`overlay_connected` nghĩa là "đang kết
    /// nối pipe active" (ADR-144), KHÔNG PHẢI "heartbeat gần đây tốt" — không bị ảnh hưởng bởi Pause
    /// (Pause chỉ đổi cadence heartbeat Vision, không ngắt kết nối, `ChildProcessSupervisor` vẫn
    /// `IsConnected=true` suốt lúc Paused).
    /// </summary>
    private IpcPayload HandleDashboardStatus(IpcPayload request)
    {
        MonitoringStateData state = monitoringStateHolder.Current;
        IpcPayload response = NewResponse(request);
        response.DashboardStatusResp = new DashboardStatusResponse
        {
            WatchdogAlive = watchdogSessionServer.IsAlive,
            VisionConnected = visionSupervisor.IsConnected,
            VisionDiagnosticState = visionSupervisor.LastDiagnosticState,
            OverlayConnected = overlaySupervisor.IsConnected,
            UsingFallbackConfig = state.UsingFallbackConfig,
            AuditLogFreeDiskBytes = GetAuditLogFreeDiskBytesBestEffort(),
            PauseAnomalyPendingAck = pauseCoordinator.AnomalyPendingAck,
        };
        return response;
    }

    /// <summary>Best-effort — `DriveInfo` có thể ném nếu ổ đĩa chứa `audit.log` không truy vấn được tạm thời; không chặn phần còn lại của response.</summary>
    private long GetAuditLogFreeDiskBytesBestEffort()
    {
        try
        {
            string? root = Path.GetPathRoot(Path.GetFullPath(auditLogPath));
            return string.IsNullOrEmpty(root) ? 0 : new DriveInfo(root).AvailableFreeSpace;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    /// <summary>
    /// `FE-070`-`072` — quét trực tiếp `audit.log` (không cache, hành động on-demand hiếm khi gọi so
    /// với poll 5s của `DashboardStatusQuery`), đếm `event_type="ContentBlocked"` theo ngày UTC,
    /// zero-fill đủ <c>range_days</c> ngày liên tục (kể cả ngày không có event nào).
    /// </summary>
    private async Task<IpcPayload> HandleAuditChartAsync(IpcPayload request, CancellationToken cancellationToken)
    {
        AuditChartQuery req = request.AuditChartQuery;
        uint rangeDays = req.RangeDays is 7 or 30 ? req.RangeDays : 7; // FE-071 — chỉ hỗ trợ 2 giá trị, mặc định an toàn nếu client gửi sai.

        Dictionary<DateOnly, uint> countsByDay = [];
        if (File.Exists(auditLogPath))
        {
            string[] lines = await File.ReadAllLinesAsync(auditLogPath, cancellationToken).ConfigureAwait(false);
            foreach (string line in lines)
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                AuditLogEntryRaw? entry = TryParseContentBlockedDate(line);
                if (entry is null)
                {
                    continue;
                }

                DateOnly date = DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeMilliseconds(entry.TsUnixMs).UtcDateTime);
                countsByDay[date] = countsByDay.GetValueOrDefault(date) + 1;
            }
        }

        DateOnly today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime);
        var resp = new AuditChartResponse();
        for (int i = (int)rangeDays - 1; i >= 0; i--)
        {
            DateOnly day = today.AddDays(-i);
            resp.Days.Add(new DailyBlockCount { DateUtc = day.ToString("yyyy-MM-dd"), BlockedCount = countsByDay.GetValueOrDefault(day) });
        }

        IpcPayload response = NewResponse(request);
        response.AuditChartResp = resp;
        return response;
    }

    private static AuditLogEntryRaw? TryParseContentBlockedDate(string line)
    {
        try
        {
            System.Text.Json.Nodes.JsonObject obj = System.Text.Json.Nodes.JsonNode.Parse(line)!.AsObject();
            string? eventType = obj["event_type"]?.GetValue<string>();
            if (eventType != "ContentBlocked")
            {
                return null;
            }

            long tsUnixMs = obj["ts_unix_ms"]!.GetValue<long>();
            return new AuditLogEntryRaw(0, tsUnixMs, eventType, "{}", "", 0f);
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidOperationException or FormatException or NullReferenceException)
        {
            return null; // Dòng hỏng/không parse được — fail-secure nghiêng về phía bỏ qua, không chặn cả biểu đồ (cùng tinh thần ReadPageAsync).
        }
    }

    private IpcPayload NewResponse(IpcPayload request) =>
        IpcEnvelope.NewEnvelope(ProcessType.Service, _messageIds.Next(), correlationId: request.MessageId);
}
