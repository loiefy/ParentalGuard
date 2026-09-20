using ParentalGuard.Ipc.Protocol;
using ParentalGuard.Service.Pause;

namespace ParentalGuard.Service.Tests;

/// <summary>`PAUSE-002`/`002a`, ADR-105 (Architecture/02 mục 3a.1) — chống bypass đổi giờ hệ thống: mọi tính toán chỉ dựa vào <c>trustedNowUnixMs</c> truyền vào, không đọc đồng hồ hệ thống trực tiếp.</summary>
public class PauseDurationCalculatorTests
{
    [Theory]
    [InlineData(PauseDuration.FifteenMinutes, 15)]
    [InlineData(PauseDuration.ThirtyMinutes, 30)]
    public void ComputeExpiresAtUnixMs_ShortDurations_AddsExactMinutes(PauseDuration duration, int minutes)
    {
        long trustedNow = 1_700_000_000_000L;

        long expiresAt = PauseDurationCalculator.ComputeExpiresAtUnixMs(duration, trustedNow);

        Assert.Equal(trustedNow + minutes * 60_000L, expiresAt);
    }

    [Fact]
    public void ComputeExpiresAtUnixMs_OneHour_AddsExactlyOneHour()
    {
        long trustedNow = 1_700_000_000_000L;

        long expiresAt = PauseDurationCalculator.ComputeExpiresAtUnixMs(PauseDuration.OneHour, trustedNow);

        Assert.Equal(trustedNow + 3_600_000L, expiresAt);
    }

    [Fact]
    public void ComputeExpiresAtUnixMs_FourHours_AddsExactlyFourHours()
    {
        long trustedNow = 1_700_000_000_000L;

        long expiresAt = PauseDurationCalculator.ComputeExpiresAtUnixMs(PauseDuration.FourHours, trustedNow);

        Assert.Equal(trustedNow + 14_400_000L, expiresAt);
    }

    [Fact]
    public void ComputeExpiresAtUnixMs_Unspecified_FailsSecureToShortestDuration()
    {
        long trustedNow = 1_700_000_000_000L;

        long expiresAt = PauseDurationCalculator.ComputeExpiresAtUnixMs(PauseDuration.Unspecified, trustedNow);

        Assert.Equal(trustedNow + 15 * 60_000L, expiresAt);
    }

    [Fact]
    public void ComputeExpiresAtUnixMs_EndOfDay_ResolvesTo235959_999LocalTime()
    {
        // Giữa trưa (local) — chắc chắn còn trong ngày hôm nay ở mọi múi giờ máy chạy test.
        DateTime localNoon = DateTime.Today.AddHours(12);
        long trustedNow = new DateTimeOffset(localNoon, TimeZoneInfo.Local.GetUtcOffset(localNoon)).ToUnixTimeMilliseconds();

        long expiresAt = PauseDurationCalculator.ComputeExpiresAtUnixMs(PauseDuration.EndOfDay, trustedNow);

        DateTime expectedLocal = DateTime.Today.AddDays(1).AddMilliseconds(-1);
        long expected = new DateTimeOffset(expectedLocal, TimeZoneInfo.Local.GetUtcOffset(expectedLocal)).ToUnixTimeMilliseconds();
        Assert.Equal(expected, expiresAt);
        Assert.True(expiresAt > trustedNow);
    }

    [Fact]
    public void ComputeExpiresAtUnixMs_EndOfDay_ExactlyAtBoundary_RollsToNextDay()
    {
        // Mục 3a.1 — trường hợp hiếm "trustedNow đúng lúc đã qua 23:59:59.999" mô phỏng bằng cách
        // đặt trustedNow CHÍNH XÁC bằng mốc 23:59:59.999 hôm nay — không bao giờ trả về pause 0/âm.
        DateTime endOfToday = DateTime.Today.AddDays(1).AddMilliseconds(-1);
        long trustedNow = new DateTimeOffset(endOfToday, TimeZoneInfo.Local.GetUtcOffset(endOfToday)).ToUnixTimeMilliseconds();

        long expiresAt = PauseDurationCalculator.ComputeExpiresAtUnixMs(PauseDuration.EndOfDay, trustedNow);

        DateTime endOfTomorrow = DateTime.Today.AddDays(2).AddMilliseconds(-1);
        long expected = new DateTimeOffset(endOfTomorrow, TimeZoneInfo.Local.GetUtcOffset(endOfTomorrow)).ToUnixTimeMilliseconds();
        Assert.Equal(expected, expiresAt);
        Assert.True(expiresAt > trustedNow);
    }
}
