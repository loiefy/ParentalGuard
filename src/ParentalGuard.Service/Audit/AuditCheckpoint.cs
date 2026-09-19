namespace ParentalGuard.Service.Audit;

/// <summary>
/// Điểm ghi tiếp theo của <c>audit.log</c> — nguồn sự thật vẫn là chính file (mục 5.4),
/// đây chỉ là giá trị cần thiết để ghi tiếp/khởi tạo lại <c>audit_meta</c> khi tạo mới
/// <c>config.db</c> (Architecture/04-data-architecture.md mục 3.6/6.2 bước 5).
/// </summary>
public sealed record AuditCheckpoint(string ChainId, long LastSeq, string LastHash)
{
    public const string GenesisHash = "0000000000000000000000000000000000000000000000000000000000000000";

    public static AuditCheckpoint CreateGenesis() => new(Guid.NewGuid().ToString(), 0, GenesisHash);
}
