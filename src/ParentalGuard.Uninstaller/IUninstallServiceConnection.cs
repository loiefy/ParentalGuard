using ParentalGuard.Ipc.Protocol;

namespace ParentalGuard.Uninstaller;

/// <summary>
/// Seam test-only (giống pattern <c>IFrameCapture</c> ở Vision, IMG-040/041) — tách giao tiếp pipe
/// thật khỏi <see cref="UninstallFlow"/> để unit test được bất biến an toàn cốt lõi (ADR-98) không
/// cần Service/pipe thật.
/// </summary>
public interface IUninstallServiceConnection : IAsyncDisposable
{
    /// <summary>Connect + handshake, timeout 10s (Architecture/09 mục 5.3) — ném exception nếu thất bại.</summary>
    Task ConnectAsync(CancellationToken cancellationToken);

    Task<AuthVerifyResponse> VerifyPasswordAsync(byte[] password, CancellationToken cancellationToken);

    Task<UninstallExecuteResponse> ExecuteUninstallAsync(byte[] actionToken, bool keepAuditLog, CancellationToken cancellationToken);
}
