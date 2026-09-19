using System.Runtime.InteropServices;
using System.Text;

namespace ParentalGuard.Vision.Capture;

/// <summary>Bước 1 (Architecture/05 mục 4.1, `BE-071`): xác định cửa sổ foreground + tên process (cho exclude-list `BE-073a`).</summary>
public static class ForegroundWindowTracker
{
    private const uint _processQueryLimitedInformation = 0x1000;

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool QueryFullProcessImageNameW(IntPtr hProcess, uint dwFlags, StringBuilder lpExeName, ref uint lpdwSize);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr hObject);

    /// <summary>Trả về <see cref="IntPtr.Zero"/> nếu không có cửa sổ foreground nào (desktop trống, secure desktop...).</summary>
    public static IntPtr GetForegroundWindowHandle() => GetForegroundWindow();

    /// <summary>Tên file thực thi (vd <c>chrome.exe</c>) — <c>null</c> nếu không lấy được (process đã thoát, quyền không đủ).</summary>
    public static string? ResolveProcessName(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return null;
        }

        GetWindowThreadProcessId(hwnd, out uint processId);
        if (processId == 0)
        {
            return null;
        }

        IntPtr processHandle = OpenProcess(_processQueryLimitedInformation, false, processId);
        if (processHandle == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            var buffer = new StringBuilder(1024);
            uint size = (uint)buffer.Capacity;
            if (!QueryFullProcessImageNameW(processHandle, 0, buffer, ref size))
            {
                return null;
            }

            string fullPath = buffer.ToString(0, (int)size);
            return Path.GetFileName(fullPath);
        }
        finally
        {
            CloseHandle(processHandle);
        }
    }
}
