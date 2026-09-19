using ParentalGuard.Ipc.Tamper;

namespace ParentalGuard.Service.Tests;

/// <summary>
/// ANTI-060 (Architecture/09-anti-tamper-architecture.md mục 6.1) — N=5/T=30 phút, 2 bộ đếm
/// độc lập (Service-side/Watchdog-side) dùng CHUNG công thức này. Test công thức thuần, không phụ
/// thuộc process nào đang chạy.
/// </summary>
public class SlidingWindowCounterTests
{
    private static readonly DateTimeOffset _t0 = DateTimeOffset.Parse("2026-01-01T00:00:00Z");

    [Fact]
    public void RecordEventAndCheckThreshold_BelowThreshold_ReturnsFalse()
    {
        var counter = new SlidingWindowCounter(thresholdCount: 5, window: TimeSpan.FromMinutes(30));

        for (int i = 0; i < 4; i++)
        {
            Assert.False(counter.RecordEventAndCheckThreshold(_t0.AddSeconds(i)));
        }
    }

    [Fact]
    public void RecordEventAndCheckThreshold_AtThreshold_ReturnsTrue()
    {
        var counter = new SlidingWindowCounter(thresholdCount: 5, window: TimeSpan.FromMinutes(30));

        for (int i = 0; i < 4; i++)
        {
            counter.RecordEventAndCheckThreshold(_t0.AddSeconds(i));
        }

        Assert.True(counter.RecordEventAndCheckThreshold(_t0.AddSeconds(4)));
    }

    [Fact]
    public void RecordEventAndCheckThreshold_EventsOutsideWindow_ArePruned_NoFalsePositive()
    {
        var counter = new SlidingWindowCounter(thresholdCount: 5, window: TimeSpan.FromMinutes(30));

        // 4 sự kiện xảy ra RẤT lâu trước (ngoài cửa sổ 30 phút) — không được cộng dồn với sự kiện mới.
        for (int i = 0; i < 4; i++)
        {
            counter.RecordEventAndCheckThreshold(_t0.AddHours(-2).AddSeconds(i));
        }

        Assert.False(counter.RecordEventAndCheckThreshold(_t0));
    }

    [Fact]
    public void RecordEventAndCheckThreshold_SlidingWindow_OldEventsExpireButRecentOnesCount()
    {
        var counter = new SlidingWindowCounter(thresholdCount: 5, window: TimeSpan.FromMinutes(30));

        counter.RecordEventAndCheckThreshold(_t0); // sẽ hết hạn ở mốc kiểm tra cuối
        for (int i = 0; i < 3; i++)
        {
            counter.RecordEventAndCheckThreshold(_t0.AddMinutes(31).AddSeconds(i));
        }

        // Sự kiện thứ 5 trong cửa sổ 30 phút TÍNH TỪ mốc mới (sự kiện đầu tiên đã hết hạn, không tính).
        Assert.False(counter.RecordEventAndCheckThreshold(_t0.AddMinutes(31).AddSeconds(3)));
        Assert.True(counter.RecordEventAndCheckThreshold(_t0.AddMinutes(31).AddSeconds(4)));
    }

    [Fact]
    public void RecordEventAndCheckThreshold_IndependentCountersDoNotInterfere()
    {
        // Mô phỏng đúng thiết kế mục 6.1 — 2 instance độc lập (Service-side/Watchdog-side).
        var serviceSide = new SlidingWindowCounter(5, TimeSpan.FromMinutes(30));
        var watchdogSide = new SlidingWindowCounter(5, TimeSpan.FromMinutes(30));

        for (int i = 0; i < 5; i++)
        {
            serviceSide.RecordEventAndCheckThreshold(_t0.AddSeconds(i));
        }

        Assert.False(watchdogSide.RecordEventAndCheckThreshold(_t0));
    }
}
