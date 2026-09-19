using ParentalGuard.Ipc.Protocol;
using ParentalGuard.Service.Audit;
using ParentalGuard.Service.Auth;
using ParentalGuard.Service.Configuration;
using ParentalGuard.Service.Data;
using ParentalGuard.Service.Ipc;
using ParentalGuard.Service.Security;
using ParentalGuard.Service.Session;

namespace ParentalGuard.Service;

/// <summary>
/// Orchestrator Đợt 0 (ROADMAP.md Đợt 0): ACL, config.db fail-secure, WFP block Vision,
/// 2 Named Pipe server (Vision/Overlay) + supervision, theo dõi đổi session tương tác
/// (Architecture/02-process-architecture.md mục 6).
/// </summary>
public sealed class Worker(ILoggerFactory loggerFactory, ILogger<Worker> logger) : BackgroundService
{
    private static readonly TimeSpan _visionHeartbeatInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan _overlayHeartbeatInterval = TimeSpan.FromSeconds(2);

    private SessionWatcher? _sessionWatcher;
    private VisionNetworkWatcher? _visionNetworkWatcher;
    private ChildProcessSupervisor? _visionSupervisor;
    private ChildProcessSupervisor? _overlaySupervisor;
    private OverlayDecisionCoordinator? _overlayDecisionCoordinator;
    private IconStatusCoordinator? _iconStatusCoordinator;
    private IconPositionCoordinator? _iconPositionCoordinator;
    private AuditLogWriter? _auditLog;
    private UiSessionServer? _uiSessionServer;
    private volatile uint _currentSessionId = SessionInterop.InvalidSessionId;

    // Architecture/05-image-pipeline-architecture.md mục 8.1 bước 5 (ADR-49): chỉ nhớ trong bộ
    // nhớ cho hết phiên chạy hiện tại của Service — không ghi config.db. Reset về false (thử lại
    // Low IL) mỗi khi Service khởi động lại.
    private volatile bool _visionRequiresMediumIl;
    private int _visionMediumIlFallbackLogged;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        ApplyAclBestEffort();

        _auditLog = await AuditLogWriter.InitializeAsync(InstallPaths.AuditLogPath, stoppingToken).ConfigureAwait(false);

        var configLoader = new FailSecureConfigLoader(_auditLog, loggerFactory.CreateLogger<FailSecureConfigLoader>());
        ConfigLoadResult config = await configLoader.LoadAsync(stoppingToken).ConfigureAwait(false);
        if (config.UsedFailSecureFallback)
        {
            logger.LogWarning("Started in fail-secure fallback (BE-061): {Reason}", config.FallbackReason);
        }

        ApplyWfpBestEffort();

        StartUiSessionServer(stoppingToken);

        _overlaySupervisor = new ChildProcessSupervisor(
            ProcessType.Overlay,
            "ParentalGuard.Svc.Overlay",
            InstallPaths.OverlayExecutablePath,
            _overlayHeartbeatInterval,
            // Architecture/03 mục 4.3: thứ tự push ngay sau handshake — rect hiện hành, trạng thái
            // icon (FE-021), rồi layout icon đã lưu (FE-020a).
            [
                payload => _overlayDecisionCoordinator!.ConfigureInitialPush(payload),
                payload => _iconStatusCoordinator!.ConfigureInitialPush(payload),
                payload => _iconPositionCoordinator!.ConfigureInitialPush(payload),
            ],
            config.IpcHmacKey,
            _auditLog,
            loggerFactory.CreateLogger("ParentalGuard.Service.Ipc.ChildProcessSupervisor.Overlay"),
            BuildOverlayOneTimeMessages(config),
            onBusinessMessage: (message, ct) => DispatchOverlayBusinessMessageAsync(message, ct),
            onSessionConnected: () => _iconStatusCoordinator!.SetState(IconState.Active),
            onSessionEnded: () => _iconStatusCoordinator!.SetState(IconState.Error));

        // BE-090: ngưỡng risk score đọc lại tại thời điểm mỗi VisionInferenceResult tới (không
        // chụp giá trị 1 lần) — Đợt 1 chưa có đường nào đổi RiskThreshold lúc runtime (Pause/UI
        // là Đợt 3/5), nhưng giữ đúng nguyên tắc "Service so ngưỡng độc lập mỗi lần" (Architecture/05
        // mục 3.3) thay vì đóng băng closure theo giá trị lúc khởi động.
        _overlayDecisionCoordinator = new OverlayDecisionCoordinator(_overlaySupervisor, _auditLog, () => config.MonitoringState.RiskThreshold);
        _iconStatusCoordinator = new IconStatusCoordinator(_overlaySupervisor);
        _iconPositionCoordinator = new IconPositionCoordinator(loggerFactory.CreateLogger("ParentalGuard.Service.Ipc.IconPositionCoordinator"));

        _visionSupervisor = new ChildProcessSupervisor(
            ProcessType.Vision,
            "ParentalGuard.Svc.Vision",
            InstallPaths.VisionExecutablePath,
            _visionHeartbeatInterval,
            [payload => payload.ControlVision = BuildControlVisionCommand(config.MonitoringState)],
            config.IpcHmacKey,
            _auditLog,
            loggerFactory.CreateLogger("ParentalGuard.Service.Ipc.ChildProcessSupervisor.Vision"),
            onBusinessMessage: (message, ct) => _overlayDecisionCoordinator!.HandleVisionResultAsync(message, ct),
            resolveLowIntegrityLevel: () => !_visionRequiresMediumIl,
            onCaptureInitAccessDeniedExitCode: OnVisionCaptureInitAccessDenied,
            onSessionConnected: () => _iconStatusCoordinator!.SetState(IconState.Active),
            onSessionEnded: () => _iconStatusCoordinator!.SetState(IconState.Error));

        StartVisionNetworkWatcherBestEffort();

        _sessionWatcher = new SessionWatcher();
        _sessionWatcher.SessionChanged += (_, _) => _ = HandleSessionChangeAsync(stoppingToken);
        _sessionWatcher.Start();

        uint initialSession = await WaitForActiveSessionAsync(stoppingToken).ConfigureAwait(false);
        await StartChildrenForSessionAsync(initialSession, stoppingToken).ConfigureAwait(false);

        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Bình thường lúc SCM stop/shutdown.
        }

        await StopAllAsync().ConfigureAwait(false);
    }

    private void ApplyAclBestEffort()
    {
        try
        {
            AclProvisioner.EnsureProgramDataAcl(InstallPaths.ProgramDataDir);
            AclProvisioner.EnsureProgramFilesAcl(InstallPaths.ProgramFilesDir);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            // Đợt 0 chưa có installer — chạy dev/test không phải SYSTEM sẽ không set được ACL.
            // Triển khai thật (LocalSystem) luôn có đủ quyền; không chặn Starting vì lý do này.
            logger.LogWarning(ex, "Could not apply install-path ACL (needs SYSTEM/admin) — continuing.");
        }
    }

    private void ApplyWfpBestEffort()
    {
        try
        {
            new WfpVisionBlocker(loggerFactory.CreateLogger<WfpVisionBlocker>()).Apply(InstallPaths.VisionExecutablePath);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // Cần binary Vision đã tồn tại đúng %ProgramFiles%\ParentalGuard\ + quyền quản trị WFP
            // engine — không có ở Đợt 0 sandbox dev. Không chặn Starting vì lý do này.
            logger.LogWarning(ex, "Could not apply WFP network block for Vision — continuing.");
        }
    }

    /// <summary>Đợt 3 (`PWD-0xx`, Architecture/08 mục 7) — pipe <c>ParentalGuard.Svc.UI</c>, độc lập vòng đời Vision/Overlay (UI tự mở, không do Service spawn).</summary>
    private void StartUiSessionServer(CancellationToken stoppingToken)
    {
        var clock = new MonotonicClock();
        var authCoordinator = new AuthCoordinator(InstallPaths.AuthDatPath, _auditLog!, clock, loggerFactory.CreateLogger<AuthCoordinator>());
        _uiSessionServer = new UiSessionServer(
            "ParentalGuard.Svc.UI",
            InstallPaths.UiExecutablePath,
            authCoordinator,
            _auditLog!,
            loggerFactory.CreateLogger<UiSessionServer>());
        _uiSessionServer.Start(stoppingToken);
    }

    private void StartVisionNetworkWatcherBestEffort()
    {
        try
        {
            _visionNetworkWatcher = new VisionNetworkWatcher(InstallPaths.VisionExecutablePath);
            _visionNetworkWatcher.NetworkBlocked += OnVisionNetworkBlocked;
            _visionNetworkWatcher.Start();
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Could not start VisionNetworkWatcher (needs Security Event Log access) — continuing.");
        }
    }

    private void OnVisionNetworkBlocked(object? sender, VisionNetworkBlockedEventArgs e)
    {
        _ = HandleVisionNetworkBlockedAsync(e);
    }

    private async Task HandleVisionNetworkBlockedAsync(VisionNetworkBlockedEventArgs e)
    {
        try
        {
            await _auditLog!.AppendAsync("VisionNetworkBlocked", new { detail = e.DetailXml }, CancellationToken.None).ConfigureAwait(false);
            _visionSupervisor!.RequestChildRestart(); // ADR-34: xử lý như crash, tái dùng luồng khôi phục.
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to handle VisionNetworkBlocked event.");
        }
    }

    /// <summary>
    /// Architecture/05 mục 8.1 bước 4-6: lần đầu tiên trong phiên chạy hiện tại phát hiện
    /// <c>Vision</c> thoát exit code 17, chuyển cờ sang Medium IL cho các lần respawn kế tiếp
    /// (đọc bởi <c>resolveLowIntegrityLevel</c>) và ghi audit log đúng 1 lần.
    /// </summary>
    private void OnVisionCaptureInitAccessDenied()
    {
        _visionRequiresMediumIl = true;
        if (Interlocked.Exchange(ref _visionMediumIlFallbackLogged, 1) == 1)
        {
            return;
        }

        _ = _auditLog!.AppendAsync("VisionCaptureFallbackMediumIl", new { }, CancellationToken.None)
            .ContinueWith(t => logger.LogError(t.Exception, "Failed to log VisionCaptureFallbackMediumIl."), TaskContinuationOptions.OnlyOnFaulted);
    }

    private async Task HandleSessionChangeAsync(CancellationToken stoppingToken)
    {
        try
        {
            uint activeSession = SessionInterop.WTSGetActiveConsoleSessionId();
            if (activeSession == SessionInterop.InvalidSessionId || activeSession == _currentSessionId)
            {
                return;
            }

            logger.LogInformation("Active console session changed to {SessionId} — respawning Vision/Overlay.", activeSession);
            await StartChildrenForSessionAsync(activeSession, stoppingToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to handle session change.");
        }
    }

    private async Task StartChildrenForSessionAsync(uint sessionId, CancellationToken stoppingToken)
    {
        _currentSessionId = sessionId;
        await _visionSupervisor!.StartForSessionAsync(sessionId, stoppingToken).ConfigureAwait(false);
        await _overlaySupervisor!.StartForSessionAsync(sessionId, stoppingToken).ConfigureAwait(false);
    }

    private async Task StopAllAsync()
    {
        if (_uiSessionServer is not null)
        {
            await _uiSessionServer.StopAsync().ConfigureAwait(false);
        }

        if (_visionSupervisor is not null)
        {
            await _visionSupervisor.StopAsync().ConfigureAwait(false);
        }

        if (_overlaySupervisor is not null)
        {
            await _overlaySupervisor.StopAsync().ConfigureAwait(false);
        }

        _visionNetworkWatcher?.Dispose();
        _sessionWatcher?.Dispose();
    }

    private static async Task<uint> WaitForActiveSessionAsync(CancellationToken token)
    {
        while (true)
        {
            uint sessionId = SessionInterop.WTSGetActiveConsoleSessionId();
            if (sessionId != SessionInterop.InvalidSessionId)
            {
                return sessionId;
            }

            await Task.Delay(TimeSpan.FromSeconds(1), token).ConfigureAwait(false);
        }
    }

    /// <summary>Kênh Overlay giờ nhận 2 loại message nghiệp vụ (`BE-032` force-close, `FE-020a` kéo-thả icon) — định tuyến theo <c>BodyCase</c>.</summary>
    private Task DispatchOverlayBusinessMessageAsync(IpcPayload message, CancellationToken cancellationToken) => message.BodyCase switch
    {
        IpcPayload.BodyOneofCase.ForceClose => _overlayDecisionCoordinator!.HandleForceCloseAsync(message, cancellationToken),
        IpcPayload.BodyOneofCase.IconPositionUpdate => _iconPositionCoordinator!.HandleIconPositionUpdateAsync(message, cancellationToken),
        _ => Task.CompletedTask,
    };

    private static ControlVisionCommand BuildControlVisionCommand(MonitoringStateData state)
    {
        var command = new ControlVisionCommand
        {
            MonitoringEnabled = state.MonitoringEnabled,
            CaptureIntervalMs = state.CaptureIntervalBaselineMs,
            RiskThreshold = state.RiskThreshold,
        };
        command.ExcludeProcessNames.AddRange(state.ExcludeProcessNames);
        return command;
    }

    // Bước 7 luồng fail-secure (BE-061b/ANTI-070b, Architecture/04 mục 6.2, Architecture/03 mục 3.2):
    // chỉ gửi 1 lần khi Service khởi động ở fail-secure fallback — không phải state hiện hành nên
    // không resend mỗi lần Overlay reconnect (ChildProcessSupervisor.oneTimeInitialMessages).
    private static IReadOnlyList<Action<IpcPayload>>? BuildOverlayOneTimeMessages(ConfigLoadResult config)
    {
        if (!config.UsedFailSecureFallback)
        {
            return null;
        }

        return [payload => payload.ShowToast = BuildFailSecureToast(payload.MessageId)];
    }

    private static ShowToastCommand BuildFailSecureToast(ulong messageId)
    {
        return new ShowToastCommand
        {
            ToastId = unchecked((uint)messageId),
            Text = "Cấu hình giám sát không còn toàn vẹn, đã tự động khôi phục về mặc định an toàn. Vui lòng kiểm tra lại.",
            Severity = ToastSeverity.Warning,
            ReasonCode = "CONFIG_FALLBACK_TRIGGERED",
            GeneratedAtUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        };
    }
}
