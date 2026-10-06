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
        DonateItem.Content = LocalizationService.Get("NavDonate");
        VersionFooterText.Text = LocalizationService.GetFormatted("PaneVersionFormat", AppVersionInfo.DisplayVersion);

        Loaded += (_, _) =>
        {
            Nav.SelectedItem = DashboardItem;
            ContentFrame.Navigate(typeof(DashboardPage));
            UpdateBackdropLayout();
        };

        // FE-005: 2 lớp nền phía sau NavigationView phải bám đúng bề rộng thanh menu (mở 240 / thu gọn 48).
        Nav.PaneOpening += (_, _) =>
        {
            UpdateBackdropLayout(paneOpen: true);
            VersionFooterText.Visibility = Visibility.Visible;
        };
        Nav.PaneClosing += (_, _) =>
        {
            UpdateBackdropLayout(paneOpen: false);
            VersionFooterText.Visibility = Visibility.Collapsed;
        };
        Nav.DisplayModeChanged += (_, _) => UpdateBackdropLayout();
        SizeChanged += (_, _) => UpdateBackdropLayout();
        ActualThemeChanged += (_, _) =>
        {
            DrawPaneCircles();
            DrawScene();
        };
        ContentBackdrop.SizeChanged += (_, _) => DrawScene();

        // S3 huỷ gate S5 tự điều hướng lại S2 (mục 6.3) bằng Frame.Navigate trực tiếp (không qua
        // OnSelectionChanged) — đồng bộ lại NavigationViewItem đang chọn cho khớp trang thật sự hiển thị.
        ContentFrame.Navigated += (_, e) => Nav.SelectedItem = e.SourcePageType == typeof(AuditLogPage)
            ? AuditLogItem
            : e.SourcePageType == typeof(SettingsPage) ? SettingsItem
            : e.SourcePageType == typeof(AboutPage) ? AboutItem
            : e.SourcePageType == typeof(DonatePage) ? DonateItem : DashboardItem;
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

    /// <summary>
    /// FE-005d (2026-10-01): cảnh nền vẽ vector, tông màu sát nền — hồ nước ở giữa/trái, cây hoa đào xum xuê cành
    /// trĩu nhẹ bên phải, xa xa là núi và rừng. Mọi kích thước tính bằng DIP CỐ ĐỊNH, neo theo đáy/mép phải vùng nội
    /// dung — đổi cỡ cửa sổ chỉ làm lộ thêm/bớt cảnh, không co giãn hình (FE-005b). Vị trí ngẫu nhiên tất định.
    /// </summary>
    private void DrawScene()
    {
        SceneCanvas.Children.Clear();
        double w = ContentBackdrop.ActualWidth;
        double h = ContentBackdrop.ActualHeight;
        if (w <= 0 || h <= 0)
        {
            return;
        }

        bool light = ActualTheme == ElementTheme.Light;
        Brush B(byte r, byte g, byte b, byte lr, byte lg, byte lb) =>
            new SolidColorBrush(light ? ColorHelper.FromArgb(255, lr, lg, lb) : ColorHelper.FromArgb(255, r, g, b));

        // 2026-10-01 (chủ dự án: chữ khó đọc): nền vùng nội dung đậm hơn (#1F1F23) và cảnh chỉ lệch nền 2–5 mức
        // màu — vẫn nhận ra hình nhưng không còn cạnh tranh với chữ.
        Brush farMountain = B(0x22, 0x22, 0x26, 0xF4, 0xF4, 0xF7);
        Brush nearMountain = B(0x1D, 0x1D, 0x21, 0xEF, 0xEF, 0xF3);
        Brush forest = B(0x1B, 0x1B, 0x1F, 0xEB, 0xEC, 0xF0);
        Brush lake = B(0x22, 0x23, 0x28, 0xF5, 0xF6, 0xF9);
        Brush ripple = B(0x26, 0x27, 0x2D, 0xF0, 0xF2, 0xF6);
        Brush trunk = B(0x1A, 0x1A, 0x1D, 0xE6, 0xE6, 0xEA);
        Brush blossom = B(0x25, 0x21, 0x25, 0xF7, 0xF0, 0xF2);
        Brush blossomDeep = B(0x28, 0x23, 0x27, 0xF4, 0xEA, 0xED);

        double horizon = h - 300; // mép trên mặt hồ: cách đáy cố định 300 DIP

        // 1. Núi xa (2 lớp) — đường gợn tất định theo toạ độ x tuyệt đối (mở rộng cửa sổ = lộ thêm núi, không kéo giãn).
        AddRidge(horizon, w, step: 90, baseRise: 110, amplitude: 150, seed: 100, farMountain);
        AddRidge(horizon, w, step: 60, baseRise: 40, amplitude: 80, seed: 300, nearMountain);

        // 2. Rừng thông dọc chân núi.
        for (double x = 0, i = 0; x < w; x += 16, i++)
        {
            double treeH = 18 + (DecorativeNoise(500 + (int)i) * 30);
            double treeW = 10 + (DecorativeNoise(700 + (int)i) * 8);
            var pine = new Polygon { Fill = forest };
            pine.Points.Add(new Windows.Foundation.Point(x - (treeW / 2), horizon + 1));
            pine.Points.Add(new Windows.Foundation.Point(x, horizon - treeH));
            pine.Points.Add(new Windows.Foundation.Point(x + (treeW / 2), horizon + 1));
            SceneCanvas.Children.Add(pine);
        }

        // 3. Hồ nước: từ chân núi xuống đáy, bờ phải cong vào gốc cây đào.
        var lakeFigure = new PathFigure { StartPoint = new Windows.Foundation.Point(0, horizon), IsClosed = true };
        lakeFigure.Segments.Add(new LineSegment { Point = new Windows.Foundation.Point(Math.Max(0, w - 330), horizon) });
        lakeFigure.Segments.Add(new BezierSegment
        {
            Point1 = new Windows.Foundation.Point(w - 200, horizon + 40),
            Point2 = new Windows.Foundation.Point(w - 300, h - 60),
            Point3 = new Windows.Foundation.Point(w - 190, h),
        });
        lakeFigure.Segments.Add(new LineSegment { Point = new Windows.Foundation.Point(0, h) });
        SceneCanvas.Children.Add(new Microsoft.UI.Xaml.Shapes.Path { Fill = lake, Data = new PathGeometry { Figures = { lakeFigure } } });

        for (int i = 0; i < 40; i++)
        {
            double y = horizon + 12 + (DecorativeNoise(900 + i) * (h - horizon - 24));
            double len = 30 + (DecorativeNoise(1000 + i) * 150);
            double x = DecorativeNoise(1100 + i) * Math.Max(1, w - 360 - len);
            var line = new Rectangle { Width = len, Height = 2, RadiusX = 1, RadiusY = 1, Fill = ripple };
            Canvas.SetLeft(line, x);
            Canvas.SetTop(line, y);
            SceneCanvas.Children.Add(line);
        }

        // 4. Cây hoa đào neo góc dưới-phải (cỡ cố định ~400×450 DIP — hệ số k).
        const double treeScale = 0.72;
        double bx = w;
        double by = h;
        (double x, double y) P(double dx, double dy) => (bx + (dx * treeScale), by + (dy * treeScale));

        var trunkTop = P(-175, -235);
        AddBranch(P(-110, 0), P(-85, -120), P(-205, -165), trunkTop, 30 * treeScale, trunk);

        (double x, double y)[][] branches =
        [
            [trunkTop, P(-270, -335), P(-430, -340), P(-540, -255)],  // nhánh dài sang trái, rủ xuống
            [P(-400, -330), P(-450, -300), P(-480, -250), P(-500, -185)], // cành con rủ trên mặt hồ
            [trunkTop, P(-210, -340), P(-300, -440), P(-390, -480)],  // chếch lên trái
            [trunkTop, P(-160, -340), P(-140, -450), P(-180, -560)],  // vươn lên
            [P(-165, -430), P(-210, -470), P(-260, -500), P(-300, -490)], // cành con của nhánh vươn lên
            [trunkTop, P(-120, -300), P(-60, -350), P(-15, -400)],    // sang phải
        ];
        double[] thickness = [14, 7, 12, 12, 6, 10];
        for (int b = 0; b < branches.Length; b++)
        {
            (double x, double y)[] br = branches[b];
            AddBranch(br[0], br[1], br[2], br[3], thickness[b] * treeScale, trunk);
        }

        // Tán hoa: các cụm tròn dọc nửa ngoài mỗi nhánh, cụm cuối nhánh dày hơn — xum xuê.
        int seed = 2000;
        for (int b = 0; b < branches.Length; b++)
        {
            (double x, double y)[] br = branches[b];
            for (double t = 0.35; t <= 1.0001; t += 0.09)
            {
                (double cx, double cy) = BezierAt(br, t);
                int puffs = t > 0.8 ? 7 : 4;
                for (int k = 0; k < puffs; k++)
                {
                    double ox = (DecorativeNoise(seed++) - 0.5) * 70 * treeScale;
                    double oy = (DecorativeNoise(seed++) - 0.5) * 50 * treeScale;
                    double r = (9 + (DecorativeNoise(seed++) * 14)) * treeScale;
                    AddDisc(cx + ox, cy + oy + (6 * treeScale), r, DecorativeNoise(seed++) < 0.3 ? blossomDeep : blossom);
                }
            }
        }

        // Vài cánh hoa rơi trên mặt hồ.
        for (int i = 0; i < 14; i++)
        {
            double x = bx - (120 * treeScale) - (DecorativeNoise(3000 + i) * 520 * treeScale);
            double y = horizon + 20 + (DecorativeNoise(3100 + i) * (h - horizon - 40));
            AddDisc(x, y, 3 + (DecorativeNoise(3200 + i) * 3), blossom);
        }
    }

    /// <summary>Dải núi: đa giác từ chân trời lên, đỉnh gợn theo nhiễu tất định của toạ độ x tuyệt đối.</summary>
    private void AddRidge(double horizon, double width, double step, double baseRise, double amplitude, int seed, Brush fill)
    {
        var ridge = new Polygon { Fill = fill };
        ridge.Points.Add(new Windows.Foundation.Point(0, horizon + 1));
        for (int i = 0; i * step <= width + step; i++)
        {
            double peak = baseRise + (DecorativeNoise(seed + i) * amplitude);
            ridge.Points.Add(new Windows.Foundation.Point(i * step, horizon - peak));
        }

        ridge.Points.Add(new Windows.Foundation.Point(width + step, horizon + 1));
        SceneCanvas.Children.Add(ridge);
    }

    private void AddBranch((double x, double y) p0, (double x, double y) p1, (double x, double y) p2, (double x, double y) p3, double thickness, Brush stroke)
    {
        var figure = new PathFigure { StartPoint = new Windows.Foundation.Point(p0.x, p0.y) };
        figure.Segments.Add(new BezierSegment
        {
            Point1 = new Windows.Foundation.Point(p1.x, p1.y),
            Point2 = new Windows.Foundation.Point(p2.x, p2.y),
            Point3 = new Windows.Foundation.Point(p3.x, p3.y),
        });
        SceneCanvas.Children.Add(new Microsoft.UI.Xaml.Shapes.Path
        {
            Stroke = stroke,
            StrokeThickness = thickness,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            Data = new PathGeometry { Figures = { figure } },
        });
    }

    private static (double x, double y) BezierAt((double x, double y)[] p, double t)
    {
        double u = 1 - t;
        double x = (u * u * u * p[0].x) + (3 * u * u * t * p[1].x) + (3 * u * t * t * p[2].x) + (t * t * t * p[3].x);
        double y = (u * u * u * p[0].y) + (3 * u * u * t * p[1].y) + (3 * u * t * t * p[2].y) + (t * t * t * p[3].y);
        return (x, y);
    }

    private void AddDisc(double x, double y, double radius, Brush fill)
    {
        var e = new Ellipse { Width = radius * 2, Height = radius * 2, Fill = fill };
        Canvas.SetLeft(e, x - radius);
        Canvas.SetTop(e, y - radius);
        SceneCanvas.Children.Add(e);
    }

    /// <summary>Dãy giả ngẫu nhiên tất định chỉ dùng trang trí (không dùng <see cref="Random"/> — CA5394), giá trị [0, 1).</summary>
    internal static double DecorativeNoise(int i)
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
            "Donate" => typeof(DonatePage),
            _ => typeof(DashboardPage),
        };
        if (ContentFrame.CurrentSourcePageType != pageType)
        {
            ContentFrame.Navigate(pageType);
        }
    }
}
