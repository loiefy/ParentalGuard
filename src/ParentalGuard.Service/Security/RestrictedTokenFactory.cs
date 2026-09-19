using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace ParentalGuard.Service.Security;

/// <summary>Token đã hardening, sẵn sàng cho <c>CreateProcessAsUser</c>. Caller phải đóng <see cref="TokenHandle"/> (mục 2.1 bước 7).</summary>
public sealed class RestrictedTokenResult
{
    public required IntPtr TokenHandle { get; init; }

    public required SecurityIdentifier UserSid { get; init; }
}

/// <summary>
/// Pipeline token cho <c>Vision</c>/<c>Overlay</c> (BE-023a bước 1-3 + hardening bước 4-5,
/// Architecture/06-security-architecture.md mục 2.1, ADR-30): Restricted Token cổ điển
/// (<c>DISABLE_MAX_PRIVILEGE</c> + disable SID Administrators) + Low Integrity Level.
/// </summary>
public static class RestrictedTokenFactory
{
    private static readonly SecurityIdentifier _lowIntegritySid = new("S-1-16-4096");

    /// <param name="applyLowIntegrityLevel">
    /// <c>false</c> = fallback Medium IL (Architecture/05-image-pipeline-architecture.md mục 8,
    /// ADR-49) — dùng khi <c>Vision</c> đã thoát với exit code 17 (nghi ngờ Desktop Duplication
    /// API không tương thích Low IL) trong phiên chạy hiện tại của <c>Service</c>.
    /// </param>
    public static RestrictedTokenResult Create(uint sessionId, bool applyLowIntegrityLevel = true)
    {
        if (!SessionInterop.WTSQueryUserToken(sessionId, out IntPtr userToken))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "WTSQueryUserToken failed.");
        }

        try
        {
            SecurityIdentifier userSid;
            using (var identity = new WindowsIdentity(userToken))
            {
                userSid = identity.User ?? throw new InvalidOperationException("Interactive session token has no user SID.");
            }

            if (!TokenInterop.DuplicateTokenEx(
                    userToken, TokenInterop.TokenAllAccess, IntPtr.Zero,
                    SecurityImpersonationLevel.Impersonation, TokenType.TokenPrimary, out IntPtr dupToken))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "DuplicateTokenEx failed.");
            }

            try
            {
                IntPtr restrictedToken = CreateRestrictedTokenWithoutAdministrators(dupToken);
                if (applyLowIntegrityLevel)
                {
                    try
                    {
                        ApplyLowIntegrityLevel(restrictedToken);
                    }
                    catch
                    {
                        SessionInterop.CloseHandle(restrictedToken);
                        throw;
                    }
                }

                return new RestrictedTokenResult { TokenHandle = restrictedToken, UserSid = userSid };
            }
            finally
            {
                SessionInterop.CloseHandle(dupToken);
            }
        }
        finally
        {
            SessionInterop.CloseHandle(userToken);
        }
    }

    private static IntPtr CreateRestrictedTokenWithoutAdministrators(IntPtr dupToken)
    {
        var administratorsSid = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        byte[] sidBytes = new byte[administratorsSid.BinaryLength];
        administratorsSid.GetBinaryForm(sidBytes, 0);

        IntPtr sidBuffer = Marshal.AllocHGlobal(sidBytes.Length);
        IntPtr sidsToDisable = Marshal.AllocHGlobal(Marshal.SizeOf<SidAndAttributes>());
        try
        {
            Marshal.Copy(sidBytes, 0, sidBuffer, sidBytes.Length);
            Marshal.StructureToPtr(new SidAndAttributes { Sid = sidBuffer, Attributes = 0 }, sidsToDisable, fDeleteOld: false);

            // CreateRestrictedToken bỏ qua SID không có mặt trong token gốc (không lỗi) — an
            // toàn để luôn truyền Administrators dù phụ huynh có dùng account đó hay không.
            if (!TokenInterop.CreateRestrictedToken(
                    dupToken, TokenInterop.DisableMaxPrivilege,
                    disableSidCount: 1, sidsToDisable,
                    deletePrivilegeCount: 0, IntPtr.Zero,
                    restrictedSidCount: 0, IntPtr.Zero,
                    out IntPtr restrictedToken))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateRestrictedToken failed.");
            }

            return restrictedToken;
        }
        finally
        {
            Marshal.FreeHGlobal(sidsToDisable);
            Marshal.FreeHGlobal(sidBuffer);
        }
    }

    private static void ApplyLowIntegrityLevel(IntPtr token)
    {
        byte[] sidBytes = new byte[_lowIntegritySid.BinaryLength];
        _lowIntegritySid.GetBinaryForm(sidBytes, 0);

        IntPtr sidBuffer = Marshal.AllocHGlobal(sidBytes.Length);
        IntPtr labelBuffer = Marshal.AllocHGlobal(Marshal.SizeOf<TokenMandatoryLabel>());
        try
        {
            Marshal.Copy(sidBytes, 0, sidBuffer, sidBytes.Length);
            var label = new TokenMandatoryLabel
            {
                Label = new SidAndAttributes { Sid = sidBuffer, Attributes = TokenInterop.SeGroupIntegrity },
            };
            Marshal.StructureToPtr(label, labelBuffer, fDeleteOld: false);

            if (!TokenInterop.SetTokenInformation(
                    token, TokenInformationClass.TokenIntegrityLevel, labelBuffer, (uint)Marshal.SizeOf<TokenMandatoryLabel>()))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "SetTokenInformation(TokenIntegrityLevel) failed.");
            }
        }
        finally
        {
            Marshal.FreeHGlobal(labelBuffer);
            Marshal.FreeHGlobal(sidBuffer);
        }
    }
}
