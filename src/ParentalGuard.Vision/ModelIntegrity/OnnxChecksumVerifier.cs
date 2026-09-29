using System.Security.Cryptography;

namespace ParentalGuard.Vision.ModelIntegrity;

/// <summary>
/// Architecture/05-image-pipeline-architecture.md mục 7 (ADR-46, `MISC-090`): so SHA-256 của
/// bytes model đã đọc với hash kỳ vọng — so sánh constant-time để không phát sinh timing oracle.
/// Gọi từ <c>Program.cs</c> (Đợt 8) trước khi load <c>InferenceSession</c>, hash kỳ vọng ở
/// <see cref="ExpectedModelChecksum"/>.
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
