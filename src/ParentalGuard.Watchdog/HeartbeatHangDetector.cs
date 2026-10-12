namespace ParentalGuard.Watchdog;

/// <summary>
/// Architecture/09 mục 3.3 (ADR-87) — phát hiện "Service còn kết nối nhưng ngừng ping" (quá <see cref="MissThreshold"/>).
/// Bug real-hardware 2026-10-12: máy thức dậy từ Sleep → "bây giờ − lần ping cuối" gồm cả thời gian ngủ → Watchdog kết luận Service treo
/// và khởi động lại Service (kéo theo Vision/Overlay) ngay lúc người dùng mở máy. Nay đo cả khoảng cách giữa 2 lần kiểm tra liên tiếp:
/// lớn bất thường (&gt; <see cref="SuspendGap"/>) = cả máy/tiến trình vừa bị đóng băng, không phải Service treo → ân hạn lại từ đầu.
/// </summary>
public sealed class HeartbeatHangDetector(DateTimeOffset now)
{
    public static readonly TimeSpan MissThreshold = TimeSpan.FromSeconds(9);

    /// <summary>Vòng kiểm tra chạy mỗi 1 giây — khoảng cách &gt; 3 giây nghĩa là vừa ngủ/đóng băng.</summary>
    public static readonly TimeSpan SuspendGap = TimeSpan.FromSeconds(3);

    private DateTimeOffset _lastPing = now;
    private DateTimeOffset _lastCheck = now;

    public void PingReceived(DateTimeOffset now) => _lastPing = now;

    /// <summary><c>true</c> = Service thật sự ngừng ping quá ngưỡng.</summary>
    public bool IsHung(DateTimeOffset now)
    {
        TimeSpan sinceLastCheck = now - _lastCheck;
        _lastCheck = now;
        if (sinceLastCheck > SuspendGap || sinceLastCheck < TimeSpan.Zero)
        {
            // Vừa thức dậy (hoặc đồng hồ bị chỉnh lùi) — cho Service trọn 1 khoảng ân hạn mới để gửi ping.
            _lastPing = now;
            return false;
        }

        return now - _lastPing > MissThreshold;
    }
}
