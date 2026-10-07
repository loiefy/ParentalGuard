using System.Runtime.InteropServices;

namespace ParentalGuard.Overlay.Windows;

/// <summary>
/// `BE-032` thực hiện cục bộ trong Overlay — xem ghi chú gap ở
/// <c>ParentalGuard.Service.Ipc.OverlayDecisionCoordinator</c>: Session 0 Isolation khiến
/// <c>Service</c> không thể tự gọi API <c>user32</c> lên 1 HWND của session tương tác, nên
/// Overlay (đã ở đúng session đó) là nơi duy nhất về vật lý có thể đóng cửa sổ.
/// </summary>
internal static class OverlayWindowInterop
{
    internal const uint WmClose = 0x0010;

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("user32.dll")]
    private static extern bool PostMessageW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr hWnd);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    /// <summary>`BE-034d`: PID tiến trình sở hữu cửa sổ — 0 nếu cửa sổ không còn.</summary>
    internal static uint GetOwningProcessId(ulong windowHandle)
    {
        GetWindowThreadProcessId(new IntPtr(unchecked((long)windowHandle)), out uint pid);
        return pid;
    }

    /// <summary>`BE-034` điều kiện (3): cửa sổ vi phạm còn tồn tại không (vd đã đóng bằng nút X gốc, FE-016).</summary>
    internal static bool WindowExists(ulong windowHandle) => IsWindow(new IntPtr(unchecked((long)windowHandle)));

    /// <summary>Best-effort — không throw nếu cửa sổ đã đóng hoặc handle không hợp lệ.</summary>
    internal static void RequestClose(ulong windowHandle)
    {
        var hwnd = new IntPtr(unchecked((long)windowHandle));
        PostMessageW(hwnd, WmClose, IntPtr.Zero, IntPtr.Zero);
    }
}
