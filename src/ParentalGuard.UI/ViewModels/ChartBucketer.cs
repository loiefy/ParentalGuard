using ParentalGuard.UI.Services.IpcClient;

namespace ParentalGuard.UI.ViewModels;

/// <summary>1 cột biểu đồ — 1 ngày (<see cref="FromDateUtc"/>==<see cref="ToDateUtc"/>) hoặc 1 tuần.</summary>
public sealed record ChartBar(string FromDateUtc, string ToDateUtc, uint BlockedCount)
{
    public bool IsSingleDay => FromDateUtc == ToDateUtc;
}

/// <summary>
/// `FE-071a` (Architecture/10 mục 6.8, ADR-151): 1 tuần/1 tháng hiển thị theo ngày; 3 tháng/6 tháng gộp theo tuần —
/// nhóm 7 ngày liên tiếp tính lùi từ ngày cuối (hôm nay), nhóm đầu tiên có thể ít hơn 7 ngày. Thuần dữ liệu.
/// </summary>
public static class ChartBucketer
{
    public const int DaysPerWeek = 7;

    /// <summary>Khoảng xem dài hơn ngưỡng này thì gộp tuần (90/180 ngày).</summary>
    public const uint WeeklyThresholdDays = 31;

    public static bool IsWeekly(uint rangeDays) => rangeDays > WeeklyThresholdDays;

    public static IReadOnlyList<ChartBar> Bucket(IReadOnlyList<DailyBlockCount> days, uint rangeDays)
    {
        if (!IsWeekly(rangeDays))
        {
            return [.. days.Select(d => new ChartBar(d.DateUtc, d.DateUtc, d.BlockedCount))];
        }

        var bars = new List<ChartBar>();
        for (int end = days.Count; end > 0; end -= DaysPerWeek)
        {
            int start = Math.Max(0, end - DaysPerWeek);
            uint sum = 0;
            for (int i = start; i < end; i++)
            {
                sum += days[i].BlockedCount;
            }

            bars.Add(new ChartBar(days[start].DateUtc, days[end - 1].DateUtc, sum));
        }

        bars.Reverse();
        return bars;
    }
}
