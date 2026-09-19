using ParentalGuard.Ipc.Protocol;

namespace ParentalGuard.Service.Ipc;

/// <summary>`BE-089b` (Architecture/04-data-architecture.md mục 5.1): map đúng 2 chuỗi literal yêu cầu cho audit log.</summary>
public static class CloseSourceMapper
{
    public static string ToAuditLogValue(CloseSource source) => source switch
    {
        CloseSource.Manual => "manual",
        CloseSource.AutoTimeout => "auto-timeout",
        // UNSPECIFIED không nên xảy ra (Overlay luôn set tường minh) — fail-secure về giá trị ít gây hiểu nhầm nhất.
        _ => "manual",
    };
}
