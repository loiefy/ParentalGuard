using System.Security.Cryptography;
using System.Text;

namespace ParentalGuard.Service.Auth;

/// <summary>
/// Recovery Key — Crockford Base32, 15 byte CSPRNG = 120-bit entropy = 24 ký tự tròn
/// (Architecture/08 mục 6.1, ADR-75 — ĐÃ CHỐT 2026-09-20). Chuẩn hoá lúc verify (mục 6.2) thao tác
/// TRỰC TIẾP trên <c>byte[]</c> UTF-8 — không bao giờ tạo <c>string</c> quản lý cho giá trị người
/// dùng nhập lại, nhất quán tinh thần ADR-73 (giảm thời gian tồn tại bản sao credential trong RAM).
/// </summary>
public static class RecoveryKeyGenerator
{
    // Crockford Base32 — loại I/L/O/U để giảm nhầm lẫn thị giác (mục 6.1).
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
    private const int RawByteLength = 15;
    public const int CanonicalLength = 24;
    private const int GroupSize = 4;

    /// <summary>Sinh key mới — trả về dạng canonical (24 ký tự hoa, không dấu gạch, dùng để hash — mục 6.1/6.3).</summary>
    public static string GenerateCanonical()
    {
        byte[] raw = RandomNumberGenerator.GetBytes(RawByteLength);
        try
        {
            return Encode(raw);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(raw);
        }
    }

    /// <summary>Chia nhóm 4 ký tự/nhóm, 6 nhóm, nối bằng '-' — chỉ để hiển thị (mục 6.1), KHÔNG dùng dạng này để hash.</summary>
    public static string FormatGrouped(string canonical)
    {
        if (canonical.Length != CanonicalLength)
        {
            throw new ArgumentException($"Expected {CanonicalLength}-char canonical Recovery Key.", nameof(canonical));
        }

        var builder = new StringBuilder(CanonicalLength + (CanonicalLength / GroupSize) - 1);
        for (int i = 0; i < canonical.Length; i += GroupSize)
        {
            if (i > 0)
            {
                builder.Append('-');
            }

            builder.Append(canonical, i, GroupSize);
        }

        return builder.ToString();
    }

    /// <summary>
    /// Chuẩn hoá lúc verify (mục 6.2): loại '-'/khoảng trắng, chuyển hoa — thao tác trực tiếp trên
    /// UTF-8 bytes (bảng chữ Crockford Base32 thuần ASCII, không cần decode Unicode đầy đủ).
    /// Trả về mảng MỚI (kích thước &lt;= đầu vào) — caller chịu trách nhiệm zero cả 2 mảng sau khi dùng.
    /// </summary>
    public static byte[] NormalizeUtf8(ReadOnlySpan<byte> rawUtf8)
    {
        byte[] output = GC.AllocateArray<byte>(rawUtf8.Length, pinned: true);
        int written = 0;
        foreach (byte b in rawUtf8)
        {
            if (b == (byte)'-' || IsAsciiWhitespace(b))
            {
                continue;
            }

            output[written++] = ToAsciiUpper(b);
        }

        if (written == output.Length)
        {
            return output;
        }

        byte[] trimmed = GC.AllocateArray<byte>(written, pinned: true);
        output.AsSpan(0, written).CopyTo(trimmed);
        CryptographicOperations.ZeroMemory(output);
        return trimmed;
    }

    private static bool IsAsciiWhitespace(byte b) => b is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n';

    private static byte ToAsciiUpper(byte b) => b is >= (byte)'a' and <= (byte)'z' ? (byte)(b - 32) : b;

    private static string Encode(ReadOnlySpan<byte> raw)
    {
        if (raw.Length != RawByteLength)
        {
            throw new ArgumentException($"Expected {RawByteLength} raw bytes.", nameof(raw));
        }

        Span<char> output = stackalloc char[CanonicalLength];
        int bitBuffer = 0;
        int bitCount = 0;
        int outIndex = 0;
        foreach (byte b in raw)
        {
            bitBuffer = (bitBuffer << 8) | b;
            bitCount += 8;
            while (bitCount >= 5)
            {
                bitCount -= 5;
                output[outIndex++] = Alphabet[(bitBuffer >> bitCount) & 0x1F];
            }
        }

        // 15 byte * 8 bit ÷ 5 bit/ký tự = 24 ký tự tròn, không có bit dư (mục 6.1).
        return new string(output);
    }
}
