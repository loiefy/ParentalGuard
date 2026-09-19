using System.Runtime.InteropServices;

namespace ParentalGuard.Vision.Capture;

/// <summary>Architecture/05 mục 3.5 (ADR-64): nguồn cửa sổ ứng viên cho các màn hình chưa có candidate.</summary>
public static class WindowZOrderEnumerator
{
    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    /// <summary><c>EnumWindows</c> trả về đúng thứ tự Z-order top→bottom hiện tại.</summary>
    public static IReadOnlyList<IntPtr> EnumerateTopLevelWindowsInZOrder()
    {
        var handles = new List<IntPtr>();
        EnumWindows((hwnd, _) =>
        {
            handles.Add(hwnd);
            return true;
        }, IntPtr.Zero);
        return handles;
    }
}
