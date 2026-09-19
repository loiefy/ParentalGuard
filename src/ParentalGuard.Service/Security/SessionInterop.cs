using System.Runtime.InteropServices;

namespace ParentalGuard.Service.Security;

/// <summary>P/Invoke cho phát hiện session tương tác (WTS*), Architecture/02 mục 2.3.</summary>
internal static partial class SessionInterop
{
    internal const uint InvalidSessionId = 0xFFFFFFFF;

    [LibraryImport("kernel32.dll")]
    internal static partial uint WTSGetActiveConsoleSessionId();

    [LibraryImport("wtsapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool WTSQueryUserToken(uint sessionId, out IntPtr token);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool CloseHandle(IntPtr handle);

    internal const uint NotifyForAllSessions = 1;

    [LibraryImport("wtsapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool WTSRegisterSessionNotification(IntPtr hWnd, uint dwFlags);

    [LibraryImport("wtsapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool WTSUnRegisterSessionNotification(IntPtr hWnd);
}
