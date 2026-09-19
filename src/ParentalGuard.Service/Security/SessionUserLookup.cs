using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace ParentalGuard.Service.Security;

/// <summary>Tra SID của user đang ở 1 session tương tác — dùng để ACL named pipe (Architecture/03 mục 2.2).</summary>
public static class SessionUserLookup
{
    public static SecurityIdentifier GetUserSid(uint sessionId)
    {
        if (!SessionInterop.WTSQueryUserToken(sessionId, out IntPtr token))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "WTSQueryUserToken failed.");
        }

        try
        {
            using var identity = new WindowsIdentity(token);
            return identity.User ?? throw new InvalidOperationException("Interactive session token has no user SID.");
        }
        finally
        {
            SessionInterop.CloseHandle(token);
        }
    }
}
