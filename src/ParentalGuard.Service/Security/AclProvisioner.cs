using System.Security.AccessControl;
using System.Security.Principal;

namespace ParentalGuard.Service.Security;

/// <summary>
/// Áp ACL idempotent cho <c>%ProgramFiles%\ParentalGuard\</c> và
/// <c>%ProgramData%\ParentalGuard\</c> (Architecture/06-security-architecture.md mục 4, ADR-37).
/// Đợt 0 chưa có installer (Đợt 9) — <c>Service</c> tự set ACL tương đương mỗi lần Starting.
/// Registry key (mục 4.3) KHÔNG cần code ở đây — OS đã mặc định giới hạn ghi
/// <c>HKLM\SYSTEM\CurrentControlSet\Services\*</c> cho SYSTEM/Administrators, đúng baseline
/// đã xác nhận trong tài liệu.
/// </summary>
public static class AclProvisioner
{
    /// <summary>Chỉ SYSTEM Full Control — không Allow bất kỳ SID nào khác (mục 4.2).</summary>
    public static void EnsureProgramDataAcl(string path)
    {
        Directory.CreateDirectory(path);
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.SetAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Allow));
        new DirectoryInfo(path).SetAccessControl(security);
    }

    /// <summary>SYSTEM Full Control, Administrators Modify, Authenticated Users Read+Execute (mục 4.1).</summary>
    public static void EnsureProgramFilesAcl(string path)
    {
        Directory.CreateDirectory(path);
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.SetAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Allow));
        security.SetAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            FileSystemRights.Modify,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Allow));
        security.SetAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null),
            FileSystemRights.ReadAndExecute,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Allow));
        new DirectoryInfo(path).SetAccessControl(security);
    }
}
