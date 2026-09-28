using ParentalGuard.Ipc.Protocol;
using ParentalGuard.Service.Auth;
using ParentalGuard.Service.Performance;

namespace ParentalGuard.Service.Tests;

/// <summary>
/// Architecture/05-image-pipeline-architecture.md mục 3.7 (`PERF-010`, ADR-130/131/132) — state
/// machine 2 bucket + hysteresis bất đối xứng, cờ Boost độc lập, quy đổi capture_interval_ms.
/// </summary>
public class AdaptiveFrameRateCoordinatorTests
{
    private const ulong Hwnd = 1;
    private const float RiskThreshold = 0.7f;

    private static AdaptiveFrameRateCoordinator NewCoordinator(PerformanceMode mode = PerformanceMode.Balanced, MonotonicClock? clock = null) =>
        new(() => mode, () => RiskThreshold, clock ?? new FakeMonotonicClock());

    private static VisionInferenceResult Result(ulong hwnd, bool contentChanged, float riskScore) =>
        new() { WindowHandle = hwnd, ContentChanged = contentChanged, RiskScore = riskScore };

    [Fact]
    public void HandleVisionResult_FirstResultContentChanged_StaysVigilant_NoIntervalChangeSignaled()
    {
        var coordinator = NewCoordinator();

        uint? newInterval = coordinator.HandleVisionResult(Result(Hwnd, contentChanged: true, riskScore: 0.1f));

        Assert.Null(newInterval); // đã Vigilant/1000ms mặc định từ đầu — không đổi gì để gửi lại.
        Assert.Equal(AdaptiveFrameRateCoordinator.VigilantIntervalMs, coordinator.CurrentIntervalMs);
    }

    [Fact]
    public void HandleVisionResult_UnchangedBelowNRelax_StaysVigilant()
    {
        var coordinator = NewCoordinator();
        coordinator.HandleVisionResult(Result(Hwnd, contentChanged: true, riskScore: 0.1f));

        for (int i = 0; i < AdaptiveFrameRateCoordinator.NRelax - 1; i++)
        {
            uint? newInterval = coordinator.HandleVisionResult(Result(Hwnd, contentChanged: false, riskScore: 0.1f));
            Assert.Null(newInterval);
        }

        Assert.Equal(AdaptiveFrameRateCoordinator.VigilantIntervalMs, coordinator.CurrentIntervalMs);
    }

    [Fact]
    public void HandleVisionResult_UnchangedReachesNRelax_BalancedMode_RelaxesTo5000()
    {
        var coordinator = NewCoordinator(PerformanceMode.Balanced);
        coordinator.HandleVisionResult(Result(Hwnd, contentChanged: true, riskScore: 0.1f));

        uint? newInterval = null;
        for (int i = 0; i < AdaptiveFrameRateCoordinator.NRelax; i++)
        {
            newInterval = coordinator.HandleVisionResult(Result(Hwnd, contentChanged: false, riskScore: 0.1f));
        }

        Assert.Equal(AdaptiveFrameRateCoordinator.BalancedRelaxedIntervalMs, newInterval);
        Assert.Equal(AdaptiveFrameRateCoordinator.BalancedRelaxedIntervalMs, coordinator.CurrentIntervalMs);
    }

    [Fact]
    public void HandleVisionResult_ContentChangedAfterRelaxed_ReturnsToVigilantImmediately()
    {
        var coordinator = NewCoordinator(PerformanceMode.Balanced);
        coordinator.HandleVisionResult(Result(Hwnd, contentChanged: true, riskScore: 0.1f));
        for (int i = 0; i < AdaptiveFrameRateCoordinator.NRelax; i++)
        {
            coordinator.HandleVisionResult(Result(Hwnd, contentChanged: false, riskScore: 0.1f));
        }

        Assert.Equal(AdaptiveFrameRateCoordinator.BalancedRelaxedIntervalMs, coordinator.CurrentIntervalMs);

        // 1 frame duy nhất content_changed=true — KHÔNG cần streak để lên lại Vigilant (ADR-130).
        uint? newInterval = coordinator.HandleVisionResult(Result(Hwnd, contentChanged: true, riskScore: 0.1f));

        Assert.Equal(AdaptiveFrameRateCoordinator.VigilantIntervalMs, newInterval);
        Assert.Equal(AdaptiveFrameRateCoordinator.VigilantIntervalMs, coordinator.CurrentIntervalMs);
    }

    [Fact]
    public void HandleVisionResult_MaximumProtectionMode_NeverRelaxesBelow1000()
    {
        var coordinator = NewCoordinator(PerformanceMode.MaximumProtection);
        coordinator.HandleVisionResult(Result(Hwnd, contentChanged: true, riskScore: 0.1f));

        for (int i = 0; i < AdaptiveFrameRateCoordinator.NRelax * 3; i++)
        {
            coordinator.HandleVisionResult(Result(Hwnd, contentChanged: false, riskScore: 0.1f));
        }

        Assert.Equal(AdaptiveFrameRateCoordinator.MaximumProtectionIntervalMs, coordinator.CurrentIntervalMs);
    }

    [Fact]
    public void HandleVisionResult_RiskScoreNearThreshold_BoostsIndependentOfVigilance()
    {
        var coordinator = NewCoordinator(PerformanceMode.Balanced);
        // Đưa cửa sổ về Relaxed trước (5000ms) để chứng minh Boost ĐÈ LÊN kết quả bucket.
        coordinator.HandleVisionResult(Result(Hwnd, contentChanged: true, riskScore: 0.1f));
        for (int i = 0; i < AdaptiveFrameRateCoordinator.NRelax; i++)
        {
            coordinator.HandleVisionResult(Result(Hwnd, contentChanged: false, riskScore: 0.1f));
        }

        Assert.Equal(AdaptiveFrameRateCoordinator.BalancedRelaxedIntervalMs, coordinator.CurrentIntervalMs);

        // risk_score = 0.6, threshold = 0.7 → dưới ngưỡng NHƯNG cách đúng 0.1 (<= margin 0.15) → Boost.
        // content_changed vẫn false (không tự thoát Relaxed) — Boost là cờ ĐỘC LẬP đè lên bucket.
        uint? newInterval = coordinator.HandleVisionResult(Result(Hwnd, contentChanged: false, riskScore: 0.6f));

        Assert.Equal(AdaptiveFrameRateCoordinator.BoostIntervalMs, newInterval);
    }

    [Fact]
    public void HandleVisionResult_RiskScoreFarBelowThreshold_DoesNotBoost()
    {
        var coordinator = NewCoordinator(PerformanceMode.Balanced);

        // risk_score = 0.1, threshold = 0.7 → lệch 0.6, vượt margin 0.15 → không Boost.
        coordinator.HandleVisionResult(Result(Hwnd, contentChanged: true, riskScore: 0.1f));

        Assert.Equal(AdaptiveFrameRateCoordinator.VigilantIntervalMs, coordinator.CurrentIntervalMs);
    }

    [Fact]
    public void HandleVisionResult_RiskScoreAtOrAboveThreshold_DoesNotBoost()
    {
        var coordinator = NewCoordinator(PerformanceMode.Balanced);
        coordinator.HandleVisionResult(Result(Hwnd, contentChanged: true, riskScore: 0.1f));
        for (int i = 0; i < AdaptiveFrameRateCoordinator.NRelax; i++)
        {
            coordinator.HandleVisionResult(Result(Hwnd, contentChanged: false, riskScore: 0.1f));
        }

        // risk_score = threshold (0.7) — overlay đã tự kích hoạt qua luồng khác (ADR-12), không cần Boost nữa.
        uint? newInterval = coordinator.HandleVisionResult(Result(Hwnd, contentChanged: false, riskScore: RiskThreshold));

        Assert.Null(newInterval);
        Assert.Equal(AdaptiveFrameRateCoordinator.BalancedRelaxedIntervalMs, coordinator.CurrentIntervalMs);
    }

    [Fact]
    public void HandleVisionResult_TwoWindows_UsesFastestAcrossAll()
    {
        var coordinator = NewCoordinator(PerformanceMode.Balanced);
        coordinator.HandleVisionResult(Result(1, contentChanged: true, riskScore: 0.1f));
        for (int i = 0; i < AdaptiveFrameRateCoordinator.NRelax; i++)
        {
            coordinator.HandleVisionResult(Result(1, contentChanged: false, riskScore: 0.1f));
        }

        Assert.Equal(AdaptiveFrameRateCoordinator.BalancedRelaxedIntervalMs, coordinator.CurrentIntervalMs);

        // Cửa sổ thứ 2 mới xuất hiện, content_changed=true → Vigilant → MIN(interval) toàn bộ = 1000ms
        // dù cửa sổ 1 vẫn đang Relaxed.
        uint? newInterval = coordinator.HandleVisionResult(Result(2, contentChanged: true, riskScore: 0.1f));

        Assert.Equal(AdaptiveFrameRateCoordinator.VigilantIntervalMs, newInterval);
    }

    [Fact]
    public void HandleVisionResult_NoChangeInDesiredInterval_ReturnsNull_AvoidsIpcSpam()
    {
        var coordinator = NewCoordinator(PerformanceMode.Balanced);
        coordinator.HandleVisionResult(Result(Hwnd, contentChanged: true, riskScore: 0.1f));

        uint? secondCall = coordinator.HandleVisionResult(Result(Hwnd, contentChanged: true, riskScore: 0.1f));

        Assert.Null(secondCall);
    }

    [Fact]
    public void HandleVisionResult_WindowIdleBeyondFiveIntervals_IsEvicted_NoLongerCountedForMin()
    {
        var clock = new FakeMonotonicClock();
        var coordinator = NewCoordinator(PerformanceMode.Balanced, clock);

        // Cửa sổ 1 lên Vigilant (giữ interval hiện hành ở 1000ms).
        coordinator.HandleVisionResult(Result(1, contentChanged: true, riskScore: 0.1f));

        // Cửa sổ 2 xuất hiện rồi "biến mất" (đóng) — không còn VisionInferenceResult nào cho nó nữa.
        coordinator.HandleVisionResult(Result(2, contentChanged: true, riskScore: 0.1f));

        // Vượt quá 5 × interval đang áp dụng (1000ms) mà không thấy cửa sổ 2 nữa → evict (mục 3.7.5).
        clock.Now += 5 * AdaptiveFrameRateCoordinator.VigilantIntervalMs + 1;

        // Đưa cửa sổ 1 (vẫn còn sống) về unchanged đủ để relax — nếu cửa sổ 2 (evicted, mặc định
        // Vigilant) còn bị đếm, kết quả tổng hợp sẽ KHÔNG BAO GIỜ relax được.
        uint? newInterval = null;
        for (int i = 0; i < AdaptiveFrameRateCoordinator.NRelax; i++)
        {
            newInterval = coordinator.HandleVisionResult(Result(1, contentChanged: false, riskScore: 0.1f));
        }

        Assert.Equal(AdaptiveFrameRateCoordinator.BalancedRelaxedIntervalMs, newInterval);
    }
}
