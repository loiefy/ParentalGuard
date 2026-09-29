using Google.Protobuf;
using Microsoft.Extensions.Logging.Abstractions;
using ParentalGuard.Ipc.Protocol;
using ParentalGuard.Service.Audit;
using ParentalGuard.Service.Auth;
using ParentalGuard.Service.Dashboard;
using ParentalGuard.Service.Data;
using ParentalGuard.Service.Ipc;
using ParentalGuard.Service.Pause;
using ParentalGuard.Service.Tamper;

namespace ParentalGuard.Service.Tests;

/// <summary>
/// Gap fix Đợt 8/9 (`10-ui-architecture.md` mục 6.2, Architecture/03 mục 3.7a) — `DashboardStatusQuery`/
/// `AuditChartQuery` cuối cùng đóng nhóm "IPC message đã định nghĩa Đợt 6 nhưng Service chưa implement
/// handler". Chỉ test qua <see cref="DashboardCoordinator.HandleAsync"/> (production path).
/// </summary>
public class DashboardCoordinatorTests : IDisposable
{
    private readonly string _authDatPath = Path.Combine(Path.GetTempPath(), $"pg-auth-dash-{Guid.NewGuid():N}.dat");
    private readonly string _auditLogPath = Path.Combine(Path.GetTempPath(), $"pg-audit-dash-{Guid.NewGuid():N}.log");
    private readonly string _configDbPath = Path.Combine(Path.GetTempPath(), $"pg-config-dash-{Guid.NewGuid():N}.db");
    private readonly CancellationTokenSource _serviceCts = new();

    private sealed record Fixture(
        DashboardCoordinator Dashboard,
        ChildProcessSupervisor Vision,
        ChildProcessSupervisor Overlay,
        WatchdogSessionServer Watchdog,
        MonitoringStateHolder Holder,
        PauseCoordinator Pause,
        AuthCoordinator Auth,
        AuditLogWriter AuditLog,
        FakeMonotonicClock Clock);

    private async Task<Fixture> CreateAsync()
    {
        MonitoringStateData state = MonitoringStateData.CreateFirstRunDefault();
        ConfigDb.CreateFresh(_configDbPath, state, PauseStateData.CreateDefault(), new byte[32], AuditCheckpoint.CreateGenesis(), lastFallbackEventUnixMs: null);

        AuditLogWriter auditLog = await AuditLogWriter.InitializeAsync(_auditLogPath, CancellationToken.None);
        var clock = new FakeMonotonicClock();
        var authCoordinator = new AuthCoordinator(_authDatPath, auditLog, clock, NullLogger.Instance);
        var holder = new MonitoringStateHolder(state);

        var visionSupervisor = new ChildProcessSupervisor(ProcessType.Vision, "test-vision-pipe", "vision.exe", TimeSpan.FromSeconds(1), null, new byte[32], auditLog, NullLogger.Instance);
        var overlaySupervisor = new ChildProcessSupervisor(ProcessType.Overlay, "test-overlay-pipe", "overlay.exe", TimeSpan.FromSeconds(2), null, new byte[32], auditLog, NullLogger.Instance);
        var iconStatus = new IconStatusCoordinator(overlaySupervisor);
        var overlayDecision = new OverlayDecisionCoordinator(overlaySupervisor, auditLog, () => 0.7f);
        var watchdog = new WatchdogSessionServer("test-watchdog-pipe", "watchdog.exe", auditLog, new AttackPatternCoordinator(auditLog, iconStatus), NullLogger.Instance);

        var pauseCoordinator = new PauseCoordinator(
            _configDbPath,
            _auditLogPath,
            authCoordinator,
            auditLog,
            clock,
            visionSupervisor,
            overlaySupervisor,
            overlayDecision,
            iconStatus,
            enabled => new ControlVisionCommand { MonitoringEnabled = enabled },
            PauseStateData.CreateDefault(),
            NullLogger.Instance);
        pauseCoordinator.Start(_serviceCts.Token);

        var dashboard = new DashboardCoordinator(visionSupervisor, overlaySupervisor, watchdog, holder, pauseCoordinator, _auditLogPath);
        return new Fixture(dashboard, visionSupervisor, overlaySupervisor, watchdog, holder, pauseCoordinator, authCoordinator, auditLog, clock);
    }

    /// <summary>
    /// `AuditChartQuery` đọc TRỰC TIẾP raw JSON line (`event_type`/`ts_unix_ms`), không qua verify
    /// hash-chain — ghi thẳng 1 dòng JSONL với timestamp tuỳ ý để test zero-fill nhiều ngày, thay vì
    /// dùng `AuditLogWriter.AppendAsync` (luôn stamp `DateTimeOffset.UtcNow`, không cho phép backdate).
    /// </summary>
    private static Task WriteContentBlockedAsync(string auditLogPath, DateTimeOffset at) =>
        File.AppendAllTextAsync(auditLogPath, $$"""{"seq":0,"ts_unix_ms":{{at.ToUnixTimeMilliseconds()}},"chain_id":"test","event_type":"ContentBlocked","detail":{"processName":"chrome.exe","riskScore":0.9},"prev_hash":"0","hash":"0"}""" + Environment.NewLine);

    /// <summary>
    /// ADR-146 — test tường minh "no-auth-required": `DashboardStatusQuery` xử lý đúng mà KHÔNG cần
    /// `AuthVerifyRequest` nào trước đó, không chỉ ngầm định vì test không truyền token (bài học sau
    /// 2 FAIL cứng liên tiếp Đợt 7/8 do để ngầm hiểu quyết định gate).
    /// </summary>
    [Fact]
    public async Task DashboardStatusQuery_NoAuthRequired_ReturnsAggregatedStatus()
    {
        Fixture fx = await CreateAsync();
        fx.Vision.IsConnected = true;
        fx.Vision.LastDiagnosticState = "cpu_fallback";
        fx.Overlay.IsConnected = false;
        fx.Watchdog.IsAlive = true;

        IpcPayload response = await fx.Dashboard.HandleAsync(new IpcPayload { MessageId = 1, DashboardStatusQuery = new DashboardStatusQuery() }, CancellationToken.None);

        DashboardStatusResponse resp = response.DashboardStatusResp;
        Assert.True(resp.WatchdogAlive);
        Assert.True(resp.VisionConnected);
        Assert.Equal("cpu_fallback", resp.VisionDiagnosticState);
        Assert.False(resp.OverlayConnected);
        Assert.False(resp.UsingFallbackConfig);
        Assert.False(resp.PauseAnomalyPendingAck);
    }

    [Fact]
    public async Task DashboardStatusQuery_ReflectsUsingFallbackConfigFromHolder()
    {
        Fixture fx = await CreateAsync();
        fx.Holder.Update(fx.Holder.Current with { UsingFallbackConfig = true });

        IpcPayload response = await fx.Dashboard.HandleAsync(new IpcPayload { MessageId = 2, DashboardStatusQuery = new DashboardStatusQuery() }, CancellationToken.None);

        Assert.True(response.DashboardStatusResp.UsingFallbackConfig);
    }

    [Fact]
    public async Task DashboardStatusQuery_ReflectsPauseAnomalyPendingAckFromPauseCoordinator()
    {
        Fixture fx = await CreateAsync();
        fx.Clock.Now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await TriggerAnomalyAsync(fx);

        IpcPayload response = await fx.Dashboard.HandleAsync(new IpcPayload { MessageId = 3, DashboardStatusQuery = new DashboardStatusQuery() }, CancellationToken.None);

        Assert.True(response.DashboardStatusResp.PauseAnomalyPendingAck);
    }

    /// <summary>ADR-146 — test tường minh "no-auth-required" cho `AuditChartQuery`.</summary>
    [Fact]
    public async Task AuditChartQuery_NoAuthRequired_ZeroFillsSevenDaysAndCountsContentBlocked()
    {
        Fixture fx = await CreateAsync();
        DateTimeOffset today = DateTimeOffset.UtcNow;
        await WriteContentBlockedAsync(_auditLogPath, today);
        await WriteContentBlockedAsync(_auditLogPath, today);
        await WriteContentBlockedAsync(_auditLogPath, today.AddDays(-2));

        IpcPayload response = await fx.Dashboard.HandleAsync(
            new IpcPayload { MessageId = 4, AuditChartQuery = new AuditChartQuery { RangeDays = 7 } }, CancellationToken.None);

        AuditChartResponse resp = response.AuditChartResp;
        Assert.Equal(7, resp.Days.Count);
        DailyBlockCount todayEntry = Assert.Single(resp.Days, d => d.DateUtc == DateOnly.FromDateTime(today.UtcDateTime).ToString("yyyy-MM-dd"));
        Assert.Equal(2u, todayEntry.BlockedCount);
        DailyBlockCount twoDaysAgoEntry = Assert.Single(resp.Days, d => d.DateUtc == DateOnly.FromDateTime(today.AddDays(-2).UtcDateTime).ToString("yyyy-MM-dd"));
        Assert.Equal(1u, twoDaysAgoEntry.BlockedCount);
        // Zero-fill — ngày không có event nào vẫn phải xuất hiện với blocked_count=0, không bị bỏ qua.
        DailyBlockCount yesterdayEntry = Assert.Single(resp.Days, d => d.DateUtc == DateOnly.FromDateTime(today.AddDays(-1).UtcDateTime).ToString("yyyy-MM-dd"));
        Assert.Equal(0u, yesterdayEntry.BlockedCount);
    }

    [Fact]
    public async Task AuditChartQuery_RangeDaysNot7Or30_DefaultsToSeven()
    {
        Fixture fx = await CreateAsync();

        IpcPayload response = await fx.Dashboard.HandleAsync(
            new IpcPayload { MessageId = 5, AuditChartQuery = new AuditChartQuery { RangeDays = 15 } }, CancellationToken.None);

        Assert.Equal(7, response.AuditChartResp.Days.Count);
    }

    [Fact]
    public async Task AuditChartQuery_RangeDays30_Returns30Days()
    {
        Fixture fx = await CreateAsync();

        IpcPayload response = await fx.Dashboard.HandleAsync(
            new IpcPayload { MessageId = 6, AuditChartQuery = new AuditChartQuery { RangeDays = 30 } }, CancellationToken.None);

        Assert.Equal(30, response.AuditChartResp.Days.Count);
    }

    private static async Task TriggerAnomalyAsync(Fixture fx)
    {
        for (int i = 0; i < 6; i++)
        {
            byte[] pauseToken = await GetValidActionTokenAsync(fx.Auth);
            await fx.Pause.HandleAsync(
                new IpcPayload { MessageId = (ulong)(500 + i * 2), PauseMonitoringReq = new PauseMonitoringRequest { ActionToken = ByteString.CopyFrom(pauseToken), Duration = PauseDuration.FifteenMinutes } },
                CancellationToken.None);
            byte[] resumeToken = await GetValidActionTokenAsync(fx.Auth);
            await fx.Pause.HandleAsync(
                new IpcPayload { MessageId = (ulong)(501 + i * 2), ResumeMonitoringReq = new ResumeMonitoringRequest { ActionToken = ByteString.CopyFrom(resumeToken) } },
                CancellationToken.None);
        }
    }

    private static async Task<byte[]> GetValidActionTokenAsync(AuthCoordinator auth)
    {
        IpcPayload setupResponse = await auth.HandleAsync(
            new IpcPayload { MessageId = 1, SetInitialPasswordReq = new SetInitialPasswordRequest { Password = ByteString.CopyFromUtf8("Passw0rd!") } }, CancellationToken.None);
        await auth.HandleAsync(
            new IpcPayload { MessageId = 2, ConfirmRecoveryReq = new ConfirmRecoveryKeySavedRequest { SetupToken = setupResponse.SetInitialPasswordResp.SetupToken, Confirmed = true } }, CancellationToken.None);
        IpcPayload verifyResponse = await auth.HandleAsync(
            new IpcPayload { MessageId = 3, AuthVerifyReq = new AuthVerifyRequest { Password = ByteString.CopyFromUtf8("Passw0rd!"), ActionContext = "pause_monitoring" } }, CancellationToken.None);
        Assert.Equal(AuthResult.Success, verifyResponse.AuthVerifyResp.Result);
        return verifyResponse.AuthVerifyResp.ActionToken.ToByteArray();
    }

    public void Dispose()
    {
        _serviceCts.Cancel();
        _serviceCts.Dispose();
        File.Delete(_authDatPath);
        File.Delete(_auditLogPath);
        foreach (string suffix in new[] { string.Empty, "-wal", "-shm" })
        {
            try
            {
                File.Delete(_configDbPath + suffix);
            }
            catch (IOException)
            {
            }
        }
    }
}
