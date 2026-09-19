using ParentalGuard.Service.Auth;

namespace ParentalGuard.Service.Tests;

/// <summary>PWD-022, Architecture/08 mục 7.7 ADR-77 — công thức <c>wall0 + (TickCount64 - tick0)</c>.</summary>
public class MonotonicClockTests
{
    [Fact]
    public void UtcNowUnixMs_AtConstruction_IsCloseToWallClock()
    {
        long before = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var clock = new MonotonicClock();
        long after = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        Assert.InRange(clock.UtcNowUnixMs, before - 50, after + 50);
    }

    [Fact]
    public void UtcNowUnixMs_IsMonotonicNonDecreasing_AcrossCalls()
    {
        var clock = new MonotonicClock();
        long first = clock.UtcNowUnixMs;
        Thread.Sleep(20);
        long second = clock.UtcNowUnixMs;

        Assert.True(second >= first);
    }

    [Fact]
    public void UtcNowUnixMs_WithExplicitAnchor_FollowsFormula()
    {
        long tick0 = Environment.TickCount64;
        const long wall0 = 1_000_000_000_000L; // mốc giả tuỳ ý, không phụ thuộc đồng hồ máy thật
        var clock = new MonotonicClock(tick0, wall0);

        long expectedApprox = wall0 + (Environment.TickCount64 - tick0);
        Assert.InRange(clock.UtcNowUnixMs, expectedApprox - 50, expectedApprox + 50);
    }

    /// <summary>Immunity với đổi giờ hệ thống SAU khi anchor đã tạo — <see cref="MonotonicClock"/> không đọc lại <see cref="DateTimeOffset.UtcNow"/> mỗi lần, chỉ cộng dồn tick.</summary>
    [Fact]
    public void UtcNowUnixMs_DoesNotReReadWallClockAfterConstruction()
    {
        var clock = new MonotonicClock(Environment.TickCount64, 500_000L);
        long v1 = clock.UtcNowUnixMs;
        long v2 = clock.UtcNowUnixMs;

        // Cả 2 lần đọc đều xấp xỉ 500_000 (không nhảy tới giá trị DateTimeOffset.UtcNow thật,
        // vốn lớn hơn nhiều — xác nhận công thức không "quên" anchor giả đã cấy).
        Assert.InRange(v1, 499_900, 500_100);
        Assert.InRange(v2, 499_900, 500_100);
    }
}
