using System.Runtime.InteropServices;

namespace ParentalGuard.Vision.Capture;

/// <summary>
/// Bug real-hardware 2026-10-06 (nhận diện nhầm VS Code/Edge 71–91%): Vision chạy DPI-unaware nên
/// <c>DXGI_OUTPUT_DESC.DesktopCoordinates</c> bị Windows ảo hoá theo tỉ lệ (màn 2880×1920 @200% báo 1440×960),
/// trong khi <c>DWMWA_EXTENDED_FRAME_BOUNDS</c> và texture Desktop Duplication luôn là pixel vật lý → vùng crop lệch,
/// Vision phân loại nhầm nội dung cửa sổ khác. PHẢI gọi trước mọi lời gọi DXGI/Win32 cửa sổ — cùng ADR-61 của Overlay.
/// </summary>
public static class DpiAwareness
{
    private static readonly IntPtr _perMonitorAwareV2 = new(-4);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetProcessDpiAwarenessContext(IntPtr value);

    /// <summary><c>false</c> nếu thất bại (vd đã được đặt trước đó) — caller chỉ ghi log, không dừng tiến trình.</summary>
    public static bool EnablePerMonitorV2() => SetProcessDpiAwarenessContext(_perMonitorAwareV2);
}
