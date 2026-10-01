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

/// <summary>`S4` Cài đặt nâng cao (Architecture/10-ui-architecture.md mục 6.4).</summary>
public sealed partial class SettingsPage : Page
{
    private readonly NavigationService _navigationService;
    private bool _performanceModeInitialized;

    public SettingsPage()
    {
        InitializeComponent();

        IServiceProvider services = ((App)Application.Current).Services;
        _navigationService = services.GetRequiredService<NavigationService>();
        ViewModel = new SettingsViewModel(
            services.GetRequiredService<IConfigFacade>(),
            services.GetRequiredService<IAuthFacade>(),
            _navigationService);
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;

        OverlayMessageLabel.Text = LocalizationService.Get("SettingsOverlayMessageLabel");
        SaveOverlayMessageButton.Content = LocalizationService.Get("SettingsSaveButton");
        ResetOverlayMessageButton.Content = LocalizationService.Get("SettingsResetToDefaultButton");
        WhitelistHeaderText.Text = LocalizationService.Get("SettingsWhitelistHeader");
        PerformanceModeHeaderText.Text = LocalizationService.Get("SettingsPerformanceModeHeader");
        BalancedRadio.Content = LocalizationService.Get("SettingsPerformanceModeBalanced");
        MaximumProtectionRadio.Content = LocalizationService.Get("SettingsPerformanceModeMaximumProtection");
        ChangePasswordHeaderText.Text = LocalizationService.Get("SettingsChangePasswordHeader");
        OldPasswordLabel.Text = LocalizationService.Get("SettingsOldPasswordLabel");
        NewPasswordLabel.Text = LocalizationService.Get("SettingsNewPasswordLabel");
        ConfirmNewPasswordLabel.Text = LocalizationService.Get("SettingsConfirmNewPasswordLabel");
        RegenerateRecoveryKeyCheckBox.Content = LocalizationService.Get("SettingsRegenerateRecoveryKeyCheckbox");
        ChangePasswordButton.Content = LocalizationService.Get("SettingsChangePasswordButton");
        ForgotOldPasswordLink.Content = LocalizationService.Get("SettingsForgotOldPasswordLink");
        NewRecoveryKeyCopyButton.Content = LocalizationService.Get("OnboardingRecoveryKeyCopyButton");
        NewRecoveryKeyAcknowledgeButton.Content = LocalizationService.Get("SettingsRecoveryKeyAcknowledgeButton");
    }

    public SettingsViewModel ViewModel { get; }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ((App)Application.Current).RegisterActiveSettingsViewModel(ViewModel);

        _performanceModeInitialized = false;
        await ViewModel.InitializeAsync(CancellationToken.None);
        PerformanceModeRadios.SelectedIndex = ViewModel.PerformanceMode == PerformanceModeOption.MaximumProtection ? 1 : 0;
        _performanceModeInitialized = true;
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        ((App)Application.Current).UnregisterActiveSettingsViewModel(ViewModel);
        // Security audit Đợt 6 S6 (bug đã sửa) — đánh dấu mồ côi TRƯỚC, phòng trường hợp ChangePasswordAsync
        // còn treo IPC ("Quên mật khẩu cũ?" → S6 giữa chừng): Success đến sau sẽ tự zero, không publish.
        ViewModel.MarkDiscarded();
        // Safety net (BUG B, học từ giai đoạn 1) — rời tab trong khi Recovery Key mới còn hiển thị vẫn phải zero buffer.
        ViewModel.AcknowledgeNewRecoveryKeyDisplayed();
    }

    private async void OnPerformanceModeSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_performanceModeInitialized)
        {
            return;
        }

        PerformanceModeOption mode = PerformanceModeRadios.SelectedIndex == 1 ? PerformanceModeOption.MaximumProtection : PerformanceModeOption.Balanced;
        await ViewModel.SetPerformanceModeAsync(mode, CancellationToken.None);
    }

    /// <summary>ViewModel đổi giá trị chủ động (load/lưu/khôi phục/hoàn tác) → đẩy xuống ô nhập; lúc đang gõ 2 giá trị bằng nhau nên không ghi đè caret.</summary>
    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SettingsViewModel.OverlayMessage) && OverlayMessageInput.Text != ViewModel.OverlayMessage)
        {
            OverlayMessageInput.Text = ViewModel.OverlayMessage;
        }
    }

    /// <summary>
    /// `FE-012a` — lọc ký tự cấm ngay khi gõ/dán, nhưng SAU khi text đổi: không can thiệp chuỗi
    /// Backspace+ký tự thay thế của bộ gõ tiếng Việt (bản cũ huỷ <c>BeforeTextChanging</c> làm hỏng bộ gõ).
    /// </summary>
    private void OnOverlayMessageTextChanged(object sender, TextChangedEventArgs e)
    {
        string text = OverlayMessageInput.Text;
        string filtered = OverlayMessageValidation.RemoveForbiddenChars(text);
        if (!string.Equals(filtered, text, StringComparison.Ordinal))
        {
            int caret = Math.Clamp(OverlayMessageInput.SelectionStart - (text.Length - filtered.Length), 0, filtered.Length);
            OverlayMessageInput.Text = filtered; // tự kích hoạt lại TextChanged với chuỗi đã sạch
            OverlayMessageInput.SelectionStart = caret;
            return;
        }

        ViewModel.OverlayMessage = text;
    }

    private async void OnSaveOverlayMessageClick(object sender, RoutedEventArgs e) => await ViewModel.SaveOverlayMessageAsync(CancellationToken.None);

    private async void OnResetOverlayMessageClick(object sender, RoutedEventArgs e) => await ViewModel.ResetOverlayMessageToDefaultAsync(CancellationToken.None);

    private async void OnRemoveWhitelistEntryClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string processName })
        {
            await ViewModel.RemoveWhitelistEntryAsync(processName, XamlRoot);
        }
    }

    /// <summary>Mục 7/ADR-124 — nút Xoá nằm trong `DataTemplate` (lặp lại theo dòng), set text lúc mỗi instance tải xong (cùng mẫu hình `AuditLogPage`).</summary>
    private void OnRemoveWhitelistButtonLoaded(object sender, RoutedEventArgs e) => ((Button)sender).Content = LocalizationService.Get("SettingsWhitelistRemoveButton");

    /// <summary>Architecture/08 mục 5.3 — đọc `PasswordBox.Password` → `byte[]` pinned NGAY, clear cả 3 `PasswordBox` TRƯỚC mọi guard/validate.</summary>
    private async void OnChangePasswordClick(object sender, RoutedEventArgs e)
    {
        string oldPassword = OldPasswordInput.Password;
        string newPassword = NewPasswordInput.Password;
        string confirmPassword = ConfirmNewPasswordInput.Password;
        OldPasswordInput.Password = string.Empty;
        NewPasswordInput.Password = string.Empty;
        ConfirmNewPasswordInput.Password = string.Empty;

        if (string.IsNullOrEmpty(oldPassword) || string.IsNullOrEmpty(newPassword))
        {
            ViewModel.ChangePasswordError = LocalizationService.Get("OnboardingPasswordEmpty");
            return;
        }

        if (!string.Equals(newPassword, confirmPassword, StringComparison.Ordinal))
        {
            ViewModel.ChangePasswordError = LocalizationService.Get("OnboardingPasswordMismatch");
            return;
        }

        byte[] oldPasswordUtf8Pinned = GC.AllocateArray<byte>(Encoding.UTF8.GetByteCount(oldPassword), pinned: true);
        Encoding.UTF8.GetBytes(oldPassword, oldPasswordUtf8Pinned);
        byte[] newPasswordUtf8Pinned = GC.AllocateArray<byte>(Encoding.UTF8.GetByteCount(newPassword), pinned: true);
        Encoding.UTF8.GetBytes(newPassword, newPasswordUtf8Pinned);

        await ViewModel.ChangePasswordAsync(oldPasswordUtf8Pinned, newPasswordUtf8Pinned, CancellationToken.None);
    }

    /// <summary>Mục 6.4 — điều hướng thẳng `S6` (không qua `S5`, chưa implement ở giai đoạn này — xem <see cref="NavigationService.NavigateToRecovery"/>).</summary>
    private void OnForgotOldPasswordClick(object sender, RoutedEventArgs e) => _navigationService.NavigateToRecovery();

    private void OnAcknowledgeNewRecoveryKeyClick(object sender, RoutedEventArgs e) => ViewModel.AcknowledgeNewRecoveryKeyDisplayed();

    /// <summary>Clipboard chuẩn Windows — cùng residual risk/lý do đã ghi nhận ở <see cref="OnboardingRecoveryKeyPage"/>.</summary>
    private void OnCopyNewRecoveryKeyClick(object sender, RoutedEventArgs e)
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
