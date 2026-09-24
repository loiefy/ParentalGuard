using System.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using ParentalGuard.UI.Services;
using ParentalGuard.UI.ViewModels;

namespace ParentalGuard.UI.Views;

/// <summary>`S1` bước 2 — Đặt mật khẩu (`PWD-001`-`003`, Architecture/10 mục 6.1).</summary>
public sealed partial class OnboardingSetPasswordPage : Page
{
    public OnboardingSetPasswordPage()
    {
        InitializeComponent();
        TitleText.Text = LocalizationService.Get("OnboardingSetPasswordTitle");
        PasswordLabel.Text = LocalizationService.Get("OnboardingPasswordLabel");
        ConfirmPasswordLabel.Text = LocalizationService.Get("OnboardingConfirmPasswordLabel");
        ContinueButton.Content = LocalizationService.Get("ContinueButton");
    }

    public OnboardingViewModel? ViewModel { get; private set; }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ViewModel = (OnboardingViewModel)e.Parameter;
        Bindings.Update();
    }

    /// <summary>`PWD-002` — thuần UX polish, thuật toán tự chọn (Architecture/10 mục 6.1/11, không phải cơ chế bảo mật).</summary>
    private void OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        StrengthText.Text = ComputeStrengthLabel(PasswordInput.Password);
    }

    private static string ComputeStrengthLabel(string password)
    {
        if (password.Length == 0)
        {
            return string.Empty;
        }

        int score = 0;
        if (password.Length >= 8)
        {
            score++;
        }

        if (password.Length >= 12)
        {
            score++;
        }

        bool hasUpper = false, hasLower = false, hasDigit = false, hasOther = false;
        foreach (char c in password)
        {
            hasUpper |= char.IsUpper(c);
            hasLower |= char.IsLower(c);
            hasDigit |= char.IsDigit(c);
            hasOther |= !char.IsLetterOrDigit(c);
        }

        if (hasUpper && hasLower)
        {
            score++;
        }

        if (hasDigit)
        {
            score++;
        }

        if (hasOther)
        {
            score++;
        }

        return score switch
        {
            <= 1 => LocalizationService.Get("PasswordStrengthWeak"),
            <= 3 => LocalizationService.Get("PasswordStrengthMedium"),
            _ => LocalizationService.Get("PasswordStrengthStrong"),
        };
    }

    /// <summary>
    /// `PWD-002a`/Architecture/08 mục 5.3 — đọc PasswordBox.Password → biến cục bộ rồi CLEAR CẢ 2
    /// PasswordBox NGAY LẬP TỨC (trước mọi guard/validate), để không nhánh return nào rời hàm mà
    /// PasswordBox còn giữ plaintext người dùng vừa gõ.
    /// </summary>
    private async void OnContinueClick(object sender, RoutedEventArgs e)
    {
        string password = PasswordInput.Password;
        string confirm = ConfirmPasswordInput.Password;
        PasswordInput.Password = string.Empty;
        ConfirmPasswordInput.Password = string.Empty;

        if (string.IsNullOrEmpty(password))
        {
            ViewModel!.PasswordErrorMessage = LocalizationService.Get("OnboardingPasswordEmpty");
            return;
        }

        if (!string.Equals(password, confirm, StringComparison.Ordinal))
        {
            ViewModel!.PasswordErrorMessage = LocalizationService.Get("OnboardingPasswordMismatch");
            return;
        }

        byte[] passwordUtf8Pinned = GC.AllocateArray<byte>(Encoding.UTF8.GetByteCount(password), pinned: true);
        Encoding.UTF8.GetBytes(password, passwordUtf8Pinned);

        await ViewModel!.SubmitPasswordAsync(passwordUtf8Pinned, CancellationToken.None);
    }
}
