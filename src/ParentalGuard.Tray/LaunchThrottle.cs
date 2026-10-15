namespace ParentalGuard.Tray;

/// <summary>
/// Click liên tục (hoặc double-click = 2 lần click) chỉ mở Dashboard tối đa 1 lần/khoảng — cùng ngưỡng 2 giây với
/// <c>DashboardLauncher</c> của Service (`FE-023`). Dashboard tự đảm bảo single-instance (ADR-117a); lớp này chỉ tránh
/// tạo hàng loạt tiến trình UI thừa trong lúc UI cũ còn đang khởi động.
/// </summary>
internal sealed class LaunchThrottle(TimeSpan minInterval, Func<long> tickCountMs)
{
    private long? _lastLaunchMs;

    public LaunchThrottle()
        : this(TimeSpan.FromSeconds(2), () => Environment.TickCount64)
    {
    }

    /// <returns><c>true</c> nếu được phép mở lúc này (đồng thời ghi nhận thời điểm mở).</returns>
    public bool TryAcquire()
    {
        long now = tickCountMs();
        if (_lastLaunchMs is long last && now - last < (long)minInterval.TotalMilliseconds)
        {
            return false;
        }

        _lastLaunchMs = now;
        return true;
    }
}
