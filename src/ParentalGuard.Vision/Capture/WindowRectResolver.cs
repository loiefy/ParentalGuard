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

    /// <summary>
    /// Đổi <paramref name="windowRect"/> (toạ độ virtual desktop) sang vùng crop trong texture Desktop
    /// Duplication của 1 output: trừ offset <paramref name="outputBounds"/> rồi cắt về trong biên
    /// output. <c>null</c> nếu cửa sổ không có phần nào nằm trên output này.
    /// Bug đã sửa 2026-09-30: trước đây dùng thẳng toạ độ desktop làm box crop — màn hình phụ (toạ độ
    /// âm/khác 0) hoặc cửa sổ lấn mép tạo box không hợp lệ, <c>CopySubresourceRegion</c> im lặng không
    /// copy gì → Vision phân loại ảnh đen, không bao giờ phát hiện vi phạm.
    /// </summary>
    public static WindowRect? ToOutputLocalCrop(WindowRect windowRect, WindowRect outputBounds)
    {
        int left = Math.Max(windowRect.X, outputBounds.X);
        int top = Math.Max(windowRect.Y, outputBounds.Y);
        int right = Math.Min(windowRect.X + windowRect.Width, outputBounds.X + outputBounds.Width);
        int bottom = Math.Min(windowRect.Y + windowRect.Height, outputBounds.Y + outputBounds.Height);
        if (right <= left || bottom <= top)
        {
            return null;
        }

        return new WindowRect(left - outputBounds.X, top - outputBounds.Y, right - left, bottom - top);
    }
}
