using System.Runtime.InteropServices;
using ParentalGuard.Service.Security;

namespace ParentalGuard.Service.Session;

public sealed class SessionChangedEventArgs(uint sessionId) : EventArgs
{
    public uint SessionId { get; } = sessionId;
}

/// <summary>
/// Phát hiện đổi active console session (khoá màn hình, Fast User Switching, RDP connect/
/// disconnect) qua <c>WM_WTSSESSION_CHANGE</c> (Architecture/02-process-architecture.md
/// mục 2.3) — chạy trên 1 message-only window trên thread riêng vì service (Session 0)
/// không có message loop mặc định.
/// </summary>
public sealed class SessionWatcher : IDisposable
{
    public event EventHandler<SessionChangedEventArgs>? SessionChanged;

    // Giữ tham chiếu sống tới delegate — nếu bị GC thu hồi trong khi native code vẫn giữ
    // con trỏ hàm (SetWindowLongPtrW), lần gọi lại tiếp theo sẽ crash.
    private readonly WindowInterop.WndProc _wndProcDelegate;
    private readonly ManualResetEventSlim _ready = new(initialState: false);

    private Thread? _thread;
    private volatile uint _threadId;
    private IntPtr _hwnd;
    private IntPtr _originalWndProc;

    public SessionWatcher()
    {
        _wndProcDelegate = WndProc;
    }

    public void Start()
    {
        _thread = new Thread(ThreadMain) { IsBackground = true, Name = "ParentalGuard.SessionWatcher" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        _ready.Wait();
    }

    private void ThreadMain()
    {
        _threadId = WindowInterop.GetCurrentThreadId();
        _hwnd = WindowInterop.CreateWindowExW(
            0, "STATIC", string.Empty, 0, 0, 0, 0, 0,
            WindowInterop.HwndMessage, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);

        if (_hwnd != IntPtr.Zero)
        {
            IntPtr wndProcPtr = Marshal.GetFunctionPointerForDelegate(_wndProcDelegate);
            _originalWndProc = WindowInterop.SetWindowLongPtrW(_hwnd, WindowInterop.GwlpWndProc, wndProcPtr);
            SessionInterop.WTSRegisterSessionNotification(_hwnd, SessionInterop.NotifyForAllSessions);
        }

        _ready.Set();
        if (_hwnd == IntPtr.Zero)
        {
            return;
        }

        while (WindowInterop.GetMessageW(out WindowInterop.MSG msg, IntPtr.Zero, 0, 0) > 0)
        {
            WindowInterop.TranslateMessage(ref msg);
            WindowInterop.DispatchMessageW(ref msg);
        }
    }

    private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WindowInterop.WmWtsSessionChange)
        {
            SessionChanged?.Invoke(this, new SessionChangedEventArgs((uint)lParam.ToInt64()));
            return IntPtr.Zero;
        }

        return WindowInterop.CallWindowProcW(_originalWndProc, hWnd, msg, wParam, lParam);
    }

    public void Dispose()
    {
        if (_hwnd != IntPtr.Zero)
        {
            SessionInterop.WTSUnRegisterSessionNotification(_hwnd);
        }

        if (_threadId != 0)
        {
            // Thoát message loop bằng WM_QUIT; Windows tự dọn HWND khi thread sở hữu nó kết
            // thúc — tránh gọi DestroyWindow chéo-thread (không hợp lệ với window thread-affine).
            WindowInterop.PostThreadMessageW(_threadId, WindowInterop.WmQuit, IntPtr.Zero, IntPtr.Zero);
        }

        _thread?.Join(TimeSpan.FromSeconds(2));
        _ready.Dispose();
    }
}
