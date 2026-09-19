using System.Runtime.InteropServices;

namespace ParentalGuard.Uninstaller.Interop;

/// <summary>P/Invoke <c>MoveFileExW</c> — kỹ thuật self-delete kinh điển (mục 5.5 bước 13).</summary>
internal static partial class MoveFileExInterop
{
    internal const uint MoveFileDelayUntilReboot = 0x00000004;

    [LibraryImport("kernel32.dll", EntryPoint = "MoveFileExW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool MoveFileEx(string existingFileName, string? newFileName, uint flags);
}
