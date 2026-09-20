using System.Text.Json;

namespace ParentalGuard.Service.Pause;

/// <summary>
/// <c>PAUSE-021</c> (ĐÃ CHỐT v0.2.2, ngưỡng &gt; 5 lần/ngày, chủ dự án xác nhận trực tiếp
/// 2026-09-20) — đếm số event <c>PauseActivated</c> trong <c>audit.log</c> theo ngày lịch UTC.
/// Quét trực tiếp <c>audit.log</c> mỗi lần kích hoạt Pause, không cache riêng — đúng quyết định đã
/// ghi ở Architecture/04-data-architecture.md mục 3.4 ("suy ra từ audit.log... không cache riêng ở
/// Đợt 0"). Dùng ngày lịch UTC (không phải local) vì <c>ts_unix_ms</c> trong audit.log không mang
/// theo múi giờ ghi nhận — spec không quy định cách tính "ngày" cụ thể nên UTC calendar day là lựa
/// chọn nhất quán, không phụ thuộc múi giờ máy đọc lại log về sau (vd Dashboard Đợt 6).
/// </summary>
public static class PauseFrequencyGuard
{
    public const int DailyThreshold = 5;

    public static async Task<(int Count, DateOnly DateUtc)> CountPauseActivatedTodayAsync(string auditLogPath, long nowUnixMs, CancellationToken cancellationToken)
    {
        DateOnly today = DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeMilliseconds(nowUnixMs).UtcDateTime);

        if (!File.Exists(auditLogPath))
        {
            return (0, today);
        }

        string[] lines = await File.ReadAllLinesAsync(auditLogPath, cancellationToken).ConfigureAwait(false);

        int count = 0;
        foreach (string line in lines)
        {
            if (!string.IsNullOrWhiteSpace(line) && IsPauseActivatedOn(line, today))
            {
                count++;
            }
        }

        return (count, today);
    }

    private static bool IsPauseActivatedOn(string line, DateOnly today)
    {
        try
        {
            using JsonDocument doc = JsonDocument.Parse(line);
            JsonElement root = doc.RootElement;
            if (!root.TryGetProperty("event_type", out JsonElement eventType) || eventType.GetString() != "PauseActivated")
            {
                return false;
            }

            long tsUnixMs = root.GetProperty("ts_unix_ms").GetInt64();
            return DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeMilliseconds(tsUnixMs).UtcDateTime) == today;
        }
        catch (JsonException)
        {
            // Dòng hỏng/không parse được — không phải việc của bộ đếm này xác minh hash-chain
            // (đó là AuditLogWriter.VerifyTail lúc khởi động), chỉ bỏ qua dòng đó khi đếm tần suất.
            return false;
        }
    }
}
