using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ParentalGuard.UI.Services;
using ParentalGuard.UI.Services.IpcClient;
using ParentalGuard.UI.ViewModels;

namespace ParentalGuard.UI.Views;

/// <summary>
/// `S1` Onboarding (Architecture/10-ui-architecture.md mục 6.1) — host <see cref="StepFrame"/> điều
/// hướng qua 3 bước con, dùng chung 1 <see cref="OnboardingViewModel"/> truyền qua tham số navigate.
/// </summary>
public sealed partial class OnboardingPage : Page
{
    private OnboardingViewModel? _viewModel;

    public OnboardingPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_viewModel is not null)
        {
            return;
        }

        IServiceProvider services = ((App)Application.Current).Services;
        _viewModel = new OnboardingViewModel(services.GetRequiredService<IAuthFacade>());
        // BUG B fix — đăng ký làm safety net cho App.OnWindowClosed (zero Recovery Key buffer nếu
        // user đóng app giữa chừng, trước khi Confirm).
        ((App)Application.Current).RegisterActiveOnboardingViewModel(_viewModel);
        _viewModel.NavigateToSetPasswordRequested += (_, _) => StepFrame.Navigate(typeof(OnboardingSetPasswordPage), _viewModel);
        _viewModel.NavigateBackToSetPasswordRequested += (_, _) => StepFrame.Navigate(typeof(OnboardingSetPasswordPage), _viewModel);
        _viewModel.NavigateToRecoveryKeyRequested += (_, _) => StepFrame.Navigate(typeof(OnboardingRecoveryKeyPage), _viewModel);
        _viewModel.AlreadyConfiguredDetected += OnAlreadyConfiguredAsync;
        _viewModel.OnboardingCompleted += (_, _) => GoToMainShell();

        StepFrame.Navigate(typeof(OnboardingWelcomePage), _viewModel);
    }

    /// <summary>Mục 6.1 bước 2 — race hiếm: auth.dat đã tồn tại khi Onboarding vẫn đang chạy.</summary>
    private async void OnAlreadyConfiguredAsync(object? sender, EventArgs e)
    {
        var dialog = new ContentDialog
        {
            Title = LocalizationService.Get("OnboardingAlreadyConfigured"),
            CloseButtonText = "OK",
            XamlRoot = XamlRoot,
        };
        await dialog.ShowAsync();
        GoToMainShell();
    }

    private void GoToMainShell()
    {
        if (_viewModel is not null)
        {
            ((App)Application.Current).UnregisterActiveOnboardingViewModel(_viewModel);
        }

        ((App)Application.Current).Services.GetRequiredService<NavigationService>().NavigateToMainShell();
    }
}
