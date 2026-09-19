using System.Runtime.InteropServices;

namespace ParentalGuard.Service.Security;

/// <summary>
/// P/Invoke để hạ Mandatory Label (SACL) của 1 kernel object (named pipe) xuống Low
/// (Architecture/06-security-architecture.md mục 2.5, ADR-31).
/// </summary>
internal static partial class PipeSecurityInterop
{
    internal const uint LabelSecurityInformation = 0x00000010;
    internal const uint SddlRevision1 = 1;

    [LibraryImport("advapi32.dll", EntryPoint = "ConvertStringSecurityDescriptorToSecurityDescriptorW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool ConvertStringSecurityDescriptorToSecurityDescriptor(
        string stringSecurityDescriptor, uint stringSdRevision, out IntPtr securityDescriptor, out uint securityDescriptorSize);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetKernelObjectSecurity(IntPtr handle, uint securityInformation, IntPtr securityDescriptor);

    [LibraryImport("kernel32.dll")]
    internal static partial IntPtr LocalFree(IntPtr hMem);
}
