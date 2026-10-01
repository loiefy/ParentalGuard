using ParentalGuard.Service.Audit;

namespace ParentalGuard.Service.Pause;

/// <summary>
/// <c>PAUSE-021</c> (ĐÃ CHỐT v0.2.2, ngưỡng &gt; 5 lần/ngày, chủ dự án xác nhận trực tiếp
/// 2026-09-20) — đếm số event <c>PauseActivated</c> trong <c>audit.log</c> theo ngày lịch UTC.
/// Suy ra từ <c>audit.log</c> (Architecture/04 mục 3.4) — từ 2026-10-01 đếm tăng dần, không quét lại cả file. Dùng ngày lịch UTC (không phải local) vì <c>ts_unix_ms</c> trong audit.log không mang
/// theo múi giờ ghi nhận — spec không quy định cách tính "ngày" cụ thể nên UTC calendar day là lựa
/// chọn nhất quán, không phụ thuộc múi giờ máy đọc lại log về sau (vd Dashboard Đợt 6).
/// </summary>
public static class PauseFrequencyGuard
{
    public const int DailyThreshold = 5;

    public static async Task<(int Count, DateOnly DateUtc)> CountPauseActivatedTodayAsync(string auditLogPath, long nowUnixMs, CancellationToken cancellationToken)
    {
        DateOnly today = DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeMilliseconds(nowUnixMs).UtcDateTime);

        // Bug real-hardware 2026-10-01: quét lại TOÀN BỘ audit.log mỗi lần bấm Tạm dừng (~8s khi log lớn) —
        // nay đếm tăng dần qua AuditLogDailyEventCounter (cùng kết quả, chỉ đọc phần mới ghi thêm).
        Dictionary<DateOnly, uint> counts = await AuditLogDailyEventCounter.CountByDayAsync(auditLogPath, "PauseActivated", cancellationToken).ConfigureAwait(false);
        return ((int)counts.GetValueOrDefault(today), today);
    }
}
