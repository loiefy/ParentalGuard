using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;
using ParentalGuard.Ipc.Client;
using ParentalGuard.UI.Services;
using ParentalGuard.UI.Services.IpcClient;

namespace ParentalGuard.UI.ViewModels;

/// <summary>
/// `S4` Cài đặt nâng cao (Architecture/10-ui-architecture.md mục 6.4) — vào tab KHÔNG gate
/// (<see cref="InitializeAsync"/> load ngay). Đổi mật khẩu tự gate qua <c>old_password</c> (không qua
/// `S5`, `08` mục 7.4); xoá whitelist entry qua `S5` (<c>manage_whitelist</c>, cùng mẫu hình
/// <see cref="AuditLogViewModel"/>); đổi thông điệp overlay/chế độ hiệu năng không gate, "full update"
/// (mục 6.4 — mỗi lần Save LUÔN gửi đầy đủ cả 2 field <c>overlay_message</c>/<c>performance_mode</c>
/// hiện hành đã LƯU, không phải giá trị đang gõ dở ở field kia).
/// </summary>
public sealed partial class SettingsViewModel(
    IConfigFacade configFacade,
    IAuthFacade authFacade,
    IAuthPromptService authPromptService) : ObservableObject, IDisposable
{
    private const string ManageWhitelistActionContext = "manage_whitelist";

    private string _lastSavedOverlayMessage = string.Empty;
    private byte[]? _newRecoveryKeyPlaintextBuffer;

    /// <summary>Giá trị <c>performance_mode</c> đã LƯU ở Service (không phải lựa chọn UI đang chờ xác nhận — đổi có hiệu lực ngay, không có trạng thái "chờ").</summary>
    public PerformanceModeOption PerformanceMode { get; private set; } = PerformanceModeOption.Balanced;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotLoading))]
    public partial bool IsLoading { get; set; } = true;

    // x:Bind (WinUI 3) không hỗ trợ toán tử `!` trong markup — expose thẳng phủ định (cùng mẫu hình DashboardViewModel.IsNotBusy).
    public bool IsNotLoading => !IsLoading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLoadError))]
    public partial string? LoadErrorMessage { get; set; }

    public bool HasLoadError => !string.IsNullOrEmpty(LoadErrorMessage);

    // --- Thông điệp overlay (FE-012/FE-012a) ---
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OverlayMessageRemainingChars))]
    public partial string OverlayMessage { get; set; } = string.Empty;

    public int OverlayMessageRemainingChars => OverlayMessageValidation.MaxLength - OverlayMessage.Length;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasOverlayMessageError))]
    public partial string? OverlayMessageError { get; set; }

    public bool HasOverlayMessageError => !string.IsNullOrEmpty(OverlayMessageError);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasOverlayMessageStatus))]
    public partial string? OverlayMessageStatus { get; set; }

    public bool HasOverlayMessageStatus => !string.IsNullOrEmpty(OverlayMessageStatus);

    // --- Quản lý whitelist (MISC-030) ---
    [ObservableProperty]
    public partial ObservableCollection<string> WhitelistedProcessNames { get; set; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasWhitelistError))]
    public partial string? WhitelistError { get; set; }

    public bool HasWhitelistError => !string.IsNullOrEmpty(WhitelistError);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasWhitelistStatus))]
    public partial string? WhitelistStatus { get; set; }

    public bool HasWhitelistStatus => !string.IsNullOrEmpty(WhitelistStatus);

    // --- Chế độ hiệu năng (PERF-050b) ---
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPerformanceModeError))]
    public partial string? PerformanceModeError { get; set; }

    public bool HasPerformanceModeError => !string.IsNullOrEmpty(PerformanceModeError);

    // --- Đổi mật khẩu (PWD-040/041) ---
    [ObservableProperty]
    public partial bool RegenerateRecoveryKey { get; set; } = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotChangingPassword))]
    [NotifyPropertyChangedFor(nameof(CanSubmitChangePassword))]
    public partial bool IsChangingPassword { get; set; }

    public bool IsNotChangingPassword => !IsChangingPassword;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasChangePasswordError))]
    public partial string? ChangePasswordError { get; set; }

    public bool HasChangePasswordError => !string.IsNullOrEmpty(ChangePasswordError);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasChangePasswordStatus))]
    public partial string? ChangePasswordStatus { get; set; }

    public bool HasChangePasswordStatus => !string.IsNullOrEmpty(ChangePasswordStatus);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNewRecoveryKeyDisplay))]
    [NotifyPropertyChangedFor(nameof(CanSubmitChangePassword))]
    public partial string? NewRecoveryKeyDisplay { get; set; }

    public bool HasNewRecoveryKeyDisplay => !string.IsNullOrEmpty(NewRecoveryKeyDisplay);

    /// <summary>Defense in depth (security audit Đợt 6 S4) — khoá nút "Đổi mật khẩu" khi Recovery Key mới chưa được acknowledge, chặn đường tái hiện qua UI thật khỏi ghi đè buffer plaintext chưa zero.</summary>
    public bool CanSubmitChangePassword => IsNotChangingPassword && !HasNewRecoveryKeyDisplay;

    /// <summary>Mục 6.4 — vào tab không gate, load ngay <c>overlay_message</c>/whitelist/<c>performance_mode</c>.</summary>
    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        IsLoading = true;
        LoadErrorMessage = null;
        try
        {
            ConfigSnapshot snapshot = await configFacade.GetConfigAsync(cancellationToken).ConfigureAwait(true);
            OverlayMessage = snapshot.OverlayMessage;
            _lastSavedOverlayMessage = snapshot.OverlayMessage;
            PerformanceMode = snapshot.PerformanceMode;
            WhitelistedProcessNames = new ObservableCollection<string>(snapshot.WhitelistedProcessNames);
        }
        catch (UiIpcConnectionException ex)
        {
            LoadErrorMessage = ex.Message;
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>Nút Lưu (mục 6.4) — defense in depth: kiểm tra lại `FE-012a` dù `TextBox` đã chặn nhập ở UI.</summary>
    public async Task SaveOverlayMessageAsync(CancellationToken cancellationToken)
    {
        OverlayMessageError = null;
        OverlayMessageStatus = null;
        if (!OverlayMessageValidation.IsValid(OverlayMessage))
        {
            OverlayMessage = _lastSavedOverlayMessage;
            OverlayMessageError = LocalizationService.Get("SettingsOverlayMessageInvalidCharacters");
            return;
        }

        try
        {
            ConfigUpdateOutcome outcome = await configFacade.UpdateOverlayMessageAsync(OverlayMessage, PerformanceMode, cancellationToken).ConfigureAwait(true);
            switch (outcome)
            {
                case ConfigUpdateOutcome.Success:
                    _lastSavedOverlayMessage = OverlayMessage;
                    OverlayMessageStatus = LocalizationService.Get("SettingsOverlayMessageSaved");
                    break;
                case ConfigUpdateOutcome.InvalidCharacters:
                    OverlayMessage = _lastSavedOverlayMessage;
                    OverlayMessageError = LocalizationService.Get("SettingsOverlayMessageInvalidCharacters");
                    break;
                case ConfigUpdateOutcome.TooLong:
                    OverlayMessage = _lastSavedOverlayMessage;
                    OverlayMessageError = LocalizationService.Get("SettingsOverlayMessageTooLong");
                    break;
            }
        }
        catch (UiIpcConnectionException ex)
        {
            OverlayMessageError = ex.Message;
        }
    }

    /// <summary>Nút "Khôi phục mặc định" (mục 6.4) — set rỗng cục bộ rồi gửi luôn, không cần xác nhận thêm.</summary>
    public Task ResetOverlayMessageToDefaultAsync(CancellationToken cancellationToken)
    {
        OverlayMessage = string.Empty;
        return SaveOverlayMessageAsync(cancellationToken);
    }

    /// <summary>`RadioButtons` đổi lựa chọn (mục 6.4/ADR-125) — có hiệu lực ngay, không gate.</summary>
    public async Task SetPerformanceModeAsync(PerformanceModeOption mode, CancellationToken cancellationToken)
    {
        PerformanceModeError = null;
        if (PerformanceMode == mode)
        {
            return;
        }

        PerformanceModeOption previous = PerformanceMode;
        PerformanceMode = mode;
        try
        {
            // "full update" — gửi kèm overlay_message ĐÃ LƯU (không phải bản đang gõ dở trong TextBox).
            ConfigUpdateOutcome outcome = await configFacade.UpdatePerformanceModeAsync(_lastSavedOverlayMessage, mode, cancellationToken).ConfigureAwait(true);
            if (outcome != ConfigUpdateOutcome.Success)
            {
                PerformanceMode = previous;
                PerformanceModeError = LocalizationService.Get("SettingsPerformanceModeSaveFailed");
            }
        }
        catch (UiIpcConnectionException ex)
        {
            PerformanceMode = previous;
            PerformanceModeError = ex.Message;
        }
    }

    /// <summary>Nút Xoá 1 dòng whitelist (mục 6.4, `MISC-030`) → `S5` (`manage_whitelist`).</summary>
    public async Task RemoveWhitelistEntryAsync(string processName, XamlRoot xamlRoot)
    {
        WhitelistError = null;
        WhitelistStatus = null;
        try
        {
            byte[]? actionToken = await authPromptService.ShowAuthPromptAsync(ManageWhitelistActionContext, xamlRoot).ConfigureAwait(true);
            if (actionToken is null)
            {
                return;
            }

            await RemoveWhitelistEntryWithTokenAsync(actionToken, processName, xamlRoot, allowRetry: true).ConfigureAwait(true);
        }
        catch (UiIpcConnectionException ex)
        {
            WhitelistError = ex.Message;
        }
    }

    /// <summary>Mục 6.5 — cùng cơ chế tự mở lại `S5` đúng 1 lần khi `action_token` hết hạn giữa chừng như <see cref="AuditLogViewModel"/>.</summary>
    private async Task RemoveWhitelistEntryWithTokenAsync(byte[] actionToken, string processName, XamlRoot xamlRoot, bool allowRetry)
    {
        try
        {
            RemoveWhitelistOutcome outcome = await configFacade.RemoveWhitelistEntryAsync(actionToken, processName, CancellationToken.None).ConfigureAwait(true);
            switch (outcome)
            {
                case RemoveWhitelistOutcome.Success:
                case RemoveWhitelistOutcome.NotFound: // idempotent guard — đã không còn trong danh sách, coi như đã xoá
                    WhitelistedProcessNames.Remove(processName);
                    WhitelistStatus = LocalizationService.GetFormatted("SettingsWhitelistRemovedFormat", processName);
                    break;
                case RemoveWhitelistOutcome.InvalidToken:
                    WhitelistError = LocalizationService.Get("DashboardActionTokenExpired");
                    if (!allowRetry)
                    {
                        break;
                    }

                    byte[]? retryToken = await authPromptService.ShowAuthPromptAsync(ManageWhitelistActionContext, xamlRoot).ConfigureAwait(true);
                    if (retryToken is null)
                    {
                        break;
                    }

                    WhitelistError = null;
                    await RemoveWhitelistEntryWithTokenAsync(retryToken, processName, xamlRoot, allowRetry: false).ConfigureAwait(true);
                    break;
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(actionToken);
        }
    }

    /// <summary>
    /// Mục 6.4 — tự gate qua <paramref name="oldPasswordUtf8Pinned"/> (không qua `S5`, `08` mục 7.4).
    /// Cả 2 buffer pinned do caller (code-behind) cấp phát; <see cref="IAuthFacade.ChangePasswordAsync"/>
    /// tự zero chúng (Architecture/08 mục 5.3).
    /// </summary>
    public async Task ChangePasswordAsync(byte[] oldPasswordUtf8Pinned, byte[] newPasswordUtf8Pinned, CancellationToken cancellationToken)
    {
        ChangePasswordError = null;
        ChangePasswordStatus = null;
        IsChangingPassword = true;
        try
        {
            ChangePasswordResult result = await authFacade.ChangePasswordAsync(oldPasswordUtf8Pinned, newPasswordUtf8Pinned, RegenerateRecoveryKey, cancellationToken).ConfigureAwait(true);
            switch (result.Outcome)
            {
                case ChangeOutcome.Success:
                    if (result.NewRecoveryKeyPlaintextUtf8 is not null)
                    {
                        // Security audit Đợt 6 S4 — nếu Recovery Key trước đó chưa được acknowledge, zero
                        // ngay trước khi ghi đè, không để plaintext cũ trôi nổi không zero trên managed heap.
                        if (_newRecoveryKeyPlaintextBuffer is not null)
                        {
                            CryptographicOperations.ZeroMemory(_newRecoveryKeyPlaintextBuffer);
                        }

                        _newRecoveryKeyPlaintextBuffer = result.NewRecoveryKeyPlaintextUtf8;
                        // Mục 6.1 bước 3 — UTF-8 decode ĐÚNG 1 LẦN từ bytes nhận được để hiển thị.
                        NewRecoveryKeyDisplay = Encoding.UTF8.GetString(_newRecoveryKeyPlaintextBuffer);
                    }

                    ChangePasswordStatus = LocalizationService.Get("SettingsChangePasswordSuccess");
                    break;
                case ChangeOutcome.WrongOldPassword:
                    ChangePasswordError = LocalizationService.Get("SettingsChangePasswordWrongOldPassword");
                    break;
                case ChangeOutcome.LockedOut:
                    ChangePasswordError = LocalizationService.Get("SettingsChangePasswordLockedOut");
                    break;
                case ChangeOutcome.NewPasswordTooLong:
                    ChangePasswordError = LocalizationService.Get("OnboardingPasswordTooLong");
                    break;
            }
        }
        catch (UiIpcConnectionException ex)
        {
            ChangePasswordError = ex.Message;
        }
        finally
        {
            IsChangingPassword = false;
        }
    }

    /// <summary>Bấm "Đã lưu" trên Recovery Key mới hiển thị — zero buffer plaintext ngay (cùng mẫu hình `S1` bước 3, residual risk `string` bất biến đã ghi nhận ở đó).</summary>
    public void AcknowledgeNewRecoveryKeyDisplayed()
    {
        if (_newRecoveryKeyPlaintextBuffer is not null)
        {
            CryptographicOperations.ZeroMemory(_newRecoveryKeyPlaintextBuffer);
            _newRecoveryKeyPlaintextBuffer = null;
        }

        NewRecoveryKeyDisplay = null;
    }

    /// <summary>Safety net (BUG B, mục "học từ giai đoạn 1") — rời tab/đóng app trước khi bấm "Đã lưu" vẫn phải zero buffer; idempotent.</summary>
    public void Dispose() => AcknowledgeNewRecoveryKeyDisplayed();
}

/// <summary>
/// `FE-012a` — chặn ký tự đặc biệt/emoji trong thông điệp overlay tự cấu hình. Dùng CẢ ở
/// <c>SettingsPage</c> (chặn ngay khi nhập, <c>TextBox.BeforeTextChanging</c>) VÀ ở
/// <see cref="SettingsViewModel.SaveOverlayMessageAsync"/> (defense in depth trước khi gửi request).
/// </summary>
public static class OverlayMessageValidation
{
    public const int MaxLength = 255; // FE-012

    // Cho phép: dấu câu tiêu chuẩn dùng trong câu văn tiếng Việt (FE-012a).
    private const string AllowedPunctuation = ".,!?:;-()\"'";

    /// <summary>Chữ cái (Latin + tiếng Việt có dấu)/chữ số/khoảng trắng thường/dấu câu cho phép — mọi ký tự khác (emoji, control char, symbol) đều bị từ chối.</summary>
    public static bool IsAllowedChar(char c) => char.IsLetter(c) || char.IsDigit(c) || c == ' ' || AllowedPunctuation.Contains(c);

    public static bool IsValid(string text) => text.Length <= MaxLength && text.All(IsAllowedChar);
}
