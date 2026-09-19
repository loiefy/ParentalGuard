using System.Runtime.InteropServices;

namespace ParentalGuard.Uninstaller.Interop;

/// <summary>P/Invoke tối thiểu để xoá key <c>Uninstall\ParentalGuard</c> (mục 5.5 bước 12) — chỉ 1 lệnh gọi, không cần gói NuGet <c>Microsoft.Win32.Registry</c> riêng.</summary>
internal static partial class RegistryDeleteInterop
{
    internal static readonly IntPtr HKeyLocalMachine = new(unchecked((int)0x80000002));

    [LibraryImport("advapi32.dll", EntryPoint = "RegDeleteTreeW", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial int RegDeleteTree(IntPtr hKey, string subKey);
}
