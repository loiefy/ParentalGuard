namespace ParentalGuard.Service.Tamper;

/// <summary>
/// Banner on-screen của <c>ANTI-061</c> (Architecture/09-anti-tamper-architecture.md mục 6.2,
/// ADR-100): giữ <see cref="ParentalGuard.Ipc.Protocol.IconState.Error"/> LIÊN TỤC kể từ lần
/// <see cref="Arm"/> gần nhất cho tới khi hết cửa sổ <c>T</c> phút không có sự kiện mới nào —
/// <see cref="Arm"/> được gọi độc lập bởi cả 2 nguồn (ngưỡng Service-side vượt cục bộ, HOẶC
/// <c>WatchdogReportEvent{PEER_RESTART_THRESHOLD_EXCEEDED}</c> từ Watchdog) — "bên nào vượt ngưỡng
/// trước, bên đó kích hoạt" (mục 6.1).
/// </summary>
public sealed class AttackBannerTimer : IDisposable
{
    private readonly TimeSpan _window;
    private readonly Action<bool> _onBannerStateChanged;
    private readonly Timer _quietTimer;
    private readonly object _sync = new();
    private bool _active;

    public AttackBannerTimer(TimeSpan window, Action<bool> onBannerStateChanged)
    {
        _window = window;
        _onBannerStateChanged = onBannerStateChanged;
        _quietTimer = new Timer(OnQuietTimeout, null, Timeout.Infinite, Timeout.Infinite);
    }

    /// <summary>
    /// Gọi mỗi khi 1 bộ đếm ANTI-060 (Service-side hoặc Watchdog-side, qua report) vừa vượt ngưỡng —
    /// (re)khởi động cửa sổ im lặng T. Trả về true đúng 1 lần khi banner CHUYỂN từ tắt sang bật (dùng
    /// để caller ghi <c>AttackPatternDetected</c> đúng 1 lần mỗi đợt tấn công, không spam mỗi sự kiện).
    /// </summary>
    public bool Arm()
    {
        bool justActivated = false;
        lock (_sync)
        {
            _quietTimer.Change(_window, Timeout.InfiniteTimeSpan);
            if (!_active)
            {
                _active = true;
                justActivated = true;
            }
        }

        if (justActivated)
        {
            _onBannerStateChanged(true);
        }

        return justActivated;
    }

    private void OnQuietTimeout(object? state)
    {
        lock (_sync)
        {
            if (!_active)
            {
                return;
            }

            _active = false;
        }

        _onBannerStateChanged(false);
    }

    public void Dispose() => _quietTimer.Dispose();
}
