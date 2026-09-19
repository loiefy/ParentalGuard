namespace ParentalGuard.Service.Auth;

/// <summary>
/// Tham số Argon2id (Architecture/08-password-authentication-architecture.md mục 4.2, ADR-72 —
/// ĐÃ CHỐT bởi chủ dự án 2026-09-20). Dùng CHUNG cho cả mật khẩu lẫn Recovery Key (mục 6.3, `PWD-031`).
/// </summary>
public readonly record struct Argon2Params(int MemoryKb, int Iterations, int Parallelism)
{
    /// <summary>Giá trị chính thức (không còn "khởi điểm đề xuất") — `m=32768 KiB (32 MiB), t=2, p=2`.</summary>
    public static readonly Argon2Params Official = new(MemoryKb: 32768, Iterations: 2, Parallelism: 2);
}
