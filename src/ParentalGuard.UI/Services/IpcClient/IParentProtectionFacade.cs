namespace ParentalGuard.UI.Services.IpcClient;

/// <summary>
/// `PAUSE-040`–`PAUSE-043` "Bảo vệ cả phụ huynh" + `FE-063a` đổi ngôn ngữ (2026-10-07). Câu hỏi do Service sinh và chấm —
/// UI chỉ hiển thị và gửi đáp án, không biết đáp án đúng.
/// </summary>
public interface IParentProtectionFacade
{
    Task<ChallengeStart> StartChallengeAsync(CancellationToken cancellationToken);

    Task<ChallengeSubmitResult> SubmitChallengeAsync(IReadOnlyList<int> answers, CancellationToken cancellationToken);

    /// <summary>Cần phiên phụ huynh; TẮT cần đã vượt thử thách trên kết nối này (`PAUSE-042`).</summary>
    Task<SetParentProtectionOutcome> SetParentProtectionAsync(bool enabled, CancellationToken cancellationToken);

    /// <summary>`FE-063a`/`FE-083`: không cần đăng nhập. <c>false</c> nếu Service không nhận mã ngôn ngữ.</summary>
    Task<bool> SetLanguageAsync(string languageCode, CancellationToken cancellationToken);
}

public enum ChallengeOutcome
{
    Started,
    Passed,
    Wrong,
    Expired,
    LockedOut,
    NoChallenge,
}

public sealed record ChallengeStart(ChallengeOutcome Outcome, IReadOnlyList<string> Questions, long ExpiresAtUnixMs, long LockedUntilUnixMs);

public sealed record ChallengeSubmitResult(ChallengeOutcome Outcome, long LockedUntilUnixMs, uint FailuresBeforeLockout);

public enum SetParentProtectionOutcome
{
    Success,
    NotAuthenticated,
    ChallengeRequired,
    Failed,
}
