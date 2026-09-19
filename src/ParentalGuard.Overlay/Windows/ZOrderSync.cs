using System.Runtime.InteropServices;

namespace ParentalGuard.Overlay.Windows;

/// <summary>`BE-087` (Architecture/07-overlay-architecture.md mục 3.5, ADR-56) — z-index cục bộ, không qua IPC.</summary>
public static class ZOrderSync
{
    private static readonly IntPtr _hwndTopmost = new(-1);
    private const uint _swpNoMove = 0x0002;
    private const uint _swpNoSize = 0x0001;
    private const uint _swpNoActivate = 0x0010;

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    /// <summary>
    /// Hàm thuần (test độc lập): lọc <paramref name="allWindowsFrontToBack"/> (kết quả <c>EnumWindows</c>,
    /// vốn trả về top→bottom) theo <paramref name="trackedHandles"/>, giữ nguyên thứ tự tương đối,
    /// rồi đảo ngược thành back→front — đúng thứ tự cần <c>SetWindowPos(HWND_TOPMOST)</c> tuần tự
    /// để ra đúng thứ tự tương đối front→back mong muốn ở bước cuối (mục 3.5).
    /// </summary>
    public static IReadOnlyList<IntPtr> ComputeBackToFrontApplicationOrder(
        IReadOnlyList<IntPtr> allWindowsFrontToBack, IReadOnlySet<IntPtr> trackedHandles)
    {
        List<IntPtr> frontToBack = allWindowsFrontToBack.Where(trackedHandles.Contains).ToList();
        frontToBack.Reverse();
        return frontToBack;
    }

    /// <summary>Đọc toàn bộ cửa sổ top-level hiện tại theo đúng Z-order thật (top→bottom).</summary>
    internal static IReadOnlyList<IntPtr> EnumerateTopLevelWindowsInZOrder()
    {
        var handles = new List<IntPtr>();
        EnumWindows((hwnd, _) =>
        {
            handles.Add(hwnd);
            return true;
        }, IntPtr.Zero);
        return handles;
    }

    /// <summary><c>SWP_NOACTIVATE</c> bắt buộc — tránh cướp focus của cửa sổ đang active (mục 3.5).</summary>
    internal static void Resync(IReadOnlySet<IntPtr> trackedHandles, Func<IntPtr, IntPtr> overlayHandleForTracked)
    {
        IReadOnlyList<IntPtr> allWindows = EnumerateTopLevelWindowsInZOrder();
        IReadOnlyList<IntPtr> applicationOrder = ComputeBackToFrontApplicationOrder(allWindows, trackedHandles);
        foreach (IntPtr trackedHandle in applicationOrder)
        {
            IntPtr overlayHandle = overlayHandleForTracked(trackedHandle);
            if (overlayHandle != IntPtr.Zero)
            {
                SetWindowPos(overlayHandle, _hwndTopmost, 0, 0, 0, 0, _swpNoMove | _swpNoSize | _swpNoActivate);
            }
        }
    }
}
