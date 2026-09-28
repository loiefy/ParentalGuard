using ParentalGuard.Vision.Pipeline;

namespace ParentalGuard.Vision.Tests;

/// <summary>
/// Architecture/05 mục 3.3/3.6 v0.3.0 (PERF-010 dòng 1, gap phát hiện bởi test-runner Đợt 7): chỉ
/// park vô hạn khi foreground THẬT SỰ nằm trong exclude-list VÀ không còn candidate nào khác.
/// </summary>
public class CaptureLoopWorkerShouldParkInfinitelyTests
{
    [Fact]
    public void ForegroundExcluded_NoCandidates_ReturnsTrue()
    {
        Assert.True(CaptureLoopWorker.ShouldParkInfinitely(foregroundInExcludeList: true, candidateCount: 0));
    }

    [Fact]
    public void ForegroundExcluded_ButOtherMonitorHasCandidate_ReturnsFalse()
    {
        // BE-073a: foreground bị loại trừ không được chặn giám sát cửa sổ khác trên màn hình khác.
        Assert.False(CaptureLoopWorker.ShouldParkInfinitely(foregroundInExcludeList: true, candidateCount: 1));
    }

    [Fact]
    public void ForegroundNotExcluded_NoCandidates_ReturnsFalse()
    {
        // Rỗng candidate vì lý do khác (vd không có cửa sổ foreground) — tự phục hồi ở interval kế tiếp.
        Assert.False(CaptureLoopWorker.ShouldParkInfinitely(foregroundInExcludeList: false, candidateCount: 0));
    }

    [Fact]
    public void ForegroundNotExcluded_HasCandidates_ReturnsFalse()
    {
        Assert.False(CaptureLoopWorker.ShouldParkInfinitely(foregroundInExcludeList: false, candidateCount: 1));
    }
}
