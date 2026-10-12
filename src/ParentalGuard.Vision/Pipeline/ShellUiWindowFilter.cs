using System.Runtime.InteropServices;
using System.Text;

namespace ParentalGuard.Vision.Pipeline;

/// <summary>
/// `BE-073c` (2026-10-12): bỏ qua ĐÚNG các cửa sổ giao diện hệ thống của <c>explorer.exe</c> chỉ chứa biểu tượng ứng dụng — khay biểu
/// tượng ẩn (góc phải dưới). KHÔNG loại trừ cả <c>explorer.exe</c> (`BE-073a`: File Explorer xem ảnh/thumbnail; khung xem trước trên
/// taskbar hiện nội dung cửa sổ) — chỉ khớp theo cặp (tiến trình, loại cửa sổ) cố định trong mã, không cấu hình được.
/// </summary>
public static class ShellUiWindowFilter
{
    /// <summary>Win11: <c>TopLevelWindowForOverflowXamlIsland</c> ("System tray overflow window."); Win10: <c>NotifyIconOverflowWindow</c>.</summary>
    internal static readonly IReadOnlySet<string> ExplorerIconOnlyClasses = new HashSet<string>(StringComparer.Ordinal)
    {
        "TopLevelWindowForOverflowXamlIsland",
        "NotifyIconOverflowWindow",
    };

    public static bool IsExcludedShellWindow(string? processName, string? className) =>
        string.Equals(processName, "explorer.exe", StringComparison.OrdinalIgnoreCase)
        && className is not null
        && ExplorerIconOnlyClasses.Contains(className);

    public static string? GetClassName(IntPtr hwnd)
    {
        var buffer = new StringBuilder(256);
        int length = GetClassNameW(hwnd, buffer, buffer.Capacity);
        return length > 0 ? buffer.ToString(0, length) : null;
    }

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassNameW(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);
}
