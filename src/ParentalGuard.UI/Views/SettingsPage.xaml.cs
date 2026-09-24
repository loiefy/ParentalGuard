using Microsoft.UI.Xaml.Controls;
using ParentalGuard.UI.Services;

namespace ParentalGuard.UI.Views;

/// <summary>`S4` Cài đặt — placeholder, implement đầy đủ ở giai đoạn 4 (Architecture/10 mục 6.4).</summary>
public sealed partial class SettingsPage : Page
{
    public SettingsPage()
    {
        InitializeComponent();
        PlaceholderText.Text = LocalizationService.Get("PlaceholderInDevelopment");
    }
}
