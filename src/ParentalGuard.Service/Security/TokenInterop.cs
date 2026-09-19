using System.Runtime.InteropServices;

namespace ParentalGuard.Service.Security;

internal enum SecurityImpersonationLevel
{
    Anonymous = 0,
    Identification = 1,
    Impersonation = 2,
    Delegation = 3,
}

internal enum TokenType
{
    TokenPrimary = 1,
    TokenImpersonation = 2,
}

internal enum TokenInformationClass
{
    TokenIntegrityLevel = 25,
}

[StructLayout(LayoutKind.Sequential)]
internal struct SidAndAttributes
{
    public IntPtr Sid;
    public uint Attributes;
}

[StructLayout(LayoutKind.Sequential)]
internal struct TokenMandatoryLabel
{
    public SidAndAttributes Label;
}

/// <summary>
/// P/Invoke cho pipeline token Restricted Token cổ điển + Low Integrity Level
/// (Architecture/06-security-architecture.md mục 2.1, ADR-30).
/// </summary>
internal static class TokenInterop
{
    internal const uint TokenAllAccess = 0x000F01FF;
    internal const uint DisableMaxPrivilege = 0x1;
    internal const uint SeGroupIntegrity = 0x00000020;

    [DllImport("advapi32.dll", SetLastError = true)]
    internal static extern bool DuplicateTokenEx(
        IntPtr existingToken,
        uint desiredAccess,
        IntPtr tokenAttributes,
        SecurityImpersonationLevel impersonationLevel,
        TokenType tokenType,
        out IntPtr newToken);

    [DllImport("advapi32.dll", SetLastError = true)]
    internal static extern bool CreateRestrictedToken(
        IntPtr existingTokenHandle,
        uint flags,
        uint disableSidCount,
        IntPtr sidsToDisable,
        uint deletePrivilegeCount,
        IntPtr privilegesToDelete,
        uint restrictedSidCount,
        IntPtr sidsToRestrict,
        out IntPtr newTokenHandle);

    [DllImport("advapi32.dll", SetLastError = true)]
    internal static extern bool SetTokenInformation(
        IntPtr tokenHandle,
        TokenInformationClass tokenInformationClass,
        IntPtr tokenInformation,
        uint tokenInformationLength);
}
