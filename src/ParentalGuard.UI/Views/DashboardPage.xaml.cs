using Microsoft.UI.Xaml.Controls;
using ParentalGuard.UI.Services;

namespace ParentalGuard.UI.Views;

/// <summary>`S2` Dashboard — placeholder, implement đầy đủ ở giai đoạn 2 (Architecture/10 mục 6.2).</summary>
public sealed partial class DashboardPage : Page
{
    public DashboardPage()
    {
        InitializeComponent();
        PlaceholderText.Text = LocalizationService.Get("PlaceholderInDevelopment");
    }
}
