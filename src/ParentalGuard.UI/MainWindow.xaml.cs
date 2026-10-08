using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.UI;

namespace ParentalGuard.UI;

/// <summary>Cửa sổ duy nhất của ứng dụng (mục 2.3 — single-instance) — chứa <see cref="RootFrame"/> cho điều hướng cấp cao (mục 4: lỗi kết nối / S1 / Main Shell).</summary>
public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Title = "ParentalGuard";
        ApplyAppIcon(AppWindow);

        // FE-006 (2026-10-01): thanh tiêu đề cùng tông màu nền app (thay thanh xám sáng mặc định), theo Dark/Light.
        RootFrame.ActualThemeChanged += (_, _) => ApplyTitleBarColors();
        RootFrame.Loaded += (_, _) => ApplyTitleBarColors();
    }

    /// <summary>
    /// `PWD-024` (Architecture/10 mục 6.8): mọi lần bấm chuột/cuộn/gõ phím trong cửa sổ (kể cả sự kiện control con
    /// đã xử lý) tính là "có thao tác" — gia hạn phiên đăng nhập phụ huynh.
    /// </summary>
    public void HookUserActivity(Action onActivity)
    {
        RootFrame.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler((_, _) => onActivity()), handledEventsToo: true);
        RootFrame.AddHandler(UIElement.PointerWheelChangedEvent, new PointerEventHandler((_, _) => onActivity()), handledEventsToo: true);
        RootFrame.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler((_, _) => onActivity()), handledEventsToo: true);
    }

    private void ApplyTitleBarColors() => ApplyTitleBarColors(AppWindow, RootFrame.ActualTheme);

    /// <summary>Icon ParentalGuard cho thanh tiêu đề/taskbar — dùng chung cho mọi cửa sổ của app (vd cửa sổ trò chơi nhảy rào).</summary>
    internal static void ApplyAppIcon(AppWindow appWindow)
    {
        // App unpackaged không tự lấy icon nhúng trong .exe cho cửa sổ WinUI.
        string iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "ParentalGuard.ico");
        if (File.Exists(iconPath))
        {
            appWindow.SetIcon(iconPath);
        }
    }

    /// <summary>`FE-006`/`FE-006a`: màu thanh tiêu đề theo Dark/Light — dùng chung cho mọi cửa sổ của app.</summary>
    internal static void ApplyTitleBarColors(AppWindow appWindow, ElementTheme theme)
    {
        if (!AppWindowTitleBar.IsCustomizationSupported())
        {
            return;
        }

        bool light = theme == ElementTheme.Light;
        // FE-006a (2026-10-01): màu riêng — tối hơn cả thanh menu (#303037) lẫn vùng nội dung (#26262B), cùng tông xám lạnh.
        Color background = light ? ColorHelper.FromArgb(255, 0xE4, 0xE4, 0xEB) : ColorHelper.FromArgb(255, 0x1B, 0x1B, 0x20);
        Color foreground = light ? Colors.Black : Colors.White;
        Color inactiveForeground = light ? ColorHelper.FromArgb(255, 0x80, 0x80, 0x88) : ColorHelper.FromArgb(255, 0x9A, 0x9A, 0xA2);
        Color hover = light ? ColorHelper.FromArgb(255, 0xD6, 0xD6, 0xDF) : ColorHelper.FromArgb(255, 0x2E, 0x2E, 0x36);

        AppWindowTitleBar titleBar = appWindow.TitleBar;
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
