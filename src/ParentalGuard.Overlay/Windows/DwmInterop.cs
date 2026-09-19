using System.Drawing;
using System.Runtime.InteropServices;

namespace ParentalGuard.Overlay.Windows;

/// <summary>Đúng API `IMG-012`/Architecture/05 mục 4.1 đã dùng phía Vision cho crop — tái dùng nhất quán ở Overlay (mục 2.4).</summary>
internal static class DwmInterop
{
    private const int _dwmwaExtendedFrameBounds = 9;

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out Rect pvAttribute, int cbAttribute);

    /// <summary><c>null</c> nếu cửa sổ không còn tồn tại hoặc API thất bại.</summary>
    internal static Rectangle? GetExtendedFrameBounds(IntPtr hwnd)
    {
        int hr = DwmGetWindowAttribute(hwnd, _dwmwaExtendedFrameBounds, out Rect rect, Marshal.SizeOf<Rect>());
        if (hr != 0)
        {
            return null;
        }

        int width = rect.Right - rect.Left;
        int height = rect.Bottom - rect.Top;
        return width <= 0 || height <= 0 ? null : new Rectangle(rect.Left, rect.Top, width, height);
    }
}
