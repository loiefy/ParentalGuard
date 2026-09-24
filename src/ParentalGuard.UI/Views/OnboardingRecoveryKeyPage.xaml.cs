using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using ParentalGuard.UI.Services;
using ParentalGuard.UI.ViewModels;
using Windows.ApplicationModel.DataTransfer;

namespace ParentalGuard.UI.Views;

/// <summary>`S1` bước 3 — Hiển thị Recovery Key (`PWD-030`/`030a`, Architecture/10 mục 6.1).</summary>
public sealed partial class OnboardingRecoveryKeyPage : Page
{
    public OnboardingRecoveryKeyPage()
    {
        InitializeComponent();
        TitleText.Text = LocalizationService.Get("OnboardingRecoveryKeyTitle");
        BodyText.Text = LocalizationService.Get("OnboardingRecoveryKeyBody");
        CopyButton.Content = LocalizationService.Get("OnboardingRecoveryKeyCopyButton");
        ConfirmCheckBox.Content = LocalizationService.Get("OnboardingRecoveryKeyConfirmCheckbox");
        ContinueButton.Content = LocalizationService.Get("ContinueButton");
    }

    public OnboardingViewModel? ViewModel { get; private set; }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ViewModel = (OnboardingViewModel)e.Parameter;
        Bindings.Update();
    }

    /// <summary>
    /// Clipboard chuẩn Windows — residual risk clipboard history ghi nhận minh bạch (Architecture/10
    /// mục 6.1 bước 3), không thiết kế cơ chế tự xoá clipboard timer riêng (không có căn cứ yêu cầu
    /// này trong `Specification/`).
    /// </summary>
    private void OnCopyClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(ViewModel?.RecoveryKeyDisplay))
        {
            return;
        }

        var dataPackage = new DataPackage();
        dataPackage.SetText(ViewModel.RecoveryKeyDisplay);
        Clipboard.SetContent(dataPackage);
    }

    private async void OnContinueClick(object sender, RoutedEventArgs e)
    {
        await ViewModel!.ConfirmRecoveryKeySavedAsync(CancellationToken.None);
    }
}
