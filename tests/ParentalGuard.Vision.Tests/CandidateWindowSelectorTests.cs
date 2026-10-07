using ParentalGuard.Vision.Capture;
using ParentalGuard.Vision.Pipeline;

namespace ParentalGuard.Vision.Tests;

/// <summary>
/// `BE-071a`/`PERF-020a`/`IMG-020a` (ĐÃ CHỐT 2026-10-01, supersedes chiến lược chỉ-foreground) — giám sát
/// mọi cửa sổ đang hiển thị, foreground trước, rồi Z-order, tối đa 4/chu kỳ. Hàm thuần, không cần Win32/DXGI.
/// </summary>
public class CandidateWindowSelectorTests
{
    private static readonly HashSet<ulong> _noneCovered = [];

    private static WindowSnapshot Win(int handle, int x, int y, int w, int h, bool shown = true, bool monitorable = true) =>
        new(new IntPtr(handle), new WindowRect(x, y, w, h), shown, monitorable);

    /// <summary>Bug real-hardware 2026-10-01: 2 cửa sổ vi phạm đặt cạnh nhau trên 1 màn hình — bản cũ chỉ quét foreground.</summary>
    [Fact]
    public void TwoSideBySideWindowsOnOneMonitor_BothSelected_ForegroundFirst()
    {
        IReadOnlyList<WindowSnapshot> z = [Win(1, 0, 0, 960, 1080), Win(2, 960, 0, 960, 1080)];

        IReadOnlyList<IntPtr> result = CandidateWindowSelector.SelectVisibleCandidates(new IntPtr(2), z, _noneCovered);

        Assert.Equal([new IntPtr(2), new IntPtr(1)], result);
    }

    /// <summary>Bug real-hardware 2026-10-01: overlay (của chính ParentalGuard) chiếm foreground — cửa sổ khác vẫn phải được quét.</summary>
    [Fact]
    public void ForegroundIsOwnOverlay_OtherVisibleWindowsStillSelected()
    {
        IReadOnlyList<WindowSnapshot> z = [Win(9, 0, 0, 960, 1080, monitorable: false), Win(2, 960, 0, 960, 1080)];

        IReadOnlyList<IntPtr> result = CandidateWindowSelector.SelectVisibleCandidates(new IntPtr(9), z, _noneCovered);

        Assert.Equal([new IntPtr(2)], result);
    }

    [Fact]
    public void CoveredWindow_SkippedButStillOccludesWindowsBelow()
    {
        // Cửa sổ 1 đang bị overlay che (BE-034b) và che kín hoàn toàn cửa sổ 2 nằm dưới.
        IReadOnlyList<WindowSnapshot> z = [Win(1, 0, 0, 1920, 1080), Win(2, 100, 100, 800, 600), Win(3, 1920, 0, 1920, 1080)];

        IReadOnlyList<IntPtr> result = CandidateWindowSelector.SelectVisibleCandidates(new IntPtr(1), z, new HashSet<ulong> { 1 });

        Assert.Equal([new IntPtr(3)], result);
    }

    [Fact]
    public void FullyOccludedWindow_Skipped_PartiallyVisibleWindow_Kept()
    {
        IReadOnlyList<WindowSnapshot> z =
        [
            Win(1, 0, 0, 1000, 1000),
            Win(2, 100, 100, 500, 500), // nằm gọn trong cửa sổ 1 → không nhìn thấy
            Win(3, 500, 0, 1000, 1000), // lấn ra ngoài cửa sổ 1 → vẫn nhìn thấy 1 phần
        ];

        IReadOnlyList<IntPtr> result = CandidateWindowSelector.SelectVisibleCandidates(new IntPtr(1), z, _noneCovered);

        Assert.Equal([new IntPtr(1), new IntPtr(3)], result);
    }

    [Fact]
    public void ExcludedOrHiddenOrTinyWindows_NotSelected()
    {
        IReadOnlyList<WindowSnapshot> z =
        [
            Win(1, 0, 0, 800, 600, monitorable: false), // exclude-list / ParentalGuard
            Win(2, 900, 0, 800, 600, shown: false), // minimized / cloaked / không tiêu đề
            Win(3, 0, 700, 40, 40), // nhỏ hơn MinWindowSidePx
            Win(4, 900, 700, 800, 300),
        ];

        IReadOnlyList<IntPtr> result = CandidateWindowSelector.SelectVisibleCandidates(IntPtr.Zero, z, _noneCovered);

        Assert.Equal([new IntPtr(4)], result);
    }

    [Fact]
    public void MoreVisibleWindowsThanCap_Capped_ForegroundAlwaysIncluded()
    {
        int count = CandidateWindowSelector.MaxCandidatesPerCycle + 2;
        IReadOnlyList<WindowSnapshot> z = [.. Enumerable.Range(1, count).Select(i => Win(i, i * 1000, 0, 900, 900))];

        IReadOnlyList<IntPtr> result = CandidateWindowSelector.SelectVisibleCandidates(new IntPtr(count), z, _noneCovered);

        Assert.Equal(CandidateWindowSelector.MaxCandidatesPerCycle, result.Count);
        Assert.Equal(new IntPtr(count), result[0]);
        Assert.Equal([.. Enumerable.Range(1, CandidateWindowSelector.MaxCandidatesPerCycle - 1).Select(i => new IntPtr(i))], result.Skip(1));
    }

    /// <summary>`BE-071c` (2026-10-07): trần 10 cửa sổ/chu kỳ.</summary>
    [Fact]
    public void Cap_IsTenWindowsPerCycle() => Assert.Equal(10, CandidateWindowSelector.MaxCandidatesPerCycle);

    /// <summary>BE-071b (bug real-hardware 2026-10-01): mở nhiều cửa sổ — cửa sổ thứ 5 trở đi từng KHÔNG BAO GIỜ được quét. Xoay vòng: mọi cửa sổ đều tới lượt, foreground luôn có mặt.</summary>
    [Fact]
    public void ManyVisibleWindows_RotationCoversEveryWindow_ForegroundEveryCycle()
    {
        int total = 28; // 27 cửa sổ còn lại / 9 suất mỗi chu kỳ = 3 chu kỳ là quét đủ
        IReadOnlyList<WindowSnapshot> z = [.. Enumerable.Range(1, total).Select(i => Win(i, i * 1000, 0, 900, 900))];
        var seen = new HashSet<IntPtr>();
        int rotation = 0;
        for (int cycle = 0; cycle < 3; cycle++)
        {
            IReadOnlyList<IntPtr> result = CandidateWindowSelector.SelectVisibleCandidates(new IntPtr(total), z, _noneCovered, rotation);
            Assert.Equal(new IntPtr(total), result[0]);
            Assert.Equal(CandidateWindowSelector.MaxCandidatesPerCycle, result.Count);
            seen.UnionWith(result);
            rotation += CandidateWindowSelector.MaxCandidatesPerCycle - 1;
        }

        Assert.Equal(total, seen.Count);
    }

    [Fact]
    public void WindowWithUnresolvedRect_Skipped()
    {
        IReadOnlyList<WindowSnapshot> z = [new(new IntPtr(1), null, true, true), Win(2, 0, 0, 800, 600)];

        Assert.Equal([new IntPtr(2)], CandidateWindowSelector.SelectVisibleCandidates(new IntPtr(1), z, _noneCovered));
    }

    /// <summary>BE-034b (2026-09-30): cửa sổ đang bị overlay che không được capture/phân loại (chỉ còn thấy chính overlay).</summary>
    [Fact]
    public void ExcludeCovered_RemovesOnlyCoveredHandles_PreservingOrder()
    {
        IReadOnlyList<IntPtr> result = CandidateWindowSelector.ExcludeCovered([new IntPtr(1), new IntPtr(2), new IntPtr(3)], new HashSet<ulong> { 2 });

        Assert.Equal([new IntPtr(1), new IntPtr(3)], result);
    }

    [Fact]
    public void ExcludeCovered_EmptyCoveredSet_ReturnsCandidatesUnchanged()
    {
        IReadOnlyList<IntPtr> candidates = [new IntPtr(1)];

        Assert.Same(candidates, CandidateWindowSelector.ExcludeCovered(candidates, new HashSet<ulong>()));
    }
}
