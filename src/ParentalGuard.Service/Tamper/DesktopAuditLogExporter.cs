using ParentalGuard.Service.Security;

namespace ParentalGuard.Service.Tamper;

/// <summary>
/// Copy <c>audit.log</c> (plaintext, SEC-041) sang Desktop của user đang ở session tương tác hiện
/// tại trước khi <c>%ProgramData%</c> bị xoá (ANTI-020, Architecture/09 mục 5.5 bước 2, ADR-97) —
/// mượn token qua <c>WTSQueryUserToken</c> + <c>SHGetKnownFolderPath</c>, kỹ thuật đã dùng cho
/// <c>CreateProcessAsUser</c> (`06` mục 2.1).
/// </summary>
public static class DesktopAuditLogExporter
{
    /// <summary>Trả về đường dẫn file đã copy, hoặc null nếu thất bại (không xác định được session/lỗi I/O — mục 5.5 bước 2: KHÔNG xoá audit.log gốc nếu thất bại).</summary>
    public static string? TryExport(string auditLogPath)
    {
        uint sessionId = SessionInterop.WTSGetActiveConsoleSessionId();
        if (sessionId == SessionInterop.InvalidSessionId)
        {
            return null;
        }

        if (!SessionInterop.WTSQueryUserToken(sessionId, out IntPtr token))
        {
            return null;
        }

        try
        {
            int hr = KnownFolderInterop.SHGetKnownFolderPath(KnownFolderInterop.FolderIdDesktop, 0, token, out IntPtr pathPtr);
            if (hr != 0)
            {
                return null;
            }

            string desktopPath;
            try
            {
                desktopPath = System.Runtime.InteropServices.Marshal.PtrToStringUni(pathPtr) ?? string.Empty;
            }
            finally
            {
                KnownFolderInterop.CoTaskMemFree(pathPtr);
            }

            if (string.IsNullOrEmpty(desktopPath) || !File.Exists(auditLogPath))
            {
                return null;
            }

            string destination = Path.Combine(desktopPath, $"ParentalGuard_AuditLog_{DateTime.UtcNow:yyyyMMdd_HHmmss}.jsonl");
            File.Copy(auditLogPath, destination, overwrite: false);
            return destination;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
        finally
        {
            SessionInterop.CloseHandle(token);
        }
    }
}
