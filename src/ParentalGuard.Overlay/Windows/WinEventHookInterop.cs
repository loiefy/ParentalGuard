using System.Runtime.InteropServices;

namespace ParentalGuard.Overlay.Windows;

/// <summary>
/// <c>SetWinEventHook</c> dùng chung cho rect real-time (`FE-016b`) và z-order (`BE-087`)
/// (Architecture/07-overlay-architecture.md mục 2.6/3.5) — <c>WINEVENT_OUTOFCONTEXT</c> nên
/// callback chạy trên thread có message pump đã đăng ký (phải là UI thread của <c>OverlayCoordinator</c>).
/// </summary>
internal static class WinEventHookInterop
{
    internal const uint EventObjectLocationChange = 0x800B;
    internal const uint EventSystemForeground = 0x0003;
    internal const uint EventObjectReorder = 0x8004;

    private const uint _winEventOutOfContext = 0;
    private const int _objIdWindow = 0;

    private delegate void WinEventProc(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("user32.dll")]
    private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc, WinEventProc lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("user32.dll")]
    private static extern bool UnhookWinEvent(IntPtr hWinEventHook);

    /// <summary>Đăng ký 1 hook/event id trong <paramref name="eventIds"/> — <paramref name="onEvent"/> nhận (hwnd, eventType).</summary>
    internal static IDisposable Register(IReadOnlyList<uint> eventIds, Action<IntPtr, uint> onEvent)
    {
        // Giữ tham chiếu sống trong Registration — GC không được thu hồi delegate trong lúc hook còn đăng ký.
        WinEventProc callback = (_, eventType, hwnd, idObject, _, _, _) =>
        {
            if (idObject != _objIdWindow || hwnd == IntPtr.Zero)
            {
                return;
            }

            onEvent(hwnd, eventType);
        };

        var hooks = new List<IntPtr>();
        foreach (uint eventId in eventIds)
        {
            IntPtr hook = SetWinEventHook(eventId, eventId, IntPtr.Zero, callback, 0, 0, _winEventOutOfContext);
            if (hook != IntPtr.Zero)
            {
                hooks.Add(hook);
            }
        }

        return new Registration(hooks, callback);
    }

    private sealed class Registration(List<IntPtr> hooks, WinEventProc keepAlive) : IDisposable
    {
        private readonly WinEventProc _keepAlive = keepAlive;

        public void Dispose()
        {
            foreach (IntPtr hook in hooks)
            {
                UnhookWinEvent(hook);
            }
        }
    }
}
