using System.Runtime.InteropServices;

namespace ParentalGuard.Service.Session;

/// <summary>
/// P/Invoke tối thiểu để tạo 1 message-only window (bọc control "STATIC" có sẵn của OS,
/// không cần tự RegisterClass) nhận <c>WM_WTSSESSION_CHANGE</c> — dùng bởi
/// <see cref="SessionWatcher"/>. Dùng <c>DllImport</c> cổ điển thay vì <c>LibraryImport</c> vì
/// cần con trỏ hàm callback (WndProc) qua <c>Marshal.GetFunctionPointerForDelegate</c>.
/// </summary>
internal static class WindowInterop
{
    internal static readonly IntPtr HwndMessage = new(-3);
    internal const int GwlpWndProc = -4;
    internal const uint WmWtsSessionChange = 0x02B1;
    internal const uint WmQuit = 0x0012;

    internal delegate IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    internal struct MSG
    {
        public IntPtr Hwnd;
        public uint Message;
        public IntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public int PtX;
        public int PtY;
    }

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern IntPtr CreateWindowExW(
        uint dwExStyle, string lpClassName, string lpWindowName, uint dwStyle,
        int x, int y, int nWidth, int nHeight,
        IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern IntPtr SetWindowLongPtrW(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll")]
    internal static extern IntPtr CallWindowProcW(IntPtr lpPrevWndFunc, IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    internal static extern int GetMessageW(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    internal static extern bool TranslateMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    internal static extern IntPtr DispatchMessageW(ref MSG lpMsg);

    [DllImport("user32.dll")]
    internal static extern bool PostThreadMessageW(uint idThread, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    internal static extern uint GetCurrentThreadId();
}
