using System.ComponentModel;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;

namespace ParentalGuard.Service.Security;

/// <summary>
/// Tạo Named Pipe server instance cho kênh <c>Vision</c>/<c>Overlay</c> đúng ACL
/// (Architecture/03-ipc-communication.md mục 2.2) + Mandatory Label hạ Low no-write-up
/// (Architecture/06-security-architecture.md mục 2.5, ADR-31 — hệ quả bắt buộc của Low IL).
/// </summary>
public static class PipeAclFactory
{
    public static NamedPipeServerStream CreateServerInstance(string pipeName, SecurityIdentifier allowedUserSid)
    {
        var pipeSecurity = new PipeSecurity();
        // Deny tường minh trước (mục 2.2) — Everyone/ANONYMOUS LOGON/Guests.
        pipeSecurity.SetAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.WorldSid, null), PipeAccessRights.FullControl, AccessControlType.Deny));
        pipeSecurity.SetAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.AnonymousSid, null), PipeAccessRights.FullControl, AccessControlType.Deny));
        pipeSecurity.SetAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinGuestsSid, null), PipeAccessRights.FullControl, AccessControlType.Deny));
        pipeSecurity.SetAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), PipeAccessRights.FullControl, AccessControlType.Allow));
        pipeSecurity.SetAccessRule(new PipeAccessRule(allowedUserSid, PipeAccessRights.ReadWrite, AccessControlType.Allow));

        NamedPipeServerStream pipe = NamedPipeServerStreamAcl.Create(
            pipeName,
            PipeDirection.InOut,
            maxNumberOfServerInstances: 1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous,
            inBufferSize: 0,
            outBufferSize: 0,
            pipeSecurity);

        ApplyLowIntegrityMandatoryLabel(pipe);
        return pipe;
    }

    /// <summary>
    /// Pipe <c>UI</c> (Architecture/03 mục 2.2) — KHÁC pipe Vision/Overlay: phụ huynh có thể mở
    /// dưới 1 tài khoản Windows khác session đang giám sát (`SEC-006`), nên ACL dùng
    /// <c>NT AUTHORITY\INTERACTIVE</c> (bất kỳ user nào đang đăng nhập tương tác cục bộ) thay vì
    /// khoá cứng 1 SID — "đúng là phụ huynh" là trách nhiệm xác thực mật khẩu (`PWD-0xx`), không
    /// phải ACL. Không cần Mandatory Label hạ Low (Architecture/06 mục 2.5 — UI chạy IL bình thường).
    /// </summary>
    public static NamedPipeServerStream CreateUiServerInstance(string pipeName)
    {
        var pipeSecurity = new PipeSecurity();
        pipeSecurity.SetAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.WorldSid, null), PipeAccessRights.FullControl, AccessControlType.Deny));
        pipeSecurity.SetAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.AnonymousSid, null), PipeAccessRights.FullControl, AccessControlType.Deny));
        pipeSecurity.SetAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinGuestsSid, null), PipeAccessRights.FullControl, AccessControlType.Deny));
        pipeSecurity.SetAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), PipeAccessRights.FullControl, AccessControlType.Allow));
        pipeSecurity.SetAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.InteractiveSid, null), PipeAccessRights.ReadWrite, AccessControlType.Allow));

        return NamedPipeServerStreamAcl.Create(
            pipeName,
            PipeDirection.InOut,
            maxNumberOfServerInstances: 1, // mục 6: pipe UI giới hạn 1 kết nối đồng thời
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous,
            inBufferSize: 0,
            outBufferSize: 0,
            pipeSecurity);
    }

    private static void ApplyLowIntegrityMandatoryLabel(NamedPipeServerStream pipe)
    {
        const string lowNoWriteUpSddl = "S:(ML;;NW;;;LW)";
        if (!PipeSecurityInterop.ConvertStringSecurityDescriptorToSecurityDescriptor(
                lowNoWriteUpSddl, PipeSecurityInterop.SddlRevision1, out IntPtr securityDescriptor, out _))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "ConvertStringSecurityDescriptorToSecurityDescriptor failed.");
        }

        try
        {
            if (!PipeSecurityInterop.SetKernelObjectSecurity(
                    pipe.SafePipeHandle.DangerousGetHandle(), PipeSecurityInterop.LabelSecurityInformation, securityDescriptor))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "SetKernelObjectSecurity(LABEL_SECURITY_INFORMATION) failed.");
            }
        }
        finally
        {
            PipeSecurityInterop.LocalFree(securityDescriptor);
        }
    }
}
