using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using ParentalGuard.UI.Services;
using ParentalGuard.UI.ViewModels;

namespace ParentalGuard.UI.Views;

/// <summary>`S1` bước 1 — Giới thiệu (`FE-031`), thuần UI, không có IPC.</summary>
public sealed partial class OnboardingWelcomePage : Page
{
    public OnboardingWelcomePage()
    {
        InitializeComponent();
        TitleText.Text = LocalizationService.Get("OnboardingWelcomeTitle");
        BodyText.Text = LocalizationService.Get("OnboardingWelcomeBody");
        AcknowledgeCheckBox.Content = LocalizationService.Get("OnboardingWelcomeAcknowledge");
        ContinueButton.Content = LocalizationService.Get("ContinueButton");
    }

    public OnboardingViewModel? ViewModel { get; private set; }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ViewModel = (OnboardingViewModel)e.Parameter;
        Bindings.Update();
    }
}
