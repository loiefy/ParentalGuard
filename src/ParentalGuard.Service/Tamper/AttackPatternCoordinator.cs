using ParentalGuard.Ipc.Tamper;
using ParentalGuard.Service.Audit;
using ParentalGuard.Service.Ipc;

namespace ParentalGuard.Service.Tamper;

/// <summary>
/// Bộ đếm <c>Service</c>-side của <c>ANTI-060</c> (Architecture/09-anti-tamper-architecture.md mục
/// 6.1, N=5/T=30 phút, ĐÃ CHỐT 2026-09-20) + cầu nối tới banner on-screen (mục 6.2, ADR-100).
/// KHÔNG chứa bộ đếm <c>Watchdog</c>-side (sống trong RAM <c>Watchdog</c>, mục 6.1) — chỉ nhận kết
/// quả đã vượt ngưỡng của phía đó qua <see cref="RecordWatchdogSideThresholdExceeded"/>.
/// </summary>
public sealed class AttackPatternCoordinator : IDisposable
{
    private static readonly TimeSpan _window = TimeSpan.FromMinutes(30);
    private const int _threshold = 5;

    private readonly SlidingWindowCounter _serviceSideCounter = new(_threshold, _window);
    private readonly AuditLogWriter _auditLog;
    private readonly AttackBannerTimer _banner;

    public AttackPatternCoordinator(AuditLogWriter auditLog, IconStatusCoordinator iconStatus)
    {
        _auditLog = auditLog;
        _banner = new AttackBannerTimer(_window, active => iconStatus.SetAttackBannerActive(active));
    }

    /// <summary>Vision/Overlay restart, VisionNetworkBlocked, TamperDetected(detected_by=Service) — mục 6.1 bảng.</summary>
    public void RecordServiceSideEvent(string trigger)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        bool exceeded = _serviceSideCounter.RecordEventAndCheckThreshold(now);
        if (!exceeded)
        {
            return;
        }

        if (_banner.Arm())
        {
            _ = _auditLog.AppendAsync(
                "AttackPatternDetected",
                new { trigger, count = _threshold, window_started_at_unix_ms = now.ToUnixTimeMilliseconds() - (long)_window.TotalMilliseconds },
                CancellationToken.None);
        }
    }

    /// <summary>Watchdog vừa báo (qua <c>WatchdogReportEvent{PEER_RESTART_THRESHOLD_EXCEEDED}</c>) rằng bộ đếm phía nó đã vượt ngưỡng.</summary>
    public void RecordWatchdogSideThresholdExceeded()
    {
        if (_banner.Arm())
        {
            _ = _auditLog.AppendAsync(
                "AttackPatternDetected",
                new { trigger = "process_restart_loop", count = _threshold, source = "watchdog" },
                CancellationToken.None);
        }
    }

    public void Dispose() => _banner.Dispose();
}
