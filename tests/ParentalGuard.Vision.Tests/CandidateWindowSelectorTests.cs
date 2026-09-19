using ParentalGuard.Vision.Pipeline;

namespace ParentalGuard.Vision.Tests;

/// <summary>Architecture/05 mục 3.5 (`IMG-020`, `BE-082`, ADR-64) — hàm thuần, không cần Win32/DXGI thật.</summary>
public class CandidateWindowSelectorTests
{
    private static readonly IntPtr _fgHwnd = new(1);
    private static readonly IntPtr _monitor1 = new(101);
    private static readonly IntPtr _monitor2 = new(102);
    private static readonly IntPtr _monitor3 = new(103);

    [Fact]
    public void SingleMonitor_ReturnsOnlyForegroundWindow_NoZOrderScan()
    {
        IReadOnlyList<IntPtr> candidates = CandidateWindowSelector.SelectCandidates(
            _fgHwnd,
            foregroundExcluded: false,
            outputCount: 1,
            windowsInZOrder: [new IntPtr(2), new IntPtr(3)], // không được xét tới vì outputCount == 1
            monitorOf: _ => _monitor1,
            isCandidateWindow: _ => throw new InvalidOperationException("Không được gọi khi outputCount == 1."));

        Assert.Equal([_fgHwnd], candidates);
    }

    [Fact]
    public void SingleMonitor_ForegroundExcluded_ReturnsEmpty()
    {
        IReadOnlyList<IntPtr> candidates = CandidateWindowSelector.SelectCandidates(
            _fgHwnd,
            foregroundExcluded: true,
            outputCount: 1,
            windowsInZOrder: [],
            monitorOf: _ => _monitor1,
            isCandidateWindow: _ => true);

        Assert.Empty(candidates);
    }

    [Fact]
    public void MultiMonitor_AddsOneWindowPerRemainingMonitor_StopsWhenAllCovered()
    {
        var w2 = new IntPtr(2); // trên monitor2, candidate hợp lệ
        var w3 = new IntPtr(3); // trên monitor2 (đã phủ bởi w2) — phải bị bỏ qua
        var w4 = new IntPtr(4); // trên monitor3, candidate hợp lệ
        var monitorByWindow = new Dictionary<IntPtr, IntPtr> { [_fgHwnd] = _monitor1, [w2] = _monitor2, [w3] = _monitor2, [w4] = _monitor3 };

        IReadOnlyList<IntPtr> candidates = CandidateWindowSelector.SelectCandidates(
            _fgHwnd,
            foregroundExcluded: false,
            outputCount: 3,
            windowsInZOrder: [w2, w3, w4],
            monitorOf: w => monitorByWindow[w],
            isCandidateWindow: _ => true);

        Assert.Equal([_fgHwnd, w2, w4], candidates);
    }

    [Fact]
    public void MultiMonitor_ForegroundExcluded_StillFillsItsMonitorFromZOrder()
    {
        // BE-073a: fg bị loại trừ không được thêm vào candidates lẫn không "chiếm chỗ" monitor của nó
        // — 1 cửa sổ khác không bị loại trừ trên cùng màn hình vẫn phải được chọn (fail-secure).
        var replacement = new IntPtr(5);
        var monitorByWindow = new Dictionary<IntPtr, IntPtr> { [_fgHwnd] = _monitor1, [replacement] = _monitor1 };

        IReadOnlyList<IntPtr> candidates = CandidateWindowSelector.SelectCandidates(
            _fgHwnd,
            foregroundExcluded: true,
            outputCount: 2, // > 1 để nhánh Z-order chạy dù chỉ còn 1 monitor thật sự cần phủ
            windowsInZOrder: [replacement],
            monitorOf: w => monitorByWindow[w],
            isCandidateWindow: _ => true);

        Assert.Equal([replacement], candidates);
    }

    [Fact]
    public void MultiMonitor_SkipsWindowsFailingIsCandidateWindow()
    {
        var hiddenWindow = new IntPtr(2);
        var monitorByWindow = new Dictionary<IntPtr, IntPtr> { [_fgHwnd] = _monitor1, [hiddenWindow] = _monitor2 };

        IReadOnlyList<IntPtr> candidates = CandidateWindowSelector.SelectCandidates(
            _fgHwnd,
            foregroundExcluded: false,
            outputCount: 2,
            windowsInZOrder: [hiddenWindow],
            monitorOf: w => monitorByWindow[w],
            isCandidateWindow: _ => false); // vd: ẩn/minimized/exclude-list

        Assert.Equal([_fgHwnd], candidates);
    }

    [Fact]
    public void MultiMonitor_SkipsWindowWithUnresolvedMonitor()
    {
        var closedWindow = new IntPtr(2); // monitorOf trả Zero (vd cửa sổ vừa đóng)
        var monitorByWindow = new Dictionary<IntPtr, IntPtr> { [_fgHwnd] = _monitor1, [closedWindow] = IntPtr.Zero };

        IReadOnlyList<IntPtr> candidates = CandidateWindowSelector.SelectCandidates(
            _fgHwnd,
            foregroundExcluded: false,
            outputCount: 2,
            windowsInZOrder: [closedWindow],
            monitorOf: w => monitorByWindow[w],
            isCandidateWindow: _ => true);

        Assert.Equal([_fgHwnd], candidates);
    }

    [Fact]
    public void NoForegroundWindow_MultiMonitor_StillFillsFromZOrder()
    {
        var w2 = new IntPtr(2);
        var monitorByWindow = new Dictionary<IntPtr, IntPtr> { [w2] = _monitor2 };

        IReadOnlyList<IntPtr> candidates = CandidateWindowSelector.SelectCandidates(
            IntPtr.Zero,
            foregroundExcluded: true,
            outputCount: 2,
            windowsInZOrder: [w2],
            monitorOf: w => monitorByWindow[w],
            isCandidateWindow: _ => true);

        Assert.Equal([w2], candidates);
    }
}
