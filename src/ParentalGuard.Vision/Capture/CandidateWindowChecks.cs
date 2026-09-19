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

    /// <summary>Visible, không minimized, có tiêu đề — không kiểm tra exclude-list (việc của caller, `ExcludeProcessMatcher`).</summary>
    public static bool IsVisibleTopLevelWindow(IntPtr hwnd) =>
        IsWindowVisible(hwnd) && !IsIconic(hwnd) && GetWindowTextLengthW(hwnd) > 0;
}
