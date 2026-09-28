using ParentalGuard.Ipc.Protocol;
using ParentalGuard.Service.Auth;

namespace ParentalGuard.Service.Performance;

public enum CaptureVigilance
{
    Vigilant,
    Relaxed,
}

internal sealed class WindowFrameRateState
{
    public CaptureVigilance Vigilance = CaptureVigilance.Vigilant;
    public int UnchangedStreak;
    public bool Boost;
    public long LastSeenAtUnixMs;
}

/// <summary>
/// Architecture/05-image-pipeline-architecture.md mục 3.7 (`PERF-010`, ADR-130/131/132) — state
/// machine 2 bucket (<see cref="CaptureVigilance.Vigilant"/>/<see cref="CaptureVigilance.Relaxed"/>)
/// với hysteresis BẤT ĐỐI XỨNG, cộng cờ <c>Boost</c> độc lập, quyết định <c>capture_interval_ms</c>
/// gửi xuống <c>Vision</c>. Domain-state RAM-only theo <c>window_handle</c> (ADR-132, `02` mục 5 điểm
/// 3) — reset rỗng khi <c>Service</c> restart, không persist trong <c>config.db</c>.
/// </summary>
public sealed class AdaptiveFrameRateCoordinator
{
    /// <summary>PERF-061 (mục 3.7.6) — PROPOSED, chờ benchmark đa cấu hình máy trước khi khoá cứng.</summary>
    public const int NRelax = 3;

    /// <summary>PERF-061 — PROPOSED.</summary>
    public const float NearThresholdMargin = 0.15f;

    /// <summary>PERF-061 — PROPOSED.</summary>
    public const uint BoostIntervalMs = 500;

    /// <summary>PERF-010 v0.4.0 ĐÃ CHỐT (mục 3.7.4, dòng 3 bảng `PERF-010`) — cố định, không phụ thuộc <c>performance_mode</c>.</summary>
    public const uint VigilantIntervalMs = 1000;

    /// <summary>
    /// PERF-010 v0.4.0 ĐÃ CHỐT (mục 3.7.4) — LƯU Ý: nguồn chính thức là 5000ms (05 mục 3.7.4), KHÔNG
    /// phải 2000ms còn ghi tạm ở `04-data-architecture.md` ADR-127 (chưa kịp amend đồng bộ, xem
    /// mục 3.7.4 đoạn "Ghi chú cần đồng bộ").
    /// </summary>
    public const uint BalancedRelaxedIntervalMs = 5000;

    /// <summary>"maximum_protection" không bao giờ nới lỏng — trùng giá trị <see cref="VigilantIntervalMs"/> (mục 3.7.4).</summary>
    public const uint MaximumProtectionIntervalMs = 1000;

    // ADR-65 tái dùng: "5 chu kỳ liên tiếp không thấy window_handle" (mục 3.7.5) — Service không có
    // tín hiệu "hết 1 chu kỳ" tường minh như Vision (mỗi VisionInferenceResult tới độc lập theo từng
    // cửa sổ), nên xấp xỉ bằng thời gian thực so với interval ĐANG ÁP DỤNG cho toàn bộ Vision — an
    // toàn hơn dùng interval cố định làm mốc (không evict sớm 1 cửa sổ vẫn đang chạy đúng nhịp Relaxed).
    private const int EvictAfterIdleMultiplier = 5;

    private readonly Func<PerformanceMode> _currentPerformanceMode;
    private readonly Func<float> _currentRiskThreshold;
    private readonly MonotonicClock _clock;
    private readonly object _sync = new();
    private readonly Dictionary<ulong, WindowFrameRateState> _states = [];

    private uint _currentIntervalMs = VigilantIntervalMs;

    public AdaptiveFrameRateCoordinator(Func<PerformanceMode> currentPerformanceMode, Func<float> currentRiskThreshold, MonotonicClock clock)
    {
        _currentPerformanceMode = currentPerformanceMode;
        _currentRiskThreshold = currentRiskThreshold;
        _clock = clock;
    }

    /// <summary>Giá trị hiện hành — dùng để điền <c>capture_interval_ms</c> mỗi khi build <c>ControlVisionCommand</c>, kể cả khi không đổi gì (resend lúc reconnect/Pause-Resume).</summary>
    public uint CurrentIntervalMs => _currentIntervalMs;

    /// <summary>
    /// Cập nhật state theo 1 <c>VisionInferenceResult</c> mới. Trả về interval MỚI nếu khác giá trị
    /// đang áp dụng (mục 3.7.4 điểm 5 — caller tự quyết định gửi <c>ControlVisionCommand</c>),
    /// <c>null</c> nếu không đổi (tránh spam IPC).
    /// </summary>
    public uint? HandleVisionResult(VisionInferenceResult result)
    {
        lock (_sync)
        {
            long now = _clock.UtcNowUnixMs;
            Prune(now);

            if (!_states.TryGetValue(result.WindowHandle, out WindowFrameRateState? state))
            {
                state = new WindowFrameRateState();
                _states[result.WindowHandle] = state;
            }

            UpdateVigilance(state, result.ContentChanged);
            UpdateBoost(state, result.RiskScore);
            state.LastSeenAtUnixMs = now;

            uint desired = ComputeDesiredIntervalMs();
            if (desired == _currentIntervalMs)
            {
                return null;
            }

            _currentIntervalMs = desired;
            return desired;
        }
    }

    /// <summary>ADR-130: lên <see cref="CaptureVigilance.Vigilant"/> NGAY LẬP TỨC (kể cả cửa sổ mới lần đầu); xuống <see cref="CaptureVigilance.Relaxed"/> cần đủ <see cref="NRelax"/> frame liên tiếp VÀ CHỈ khi <c>performance_mode == "balanced"</c>.</summary>
    private void UpdateVigilance(WindowFrameRateState state, bool contentChanged)
    {
        if (contentChanged)
        {
            state.UnchangedStreak = 0;
            state.Vigilance = CaptureVigilance.Vigilant;
            return;
        }

        state.UnchangedStreak++;
        if (state.Vigilance == CaptureVigilance.Vigilant
            && state.UnchangedStreak >= NRelax
            && _currentPerformanceMode() == PerformanceMode.Balanced)
        {
            state.Vigilance = CaptureVigilance.Relaxed;
        }
    }

    /// <summary>Mục 3.7.3 — độc lập, KHÔNG hysteresis (bật/tắt ngay theo từng frame), áp dụng như nhau ở cả 2 <c>performance_mode</c>.</summary>
    private void UpdateBoost(WindowFrameRateState state, float riskScore)
    {
        float threshold = _currentRiskThreshold();
        state.Boost = riskScore < threshold && threshold - riskScore <= NearThresholdMargin;
    }

    /// <summary>Mục 3.7.4 — MIN(interval) trên toàn bộ cửa sổ candidate đang theo dõi.</summary>
    private uint ComputeDesiredIntervalMs()
    {
        bool anyBoost = false;
        bool anyVigilant = false;
        foreach (WindowFrameRateState state in _states.Values)
        {
            anyBoost |= state.Boost;
            anyVigilant |= state.Vigilance == CaptureVigilance.Vigilant;
        }

        if (anyBoost)
        {
            return BoostIntervalMs;
        }

        return anyVigilant ? VigilantIntervalMs : StaticIntervalForMode(_currentPerformanceMode());
    }

    private static uint StaticIntervalForMode(PerformanceMode mode) =>
        mode == PerformanceMode.MaximumProtection ? MaximumProtectionIntervalMs : BalancedRelaxedIntervalMs;

    /// <summary>Mục 3.7.5 — evict cửa sổ không còn candidate (đóng cửa sổ/hết foreground lâu).</summary>
    private void Prune(long nowUnixMs)
    {
        long idleTimeoutMs = (long)_currentIntervalMs * EvictAfterIdleMultiplier;
        List<ulong>? expired = null;
        foreach ((ulong handle, WindowFrameRateState state) in _states)
        {
            if (nowUnixMs - state.LastSeenAtUnixMs < idleTimeoutMs)
            {
                continue;
            }

            (expired ??= []).Add(handle);
        }

        if (expired is null)
        {
            return;
        }

        foreach (ulong handle in expired)
        {
            _states.Remove(handle);
        }
    }
}
