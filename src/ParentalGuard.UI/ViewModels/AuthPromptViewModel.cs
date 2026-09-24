using CommunityToolkit.Mvvm.ComponentModel;
using ParentalGuard.Ipc.Client;
using ParentalGuard.UI.Services;
using ParentalGuard.UI.Services.IpcClient;

namespace ParentalGuard.UI.ViewModels;

/// <summary>`S5` Auth Modal (Architecture/10-ui-architecture.md mục 6.5, `08` mục 7.2) — dùng bởi <see cref="Views.AuthPromptDialog"/>.</summary>
public sealed partial class AuthPromptViewModel(IAuthFacade authFacade, string actionContext) : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? ErrorMessage { get; set; }

    [ObservableProperty]
    public partial bool IsSubmitEnabled { get; set; } = true;

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    /// <summary>x:Bind hỗ trợ chuyển <c>bool</c> → <c>Visibility</c> trực tiếp, không cần converter riêng.</summary>
    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    public string PurposeText => actionContext switch
    {
        "pause_monitoring" => LocalizationService.Get("AuthPromptPurposePauseMonitoring"),
        "view_audit_log" => LocalizationService.Get("AuthPromptPurposeViewAuditLog"),
        "manage_whitelist" => LocalizationService.Get("AuthPromptPurposeManageWhitelist"),
        _ => string.Empty,
    };

    /// <summary>Set khi <c>AuthResult.Success</c> — caller (dialog) đọc giá trị này sau khi <see cref="SubmitAsync"/> trả <c>true</c>.</summary>
    public byte[]? ActionToken { get; private set; }

    /// <summary>
    /// <paramref name="passwordUtf8Pinned"/> — buffer pinned caller (code-behind dialog) vừa đọc từ
    /// <c>PasswordBox</c>; <see cref="IAuthFacade.AuthVerifyAsync"/> tự zero buffer này (mục 6.5,
    /// Architecture/08 mục 5.3). Trả <c>true</c> nếu <c>SUCCESS</c> (dialog đóng lại, trả <see cref="ActionToken"/> cho caller gốc).
    /// </summary>
    public async Task<bool> SubmitAsync(byte[] passwordUtf8Pinned, CancellationToken cancellationToken)
    {
        ErrorMessage = null;
        IsBusy = true;
        try
        {
            AuthVerifyResult result = await authFacade.AuthVerifyAsync(passwordUtf8Pinned, actionContext, cancellationToken).ConfigureAwait(false);
            switch (result.Outcome)
            {
                case AuthOutcome.Success:
                    ActionToken = result.ActionToken;
                    return true;
                case AuthOutcome.WrongPassword:
                    ErrorMessage = LocalizationService.Get("AuthPromptWrongPassword")
                        + (result.ConsecutiveFailures > 0 ? $" ({result.ConsecutiveFailures})" : string.Empty);
                    return false;
                case AuthOutcome.LockedOut:
                    IsSubmitEnabled = false;
                    TimeSpan remaining = DateTimeOffset.FromUnixTimeMilliseconds(result.LockoutUntilUnixMs) - DateTimeOffset.UtcNow;
                    string countdown = remaining > TimeSpan.Zero ? remaining.ToString(@"mm\:ss") : "00:00";
                    ErrorMessage = LocalizationService.GetFormatted("AuthPromptLockedOutFormat", countdown);
                    return false;
                default:
                    return false;
            }
        }
        catch (UiIpcConnectionException ex)
        {
            ErrorMessage = ex.Message;
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }
}
