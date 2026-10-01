using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
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
            UpdateBackdropLayout();
        };

        // FE-005: 2 lớp nền phía sau NavigationView phải bám đúng bề rộng thanh menu (mở 240 / thu gọn 48).
        Nav.PaneOpening += (_, _) => UpdateBackdropLayout(paneOpen: true);
        Nav.PaneClosing += (_, _) => UpdateBackdropLayout(paneOpen: false);
        Nav.DisplayModeChanged += (_, _) => UpdateBackdropLayout();
        SizeChanged += (_, _) => UpdateBackdropLayout();
        ActualThemeChanged += (_, _) => DrawPaneCircles();

        // S3 huỷ gate S5 tự điều hướng lại S2 (mục 6.3) bằng Frame.Navigate trực tiếp (không qua
        // OnSelectionChanged) — đồng bộ lại NavigationViewItem đang chọn cho khớp trang thật sự hiển thị.
        ContentFrame.Navigated += (_, e) => Nav.SelectedItem = e.SourcePageType == typeof(AuditLogPage)
            ? AuditLogItem
            : e.SourcePageType == typeof(SettingsPage) ? SettingsItem : DashboardItem;
    }

    private double _paneWidth = -1;
    private double _paneHeight = -1;

    private void UpdateBackdropLayout(bool? paneOpen = null)
    {
        bool open = paneOpen ?? Nav.IsPaneOpen;
        double width = open ? Nav.OpenPaneLength : Nav.CompactPaneLength;
        PaneBackdrop.Width = width;
        ContentBackdrop.Margin = new Thickness(width, 0, 0, 0);
        if (Math.Abs(width - _paneWidth) > 0.5 || Math.Abs(ActualHeight - _paneHeight) > 0.5)
        {
            _paneWidth = width;
            _paneHeight = ActualHeight;
            DrawPaneCircles();
        }
    }

    /// <summary>FE-005: hình tròn mờ "ngẫu nhiên" phía sau menu — seed cố định nên mỗi lần mở app bố cục giống nhau.</summary>
    private void DrawPaneCircles()
    {
        PaneCircles.Children.Clear();
        double width = PaneBackdrop.Width;
        double height = ActualHeight;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        Windows.UI.Color color = ActualTheme == ElementTheme.Light
            ? ColorHelper.FromArgb(255, 0xE2, 0xE2, 0xE9)
            : ColorHelper.FromArgb(255, 0x3C, 0x3C, 0x45);
        int seq = 0;
        double Next() => DecorativeNoise(seq++);
        int count = Math.Max(8, (int)(height / 70));
        for (int i = 0; i < count; i++)
        {
            double diameter = 24 + (Next() * 110);
            var circle = new Ellipse
            {
                Width = diameter,
                Height = diameter,
                Fill = new SolidColorBrush(color),
                Opacity = 0.35 + (Next() * 0.5),
            };
            // Cho phép tràn 1 phần ra ngoài mép (bị cắt) — trông tự nhiên hơn khi pane thu gọn chỉ còn 48px.
            Canvas.SetLeft(circle, (Next() * (Math.Max(width, 120) + diameter)) - diameter / 2);
            Canvas.SetTop(circle, (Next() * (height + diameter)) - diameter / 2);
            PaneCircles.Children.Add(circle);
        }

        PaneBackdrop.Clip = new RectangleGeometry { Rect = new Windows.Foundation.Rect(0, 0, width, height) };
    }

    /// <summary>Dãy giả ngẫu nhiên tất định chỉ dùng trang trí (không dùng <see cref="Random"/> — CA5394), giá trị [0, 1).</summary>
    private static double DecorativeNoise(int i)
    {
        double x = Math.Sin((i + 1) * 12.9898) * 43758.5453;
        return x - Math.Floor(x);
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
