namespace ParentalGuard.Service.Auth;

/// <summary>
/// Phiên đăng nhập phụ huynh dùng chung `S3`/`S4` (`PWD-024`, Architecture/08 mục 7.10, Architecture/03
/// mục 3.7b ADR-149) — gắn với ĐÚNG 1 kết nối pipe <c>UI</c>: <c>UiSessionServer.RunConnectionAsync</c> tạo 1
/// instance mới mỗi kết nối, kết nối đóng (đóng Dashboard) là phiên mất theo. Cùng mẫu hình
/// <see cref="Audit.AuditLogViewSession"/> — không có state service-wide để rò sang kết nối khác.
/// Hết hạn sau <see cref="IdleTimeout"/> không có request hợp lệ/KEEPALIVE nào, đo bằng
/// <see cref="MonotonicClock"/> (không tin đồng hồ hệ thống, cùng lý do `PWD-022`).
/// </summary>
public sealed class UiParentSession(MonotonicClock clock)
{
    public static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(10);

    private long? _idleExpiresAtUnixMs;

    public long? IdleExpiresAtUnixMs => _idleExpiresAtUnixMs;

    public bool IsActive => _idleExpiresAtUnixMs is long expiresAt && clock.UtcNowUnixMs < expiresAt;

    /// <summary>Mở (hoặc mở lại) phiên — chỉ gọi SAU KHI đã tiêu thụ thành công 1 <c>action_token</c> "parent_session".</summary>
    public long Open()
    {
        long expiresAt = clock.UtcNowUnixMs + (long)IdleTimeout.TotalMilliseconds;
        _idleExpiresAtUnixMs = expiresAt;
        return expiresAt;
    }

    /// <summary>
    /// <c>true</c> + gia hạn idle nếu phiên còn hiệu lực; phiên đã hết hạn thì xoá hẳn (không tự hồi sinh)
    /// và trả <c>false</c>.
    /// </summary>
    public bool TryTouch()
    {
        if (!IsActive)
        {
            _idleExpiresAtUnixMs = null;
            return false;
        }

        _idleExpiresAtUnixMs = clock.UtcNowUnixMs + (long)IdleTimeout.TotalMilliseconds;
        return true;
    }

    public void Close() => _idleExpiresAtUnixMs = null;
}
