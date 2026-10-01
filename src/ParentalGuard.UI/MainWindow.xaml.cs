using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

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
    }

    public Frame RootFrameControl => RootFrame;
}
