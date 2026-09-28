using System.Runtime.InteropServices;

namespace ParentalGuard.Vision.Capture;

/// <summary>
/// Architecture/05-image-pipeline-architecture.md mục 3.6 (ADR-133) — Thread thứ 3 của <c>Vision</c>,
/// tách biệt Thread IPC (async, mục 3.1) và Thread Capture-Inference (DXGI/ONNX, ADR-38): bơm message
/// cho 1 message-only window để nhận <c>EVENT_SYSTEM_FOREGROUND</c> (đánh thức
/// <see cref="_onForegroundChanged"/> ngay lập tức — PERF-020, park vô hạn ở exclude-list) và
/// <c>WM_DISPLAYCHANGE</c> (đặt <see cref="DisplayChanged"/>). KHÔNG gọi bất kỳ API DXGI/ONNX Runtime
/// nào — không vi phạm ADR-38.
/// </summary>
public sealed class WindowMessagePump
{
    private const uint WinEventOutOfContext = 0;
    private const uint EventSystemForeground = 0x0003;
    private const uint WmDisplayChange = 0x007E;
    private const int HwndMessage = -3;

    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    private delegate void WinEventDelegate(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

    [StructLayout(LayoutKind.Sequential)]
    private struct Msg
    {
        public IntPtr Hwnd;
        public uint Message;
        public IntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public int PtX;
        public int PtY;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WndClassEx
    {
        public uint CbSize;
        public uint Style;
        public WndProcDelegate WndProc;
        public int CbClsExtra;
        public int CbWndExtra;
        public IntPtr HInstance;
        public IntPtr HIcon;
        public IntPtr HCursor;
        public IntPtr HbrBackground;
        public string? LpszMenuName;
        public string LpszClassName;
        public IntPtr HIconSm;
    }

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern ushort RegisterClassEx(ref WndClassEx lpwcx);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowEx(uint dwExStyle, string lpClassName, string? lpWindowName, uint dwStyle, int x, int y, int nWidth, int nHeight, IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("user32.dll")]
    private static extern bool GetMessage(out Msg lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref Msg lpMsg);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage(ref Msg lpMsg);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("user32.dll")]
    private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc, WinEventDelegate lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("kernel32.dll")]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    private readonly Action _onForegroundChanged;

    // Giữ tham chiếu sống suốt vòng đời thread — GC không được thu hồi delegate trong lúc native code
    // vẫn có thể gọi callback qua con trỏ đã đăng ký (RegisterClassEx/SetWinEventHook).
    private WndProcDelegate? _wndProcDelegate;
    private WinEventDelegate? _winEventDelegate;
    private int _displayChanged;

    public WindowMessagePump(Action onForegroundChanged)
    {
        _onForegroundChanged = onForegroundChanged;
    }

    /// <summary>Đặt bởi <c>WM_DISPLAYCHANGE</c> — Thread Capture-Inference đọc + tự reset qua <see cref="ConsumeDisplayChanged"/> ở đầu mỗi vòng lặp (mục 3.6).</summary>
    public bool ConsumeDisplayChanged() => Interlocked.Exchange(ref _displayChanged, 0) != 0;

    public void Start()
    {
        var thread = new Thread(Run)
        {
            IsBackground = true,
            Name = "ParentalGuard.Vision.WindowMessagePump",
        };
        thread.Start();
    }

    private void Run()
    {
        _wndProcDelegate = WndProcImpl;
        IntPtr moduleHandle = GetModuleHandle(null);
        string className = $"ParentalGuard.Vision.MessagePump.{Environment.ProcessId}";
        var windowClass = new WndClassEx
        {
            CbSize = (uint)Marshal.SizeOf<WndClassEx>(),
            WndProc = _wndProcDelegate,
            HInstance = moduleHandle,
            LpszClassName = className,
        };
        RegisterClassEx(ref windowClass);

        _ = CreateWindowEx(0, className, null, 0, 0, 0, 0, 0, new IntPtr(HwndMessage), IntPtr.Zero, moduleHandle, IntPtr.Zero);

        _winEventDelegate = OnWinEvent;
        SetWinEventHook(EventSystemForeground, EventSystemForeground, IntPtr.Zero, _winEventDelegate, idProcess: 0, idThread: 0, WinEventOutOfContext);

        while (GetMessage(out Msg msg, IntPtr.Zero, 0, 0))
        {
            TranslateMessage(ref msg);
            DispatchMessage(ref msg);
        }
    }

    private IntPtr WndProcImpl(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WmDisplayChange)
        {
            Interlocked.Exchange(ref _displayChanged, 1);
        }

        return DefWindowProc(hWnd, msg, wParam, lParam);
    }

    private void OnWinEvent(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime) =>
        _onForegroundChanged();
}
