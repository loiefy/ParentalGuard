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
        AboutItem.Content = LocalizationService.Get("NavAbout");

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
        ActualThemeChanged += (_, _) =>
        {
            DrawPaneCircles();
            DrawFlowerPattern();
        };
        ContentBackdrop.SizeChanged += (_, _) => DrawFlowerPattern();

        // S3 huỷ gate S5 tự điều hướng lại S2 (mục 6.3) bằng Frame.Navigate trực tiếp (không qua
        // OnSelectionChanged) — đồng bộ lại NavigationViewItem đang chọn cho khớp trang thật sự hiển thị.
        ContentFrame.Navigated += (_, e) => Nav.SelectedItem = e.SourcePageType == typeof(AuditLogPage)
            ? AuditLogItem
            : e.SourcePageType == typeof(SettingsPage) ? SettingsItem
            : e.SourcePageType == typeof(AboutPage) ? AboutItem : DashboardItem;
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

    private const double _flowerCell = 190; // DIP — cỡ ô lưới cố định, độc lập kích thước cửa sổ (2026-10-01: dày hơn)

    /// <summary>
    /// FE-005: họa tiết chùm hoa tối giản, kích thước cố định. Mỗi ô lưới (neo góc trên-trái vùng nội dung) có hoặc
    /// không 1 chùm hoa, vị trí/xoay/cỡ suy từ chỉ số ô (tất định) — kéo giãn cửa sổ không làm hoa đổi cỡ/nhảy chỗ.
    /// </summary>
    private void DrawFlowerPattern()
    {
        FlowerCanvas.Children.Clear();
        double width = ContentBackdrop.ActualWidth;
        double height = ContentBackdrop.ActualHeight;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        bool light = ActualTheme == ElementTheme.Light;
        // 2026-10-01: màu hoa gần màu nền hơn (nền #26262B / #FAFAFC).
        var petal = new SolidColorBrush(light ? ColorHelper.FromArgb(255, 0xF2, 0xF2, 0xF5) : ColorHelper.FromArgb(255, 0x22, 0x22, 0x27));
        var center = new SolidColorBrush(light ? ColorHelper.FromArgb(255, 0xFA, 0xFA, 0xFC) : ColorHelper.FromArgb(255, 0x29, 0x29, 0x2F));

        int cols = (int)Math.Ceiling(width / _flowerCell);
        int rows = (int)Math.Ceiling(height / _flowerCell);
        for (int row = 0; row < rows; row++)
        {
            for (int col = 0; col < cols; col++)
            {
                int cell = (row * 97) + col; // chỉ số ô ổn định theo (hàng, cột), không phụ thuộc số cột hiện có
                if (DecorativeNoise(cell * 7) < 0.15)
                {
                    continue; // để trống 1 số ô — tránh lặp lại đều đặn như giấy dán tường
                }

                // Lệch ngẫu nhiên ra cả ngoài ô (chồng sang ô bên) — bớt cảm giác xếp theo lưới.
                double cx = (col * _flowerCell) + (DecorativeNoise((cell * 7) + 1) * _flowerCell * 1.2) - (_flowerCell * 0.1);
                double cy = (row * _flowerCell) + (DecorativeNoise((cell * 7) + 2) * _flowerCell * 1.2) - (_flowerCell * 0.1);
                double rotation = DecorativeNoise((cell * 7) + 3) * 360;
                double scale = 1.4 + (DecorativeNoise((cell * 7) + 4) * 1.1); // 1.4x–2.5x so với bản đầu
                AddFlowerCluster(cx, cy, rotation, scale, cell, petal, center);
            }
        }
    }

    /// <summary>1 chùm = 3 bông 5 cánh khác cỡ + vài chấm nhỏ, xoay quanh tâm chùm.</summary>
    private void AddFlowerCluster(double cx, double cy, double rotationDeg, double scale, int seed, Brush petal, Brush center)
    {
        (double dx, double dy, double r)[] flowers = [(0, 0, 16), (30, -18, 11), (-20, 26, 9)];
        (double dx, double dy, double r)[] dots = [(34, 14, 3.5), (-30, -12, 3), (8, 36, 2.5)];
        double rad = rotationDeg * Math.PI / 180;
        (double x, double y) Rotate(double x, double y) => (cx + (((x * Math.Cos(rad)) - (y * Math.Sin(rad))) * scale), cy + (((x * Math.Sin(rad)) + (y * Math.Cos(rad))) * scale));

        foreach ((double dx, double dy, double r) in flowers)
        {
            (double fx, double fy) = Rotate(dx, dy);
            double rs = r * scale;
            double petalR = rs * 0.55;
            for (int k = 0; k < 5; k++)
            {
                double a = rad + (k * 2 * Math.PI / 5) + (DecorativeNoise(seed) * 0.6);
                AddCircle(fx + (Math.Cos(a) * rs * 0.55), fy + (Math.Sin(a) * rs * 0.55), petalR, petal);
            }

            AddCircle(fx, fy, rs * 0.28, center);
        }

        foreach ((double dx, double dy, double r) in dots)
        {
            (double px, double py) = Rotate(dx, dy);
            AddCircle(px, py, r * scale, petal);
        }
    }

    private void AddCircle(double x, double y, double radius, Brush fill)
    {
        var e = new Ellipse { Width = radius * 2, Height = radius * 2, Fill = fill };
        Canvas.SetLeft(e, x - radius);
        Canvas.SetTop(e, y - radius);
        FlowerCanvas.Children.Add(e);
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
            "About" => typeof(AboutPage),
            _ => typeof(DashboardPage),
        };
        if (ContentFrame.CurrentSourcePageType != pageType)
        {
            ContentFrame.Navigate(pageType);
        }
    }
}
