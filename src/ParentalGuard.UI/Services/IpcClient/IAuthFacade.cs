namespace ParentalGuard.UI.Services.IpcClient;

/// <summary>
/// Facade domain Password/Auth (Architecture/10-ui-architecture.md mục 2.2/5, `PWD-0xx`) — nhận/trả
/// POCO đơn giản, ViewModel không thấy <c>IpcPayload</c>. Dùng cho <c>S1</c> Onboarding và <c>S5</c>
/// Auth Modal.
/// </summary>
public interface IAuthFacade
{
    /// <summary><c>AuthStatusQuery</c> — <c>true</c> nếu đã hoàn tất Onboarding (mục 4).</summary>
    Task<bool> GetAuthStatusAsync(CancellationToken cancellationToken);

    /// <summary>
    /// <c>SetInitialPasswordRequest</c> (mục 6.1 bước 2). <paramref name="passwordUtf8Pinned"/> PHẢI
    /// là buffer <c>pinned</c> do caller cấp phát (Architecture/08 mục 5.1/5.3) — hàm này zero buffer
    /// đó trong <c>finally</c> bất kể thành công/thất bại, caller KHÔNG được dùng lại sau khi gọi.
    /// </summary>
    Task<SetInitialPasswordResult> SetInitialPasswordAsync(byte[] passwordUtf8Pinned, CancellationToken cancellationToken);

    /// <summary><c>ConfirmRecoveryKeySavedRequest</c> (mục 6.1 bước 4).</summary>
    Task<ConfirmRecoveryKeySavedResult> ConfirmRecoveryKeySavedAsync(byte[] setupToken, bool confirmed, CancellationToken cancellationToken);

    /// <summary>
    /// <c>AuthVerifyRequest</c> (mục 6.5, `S5`). <paramref name="passwordUtf8Pinned"/> — cùng quy tắc
    /// zero như <see cref="SetInitialPasswordAsync"/>.
    /// </summary>
    Task<AuthVerifyResult> AuthVerifyAsync(byte[] passwordUtf8Pinned, string actionContext, CancellationToken cancellationToken);

    /// <summary>
    /// <c>ChangePasswordRequest</c> (mục 6.4, `PWD-040`/`041`, Architecture/08 mục 7.4) — tự gate qua
    /// <paramref name="oldPasswordUtf8Pinned"/>, KHÔNG qua `S5`. Cả 2 buffer pinned bị zero trong
    /// <c>finally</c> (Architecture/08 mục 5.3), caller KHÔNG được dùng lại sau khi gọi.
    /// </summary>
    Task<ChangePasswordResult> ChangePasswordAsync(
        byte[] oldPasswordUtf8Pinned,
        byte[] newPasswordUtf8Pinned,
        bool regenerateRecoveryKey,
        CancellationToken cancellationToken);
}

public enum SetupOutcome
{
    Success,
    PasswordTooLong,
    AlreadyConfigured,
}

/// <summary>
/// <paramref name="RecoveryKeyPlaintextUtf8"/> chỉ set khi <see cref="SetupOutcome.Success"/> — buffer
/// lấy trực tiếp từ <c>ByteString</c> nội bộ Protobuf (Architecture/08 mục 5.2), CALLER sở hữu và
/// chịu trách nhiệm zero (<c>CryptographicOperations.ZeroMemory</c>) ngay khi rời màn hình hiển thị
/// (mục 6.1 bước 3, residual risk `string` bất biến sau khi decode UTF-8 đã ghi nhận ở đó).
/// </summary>
public sealed record SetInitialPasswordResult(SetupOutcome Outcome, byte[]? RecoveryKeyPlaintextUtf8, byte[]? SetupToken);

public enum ConfirmOutcome
{
    Persisted,
    TokenExpired,
    TokenNotFound,
}

public sealed record ConfirmRecoveryKeySavedResult(ConfirmOutcome Outcome);

public enum AuthOutcome
{
    Success,
    WrongPassword,
    LockedOut,
}

public sealed record AuthVerifyResult(
    AuthOutcome Outcome,
    byte[]? ActionToken,
    long ActionTokenExpiresAtUnixMs,
    long LockoutUntilUnixMs,
    uint ConsecutiveFailures);

public enum ChangeOutcome
{
    Success,
    WrongOldPassword,
    LockedOut,
    NewPasswordTooLong,
}

/// <summary>
/// <paramref name="NewRecoveryKeyPlaintextUtf8"/> chỉ set khi <see cref="ChangeOutcome.Success"/> VÀ
/// <c>regenerate_recovery_key=true</c> — cùng quy tắc sở hữu/zero như
/// <see cref="SetInitialPasswordResult.RecoveryKeyPlaintextUtf8"/> (Architecture/08 mục 5.2/ADR-83).
/// </summary>
public sealed record ChangePasswordResult(ChangeOutcome Outcome, byte[]? NewRecoveryKeyPlaintextUtf8);
