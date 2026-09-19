using System.Security.Cryptography;

namespace ParentalGuard.Service.Auth;

/// <summary>
/// <c>AuthState</c> RAM-only (Architecture/02-process-architecture.md mục 5, Architecture/08 mục
/// 7.1/7.2) — KHÔNG có bảng trong <c>config.db</c> (`PWD-013`). Đồng bộ hoá đọc-sửa-ghi do
/// <see cref="AuthCoordinator"/> đảm nhiệm qua 1 semaphore chung (mục 7.7 gạch cuối) — lớp này chỉ
/// là container dữ liệu thuần, KHÔNG tự khoá.
/// </summary>
public sealed class AuthState
{
    public PendingSetup? PendingSetup { get; set; }

    /// <summary>Khoá = hex(action_token) — 16 byte ngẫu nhiên nên tra cứu theo hash-dictionary là đủ an toàn (mục 7.2).</summary>
    public Dictionary<string, PendingActionToken> PendingActionTokens { get; } = [];

    /// <summary>
    /// Salt "mồi" cho <c>RunDecoyArgon2idAsync</c> (Architecture/08 mục 7.9, ADR-84) — sinh đúng 1
    /// lần lúc <c>AuthCoordinator</c> khởi tạo (Service <c>Starting</c>), tái dùng cho mọi lượt bù
    /// thời gian trong suốt vòng đời tiến trình. RAM-only, không persist, không liên quan <c>auth.dat</c>.
    /// </summary>
    public byte[] DecoySalt { get; } = RandomNumberGenerator.GetBytes(16);
}

/// <summary>
/// Chờ xác nhận Recovery Key đã lưu (mục 7.1) — tối đa 1 slot tại 1 thời điểm (đúng giới hạn 1 kết
/// nối UI đồng thời, Architecture/03 mục 6). Hết hạn sau 30 phút không xác nhận.
///
/// <para>
/// KHÔNG giữ Recovery Key plaintext (sửa v0.3.0, ADR-83/mục 5.5 điểm 4) — bước Confirm chỉ cần
/// <see cref="RecoveryKeyHashPhc"/> đã tính sẵn, chưa từng cần đọc lại plaintext; giữ plaintext sống
/// tới tận lúc Confirm (tối đa 30 phút) là cửa sổ tồn tại dư thừa không cần thiết.
/// </para>
/// </summary>
public sealed class PendingSetup
{
    public static readonly TimeSpan Ttl = TimeSpan.FromMinutes(30);

    public required string PasswordHashPhc { get; init; }

    public required string RecoveryKeyHashPhc { get; init; }

    public required byte[] SetupToken { get; init; }

    public required long CreatedAtUnixMs { get; init; }

    public bool IsExpired(long nowUnixMs) => nowUnixMs - CreatedAtUnixMs >= Ttl.TotalMilliseconds;

    public bool TokenMatches(ReadOnlySpan<byte> candidate) =>
        candidate.Length == SetupToken.Length && CryptographicOperations.FixedTimeEquals(candidate, SetupToken);
}

/// <summary>
/// Cầu nối ngắn hạn giữa "xác thực" (<c>AuthVerifyRequest</c>) và "thực thi hành động nhạy cảm"
/// (message riêng, thiết kế ở Đợt tương ứng) — TTL 15 giây, dùng 1 lần, RAM-only (mục 7.2, ADR-78).
/// KHÔNG phải cơ chế bảo mật chính — chỉ là cầu nối kỹ thuật.
/// </summary>
public sealed class PendingActionToken
{
    public static readonly TimeSpan Ttl = TimeSpan.FromSeconds(15);

    public required string ActionContext { get; init; }

    public required long ExpiresAtUnixMs { get; init; }
}
