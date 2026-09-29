namespace ParentalGuard.Vision.ModelIntegrity;

/// <summary>
/// Architecture/05-image-pipeline-architecture.md mục 7 (ADR-46, `MISC-090`): hash SHA-256 (hex) của
/// đúng file <c>models/nsfw_model.onnx</c> đóng gói cùng bản build hiện tại — nhúng hằng số trong
/// <c>ParentalGuard.Vision.exe</c> thay vì đọc lại từ <c>config.db</c>/IPC (self-contained, `SEC-017`).
/// PHẢI cập nhật giá trị này (tính lại <c>sha256sum models/nsfw_model.onnx</c>) mỗi khi thay file model
/// bằng bản khác — checksum đồng bộ với chính binary <c>Vision.exe</c> của release đó, không auto-update
/// (`GEN-034`/`MISC-020` REJECTED).
/// </summary>
public static class ExpectedModelChecksum
{
    public const string Sha256Hex = "0af1e2ff246efba9a761b17e60e5c3b38feab2e545ffcf948e168babea971a84";
}
