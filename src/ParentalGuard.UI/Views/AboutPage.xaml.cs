using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ParentalGuard.UI.Services;

namespace ParentalGuard.UI.Views;

/// <summary>
/// FE-090 (Spec 03, ĐÃ CHỐT 2026-10-01): tab Giới thiệu — tên đơn vị, email, phiên bản (Donate tách tab riêng, FE-091a).
/// Giá trị lấy từ <c>UiStrings.resx</c> (*Value) để chủ dự án điền sau không cần sửa code; trống → "(đang cập nhật)".
/// Giá trị bắt đầu bằng http(s):// hiển thị thành liên kết mở trình duyệt mặc định; email thành liên kết mailto.
/// </summary>
public sealed partial class AboutPage : Page
{
    public AboutPage()
    {
        InitializeComponent();

        AboutHeaderText.Text = LocalizationService.Get("AboutHeader");
        AboutIntroText.Text = LocalizationService.Get("AboutIntro");
        CompanyLabel.Text = LocalizationService.Get("AboutCompanyNameLabel");
        EmailLabel.Text = LocalizationService.Get("AboutEmailLabel");
        VersionLabel.Text = LocalizationService.Get("AboutVersionLabel");

        CompanyValue.Content = BuildValue(LocalizationService.Get("AboutCompanyNameValue"), isEmail: false);
        EmailValue.Content = BuildValue(LocalizationService.Get("AboutEmailValue"), isEmail: true);
        VersionValue.Text = AppVersionInfo.DisplayVersion;

        // FE-092 (2026-10-05): cách ứng dụng hoạt động và dữ liệu được lưu.
        HowItWorksHeaderText.Text = LocalizationService.Get("AboutHowItWorksHeader");
        HowItWorksOperationText.Text = LocalizationService.Get("AboutHowItWorksOperation");
        HowItWorksPrivacyText.Text = LocalizationService.Get("AboutHowItWorksPrivacy");
        StoredDataIntroText.Text = LocalizationService.Get("AboutStoredDataIntro");
        foreach (string key in new[] { "AboutStoredDataViolations", "AboutStoredDataPause", "AboutStoredDataAuth", "AboutStoredDataSecurity", "AboutStoredDataConfig" })
        {
            StoredDataList.Children.Add(new TextBlock { Text = "•  " + LocalizationService.Get(key), TextWrapping = TextWrapping.Wrap, Opacity = 0.85 });
        }

        StoredDataNoteText.Text = LocalizationService.Get("AboutStoredDataNote");
    }

    /// <summary>Dùng chung với <see cref="DonatePage"/> (FE-091a).</summary>
    internal static UIElement BuildValue(string value, bool isEmail)
    {
        // LocalizationService trả về chính tên key khi giá trị rỗng/thiếu — coi như chưa điền.
        if (string.IsNullOrWhiteSpace(value) || value.EndsWith("Value", StringComparison.Ordinal))
        {
            return new TextBlock { Text = LocalizationService.Get("AboutNotYetAvailable"), Opacity = 0.6 };
        }

        string? uri = isEmail ? $"mailto:{value}" : value.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || value.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ? value : null;
        if (uri is not null && Uri.TryCreate(uri, UriKind.Absolute, out Uri? parsed))
        {
            return new HyperlinkButton { Content = value, NavigateUri = parsed, Padding = new Thickness(0) };
        }

        return new TextBlock { Text = value, IsTextSelectionEnabled = true, TextWrapping = TextWrapping.Wrap };
    }
}
