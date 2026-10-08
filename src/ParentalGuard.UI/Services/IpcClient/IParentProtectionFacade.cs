namespace ParentalGuard.UI.Services.IpcClient;

/// <summary>
/// `PAUSE-040`, `PAUSE-044`–`PAUSE-048` "Bảo vệ cả phụ huynh" (trò chơi nhảy rào, 2026-10-08) + `FE-063a` đổi ngôn ngữ.
/// Service quyết định có cần chơi hay không, ghi nhận ván chơi và tự áp dụng kết quả — UI chỉ hiển thị trò chơi.
/// </summary>
public interface IParentProtectionFacade
{
    /// <summary>Bắt đầu ván để TẠM DỪNG — <paramref name="actionToken"/> ("pause_monitoring") bị Service tiêu thụ ngay.</summary>
    Task<ParentGameStart> StartPauseGameAsync(byte[] actionToken, PauseDurationOption duration, CancellationToken cancellationToken);

    /// <summary>Bắt đầu ván để TẮT chế độ / GIẢM quãng đường (cần phiên phụ huynh). <paramref name="gameMeters"/> 0 = giữ nguyên.</summary>
    Task<ParentGameStart> StartSettingsGameAsync(bool enabled, uint gameMeters, CancellationToken cancellationToken);

    /// <summary><paramref name="completed"/> = false khi vấp rào hoặc thoát trò chơi.</summary>
    Task<ParentGameFinish> FinishGameAsync(bool completed, uint metersReached, CancellationToken cancellationToken);

    /// <summary>Chỉ BẬT / TĂNG quãng đường được đổi thẳng; tắt/giảm → <see cref="SetParentProtectionOutcome.ChallengeRequired"/>.</summary>
    Task<SetParentProtectionOutcome> SetParentProtectionAsync(bool enabled, uint gameMeters, CancellationToken cancellationToken);

    /// <summary>`FE-063a`/`FE-083`: không cần đăng nhập. <c>false</c> nếu Service không nhận mã ngôn ngữ.</summary>
    Task<bool> SetLanguageAsync(string languageCode, CancellationToken cancellationToken);
}

public enum ParentGameOutcome
{
    Started,
    InvalidToken,
    NotAuthenticated,
    NotRequired,
    AlreadyPaused,
    Paused,
    Applied,
    Lost,
    TooFast,
    NoGame,
    Failed,
}

public sealed record ParentGameStart(ParentGameOutcome Outcome, uint TargetMeters, ulong Seed, uint MinDurationMs);

public sealed record ParentGameFinish(ParentGameOutcome Outcome, long PauseExpiresAtUnixMs);

public enum SetParentProtectionOutcome
{
    Success,
    NotAuthenticated,
    ChallengeRequired,
    Failed,
}
