using ParentalGuard.Watchdog;

namespace ParentalGuard.Watchdog.Tests;

/// <summary>Bug real-hardware 2026-10-12: thức dậy từ Sleep bị nhầm là "Service treo" → Watchdog khởi động lại Service.</summary>
public sealed class HeartbeatHangDetectorTests
{
    private static readonly DateTimeOffset _t0 = new(2026, 10, 12, 19, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ServiceStopsPinging_DetectedAfterNineSeconds()
    {
        var detector = new HeartbeatHangDetector(_t0);
        for (int s = 1; s <= 9; s++)
        {
            Assert.False(detector.IsHung(_t0.AddSeconds(s)));
        }

        Assert.True(detector.IsHung(_t0.AddSeconds(10)));
    }

    [Fact]
    public void RegularPings_NeverHung()
    {
        var detector = new HeartbeatHangDetector(_t0);
        for (int s = 1; s <= 60; s++)
        {
            if (s % 3 == 0)
            {
                detector.PingReceived(_t0.AddSeconds(s));
            }

            Assert.False(detector.IsHung(_t0.AddSeconds(s)));
        }
    }

    [Fact]
    public void ResumeFromSleep_GetsFreshGracePeriod_ThenStillDetectsRealHang()
    {
        var detector = new HeartbeatHangDetector(_t0);
        Assert.False(detector.IsHung(_t0.AddSeconds(1)));

        // Máy ngủ 3 ngày: lần kiểm tra kế tiếp cách lần trước rất xa → không phải treo.
        DateTimeOffset wake = _t0.AddDays(3);
        Assert.False(detector.IsHung(wake));

        // Service gửi ping lại bình thường sau khi thức → không bao giờ báo treo.
        detector.PingReceived(wake.AddSeconds(2));
        Assert.False(detector.IsHung(wake.AddSeconds(2)));

        // Nếu sau khi thức Service thật sự im lặng > 9 giây → vẫn phát hiện.
        for (int s = 3; s <= 11; s++)
        {
            Assert.False(detector.IsHung(wake.AddSeconds(s)));
        }

        Assert.True(detector.IsHung(wake.AddSeconds(12)));
    }

    [Fact]
    public void ClockMovedBackwards_NotHung()
    {
        var detector = new HeartbeatHangDetector(_t0);

        Assert.False(detector.IsHung(_t0.AddHours(-1)));
    }
}
