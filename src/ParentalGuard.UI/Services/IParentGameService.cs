using ParentalGuard.UI.Services.IpcClient;

namespace ParentalGuard.UI.Services;

/// <summary>
/// `PAUSE-044`–`PAUSE-047` (2026-10-08) — trọn 1 lượt trò chơi nhảy rào: xin Service bắt đầu ván → mở cửa sổ trò chơi → báo kết quả.
/// Tách thành seam để ViewModel test được bằng fake (không cần cửa sổ thật).
/// </summary>
public interface IParentGameService
{
    /// <summary>Mật khẩu đã nhập xong (<paramref name="actionToken"/>) → chơi → về đích thì Service tự tạm dừng.</summary>
    Task<ParentGameFinish> PauseWithGameAsync(byte[] actionToken, PauseDurationOption duration);

    /// <summary>Tắt chế độ / giảm quãng đường → chơi ở độ khó hiện hành → về đích thì Service tự áp dụng.</summary>
    Task<ParentGameFinish> ChangeSettingsWithGameAsync(bool enabled, uint gameMeters);
}
