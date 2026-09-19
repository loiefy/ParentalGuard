namespace ParentalGuard.Service.Auth;

/// <summary>
/// Bảng delay luỹ tiến (`PWD-021`, Architecture/08 mục 7.7) — bộ đếm dùng CHUNG cho mật khẩu lẫn
/// Recovery Key (ADR-76). Hàm thuần, không I/O — test được độc lập với `auth.dat`/RAM state.
/// </summary>
public static class RateLimitPolicy
{
    /// <summary>Ngưỡng ghi <c>AuthBruteForceThresholdReached</c> (mục 7.7, hàng cuối bảng).</summary>
    public const int BruteForceAuditThreshold = 9;

    /// <summary>Delay (ms) trước lần thử tiếp theo, theo số lần sai liên tiếp SAU KHI tăng.</summary>
    public static long DelayMsFor(int consecutiveFailuresAfterIncrement) => consecutiveFailuresAfterIncrement switch
    {
        <= 3 => 0,
        <= 5 => 30_000,
        <= 8 => 5 * 60_000,
        _ => 30 * 60_000,
    };
}
