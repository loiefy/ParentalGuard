using ParentalGuard.Ipc.Protocol;

namespace ParentalGuard.Service.Pause;

/// <summary>`PauseActivated.detail.duration` (Architecture/04-data-architecture.md mục 5.1, v0.2.3) — map đúng 5 chuỗi literal yêu cầu cho audit log.</summary>
public static class PauseDurationMapper
{
    public static string ToAuditLogValue(PauseDuration duration) => duration switch
    {
        PauseDuration.FifteenMinutes => "FIFTEEN_MINUTES",
        PauseDuration.ThirtyMinutes => "THIRTY_MINUTES",
        PauseDuration.OneHour => "ONE_HOUR",
        PauseDuration.FourHours => "FOUR_HOURS",
        PauseDuration.EndOfDay => "END_OF_DAY",
        // UNSPECIFIED không nên xảy ra (PauseDurationCalculator đã fail-secure về 15 phút) — giữ
        // đồng bộ với lựa chọn fail-secure đó thay vì ghi 1 giá trị literal thứ 6 ngoài 5 giá trị đã chốt.
        _ => "FIFTEEN_MINUTES",
    };
}
