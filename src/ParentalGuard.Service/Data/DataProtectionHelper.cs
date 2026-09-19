using System.Security.Cryptography;

namespace ParentalGuard.Service.Data;

/// <summary>
/// DPAPI machine-scope qua <see cref="ProtectedData"/> (Architecture/06-security-architecture.md
/// mục 5, ADR-35) — dùng cho mọi blob nhạy cảm trong <c>config.db</c>/<c>auth.dat</c>.
/// </summary>
public static class DataProtectionHelper
{
    /// <summary>
    /// _entropy bổ sung nhúng trong <c>Service</c> (mục 5) — không phải bí mật chống kẻ tấn công
    /// đọc được binary (mã nguồn mở), chỉ tránh 1 tool giải mã DPAPI generic đọc nhầm dữ liệu này.
    /// </summary>
    private static readonly byte[] _entropy =
    [
        0x8F, 0x2A, 0x51, 0xC3, 0x7D, 0x14, 0x9B, 0xE6,
        0x03, 0xAA, 0x5C, 0x7E, 0x21, 0xF4, 0x6D, 0x90,
        0x38, 0xB7, 0x1F, 0x62, 0xD5, 0x84, 0x0A, 0xE9,
        0x4B, 0x17, 0xC8, 0x53, 0x9E, 0x2D, 0x76, 0xFA,
    ];

    public static byte[] Protect(byte[] plaintext) =>
        ProtectedData.Protect(plaintext, _entropy, DataProtectionScope.LocalMachine);

    public static byte[] Unprotect(byte[] ciphertext) =>
        ProtectedData.Unprotect(ciphertext, _entropy, DataProtectionScope.LocalMachine);
}
