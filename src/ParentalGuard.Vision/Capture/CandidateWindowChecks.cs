using System.Runtime.InteropServices;

namespace ParentalGuard.Vision.Capture;

/// <summary>Architecture/05 mục 3.5: heuristic loại message-only/helper window khỏi danh sách candidate đa màn hình.</summary>
public static class CandidateWindowChecks
{
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("user32.dll")]
    private static extern int GetWindowTextLengthW(IntPtr hWnd);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out int pvAttribute, int cbAttribute);

    private const int _dwmwaCloaked = 14;

    /// <summary>
    /// Visible, không minimized, không bị DWM cloak, có tiêu đề — không kiểm tra exclude-list (việc của caller,
    /// `ExcludeProcessMatcher`). Cloak (`BE-071a`, 2026-10-01): cửa sổ UWP nền/desktop ảo khác vẫn "visible" theo
    /// <c>IsWindowVisible</c> nhưng không hiện trên màn hình — không được coi là đang hiển thị/che cửa sổ khác.
    /// </summary>
    public static bool IsVisibleTopLevelWindow(IntPtr hwnd) =>
        IsWindowVisible(hwnd) && !IsIconic(hwnd) && GetWindowTextLengthW(hwnd) > 0 && !IsCloaked(hwnd);

    private static bool IsCloaked(IntPtr hwnd) =>
        DwmGetWindowAttribute(hwnd, _dwmwaCloaked, out int cloaked, sizeof(int)) == 0 && cloaked != 0;
}
