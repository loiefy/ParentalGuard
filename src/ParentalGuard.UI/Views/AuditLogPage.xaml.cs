using Microsoft.UI.Xaml.Controls;
using ParentalGuard.UI.Services;

namespace ParentalGuard.UI.Views;

/// <summary>`S3` Audit log — placeholder, implement đầy đủ ở giai đoạn 3 (Architecture/10 mục 6.3).</summary>
public sealed partial class AuditLogPage : Page
{
    public AuditLogPage()
    {
        InitializeComponent();
        PlaceholderText.Text = LocalizationService.Get("PlaceholderInDevelopment");
    }
}
