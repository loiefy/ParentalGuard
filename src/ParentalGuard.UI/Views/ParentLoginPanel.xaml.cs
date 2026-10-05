using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using ParentalGuard.UI.Services;

namespace ParentalGuard.UI.Views;

/// <summary>
/// Khung đăng nhập phụ huynh (`FE-080`/`FE-081`, `PWD-024`, Architecture/10 mục 6.8) — đặt ở đầu tab Lịch sử và
/// Cài đặt. Tự đổi giữa "khung đăng nhập" và "đã đăng nhập + Đăng xuất" theo <see cref="ParentSessionService.SessionChanged"/>.
/// </summary>
public sealed partial class ParentLoginPanel : UserControl
{
    private readonly ParentSessionService _session;
    private readonly NavigationService _navigationService;

    public ParentLoginPanel()
    {
        InitializeComponent();

        IServiceProvider services = ((App)Application.Current).Services;
        _session = services.GetRequiredService<ParentSessionService>();
        _navigationService = services.GetRequiredService<NavigationService>();

        TitleText.Text = LocalizationService.Get("ParentLoginTitle");
        HintText.Text = LocalizationService.Get("ParentLoginHint");
        PasswordInput.PlaceholderText = LocalizationService.Get("AuthPromptPasswordLabel");
        LoginButton.Content = LocalizationService.Get("ParentLoginButton");
        ForgotPasswordLink.Content = LocalizationService.Get("AuthPromptForgotPasswordLink");
        LoggedInText.Text = LocalizationService.Get("ParentLoggedInText");
        LogoutButton.Content = LocalizationService.Get("ParentLogoutButton");

        Loaded += (_, _) =>
        {
            _session.SessionChanged += OnSessionChanged;
            ApplyState();
        };
        Unloaded += (_, _) => _session.SessionChanged -= OnSessionChanged;
    }

    private void OnSessionChanged(object? sender, EventArgs e) => ApplyState();

    private void ApplyState()
    {
        bool loggedIn = _session.IsLoggedIn;
        LoginCard.Visibility = loggedIn ? Visibility.Collapsed : Visibility.Visible;
        LoggedInBar.Visibility = loggedIn ? Visibility.Visible : Visibility.Collapsed;
        if (loggedIn)
        {
            ShowError(null);
        }
    }

    private async void OnPasswordKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter)
        {
            e.Handled = true;
            await LoginAsync();
        }
    }

    private async void OnLoginClick(object sender, RoutedEventArgs e) => await LoginAsync();

    /// <summary>Architecture/08 mục 5.3 — đọc <c>PasswordBox.Password</c> → <c>byte[]</c> pinned NGAY, xoá ô nhập trước mọi xử lý.</summary>
    private async Task LoginAsync()
    {
        string password = PasswordInput.Password;
        PasswordInput.Password = string.Empty;
        if (string.IsNullOrEmpty(password))
        {
            ShowError(LocalizationService.Get("OnboardingPasswordEmpty"));
            return;
        }

        byte[] passwordUtf8Pinned = GC.AllocateArray<byte>(Encoding.UTF8.GetByteCount(password), pinned: true);
        Encoding.UTF8.GetBytes(password, passwordUtf8Pinned);

        LoginButton.IsEnabled = false;
        try
        {
            ParentLoginResult result = await _session.LoginAsync(passwordUtf8Pinned, CancellationToken.None);
            switch (result.Outcome)
            {
                case ParentLoginOutcome.Success:
                    ShowError(null);
                    break;
                case ParentLoginOutcome.WrongPassword:
                    ShowError(LocalizationService.Get("AuthPromptWrongPassword")
                        + (result.ConsecutiveFailures > 0 ? $" ({result.ConsecutiveFailures})" : string.Empty));
                    break;
                case ParentLoginOutcome.LockedOut:
                    TimeSpan remaining = DateTimeOffset.FromUnixTimeMilliseconds(result.LockoutUntilUnixMs) - DateTimeOffset.UtcNow;
                    string countdown = remaining > TimeSpan.Zero ? remaining.ToString(@"mm\:ss") : "00:00";
                    ShowError(LocalizationService.GetFormatted("AuthPromptLockedOutFormat", countdown));
                    break;
                default:
                    ShowError(result.ErrorMessage ?? LocalizationService.Get("ParentLoginFailed"));
                    break;
            }
        }
        finally
        {
            LoginButton.IsEnabled = true;
        }
    }

    private async void OnLogoutClick(object sender, RoutedEventArgs e) => await _session.LogoutAsync();

    private void OnForgotPasswordClick(object sender, RoutedEventArgs e) => _navigationService.NavigateToRecovery();

    private void ShowError(string? message)
    {
        ErrorText.Text = message ?? string.Empty;
        ErrorText.Visibility = string.IsNullOrEmpty(message) ? Visibility.Collapsed : Visibility.Visible;
    }
}
