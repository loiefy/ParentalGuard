using Microsoft.UI.Xaml.Controls;
using ParentalGuard.UI.Services;

namespace ParentalGuard.UI.Views;

/// <summary>Main Shell (Architecture/10-ui-architecture.md mục 4/6.2-6.4) — `NavigationView` 3 mục, giai đoạn này chỉ trỏ tới placeholder.</summary>
public sealed partial class MainShellPage : Page
{
    public MainShellPage()
    {
        InitializeComponent();
        DashboardItem.Content = LocalizationService.Get("NavDashboard");
        AuditLogItem.Content = LocalizationService.Get("NavAuditLog");
        SettingsItem.Content = LocalizationService.Get("NavSettings");

        Loaded += (_, _) =>
        {
            Nav.SelectedItem = DashboardItem;
            ContentFrame.Navigate(typeof(DashboardPage));
        };

        // S3 huỷ gate S5 tự điều hướng lại S2 (mục 6.3) bằng Frame.Navigate trực tiếp (không qua
        // OnSelectionChanged) — đồng bộ lại NavigationViewItem đang chọn cho khớp trang thật sự hiển thị.
        ContentFrame.Navigated += (_, e) => Nav.SelectedItem = e.SourcePageType == typeof(AuditLogPage)
            ? AuditLogItem
            : e.SourcePageType == typeof(SettingsPage) ? SettingsItem : DashboardItem;
    }

    private void OnSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItemContainer is not NavigationViewItem item)
        {
            return;
        }

        Type pageType = (string)item.Tag switch
        {
            "AuditLog" => typeof(AuditLogPage),
            "Settings" => typeof(SettingsPage),
            _ => typeof(DashboardPage),
        };
        if (ContentFrame.CurrentSourcePageType != pageType)
        {
            ContentFrame.Navigate(pageType);
        }
    }
}
