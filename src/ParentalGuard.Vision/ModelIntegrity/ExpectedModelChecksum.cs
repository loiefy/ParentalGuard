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
#if PARENTALGUARD_MODEL_MARQO
    public const string Sha256Hex = "57ed6a1f1310200d318940ea52a4b47bd084c7ff0311ae68d03421828e42c707"; // nsfw_marqo_384.onnx (tools/export_nsfw_models.py)
#elif PARENTALGUARD_MODEL_FALCONSAI
    public const string Sha256Hex = "af894b02a922315e06d31d6007140c2bc17de585f4ce4db1e3d8417051563b93"; // nsfw_falconsai_224.onnx
#else
    public const string Sha256Hex = "0af1e2ff246efba9a761b17e60e5c3b38feab2e545ffcf948e168babea971a84"; // nsfw_model.onnx (GantMan)
#endif
}
