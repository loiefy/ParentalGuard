using ParentalGuard.Overlay.Windows;

namespace ParentalGuard.Overlay.Tests;

/// <summary>`BE-087` (Architecture/07-overlay-architecture.md mục 3.5, ADR-56).</summary>
public class ZOrderSyncTests
{
    [Fact]
    public void ComputeBackToFrontApplicationOrder_FiltersUntrackedAndReversesOrder()
    {
        // EnumWindows trả về top→bottom (front→back) — [A(front), B, C, D(back)], B/D không tracked.
        IntPtr a = new(1), b = new(2), c = new(3), d = new(4);
        List<IntPtr> frontToBack = [a, b, c, d];
        var tracked = new HashSet<IntPtr> { a, c };

        IReadOnlyList<IntPtr> applicationOrder = ZOrderSync.ComputeBackToFrontApplicationOrder(frontToBack, tracked);

        // SetWindowPos(TOPMOST) áp theo thứ tự back→front: C trước, A sau — A kết thúc là front-most trong dải tracked.
        Assert.Equal([c, a], applicationOrder);
    }

    [Fact]
    public void ComputeBackToFrontApplicationOrder_NoTrackedWindows_ReturnsEmpty()
    {
        IReadOnlyList<IntPtr> applicationOrder = ZOrderSync.ComputeBackToFrontApplicationOrder([new IntPtr(1), new IntPtr(2)], new HashSet<IntPtr>());

        Assert.Empty(applicationOrder);
    }

    [Fact]
    public void ComputeBackToFrontApplicationOrder_AllTracked_FullyReversed()
    {
        IntPtr a = new(1), b = new(2), c = new(3);
        var tracked = new HashSet<IntPtr> { a, b, c };

        IReadOnlyList<IntPtr> applicationOrder = ZOrderSync.ComputeBackToFrontApplicationOrder([a, b, c], tracked);

        Assert.Equal([c, b, a], applicationOrder);
    }
}
