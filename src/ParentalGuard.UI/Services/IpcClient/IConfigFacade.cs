namespace ParentalGuard.UI.Services.IpcClient;

/// <summary>
/// Facade domain cấu hình (Architecture/10-ui-architecture.md mục 2.2/5/6.4, `FE-012`/`FE-012a`/
/// `MISC-030`/`PERF-050b`) — dùng ở `S4`. <c>ConfigQuery</c>/<c>ConfigUpdateRequest</c> không gate
/// (cosmetic/operational); <c>RemoveWhitelistEntryRequest</c> nhận <c>action_token</c> sẵn có từ `S5`
/// (`manage_whitelist`) — cùng mẫu hình <see cref="IAuditFacade"/>, facade chỉ forward, không tự tạo.
/// </summary>
public interface IConfigFacade
{
    /// <summary><c>ConfigQuery{}</c> (mục 6.4) — không gate, gọi ngay khi vào tab.</summary>
    Task<ConfigSnapshot> GetConfigAsync(CancellationToken cancellationToken);

    /// <summary>
    /// <c>ConfigUpdateRequest{overlay_message, performance_mode}</c> — mục 6.4: mọi lần Save LUÔN gửi
    /// đầy đủ cả 2 field hiện hành ("full update", không phải partial) dù mục đích gọi là đổi thông
    /// điệp overlay hay đổi chế độ hiệu năng — <paramref name="currentPerformanceMode"/> PHẢI là giá
    /// trị hiện hành (không phải mặc định), tránh vô tình ghi đè lựa chọn hiệu năng của người dùng.
    /// </summary>
    Task<ConfigUpdateOutcome> UpdateOverlayMessageAsync(string overlayMessage, PerformanceModeOption currentPerformanceMode, CancellationToken cancellationToken);

    /// <summary>Cùng request/nguyên tắc "full update" như <see cref="UpdateOverlayMessageAsync"/> — không gate (ADR-125).</summary>
    Task<ConfigUpdateOutcome> UpdatePerformanceModeAsync(string currentOverlayMessage, PerformanceModeOption performanceMode, CancellationToken cancellationToken);

    /// <summary><c>RemoveWhitelistEntryRequest{action_token, process_name}</c> (mục 6.4, `MISC-030`).</summary>
    Task<RemoveWhitelistOutcome> RemoveWhitelistEntryAsync(byte[] actionToken, string processName, CancellationToken cancellationToken);
}

public sealed record ConfigSnapshot(string OverlayMessage, IReadOnlyList<string> WhitelistedProcessNames, PerformanceModeOption PerformanceMode);

/// <summary>1-1 với <c>PerformanceMode</c> proto (trừ <c>UNSPECIFIED</c>, `PERF-050b` chỉ có đúng 2 mức).</summary>
public enum PerformanceModeOption
{
    Balanced,
    MaximumProtection,
}

public enum ConfigUpdateOutcome
{
    Success,
    InvalidCharacters,
    TooLong,
}

public enum RemoveWhitelistOutcome
{
    Success,
    InvalidToken,
    NotFound,
}
