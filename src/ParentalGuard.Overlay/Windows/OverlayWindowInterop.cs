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

    /// <summary>Best-effort — không throw nếu cửa sổ đã đóng hoặc handle không hợp lệ.</summary>
    internal static void RequestClose(ulong windowHandle)
    {
        var hwnd = new IntPtr(unchecked((long)windowHandle));
        PostMessageW(hwnd, WmClose, IntPtr.Zero, IntPtr.Zero);
    }
}
