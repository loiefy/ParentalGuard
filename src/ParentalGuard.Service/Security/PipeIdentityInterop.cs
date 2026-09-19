using System.Runtime.InteropServices;

namespace ParentalGuard.Service.Security;

/// <summary>P/Invoke lấy PID tiến trình vừa connect vào named pipe (Architecture/03 mục 4.2 bước 1).</summary>
internal static partial class PipeIdentityInterop
{
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetNamedPipeClientProcessId(IntPtr pipeHandle, out uint clientProcessId);
}
