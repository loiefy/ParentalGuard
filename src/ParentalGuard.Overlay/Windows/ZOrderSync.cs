using System.Drawing;
using System.Runtime.InteropServices;

namespace ParentalGuard.Overlay.Windows;

/// <summary>
/// `BE-087` (Architecture/07-overlay-architecture.md mục 3.5, ADR-56) — z-index cục bộ, không qua IPC.
/// Bug real-hardware 2026-10-06: overlay luôn <c>HWND_TOPMOST</c> nên khi cửa sổ khác (vd chính Dashboard) được đưa lên
/// trên cửa sổ vi phạm, overlay vẫn đè lên cửa sổ đó. Nay: overlay chỉ topmost khi KHÔNG có cửa sổ thường nào khác nằm
/// trên và chồng lên cửa sổ vi phạm; ngược lại overlay được hạ xuống nằm ngay trên cửa sổ vi phạm (dưới cửa sổ kia).
/// </summary>
public static class ZOrderSync
{
    private static readonly IntPtr _hwndTop = IntPtr.Zero;
    private static readonly IntPtr _hwndTopmost = new(-1);
    private static readonly IntPtr _hwndNoTopmost = new(-2);
    private const uint _swpNoMove = 0x0002;
    private const uint _swpNoSize = 0x0001;
    private const uint _swpNoActivate = 0x0010;
    private const int _gwlExStyle = -20;
    private const long _wsExTopmost = 0x00000008;
    private const int _dwmwaCloaked = 14;

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr hWnd);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out int pvAttribute, int cbAttribute);

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

    /// <summary>
    /// Hàm thuần (test độc lập): cửa sổ vi phạm nào đang bị 1 cửa sổ "thường" khác (không topmost, không thuộc
    /// Overlay, đang hiển thị — <paramref name="isForeignNormalWindow"/>) nằm TRÊN nó trong Z-order và chồng lên
    /// vùng của nó → overlay của cửa sổ đó phải hạ xuống, không được đè lên cửa sổ khác.
    /// </summary>
    public static IReadOnlySet<IntPtr> ComputeDemoted(
        IReadOnlyList<IntPtr> allWindowsFrontToBack,
        IReadOnlySet<IntPtr> trackedHandles,
        Func<IntPtr, bool> isForeignNormalWindow,
        Func<IntPtr, Rectangle?> boundsOf)
    {
        var demoted = new HashSet<IntPtr>();
        var aboveRects = new List<Rectangle>();
        foreach (IntPtr hwnd in allWindowsFrontToBack)
        {
            if (trackedHandles.Contains(hwnd))
            {
                if (boundsOf(hwnd) is { } tracked && aboveRects.Any(r => r.IntersectsWith(tracked)))
                {
                    demoted.Add(hwnd);
                }

                continue;
            }

            if (isForeignNormalWindow(hwnd) && boundsOf(hwnd) is { } rect)
            {
                aboveRects.Add(rect);
            }
        }

        return demoted;
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
        uint ownProcessId = (uint)Environment.ProcessId;
        IReadOnlySet<IntPtr> demoted = ComputeDemoted(allWindows, trackedHandles, hwnd => IsForeignNormalWindow(hwnd, ownProcessId), DwmInterop.GetExtendedFrameBounds);

        IReadOnlyList<IntPtr> applicationOrder = ComputeBackToFrontApplicationOrder(allWindows, trackedHandles);
        foreach (IntPtr trackedHandle in applicationOrder)
        {
            IntPtr overlayHandle = overlayHandleForTracked(trackedHandle);
            if (overlayHandle == IntPtr.Zero)
            {
                continue;
            }

            if (!demoted.Contains(trackedHandle))
            {
                SetWindowPos(overlayHandle, _hwndTopmost, 0, 0, 0, 0, _swpNoMove | _swpNoSize | _swpNoActivate);
                continue;
            }

            // Rời dải topmost rồi đặt ngay dưới cửa sổ đang nằm sát trên cửa sổ vi phạm (= ngay trên cửa sổ vi phạm).
            SetWindowPos(overlayHandle, _hwndNoTopmost, 0, 0, 0, 0, _swpNoMove | _swpNoSize | _swpNoActivate);
            IntPtr insertAfter = WindowJustAbove(allWindows, trackedHandle, ownProcessId);
            SetWindowPos(overlayHandle, insertAfter, 0, 0, 0, 0, _swpNoMove | _swpNoSize | _swpNoActivate);
        }
    }

    /// <summary>Cửa sổ ngay trên <paramref name="target"/> (bỏ qua cửa sổ của chính Overlay); topmost/không có → HWND_TOP.</summary>
    private static IntPtr WindowJustAbove(IReadOnlyList<IntPtr> frontToBack, IntPtr target, uint ownProcessId)
    {
        IntPtr candidate = _hwndTop;
        foreach (IntPtr hwnd in frontToBack)
        {
            if (hwnd == target)
            {
                break;
            }

            GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid != ownProcessId)
            {
                candidate = hwnd;
            }
        }

        return candidate != _hwndTop && IsTopmost(candidate) ? _hwndTop : candidate;
    }

    private static bool IsTopmost(IntPtr hwnd) => (GetWindowLongPtr(hwnd, _gwlExStyle).ToInt64() & _wsExTopmost) != 0;

    private static bool IsForeignNormalWindow(IntPtr hwnd, uint ownProcessId)
    {
        if (!IsWindowVisible(hwnd) || IsIconic(hwnd) || IsTopmost(hwnd))
        {
            return false;
        }

        GetWindowThreadProcessId(hwnd, out uint pid);
        if (pid == ownProcessId)
        {
            return false;
        }

        // Cửa sổ "ẩn" kiểu UWP/virtual desktop khác vẫn IsWindowVisible=true nhưng bị DWM cloak — không tính.
        return DwmGetWindowAttribute(hwnd, _dwmwaCloaked, out int cloaked, sizeof(int)) != 0 || cloaked == 0;
    }
}
