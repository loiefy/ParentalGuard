using System.Runtime.InteropServices;

namespace ParentalGuard.Ipc.Tamper;

/// <summary>
/// P/Invoke <c>advapi32.dll</c> cho giám sát registry qua <c>RegNotifyChangeKeyValue</c> (không
/// polling — Architecture/09-anti-tamper-architecture.md mục 4.1, ADR-90).
/// </summary>
internal static partial class RegistryTamperInterop
{
    internal static readonly IntPtr HKeyLocalMachine = new(unchecked((int)0x80000002));

    /// <summary>Chỉ dùng cho unit test (<c>RegistryStartValueWatcherTests</c>) — tránh cần quyền SYSTEM để test logic self-heal.</summary>
    internal static readonly IntPtr HKeyCurrentUserForTest = new(unchecked((int)0x80000001));

    internal const int KeyNotify = 0x0010;
    internal const int KeyQueryValue = 0x0001;
    internal const int KeySetValue = 0x0002;
    internal const int KeyWow6464Key = 0x0100;

    internal const uint RegNotifyChangeLastSet = 0x00000004;
    internal const uint RegNotifyThreadAgnostic = 0x10000000; // Win 8+, cho phép chờ trên thread nền bất kỳ

    internal const int RegOptionNonVolatile = 0;
    internal const uint RegDwordType = 4;

    internal const int ErrorSuccess = 0;

    [LibraryImport("advapi32.dll", EntryPoint = "RegOpenKeyExW", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial int RegOpenKeyEx(IntPtr hKey, string subKey, int options, int samDesired, out IntPtr result);

    [LibraryImport("advapi32.dll")]
    internal static partial int RegNotifyChangeKeyValue(IntPtr hKey, [MarshalAs(UnmanagedType.Bool)] bool watchSubtree, uint notifyFilter, IntPtr hEvent, [MarshalAs(UnmanagedType.Bool)] bool asynchronous);

    [LibraryImport("advapi32.dll", EntryPoint = "RegQueryValueExW", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial int RegQueryValueEx(IntPtr hKey, string valueName, IntPtr reserved, out uint type, out uint data, ref int dataSize);

    [LibraryImport("advapi32.dll", EntryPoint = "RegSetValueExW", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial int RegSetValueEx(IntPtr hKey, string valueName, int reserved, uint type, in uint data, int dataSize);

    [LibraryImport("advapi32.dll")]
    internal static partial int RegCloseKey(IntPtr hKey);

    // Chỉ dùng cho unit test (RegistryStartValueWatcherTests) — tạo/xoá key test tạm dưới HKCU.
    [LibraryImport("advapi32.dll", EntryPoint = "RegCreateKeyExW", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial int RegCreateKeyExForTest(IntPtr hKey, string subKey, int reserved, IntPtr classType, int options, int samDesired, IntPtr securityAttributes, out IntPtr result, out int disposition);

    [LibraryImport("advapi32.dll", EntryPoint = "RegDeleteKeyW", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial int RegDeleteKeyForTest(IntPtr hKey, string subKey);
}
