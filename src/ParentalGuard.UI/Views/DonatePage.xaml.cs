using Microsoft.UI.Xaml.Controls;
using ParentalGuard.UI.Services;

namespace ParentalGuard.UI.Views;

/// <summary>FE-091/FE-091a (Spec 03, ĐÃ CHỐT 2026-10-06): tab Ủng hộ dự án (Donate) — chỉ hiển thị thông tin, app không xử lý thanh toán.</summary>
public sealed partial class DonatePage : Page
{
    public DonatePage()
    {
        InitializeComponent();
        DonateHeaderText.Text = LocalizationService.Get("AboutDonateHeader");
        DonateIntroText.Text = LocalizationService.Get("AboutDonateIntro");
        PayPalLabel.Text = LocalizationService.Get("AboutPayPalLabel");
        PayPalValue.Content = AboutPage.BuildValue(LocalizationService.Get("AboutPayPalValue"), isEmail: false);
    }
}
