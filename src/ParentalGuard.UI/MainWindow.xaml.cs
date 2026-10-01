using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.UI;

namespace ParentalGuard.UI;

/// <summary>Cửa sổ duy nhất của ứng dụng (mục 2.3 — single-instance) — chứa <see cref="RootFrame"/> cho điều hướng cấp cao (mục 4: lỗi kết nối / S1 / Main Shell).</summary>
public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Title = "ParentalGuard";
        // Icon title bar/taskbar — app unpackaged không tự lấy icon nhúng trong .exe cho cửa sổ WinUI.
        string iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "ParentalGuard.ico");
        if (File.Exists(iconPath))
        {
            AppWindow.SetIcon(iconPath);
        }

        // FE-006 (2026-10-01): thanh tiêu đề cùng tông màu nền app (thay thanh xám sáng mặc định), theo Dark/Light.
        RootFrame.ActualThemeChanged += (_, _) => ApplyTitleBarColors();
        RootFrame.Loaded += (_, _) => ApplyTitleBarColors();
    }

    private void ApplyTitleBarColors()
    {
        if (!AppWindowTitleBar.IsCustomizationSupported())
        {
            return;
        }

        bool light = RootFrame.ActualTheme == ElementTheme.Light;
        Color background = light ? ColorHelper.FromArgb(255, 0xF1, 0xF1, 0xF5) : ColorHelper.FromArgb(255, 0x30, 0x30, 0x37);
        Color foreground = light ? Colors.Black : Colors.White;
        Color inactiveForeground = light ? ColorHelper.FromArgb(255, 0x80, 0x80, 0x88) : ColorHelper.FromArgb(255, 0x9A, 0x9A, 0xA2);
        Color hover = light ? ColorHelper.FromArgb(255, 0xE2, 0xE2, 0xE9) : ColorHelper.FromArgb(255, 0x3C, 0x3C, 0x45);

        AppWindowTitleBar titleBar = AppWindow.TitleBar;
        titleBar.BackgroundColor = background;
        titleBar.InactiveBackgroundColor = background;
        titleBar.ForegroundColor = foreground;
        titleBar.InactiveForegroundColor = inactiveForeground;
        titleBar.ButtonBackgroundColor = background;
        titleBar.ButtonInactiveBackgroundColor = background;
        titleBar.ButtonForegroundColor = foreground;
        titleBar.ButtonInactiveForegroundColor = inactiveForeground;
        titleBar.ButtonHoverBackgroundColor = hover;
        titleBar.ButtonHoverForegroundColor = foreground;
        titleBar.ButtonPressedBackgroundColor = hover;
        titleBar.ButtonPressedForegroundColor = foreground;
    }

    public Frame RootFrameControl => RootFrame;
}
