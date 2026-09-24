namespace ParentalGuard.UI.Services.IpcClient;

/// <summary>
/// Facade domain Audit log/biểu đồ (Architecture/10-ui-architecture.md mục 2.2/5, `MISC-030`/
/// `FE-070`-`072`) — dùng ở `S3`. CHƯA implement ở giai đoạn nền tảng này — điền ở giai đoạn viết
/// `S3` (`AuditLogQuery`/`AuditChartQuery`/`MarkFalsePositiveRequest`).
/// </summary>
public interface IAuditFacade;
