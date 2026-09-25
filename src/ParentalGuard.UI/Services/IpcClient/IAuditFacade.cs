namespace ParentalGuard.UI.Services.IpcClient;

/// <summary>
/// Facade domain Audit log (Architecture/10-ui-architecture.md mục 2.2/5/6.3, `PWD-020`/`MISC-030`) —
/// dùng ở `S3`, sau khi đã qua `S5` Auth Modal (caller sở hữu <c>action_token</c> — facade chỉ forward,
/// không tự tạo/tự xác thực, cùng mẫu hình <see cref="IPauseFacade"/>).
/// </summary>
public interface IAuditFacade
{
    /// <summary>
    /// <c>AuditLogQuery{action_token, page, page_size}</c> (mục 6.3) — <paramref name="actionToken"/>
    /// chỉ bắt buộc hợp lệ ở trang đầu tiên của 1 phiên xem; truyền mảng rỗng cho các trang kế tiếp.
    /// </summary>
    Task<AuditLogFetchResult> GetAuditLogAsync(byte[] actionToken, uint page, uint pageSize, CancellationToken cancellationToken);

    /// <summary><c>MarkFalsePositiveRequest{action_token, process_name}</c> (mục 6.3, `MISC-030`).</summary>
    Task<MarkFalsePositiveOutcome> MarkFalsePositiveAsync(byte[] actionToken, string processName, CancellationToken cancellationToken);
}

public enum AuditLogQueryOutcome
{
    Success,
    InvalidToken,
}

/// <summary>1 dòng lịch sử (mục 6.3) — <see cref="ProcessName"/>/<see cref="RiskScore"/> chỉ có ý nghĩa khi <see cref="EventType"/>="ContentBlocked".</summary>
public sealed record AuditLogEntry(ulong Seq, long TsUnixMs, string EventType, string ProcessName, float RiskScore);

public sealed record AuditLogFetchResult(AuditLogQueryOutcome Outcome, IReadOnlyList<AuditLogEntry> Entries, bool HasMore);

public enum MarkFalsePositiveOutcome
{
    Success,
    InvalidToken,
    AlreadyListed,
}
