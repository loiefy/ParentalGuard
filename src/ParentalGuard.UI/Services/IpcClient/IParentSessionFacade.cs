namespace ParentalGuard.UI.Services.IpcClient;

/// <summary>
/// Facade <c>ParentSessionRequest</c> (Architecture/03 mục 3.7b, Architecture/10 mục 6.8, `PWD-024`) — phiên
/// đăng nhập phụ huynh dùng chung `S3`/`S4`, gắn với kết nối pipe hiện tại của <c>UiIpcClient</c>.
/// </summary>
public interface IParentSessionFacade
{
    /// <summary>OPEN — đổi <paramref name="actionToken"/> ("parent_session") lấy phiên; facade zero token sau khi gửi.</summary>
    Task<ParentSessionOutcome> OpenAsync(byte[] actionToken, CancellationToken cancellationToken);

    /// <summary>KEEPALIVE — gia hạn idle 10 phút phía Service.</summary>
    Task<ParentSessionOutcome> KeepAliveAsync(CancellationToken cancellationToken);

    /// <summary>CLOSE — Đăng xuất (idempotent).</summary>
    Task CloseAsync(CancellationToken cancellationToken);
}

public enum ParentSessionOutcome
{
    Success,
    InvalidToken,
    NotActive,
}
