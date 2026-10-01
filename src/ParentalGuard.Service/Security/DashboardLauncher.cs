using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace ParentalGuard.Service.Security;

/// <summary>
/// `FE-023` (Specification/03 v0.11.0, ĐÃ CHỐT 2026-10-01): double-click icon trạng thái mở Dashboard.
/// <c>Overlay</c> chạy Low IL — nếu tự spawn <c>ParentalGuard.UI.exe</c>, UI sẽ kế thừa Low IL và không mở
/// được pipe UI. Vì vậy Overlay chỉ GỬI YÊU CẦU (<c>OpenDashboardRequest</c>), còn <c>Service</c> (SYSTEM)
/// khởi chạy UI bằng token của chính user đang đăng nhập session đó (Medium IL, như mở từ Start Menu).
/// Chỉ chạy đúng 1 đường dẫn cố định, không tham số, không kế thừa handle — Overlay bị chiếm quyền cũng
/// không thể dùng kênh này để chạy thứ gì khác. Trùng lặp được UI tự xử lý (single-instance, ADR-117a).
/// </summary>
public static class DashboardLauncher
{
    private static long _lastLaunchTicks;

    /// <summary>Chống spam: Overlay (không tin cậy) gửi liên tục cũng chỉ tạo tối đa 1 tiến trình/2 giây.</summary>
    private static readonly TimeSpan _minInterval = TimeSpan.FromSeconds(2);

    [DllImport("userenv.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateEnvironmentBlock(out IntPtr environment, IntPtr token, [MarshalAs(UnmanagedType.Bool)] bool inherit);

    [DllImport("userenv.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyEnvironmentBlock(IntPtr environment);

    /// <returns><c>false</c> nếu bị bỏ qua do chống spam.</returns>
    public static bool Launch(uint sessionId, string uiExecutablePath)
    {
        long now = Environment.TickCount64;
        long last = Interlocked.Read(ref _lastLaunchTicks);
        if (last != 0 && now - last < (long)_minInterval.TotalMilliseconds)
        {
            return false;
        }

        Interlocked.Exchange(ref _lastLaunchTicks, now);

        if (!SessionInterop.WTSQueryUserToken(sessionId, out IntPtr userToken))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "WTSQueryUserToken failed.");
        }

        IntPtr environment = IntPtr.Zero;
        try
        {
            if (!CreateEnvironmentBlock(out environment, userToken, inherit: false))
            {
                environment = IntPtr.Zero; // UI vẫn chạy được với môi trường mặc định — không chặn việc mở Dashboard.
            }

            var startupInfo = new StartupInfoEx
            {
                StartupInfo = new StartupInfo
                {
                    Cb = Marshal.SizeOf<StartupInfo>(),
                    Desktop = "winsta0\\default",
                },
            };

            bool created = ProcessInterop.CreateProcessAsUser(
                userToken,
                applicationName: uiExecutablePath,
                new StringBuilder($"\"{uiExecutablePath}\""),
                processAttributes: IntPtr.Zero,
                threadAttributes: IntPtr.Zero,
                inheritHandles: false,
                creationFlags: ProcessInterop.CreateUnicodeEnvironment,
                environment,
                currentDirectory: Path.GetDirectoryName(uiExecutablePath),
                ref startupInfo,
                out ProcessInformation processInfo);
            if (!created)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), $"CreateProcessAsUser failed for '{uiExecutablePath}'.");
            }

            SessionInterop.CloseHandle(processInfo.Thread);
            SessionInterop.CloseHandle(processInfo.Process);
            return true;
        }
        finally
        {
            if (environment != IntPtr.Zero)
            {
                DestroyEnvironmentBlock(environment);
            }

            SessionInterop.CloseHandle(userToken);
        }
    }
}
