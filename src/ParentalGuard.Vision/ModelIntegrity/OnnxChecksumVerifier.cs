using System.Security.Cryptography;

namespace ParentalGuard.Vision.ModelIntegrity;

/// <summary>
/// Architecture/05-image-pipeline-architecture.md mục 7 (ADR-46, `MISC-090`): so SHA-256 của
/// bytes model đã đọc với hash kỳ vọng — so sánh constant-time để không phát sinh timing oracle.
/// Utility thuần, sẵn sàng dùng — CHƯA được Program.cs gọi ở Đợt 1 (`MISC-090` là phạm vi Đợt 8
/// theo ROADMAP.md mục 3; không mở khoá sớm để tránh chặn happy-path khi chưa có hash chính thức).
/// </summary>
public static class OnnxChecksumVerifier
{
    public static bool Verify(byte[] modelBytes, byte[] expectedSha256)
    {
        ArgumentNullException.ThrowIfNull(modelBytes);
        ArgumentNullException.ThrowIfNull(expectedSha256);

        byte[] actual = SHA256.HashData(modelBytes);
        return CryptographicOperations.FixedTimeEquals(actual, expectedSha256);
    }
}
