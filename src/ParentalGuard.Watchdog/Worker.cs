using System.Collections.Concurrent;
using ParentalGuard.Ipc.Protocol;
using ParentalGuard.Ipc.Tamper;

namespace ParentalGuard.Watchdog;

/// <summary>
/// Orchestrator tối giản (ADR-86, Architecture/09-anti-tamper-architecture.md mục 3.1) — CHỈ 3 việc:
/// heartbeat với <c>Service</c>, gọi SCM start/stop/create, báo cáo sự kiện qua IPC. KHÔNG đọc
/// <c>config.db</c>/<c>auth.dat</c>, KHÔNG tự ghi <c>audit.log</c>.
/// </summary>
public sealed class Worker(ILoggerFactory loggerFactory, ILogger<Worker> logger, StartupArgs startupArgs) : BackgroundService
{
    private RegistryStartValueWatcher? _serviceKeyWatcher;
    private RegistryStartValueWatcher? _watchdogKeyWatcher;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await ApplyScRecoveryOptionsBestEffortAsync(stoppingToken).ConfigureAwait(false);

        var pendingReports = new ConcurrentQueue<WatchdogReportEvent>();
        if (RestartedByWatchdogDetector.WasRestartedByWatchdog(startupArgs.Values))
        {
            // Mục 3.6 — Watchdog không tự ghi audit.log, báo cáo cho Service ngay khi kết nối lại được.
            pendingReports.Enqueue(new WatchdogReportEvent
            {
                EventType = WatchdogEventType.PeerMissedHeartbeatRestarted,
                TargetProcess = "Watchdog",
                DetectedAtUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                ActionTaken = "restarted_by_service",
            });
        }

        StartRegistryTamperWatchersBestEffort(pendingReports);

        var connection = new WatchdogPeerConnection(pendingReports, loggerFactory.CreateLogger<WatchdogPeerConnection>());
        try
        {
            await connection.RunForeverAsync(stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Bình thường lúc SCM stop.
        }
        finally
        {
            _serviceKeyWatcher?.Dispose();
            _watchdogKeyWatcher?.Dispose();
        }
    }

    /// <summary>ANTI-011 (mục 3.5, ADR-89) — idempotent cho chính <c>Watchdog</c>.</summary>
    private async Task ApplyScRecoveryOptionsBestEffortAsync(CancellationToken cancellationToken)
    {
        try
        {
            await ScFailureConfigurator.ConfigureAsync(WatchdogPaths.WatchdogServiceName, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException)
        {
            logger.LogWarning(ex, "Could not configure SCM recovery options (needs SYSTEM/admin) — continuing.");
        }
    }

    /// <summary>ANTI-031 (mục 4) — giám sát CẢ 2 key (chính mình + Service), tự phục hồi; báo cáo qua IPC thay vì tự ghi audit.log (ADR-86).</summary>
    private void StartRegistryTamperWatchersBestEffort(ConcurrentQueue<WatchdogReportEvent> pendingReports)
    {
        try
        {
            void OnTamperDetectedAndRestored(string keyPath, uint oldValue, uint newValue) => pendingReports.Enqueue(new WatchdogReportEvent
            {
                EventType = WatchdogEventType.PeerRegistryTamperDetectedRestored,
                TargetProcess = keyPath,
                DetectedAtUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                ActionTaken = $"restored_start_value_from_0x{oldValue:X8}",
            });

            void OnError(Exception ex) => logger.LogError(ex, "RegistryStartValueWatcher failed.");

            _serviceKeyWatcher = new RegistryStartValueWatcher(WatchdogPaths.ServiceRegistryKeyPath, OnTamperDetectedAndRestored, OnError);
            _watchdogKeyWatcher = new RegistryStartValueWatcher(WatchdogPaths.WatchdogRegistryKeyPath, OnTamperDetectedAndRestored, OnError);
            _serviceKeyWatcher.Start();
            _watchdogKeyWatcher.Start();
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException)
        {
            logger.LogWarning(ex, "Could not start registry tamper watchers (needs SYSTEM/admin) — continuing.");
        }
    }
}

/// <summary>Bọc <c>Main(string[] args)</c> gốc để DI-inject vào <see cref="Worker"/> (cùng khuôn mẫu <c>ParentalGuard.Service.StartupArgs</c>).</summary>
public sealed record StartupArgs(string[] Values);
