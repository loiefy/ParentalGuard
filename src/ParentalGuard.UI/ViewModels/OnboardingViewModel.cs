using System.Security.Cryptography;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ParentalGuard.Ipc.Client;
using ParentalGuard.UI.Services;
using ParentalGuard.UI.Services.IpcClient;

namespace ParentalGuard.UI.ViewModels;

/// <summary>
/// `S1` Onboarding (Architecture/10-ui-architecture.md mục 6.1, `PWD-001`-`004`/`030`/`030a`) — 1
/// ViewModel dùng chung cho cả 3 bước (Giới thiệu/Đặt mật khẩu/Recovery Key), truyền qua
/// <c>Frame.Navigate</c> giữa 3 <c>Page</c> con của <see cref="Views.OnboardingPage"/>.
/// </summary>
public sealed partial class OnboardingViewModel(IAuthFacade authFacade) : ObservableObject, IDisposable
{
    [ObservableProperty]
    public partial bool IntroAcknowledged { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPasswordError))]
    public partial string? PasswordErrorMessage { get; set; }

    [ObservableProperty]
    public partial string? RecoveryKeyDisplay { get; set; }

    [ObservableProperty]
    public partial bool RecoveryKeySavedConfirmed { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRecoveryError))]
    public partial string? RecoveryErrorMessage { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotBusy))]
    public partial bool IsBusy { get; set; }

    /// <summary>x:Bind hỗ trợ chuyển <c>bool</c> → <c>Visibility</c> trực tiếp, không cần converter riêng.</summary>
    public bool HasPasswordError => !string.IsNullOrEmpty(PasswordErrorMessage);

    public bool HasRecoveryError => !string.IsNullOrEmpty(RecoveryErrorMessage);

    public bool IsNotBusy => !IsBusy;

    private byte[]? _setupToken;
    private byte[]? _recoveryKeyPlaintextBuffer;

    /// <summary>Hoàn tất Onboarding (`ConfirmResult.Persisted`) — caller (View) điều hướng Main Shell.</summary>
    public event EventHandler? OnboardingCompleted;

    /// <summary><c>SetupResult.AlreadyConfigured</c> (mục 6.1 bước 2, race hiếm) — caller hiện thông báo rồi điều hướng Main Shell.</summary>
    public event EventHandler? AlreadyConfiguredDetected;

    /// <summary>Yêu cầu View điều hướng sang <c>OnboardingSetPasswordPage</c> (bước 2).</summary>
    public event EventHandler? NavigateToSetPasswordRequested;

    /// <summary>Yêu cầu View điều hướng sang <c>OnboardingRecoveryKeyPage</c> (bước 3).</summary>
    public event EventHandler? NavigateToRecoveryKeyRequested;

    /// <summary>Token expired/not found (mục 6.1 bước 4) — caller quay lại <c>OnboardingSetPasswordPage</c>.</summary>
    public event EventHandler? NavigateBackToSetPasswordRequested;

    [RelayCommand]
    private void AcknowledgeIntro()
    {
        if (!IntroAcknowledged)
        {
            return;
        }

        NavigateToSetPasswordRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Mục 6.1 bước 2. <paramref name="passwordUtf8Pinned"/> — buffer pinned caller (code-behind) vừa
    /// đọc từ 2 <c>PasswordBox</c> đã khớp nhau; <see cref="IAuthFacade.SetInitialPasswordAsync"/> tự
    /// zero buffer này (Architecture/08 mục 5.3).
    /// </summary>
    public async Task SubmitPasswordAsync(byte[] passwordUtf8Pinned, CancellationToken cancellationToken)
    {
        PasswordErrorMessage = null;
        IsBusy = true;
        try
        {
            SetInitialPasswordResult result = await authFacade.SetInitialPasswordAsync(passwordUtf8Pinned, cancellationToken).ConfigureAwait(false);
            switch (result.Outcome)
            {
                case SetupOutcome.Success:
                    _setupToken = result.SetupToken;
                    _recoveryKeyPlaintextBuffer = result.RecoveryKeyPlaintextUtf8;
                    // Mục 6.1 bước 3 — UTF-8 decode ĐÚNG 1 LẦN từ bytes nhận được để hiển thị.
                    RecoveryKeyDisplay = _recoveryKeyPlaintextBuffer is not null ? Encoding.UTF8.GetString(_recoveryKeyPlaintextBuffer) : null;
                    RecoveryKeySavedConfirmed = false;
                    NavigateToRecoveryKeyRequested?.Invoke(this, EventArgs.Empty);
                    break;
                case SetupOutcome.PasswordTooLong:
                    PasswordErrorMessage = LocalizationService.Get("OnboardingPasswordTooLong");
                    break;
                case SetupOutcome.AlreadyConfigured:
                    AlreadyConfiguredDetected?.Invoke(this, EventArgs.Empty);
                    break;
            }
        }
        catch (UiIpcConnectionException ex)
        {
            PasswordErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Mục 6.1 bước 4 — chỉ gọi khi <see cref="RecoveryKeySavedConfirmed"/> đã tick.</summary>
    public async Task ConfirmRecoveryKeySavedAsync(CancellationToken cancellationToken)
    {
        if (!RecoveryKeySavedConfirmed || _setupToken is null)
        {
            return;
        }

        RecoveryErrorMessage = null;
        IsBusy = true;
        try
        {
            ConfirmRecoveryKeySavedResult result = await authFacade.ConfirmRecoveryKeySavedAsync(_setupToken, confirmed: true, cancellationToken).ConfigureAwait(false);
            switch (result.Outcome)
            {
                case ConfirmOutcome.Persisted:
                    ZeroRecoveryKeyBuffer();
                    OnboardingCompleted?.Invoke(this, EventArgs.Empty);
                    break;
                case ConfirmOutcome.TokenExpired:
                case ConfirmOutcome.TokenNotFound:
                    ZeroRecoveryKeyBuffer();
                    _setupToken = null;
                    RecoveryKeySavedConfirmed = false;
                    PasswordErrorMessage = LocalizationService.Get("OnboardingTokenExpired");
                    NavigateBackToSetPasswordRequested?.Invoke(this, EventArgs.Empty);
                    break;
            }
        }
        catch (UiIpcConnectionException ex)
        {
            RecoveryErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Mục 6.1 bước 3 — zero buffer plaintext ngay khi rời màn hình Recovery Key (residual risk `string` bất biến đã ghi nhận ở đó).</summary>
    public void ZeroRecoveryKeyBuffer()
    {
        if (_recoveryKeyPlaintextBuffer is not null)
        {
            CryptographicOperations.ZeroMemory(_recoveryKeyPlaintextBuffer);
            _recoveryKeyPlaintextBuffer = null;
        }

        RecoveryKeyDisplay = null;
    }

    /// <summary>
    /// Safety net (App.xaml.cs OnWindowClosed) — người dùng đóng app giữa chừng ở màn Recovery Key
    /// (trước khi bấm Confirm) thì <see cref="ZeroRecoveryKeyBuffer"/> chưa được gọi từ luồng bình
    /// thường; idempotent (no-op nếu buffer đã null).
    /// </summary>
    public void Dispose() => ZeroRecoveryKeyBuffer();
}
