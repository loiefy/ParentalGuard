using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using ParentalGuard.UI.Services;
using ParentalGuard.UI.Services.IpcClient;
using ParentalGuard.UI.ViewModels;
using Windows.ApplicationModel.DataTransfer;

namespace ParentalGuard.UI.Views;

/// <summary>`S6` Recovery (Architecture/10-ui-architecture.md mục 6.6, `PWD-032`/`033`).</summary>
public sealed partial class RecoveryPage : Page
{
    private readonly NavigationService _navigationService;

    public RecoveryPage()
    {
        InitializeComponent();

        IServiceProvider services = ((App)Application.Current).Services;
        _navigationService = services.GetRequiredService<NavigationService>();
        ViewModel = new RecoveryViewModel(services.GetRequiredService<IAuthFacade>());

        TitleText.Text = LocalizationService.Get("RecoveryTitle");
        BodyText.Text = LocalizationService.Get("RecoveryBody");
        RecoveryKeyLabel.Text = LocalizationService.Get("RecoveryKeyLabel");
        NewPasswordLabel.Text = LocalizationService.Get("SettingsNewPasswordLabel");
        ConfirmNewPasswordLabel.Text = LocalizationService.Get("SettingsConfirmNewPasswordLabel");
        SubmitButton.Content = LocalizationService.Get("RecoverySubmitButton");
        CancelLink.Content = LocalizationService.Get("RecoveryCancelLink");
        NewRecoveryKeyBody.Text = LocalizationService.Get("OnboardingRecoveryKeyBody");
        CopyButton.Content = LocalizationService.Get("OnboardingRecoveryKeyCopyButton");
        AcknowledgeButton.Content = LocalizationService.Get("SettingsRecoveryKeyAcknowledgeButton");
    }

    public RecoveryViewModel ViewModel { get; }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ((App)Application.Current).RegisterActiveRecoveryViewModel(ViewModel);
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        ((App)Application.Current).UnregisterActiveRecoveryViewModel(ViewModel);
        // Security audit Đợt 6 S6 (bug đã sửa) — đánh dấu mồ côi TRƯỚC, phòng trường hợp SubmitAsync còn
        // treo IPC (Cancel trong lúc Submit): Success đến sau thời điểm này sẽ tự zero, không publish.
        ViewModel.MarkDiscarded();
        // Safety net (BUG B, học từ giai đoạn 1/4) — rời trang trong khi Recovery Key mới còn hiển thị vẫn phải zero buffer.
        ViewModel.AcknowledgeNewRecoveryKeyDisplayed();
    }

    /// <summary>
    /// Architecture/08 mục 5.3/mục 6.2 — đọc Recovery Key/2 `PasswordBox` → biến cục bộ, CLEAR CẢ 3
    /// input NGAY LẬP TỨC (trước mọi guard/validate), rồi mới chuẩn hoá Recovery Key + build buffer pinned.
    /// </summary>
    private async void OnSubmitClick(object sender, RoutedEventArgs e)
    {
        string recoveryKeyRaw = RecoveryKeyInput.Text;
        string newPassword = NewPasswordInput.Password;
        string confirmPassword = ConfirmNewPasswordInput.Password;
        RecoveryKeyInput.Text = string.Empty;
        NewPasswordInput.Password = string.Empty;
        ConfirmNewPasswordInput.Password = string.Empty;

        if (string.IsNullOrWhiteSpace(recoveryKeyRaw))
        {
            ViewModel.ErrorMessage = LocalizationService.Get("RecoveryKeyEmpty");
            return;
        }

        if (string.IsNullOrEmpty(newPassword))
        {
            ViewModel.ErrorMessage = LocalizationService.Get("OnboardingPasswordEmpty");
            return;
        }

        if (!string.Equals(newPassword, confirmPassword, StringComparison.Ordinal))
        {
            ViewModel.ErrorMessage = LocalizationService.Get("OnboardingPasswordMismatch");
            return;
        }

        string recoveryKeyNormalized = RecoveryViewModel.NormalizeRecoveryKey(recoveryKeyRaw);
        byte[] recoveryKeyUtf8Pinned = GC.AllocateArray<byte>(Encoding.UTF8.GetByteCount(recoveryKeyNormalized), pinned: true);
        Encoding.UTF8.GetBytes(recoveryKeyNormalized, recoveryKeyUtf8Pinned);
        byte[] newPasswordUtf8Pinned = GC.AllocateArray<byte>(Encoding.UTF8.GetByteCount(newPassword), pinned: true);
        Encoding.UTF8.GetBytes(newPassword, newPasswordUtf8Pinned);

        await ViewModel.SubmitAsync(recoveryKeyUtf8Pinned, newPasswordUtf8Pinned, CancellationToken.None);
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => _navigationService.NavigateToMainShell();

    private void OnAcknowledgeClick(object sender, RoutedEventArgs e)
    {
        ViewModel.AcknowledgeNewRecoveryKeyDisplayed();
        _navigationService.NavigateToMainShell();
    }

    /// <summary>Clipboard chuẩn Windows — cùng residual risk/lý do đã ghi nhận ở <see cref="OnboardingRecoveryKeyPage"/>.</summary>
    private void OnCopyClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(ViewModel.NewRecoveryKeyDisplay))
        {
            return;
        }

        var dataPackage = new DataPackage();
        dataPackage.SetText(ViewModel.NewRecoveryKeyDisplay);
        Clipboard.SetContent(dataPackage);
    }
}
