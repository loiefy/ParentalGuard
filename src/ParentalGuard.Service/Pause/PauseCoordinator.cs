using Google.Protobuf;
using Microsoft.Extensions.Logging;
using ParentalGuard.Ipc.Framing;
using ParentalGuard.Ipc.Protocol;
using ParentalGuard.Ipc.Security;
using ParentalGuard.Service.Audit;
using ParentalGuard.Service.Auth;
using ParentalGuard.Service.Data;
using ParentalGuard.Service.Ipc;

namespace ParentalGuard.Service.Pause;

/// <summary>
/// Orchestrator Pause/Resume (<c>PAUSE-001</c>-<c>004</c>/<c>020</c>/<c>021</c>/<c>030</c>/<c>031</c>,
/// Architecture/02-process-architecture.md mục 3a) — nhận <c>PauseMonitoringRequest</c>/
/// <c>ResumeMonitoringRequest</c>/<c>PauseStatusQuery</c> từ pipe <c>UI</c>, xác thực
/// <c>action_token</c> qua <see cref="AuthCoordinator"/> (action_context="pause_monitoring",
/// ADR-101), ghi <c>pause_state</c> vào <c>config.db</c>, điều khiển tần suất hiệu dụng của
/// <c>Vision</c> qua <c>ControlVisionCommand.MonitoringEnabled</c> (KHÔNG kill process — ADR-40,
/// mục 3a.5) + cadence heartbeat (ADR-104), giải phóng overlay đang che (ADR-106), chạy
/// <c>PauseMonitor</c> tick 30 giây cho auto-resume + banner nhắc (ADR-102/103).
/// </summary>
public sealed class PauseCoordinator
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan BannerInterval = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan NormalVisionHeartbeatInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan PausedVisionHeartbeatInterval = TimeSpan.FromSeconds(10);
    private const string ActionContext = "pause_monitoring";

    private readonly string _configDbPath;
    private readonly string _auditLogPath;
    private readonly AuthCoordinator _authCoordinator;
    private readonly AuditLogWriter _auditLog;
    private readonly MonotonicClock _clock;
    private readonly ChildProcessSupervisor _visionSupervisor;
    private readonly ChildProcessSupervisor _overlaySupervisor;
    private readonly OverlayDecisionCoordinator _overlayDecisionCoordinator;
    private readonly IconStatusCoordinator _iconStatusCoordinator;
    private readonly Func<bool, ControlVisionCommand> _buildControlVisionCommand;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly IpcMessageIdGenerator _messageIds = new();

    private volatile PauseStateData _state;
    private long? _lastBannerShownAtUnixMs;
    private CancellationToken _serviceStoppingToken;
    private CancellationTokenSource? _tickCts;

    public PauseCoordinator(
        string configDbPath,
        string auditLogPath,
        AuthCoordinator authCoordinator,
        AuditLogWriter auditLog,
        MonotonicClock clock,
        ChildProcessSupervisor visionSupervisor,
        ChildProcessSupervisor overlaySupervisor,
        OverlayDecisionCoordinator overlayDecisionCoordinator,
        IconStatusCoordinator iconStatusCoordinator,
        Func<bool, ControlVisionCommand> buildControlVisionCommand,
        PauseStateData initialState,
        ILogger logger)
    {
        _configDbPath = configDbPath;
        _auditLogPath = auditLogPath;
        _authCoordinator = authCoordinator;
        _auditLog = auditLog;
        _clock = clock;
        _visionSupervisor = visionSupervisor;
        _overlaySupervisor = overlaySupervisor;
        _overlayDecisionCoordinator = overlayDecisionCoordinator;
        _iconStatusCoordinator = iconStatusCoordinator;
        _buildControlVisionCommand = buildControlVisionCommand;
        _logger = logger;
        _state = initialState;
    }

    public bool IsPaused => _state.IsPaused;

    public long PauseExpiresAtUnixMs => _state.PauseExpiresAtUnixMs ?? 0;

    /// <summary>Gọi đúng 1 lần lúc <c>Worker.ExecuteAsync</c> — khởi động lại <c>PauseMonitor</c> nếu Starting vào thẳng <c>Running·Paused</c> (mục 3a.3).</summary>
    public void Start(CancellationToken serviceStoppingToken)
    {
        _serviceStoppingToken = serviceStoppingToken;
        if (_state.IsPaused)
        {
            StartTick();
        }
    }

    /// <summary>Định tuyến theo <see cref="IpcPayload.BodyOneofCase"/> — pipe UI gọi đúng 1 hàm này cho domain Pause/Resume (field 92-97).</summary>
    public Task<IpcPayload> HandleAsync(IpcPayload request, CancellationToken cancellationToken) => request.BodyCase switch
    {
        IpcPayload.BodyOneofCase.PauseMonitoringReq => HandlePauseAsync(request, cancellationToken),
        IpcPayload.BodyOneofCase.ResumeMonitoringReq => HandleResumeAsync(request, cancellationToken),
        IpcPayload.BodyOneofCase.PauseStatusQuery => HandleStatusQueryAsync(request),
        _ => throw new InvalidOperationException($"PauseCoordinator received unexpected message: {request.BodyCase}."),
    };

    /// <summary>Mục 3a.1 — kích hoạt Pause qua <c>action_token</c> (cùng cổng chung <c>AuthCoordinator</c> đã dùng cho uninstall, ADR-101).</summary>
    private async Task<IpcPayload> HandlePauseAsync(IpcPayload request, CancellationToken cancellationToken)
    {
        PauseMonitoringRequest req = request.PauseMonitoringReq;
        IpcPayload response = NewResponse(request);

        if (!await ConsumeTokenAsync(req.ActionToken, cancellationToken).ConfigureAwait(false))
        {
            response.PauseMonitoringResp = new PauseMonitoringResponse { Result = PauseResult.InvalidToken };
            return response;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_state.IsPaused)
            {
                // Idempotent guard (mục 3a.1) — hiếm, 2 phiên UI thao tác gần như đồng thời.
                response.PauseMonitoringResp = new PauseMonitoringResponse { Result = PauseResult.AlreadyPaused };
                return response;
            }

            long trustedNow = _clock.UtcNowUnixMs;
            long expiresAt = PauseDurationCalculator.ComputeExpiresAtUnixMs(req.Duration, trustedNow);
            var newState = new PauseStateData(IsPaused: true, PauseStartedAtUnixMs: trustedNow, PauseExpiresAtUnixMs: expiresAt);

            if (!TryPersist(newState))
            {
                // Fail-secure (Architecture/01 mục 5): không ghi được pause_state → KHÔNG áp dụng
                // Pause, giữ nguyên Running·Monitoring. `PauseResult` chỉ có 3 giá trị đã approve
                // (03-ipc-communication.md mục 3.6) — dùng Unspecified (giá trị mặc định proto3)
                // cho lỗi nội bộ hiếm gặp này thay vì phát minh thêm giá trị enum ngoài phạm vi đã duyệt.
                response.PauseMonitoringResp = new PauseMonitoringResponse { Result = PauseResult.Unspecified };
                return response;
            }

            _state = newState;
            _lastBannerShownAtUnixMs = null; // ADR-103 — chờ đủ 10 phút kể từ pause_started_at mới gửi banner đầu tiên.

            _visionSupervisor.TryEnqueueBusinessMessage(payload => payload.ControlVision = _buildControlVisionCommand(false));
            _overlayDecisionCoordinator.ClearForPause(); // ADR-106
            _visionSupervisor.SetHeartbeatInterval(PausedVisionHeartbeatInterval); // ADR-104
            _iconStatusCoordinator.SetState(IconState.Paused, expiresAt);

            string durationLiteral = PauseDurationMapper.ToAuditLogValue(req.Duration);
            await _auditLog.AppendAsync("PauseActivated", new { duration = durationLiteral, pause_expires_at_unix_ms = expiresAt }, CancellationToken.None).ConfigureAwait(false);
            await CheckDailyFrequencyAnomalyAsync(trustedNow, cancellationToken).ConfigureAwait(false); // PAUSE-021

            StartTick();

            response.PauseMonitoringResp = new PauseMonitoringResponse { Result = PauseResult.Success, PauseExpiresAtUnixMs = expiresAt };
            return response;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Mục 3a.2 — resume sớm chủ động, CÙNG <c>action_context</c> với Pause (ADR-101).</summary>
    private async Task<IpcPayload> HandleResumeAsync(IpcPayload request, CancellationToken cancellationToken)
    {
        ResumeMonitoringRequest req = request.ResumeMonitoringReq;
        IpcPayload response = NewResponse(request);

        if (!await ConsumeTokenAsync(req.ActionToken, cancellationToken).ConfigureAwait(false))
        {
            response.ResumeMonitoringResp = new ResumeMonitoringResponse { Result = ResumeResult.InvalidToken };
            return response;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_state.IsPaused)
            {
                response.ResumeMonitoringResp = new ResumeMonitoringResponse { Result = ResumeResult.NotPaused };
                return response;
            }

            long originalExpiresAt = _state.PauseExpiresAtUnixMs ?? 0;
            await ApplyResumeAsync("manual", originalExpiresAt).ConfigureAwait(false);

            response.ResumeMonitoringResp = new ResumeMonitoringResponse { Result = ResumeResult.Success };
            return response;
        }
        finally
        {
            _gate.Release();
        }
    }

    private Task<IpcPayload> HandleStatusQueryAsync(IpcPayload request)
    {
        IpcPayload response = NewResponse(request);
        PauseStateData snapshot = _state;
        response.PauseStatusResp = new PauseStatusResponse { IsPaused = snapshot.IsPaused, PauseExpiresAtUnixMs = snapshot.PauseExpiresAtUnixMs ?? 0 };
        return Task.FromResult(response);
    }

    /// <summary>Test hook (giống mẫu hình <c>PendingSetupForTest</c> ở <c>AuthCoordinator</c>) — chạy tick <c>PauseMonitor</c> ngay lập tức, không chờ 30 giây thật.</summary>
    internal Task TriggerTickForTestAsync(CancellationToken cancellationToken) => OnTickAsync(cancellationToken);

    /// <summary>Test hook — kiểm tra ADR-103 (banner đầu tiên chờ đủ 10 phút kể từ <c>pause_started_at</c>, không gửi ngay lúc kích hoạt).</summary>
    internal long? LastBannerShownAtUnixMsForTest => _lastBannerShownAtUnixMs;

    private void StartTick()
    {
        _tickCts?.Cancel();
        _tickCts = CancellationTokenSource.CreateLinkedTokenSource(_serviceStoppingToken);
        _ = TickLoopAsync(_tickCts.Token);
    }

    private void StopTick()
    {
        _tickCts?.Cancel();
        _tickCts = null;
    }

    /// <summary>ADR-102 — tick chu kỳ cố định 30 giây, KHÔNG dùng <c>Timer</c> bắn đúng 1 lần tại thời điểm hết hạn (tránh trôi lịch khi máy sleep/hibernate).</summary>
    private async Task TickLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TickInterval, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            try
            {
                await OnTickAsync(token).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or ConfigLoadException)
            {
                _logger.LogWarning(ex, "PauseMonitor tick failed — will retry next cycle.");
            }
        }
    }

    private async Task OnTickAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_state.IsPaused)
            {
                StopTick(); // Đã resume qua đường khác giữa chừng (manual) — an toàn dừng tick.
                return;
            }

            long trustedNow = _clock.UtcNowUnixMs;
            if (_state.PauseExpiresAtUnixMs is long expiresAt && trustedNow >= expiresAt)
            {
                await ApplyResumeAsync("auto_expired", expiresAt).ConfigureAwait(false);
                return;
            }

            long bannerBaseline = _lastBannerShownAtUnixMs ?? _state.PauseStartedAtUnixMs ?? trustedNow;
            if (trustedNow - bannerBaseline >= BannerInterval.TotalMilliseconds)
            {
                SendBannerReminder(trustedNow);
                _lastBannerShownAtUnixMs = trustedNow;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Chuyển <c>Running·Paused</c> → <c>Running·Monitoring</c> — dùng chung cho resume sớm thủ công lẫn tự động hết hạn. PHẢI gọi trong <see cref="_gate"/>.</summary>
    private async Task ApplyResumeAsync(string trigger, long originalExpiresAtUnixMs)
    {
        long trustedNow = _clock.UtcNowUnixMs;
        var resumedState = PauseStateData.CreateDefault();

        // Fail-secure theo chiều NGƯỢC với HandlePauseAsync (Architecture/01 mục 5): Resume nghiêng
        // về phía giám sát BẬT — vẫn áp dụng chuyển trạng thái RAM dù ghi config.db lỗi (hiếm gặp),
        // chỉ log cảnh báo, không chặn việc bật lại giám sát vì lý do lưu trữ.
        if (!TryPersist(resumedState))
        {
            _logger.LogError("Could not persist pause_state resume (trigger={Trigger}) — applying RAM-only resume (fail-secure: monitoring stays ON).", trigger);
        }

        _state = resumedState;
        _lastBannerShownAtUnixMs = null;

        _visionSupervisor.TryEnqueueBusinessMessage(payload => payload.ControlVision = _buildControlVisionCommand(true));
        _visionSupervisor.SetHeartbeatInterval(NormalVisionHeartbeatInterval); // ADR-104
        _iconStatusCoordinator.SetState(IconState.Active);

        await _auditLog.AppendAsync(
            "PauseResumed",
            new { trigger, pause_expires_at_unix_ms = originalExpiresAtUnixMs, actual_resumed_at_unix_ms = trustedNow },
            CancellationToken.None).ConfigureAwait(false);

        StopTick();
    }

    /// <summary>Mục 3a.3/ADR-108 — tái dùng nguyên <c>ShowToastCommand</c> đã có từ Đợt 0, không tạo message/state UI mới.</summary>
    private void SendBannerReminder(long trustedNow)
    {
        long remainingMs = Math.Max(0, (_state.PauseExpiresAtUnixMs ?? trustedNow) - trustedNow);
        string text = $"Giám sát đang tạm dừng — còn lại {FormatRemaining(TimeSpan.FromMilliseconds(remainingMs))}";

        _overlaySupervisor.TryEnqueueBusinessMessage(payload => payload.ShowToast = new ShowToastCommand
        {
            ToastId = unchecked((uint)_messageIds.Next()),
            Text = text,
            Severity = ToastSeverity.Info,
            ReasonCode = "PAUSE_REMINDER",
            GeneratedAtUnixMs = trustedNow,
        });
    }

    private static string FormatRemaining(TimeSpan remaining) =>
        remaining.TotalHours >= 1 ? $"{(int)remaining.TotalHours} giờ {remaining.Minutes} phút" : $"{Math.Max(1, remaining.Minutes)} phút";

    private async Task<bool> ConsumeTokenAsync(ByteString token, CancellationToken cancellationToken)
    {
        byte[] tokenBytes = CredentialBytes.UnsafeGetBuffer(token);
        try
        {
            return await _authCoordinator.TryConsumeActionTokenAsync(tokenBytes, ActionContext, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            CredentialBytes.Zero(tokenBytes);
        }
    }

    private bool TryPersist(PauseStateData state)
    {
        try
        {
            using ConfigDb db = ConfigDb.Open(_configDbPath);
            db.UpdatePauseState(state);
            return true;
        }
        catch (ConfigLoadException ex)
        {
            _logger.LogError(ex, "Failed to persist pause_state to config.db.");
            return false;
        }
    }

    /// <summary>PAUSE-021 — cảnh báo nhẹ (chỉ ghi audit log, chưa có UI Dashboard ở Đợt 5) khi vượt ngưỡng &gt; 5 lần/ngày.</summary>
    private async Task CheckDailyFrequencyAnomalyAsync(long nowUnixMs, CancellationToken cancellationToken)
    {
        int countToday;
        DateOnly dateUtc;
        try
        {
            (countToday, dateUtc) = await PauseFrequencyGuard.CountPauseActivatedTodayAsync(_auditLogPath, nowUnixMs, cancellationToken).ConfigureAwait(false);
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "PAUSE-021: could not scan audit.log for daily pause frequency — skipping this check.");
            return;
        }

        if (countToday > PauseFrequencyGuard.DailyThreshold)
        {
            // Schema ĐÃ CHỐT ở Architecture/04-data-architecture.md v0.2.4 mục 5.1: {date_utc, activation_count}.
            await _auditLog.AppendAsync(
                "PauseFrequencyAnomalyDetected",
                new { date_utc = dateUtc.ToString("yyyy-MM-dd"), activation_count = countToday },
                CancellationToken.None).ConfigureAwait(false);
        }
    }

    private IpcPayload NewResponse(IpcPayload request) =>
        IpcEnvelope.NewEnvelope(ProcessType.Service, _messageIds.Next(), correlationId: request.MessageId);
}
