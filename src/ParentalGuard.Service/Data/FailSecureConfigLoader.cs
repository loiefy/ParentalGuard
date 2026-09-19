using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using ParentalGuard.Service.Audit;
using ParentalGuard.Service.Configuration;

namespace ParentalGuard.Service.Data;

public sealed record ConfigLoadResult(
    MonitoringStateData MonitoringState,
    PauseStateData PauseState,
    byte[] IpcHmacKey,
    bool UsedFailSecureFallback,
    string? FallbackReason);

/// <summary>
/// Luồng nạp cấu hình fail-secure đầy đủ (BE-061/061a/061b, ANTI-070,
/// Architecture/04-data-architecture.md mục 6).
/// </summary>
public sealed class FailSecureConfigLoader(AuditLogWriter auditLog, ILogger<FailSecureConfigLoader> logger)
{
    public async Task<ConfigLoadResult> LoadAsync(CancellationToken cancellationToken)
    {
        bool configExisted = File.Exists(InstallPaths.ConfigDbPath);
        bool authDatExisted = File.Exists(InstallPaths.AuthDatPath);

        if (!configExisted && !authDatExisted)
        {
            // Lần đầu cài đặt — luồng Onboarding bình thường, KHÔNG fail-secure (mục 6.1 dòng 1).
            return CreateFirstRun();
        }

        try
        {
            using ConfigDb db = ConfigDb.Open(InstallPaths.ConfigDbPath);
            ConfigSnapshot snapshot = db.ReadSnapshot();
            return new ConfigLoadResult(snapshot.MonitoringState, snapshot.PauseState, snapshot.IpcHmacKey, UsedFailSecureFallback: false, FallbackReason: null);
        }
        catch (ConfigLoadException ex)
        {
            logger.LogWarning("config.db unreadable, entering fail-secure fallback: {Reason}", ex.Reason);
            return await RunFailSecureFlowAsync(ex.Reason, cancellationToken).ConfigureAwait(false);
        }
    }

    private ConfigLoadResult CreateFirstRun()
    {
        MonitoringStateData monitoringState = MonitoringStateData.CreateFirstRunDefault();
        PauseStateData pauseState = PauseStateData.CreateDefault();
        byte[] hmacKey = RandomNumberGenerator.GetBytes(32);
        ConfigDb.CreateFresh(InstallPaths.ConfigDbPath, monitoringState, pauseState, hmacKey, auditLog.Checkpoint, lastFallbackEventUnixMs: null);
        return new ConfigLoadResult(monitoringState, pauseState, hmacKey, UsedFailSecureFallback: false, FallbackReason: null);
    }

    private async Task<ConfigLoadResult> RunFailSecureFlowAsync(string reason, CancellationToken cancellationToken)
    {
        // Bước 1 (mục 6.2): ghi audit log TRƯỚC khi đụng tới config.db — độc lập, ghi được
        // ngay cả khi config.db hỏng hoàn toàn; idempotent nếu Service crash giữa chừng luồng này.
        await auditLog.AppendAsync("ConfigFallbackTriggered", new { reason }, cancellationToken).ConfigureAwait(false);

        // Bước 2-3: nạp default hard-code vào RAM.
        MonitoringStateData monitoringState = MonitoringStateData.CreateFailSecureDefault();
        PauseStateData pauseState = PauseStateData.CreateDefault();

        // Bước 4: sinh MỚI khoá HMAC (không thể khôi phục khoá cũ nếu config.db hỏng toàn bộ).
        byte[] hmacKey = RandomNumberGenerator.GetBytes(32);

        // Bước 5: tạo config.db MỚI HOÀN TOÀN.
        long fallbackEventUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        ConfigDb.CreateFresh(InstallPaths.ConfigDbPath, monitoringState, pauseState, hmacKey, auditLog.Checkpoint, fallbackEventUnixMs);

        // Bước 6 (đẩy ControlVisionCommand/OverlayRectListCommand phản ánh state mới cho
        // Vision/Overlay) và bước 7 (ShowToastCommand qua Overlay, BE-061b/ANTI-070b,
        // Architecture/03 mục 3.2) do Worker thực hiện sau khi kênh IPC đã sẵn sàng — ngoài phạm
        // vi loader này (loader chỉ trả UsedFailSecureFallback/FallbackReason qua ConfigLoadResult).

        // Bước 8: chuyển state machine sang Running·Monitoring — quyết định ở Worker.
        return new ConfigLoadResult(monitoringState, pauseState, hmacKey, UsedFailSecureFallback: true, FallbackReason: reason);
    }
}
