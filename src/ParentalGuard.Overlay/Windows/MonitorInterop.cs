using System.Drawing;
using System.Runtime.InteropServices;

namespace ParentalGuard.Overlay.Windows;

/// <summary>
/// 1 màn hình vật lý — <see cref="DeviceName"/> = <c>MONITORINFOEX.szDevice</c> (vd
/// <c>\\.\DISPLAY1</c>), định danh ổn định xuyên suốt các lần khởi động (ADR-67), KHÔNG phải
/// <c>monitor_id</c> của <c>Vision</c>/IPC (chỉ ổn định trong 1 phiên chạy).
/// </summary>
public readonly record struct MonitorInfo(string DeviceName, Rectangle Bounds, Rectangle WorkArea);

/// <summary>Win32 interop cho multi-monitor (`BE-080`-`083`, Architecture/07-overlay-architecture.md mục 4).</summary>
internal static class MonitorInterop
{
    private const uint _monitorDefaultToNearest = 2;
    private const int _cchDeviceName = 32;

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfoEx
    {
        public int CbSize;
        public Rect RcMonitor;
        public Rect RcWork;
        public uint DwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = _cchDeviceName)]
        public string SzDevice;
    }

    private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor, ref Rect lprcMonitor, IntPtr dwData);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfoW(IntPtr hMonitor, ref MonitorInfoEx lpmi);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    /// <summary>ADR-58: overlay gộp tự resolve bounds toàn màn hình (<c>rcMonitor</c>, KHÔNG phải <c>rcWork</c>) từ 1 window handle đại diện.</summary>
    internal static MonitorInfo? GetMonitorInfoForWindow(IntPtr hwnd)
    {
        IntPtr hMonitor = MonitorFromWindow(hwnd, _monitorDefaultToNearest);
        return hMonitor == IntPtr.Zero ? null : ReadMonitorInfo(hMonitor);
    }

    internal static IReadOnlyList<MonitorInfo> EnumerateMonitors()
    {
        var results = new List<MonitorInfo>();
        bool CollectMonitor(IntPtr hMonitor, IntPtr hdcMonitor, ref Rect lprcMonitor, IntPtr dwData)
        {
            MonitorInfo? info = ReadMonitorInfo(hMonitor);
            if (info is { } value)
            {
                results.Add(value);
            }

            return true;
        }

        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, CollectMonitor, IntPtr.Zero);
        return results;
    }

    /// <summary>ADR-61: DPI per-monitor thật — dùng handle CỦA CHÍNH cửa sổ cần scale (không dùng DPI hệ thống chung).</summary>
    internal static double GetDpiScale(IntPtr hwnd) => GetDpiForWindow(hwnd) / 96.0;

    private static MonitorInfo? ReadMonitorInfo(IntPtr hMonitor)
    {
        var raw = new MonitorInfoEx { CbSize = Marshal.SizeOf<MonitorInfoEx>() };
        if (!GetMonitorInfoW(hMonitor, ref raw))
        {
            return null;
        }

        return new MonitorInfo(
            raw.SzDevice,
            Rectangle.FromLTRB(raw.RcMonitor.Left, raw.RcMonitor.Top, raw.RcMonitor.Right, raw.RcMonitor.Bottom),
            Rectangle.FromLTRB(raw.RcWork.Left, raw.RcWork.Top, raw.RcWork.Right, raw.RcWork.Bottom));
    }
}
