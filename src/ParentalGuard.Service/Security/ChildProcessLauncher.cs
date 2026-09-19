using System.ComponentModel;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using ParentalGuard.Ipc.Client;

namespace ParentalGuard.Service.Security;

public sealed record LaunchedChildProcess(System.Diagnostics.Process Process, uint ProcessId);

/// <summary>
/// Spawn <c>Vision</c>/<c>Overlay</c> vào session tương tác qua token đã hardening
/// (BE-023a bước 6-7, Architecture/06-security-architecture.md mục 2.1) + bootstrap khoá HMAC
/// qua anonymous pipe kế thừa handle (Architecture/03-ipc-communication.md mục 5.2, ADR-18).
///
/// Handle bootstrap KHÔNG đi qua command-line/biến môi trường (cả 2 đều đọc được từ tiến trình
/// khác cùng user qua NtQueryInformationProcess/PEB — đúng lý do ADR-18 cấm). Thay vào đó:
/// truyền qua slot StdInput chuẩn của STARTUPINFO (con đọc lại bằng GetStdHandle, không cần biết
/// trước số hiệu handle), và giới hạn tập handle kế thừa còn đúng 1 handle này qua
/// STARTUPINFOEX + PROC_THREAD_ATTRIBUTE_HANDLE_LIST — không còn kế thừa "mọi handle inheritable
/// hiện có" của Service như bản dùng StartupInfo cổ điển trước đây.
/// </summary>
public static class ChildProcessLauncher
{
    public static LaunchedChildProcess Launch(uint sessionId, string executablePath, string pipeName, uint protocolVersion, byte[] hmacKey, bool applyLowIntegrityLevel = true)
    {
        RestrictedTokenResult token = RestrictedTokenFactory.Create(sessionId, applyLowIntegrityLevel);
        try
        {
            return LaunchWithToken(token.TokenHandle, executablePath, pipeName, protocolVersion, hmacKey);
        }
        finally
        {
            // Architecture/06 mục 2.1 bước 7: đóng ngay, không giữ lại lâu hơn cần thiết.
            SessionInterop.CloseHandle(token.TokenHandle);
        }
    }

    private static LaunchedChildProcess LaunchWithToken(IntPtr tokenHandle, string executablePath, string pipeName, uint protocolVersion, byte[] hmacKey)
    {
        var bootstrapPipe = new AnonymousPipeServerStream(PipeDirection.Out, HandleInheritability.Inheritable);
        try
        {
            IntPtr clientHandle = bootstrapPipe.ClientSafePipeHandle.DangerousGetHandle();
            using ProcessInterop.ProcThreadAttributeList attributeList = ProcessInterop.CreateSingleHandleAttributeList(clientHandle);

            var commandLine = new StringBuilder($"\"{executablePath}\"");
            var startupInfo = new StartupInfoEx
            {
                StartupInfo = new StartupInfo
                {
                    Cb = Marshal.SizeOf<StartupInfoEx>(),
                    Desktop = "winsta0\\default", // token thuộc session tương tác — cần trỏ đúng window station/desktop
                    Flags = ProcessInterop.StartfUsestdhandles,
                    StdInput = clientHandle, // con đọc lại bằng GetStdHandle(STD_INPUT_HANDLE) — không qua command line
                    StdOutput = IntPtr.Zero,
                    StdError = IntPtr.Zero,
                },
                AttributeList = attributeList.Handle,
            };

            bool created = ProcessInterop.CreateProcessAsUser(
                tokenHandle,
                applicationName: null,
                commandLine,
                processAttributes: IntPtr.Zero,
                threadAttributes: IntPtr.Zero,
                inheritHandles: true, // bắt buộc để kế thừa handle hoạt động — nhưng attributeList giới hạn còn đúng 1 handle
                creationFlags: ProcessInterop.CreateUnicodeEnvironment | ProcessInterop.CreateNoWindow | ProcessInterop.ExtendedStartupinfoPresent,
                environment: IntPtr.Zero,
                currentDirectory: Path.GetDirectoryName(executablePath),
                ref startupInfo,
                out ProcessInformation processInfo);

            if (!created)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), $"CreateProcessAsUser failed for '{executablePath}'.");
            }

            SessionInterop.CloseHandle(processInfo.Thread);
            try
            {
                // Đầu client-side handle giờ thuộc sở hữu tiến trình con — Service không cần giữ bản sao của mình nữa.
                bootstrapPipe.DisposeLocalCopyOfClientHandle();
                ChildIpcBootstrap.WriteTo(bootstrapPipe, pipeName, protocolVersion, hmacKey);

                var process = System.Diagnostics.Process.GetProcessById((int)processInfo.ProcessId);
                return new LaunchedChildProcess(process, processInfo.ProcessId);
            }
            finally
            {
                // Đóng dù WriteTo/GetProcessById throw giữa chừng — tránh leak process handle.
                SessionInterop.CloseHandle(processInfo.Process);
            }
        }
        finally
        {
            // Đóng đầu ghi phía Service ngay sau khi ghi xong dữ liệu bootstrap (ADR-18).
            bootstrapPipe.Dispose();
        }
    }
}
