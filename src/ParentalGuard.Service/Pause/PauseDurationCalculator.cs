using ParentalGuard.Ipc.Protocol;

namespace ParentalGuard.Service.Pause;

/// <summary>
/// Tính <c>pause_expires_at_unix_ms</c> theo <c>PauseDuration</c> đã chọn (`PAUSE-002`,
/// Architecture/02-process-architecture.md mục 3a.1) — luôn nhận <paramref name="trustedNowUnixMs"/>
/// từ <c>MonotonicClock</c> (ADR-105), KHÔNG bao giờ đọc trực tiếp đồng hồ hệ thống ở đây, để không
/// mở lại đường bypass qua đổi giờ máy (`PAUSE-002`, "không có tuỳ chọn tạm dừng vô thời hạn").
/// </summary>
public static class PauseDurationCalculator
{
    public static long ComputeExpiresAtUnixMs(PauseDuration duration, long trustedNowUnixMs) => duration switch
    {
        PauseDuration.FifteenMinutes => trustedNowUnixMs + (long)TimeSpan.FromMinutes(15).TotalMilliseconds,
        PauseDuration.ThirtyMinutes => trustedNowUnixMs + (long)TimeSpan.FromMinutes(30).TotalMilliseconds,
        PauseDuration.OneHour => trustedNowUnixMs + (long)TimeSpan.FromHours(1).TotalMilliseconds,
        PauseDuration.FourHours => trustedNowUnixMs + (long)TimeSpan.FromHours(4).TotalMilliseconds,
        PauseDuration.EndOfDay => EndOfDayLocalUnixMs(trustedNowUnixMs),
        // UNSPECIFIED không nên xảy ra (UI luôn bắt buộc chọn 1 trong 5 lựa chọn, PAUSE-002) — input
        // IPC không tin cậy (DEV-040): fail-secure về lựa chọn NGẮN NHẤT thay vì "hết hạn ngay lập
        // tức" (gây pause 0 giây, dễ hiểu nhầm là lỗi) hoặc "vô thời hạn" (vi phạm PAUSE-002).
        _ => trustedNowUnixMs + (long)TimeSpan.FromMinutes(15).TotalMilliseconds,
    };

    /// <summary>PAUSE-002a: 23:59:59.999 theo giờ hệ thống LOCAL của ngày hiện tại (tính theo <paramref name="trustedNowUnixMs"/>), không phải UTC.</summary>
    private static long EndOfDayLocalUnixMs(long trustedNowUnixMs)
    {
        DateTime localNow = DateTimeOffset.FromUnixTimeMilliseconds(trustedNowUnixMs).ToLocalTime().DateTime;
        long candidate = EndOfDayFor(localNow.Date);
        if (candidate <= trustedNowUnixMs)
        {
            // Hiếm — trustedNow đúng lúc đã qua 23:59:59.999 khi xử lý (mục 3a.1) → ngày hôm sau,
            // không bao giờ trả về pause 0 giây hoặc âm.
            candidate = EndOfDayFor(localNow.Date.AddDays(1));
        }

        return candidate;
    }

    private static long EndOfDayFor(DateTime localDate)
    {
        DateTime endOfDay = localDate.AddDays(1).AddMilliseconds(-1);
        var offset = new DateTimeOffset(endOfDay, TimeZoneInfo.Local.GetUtcOffset(endOfDay));
        return offset.ToUnixTimeMilliseconds();
    }
}
