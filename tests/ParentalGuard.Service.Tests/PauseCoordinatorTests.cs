using System.Text.Json;
using Google.Protobuf;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using ParentalGuard.Ipc.Protocol;
using ParentalGuard.Service.Audit;
using ParentalGuard.Service.Auth;
using ParentalGuard.Service.Data;
using ParentalGuard.Service.Ipc;
using ParentalGuard.Service.Pause;
using ParentalGuard.Service.Tamper;

namespace ParentalGuard.Service.Tests;

/// <summary>
/// `PAUSE-001`-`004`/`020`/`021`/`030`/`031` (Architecture/02-process-architecture.md mục 3a) — chỉ
/// test được qua <see cref="PauseCoordinator.HandleAsync"/> (production path), không cần pipe/UI
/// thật (giống mẫu hình <c>UninstallCoordinatorTests</c>).
/// </summary>
public class PauseCoordinatorTests : IDisposable
{
    private readonly string _authDatPath = Path.Combine(Path.GetTempPath(), $"pg-auth-pause-{Guid.NewGuid():N}.dat");
    private readonly string _auditLogPath = Path.Combine(Path.GetTempPath(), $"pg-audit-pause-{Guid.NewGuid():N}.log");
    private readonly string _configDbPath = Path.Combine(Path.GetTempPath(), $"pg-config-pause-{Guid.NewGuid():N}.db");
    private readonly CancellationTokenSource _serviceCts = new();

    private sealed record Fixture(
        PauseCoordinator Pause,
        AuthCoordinator Auth,
        FakeMonotonicClock Clock,
        AuditLogWriter AuditLog,
        AttackPatternCoordinator AttackPattern);

    private async Task<Fixture> CreateAsync(PauseStateData? initial = null)
    {
        ConfigDb.CreateFresh(_configDbPath, MonitoringStateData.CreateFirstRunDefault(), PauseStateData.CreateDefault(), new byte[32], AuditCheckpoint.CreateGenesis(), lastFallbackEventUnixMs: null);

        AuditLogWriter auditLog = await AuditLogWriter.InitializeAsync(_auditLogPath, CancellationToken.None);
        var clock = new FakeMonotonicClock();
        var authCoordinator = new AuthCoordinator(_authDatPath, auditLog, clock, NullLogger.Instance);

        var overlaySupervisor = new ChildProcessSupervisor(ProcessType.Overlay, "test-overlay-pipe", "overlay.exe", TimeSpan.FromSeconds(2), null, new byte[32], auditLog, NullLogger.Instance);
        var visionSupervisor = new ChildProcessSupervisor(ProcessType.Vision, "test-vision-pipe", "vision.exe", TimeSpan.FromSeconds(1), null, new byte[32], auditLog, NullLogger.Instance);
        var iconStatus = new IconStatusCoordinator(overlaySupervisor);
        var overlayDecision = new OverlayDecisionCoordinator(overlaySupervisor, auditLog, () => 0.7f);
        var attackPattern = new AttackPatternCoordinator(auditLog, iconStatus);

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
            initial ?? PauseStateData.CreateDefault(),
            NullLogger.Instance);
        pauseCoordinator.Start(_serviceCts.Token);

        return new Fixture(pauseCoordinator, authCoordinator, clock, auditLog, attackPattern);
    }

    private static async Task<byte[]> GetValidActionTokenAsync(AuthCoordinator auth, string actionContext = "pause_monitoring")
    {
        IpcPayload setupResponse = await auth.HandleAsync(
            new IpcPayload { MessageId = 1, SetInitialPasswordReq = new SetInitialPasswordRequest { Password = ByteString.CopyFromUtf8("Passw0rd!") } }, CancellationToken.None);
        await auth.HandleAsync(
            new IpcPayload { MessageId = 2, ConfirmRecoveryReq = new ConfirmRecoveryKeySavedRequest { SetupToken = setupResponse.SetInitialPasswordResp.SetupToken, Confirmed = true } }, CancellationToken.None);
        IpcPayload verifyResponse = await auth.HandleAsync(
            new IpcPayload { MessageId = 3, AuthVerifyReq = new AuthVerifyRequest { Password = ByteString.CopyFromUtf8("Passw0rd!"), ActionContext = actionContext } }, CancellationToken.None);
        Assert.Equal(AuthResult.Success, verifyResponse.AuthVerifyResp.Result);
        return verifyResponse.AuthVerifyResp.ActionToken.ToByteArray();
    }

    [Fact]
    public async Task HandlePause_InvalidToken_ReturnsInvalidTokenAndDoesNotPause()
    {
        Fixture fx = await CreateAsync();

        IpcPayload response = await fx.Pause.HandleAsync(
            new IpcPayload { MessageId = 10, PauseMonitoringReq = new PauseMonitoringRequest { ActionToken = ByteString.CopyFrom([1, 2, 3]), Duration = PauseDuration.FifteenMinutes } },
            CancellationToken.None);

        Assert.Equal(PauseResult.InvalidToken, response.PauseMonitoringResp.Result);
        Assert.False(fx.Pause.IsPaused);
    }

    [Fact]
    public async Task HandlePause_ValidToken_TransitionsToPausedAndPersistsToConfigDb()
    {
        Fixture fx = await CreateAsync();
        byte[] token = await GetValidActionTokenAsync(fx.Auth);
        fx.Clock.Now = 1_700_000_000_000L;

        IpcPayload response = await fx.Pause.HandleAsync(
            new IpcPayload { MessageId = 11, PauseMonitoringReq = new PauseMonitoringRequest { ActionToken = ByteString.CopyFrom(token), Duration = PauseDuration.FifteenMinutes } },
            CancellationToken.None);

        Assert.Equal(PauseResult.Success, response.PauseMonitoringResp.Result);
        Assert.Equal(fx.Clock.Now + 15 * 60_000L, response.PauseMonitoringResp.PauseExpiresAtUnixMs);
        Assert.True(fx.Pause.IsPaused);

        using ConfigDb db = ConfigDb.Open(_configDbPath);
        ConfigSnapshot snapshot = db.ReadSnapshot();
        Assert.True(snapshot.PauseState.IsPaused);
        Assert.Equal(fx.Clock.Now + 15 * 60_000L, snapshot.PauseState.PauseExpiresAtUnixMs);

        string auditContent = await File.ReadAllTextAsync(_auditLogPath);
        Assert.Contains("\"event_type\":\"PauseActivated\"", auditContent);
    }

    [Fact]
    public async Task HandlePause_ActionTokenIsSingleUse_SecondPauseRequestFails()
    {
        Fixture fx = await CreateAsync();
        byte[] token = await GetValidActionTokenAsync(fx.Auth);

        await fx.Pause.HandleAsync(
            new IpcPayload { MessageId = 12, PauseMonitoringReq = new PauseMonitoringRequest { ActionToken = ByteString.CopyFrom(token), Duration = PauseDuration.FifteenMinutes } },
            CancellationToken.None);
        IpcPayload second = await fx.Pause.HandleAsync(
            new IpcPayload { MessageId = 13, PauseMonitoringReq = new PauseMonitoringRequest { ActionToken = ByteString.CopyFrom(token), Duration = PauseDuration.FifteenMinutes } },
            CancellationToken.None);

        Assert.Equal(PauseResult.InvalidToken, second.PauseMonitoringResp.Result);
    }

    [Fact]
    public async Task HandlePause_AlreadyPaused_ReturnsAlreadyPausedIdempotentGuard()
    {
        Fixture fx = await CreateAsync();
        byte[] firstToken = await GetValidActionTokenAsync(fx.Auth);
        await fx.Pause.HandleAsync(
            new IpcPayload { MessageId = 14, PauseMonitoringReq = new PauseMonitoringRequest { ActionToken = ByteString.CopyFrom(firstToken), Duration = PauseDuration.FifteenMinutes } },
            CancellationToken.None);

        byte[] secondToken = await GetValidActionTokenAsync(fx.Auth); // action_token mới, hợp lệ — nhưng state đã Paused
        IpcPayload second = await fx.Pause.HandleAsync(
            new IpcPayload { MessageId = 15, PauseMonitoringReq = new PauseMonitoringRequest { ActionToken = ByteString.CopyFrom(secondToken), Duration = PauseDuration.OneHour } },
            CancellationToken.None);

        Assert.Equal(PauseResult.AlreadyPaused, second.PauseMonitoringResp.Result);
    }

    [Fact]
    public async Task HandleResume_NotPaused_ReturnsNotPausedIdempotentGuard()
    {
        Fixture fx = await CreateAsync();
        byte[] token = await GetValidActionTokenAsync(fx.Auth);

        IpcPayload response = await fx.Pause.HandleAsync(
            new IpcPayload { MessageId = 20, ResumeMonitoringReq = new ResumeMonitoringRequest { ActionToken = ByteString.CopyFrom(token) } },
            CancellationToken.None);

        Assert.Equal(ResumeResult.NotPaused, response.ResumeMonitoringResp.Result);
    }

    [Fact]
    public async Task HandleResume_InvalidToken_ReturnsInvalidTokenAndStaysPaused()
    {
        Fixture fx = await CreateAsync();
        byte[] pauseToken = await GetValidActionTokenAsync(fx.Auth);
        await fx.Pause.HandleAsync(
            new IpcPayload { MessageId = 21, PauseMonitoringReq = new PauseMonitoringRequest { ActionToken = ByteString.CopyFrom(pauseToken), Duration = PauseDuration.OneHour } },
            CancellationToken.None);

        IpcPayload response = await fx.Pause.HandleAsync(
            new IpcPayload { MessageId = 22, ResumeMonitoringReq = new ResumeMonitoringRequest { ActionToken = ByteString.CopyFrom([9, 9, 9]) } },
            CancellationToken.None);

        Assert.Equal(ResumeResult.InvalidToken, response.ResumeMonitoringResp.Result);
        Assert.True(fx.Pause.IsPaused);
    }

    [Fact]
    public async Task HandleResume_ValidToken_ResumesWithManualTriggerAndClearsExpiry()
    {
        Fixture fx = await CreateAsync();
        byte[] pauseToken = await GetValidActionTokenAsync(fx.Auth);
        await fx.Pause.HandleAsync(
            new IpcPayload { MessageId = 23, PauseMonitoringReq = new PauseMonitoringRequest { ActionToken = ByteString.CopyFrom(pauseToken), Duration = PauseDuration.OneHour } },
            CancellationToken.None);

        byte[] resumeToken = await GetValidActionTokenAsync(fx.Auth);
        IpcPayload response = await fx.Pause.HandleAsync(
            new IpcPayload { MessageId = 24, ResumeMonitoringReq = new ResumeMonitoringRequest { ActionToken = ByteString.CopyFrom(resumeToken) } },
            CancellationToken.None);

        Assert.Equal(ResumeResult.Success, response.ResumeMonitoringResp.Result);
        Assert.False(fx.Pause.IsPaused);

        string auditContent = await File.ReadAllTextAsync(_auditLogPath);
        Assert.Contains("\"trigger\":\"manual\"", auditContent);
    }

    [Fact]
    public async Task HandleStatusQuery_ReflectsCurrentPauseState()
    {
        Fixture fx = await CreateAsync();
        byte[] token = await GetValidActionTokenAsync(fx.Auth);
        await fx.Pause.HandleAsync(
            new IpcPayload { MessageId = 30, PauseMonitoringReq = new PauseMonitoringRequest { ActionToken = ByteString.CopyFrom(token), Duration = PauseDuration.ThirtyMinutes } },
            CancellationToken.None);

        IpcPayload response = await fx.Pause.HandleAsync(new IpcPayload { MessageId = 31, PauseStatusQuery = new PauseStatusQuery() }, CancellationToken.None);

        Assert.True(response.PauseStatusResp.IsPaused);
        Assert.True(response.PauseStatusResp.PauseExpiresAtUnixMs > 0);
    }

    [Fact]
    public async Task Tick_WhenExpired_AutoResumesWithAutoExpiredTrigger()
    {
        Fixture fx = await CreateAsync();
        byte[] token = await GetValidActionTokenAsync(fx.Auth);
        await fx.Pause.HandleAsync(
            new IpcPayload { MessageId = 40, PauseMonitoringReq = new PauseMonitoringRequest { ActionToken = ByteString.CopyFrom(token), Duration = PauseDuration.FifteenMinutes } },
            CancellationToken.None);

        fx.Clock.Now += 16 * 60_000L; // vượt quá 15 phút đã chọn
        await fx.Pause.TriggerTickForTestAsync(CancellationToken.None);

        Assert.False(fx.Pause.IsPaused);
        string auditContent = await File.ReadAllTextAsync(_auditLogPath);
        Assert.Contains("\"trigger\":\"auto_expired\"", auditContent);
    }

    [Fact]
    public async Task Tick_WhenNotYetExpired_StaysPaused()
    {
        Fixture fx = await CreateAsync();
        byte[] token = await GetValidActionTokenAsync(fx.Auth);
        await fx.Pause.HandleAsync(
            new IpcPayload { MessageId = 41, PauseMonitoringReq = new PauseMonitoringRequest { ActionToken = ByteString.CopyFrom(token), Duration = PauseDuration.OneHour } },
            CancellationToken.None);

        fx.Clock.Now += 60_000L; // còn xa mới hết hạn (1 giờ)
        await fx.Pause.TriggerTickForTestAsync(CancellationToken.None);

        Assert.True(fx.Pause.IsPaused);
    }

    [Fact]
    public async Task Tick_BannerNotSentImmediately_OnlyAfterTenMinutesFromPauseStarted()
    {
        Fixture fx = await CreateAsync();
        byte[] token = await GetValidActionTokenAsync(fx.Auth);
        await fx.Pause.HandleAsync(
            new IpcPayload { MessageId = 42, PauseMonitoringReq = new PauseMonitoringRequest { ActionToken = ByteString.CopyFrom(token), Duration = PauseDuration.FourHours } },
            CancellationToken.None);
        Assert.Null(fx.Pause.LastBannerShownAtUnixMsForTest); // ADR-103 — không gửi ngay lúc kích hoạt

        fx.Clock.Now += 5 * 60_000L; // mới 5 phút — chưa đủ 10 phút
        await fx.Pause.TriggerTickForTestAsync(CancellationToken.None);
        Assert.Null(fx.Pause.LastBannerShownAtUnixMsForTest);

        fx.Clock.Now += 6 * 60_000L; // tổng 11 phút — đã đủ 10 phút
        await fx.Pause.TriggerTickForTestAsync(CancellationToken.None);
        Assert.NotNull(fx.Pause.LastBannerShownAtUnixMsForTest);
    }

    /// <summary>Restart trong lúc Pause đã hết hạn — cùng logic tick, mô phỏng "boot thẳng vào Running·Paused đã hết hạn" qua initial state + trigger tick (khác `PauseStateRecovery`, vốn xử lý resume-lúc-offline TRƯỚC KHI PauseCoordinator được tạo).</summary>
    [Fact]
    public async Task BootIntoAlreadyExpiredPausedState_TickResumesAutomatically()
    {
        var expiredPaused = new PauseStateData(IsPaused: true, PauseStartedAtUnixMs: 1_700_000_000_000L, PauseExpiresAtUnixMs: 1_700_000_100_000L);
        Fixture fx = await CreateAsync(expiredPaused);
        fx.Clock.Now = 1_700_000_200_000L; // đã qua mốc hết hạn

        await fx.Pause.TriggerTickForTestAsync(CancellationToken.None);

        Assert.False(fx.Pause.IsPaused);
    }

    /// <summary>`ANTI-060` tách biệt (Architecture/02 mục 3a.6) — Pause hợp lệ KHÔNG được sinh sự kiện nào bị `AttackPatternCoordinator` đếm, kể cả lặp lại nhiều lần.</summary>
    [Fact]
    public async Task PauseResumeCycles_NeverTriggerAttackPatternCoordinator()
    {
        Fixture fx = await CreateAsync();

        for (int i = 0; i < 8; i++) // vượt xa ngưỡng ANTI-060 (N=5/30 phút) NẾU bị tính nhầm vào bộ đếm đó
        {
            byte[] pauseToken = await GetValidActionTokenAsync(fx.Auth);
            await fx.Pause.HandleAsync(
                new IpcPayload { MessageId = (ulong)(100 + i * 2), PauseMonitoringReq = new PauseMonitoringRequest { ActionToken = ByteString.CopyFrom(pauseToken), Duration = PauseDuration.FifteenMinutes } },
                CancellationToken.None);

            byte[] resumeToken = await GetValidActionTokenAsync(fx.Auth);
            await fx.Pause.HandleAsync(
                new IpcPayload { MessageId = (ulong)(101 + i * 2), ResumeMonitoringReq = new ResumeMonitoringRequest { ActionToken = ByteString.CopyFrom(resumeToken) } },
                CancellationToken.None);
        }

        string auditContent = await File.ReadAllTextAsync(_auditLogPath);
        Assert.DoesNotContain("AttackPatternDetected", auditContent);
        Assert.DoesNotContain("\"event_type\":\"ProcessRestarted\"", auditContent);
    }

    [Fact]
    public async Task PauseActivatedMoreThanFiveTimesInDay_LogsFrequencyAnomaly()
    {
        Fixture fx = await CreateAsync();
        // AuditLogWriter luôn ghi ts_unix_ms bằng đồng hồ thật (DateTimeOffset.UtcNow), không phải
        // trusted_now — neo FakeMonotonicClock vào đúng "hôm nay" thật để so khớp đúng ngày lịch UTC.
        fx.Clock.Now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        for (int i = 0; i < 6; i++) // > 5 lần/ngày (PAUSE-021, ĐÃ CHỐT v0.2.2)
        {
            byte[] pauseToken = await GetValidActionTokenAsync(fx.Auth);
            await fx.Pause.HandleAsync(
                new IpcPayload { MessageId = (ulong)(200 + i * 2), PauseMonitoringReq = new PauseMonitoringRequest { ActionToken = ByteString.CopyFrom(pauseToken), Duration = PauseDuration.FifteenMinutes } },
                CancellationToken.None);

            byte[] resumeToken = await GetValidActionTokenAsync(fx.Auth);
            await fx.Pause.HandleAsync(
                new IpcPayload { MessageId = (ulong)(201 + i * 2), ResumeMonitoringReq = new ResumeMonitoringRequest { ActionToken = ByteString.CopyFrom(resumeToken) } },
                CancellationToken.None);

            fx.Clock.Now += 1_000L; // vẫn cùng ngày lịch UTC
        }

        string auditContent = await File.ReadAllTextAsync(_auditLogPath);
        Assert.Contains("PauseFrequencyAnomalyDetected", auditContent);

        // Schema ĐÃ CHỐT ở Architecture/04-data-architecture.md v0.2.4 mục 5.1: {date_utc, activation_count}
        // — kiểm field-level để không tái diễn gap tên field đã bị security-privacy-auditor/test-runner phát hiện.
        string[] lines = auditContent.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        string anomalyLine = Assert.Single(lines, l => l.Contains("PauseFrequencyAnomalyDetected"));
        using JsonDocument doc = JsonDocument.Parse(anomalyLine);
        JsonElement detail = doc.RootElement.GetProperty("detail");
        Assert.Equal(6, detail.GetProperty("activation_count").GetInt32());
        DateOnly expectedDate = DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeMilliseconds(fx.Clock.Now).UtcDateTime);
        Assert.Equal(expectedDate.ToString("yyyy-MM-dd"), detail.GetProperty("date_utc").GetString());
    }

    [Fact]
    public async Task PauseActivatedFiveTimesOrFewerInDay_DoesNotLogFrequencyAnomaly()
    {
        Fixture fx = await CreateAsync();
        fx.Clock.Now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        for (int i = 0; i < 5; i++) // đúng ngưỡng, KHÔNG vượt quá (PAUSE-021 yêu cầu "> 5", không phải ">=")
        {
            byte[] pauseToken = await GetValidActionTokenAsync(fx.Auth);
            await fx.Pause.HandleAsync(
                new IpcPayload { MessageId = (ulong)(300 + i * 2), PauseMonitoringReq = new PauseMonitoringRequest { ActionToken = ByteString.CopyFrom(pauseToken), Duration = PauseDuration.FifteenMinutes } },
                CancellationToken.None);

            byte[] resumeToken = await GetValidActionTokenAsync(fx.Auth);
            await fx.Pause.HandleAsync(
                new IpcPayload { MessageId = (ulong)(301 + i * 2), ResumeMonitoringReq = new ResumeMonitoringRequest { ActionToken = ByteString.CopyFrom(resumeToken) } },
                CancellationToken.None);

            fx.Clock.Now += 1_000L;
        }

        string auditContent = await File.ReadAllTextAsync(_auditLogPath);
        Assert.DoesNotContain("PauseFrequencyAnomalyDetected", auditContent);
    }

    /// <summary>
    /// Giữ RESERVED lock trên <c>_configDbPath</c> qua 1 connection SQLite riêng — mô phỏng I/O lỗi
    /// thoáng qua hoàn toàn tự nhiên (AV quét file, WAL checkpoint, backup tool...) đúng cách
    /// security-privacy-auditor đã tái hiện <c>SQLite Error 5: 'database is locked'</c> khi
    /// <see cref="PauseCoordinator.TryPersist"/> gọi <see cref="ConfigDb.UpdatePauseState"/>.
    /// </summary>
    private static SqliteConnection AcquireWriteLock(string dbPath)
    {
        var locker = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = dbPath, Mode = SqliteOpenMode.ReadWriteCreate }.ToString());
        locker.Open();
        using SqliteCommand cmd = locker.CreateCommand();
        cmd.CommandText = "BEGIN IMMEDIATE TRANSACTION;";
        cmd.ExecuteNonQuery();
        return locker;
    }

    private static void ReleaseWriteLock(SqliteConnection locker)
    {
        using (SqliteCommand rollback = locker.CreateCommand())
        {
            rollback.CommandText = "ROLLBACK;";
            rollback.ExecuteNonQuery();
        }

        locker.Dispose();
    }

    /// <summary>
    /// Regression cho FAIL cứng audit 2026-09-20 (`ConfigDb` write-path không bắt <c>SqliteException</c>):
    /// trước fix, exception thoát thẳng qua <see cref="PauseCoordinator.TryPersist"/> (chỉ bắt
    /// <c>ConfigLoadException</c>) → request UI hang thay vì trả response. Sau fix: <c>PauseResult</c>
    /// trả về hợp lý và Pause KHÔNG được áp dụng (fail-secure nghiêng về giám sát BẬT, Architecture/01
    /// mục 5) — đúng hướng NGƯỢC với Resume bên dưới.
    /// </summary>
    [Fact]
    public async Task HandlePause_ConfigDbLockedDuringWrite_ReturnsUnspecifiedAndStaysMonitoring()
    {
        Fixture fx = await CreateAsync();
        byte[] token = await GetValidActionTokenAsync(fx.Auth);

        SqliteConnection locker = AcquireWriteLock(_configDbPath);
        try
        {
            IpcPayload response = await fx.Pause.HandleAsync(
                new IpcPayload { MessageId = 50, PauseMonitoringReq = new PauseMonitoringRequest { ActionToken = ByteString.CopyFrom(token), Duration = PauseDuration.FifteenMinutes } },
                CancellationToken.None);

            Assert.Equal(PauseResult.Unspecified, response.PauseMonitoringResp.Result);
            Assert.False(fx.Pause.IsPaused);
        }
        finally
        {
            ReleaseWriteLock(locker);
        }
    }

    /// <summary>
    /// Regression cho FAIL cứng audit 2026-09-20 — chiều Resume, bài test quan trọng nhất: trước fix,
    /// <c>SqliteException</c> thoát khỏi <see cref="PauseCoordinator.TryPersist"/> làm dòng
    /// <c>_state = resumedState;</c> trong <see cref="PauseCoordinator"/> (sau đoạn fail-secure) KHÔNG
    /// BAO GIỜ chạy — giám sát ÂM THẦM vẫn Paused dù request "thành công". Sau fix: RAM state VẪN
    /// chuyển sang Monitoring (<c>IsPaused == false</c>) dù ghi <c>config.db</c> lỗi.
    /// </summary>
    [Fact]
    public async Task HandleResume_ConfigDbLockedDuringWrite_StillAppliesRamStateResumeFailSecure()
    {
        Fixture fx = await CreateAsync();
        byte[] pauseToken = await GetValidActionTokenAsync(fx.Auth);
        await fx.Pause.HandleAsync(
            new IpcPayload { MessageId = 51, PauseMonitoringReq = new PauseMonitoringRequest { ActionToken = ByteString.CopyFrom(pauseToken), Duration = PauseDuration.OneHour } },
            CancellationToken.None);
        Assert.True(fx.Pause.IsPaused);

        byte[] resumeToken = await GetValidActionTokenAsync(fx.Auth);
        SqliteConnection locker = AcquireWriteLock(_configDbPath);
        try
        {
            IpcPayload response = await fx.Pause.HandleAsync(
                new IpcPayload { MessageId = 52, ResumeMonitoringReq = new ResumeMonitoringRequest { ActionToken = ByteString.CopyFrom(resumeToken) } },
                CancellationToken.None);

            Assert.Equal(ResumeResult.Success, response.ResumeMonitoringResp.Result);
            Assert.False(fx.Pause.IsPaused); // Fail-secure: RAM state vẫn resume dù ghi đĩa lỗi (Architecture/01 mục 5).
        }
        finally
        {
            ReleaseWriteLock(locker);
        }
    }

    /// <summary>ADR-101 — action_token phát hành cho action_context khác (vd. "uninstall") KHÔNG được dùng cho Pause (permanent hoá kịch bản security-privacy-auditor đã xác nhận adhoc).</summary>
    [Fact]
    public async Task HandlePause_ActionTokenIssuedForDifferentActionContext_ReturnsInvalidToken()
    {
        Fixture fx = await CreateAsync();
        byte[] mismatchedToken = await GetValidActionTokenAsync(fx.Auth, actionContext: "uninstall");

        IpcPayload response = await fx.Pause.HandleAsync(
            new IpcPayload { MessageId = 60, PauseMonitoringReq = new PauseMonitoringRequest { ActionToken = ByteString.CopyFrom(mismatchedToken), Duration = PauseDuration.FifteenMinutes } },
            CancellationToken.None);

        Assert.Equal(PauseResult.InvalidToken, response.PauseMonitoringResp.Result);
        Assert.False(fx.Pause.IsPaused);
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
