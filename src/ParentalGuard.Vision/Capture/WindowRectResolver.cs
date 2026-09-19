using System.Runtime.InteropServices;

namespace ParentalGuard.Vision.Capture;

/// <summary>Toạ độ màn hình tuyệt đối của 1 cửa sổ, đúng vùng vẽ thật DWM (`IMG-012`) — không dùng <c>GetWindowRect</c> thô.</summary>
public readonly record struct WindowRect(int X, int Y, int Width, int Height);

public static class WindowRectResolver
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

    /// <summary><c>null</c> nếu cửa sổ không còn tồn tại hoặc API thất bại (HRESULT khác S_OK).</summary>
    public static WindowRect? Resolve(IntPtr hwnd)
    {
        int hr = DwmGetWindowAttribute(hwnd, _dwmwaExtendedFrameBounds, out Rect rect, Marshal.SizeOf<Rect>());
        if (hr != 0)
        {
            return null;
        }

        int width = rect.Right - rect.Left;
        int height = rect.Bottom - rect.Top;
        if (width <= 0 || height <= 0)
        {
            return null;
        }

        return new WindowRect(rect.Left, rect.Top, width, height);
    }
}
