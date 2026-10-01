using ParentalGuard.Ipc.Protocol;
using ParentalGuard.Overlay.Rendering;

namespace ParentalGuard.Overlay.Tests;

/// <summary>
/// `BE-089c` (ĐÃ CHỐT 2026-10-01): mỗi màn hình 1 overlay full-screen riêng, chỉ chứa cửa sổ của chính màn hình
/// đó. Bug real-hardware: Service nhóm theo monitor_id (chỉ số output DXGI theo TỪNG adapter) nên 2 màn hình khác
/// adapter có thể trùng id → 1 overlay dùng chung cho 2 màn hình.
/// </summary>
public class MergedOverlayRegroupTests
{
    private static OverlayRect Merged(uint overlayId, ulong representative, params ulong[] all)
    {
        var rect = new OverlayRect { WindowHandle = representative, OverlayId = overlayId, IsMerged = true };
        rect.MergedWindowHandles.AddRange(all);
        return rect;
    }

    [Fact]
    public void SameMonitorIdFromService_ButWindowsOnTwoRealMonitors_SplitIntoTwoOverlays()
    {
        // Service gửi 1 rect gộp (monitor_id trùng nhau) chứa cửa sổ của 2 màn hình thật.
        var monitorOf = new Dictionary<ulong, string> { [1] = "DISPLAY1", [2] = "DISPLAY2", [3] = "DISPLAY1", [4] = "DISPLAY2" };

        IReadOnlyList<OverlayRect> result = OverlayCoordinator.RegroupMergedByRealMonitor([Merged(7, 1, 1, 2, 3, 4)], h => monitorOf[h]);

        Assert.Equal(2, result.Count);
        Assert.All(result, r => Assert.True(r.IsMerged));
        Assert.Equal([1UL, 3UL], result[0].MergedWindowHandles);
        Assert.Equal([2UL, 4UL], result[1].MergedWindowHandles);
        Assert.Equal(1UL, result[0].WindowHandle);
        Assert.Equal(2UL, result[1].WindowHandle);
    }

    [Fact]
    public void NormalRects_PassThroughUnchanged()
    {
        var normal = new OverlayRect { WindowHandle = 5, OverlayId = 3 };

        IReadOnlyList<OverlayRect> result = OverlayCoordinator.RegroupMergedByRealMonitor([normal], _ => "DISPLAY1");

        Assert.Same(normal, Assert.Single(result));
    }
}
